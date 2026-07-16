using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

[DefaultExecutionOrder(100)]
[ExecuteInEditMode]
public class Subtitles : MonoBehaviour
{
    public SubtitleAsset subs;

    float timeAtPlay;

    public bool isPlaying = false;
    bool lastPlaying = false;

    [Header("Standard Camera Placement")]
    [Tooltip("Subtitle position relative to the viewer camera, in meters.")]
    public Vector3 offset = new Vector3(0f, -.36f, .74f);
    [Tooltip("Uniform world-space scale of the subtitle panel.")]
    [Min(.001f)]
    public float size = .1f;
    [Tooltip("How quickly subtitles catch up to their target pose. Set to zero for immediate placement.")]
    [Min(0f)]
    public float smooth = 9f;

    [Header("Immersive Surface Overlay")]
    [Tooltip("Render subtitles as a fixed overlay on one immersive surface camera.")]
    public bool immersiveMode = true;
    public ImmersiveController.SurfaceId immersiveSurface = ImmersiveController.SurfaceId.Front;
    [Tooltip("Normalized viewport position: (0,0) is bottom-left and (1,1) is top-right.")]
    public Vector2 immersivePosition = new Vector2(.5f, .12f);
    [Tooltip("Subtitle panel width as a fraction of the selected surface width.")]
    [Range(.01f, 2f)]
    public float immersiveSize = .8f;

    UIDocument uiDocument;
    Label subtitleLabel;

    SubtitleLine curLine;

    DataManager dataManager;
    ImmersiveController immersiveController;
    Camera overlayCamera;
    Camera overlayBaseCamera;
    Camera excludedMainCamera;
    int excludedMainCameraMask;
    int originalLayer;
    Transform originalParent;
    Vector3 originalLocalPosition;
    Quaternion originalLocalRotation;
    Vector3 originalLocalScale;
    int originalSiblingIndex;
    bool originalLayerCaptured;
    bool originalTransformCaptured;
    bool overlayStackWarningLogged;

    public Vector3 Position
    {
        get => offset;
        set => offset = SanitizePosition(value);
    }

    public float Size
    {
        get => size;
        set
        {
            size = Mathf.Max(.001f, SanitizeFloat(value));
            ApplySize();
        }
    }

    public bool ImmersiveMode
    {
        get => immersiveMode;
        set => immersiveMode = value;
    }

    public ImmersiveController.SurfaceId ImmersiveSurface
    {
        get => immersiveSurface;
        set => immersiveSurface = value;
    }

    public Vector2 ImmersivePosition
    {
        get => immersivePosition;
        set => immersivePosition = SanitizeViewportPosition(value);
    }

    public float ImmersiveSize
    {
        get => immersiveSize;
        set => immersiveSize = Mathf.Clamp(SanitizeFloat(value), .01f, 2f);
    }

    public float Smoothing
    {
        get => smooth;
        set => smooth = Mathf.Max(0f, SanitizeFloat(value));
    }


