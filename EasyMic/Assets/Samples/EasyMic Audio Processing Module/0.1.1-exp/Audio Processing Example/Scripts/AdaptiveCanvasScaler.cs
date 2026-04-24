// =============================================================================
//  AdaptiveCanvasScaler.cs
//
//  Zero-config adaptive scaler for screen-space UI.
//  - Does not create wrapper objects.
//  - Does not modify child UI scale or size.
//  - Uses CanvasScaler for scaling.
//  - Applies safe area only to an existing content root.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

[AddComponentMenu("UI/Adaptive Canvas Scaler")]
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(RectTransform))]
[HideInInspector]
public sealed class AdaptiveCanvasScaler : CanvasScaler
{
    private const float kDefaultReferencePixelsPerUnit = 100f;
    private const float kDefaultFallbackScreenDpi = 96f;
    private const float kDefaultDefaultSpriteDpi = 96f;

    private const float kAspectDeadZone = 0.02f;
    private const float kAspectCurveRange = 0.35f;

    private static readonly Vector2 kDefaultPortraitReference = new Vector2(1080f, 1920f);
    private static readonly Vector2 kDefaultLandscapeReference = new Vector2(1920f, 1080f);

    [SerializeField, HideInInspector] private Vector2 _capturedReferenceResolution;
    [SerializeField, HideInInspector] private bool _hasCapturedReferenceResolution;
    [SerializeField, HideInInspector] private string _contentRootPath;

    private Canvas _canvas;
    private RectTransform _canvasRect;
    private RectTransform _contentRoot;

    private Vector2 _lastDisplaySize = Vector2.zero;
    private Rect _lastSafeArea = default;
    private RenderMode _lastRenderMode;

    protected override void Handle()
    {
        EnsureComponents();
        if (_canvas == null || _canvasRect == null)
            return;

        if (_canvas.renderMode == RenderMode.WorldSpace)
        {
            base.Handle();
            return;
        }

        CaptureReferenceResolutionIfNeeded();
        ResolveContentRoot();
        ApplyAdaptiveScaleSettings();
        ApplySafeAreaToContentRoot();
        CacheCurrentState();
        base.Handle();
    }

    protected override void HandleConstantPixelSize()
    {
        base.HandleConstantPixelSize();
    }

    protected override void HandleScaleWithScreenSize()
    {
        base.HandleScaleWithScreenSize();
    }

    protected override void HandleConstantPhysicalSize()
    {
        base.HandleConstantPhysicalSize();
    }

    private void EnsureComponents()
    {
        if (_canvas == null)
            _canvas = GetComponent<Canvas>();

        if (_canvasRect == null)
            _canvasRect = GetComponent<RectTransform>();
    }

    private void CaptureReferenceResolutionIfNeeded()
    {
        if (_hasCapturedReferenceResolution)
            return;

        Vector2 candidate = Vector2.zero;

        RectTransform preferredRoot = FindBestContentRoot();
        if (preferredRoot != null && preferredRoot != _canvasRect)
            candidate = GetRectSize(preferredRoot);

        if (candidate.x < 1f || candidate.y < 1f)
            candidate = GetRectSize(_canvasRect);

        if (candidate.x < 1f || candidate.y < 1f)
        {
            Vector2 display = GetDisplaySize();
            candidate = display.x >= display.y
                ? kDefaultLandscapeReference
                : kDefaultPortraitReference;
        }

        _capturedReferenceResolution = new Vector2(
            Mathf.Max(32f, Mathf.Round(candidate.x)),
            Mathf.Max(32f, Mathf.Round(candidate.y)));

        _hasCapturedReferenceResolution = true;
    }

    private void ResolveContentRoot()
    {
        if (!string.IsNullOrEmpty(_contentRootPath))
        {
            Transform existing = transform.Find(_contentRootPath);
            _contentRoot = existing as RectTransform;
            if (_contentRoot != null)
                return;
        }

        _contentRoot = FindBestContentRoot();
        _contentRootPath = GetRelativePath(_contentRoot);
    }

