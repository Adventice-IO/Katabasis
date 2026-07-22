using System;
using Klak.Ndi;
using Klak.Spout;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(5000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[RequireComponent(typeof(UniversalAdditionalCameraData))]
public sealed class PcVrSpectatorCamera : MonoBehaviour
{
    public const int CurrentConfigurationVersion = 4;

    private static readonly int SpectatorPassId = Shader.PropertyToID("_KatabasisSpectatorPass");
    private static readonly int SpectatorPointModeId = Shader.PropertyToID("_KatabasisSpectatorPointMode");
    private static readonly int SpectatorPointSizeId = Shader.PropertyToID("_KatabasisSpectatorPointSize");
    private static readonly int SpectatorPointAlphaId = Shader.PropertyToID("_KatabasisSpectatorPointAlpha");

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
        public bool oneEuroEnabled = true;
        public float oneEuroPositionDeadZone = .01f;
        public float oneEuroRotationDeadZone = 1f;
        public float oneEuroPositionMinCutoff = .1f; 
        public float oneEuroPositionBeta = 4f;
        public float oneEuroRotationMinCutoff = .1f;
        public float oneEuroRotationBeta = 1.5f;
        public Vector3 positionOffset;
        public Vector3 rotationOffset;

        public float fieldOfView = 75f;
        public float nearClipPlane = .05f;
        public float farClipPlane = 1000f;
        public int targetDisplay;

        public KatabasisMeshConfiguration.PointRenderingMode pointRenderingMode =
            KatabasisMeshConfiguration.PointRenderingMode.Point;
        public float pointSize = 2f;
        public float pointAlpha = 1f;

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

    [Header("Recorder Output")]
    [Tooltip("Persistent output used directly by the spectator camera, Spout/NDI, picture-in-picture, and Unity Recorder.")]
    [SerializeField] private RenderTexture outputTextureAsset;

    [Header("Runtime Configuration")]
    [SerializeField] private RuntimeConfiguration configuration = new RuntimeConfiguration();

    private Camera _spectatorCamera;
    private UniversalAdditionalCameraData _additionalCameraData;
    private SpoutSender _spoutSender;
    private NdiSender _ndiSender;
    private KatabasisMeshConfiguration _pointCloudConfiguration;
    private RenderTexture _renderTexture;
    private bool _ownsRenderTexture;
    private GameObject _pipCanvasObject;
    private Canvas _pipCanvas;
    private RectTransform _pipFrame;
    private RawImage _pipImage;
    private Camera _activeSourceCamera;
    private Vector3 _positionVelocity;
    private readonly OneEuroPoseFilter _oneEuroPoseFilter = new OneEuroPoseFilter();
    private double _lastPoseFilterTime;
    private bool _sourceWarningLogged;
    private bool _hasAudiencePoseOffset;
    private Vector3 _audiencePositionOffset;
    private Quaternion _audienceRotationOffset = Quaternion.identity;
    private int _deferredOutputRefreshFrame = -1;
    private int _deferredOutputReactivateFrame = -1;
    private int _excludedCullingLayers;

    public event Action<RuntimeConfiguration> ConfigurationChanged;

    public Camera SourceCamera => _activeSourceCamera;
    public Camera SpectatorCamera => _spectatorCamera;
    public RenderTexture OutputTexture => _renderTexture;
    public RenderTexture OutputTextureAsset => outputTextureAsset;
    public bool IsEnabled => configuration != null && configuration.enabled;
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
        ApplyPointCloudRenderingConfiguration();

        if (configuration.enabled && _activeSourceCamera != null)
        {
            SnapToSource();
        }

        ScheduleDeferredOutputRefresh();
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;

        CacheComponents();
        ResolveSourceCamera();
        ApplyCameraConfiguration();
        ApplySenderConfiguration();
        ApplyPointCloudRenderingConfiguration();
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        ResetSpectatorShaderPass();
        SetOutputEnabled(false);
        _pointCloudConfiguration?.ConfigureSpectatorRendering(
            false,
            KatabasisMeshConfiguration.PointRenderingMode.Point);
    }

    private void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        ResetSpectatorShaderPass();
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
        ApplySpectatorCullingMask();
        _spectatorCamera.clearFlags = _activeSourceCamera.clearFlags;
        _spectatorCamera.backgroundColor = _activeSourceCamera.backgroundColor;

        var deltaTime = Time.unscaledDeltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        UpdateSpectatorPose(deltaTime);
    }

    private void OnValidate()
    {
        if (configuration == null)
        {
            configuration = new RuntimeConfiguration();
        }

        NormalizeConfiguration(configuration);
        SynchronizeOutputTextureAssetDescriptor();
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
        var wasOneEuroEnabled = configuration != null && configuration.oneEuroEnabled;
        configuration = CopyConfiguration(value);
        NormalizeConfiguration(configuration);

        CacheComponents();
        ResolveSourceCamera();
        ApplyCameraConfiguration();
        ApplySenderConfiguration();
        ApplyPointCloudRenderingConfiguration();

        if (wasOneEuroEnabled != configuration.oneEuroEnabled)
        {
            ResetPoseFilter();
        }

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
            ResetSpectatorShaderPass();
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

        ResetPoseFilter();
        GetFilteredSourcePose(out var sourcePosition, out var sourceRotation);
        transform.SetPositionAndRotation(
            GetTargetPosition(sourcePosition, sourceRotation),
            GetTargetRotation(sourceRotation));
        _positionVelocity = Vector3.zero;
        CacheAudiencePoseOffset();
    }

    public void SetCullingLayerExcluded(int layer, bool excluded)
    {
        if (layer < 0 || layer > 31)
        {
            return;
        }

        int layerMask = 1 << layer;
        if (excluded)
        {
            _excludedCullingLayers |= layerMask;
        }
        else
        {
            _excludedCullingLayers &= ~layerMask;
        }

        ApplySpectatorCullingMask();
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

        var pointState = configuration.pointRenderingMode
            == KatabasisMeshConfiguration.PointRenderingMode.Size
                ? $"{configuration.pointSize:F1}px points, {configuration.pointAlpha:F2} alpha"
                : $"point mode, {configuration.pointAlpha:F2} alpha";

        return $"{resolution} | {displayState} | {pointState} | {spoutState} | {ndiState}";
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

        if (value.version < 3)
        {
            value.pointRenderingMode = KatabasisMeshConfiguration.PointRenderingMode.Point;
            value.pointSize = 2f;
            value.pointAlpha = 1f;
        }

        if (value.version < 4)
        {
            value.oneEuroEnabled = true;
            value.oneEuroPositionDeadZone = .01f;
            value.oneEuroRotationDeadZone = 1f;
            value.oneEuroPositionMinCutoff = .1f;
            value.oneEuroPositionBeta = 4f;
            value.oneEuroRotationMinCutoff = .1f;
            value.oneEuroRotationBeta = 1.5f;
        }

        value.version = CurrentConfigurationVersion;
        value.positionSmoothing = NonNegative(value.positionSmoothing);
        value.rotationSmoothing = NonNegative(value.rotationSmoothing);
        value.maxPositionSpeed = Mathf.Max(.01f, NonNegative(value.maxPositionSpeed));
        value.maxRotationSpeed = Mathf.Max(.01f, NonNegative(value.maxRotationSpeed));
        value.horizonLock = Mathf.Clamp01(Finite(value.horizonLock));
        value.oneEuroPositionDeadZone = Mathf.Clamp(
            NonNegative(value.oneEuroPositionDeadZone),
            0f,
            1f);
        value.oneEuroRotationDeadZone = Mathf.Clamp(
            NonNegative(value.oneEuroRotationDeadZone),
            0f,
            45f);
        value.oneEuroPositionMinCutoff = Mathf.Clamp(
            NonNegative(value.oneEuroPositionMinCutoff),
            .001f,
            30f);
        value.oneEuroPositionBeta = Mathf.Clamp(
            NonNegative(value.oneEuroPositionBeta),
            0f,
            100f);
        value.oneEuroRotationMinCutoff = Mathf.Clamp(
            NonNegative(value.oneEuroRotationMinCutoff),
            .001f,
            30f);
        value.oneEuroRotationBeta = Mathf.Clamp(
            NonNegative(value.oneEuroRotationBeta),
            0f,
            100f);
        value.positionOffset = Finite(value.positionOffset);
        value.rotationOffset = Finite(value.rotationOffset);
        value.fieldOfView = Mathf.Clamp(Finite(value.fieldOfView), 10f, 160f);
        value.nearClipPlane = Mathf.Max(.001f, NonNegative(value.nearClipPlane));
        value.farClipPlane = Mathf.Max(value.nearClipPlane + .01f, NonNegative(value.farClipPlane));
        value.targetDisplay = Mathf.Clamp(value.targetDisplay, 0, 7);
        if (!Enum.IsDefined(typeof(KatabasisMeshConfiguration.PointRenderingMode), value.pointRenderingMode))
        {
            value.pointRenderingMode = KatabasisMeshConfiguration.PointRenderingMode.Point;
        }
        value.pointSize = Mathf.Clamp(Finite(value.pointSize), .1f, 64f);
        value.pointAlpha = Mathf.Clamp01(Finite(value.pointAlpha));
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

        if (_pointCloudConfiguration == null)
        {
            _pointCloudConfiguration = FindAnyObjectByType<KatabasisMeshConfiguration>(
                FindObjectsInactive.Include);
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
        _hasAudiencePoseOffset = false;
        ResetPoseFilter();
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

        ApplySpectatorCullingMask();
        _spectatorCamera.clearFlags = _activeSourceCamera.clearFlags;
        _spectatorCamera.backgroundColor = _activeSourceCamera.backgroundColor;
        _spectatorCamera.renderingPath = _activeSourceCamera.renderingPath;
        _spectatorCamera.allowHDR = _activeSourceCamera.allowHDR;
        _spectatorCamera.allowMSAA = _activeSourceCamera.allowMSAA;
        _spectatorCamera.allowDynamicResolution = _activeSourceCamera.allowDynamicResolution;
        _spectatorCamera.depthTextureMode = _activeSourceCamera.depthTextureMode;
        _spectatorCamera.useOcclusionCulling = _activeSourceCamera.useOcclusionCulling;
    }

    private void ApplySpectatorCullingMask()
    {
        if (_spectatorCamera == null || _activeSourceCamera == null)
        {
            return;
        }

        _spectatorCamera.cullingMask =
            _activeSourceCamera.cullingMask & ~_excludedCullingLayers;
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

    private void ApplyPointCloudRenderingConfiguration()
    {
        if (_pointCloudConfiguration == null)
        {
            _pointCloudConfiguration = FindAnyObjectByType<KatabasisMeshConfiguration>(
                FindObjectsInactive.Include);
        }

        _pointCloudConfiguration?.ConfigureSpectatorRendering(
            configuration.enabled,
            configuration.pointRenderingMode);
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

        if (outputTextureAsset != null)
        {
            var outputChanged = false;
            if (_renderTexture != outputTextureAsset)
            {
                ReleaseRenderTexture();
                _renderTexture = outputTextureAsset;
                _ownsRenderTexture = false;
                outputChanged = true;
            }

            var descriptorChanged = _renderTexture.width != configuration.outputWidth
                || _renderTexture.height != configuration.outputHeight
                || _renderTexture.depth != 24
                || _renderTexture.format != RenderTextureFormat.ARGB32;

            if (descriptorChanged || !_renderTexture.IsCreated())
            {
                DetachOutputTextureReferences();
                ConfigureRenderTexture(
                    _renderTexture,
                    configuration.outputWidth,
                    configuration.outputHeight);
                outputChanged = true;
            }

            AssignOutputTextureReferences();
            return outputChanged;
        }

        if (_renderTexture != null && !_ownsRenderTexture)
        {
            ReleaseRenderTexture();
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
        _ownsRenderTexture = true;
        AssignOutputTextureReferences();

        return true;
    }

    private void SynchronizeOutputTextureAssetDescriptor()
    {
        if (outputTextureAsset == null || configuration == null)
        {
            return;
        }

        if (outputTextureAsset.width == configuration.outputWidth
            && outputTextureAsset.height == configuration.outputHeight
            && outputTextureAsset.depth == 24
            && outputTextureAsset.format == RenderTextureFormat.ARGB32)
        {
            return;
        }

        ConfigureRenderTexture(
            outputTextureAsset,
            configuration.outputWidth,
            configuration.outputHeight);
    }

    private static void ConfigureRenderTexture(RenderTexture texture, int width, int height)
    {
        if (texture.IsCreated())
        {
            texture.Release();
        }

        texture.width = width;
        texture.height = height;
        texture.depth = 24;
        texture.format = RenderTextureFormat.ARGB32;
        texture.antiAliasing = 1;
        texture.useMipMap = false;
        texture.autoGenerateMips = false;
        texture.Create();

#if UNITY_EDITOR
        if (EditorUtility.IsPersistent(texture))
        {
            EditorUtility.SetDirty(texture);
        }
#endif
    }

    private void AssignOutputTextureReferences()
    {
        _spectatorCamera.targetTexture = _renderTexture;

        if (_pipImage != null)
        {
            _pipImage.texture = _renderTexture;
        }
    }

    private void DetachOutputTextureReferences()
    {
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

        DetachOutputTextureReferences();

        if (_renderTexture.IsCreated())
        {
            _renderTexture.Release();
        }

        if (_ownsRenderTexture)
        {
            Destroy(_renderTexture);
        }

        _renderTexture = null;
        _ownsRenderTexture = false;
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

    private void UpdateSpectatorPose(float deltaTime)
    {
        UpdatePoseFilter(deltaTime);
        GetFilteredSourcePose(out var sourcePosition, out var sourceRotation);
        var targetPosition = GetTargetPosition(sourcePosition, sourceRotation);
        var targetRotation = GetTargetRotation(sourceRotation);

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
            if ((transform.position - targetPosition).sqrMagnitude <= .00000001f
                && _positionVelocity.sqrMagnitude <= .00000001f)
            {
                transform.position = targetPosition;
                _positionVelocity = Vector3.zero;
            }
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
            if (Quaternion.Angle(transform.rotation, targetRotation) <= .001f)
            {
                transform.rotation = targetRotation;
            }
        }

        CacheAudiencePoseOffset();
    }

    private void ApplyLatestTrackedPose()
    {
        if (!configuration.enabled
            || _activeSourceCamera == null
            || !_activeSourceCamera.isActiveAndEnabled)
        {
            return;
        }

        if (!_hasAudiencePoseOffset)
        {
            SnapToSource();
            return;
        }

        UpdatePoseFilter(Time.unscaledDeltaTime);
        GetFilteredSourcePose(out var sourcePosition, out var sourceRotation);
        var targetPosition = GetTargetPosition(sourcePosition, sourceRotation);
        var targetRotation = GetTargetRotation(sourceRotation);
        transform.SetPositionAndRotation(
            targetPosition + _audiencePositionOffset,
            targetRotation * _audienceRotationOffset);
    }

    private void CacheAudiencePoseOffset()
    {
        if (_activeSourceCamera == null)
        {
            _hasAudiencePoseOffset = false;
            return;
        }

        GetFilteredSourcePose(out var sourcePosition, out var sourceRotation);
        var targetPosition = GetTargetPosition(sourcePosition, sourceRotation);
        var targetRotation = GetTargetRotation(sourceRotation);
        _audiencePositionOffset = transform.position - targetPosition;
        _audienceRotationOffset = Quaternion.Inverse(targetRotation) * transform.rotation;
        _hasAudiencePoseOffset = true;
    }

    private void ResetPoseFilter()
    {
        _lastPoseFilterTime = Time.realtimeSinceStartupAsDouble;
        if (_activeSourceCamera == null)
        {
            _oneEuroPoseFilter.Clear();
            return;
        }

        var sourceTransform = _activeSourceCamera.transform;
        _oneEuroPoseFilter.Reset(sourceTransform.position, sourceTransform.rotation);
    }

    private void UpdatePoseFilter(float fallbackDeltaTime)
    {
        if (_activeSourceCamera == null)
        {
            return;
        }

        var now = Time.realtimeSinceStartupAsDouble;
        var elapsed = (float)(now - _lastPoseFilterTime);
        var deltaTime = elapsed > 0f ? elapsed : fallbackDeltaTime;
        _lastPoseFilterTime = now;

        var sourceTransform = _activeSourceCamera.transform;
        if (!configuration.oneEuroEnabled || deltaTime <= 0f)
        {
            _oneEuroPoseFilter.Reset(sourceTransform.position, sourceTransform.rotation);
            return;
        }

        _oneEuroPoseFilter.Filter(
            sourceTransform.position,
            sourceTransform.rotation,
            deltaTime,
            configuration.oneEuroPositionMinCutoff,
            configuration.oneEuroPositionBeta,
            configuration.oneEuroRotationMinCutoff,
            configuration.oneEuroRotationBeta,
            configuration.oneEuroPositionDeadZone,
            configuration.oneEuroRotationDeadZone);
    }

    private void GetFilteredSourcePose(out Vector3 position, out Quaternion rotation)
    {
        if (configuration.oneEuroEnabled && _oneEuroPoseFilter.IsInitialized)
        {
            position = _oneEuroPoseFilter.Position;
            rotation = _oneEuroPoseFilter.Rotation;
            return;
        }

        var sourceTransform = _activeSourceCamera.transform;
        position = sourceTransform.position;
        rotation = sourceTransform.rotation;
    }

    private Vector3 GetTargetPosition(Vector3 sourcePosition, Quaternion sourceRotation)
    {
        return sourcePosition + sourceRotation * configuration.positionOffset;
    }

    private Quaternion GetTargetRotation(Quaternion sourceRotation)
    {
        var target = sourceRotation * Quaternion.Euler(configuration.rotationOffset);
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

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        var isSpectatorPass = configuration.enabled && camera == _spectatorCamera;
        _pointCloudConfiguration?.SetSpectatorPassActive(isSpectatorPass);
        if (isSpectatorPass)
        {
            ApplyLatestTrackedPose();
        }

        var commandBuffer = CommandBufferPool.Get("PC-VR point-cloud appearance");
        commandBuffer.SetGlobalFloat(SpectatorPassId, isSpectatorPass ? 1f : 0f);
        if (!isSpectatorPass)
        {
            context.ExecuteCommandBuffer(commandBuffer);
            CommandBufferPool.Release(commandBuffer);
            return;
        }

        commandBuffer.SetGlobalFloat(
            SpectatorPointModeId,
            configuration.pointRenderingMode == KatabasisMeshConfiguration.PointRenderingMode.Size
                ? 1f
                : 0f);
        commandBuffer.SetGlobalFloat(SpectatorPointSizeId, configuration.pointSize);
        commandBuffer.SetGlobalFloat(SpectatorPointAlphaId, configuration.pointAlpha);
        context.ExecuteCommandBuffer(commandBuffer);
        CommandBufferPool.Release(commandBuffer);
    }

    private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera == _spectatorCamera)
        {
            var commandBuffer = CommandBufferPool.Get("Reset PC-VR point-cloud appearance");
            commandBuffer.SetGlobalFloat(SpectatorPassId, 0f);
            context.ExecuteCommandBuffer(commandBuffer);
            CommandBufferPool.Release(commandBuffer);
            _pointCloudConfiguration?.SetSpectatorPassActive(false);
        }
    }

    private static void ResetSpectatorShaderPass()
    {
        Shader.SetGlobalFloat(SpectatorPassId, 0f);
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
            oneEuroEnabled = source.oneEuroEnabled,
            oneEuroPositionDeadZone = source.oneEuroPositionDeadZone,
            oneEuroRotationDeadZone = source.oneEuroRotationDeadZone,
            oneEuroPositionMinCutoff = source.oneEuroPositionMinCutoff,
            oneEuroPositionBeta = source.oneEuroPositionBeta,
            oneEuroRotationMinCutoff = source.oneEuroRotationMinCutoff,
            oneEuroRotationBeta = source.oneEuroRotationBeta,
            positionOffset = source.positionOffset,
            rotationOffset = source.rotationOffset,
            fieldOfView = source.fieldOfView,
            nearClipPlane = source.nearClipPlane,
            farClipPlane = source.farClipPlane,
            targetDisplay = source.targetDisplay,
            pointRenderingMode = source.pointRenderingMode,
            pointSize = source.pointSize,
            pointAlpha = source.pointAlpha,
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

    private sealed class OneEuroPoseFilter
    {
        private const float DerivativeCutoff = 1f;

        private Vector3 _lastRawPosition;
        private Vector3 _positionDeadZoneAnchor;
        private Vector3 _filteredPosition;
        private Vector3 _filteredVelocity;
        private Quaternion _lastRawRotation;
        private Quaternion _rotationDeadZoneAnchor;
        private Quaternion _filteredRotation;
        private float _filteredAngularVelocity;

        public bool IsInitialized { get; private set; }
        public Vector3 Position => _filteredPosition;
        public Quaternion Rotation => _filteredRotation;

        public void Clear()
        {
            IsInitialized = false;
        }

        public void Reset(Vector3 position, Quaternion rotation)
        {
            _lastRawPosition = position;
            _positionDeadZoneAnchor = position;
            _filteredPosition = position;
            _filteredVelocity = Vector3.zero;
            _lastRawRotation = rotation;
            _rotationDeadZoneAnchor = rotation;
            _filteredRotation = rotation;
            _filteredAngularVelocity = 0f;
            IsInitialized = true;
        }

        public void Filter(
            Vector3 position,
            Quaternion rotation,
            float deltaTime,
            float positionMinCutoff,
            float positionBeta,
            float rotationMinCutoff,
            float rotationBeta,
            float positionDeadZone,
            float rotationDeadZone)
        {
            if (!IsInitialized || deltaTime <= 0f)
            {
                Reset(position, rotation);
                return;
            }

            position = ApplyDeadZone(position, ref _positionDeadZoneAnchor, positionDeadZone);
            rotation = ApplyDeadZone(rotation, ref _rotationDeadZoneAnchor, rotationDeadZone);

            var derivativeAlpha = Alpha(deltaTime, DerivativeCutoff);

            var velocity = (position - _lastRawPosition) / deltaTime;
            _filteredVelocity = Vector3.Lerp(
                _filteredVelocity,
                velocity,
                derivativeAlpha);
            var positionCutoff = positionMinCutoff
                + positionBeta * _filteredVelocity.magnitude;
            _filteredPosition = Vector3.Lerp(
                _filteredPosition,
                position,
                Alpha(deltaTime, positionCutoff));

            var angularVelocity = Quaternion.Angle(_lastRawRotation, rotation)
                * Mathf.Deg2Rad
                / deltaTime;
            _filteredAngularVelocity = Mathf.Lerp(
                _filteredAngularVelocity,
                angularVelocity,
                derivativeAlpha);
            var rotationCutoff = rotationMinCutoff
                + rotationBeta * _filteredAngularVelocity;
            _filteredRotation = Quaternion.Slerp(
                _filteredRotation,
                rotation,
                Alpha(deltaTime, rotationCutoff));

            _lastRawPosition = position;
            _lastRawRotation = rotation;
        }

        private static Vector3 ApplyDeadZone(
            Vector3 value,
            ref Vector3 anchor,
            float radius)
        {
            var delta = value - anchor;
            var distance = delta.magnitude;
            if (distance <= radius || distance <= Mathf.Epsilon)
            {
                return anchor;
            }

            anchor += delta * ((distance - radius) / distance);
            return anchor;
        }

        private static Quaternion ApplyDeadZone(
            Quaternion value,
            ref Quaternion anchor,
            float angle)
        {
            var distance = Quaternion.Angle(anchor, value);
            if (distance <= angle || distance <= Mathf.Epsilon)
            {
                return anchor;
            }

            anchor = Quaternion.RotateTowards(anchor, value, distance - angle);
            return anchor;
        }

        private static float Alpha(float deltaTime, float cutoff)
        {
            var frequency = 2f * Mathf.PI * cutoff;
            return frequency * deltaTime / (1f + frequency * deltaTime);
        }
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
