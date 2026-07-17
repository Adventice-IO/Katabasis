using System;
using Klak.Ndi;
using Klak.Spout;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

[DefaultExecutionOrder(5000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[RequireComponent(typeof(UniversalAdditionalCameraData))]
public sealed class PcVrSpectatorCamera : MonoBehaviour
{
    public const int CurrentConfigurationVersion = 2;

    public enum PipCorner
    {
        TopRight,
        TopLeft,
        BottomRight,
        BottomLeft
    }

    [Serializable]
    public sealed class RuntimeConfiguration
    {
        public int version = CurrentConfigurationVersion;
        public bool enabled;

        public float positionSmoothing = .15f;
        public float rotationSmoothing = .2f;
        public float maxPositionSpeed = 10f;
        public float maxRotationSpeed = 180f;
        public float horizonLock = .35f;
        public Vector3 positionOffset;
        public Vector3 rotationOffset;

        public float fieldOfView = 75f;
        public float nearClipPlane = .05f;
        public float farClipPlane = 1000f;
        public int targetDisplay;

        public int outputWidth = 1920;
        public int outputHeight = 1080;
        public PipCorner pipCorner = PipCorner.TopRight;
        public float pipWidth = .32f;
        public int pipMargin = 16;

        public string streamName = "Katabasis PC-VR";
        public bool enableSpoutSender = true;
        public bool enableNdiSender = true;
    }

    [Header("Tracked View")]
    [Tooltip("Usually the XR Main Camera. When empty, Camera.main is resolved automatically.")]
    [SerializeField] private Camera sourceCamera;

    [Header("Runtime Configuration")]
    [SerializeField] private RuntimeConfiguration configuration = new RuntimeConfiguration();

    private Camera _spectatorCamera;
    private UniversalAdditionalCameraData _additionalCameraData;
    private SpoutSender _spoutSender;
    private NdiSender _ndiSender;
    private RenderTexture _renderTexture;
    private GameObject _pipCanvasObject;
    private Canvas _pipCanvas;
    private RectTransform _pipFrame;
    private RawImage _pipImage;
    private Camera _activeSourceCamera;
    private Vector3 _positionVelocity;
    private bool _sourceWarningLogged;
    private int _deferredOutputRefreshFrame = -1;
    private int _deferredOutputReactivateFrame = -1;

    public event Action<RuntimeConfiguration> ConfigurationChanged;

    public Camera SourceCamera => _activeSourceCamera;
    public Camera SpectatorCamera => _spectatorCamera;
    public RenderTexture OutputTexture => _renderTexture;
    public bool IsSpoutSupported => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11;
    public bool IsTargetDisplayAvailable => configuration.targetDisplay >= 0
        && configuration.targetDisplay < Display.displays.Length;
    public bool IsOutputActive => configuration.enabled
        && _spectatorCamera != null
        && _spectatorCamera.enabled
        && _activeSourceCamera != null;

    private void Awake()
    {
        CacheComponents();
        NormalizeConfiguration(configuration);
        ResolveSourceCamera();
        ApplyCameraConfiguration();
        ApplySenderConfiguration();

        if (configuration.enabled && _activeSourceCamera != null)
        {
            SnapToSource();
        }

        ScheduleDeferredOutputRefresh();
    }

    private void OnEnable()
    {
        CacheComponents();
        ResolveSourceCamera();
        ApplyCameraConfiguration();
        ApplySenderConfiguration();
    }

    private void OnDestroy()
    {
        ReleaseRenderTexture();
        if (_pipCanvasObject != null)
        {
            Destroy(_pipCanvasObject);
        }
    }

    private void LateUpdate()
    {
        if (!configuration.enabled)
        {
            return;
        }

        if (_deferredOutputReactivateFrame >= 0)
        {
            if (Time.frameCount < _deferredOutputReactivateFrame)
            {
                return;
            }

            _deferredOutputReactivateFrame = -1;
            ApplySenderConfiguration();
            if (_activeSourceCamera != null)
            {
                SnapToSource();
            }
        }

        if (_deferredOutputRefreshFrame >= 0
            && Time.frameCount < _deferredOutputRefreshFrame)
        {
            SetOutputEnabled(false);
            return;
        }

        var outputRecreated = EnsureRenderTexture();
        EnsurePipPresenter();
        if (outputRecreated)
        {
            ApplySenderConfiguration();
        }

        if (_deferredOutputRefreshFrame >= 0
            && Time.frameCount >= _deferredOutputRefreshFrame)
        {
            _deferredOutputRefreshFrame = -1;
            SetOutputEnabled(false);
            RecreateRenderTexture();
            ApplySenderConfiguration(false);
            _deferredOutputReactivateFrame = Time.frameCount + 1;
            return;
        }

        if (_activeSourceCamera == null)
        {
            ResolveSourceCamera();
            if (_activeSourceCamera == null)
            {
                SetOutputEnabled(false);
                if (!_sourceWarningLogged)
                {
                    Debug.LogWarning("PC-VR spectator output is enabled, but no Main Camera is available.", this);
                    _sourceWarningLogged = true;
                }
                return;
            }

            _sourceWarningLogged = false;
            CopySourcePresentationSettings();
            ApplyCameraConfiguration();
            ApplySenderConfiguration();
            SnapToSource();
        }

        if (!_activeSourceCamera.isActiveAndEnabled)
        {
            ResolveSourceCamera();
        }

        SetOutputEnabled(_activeSourceCamera != null);
        if (_activeSourceCamera == null)
        {
            return;
        }

        UpdatePipLayout();

        // Keep scene visibility and presentation aligned with the player's camera,
        // while lens and transform settings remain independent for the audience.
        _spectatorCamera.cullingMask = _activeSourceCamera.cullingMask;
        _spectatorCamera.clearFlags = _activeSourceCamera.clearFlags;
        _spectatorCamera.backgroundColor = _activeSourceCamera.backgroundColor;

        var deltaTime = Time.unscaledDeltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        var targetPosition = _activeSourceCamera.transform.TransformPoint(configuration.positionOffset);
        var targetRotation = GetTargetRotation();

        if (configuration.positionSmoothing <= 0f)
        {
            transform.position = targetPosition;
            _positionVelocity = Vector3.zero;
        }
        else
        {
            transform.position = Vector3.SmoothDamp(
                transform.position,
                targetPosition,
                ref _positionVelocity,
                configuration.positionSmoothing,
                configuration.maxPositionSpeed,
                deltaTime);
        }

        if (configuration.rotationSmoothing <= 0f)
        {
            transform.rotation = targetRotation;
        }
        else
        {
            var angle = Quaternion.Angle(transform.rotation, targetRotation);
            var response = 1f - Mathf.Exp(-deltaTime / configuration.rotationSmoothing);
            var step = Mathf.Min(angle * response, configuration.maxRotationSpeed * deltaTime);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, step);
        }
    }

    private void OnValidate()
    {
        if (configuration == null)
        {
            configuration = new RuntimeConfiguration();
        }

        NormalizeConfiguration(configuration);
        if (!Application.isPlaying)
        {
            CacheComponents();
            ApplyCameraConfiguration();
            ApplySenderConfiguration();
        }
    }

    public RuntimeConfiguration CaptureConfiguration()
    {
        return CopyConfiguration(configuration);
    }

    public void ApplyConfiguration(RuntimeConfiguration value, bool notify = true)
    {
        if (value == null)
        {
            return;
        }

        var wasEnabled = configuration != null && configuration.enabled;
        configuration = CopyConfiguration(value);
        NormalizeConfiguration(configuration);

        CacheComponents();
        ResolveSourceCamera();
        ApplyCameraConfiguration();
        ApplySenderConfiguration();

        if (!wasEnabled && configuration.enabled && _activeSourceCamera != null)
        {
            SnapToSource();
        }

        if (!wasEnabled && configuration.enabled)
        {
            ScheduleDeferredOutputRefresh();
        }
        else if (!configuration.enabled)
        {
            _deferredOutputRefreshFrame = -1;
            _deferredOutputReactivateFrame = -1;
        }

        if (notify)
        {
            ConfigurationChanged?.Invoke(CaptureConfiguration());
        }
    }

    public void SnapToSource()
    {
        ResolveSourceCamera();
        if (_activeSourceCamera == null)
        {
            return;
        }

        transform.SetPositionAndRotation(
            _activeSourceCamera.transform.TransformPoint(configuration.positionOffset),
            GetTargetRotation());
        _positionVelocity = Vector3.zero;
    }

    public string GetStatusSummary()
    {
        if (!configuration.enabled)
        {
            return "PC-VR spectator camera is disabled.";
        }

        if (_activeSourceCamera == null)
        {
            return "Waiting for the XR Main Camera.";
        }

        var spoutState = !configuration.enableSpoutSender
            ? "Spout off"
            : IsSpoutSupported ? "Spout on" : "Spout unavailable (requires Direct3D 11)";
        var ndiState = configuration.enableNdiSender ? "NDI on" : "NDI off";
        var resolution = _renderTexture != null
            ? $"{_renderTexture.width} x {_renderTexture.height}"
            : "no output";
        var displayState = IsTargetDisplayAvailable
            ? $"Display {configuration.targetDisplay + 1}"
            : $"Display {configuration.targetDisplay + 1} unavailable";

        return $"{resolution} | {displayState} | {spoutState} | {ndiState}";
    }

    public static void NormalizeConfiguration(RuntimeConfiguration value)
    {
        if (value == null)
        {
            return;
        }

        if (value.version < 2)
        {
            value.outputWidth = 1920;
            value.outputHeight = 1080;
            value.pipCorner = PipCorner.TopRight;
            value.pipWidth = .32f;
            value.pipMargin = 16;
        }

        value.version = CurrentConfigurationVersion;
        value.positionSmoothing = NonNegative(value.positionSmoothing);
        value.rotationSmoothing = NonNegative(value.rotationSmoothing);
        value.maxPositionSpeed = Mathf.Max(.01f, NonNegative(value.maxPositionSpeed));
        value.maxRotationSpeed = Mathf.Max(.01f, NonNegative(value.maxRotationSpeed));
        value.horizonLock = Mathf.Clamp01(Finite(value.horizonLock));
        value.positionOffset = Finite(value.positionOffset);
        value.rotationOffset = Finite(value.rotationOffset);
        value.fieldOfView = Mathf.Clamp(Finite(value.fieldOfView), 10f, 160f);
        value.nearClipPlane = Mathf.Max(.001f, NonNegative(value.nearClipPlane));
        value.farClipPlane = Mathf.Max(value.nearClipPlane + .01f, NonNegative(value.farClipPlane));
        value.targetDisplay = Mathf.Clamp(value.targetDisplay, 0, 7);
        value.outputWidth = Mathf.Clamp(value.outputWidth, 16, 8192);
        value.outputHeight = Mathf.Clamp(value.outputHeight, 16, 8192);
        if (!Enum.IsDefined(typeof(PipCorner), value.pipCorner))
        {
            value.pipCorner = PipCorner.TopRight;
        }

        value.pipWidth = Mathf.Clamp(Finite(value.pipWidth), .1f, .8f);
        value.pipMargin = Mathf.Clamp(value.pipMargin, 0, 512);
        value.streamName = string.IsNullOrWhiteSpace(value.streamName)
            ? "Katabasis PC-VR"
            : value.streamName.Trim();
    }

    private void CacheComponents()
    {
        if (_spectatorCamera == null)
        {
            _spectatorCamera = GetComponent<Camera>();
        }

        if (_additionalCameraData == null)
        {
            _additionalCameraData = GetComponent<UniversalAdditionalCameraData>();
        }

        if (_spoutSender == null)
        {
            _spoutSender = GetComponent<SpoutSender>();
        }

        if (_ndiSender == null)
        {
            _ndiSender = GetComponent<NdiSender>();
        }
    }

    private void ResolveSourceCamera()
    {
        var resolved = sourceCamera != null && sourceCamera != _spectatorCamera
            ? sourceCamera
            : Camera.main;

        if (resolved == _spectatorCamera)
        {
            resolved = null;
        }

        if (resolved == _activeSourceCamera)
        {
            return;
        }

        _activeSourceCamera = resolved;
        if (_activeSourceCamera != null)
        {
            CopySourcePresentationSettings();
        }
    }

    private void CopySourcePresentationSettings()
    {
        if (_spectatorCamera == null || _activeSourceCamera == null)
        {
            return;
        }

        _spectatorCamera.cullingMask = _activeSourceCamera.cullingMask;
        _spectatorCamera.clearFlags = _activeSourceCamera.clearFlags;
        _spectatorCamera.backgroundColor = _activeSourceCamera.backgroundColor;
        _spectatorCamera.renderingPath = _activeSourceCamera.renderingPath;
        _spectatorCamera.allowHDR = _activeSourceCamera.allowHDR;
        _spectatorCamera.allowMSAA = _activeSourceCamera.allowMSAA;
        _spectatorCamera.allowDynamicResolution = _activeSourceCamera.allowDynamicResolution;
        _spectatorCamera.depthTextureMode = _activeSourceCamera.depthTextureMode;
        _spectatorCamera.useOcclusionCulling = _activeSourceCamera.useOcclusionCulling;
    }

    private void ApplyCameraConfiguration()
    {
        if (_spectatorCamera == null || configuration == null)
        {
            return;
        }

        _spectatorCamera.orthographic = false;
        _spectatorCamera.fieldOfView = configuration.fieldOfView;
        _spectatorCamera.nearClipPlane = configuration.nearClipPlane;
        _spectatorCamera.farClipPlane = configuration.farClipPlane;
        _spectatorCamera.rect = new Rect(0f, 0f, 1f, 1f);
        _spectatorCamera.depth = 0f;
        _spectatorCamera.ResetProjectionMatrix();

        if (_additionalCameraData != null)
        {
            _additionalCameraData.allowXRRendering = false;
        }

        if (Application.isPlaying
            && configuration.targetDisplay > 0
            && IsTargetDisplayAvailable
            && !Display.displays[configuration.targetDisplay].active)
        {
            Display.displays[configuration.targetDisplay].Activate();
        }

        if (configuration.enabled)
        {
            EnsureRenderTexture();
            EnsurePipPresenter();
            UpdatePipLayout();
        }
        else
        {
            ReleaseRenderTexture();
        }
    }

    private void ApplySenderConfiguration(bool updateEnabledState = true)
    {
        if (configuration == null)
        {
            return;
        }

        if (_spoutSender != null)
        {
            _spoutSender.spoutName = configuration.streamName;
            _spoutSender.captureMethod = Klak.Spout.CaptureMethod.Texture;
            _spoutSender.sourceCamera = null;
            _spoutSender.sourceTexture = _renderTexture;
            _spoutSender.keepAlpha = false;
        }

        if (_ndiSender != null)
        {
            _ndiSender.ndiName = configuration.streamName;
            _ndiSender.captureMethod = Klak.Ndi.CaptureMethod.Texture;
            _ndiSender.sourceCamera = null;
            _ndiSender.sourceTexture = _renderTexture;
            _ndiSender.keepAlpha = false;
        }

        if (updateEnabledState)
        {
            SetOutputEnabled(configuration.enabled && _activeSourceCamera != null);
        }
    }

    private void SetOutputEnabled(bool cameraEnabled)
    {
        if (_spectatorCamera != null)
        {
            _spectatorCamera.enabled = cameraEnabled;
        }

        if (_spoutSender != null)
        {
            _spoutSender.enabled = cameraEnabled
                && configuration.enableSpoutSender
                && IsSpoutSupported;
        }

        if (_ndiSender != null)
        {
            _ndiSender.enabled = cameraEnabled && configuration.enableNdiSender;
        }

        if (_pipCanvasObject != null)
        {
            _pipCanvasObject.SetActive(cameraEnabled);
        }
    }

    private bool EnsureRenderTexture()
    {
        if (!Application.isPlaying)
        {
            _spectatorCamera.targetTexture = null;
            return false;
        }

        if (_renderTexture != null
            && _renderTexture.IsCreated()
            && _renderTexture.width == configuration.outputWidth
            && _renderTexture.height == configuration.outputHeight)
        {
            _spectatorCamera.targetTexture = _renderTexture;
            return false;
        }

        ReleaseRenderTexture();
        _renderTexture = new RenderTexture(
            configuration.outputWidth,
            configuration.outputHeight,
            24,
            RenderTextureFormat.ARGB32)
        {
            name = "PC-VR Spectator Output",
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false
        };
        _renderTexture.Create();
        _spectatorCamera.targetTexture = _renderTexture;

        if (_pipImage != null)
        {
            _pipImage.texture = _renderTexture;
        }

        return true;
    }

    private void RecreateRenderTexture()
    {
        ReleaseRenderTexture();
        EnsureRenderTexture();
    }

    private void ScheduleDeferredOutputRefresh()
    {
        if (Application.isPlaying && configuration.enabled)
        {
            // OpenXR can recreate its graphics targets during the first frames.
            // Refresh once after that startup window so the spectator target is
            // backed by the final graphics context.
            _deferredOutputReactivateFrame = -1;
            _deferredOutputRefreshFrame = Time.frameCount + 3;
            SetOutputEnabled(false);
        }
    }

    private void ReleaseRenderTexture()
    {
        if (_renderTexture == null)
        {
            return;
        }

        if (_spectatorCamera != null && _spectatorCamera.targetTexture == _renderTexture)
        {
            _spectatorCamera.targetTexture = null;
        }

        if (_spoutSender != null && _spoutSender.sourceTexture == _renderTexture)
        {
            _spoutSender.sourceTexture = null;
        }

        if (_ndiSender != null && _ndiSender.sourceTexture == _renderTexture)
        {
            _ndiSender.sourceTexture = null;
        }

        if (_pipImage != null && _pipImage.texture == _renderTexture)
        {
            _pipImage.texture = null;
        }

        _renderTexture.Release();
        Destroy(_renderTexture);
        _renderTexture = null;
    }

    private void EnsurePipPresenter()
    {
        if (!Application.isPlaying || _pipCanvasObject != null)
        {
            return;
        }

        _pipCanvasObject = new GameObject(
            "PC-VR Picture in Picture",
            typeof(RectTransform),
            typeof(Canvas));
        _pipCanvas = _pipCanvasObject.GetComponent<Canvas>();
        _pipCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _pipCanvas.sortingOrder = 900;
        _pipCanvas.pixelPerfect = false;

        var frameObject = new GameObject("Frame", typeof(RectTransform), typeof(Image));
        _pipFrame = frameObject.GetComponent<RectTransform>();
        _pipFrame.SetParent(_pipCanvasObject.transform, false);
        var frameImage = frameObject.GetComponent<Image>();
        frameImage.color = new Color(0f, 0f, 0f, .9f);
        frameImage.raycastTarget = false;

        var imageObject = new GameObject("Spectator View", typeof(RectTransform), typeof(RawImage));
        var imageTransform = imageObject.GetComponent<RectTransform>();
        imageTransform.SetParent(_pipFrame, false);
        imageTransform.anchorMin = Vector2.zero;
        imageTransform.anchorMax = Vector2.one;
        imageTransform.offsetMin = new Vector2(3f, 3f);
        imageTransform.offsetMax = new Vector2(-3f, -3f);
        _pipImage = imageObject.GetComponent<RawImage>();
        _pipImage.texture = _renderTexture;
        _pipImage.color = Color.white;
        _pipImage.raycastTarget = false;

        _pipCanvasObject.SetActive(false);
    }

    private void UpdatePipLayout()
    {
        if (_pipCanvas == null || _pipFrame == null || configuration == null)
        {
            return;
        }

        _pipCanvas.targetDisplay = configuration.targetDisplay;

        var displayWidth = Screen.width;
        var displayHeight = Screen.height;
        if (IsTargetDisplayAvailable && configuration.targetDisplay > 0)
        {
            var display = Display.displays[configuration.targetDisplay];
            displayWidth = Mathf.Max(1, display.renderingWidth);
            displayHeight = Mathf.Max(1, display.renderingHeight);
        }

        const float border = 3f;
        var contentWidth = displayWidth * configuration.pipWidth;
        var aspect = configuration.outputWidth / (float)configuration.outputHeight;
        var contentHeight = contentWidth / aspect;
        var maximumHeight = Mathf.Max(1f, displayHeight - configuration.pipMargin * 2f - border * 2f);
        if (contentHeight > maximumHeight)
        {
            contentHeight = maximumHeight;
            contentWidth = contentHeight * aspect;
        }

        var anchor = GetPipAnchor(configuration.pipCorner);
        _pipFrame.anchorMin = anchor;
        _pipFrame.anchorMax = anchor;
        _pipFrame.pivot = anchor;
        _pipFrame.sizeDelta = new Vector2(contentWidth + border * 2f, contentHeight + border * 2f);
        _pipFrame.anchoredPosition = new Vector2(
            anchor.x > .5f ? -configuration.pipMargin : configuration.pipMargin,
            anchor.y > .5f ? -configuration.pipMargin : configuration.pipMargin);
    }

    private static Vector2 GetPipAnchor(PipCorner corner)
    {
        switch (corner)
        {
            case PipCorner.TopLeft:
                return new Vector2(0f, 1f);
            case PipCorner.BottomRight:
                return new Vector2(1f, 0f);
            case PipCorner.BottomLeft:
                return Vector2.zero;
            default:
                return Vector2.one;
        }
    }

    private Quaternion GetTargetRotation()
    {
        var target = _activeSourceCamera.transform.rotation * Quaternion.Euler(configuration.rotationOffset);
        if (configuration.horizonLock <= 0f)
        {
            return target;
        }

        var forward = target * Vector3.forward;
        if (Mathf.Abs(Vector3.Dot(forward.normalized, Vector3.up)) > .999f)
        {
            return target;
        }

        var level = Quaternion.LookRotation(forward, Vector3.up);
        return Quaternion.Slerp(target, level, configuration.horizonLock);
    }

    private static RuntimeConfiguration CopyConfiguration(RuntimeConfiguration source)
    {
        return new RuntimeConfiguration
        {
            version = source.version,
            enabled = source.enabled,
            positionSmoothing = source.positionSmoothing,
            rotationSmoothing = source.rotationSmoothing,
            maxPositionSpeed = source.maxPositionSpeed,
            maxRotationSpeed = source.maxRotationSpeed,
            horizonLock = source.horizonLock,
            positionOffset = source.positionOffset,
            rotationOffset = source.rotationOffset,
            fieldOfView = source.fieldOfView,
            nearClipPlane = source.nearClipPlane,
            farClipPlane = source.farClipPlane,
            targetDisplay = source.targetDisplay,
            outputWidth = source.outputWidth,
            outputHeight = source.outputHeight,
            pipCorner = source.pipCorner,
            pipWidth = source.pipWidth,
            pipMargin = source.pipMargin,
            streamName = source.streamName,
            enableSpoutSender = source.enableSpoutSender,
            enableNdiSender = source.enableNdiSender
        };
    }

    private static float NonNegative(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
    }

    private static float Finite(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    private static Vector3 Finite(Vector3 value)
    {
        return new Vector3(Finite(value.x), Finite(value.y), Finite(value.z));
    }
}