    private RectTransform FindBestContentRoot()
    {
        if (_canvasRect == null)
            return null;

        RectTransform best = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < _canvasRect.childCount; i++)
        {
            RectTransform child = _canvasRect.GetChild(i) as RectTransform;
            if (child == null)
                continue;

            float score = ScoreContentRootCandidate(child);
            if (score > bestScore)
            {
                bestScore = score;
                best = child;
            }
        }

        return best != null ? best : _canvasRect;
    }

    private float ScoreContentRootCandidate(RectTransform candidate)
    {
        if (candidate == null || !candidate.gameObject.activeInHierarchy)
            return float.NegativeInfinity;

        float score = 0f;
        Rect rect = candidate.rect;

        if (rect.width > 0f && rect.height > 0f)
            score += rect.width * rect.height * 0.001f;

        Vector2 anchorSpan = candidate.anchorMax - candidate.anchorMin;
        score += anchorSpan.x * 40f;
        score += anchorSpan.y * 40f;

        bool fullStretch = Approximately(candidate.anchorMin, Vector2.zero)
            && Approximately(candidate.anchorMax, Vector2.one);
        if (fullStretch)
            score += 160f;

        if (Approximately(candidate.offsetMin, Vector2.zero) && Approximately(candidate.offsetMax, Vector2.zero))
            score += 50f;
        else
            score -= (candidate.offsetMin.magnitude + candidate.offsetMax.magnitude) * 0.05f;

        if (candidate.parent == _canvasRect)
            score += 25f;

        if (candidate.GetComponent<LayoutGroup>() != null)
            score += 20f;

        if (candidate.GetComponent<ContentSizeFitter>() != null)
            score -= 10f;

        if (candidate.GetComponent<ScrollRect>() != null)
            score -= 40f;

        if (candidate.GetComponent<Mask>() != null || candidate.GetComponent<RectMask2D>() != null)
            score += 5f;

        if (candidate.childCount > 0)
            score += Mathf.Min(candidate.childCount, 20) * 3f;

        Vector2 candidateSize = GetRectSize(candidate);
        Vector2 canvasSize = GetRectSize(_canvasRect);
        if (candidateSize.x > 0f && candidateSize.y > 0f && canvasSize.x > 0f && canvasSize.y > 0f)
        {
            float widthRatio = Mathf.Min(candidateSize.x, canvasSize.x) / Mathf.Max(candidateSize.x, canvasSize.x);
            float heightRatio = Mathf.Min(candidateSize.y, canvasSize.y) / Mathf.Max(candidateSize.y, canvasSize.y);
            score += widthRatio * 60f;
            score += heightRatio * 60f;
        }

        return score;
    }

    private void ApplyAdaptiveScaleSettings()
    {
        uiScaleMode = ScaleMode.ScaleWithScreenSize;
        screenMatchMode = ScreenMatchMode.MatchWidthOrHeight;

        if (referencePixelsPerUnit <= 0f)
            referencePixelsPerUnit = kDefaultReferencePixelsPerUnit;

        if (fallbackScreenDPI <= 0f)
            fallbackScreenDPI = kDefaultFallbackScreenDpi;

        if (defaultSpriteDPI <= 0f)
            defaultSpriteDPI = kDefaultDefaultSpriteDpi;

        Vector2 reference = _hasCapturedReferenceResolution
            ? _capturedReferenceResolution
            : GetDefaultReferenceForCurrentOrientation();

        if (reference.x < 1f || reference.y < 1f)
            reference = GetDefaultReferenceForCurrentOrientation();

        referenceResolution = reference;

        Vector2 display = GetDisplaySize();
        matchWidthOrHeight = ComputeAdaptiveMatch(reference, display);
    }

    private void ApplySafeAreaToContentRoot()
    {
        if (_contentRoot == null || _contentRoot == _canvasRect)
            return;

        Vector2 display = GetDisplaySize();
        if (display.x < 1f || display.y < 1f)
            return;

        Rect safeArea = Screen.safeArea;
        if (safeArea.width <= 0f || safeArea.height <= 0f)
            return;

        Vector2 anchorMin = new Vector2(
            Mathf.Clamp01(safeArea.xMin / display.x),
            Mathf.Clamp01(safeArea.yMin / display.y));

        Vector2 anchorMax = new Vector2(
            Mathf.Clamp01(safeArea.xMax / display.x),
            Mathf.Clamp01(safeArea.yMax / display.y));

        if (!Approximately(_contentRoot.anchorMin, anchorMin))
            _contentRoot.anchorMin = anchorMin;

        if (!Approximately(_contentRoot.anchorMax, anchorMax))
            _contentRoot.anchorMax = anchorMax;

        if (!Approximately(_contentRoot.offsetMin, Vector2.zero))
            _contentRoot.offsetMin = Vector2.zero;

        if (!Approximately(_contentRoot.offsetMax, Vector2.zero))
            _contentRoot.offsetMax = Vector2.zero;
    }

    private Vector2 GetDefaultReferenceForCurrentOrientation()
    {
        Vector2 display = GetDisplaySize();
        return display.x >= display.y
            ? kDefaultLandscapeReference
            : kDefaultPortraitReference;
    }

    private Vector2 GetDisplaySize()
    {
#if UNITY_2020_1_OR_NEWER
        if (_canvas != null)
        {
            Vector2 renderingDisplaySize = _canvas.renderingDisplaySize;
            if (renderingDisplaySize.x > 0f && renderingDisplaySize.y > 0f)
                return renderingDisplaySize;
        }
#endif
        return new Vector2(Screen.width, Screen.height);
    }

    private static Vector2 GetRectSize(RectTransform rectTransform)
    {
        if (rectTransform == null)
            return Vector2.zero;

        Rect rect = rectTransform.rect;
        if (rect.width <= 0f || rect.height <= 0f)
            return Vector2.zero;

        return rect.size;
    }

    private static float ComputeAdaptiveMatch(Vector2 reference, Vector2 display)
    {
        if (reference.x < 1f || reference.y < 1f || display.x < 1f || display.y < 1f)
            return 0.5f;

        float referenceAspect = reference.x / reference.y;
        float displayAspect = display.x / display.y;
        float delta = (displayAspect - referenceAspect) / referenceAspect;

        if (Mathf.Abs(delta) <= kAspectDeadZone)
            return 0.5f;

        float signed = Mathf.Clamp(delta / kAspectCurveRange, -1f, 1f);
        float curved = Mathf.SmoothStep(0f, 1f, (signed + 1f) * 0.5f);
        return Mathf.Clamp01(curved);
    }

    private static bool Approximately(Vector2 a, Vector2 b)
    {
        return Mathf.Abs(a.x - b.x) < 0.001f && Mathf.Abs(a.y - b.y) < 0.001f;
    }

    private string GetRelativePath(RectTransform target)
    {
        if (target == null || target == _canvasRect)
            return string.Empty;

        List<string> names = new List<string>();
        Transform current = target;
        while (current != null && current != transform)
        {
            names.Add(current.name);
            current = current.parent;
        }

        if (current != transform)
            return string.Empty;

        names.Reverse();
        return string.Join("/", names);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        EnsureComponents();
        ForceRefresh();
    }

    protected override void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.delayCall -= ForceRefresh;
