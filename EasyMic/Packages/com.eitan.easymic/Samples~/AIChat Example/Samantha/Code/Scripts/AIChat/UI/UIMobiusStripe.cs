using System.Collections.Generic;
using UnityEngine;

namespace Radishmouse
{
    /// <summary>
    /// Single-curve Möbius / Lissajous-style stripe driven by UILineRenderer smoothing.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("UI/EasyMic/AI Chat/Mobius Stripe")]
    [RequireComponent(typeof(RectTransform))]
    public sealed class UIMobiusStripe : MonoBehaviour
    {
        public enum Orientation { Horizontal, Vertical }

        [Header("Shape")]
        [Tooltip("Number of lobes. 1=circle/ellipse; 2=figure-eight; 3+=multi-lobe chain.")]
        [Range(1, 24)] public int loops = 3;

        [Tooltip("Layout of lobes.")]
        public Orientation orientation = Orientation.Horizontal;

        [Header("Sampling")]
        [Tooltip("Nominal control points per lobe. UILineRenderer adds smoothing on top.")]
        [Range(8, 2048)] public int samplesPerLoop = 128;

        [Tooltip("Phase offset (radians) for the fast axis (the loops component).")]
        public float phase = 0f;

        [Tooltip("Offset along the slow axis (radians). Adjust to roll the stripe around its path.")]
        public float pathOffsetRadians = 0f;

        [Header("Perspective View")]
        [Tooltip("Toggle 3D rotation + perspective projection. Off = flat 2D path.")]
        public bool enablePerspective = true;

        [Tooltip("Euler rotation (degrees) applied before perspective projection.")]
        public Vector3 perspectiveEuler = new Vector3(0f, 0f, 0f);

        [Tooltip("Multiplier for the camera distance relative to the stripe radius. Lower values increase perspective depth."), Range(1f, 8f)]
        public float perspectiveDistanceFactor = 1.4f;

        [Header("Auto-size from RectTransform")]
        [Tooltip("Scale RectTransform size before halving to radii.")]
        public Vector2 sizeScale = Vector2.one;

        [Tooltip("Padding inside the rect (pixels). Subtracted before halving.")]
        public Vector2 sizePadding = Vector2.zero;

        [Tooltip("Shrink radii by half the line thickness so the stroke stays inside the rect.")]
        public bool shrinkToAvoidClipping = true;

        private RectTransform _rt;
        private UILineRenderer _line;
        private readonly List<Vector2> _points = new List<Vector2>(512);
        private Snapshot _last;


        public UILineRenderer LineRenderer => _line;
        private struct Snapshot
        {
            public int loops, samplesPerLoop;
            public Orientation orientation;
            public float phase;
            public float pathOffset;
            public Vector3 perspectiveEuler;
            public float perspectiveFactor;
            public bool perspectiveEnabled;
            public Vector2 rectSize, sizeScale, sizePadding;
            public bool shrink;
            public float lineThickness;
            public bool useSpline;
            public int splineSubdiv;

            public bool Matches(in Snapshot other)
            {
                return loops == other.loops &&
                       samplesPerLoop == other.samplesPerLoop &&
                       orientation == other.orientation &&
                       Mathf.Approximately(phase, other.phase) &&
                       Mathf.Approximately(pathOffset, other.pathOffset) &&
                       ApproximatelyVector3(perspectiveEuler, other.perspectiveEuler) &&
                       Mathf.Approximately(perspectiveFactor, other.perspectiveFactor) &&
                       perspectiveEnabled == other.perspectiveEnabled &&
                       rectSize == other.rectSize &&
                       sizeScale == other.sizeScale &&
                       sizePadding == other.sizePadding &&
                       shrink == other.shrink &&
                       Mathf.Approximately(lineThickness, other.lineThickness) &&
                       useSpline == other.useSpline &&
                       splineSubdiv == other.splineSubdiv;
            }

            private static bool ApproximatelyVector3(Vector3 a, Vector3 b)
            {
                return (a - b).sqrMagnitude <= 0.0001f;
            }
        }

