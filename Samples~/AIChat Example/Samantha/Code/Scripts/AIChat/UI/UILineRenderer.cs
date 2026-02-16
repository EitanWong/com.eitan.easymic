using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Radishmouse
{
    [RequireComponent(typeof(CanvasRenderer))]
    public class UILineRenderer : MaskableGraphic
    {
        [Tooltip("Input points. By default interpreted in RectTransform local space (origin at pivot).")]
        public List<Vector2> points = new List<Vector2>();

        [Min(0.01f)]
        [Tooltip("Average stroke width in pixels (set to 0 for automatic sizing). Actual width adapts to local curvature.")]
        public float thickness = 6f;

        [Header("Smoothing")]
        [Tooltip("Enable Catmull–Rom resampling for smoother curves.")]
        public bool useSpline = true;
        [Range(1, 24)] public int splineSubdivisions = 8;
        [Range(0f, 1f)] public float splineTension = 0.5f;

        [Header("Input Space")]
        [Tooltip("If true, points are already in RectTransform LOCAL space (pivot is origin). If false, points are in rect space with origin at bottom-left and will be converted.")]
        [FormerlySerializedAs("alignToPivot")]
        public bool pointsInPivotSpace = true;

        [Header("Topology")]
        [Tooltip("Connect the first and last point to form a closed loop (seamless smoothing).")]
        public bool closeLoop = false;

        [Header("Stylization")]
        [Tooltip("Multiplier applied to the thickness to determine the thinnest spans.")]
        [Range(0f, 1f)] public float thinThicknessMultiplier = 0.3f;

        [Tooltip("Shifts the transparency gradient; positive values keep thin spans more opaque.")]
        [Range(-1f, 1f)] public float transparencyShift = 0.1f;

        [Tooltip("Normalized roll offset applied to the stylized thickness/opacity profile (useful when geometry rolls).")]
        [Range(0f, 1f)] public float styleRollOffset = 0f;

        // Reused work buffers to minimize GC
        private static readonly List<Vector2> s_Local = new List<Vector2>(256);
        private static readonly List<Vector2> s_Path = new List<Vector2>(256);
        private static readonly List<Vector2> s_Resampled = new List<Vector2>(256);
        private static readonly List<Vector2> s_SegF = new List<Vector2>(256);
        private static readonly List<Vector2> s_SegB = new List<Vector2>(256);
        private static readonly List<Vector2> s_Miter = new List<Vector2>(256);
        private static readonly List<float> s_SegLen = new List<float>(256);
        private static readonly List<float> s_PathWidths = new List<float>(256);
        private static readonly List<float> s_PathWidthScratch = new List<float>(256);
        private static readonly List<float> s_PathHalfWidth = new List<float>(256);

        private const float MIN_PIXEL_WIDTH = 1f;
        private const float WIDTH_CLAMP_MIN = 0.3f;
        internal const float AutoWidthClampMax = 3f;
        private const float WIDTH_CLAMP_MAX = AutoWidthClampMax;
        private const float ADAPTIVE_WEIGHT = 1.15f;
        private const float MITER_LIMIT = 4f;
        private const float MIN_SEGMENT_SPACING = 1.5f;
        private const int MAX_SEGMENT_DIVISIONS = 12;

        public override Texture mainTexture =>
            material && material.mainTexture ? material.mainTexture : s_WhiteTexture;

        protected override void OnEnable()
        {
            base.OnEnable();
            SetVerticesDirty();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            SetVerticesDirty();
        }

        public void SetPoints(IEnumerable<Vector2> newPoints)
        {
            points.Clear();
            if (newPoints != null)
            {
                points.AddRange(newPoints);
            }


            SetVerticesDirty();
        }

        public void SetStyleRollOffset(float normalized)
        {
            float wrapped = Mathf.Repeat(normalized, 1f);
            if (!Mathf.Approximately(wrapped, styleRollOffset))
            {
                styleRollOffset = wrapped;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (points == null || points.Count < 2)
            {
                return;
            }

            // --- FIX: compute pivot only when needed, pass instance values to static helper ---
            Vector2 pivotOff = default;
            if (!pointsInPivotSpace)
            {
                var rt = rectTransform;     // instance member (OK here)
                var rect = rt.rect;
                var pv = rt.pivot;
                pivotOff = new Vector2(rect.width * pv.x, rect.height * pv.y);
            }
            ConvertToLocalPoints(points, s_Local, pointsInPivotSpace, pivotOff);
            // -------------------------------------------------------------------------------

            // Step 2: optional spline resample (loop-aware)
            if (useSpline && splineSubdivisions > 1 && s_Local.Count >= 3)
            {
                ResampleSpline(s_Local, s_Path, closeLoop, splineSubdivisions, splineTension);
            }
            else
            {
                CopyTo(s_Local, s_Path);
            }


            RemoveTinySegments(s_Path);
            if (s_Path.Count < 2)
            {
                return;
            }

            AdaptiveRefinePath(s_Path, s_Resampled, closeLoop, thickness);
            CopyTo(s_Resampled, s_Path);

            // Step 3: triangulate thick polyline with miter joins

            BuildMesh(vh, s_Path, thickness, closeLoop, color);
        }

        // ---------- Helpers ----------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector2 Perp(in Vector2 v) => new Vector2(-v.y, v.x);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureSize<T>(List<T> list, int size)
        {
            if (list.Capacity < size)
            {
                list.Capacity = size;
            }

            while (list.Count < size)
            {
                list.Add(default(T));
            }

            if (list.Count > size)
            {
                list.RemoveRange(size, list.Count - size);
            }

        }

        private static void AdaptiveRefinePath(List<Vector2> src, List<Vector2> dst, bool loop, float baseWidth)
        {
            int n = src.Count;
            EnsureSize(dst, 0);
            if (n < 2)
            {
                dst.AddRange(src);
                return;
            }

            float avgSpacing = EstimateAverageSegmentLength(src, loop);
            float reference = baseWidth > 0f ? baseWidth : avgSpacing;
            if (reference < MIN_SEGMENT_SPACING)
            {
                reference = MIN_SEGMENT_SPACING;
            }

            float target = Mathf.Max(MIN_SEGMENT_SPACING, reference * 0.35f);

            // Start from the first point.
            dst.Add(src[0]);

            int segmentCount = loop ? n : (n - 1);
            for (int i = 0; i < segmentCount; i++)
            {
                int aIndex = i;
                int bIndex = loop ? (i + 1) % n : (i + 1);
                if (!loop && bIndex >= n)
                {
                    break;
                }

                Vector2 a = src[aIndex];
                Vector2 b = src[bIndex];
                bool includeStart = false;
                bool includeEnd = !(loop && i == segmentCount - 1);
                EmitSubdividedSegment(a, b, target, dst, includeStart, includeEnd);
            }

            if (!loop)
            {
                Vector2 last = src[n - 1];
                if ((dst[dst.Count - 1] - last).sqrMagnitude > 1e-10f)
                {
                    dst.Add(last);
                }
            }
        }

        private static float EstimateAverageSegmentLength(List<Vector2> path, bool loop)
        {
            int n = path.Count;
            if (n < 2)
            {
                return 0f;
            }

            float total = 0f;
            for (int i = 1; i < n; i++)
            {
                total += (path[i] - path[i - 1]).magnitude;
            }

            if (loop)
            {
                total += (path[0] - path[n - 1]).magnitude;
            }

            int segCount = loop ? n : (n - 1);
            return segCount > 0 ? (total / segCount) : 0f;
        }

        private static void EmitSubdividedSegment(in Vector2 a, in Vector2 b, float spacing, List<Vector2> dst, bool includeStart, bool includeEnd)
        {
            Vector2 delta = b - a;
            float len = delta.magnitude;
            if (len < 1e-6f)
            {
                if (includeEnd && (dst.Count == 0 || (dst[dst.Count - 1] - b).sqrMagnitude > 1e-10f))
                {
                    dst.Add(b);
                }
                return;
            }

            float stepLen = Mathf.Max(spacing, MIN_SEGMENT_SPACING);
            int steps = Mathf.Clamp(Mathf.CeilToInt(len / stepLen), 1, MAX_SEGMENT_DIVISIONS);

            for (int s = 0; s <= steps; s++)
            {
                if (!includeStart && s == 0)
                {
                    continue;
                }
                if (!includeEnd && s == steps)
                {
                    continue;
                }

                float t = s / (float)steps;
                Vector2 p = Vector2.LerpUnclamped(a, b, t);
                if (dst.Count == 0 || (p - dst[dst.Count - 1]).sqrMagnitude > 1e-10f)
                {
                    dst.Add(p);
                }
            }

        }

        // --- FIX: remove instance usage from static method by threading in values ---
        private static void ConvertToLocalPoints(List<Vector2> src, List<Vector2> dst, bool inPivotSpace, in Vector2 pivotOffset)
        {
            EnsureSize(dst, 0); // clear but keep capacity
            int n = src.Count;
            if (!inPivotSpace)
            {
                dst.Capacity = Mathf.Max(dst.Capacity, n);
                for (int i = 0; i < n; i++)
                {
                    dst.Add(src[i] - pivotOffset);
                }

            }
            else
            {
                dst.AddRange(src);
            }
        }
        // ---------------------------------------------------------------------------

        private static List<float> ComputeHalfWidths(List<Vector2> path, float baseWidth, bool loop)
        {
            int n = path.Count;
            EnsureSize(s_SegLen, n);
            EnsureSize(s_PathWidths, n);
            EnsureSize(s_PathWidthScratch, n);
            EnsureSize(s_PathHalfWidth, n);

            if (n == 0)
            {
                return s_PathHalfWidth;
            }


            float prevLen = 0f;
            float totalLen = 0f;
            for (int i = 0; i < n - 1; i++)
            {
                float len = (path[i + 1] - path[i]).magnitude;
                s_SegLen[i] = len;
                totalLen += len;
                prevLen = len;
            }

            if (loop)
            {
                float len = (path[0] - path[n - 1]).magnitude;
                s_SegLen[n - 1] = len;
                totalLen += len;
            }
            else
            {
                s_SegLen[n - 1] = prevLen;
            }


            int segCount = loop ? n : Mathf.Max(1, n - 1);
            float avgSpacing = Mathf.Max(totalLen / Mathf.Max(1, segCount), 1e-3f);
            float widthBase = baseWidth <= 0f ? Mathf.Max(avgSpacing, MIN_PIXEL_WIDTH) : Mathf.Max(baseWidth, MIN_PIXEL_WIDTH);
            float minWidth = Mathf.Max(widthBase * WIDTH_CLAMP_MIN, MIN_PIXEL_WIDTH);
            float maxWidth = widthBase * WIDTH_CLAMP_MAX;

            for (int i = 0; i < n; i++)
            {
                float lenPrev = loop ? s_SegLen[(i - 1 + n) % n] : (i > 0 ? s_SegLen[i - 1] : s_SegLen[0]);
                float lenNext = loop ? s_SegLen[i] : (i < n - 1 ? s_SegLen[i] : s_SegLen[Mathf.Max(0, n - 2)]);
                float localSpacing = 0.5f * (lenPrev + lenNext);
                float ratio = localSpacing / avgSpacing;
                float adaptive = 1f + (ratio - 1f) * ADAPTIVE_WEIGHT;
                float width = Mathf.Clamp(widthBase * adaptive, minWidth, maxWidth);
                s_PathWidths[i] = width;
            }


            SmoothWidths(s_PathWidths, s_PathWidthScratch, loop, minWidth, maxWidth);

            for (int i = 0; i < n; i++)
            {
                float half = 0.5f * Mathf.Clamp(s_PathWidthScratch[i], minWidth, maxWidth);
                s_PathHalfWidth[i] = Mathf.Max(half, 0.5f * MIN_PIXEL_WIDTH);
            }


            return s_PathHalfWidth;
        }

        private static void SmoothWidths(List<float> src, List<float> dst, bool loop, float minWidth, float maxWidth)
        {
            int n = src.Count;
            if (n == 0)
            {
                return;
            }


            if (n < 3 && !loop)
            {
                for (int i = 0; i < n; i++)
                {
                    dst[i] = Mathf.Clamp(src[i], minWidth, maxWidth);
                }
                return;
            }


            for (int i = 0; i < n; i++)
            {
                float prev = src[(i - 1 + n) % n];
                float cur = src[i];
                float next = src[(i + 1) % n];

                if (!loop)
                {
                    if (i == 0)
                    {
                        prev = cur;
                    }


                    if (i == n - 1)
                    {
                        next = cur;
                    }

                }


                float smoothed = (prev + cur * 2f + next) * 0.25f;
                dst[i] = Mathf.Clamp(smoothed, minWidth, maxWidth);
            }
        }

        private static void ResampleSpline(List<Vector2> src, List<Vector2> dst, bool loop, int subdiv, float tension)
        {
            int n = src.Count;
            EnsureSize(dst, 0); // clear
            if (subdiv < 1)
            {
                subdiv = 1;
            }


            float invSubdiv = 1f / subdiv;

            dst.Capacity = Mathf.Max(dst.Capacity, loop ? n * subdiv : (n - 1) * subdiv + 1);

            if (loop)
            {
                for (int i = 0; i < n; i++)
                {
                    Vector2 p0 = src[(i - 1 + n) % n];
                    Vector2 p1 = src[i];
                    Vector2 p2 = src[(i + 1) % n];
                    Vector2 p3 = src[(i + 2) % n];

                    for (int s = 0; s < subdiv; s++)
                    {
                        float t = s * invSubdiv; // [0,1)
                        dst.Add(CatmullRom(p0, p1, p2, p3, t, tension));
                    }
                }
            }
            else
            {
                dst.Add(src[0]);
                for (int i = 0; i < n - 1; i++)
                {
                    Vector2 p0 = (i == 0) ? src[i] : src[i - 1];
                    Vector2 p1 = src[i];
                    Vector2 p2 = src[i + 1];
                    Vector2 p3 = (i + 2 < n) ? src[i + 2] : src[i + 1];

                    for (int s = 1; s <= subdiv; s++)
                    {
                        float t = s * invSubdiv; // (0,1]
                        dst.Add(CatmullRom(p0, p1, p2, p3, t, tension));
                    }
                }
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector2 CatmullRom(in Vector2 p0, in Vector2 p1, in Vector2 p2, in Vector2 p3, float t, float tension)
        {
            float t2 = t * t, t3 = t2 * t;
            float s = (1f - tension) * 0.5f;
            Vector2 m1 = s * (p2 - p0);
            Vector2 m2 = s * (p3 - p1);

            return (2f * t3 - 3f * t2 + 1f) * p1 +
                   (t3 - 2f * t2 + t) * m1 +
                   (-2f * t3 + 3f * t2) * p2 +
                   (t3 - t2) * m2;
        }

        private static void CopyTo(List<Vector2> src, List<Vector2> dst)
        {
            EnsureSize(dst, 0);
            dst.AddRange(src);
        }

        private static void RemoveTinySegments(List<Vector2> path, float eps = 0.01f)
        {
            int n = path.Count;
            if (n < 2)
            {
                return;
            }


            float eps2 = eps * eps;
            int w = 1;
            for (int i = 1; i < n; i++)
            {
                if ((path[i] - path[w - 1]).sqrMagnitude > eps2)
                {
                    if (w != i)
                    {
                        path[w] = path[i];
                    }


                    w++;
                }
            }
            if (w < n)
            {
                path.RemoveRange(w, n - w);
            }

        }

        private void BuildMesh(VertexHelper vh, List<Vector2> path, float width, bool loop, Color32 col)
        {
            int n = path.Count;
            if (n < 2)
            {
                return;
            }

            var halfWidths = ComputeHalfWidths(path, width, loop);
            StylizeHalfWidths(halfWidths, loop);
            float maxHalf = 0f;
            float minHalf = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float half = halfWidths[i];
                if (half > maxHalf)
                {
                    maxHalf = half;
                }
                if (half < minHalf)
                {
                    minHalf = half;
                }
            }
            if (minHalf == float.MaxValue)
            {
                minHalf = maxHalf;
            }

            float minWidth = Mathf.Max(minHalf * 2f, 1e-3f);
            float maxWidth = Mathf.Max(maxHalf * 2f, minWidth + 1e-3f);
            float widthSpan = maxWidth - minWidth;
            bool hasWidthRange = widthSpan > 1e-4f;
            if (!hasWidthRange)
            {
                widthSpan = 1f;
            }

            EnsureSize(s_SegF, n);
            EnsureSize(s_SegB, n);
            EnsureSize(s_Miter, n);

            // Segment directions
            if (loop)
            {
                for (int i = 0; i < n; i++)
                {
                    Vector2 df = path[(i + 1) % n] - path[i];
                    Vector2 db = path[i] - path[(i - 1 + n) % n];
                    s_SegF[i] = df.sqrMagnitude > 1e-12f ? df.normalized : Vector2.right;
                    s_SegB[i] = db.sqrMagnitude > 1e-12f ? db.normalized : Vector2.right;
                }
            }
            else
            {
                // first
                {
                    Vector2 df = path[1] - path[0];
                    s_SegF[0] = s_SegB[0] = (df.sqrMagnitude > 1e-12f) ? df.normalized : Vector2.right;
                }
                // interior
                for (int i = 1; i < n - 1; i++)
                {
                    Vector2 df = path[i + 1] - path[i];
                    Vector2 db = path[i] - path[i - 1];
                    s_SegF[i] = (df.sqrMagnitude > 1e-12f) ? df.normalized : Vector2.right;
                    s_SegB[i] = (db.sqrMagnitude > 1e-12f) ? db.normalized : Vector2.right;
                }
                // last
                {
                    Vector2 db = path[n - 1] - path[n - 2];
                    s_SegB[n - 1] = s_SegF[n - 1] = (db.sqrMagnitude > 1e-12f) ? db.normalized : Vector2.right;
                }
            }

            // Miter normals
            for (int i = 0; i < n; i++)
            {
                float half = halfWidths[i];
                bool endpoint = !loop && (i == 0 || i == n - 1);

                if (endpoint)
                {
                    Vector2 d = (i == 0) ? s_SegF[i] : s_SegB[i];
                    Vector2 nSeg = Perp(d);
                    float denom = Vector2.Dot(nSeg, nSeg);
                    float scale = (denom > 1e-6f) ? (half / Mathf.Sqrt(denom)) : half;
                    s_Miter[i] = nSeg * scale; // butt cap
                }
                else
                {
                    Vector2 n1 = Perp(s_SegB[i]);
                    Vector2 n2 = Perp(s_SegF[i]);
                    Vector2 m = n1 + n2;

                    if (m.sqrMagnitude < 1e-12f)
                    {
                        m = n2;
                    }
                    else
                    {
                        m.Normalize();
                    }

                    float denom = Vector2.Dot(m, n2);
                    float scale = (Mathf.Abs(denom) > 1e-6f) ? (half / denom) : half;
                    Vector2 mit = m * scale;
                    float maxLen = half * MITER_LIMIT;
                    float mitMag = mit.magnitude;
                    if (mitMag > maxLen)
                    {
                        mit = mit / mitMag * maxLen;
                    }
                    s_Miter[i] = mit;
                }
            }

            // Emit vertices
            var v = UIVertex.simpleVert;
            float baseAlpha = col.a / 255f;
            float clampedShift = Mathf.Clamp(transparencyShift, -1f, 1f);

            // Optional: basic UV across width (0/1)
            Vector2 uv0 = Vector2.zero, uv1 = Vector2.right;

            for (int i = 0; i < n; i++)
            {
                Vector2 p = path[i];
                Vector2 off = s_Miter[i];
                float normalized = hasWidthRange
                    ? Mathf.Clamp01((halfWidths[i] * 2f - minWidth) / widthSpan)
                    : 1f;
                float emphasised = Mathf.SmoothStep(0f, 1f, normalized);
                float alphaFrac = Mathf.Clamp01(emphasised + clampedShift);
                float finalAlpha = Mathf.Clamp01(baseAlpha * alphaFrac);
                byte alpha = (byte)Mathf.RoundToInt(finalAlpha * 255f);
                var vertexColor = col;
                vertexColor.a = alpha;
                v.color = vertexColor;

                v.position = p - off; v.uv0 = uv0; vh.AddVert(v);
                v.position = p + off; v.uv0 = uv1; vh.AddVert(v);
            }

            // Triangles
            if (loop)
            {
                for (int i = 0; i < n; i++)
                {
                    int iNext = (i + 1) % n;
                    int a = i * 2, b = a + 1;
                    int c = iNext * 2, d = c + 1;
                    vh.AddTriangle(a, c, b);
                    vh.AddTriangle(b, c, d);
                }
            }
            else
            {
                for (int i = 0; i < n - 1; i++)
                {
                    int a = i * 2, b = a + 1;
                    int c = (i + 1) * 2, d = c + 1;
                    vh.AddTriangle(a, c, b);
                    vh.AddTriangle(b, c, d);
                }
            }
        }

        private void StylizeHalfWidths(List<float> halfWidths, bool loop)
        {
            int n = halfWidths.Count;
            if (n == 0)
            {
                return;
            }

            float clampedThinMultiplier = Mathf.Clamp01(thinThicknessMultiplier);
            float maxHalfTarget = Mathf.Max(thickness * 0.5f, MIN_PIXEL_WIDTH * 0.5f);
            float minHalfTarget = Mathf.Max(maxHalfTarget * clampedThinMultiplier, MIN_PIXEL_WIDTH * 0.5f);
            if (maxHalfTarget <= minHalfTarget)
            {
                maxHalfTarget = minHalfTarget + 1e-4f;
            }

            float min = float.MaxValue;
            float max = 0f;
            for (int i = 0; i < n; i++)
            {
                float half = halfWidths[i];
                if (half < min)
                {
                    min = half;
                }
                if (half > max)
                {
                    max = half;
                }
            }

            if (max - min < 1e-4f)
            {
                for (int i = 0; i < n; i++)
                {
                    halfWidths[i] = maxHalfTarget;
                }
                return;
            }

            float range = max - min;

            EnsureSize(s_PathWidthScratch, n);
            for (int i = 0; i < n; i++)
            {
                float norm = Mathf.Clamp01((halfWidths[i] - min) / range);
                // SmoothStep accentuates contrast without introducing new parameters.
                s_PathWidthScratch[i] = Mathf.SmoothStep(0f, 1f, norm);
            }

            if (loop)
            {
                float offset = Mathf.Repeat(styleRollOffset, 1f);
                if (offset > 1e-4f)
                {
                    float scaled = offset * n;
                    int whole = n > 0 ? Mathf.FloorToInt(scaled) % n : 0;
                    float frac = scaled - whole;
                    EnsureSize(s_PathWidths, n);
                    for (int i = 0; i < n; i++)
                    {
                        int idxA = (i + whole) % n;
                        int idxB = (idxA + 1) % n;
                        float vA = s_PathWidthScratch[idxA];
                        float vB = s_PathWidthScratch[idxB];
                        s_PathWidths[i] = Mathf.Lerp(vA, vB, frac);
                    }
                    for (int i = 0; i < n; i++)
                    {
                        s_PathWidthScratch[i] = s_PathWidths[i];
                    }
                }
            }

            for (int i = 0; i < n; i++)
            {
                halfWidths[i] = Mathf.Lerp(minHalfTarget, maxHalfTarget, s_PathWidthScratch[i]);
            }
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            thickness = Mathf.Max(0f, thickness);
            splineSubdivisions = Mathf.Clamp(splineSubdivisions, 1, 24);
            thinThicknessMultiplier = Mathf.Clamp01(thinThicknessMultiplier);
            transparencyShift = Mathf.Clamp(transparencyShift, -1f, 1f);
            styleRollOffset = Mathf.Repeat(styleRollOffset, 1f);
            SetVerticesDirty();
        }
#endif
    }
}