#endif
        base.OnDisable();
    }

    protected override void OnDestroy()
    {
#if UNITY_EDITOR
        EditorApplication.delayCall -= ForceRefresh;
#endif
        base.OnDestroy();
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        EnsureComponents();
        EditorApplication.delayCall -= ForceRefresh;
        EditorApplication.delayCall += ForceRefresh;
    }
#endif

    private void Update()
    {
        if (!isActiveAndEnabled)
            return;

        if (_canvas == null)
            EnsureComponents();

        if (_canvas == null || _canvas.renderMode == RenderMode.WorldSpace)
            return;

        Vector2 display = GetDisplaySize();
        Rect safeArea = Screen.safeArea;
        RenderMode renderMode = _canvas.renderMode;

        if (!Approximately(display, _lastDisplaySize)
            || !ApproximatelyRect(safeArea, _lastSafeArea)
            || renderMode != _lastRenderMode)
        {
            ForceRefresh();
        }
    }

    private void ForceRefresh()
    {
        if (this == null || !isActiveAndEnabled)
            return;

        EnsureComponents();
        ResolveContentRoot();
        Handle();
    }

    private void CacheCurrentState()
    {
        _lastDisplaySize = GetDisplaySize();
        _lastSafeArea = Screen.safeArea;
        _lastRenderMode = _canvas != null ? _canvas.renderMode : RenderMode.ScreenSpaceOverlay;
    }

    private static bool ApproximatelyRect(Rect a, Rect b)
    {
        return Mathf.Abs(a.x - b.x) < 0.5f
            && Mathf.Abs(a.y - b.y) < 0.5f
            && Mathf.Abs(a.width - b.width) < 0.5f
            && Mathf.Abs(a.height - b.height) < 0.5f;
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(AdaptiveCanvasScaler))]
internal sealed class AdaptiveCanvasScalerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        AdaptiveCanvasScaler scaler = (AdaptiveCanvasScaler)target;
        Canvas canvas = scaler.GetComponent<Canvas>();
        RectTransform canvasRect = scaler.GetComponent<RectTransform>();

        EditorGUILayout.Space(4f);
        DrawInfoCard(canvas, canvasRect);
    }

    private static void DrawInfoCard(Canvas canvas, RectTransform canvasRect)
    {
        Color accent = Application.isPlaying
            ? new Color(0.24f, 0.60f, 0.36f, 1f)
            : new Color(0.45f, 0.52f, 0.62f, 1f);

        Rect outer = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        Rect topBar = new Rect(outer.x + 1f, outer.y + 1f, outer.width - 2f, 3f);
        EditorGUI.DrawRect(topBar, accent);

        GUILayout.Space(6f);
        DrawTwoColumnRow(
            "Mode",
            canvas != null ? canvas.renderMode.ToString() : "Unknown",
            "Canvas",
            FormatVector2(canvasRect != null ? canvasRect.rect.size : Vector2.zero));
        GUILayout.Space(2f);
        EditorGUILayout.EndVertical();
    }

    private static void DrawTwoColumnRow(
        string leftLabel,
        string leftValue,
        string rightLabel,
        string rightValue)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(8f);
            DrawInfoCell(leftLabel, leftValue);
            GUILayout.Space(10f);
            DrawInfoCell(rightLabel, rightValue);
            GUILayout.Space(8f);
        }
    }

    private static void DrawInfoCell(string label, string value)
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(120f)))
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            EditorGUILayout.SelectableLabel(
                value,
                EditorStyles.miniLabel,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }
    }

    private static string FormatVector2(Vector2 value)
    {
        return string.Format("{0} × {1}", Mathf.RoundToInt(value.x), Mathf.RoundToInt(value.y));
    }

    private static string FormatRect(Rect rect)
    {
        return string.Format(
            "{0}, {1}  {2} × {3}",
            Mathf.RoundToInt(rect.x),
            Mathf.RoundToInt(rect.y),
            Mathf.RoundToInt(rect.width),
            Mathf.RoundToInt(rect.height));
    }

    private static string FormatAspect(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return "Unknown";

        float aspect = (float)width / height;
        return aspect.ToString("0.###");
    }
}
#endif