        private void OnEnable()
        {
            CacheRefs();
            EnsureLineComponent();
            ApplyLineFlags();
            Rebuild(true);
        }

        private void Reset()
        {
            CacheRefs();
            EnsureLineComponent();
            ApplyLineFlags();
            Rebuild(true);
        }

        private void CacheRefs()
        {
            if (!_rt)
            {
                _rt = (RectTransform)transform;
            }
        }

        private void EnsureLineComponent()
        {
            if (!_line)
            {
                _line = GetComponent<UILineRenderer>();
            }

            if (!_line)
            {
                _line = gameObject.AddComponent<UILineRenderer>();
            }
        }

        private void ApplyLineFlags()
        {
            if (!_line)
            {
                return;
            }

            _line.pointsInPivotSpace = true;
            _line.closeLoop = true;
        }

        private void OnRectTransformDimensionsChange()
        {
            Rebuild();
        }

#if UNITY_EDITOR
        private void Update()
        {
            if (!Application.isPlaying)
            {
                Rebuild();
            }
        }

        private void OnValidate()
        {
            loops = Mathf.Clamp(loops, 1, 128);
            samplesPerLoop = Mathf.Clamp(samplesPerLoop, 8, 2048);
            perspectiveDistanceFactor = Mathf.Clamp(perspectiveDistanceFactor, 1f, 8f);
            CacheRefs();
            EnsureLineComponent();
            ApplyLineFlags();
            Rebuild(true);
        }
#endif

        public void RebuildNow() => Rebuild(true);

        private void Rebuild(bool force = false)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            CacheRefs();
            EnsureLineComponent();
            if (!_rt || !_line)
            {
                return;
            }

            var rect = _rt.rect;
            Vector3 euler = enablePerspective ? perspectiveEuler : Vector3.zero;
            float perspectiveFactor = enablePerspective ? Mathf.Clamp(perspectiveDistanceFactor, 1f, 8f) : 1f;

            var snap = new Snapshot
            {
                loops = Mathf.Clamp(loops, 1, 128),
                samplesPerLoop = Mathf.Max(4, samplesPerLoop),
                orientation = orientation,
                phase = phase,
                pathOffset = pathOffsetRadians,
                perspectiveEuler = euler,
                perspectiveFactor = perspectiveFactor,
                perspectiveEnabled = enablePerspective,
                rectSize = rect.size,
                sizeScale = sizeScale,
                sizePadding = sizePadding,
                shrink = shrinkToAvoidClipping,
                lineThickness = _line.thickness,
                useSpline = _line.useSpline,
                splineSubdiv = Mathf.Max(1, _line.splineSubdivisions)
            };

            if (!force && _last.Matches(snap))
            {
                return;
            }

            _last = snap;

            ComputeRadii(snap, out float rx, out float ry);
            int controlsPerLoop = ResolveControlPointsPerLoop(snap);
            Quaternion perspectiveRotation = enablePerspective ? Quaternion.Euler(euler) : Quaternion.identity;
            GenerateControlPoints(_points, snap.loops, controlsPerLoop, snap.phase, snap.pathOffset, rx, ry, snap.orientation, perspectiveRotation, perspectiveFactor, snap.perspectiveEnabled);

            _line.pointsInPivotSpace = true;
            _line.closeLoop = true;
            _line.SetPoints(_points);
            _line.SetStyleRollOffset(Mathf.Repeat(snap.pathOffset / (Mathf.PI * 2f), 1f));
        }

        private static int ResolveControlPointsPerLoop(in Snapshot s)
        {
            int perLoop = Mathf.Max(4, s.samplesPerLoop);
            if (s.useSpline && s.splineSubdiv > 1)
            {
                perLoop = Mathf.Max(4, Mathf.CeilToInt(perLoop / (float)s.splineSubdiv));
            }
            return perLoop;
        }

