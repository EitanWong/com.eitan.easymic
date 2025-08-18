using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[ExecuteInEditMode]
[RequireComponent(typeof(CanvasScaler))]
public class UICanvasAdaptiveScaler : UIBehaviour
{
    // Cached components
    private CanvasScaler canvasScaler;
    
    // Cached values to avoid GC allocations
    private int lastScreenWidth;
    private int lastScreenHeight;
    private float lastAspectRatio;
    private bool isInitialized;
    
    // Resolution check timing
    private readonly float checkInterval = 1.0f;
    private float timeSinceLastCheck;

    protected override void Awake()
    {
        base.Awake();
        canvasScaler = GetComponent<CanvasScaler>();
        if (canvasScaler == null)
        {
            Debug.LogError("CanvasScaler component not found!");
            enabled = false;
            return;
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        // Cache initial screen dimensions
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        lastAspectRatio = (float)lastScreenWidth / lastScreenHeight;
        
        // Initial update
        UpdateCanvasScaler();
        isInitialized = true;
        timeSinceLastCheck = 0f;
    }

    protected void Update()
    {
        if (!isInitialized) return;
        
        // Periodically check for resolution changes using unscaled time
        // to ensure consistent behavior regardless of timeScale
        timeSinceLastCheck += Time.unscaledDeltaTime;
        if (timeSinceLastCheck >= checkInterval)
        {
            CheckResolutionChange();
            timeSinceLastCheck = 0f;
        }
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        if (!isInitialized) return;
        CheckResolutionChange();
    }

    private void CheckResolutionChange()
    {
        // Using direct int comparisons instead of Vector2
        int currentWidth = Screen.width;
        int currentHeight = Screen.height;
        
        // Only calculate aspect ratio if dimensions have changed
        if (currentWidth != lastScreenWidth || currentHeight != lastScreenHeight)
        {
            float currentAspectRatio = (float)currentWidth / currentHeight;
            
            // Only update if aspect ratio has changed significantly
            if (Mathf.Abs(currentAspectRatio - lastAspectRatio) > 0.01f)
            {
                UpdateCanvasScaler();
                lastAspectRatio = currentAspectRatio;
            }
            
            // Update cached dimensions
            lastScreenWidth = currentWidth;
            lastScreenHeight = currentHeight;
        }
    }

    private void UpdateCanvasScaler()
    {
        int screenWidth = Screen.width;
        int screenHeight = Screen.height;
        float screenAspectRatio = (float)screenWidth / screenHeight;
        
        // Configure Canvas Scaler
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        
        // Set reference resolution directly to match actual screen resolution 
        // with some adjustments for extreme cases
        if (screenAspectRatio >= 1.7f) // Wide screens (16:9 and wider)
        {
            // For wide screens, match height for consistent UI size
            canvasScaler.matchWidthOrHeight = 1.0f;
        }
        else if (screenAspectRatio <= 1.5f) // Narrow screens (4:3 and narrower)
        {
            // For narrow screens, match width for consistent UI size
            canvasScaler.matchWidthOrHeight = 0.0f;
        }
        else
        {
            // For mid-range aspect ratios, blend between width and height matching
            float blend = (screenAspectRatio - 1.5f) / 0.2f; // Linear interpolation between 1.5 and 1.7
            canvasScaler.matchWidthOrHeight = blend;
        }
        
        // Use the actual screen resolution as reference to maintain consistent pixel density
        canvasScaler.referenceResolution = new Vector2(screenWidth, screenHeight);
    }
}