    void OnEnable()
    {
        if (!originalLayerCaptured)
        {
            originalLayer = gameObject.layer;
            originalLayerCaptured = true;
        }

        if (!originalTransformCaptured)
        {
            originalParent = transform.parent;
            originalLocalPosition = transform.localPosition;
            originalLocalRotation = transform.localRotation;
            originalLocalScale = transform.localScale;
            originalSiblingIndex = transform.GetSiblingIndex();
            originalTransformCaptured = true;
        }

        SanitizeSettings();
        ApplySize();
        uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            //Debug.LogError("No UIDocument found on Subtitles component");
            return;
        }
        //uiDocument.enabled = true;
        initDocument();
    }

    void OnDisable()
    {
        RestoreStandardRouting();
    }

    void OnValidate()
    {
        SanitizeSettings();
        ApplySize();
    }

    void initDocument()
    {
        if (uiDocument == null || uiDocument.rootVisualElement == null)
        {
            //Debug.LogError("Cannot initialize subtitle document: UIDocument is null or rootVisualElement is null");
            return;
        }

        subtitleLabel = uiDocument.rootVisualElement.Q<Label>("subtitle");
        subtitleLabel.AddToClassList("hidden");
        //Debug.Log("Subtitle label: " + (subtitleLabel != null ? subtitleLabel.name : "null"));
    }

    private void Start()
    {
        dataManager = GameObject.FindAnyObjectByType<DataManager>();
    }

    // Update is called once per frame
    void Update()
    {
        if (subtitleLabel == null)
        {
            initDocument();
        }

        if (isPlaying != lastPlaying || (isPlaying && timeAtPlay == 0))
        {
            //Debug.Log("Subtitle playback started at " + Time.time);
            subtitleLabel.AddToClassList("hidden");
            if (isPlaying)
            {
                timeAtPlay = Time.time;
            }
            lastPlaying = isPlaying;
            //uiDocument.enabled = true;
        }

        if (subtitleLabel == null)
        {
            //Debug.LogError("No subtitle label found in UIDocument");
            return;
        }


        UpdatePlacement();

        if (isPlaying && subs != null && timeAtPlay > 0)
        {
            float timeSincePlay = Time.time - timeAtPlay;
            SubtitleLine subtitle = getSubtitleAt(timeSincePlay, out bool isFinished);
            if (curLine != subtitle)
            {
                //Debug.Log($"Subtitle changed at {timeSincePlay:F2}s: {(subtitle != null ? subtitle.text : "null")}");
                curLine = subtitle;

                if (curLine != null)
                {
                    //Debug.Log($"Current subtitle: {curLine.text}");
                    subtitleLabel.RemoveFromClassList("hidden");
                    subtitleLabel.text = subtitle.text;
                }
                else
                {
                    if (isFinished)
                    {
                        Debug.Log("Subtitle playback finished at " + Time.time);
                        isPlaying = false;
                    }
                    subtitleLabel.AddToClassList("hidden");
                }
            }

        }
        else
        {
            //uiDocument.enabled = false;
        }
    }

    void UpdatePlacement()
    {
        Camera targetCamera = Camera.main;
        ImmersiveController targetImmersiveController = GetImmersiveController();
        if (immersiveMode)
        {
            if (!Application.isPlaying)
            {
                RestoreStandardRouting();
                return;
            }

            if (targetImmersiveController != null
                && targetImmersiveController.TryGetSurfaceCamera(immersiveSurface, out var surfaceCamera))
            {
                ConfigureImmersiveRouting(targetImmersiveController, surfaceCamera, targetCamera);
                UpdateImmersiveOverlayPlacement(surfaceCamera);
            }
            else
            {
                RestoreStandardRouting();
            }

            return;
        }

        RestoreStandardRouting();
        if (targetCamera == null)
        {
            return;
        }

        Transform anchor = targetCamera.transform;
        Vector3 targetPosition = anchor.TransformPoint(offset);
        Vector3 targetForward = targetPosition - anchor.position;
        if (targetForward.sqrMagnitude <= Mathf.Epsilon)
        {
            targetForward = anchor.forward;
        }

        Quaternion targetRotation = Quaternion.LookRotation(targetForward, anchor.up);
        float blend = smooth <= 0f || !Application.isPlaying
            ? 1f
            : 1f - Mathf.Exp(-smooth * Time.unscaledDeltaTime);

        transform.position = Vector3.Lerp(transform.position, targetPosition, blend);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, blend);
    }

    ImmersiveController GetImmersiveController()
    {
        if (immersiveController == null)
        {
            immersiveController =
                FindAnyObjectByType<ImmersiveController>(FindObjectsInactive.Include);
        }

        return immersiveController;
    }

    void ConfigureImmersiveRouting(
        ImmersiveController controller,
        Camera surfaceCamera,
        Camera mainCamera)
    {
        int overlayLayer = controller.SubtitleOverlayLayer;
        if (overlayLayer < 0)
        {
            overlayLayer = LayerMask.NameToLayer(ImmersiveController.SubtitleOverlayLayerName);
        }

        if (overlayLayer < 0)
        {
            return;
        }

        EnsureOverlayCamera(overlayLayer);
        gameObject.layer = overlayLayer;
        ExcludeOverlayFromMainCamera(mainCamera, overlayLayer);
        if (!AttachOverlayCamera(surfaceCamera))
        {
            return;
        }

        if (transform.parent != overlayCamera.transform)
        {
            transform.SetParent(overlayCamera.transform, false);
        }
    }

    void ExcludeOverlayFromMainCamera(Camera mainCamera, int overlayLayer)
    {
        if (mainCamera == null)
        {
            return;
        }

        if (excludedMainCamera != mainCamera)
        {
            RestoreMainCameraMask();
            excludedMainCamera = mainCamera;
            excludedMainCameraMask = mainCamera.cullingMask;
        }

        mainCamera.cullingMask = excludedMainCameraMask & ~(1 << overlayLayer);
    }

    void EnsureOverlayCamera(int overlayLayer)
    {
        if (overlayCamera != null)
        {
            overlayCamera.cullingMask = 1 << overlayLayer;
            return;
        }

        // Keep this helper in the active scene hierarchy. HideAndDontSave removes
        // the object from that hierarchy, so parenting the UIDocument below it
        // deactivates the subtitle and tears the camera down again in OnDisable.
        var cameraObject = new GameObject("Subtitle 2D Overlay Camera")
        {
            hideFlags = HideFlags.HideInHierarchy,
            layer = overlayLayer
        };

        overlayCamera = cameraObject.AddComponent<Camera>();
        overlayCamera.enabled = true;
        overlayCamera.orthographic = true;
        overlayCamera.orthographicSize = .5f;
        overlayCamera.nearClipPlane = .01f;
        overlayCamera.farClipPlane = 10f;
        overlayCamera.clearFlags = CameraClearFlags.Nothing;
        overlayCamera.cullingMask = 1 << overlayLayer;
        overlayCamera.useOcclusionCulling = false;
        overlayCamera.allowHDR = false;
        overlayCamera.allowMSAA = false;
        overlayCamera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        var overlayData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        overlayData.renderType = CameraRenderType.Overlay;
        overlayData.renderPostProcessing = false;
        overlayData.renderShadows = false;
    }

    bool AttachOverlayCamera(Camera surfaceCamera)
    {
        if (overlayCamera == null || surfaceCamera == null)
        {
            return false;
        }

        var surfaceData = surfaceCamera.GetUniversalAdditionalCameraData();
        var cameraStack = surfaceData.cameraStack;
        if (cameraStack == null)
        {
            if (!overlayStackWarningLogged)
            {
                Debug.LogError(
                    $"The renderer used by {surfaceCamera.name} does not support URP camera stacking; "
                    + "the subtitle 2D overlay cannot be composited into this immersive output.",
                    this);
                overlayStackWarningLogged = true;
            }

            return false;
        }

        overlayStackWarningLogged = false;
        if (overlayBaseCamera == surfaceCamera && cameraStack.Contains(overlayCamera))
        {
            return true;
        }

        DetachOverlayCamera();
        cameraStack.RemoveAll(camera => camera == null);
        // The overlay is rendered into the base camera target before the
        // texture-based Spout and NDI senders read that render texture.
        cameraStack.Add(overlayCamera);
        overlayBaseCamera = surfaceCamera;
        return true;
    }

    void DetachOverlayCamera()
    {
        if (overlayBaseCamera != null)
        {
            var surfaceData = overlayBaseCamera.GetComponent<UniversalAdditionalCameraData>();
            surfaceData?.cameraStack.Remove(overlayCamera);
        }

        overlayBaseCamera = null;
    }

    void RestoreStandardRouting()
    {
        RestoreOriginalTransformParent();
        DetachOverlayCamera();
        DestroyOverlayCamera();

        if (originalLayerCaptured)
        {
            gameObject.layer = originalLayer;
        }

        RestoreMainCameraMask();
    }

    void RestoreOriginalTransformParent()
    {
        if (!originalTransformCaptured || transform.parent == originalParent)
        {
            return;
        }

        transform.SetParent(originalParent, false);
        transform.localPosition = originalLocalPosition;
        transform.localRotation = originalLocalRotation;
        transform.localScale = originalLocalScale;
        if (originalParent != null)
        {
            transform.SetSiblingIndex(Mathf.Min(originalSiblingIndex, originalParent.childCount - 1));
        }
    }

    void DestroyOverlayCamera()
    {
        if (overlayCamera == null)
        {
            return;
        }

        GameObject cameraObject = overlayCamera.gameObject;
        overlayCamera = null;
        if (Application.isPlaying)
        {
            Destroy(cameraObject);
        }
        else
        {
            DestroyImmediate(cameraObject);
        }
    }

    void RestoreMainCameraMask()
    {
        if (excludedMainCamera != null)
        {
            excludedMainCamera.cullingMask = excludedMainCameraMask;
        }

        excludedMainCamera = null;
    }

    void UpdateImmersiveOverlayPlacement(Camera surfaceCamera)
    {
        if (overlayCamera == null)
        {
            return;
        }

        RenderTexture targetTexture = surfaceCamera.targetTexture as RenderTexture;
        float width = targetTexture != null ? targetTexture.width : Mathf.Max(1, surfaceCamera.pixelWidth);
        float height = targetTexture != null ? targetTexture.height : Mathf.Max(1, surfaceCamera.pixelHeight);
        float aspect = width / Mathf.Max(1f, height);
        overlayCamera.aspect = aspect;
        overlayCamera.orthographicSize = .5f;

        float panelWorldWidthAtScaleOne = GetPanelWorldWidthAtScaleOne();
        float targetScale = aspect * immersiveSize / panelWorldWidthAtScaleOne;

        transform.localPosition = new Vector3(
            (immersivePosition.x - .5f) * aspect,
            immersivePosition.y - .5f,
            1f);
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one * targetScale;
    }

    float GetPanelWorldWidthAtScaleOne()
    {
        if (uiDocument == null)
        {
            return 6.5f;
        }

        float pixelsPerUnit = uiDocument.panelSettings != null
            ? uiDocument.panelSettings.referenceSpritePixelsPerUnit
            : 100f;
        return Mathf.Max(.001f, uiDocument.worldSpaceSize.x / Mathf.Max(.001f, pixelsPerUnit));
    }

    void ApplySize()
    {
        transform.localScale = Vector3.one * size;
    }

    void SanitizeSettings()
    {
        offset = SanitizePosition(offset);
        size = Mathf.Max(.001f, SanitizeFloat(size));
        smooth = Mathf.Max(0f, SanitizeFloat(smooth));
        immersivePosition = SanitizeViewportPosition(immersivePosition);
        immersiveSize = Mathf.Clamp(SanitizeFloat(immersiveSize), .01f, 2f);
    }

    static Vector3 SanitizePosition(Vector3 value)
    {
        return new Vector3(
            SanitizeFloat(value.x),
            SanitizeFloat(value.y),
            SanitizeFloat(value.z));
    }

    static Vector2 SanitizeViewportPosition(Vector2 value)
    {
        return new Vector2(
            Mathf.Clamp01(SanitizeFloat(value.x)),
            Mathf.Clamp01(SanitizeFloat(value.y)));
    }

    static float SanitizeFloat(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    public void play(SubtitleAsset file)
    {
        subs = file;
        isPlaying = true;
        //uiDocument.enabled = true;
    }

    public void play(string relativeSubtitlePath)
    {
        play(relativeSubtitlePath, 0f);
    }

    public void play(string relativeSubtitlePath, float startTime)
    {
        if (!dataManager.IsFolderReady(DataManager.DataFolder.Interviews))
        {
            dataManager.PreloadFolder(DataManager.DataFolder.Interviews, (success, path) =>
            {
                if (!success)
                {
                    subs = null;
                    isPlaying = false;
                    return;
                }

                subs = LoadSubtitleAsset(relativeSubtitlePath);
                timeAtPlay = Time.time - Mathf.Max(0f, startTime);
                isPlaying = subs != null;
            });
            return;
        }

        timeAtPlay = Time.time - Mathf.Max(0f, startTime);
        subs = LoadSubtitleAsset(relativeSubtitlePath);
        if (subs != null)
        {
            Debug.Log("Loading subtitles for " + relativeSubtitlePath + ", first line at " + (subs.lines != null && subs.lines.Count > 0 ? subs.lines[0].startTime.ToString("F2") : "no lines"));
        }

        isPlaying = subs != null;
    }

    public void stop()
    {
        isPlaying = false;
        //uiDocument.enabled = false;
    }


    SubtitleLine getSubtitleAt(float time, out bool isFinished)
    {
        if (subs == null || subs.lines == null)
        {
            isFinished = true;
            return null;
        }

        bool isAfterLastLine = time > subs.lines[subs.lines.Count - 1].endTime;
        isFinished = isAfterLastLine;

        foreach (var subtitle in subs.lines)
        {
            if (subtitle.startTime <= time && subtitle.endTime >= time)
            {
                return subtitle;
            }
        }

        return null;
    }

    SubtitleAsset LoadSubtitleAsset(string relativeSubtitlePath)
    {
        if (string.IsNullOrWhiteSpace(relativeSubtitlePath))
        {
            Debug.LogWarning("Relative subtitle path is null or empty");
            return null;
        }

        string subtitlePath = dataManager.GetFilePath(DataManager.DataFolder.Interviews, relativeSubtitlePath);
        if (!File.Exists(subtitlePath))
        {
            Debug.LogWarning($"Subtitle file not found at path: {subtitlePath}");
            return null;
        }

        return SubtitleSrtParser.LoadFromFile(subtitlePath);
    }
}