        private static void ComputeRadii(in Snapshot s, out float rx, out float ry)
        {
            float width = Mathf.Max(1f, s.rectSize.x * s.sizeScale.x - Mathf.Max(0f, s.sizePadding.x));
            float height = Mathf.Max(1f, s.rectSize.y * s.sizeScale.y - Mathf.Max(0f, s.sizePadding.y));
            rx = Mathf.Max(1f, 0.5f * width);
            ry = Mathf.Max(1f, 0.5f * height);

            if (s.shrink)
            {
                float shrink = Mathf.Max(0f, s.lineThickness * 0.5f);
                rx = Mathf.Max(1f, rx - shrink);
                ry = Mathf.Max(1f, ry - shrink);
            }
        }

        private static void GenerateControlPoints(List<Vector2> buffer, int loopCount, int perLoop, float phase, float pathOffset, float rx, float ry, Orientation orientation, Quaternion planeRotation, float perspectiveFactor, bool perspectiveEnabled)
        {
            buffer.Clear();
            loopCount = Mathf.Max(1, loopCount);
            perLoop = Mathf.Max(4, perLoop);
            int total = loopCount * perLoop;

            if (total < 3)
            {
                return;
            }

            float extremeScale = (loopCount == 1) ? 1f : 2f;
            float rxEff, ryEff;
            float fastPhase = phase;

            if (loopCount == 1)
            {
                float radius = Mathf.Max(1f, Mathf.Min(rx, ry));
                rxEff = radius;
                ryEff = radius;
                fastPhase = 0f;
            }
            else if (orientation == Orientation.Horizontal)
            {
                float chainRadius = Mathf.Max(1f, Mathf.Min(rx / (extremeScale * loopCount), ry));
                rxEff = loopCount * chainRadius;
                ryEff = chainRadius;
            }
            else
            {
                float chainRadius = Mathf.Max(1f, Mathf.Min(rx, ry / (extremeScale * loopCount)));
                rxEff = chainRadius;
                ryEff = loopCount * chainRadius;
            }

            const float depthStrength = 0.6f;
            float startT = Mathf.PI * 0.5f;
            float step = Mathf.PI * 2f / total;
            float majorRadius = Mathf.Max(rxEff, ryEff);
            float minorRadius = Mathf.Max(1f, Mathf.Min(rxEff, ryEff));
            float perspectiveDistance = Mathf.Max(1f, majorRadius * perspectiveFactor + depthStrength * minorRadius);

            for (int i = 0; i < total; i++)
            {
                float T = startT + step * i;
                float slowArg = T + pathOffset;
                float scale = (loopCount == 1)
                    ? 1f
                    : 1f + 0.5f * (extremeScale - 1f) * (1f + Mathf.Cos(2f * slowArg));
                float fastArg = loopCount * slowArg + fastPhase;

                float x, y, z;
                if (orientation == Orientation.Horizontal)
                {
                    x = rxEff * scale * Mathf.Cos(slowArg);
                    y = ryEff * Mathf.Sin(fastArg);
                    float wrapAmplitude = depthStrength * minorRadius * scale * 1.5f;
                    z = wrapAmplitude * Mathf.Cos(fastArg) * Mathf.Sin(slowArg);
                }
                else
                {
                    x = rxEff * Mathf.Sin(fastArg);
                    y = ryEff * scale * Mathf.Cos(slowArg);
                    float wrapAmplitude = depthStrength * minorRadius * scale * 1.5f;
                    z = wrapAmplitude * Mathf.Cos(fastArg) * Mathf.Sin(slowArg);
                }

                if (perspectiveEnabled)
                {
                    Vector3 rotated = planeRotation * new Vector3(x, y, z);
                    float denom = perspectiveDistance - rotated.z;
                    if (denom < 1e-3f)
                    {
                        denom = 1e-3f;
                    }
                    float perspectiveScale = perspectiveDistance / denom;
                    buffer.Add(new Vector2(rotated.x * perspectiveScale, rotated.y * perspectiveScale));
                }
                else
                {
                    // Flat 2D output: no 3D rotation, no perspective foreshortening.
                    buffer.Add(new Vector2(x, y));
                }
            }
        }
    }
}
