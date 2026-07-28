using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public sealed class ImmersiveController : MonoBehaviour
{
    public const int CurrentConfigurationVersion = 2;

    public enum SetupShape
    {
        Room = 0,
        Cylinder = 1,
        Dome = 2
    }

    public enum DomeUnwrapMode
    {
        DomemasterEquidistant = 0,
        DomemasterEqualArea = 1,
        Equirectangular = 2
    }

    public enum SurfaceId
    {
        Front,
        Back,
        Left,
        Right,
        Floor,
        Ceiling
    }

    public enum ResolutionMode
    {
        Height,
        Width,
        Depth
    }

    public enum VisualMode
    {
        Default,
        DebugMaterialA,
        DebugMaterialB
    }

    public enum RoomAlignmentMode
    {
        FrontWall = 0,
        BackWall = 1,
        LeftWall = 2,
        RightWall = 3,
        RoomCenter = 6
    }

    [Serializable]
    public sealed class RuntimeConfiguration
    {
        public int version = CurrentConfigurationVersion;

        public SetupShape setupShape = SetupShape.Room;

        public float roomWidth;
        public float roomHeight;
        public float roomDepth;
        public RoomAlignmentMode roomAlignment;
        public Vector3 cameraOffsetFromAnchor;

        public float cylinderRadius = 5f;
        public float cylinderBaseHeight;
        public float cylinderPanelHeight = 3f;
        public float cylinderAngle = 180f;

        public float domeFloorRadius = 5f;
        public float domeCenterHeight = 5f;
        public DomeUnwrapMode domeUnwrapMode = DomeUnwrapMode.DomemasterEquidistant;

        public bool leftWall;
        public bool rightWall;
        public bool frontWall;
        public bool backWall;
        public bool floor;
        public bool ceiling;

        public ResolutionMode resolutionMode;
        public int desiredResolutionValue;
        public int resolutionDivider;
        public int depthBufferBits;
        public RenderTextureFormat renderTextureFormat;

        public VisualMode visualMode;
        public bool enableSpoutSender;
        public bool enableNdiSender;
    }

    [Serializable]
    private sealed class SurfaceRig
    {
        public SurfaceId id;
        public GameObject wall;
        public MeshRenderer renderer;
        public Material runtimeMaterial;
        public Camera camera;
        public RenderTexture renderTexture;
        public bool ownsRenderTexture;
        public Mesh planarPreviewMesh;
        public readonly Vector3[] cornersWorld = new Vector3[4];
        public Mesh generatedPreviewMesh;
        public int previewMeshSignature;
        public GameObject curvedCaptureRoot;
        public GameObject projectionQuad;
        public Material projectionMaterial;
        public readonly Camera[] captureCameras = new Camera[6];
        public readonly RenderTexture[] captureTextures = new RenderTexture[6];
        public int failedCaptureFaceSize;
        public int failedCaptureDepth;
        public RenderTextureFormat failedCaptureFormat;
    }

    private const string CamerasContainerName = "Cameras";
    private const string WallsContainerName = "Walls";
    private const string PreviewLayerName = "Immersive";
    private const string ProjectionLayerName = "ImmersiveProjection";
    private const string CurvedProjectionShaderName = "Immersive/CurvedProjection";
    private const string CurvedCaptureRootSuffix = "_CurvedCapture";
    private const string ProjectionQuadName = "CurvedProjectionQuad";
    private const float CurvedOutputIsolationDistance = 10000f;
    private const int MaximumCurvedCaptureFaceSize = 4096;
    private const float MinimumDimension = 0.01f;
    public const string SubtitleOverlayLayerName = "SubtitleOverlay";
    public const string AimOverlayLayerName = "AimOverlay";

    private static readonly Vector3[] CaptureDirections =
    {
        Vector3.right,
        Vector3.left,
        Vector3.up,
        Vector3.down,
        Vector3.forward,
        Vector3.back
    };

    private static readonly Vector3[] CaptureUpDirections =
    {
        Vector3.up,
        Vector3.up,
        Vector3.back,
        Vector3.forward,
        Vector3.up,
        Vector3.up
    };

    private static readonly string[] CaptureFaceNames =
    {
        "PositiveX",
        "NegativeX",
        "PositiveY",
        "NegativeY",
        "PositiveZ",
        "NegativeZ"
    };

    private static readonly string[] CaptureTextureProperties =
    {
        "_PositiveX",
        "_NegativeX",
        "_PositiveY",
        "_NegativeY",
        "_PositiveZ",
        "_NegativeZ"
    };

    private static readonly string[] CaptureMatrixProperties =
    {
        "_PositiveXVP",
        "_NegativeXVP",
        "_PositiveYVP",
        "_NegativeYVP",
        "_PositiveZVP",
        "_NegativeZVP"
    };

    private static readonly SurfaceId[] RoomSurfaces =
    {
        SurfaceId.Front,
        SurfaceId.Back,
        SurfaceId.Left,
        SurfaceId.Right,
        SurfaceId.Floor,
        SurfaceId.Ceiling
    };

    private static Mesh _builtInQuadMesh;

    [Header("References")]
    [SerializeField] private GameObject cameraPrefab;

    [Header("Immersive Setup")]
    [SerializeField] private SetupShape setupShape = SetupShape.Room;

    [Header("Room Dimensions (meters)")]
    [Min(0.01f)][SerializeField] private float roomWidth = 5f;
    [Min(0.01f)][SerializeField] private float roomHeight = 3f;
    [Min(0.01f)][SerializeField] private float roomDepth = 5f;

    [Header("Cylinder (meters / degrees)")]
    [Min(0.01f)][SerializeField] private float cylinderRadius = 5f;
    [Tooltip("Height of the lower edge relative to the shape's floor anchor.")]
    [SerializeField] private float cylinderBaseHeight;
    [Min(0.01f)][SerializeField] private float cylinderPanelHeight = 3f;
    [Range(1f, 360f)][SerializeField] private float cylinderAngle = 180f;

    [Header("Dome (meters)")]
    [Min(0.01f)][SerializeField] private float domeFloorRadius = 5f;
    [Tooltip("Height of the dome apex above its floor-center anchor.")]
    [Min(0.01f)][SerializeField] private float domeCenterHeight = 5f;
    [SerializeField] private DomeUnwrapMode domeUnwrapMode =
        DomeUnwrapMode.DomemasterEquidistant;

    [Header("Room Alignment")]
    [Tooltip("The point of the room kept fixed relative to the camera anchor when the room dimensions change.")]
    [SerializeField] private RoomAlignmentMode roomAlignment = RoomAlignmentMode.FrontWall;
    [Tooltip("Optional transform whose local Y is driven by Camera Offset From Anchor Y. Assign the XR Origin Camera Offset here to keep the room floor fixed while changing camera height. X and Z are not modified.")]
    [SerializeField] private Transform anchorTransform;
    [Tooltip("Camera position relative to the selected floor-level room anchor, in ImmersiveController local space. Y is the camera height above the floor.")]
    [SerializeField] private Vector3 cameraOffsetFromAnchor;

    [Header("Enabled Surfaces")]
    [SerializeField] private bool leftWall = true;
    [SerializeField] private bool rightWall = true;
    [SerializeField] private bool frontWall = true;
    [SerializeField] private bool backWall = true;
    [SerializeField] private bool floor = true;
    [SerializeField] private bool ceiling = true;

    [Header("Resolution Settings")]
    [SerializeField] private ResolutionMode resolutionMode = ResolutionMode.Height;
    [Min(16)][SerializeField] private int desiredResolutionValue = 1080;
    [Min(16)][SerializeField] private int resolutionHeight = 1080;
    [Min(16)][SerializeField] private int resolutionWidth = 1920;
    [Min(16)][SerializeField] private int resolutionDepth = 1080;
    [SerializeField] private int depthBufferBits = 24;
    [SerializeField] private RenderTextureFormat renderTextureFormat = RenderTextureFormat.ARGB32;
    [Range(1, 4)]
    [SerializeField] private int resolutionDivider = 1;

    [Header("Visual Settings")]
    [SerializeField] private VisualMode visualMode = VisualMode.Default;
    [SerializeField] private Material debugMaterialA;
    [SerializeField] private Material debugMaterialB;

    [Header("Outputs")]
    [SerializeField] private bool enableSpoutSender = true;
    [SerializeField] private bool enableNdiSender = true;

    [Header("Recorder Render Texture Assets")]
    [Tooltip("Persistent assets used directly by the surface cameras, Spout/NDI, and Unity Recorder. Their descriptors are kept synchronized with the room setup.")]
    [SerializeField] private RenderTexture leftRenderTextureAsset;
    [SerializeField] private RenderTexture rightRenderTextureAsset;
    [SerializeField] private RenderTexture frontRenderTextureAsset;
    [SerializeField] private RenderTexture backRenderTextureAsset;
    [SerializeField] private RenderTexture floorRenderTextureAsset;
    [SerializeField] private RenderTexture ceilingRenderTextureAsset;

    [Header("Runtime Configuration")]
    [SerializeField] private bool loadSavedConfigurationOnStart = true;
    [SerializeField] private bool autosaveRuntimeChanges = true;
    [Min(0f)][SerializeField] private float autosaveDelay = 0.5f;
    [SerializeField] private string configurationFileName = "immersive-config.json";

    [Header("Generated Render Textures (Runtime Read-only)")]
    [NonSerialized] public RenderTexture leftRT;
    [NonSerialized] public RenderTexture rightRT;
    [NonSerialized] public RenderTexture frontRT;
    [NonSerialized] public RenderTexture backRT;
    [NonSerialized] public RenderTexture floorRT;
    [NonSerialized] public RenderTexture ceilingRT;

    private readonly Dictionary<SurfaceId, SurfaceRig> _rigs = new Dictionary<SurfaceId, SurfaceRig>(6);
    private Transform _camerasContainer;
    private Transform _wallsContainer;
    private int _previewLayer = -1;
    private int _projectionLayer = -1;
    private int _subtitleOverlayLayer = -1;
    private int _aimOverlayLayer = -1;
    private bool _requiresSync = true;
    private bool _outputEnabled = true;
    private bool _cameraOffsetEnabled = true;
    private bool _autosavePending;
    private float _autosaveAt;
    private bool _projectionShaderWarningLogged;
    private bool _projectionLayerWarningLogged;
    private bool _captureTextureWarningLogged;
#if UNITY_EDITOR
    private static bool _editorAssemblyReloading;
#endif

    public event Action<RuntimeConfiguration> ConfigurationChanged;

    public bool AutosaveRuntimeChanges => autosaveRuntimeChanges;
    public bool IsSpoutSupported => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11;
    public string ConfigurationDirectory => Path.Combine(Application.persistentDataPath, "ImmersiveController");
    public string DefaultConfigurationPath => Path.Combine(ConfigurationDirectory, GetSafeConfigurationFileName());
    public RenderTexture LeftRenderTexture => leftRT;
    public RenderTexture RightRenderTexture => rightRT;
    public RenderTexture FrontRenderTexture => frontRT;
    public RenderTexture BackRenderTexture => backRT;
    public RenderTexture FloorRenderTexture => floorRT;
    public RenderTexture CeilingRenderTexture => ceilingRT;
    public int SubtitleOverlayLayer => _subtitleOverlayLayer;
    public int AimOverlayLayer => _aimOverlayLayer;
    public bool OutputEnabled => _outputEnabled;
    public bool CameraOffsetEnabled => _cameraOffsetEnabled;
    public SetupShape CurrentSetupShape => setupShape;

    public bool TryGetSetupWarning(out string message)
    {
        message = null;
        if (setupShape == SetupShape.Room)
        {
            return false;
        }

        var eye = GetEffectiveCameraOffsetFromAnchor();
        if (setupShape == SetupShape.Cylinder)
        {
            var radialDistance = new Vector2(eye.x, eye.z).magnitude;
            if (radialDistance >= cylinderRadius)
            {
                message =
                    "Camera X/Z offset is outside the cylinder radius; "
                    + "the curved projection can overlap itself.";
                return true;
            }
        }
        else
        {
            GetDomeSphere(out _, out var sphereCenterY, out _);
            var domeImplicit =
                eye.sqrMagnitude
                - 2f * sphereCenterY * eye.y
                - domeFloorRadius * domeFloorRadius;
            if (eye.y < 0f || domeImplicit >= 0f)
            {
                message =
                    "Camera offset is outside the dome volume; "
                    + "the curved projection can overlap itself.";
                return true;
            }
        }

        if (frontRT != null)
        {
            var requestedFaceSize = GetRequestedCurvedCaptureFaceSize(frontRT);
            var maximumFaceSize = GetMaximumCurvedCaptureFaceSize();
            if (requestedFaceSize > maximumFaceSize)
            {
                message =
                    $"Curved capture faces are capped at {maximumFaceSize}px "
                    + $"(requested {requestedFaceSize}px); reduce output resolution "
                    + "or the setup radius for full sampling density.";
                return true;
            }
        }

        return false;
    }

    public bool TryGetSurfaceCamera(SurfaceId surface, out Camera surfaceCamera)
    {
        if (!_outputEnabled)
        {
            surfaceCamera = null;
            return false;
        }

        ProcessPendingChanges();
        if (setupShape != SetupShape.Room)
        {
            surface = SurfaceId.Front;
        }

        if (_rigs.TryGetValue(surface, out var rig) && rig != null && rig.camera != null)
        {
            surfaceCamera = rig.camera;
            return true;
        }

        surfaceCamera = null;
        return false;
    }

    public bool TryProjectWorldRayToOutput(
        Vector3 originWorld,
        Vector3 directionWorld,
        out SurfaceId surface,
        out Camera outputCamera,
        out Vector3 viewportPosition)
    {
        surface = SurfaceId.Front;
        outputCamera = null;
        viewportPosition = default;

        if (!_outputEnabled || directionWorld.sqrMagnitude <= Mathf.Epsilon)
        {
            return false;
        }

        ProcessPendingChanges();
        if (setupShape == SetupShape.Room)
        {
            return TryProjectRoomRay(
                originWorld,
                directionWorld,
                out surface,
                out outputCamera,
                out viewportPosition);
        }

        if (!_rigs.TryGetValue(SurfaceId.Front, out var rig)
            || rig?.camera == null
            || rig.wall == null
            || !rig.camera.isActiveAndEnabled)
        {
            return false;
        }

        var shapeTransform = rig.wall.transform;
        var origin = shapeTransform.InverseTransformPoint(originWorld);
        var direction = shapeTransform.worldToLocalMatrix
            .MultiplyVector(directionWorld)
            .normalized;
        Vector2 uv;
        float distance;

        var hit = setupShape == SetupShape.Cylinder
            ? TryIntersectCylinder(origin, direction, out uv, out distance)
            : TryIntersectDome(origin, direction, out uv, out distance);
        if (!hit)
        {
            return false;
        }

        outputCamera = rig.camera;
        viewportPosition = new Vector3(uv.x, uv.y, distance);
        return true;
    }

    public RenderTexture GetRenderTextureAsset(SurfaceId surface)
    {
        switch (surface)
        {
            case SurfaceId.Left:
                return leftRenderTextureAsset;
            case SurfaceId.Right:
                return rightRenderTextureAsset;
            case SurfaceId.Front:
                return frontRenderTextureAsset;
            case SurfaceId.Back:
                return backRenderTextureAsset;
            case SurfaceId.Floor:
                return floorRenderTextureAsset;
            case SurfaceId.Ceiling:
                return ceilingRenderTextureAsset;
            default:
                return null;
        }
    }

    public void UseExternalConfigurationPersistence()
    {
        loadSavedConfigurationOnStart = false;
        autosaveRuntimeChanges = false;
        _autosavePending = false;
    }

    public void SetOutputEnabled(bool enabled)
    {
        _outputEnabled = enabled;
        EnsureContainers();

        if (_camerasContainer != null)
        {
            _camerasContainer.gameObject.SetActive(enabled);
        }

        if (_wallsContainer != null)
        {
            _wallsContainer.gameObject.SetActive(enabled);
        }

        foreach (var pair in _rigs)
        {
            var rig = pair.Value;
            if (rig?.camera != null)
            {
                rig.camera.enabled = enabled;
            }

            if (rig != null)
            {
                UpdateSenderState(rig);
            }
        }
    }

    public void SetCameraOffsetEnabled(bool enabled)
    {
        if (_cameraOffsetEnabled == enabled)
        {
            return;
        }

        _cameraOffsetEnabled = enabled;
        UpdateAnchorTransformHeight();
        UpdateRigs();
    }

    private void Awake()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (loadSavedConfigurationOnStart)
        {
            LoadDefaultConfiguration(false, false, out _);
        }
    }

    private void OnEnable()
    {
#if UNITY_EDITOR
        _editorAssemblyReloading = false;
        AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
#endif
        _previewLayer = GetOrCreatePreviewLayer();
        _projectionLayer = GetOrCreateLayer(ProjectionLayerName);
        _subtitleOverlayLayer = GetOrCreateLayer(SubtitleOverlayLayerName);
        _aimOverlayLayer = GetOrCreateLayer(AimOverlayLayerName);
        EnsureContainers();
        _requiresSync = true;
        ProcessPendingChanges();
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.delayCall -= DelayedProcessPendingChanges;
        AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
#endif

        if (Application.isPlaying && _autosavePending)
        {
            SaveDefaultConfiguration(out _);
        }

#if UNITY_EDITOR
        // Native scene objects survive a managed assembly reload. Keeping the
        // generated rig intact lets OnEnable rebind it instead of dirtying the
        // scene with a fresh set of object IDs after every script compilation.
        if (!Application.isPlaying && _editorAssemblyReloading)
        {
            ReleaseTransientResourcesForAssemblyReload();
            return;
        }
#endif

        if (_camerasContainer != null)
        {
            _camerasContainer.gameObject.SetActive(false);
        }

        if (_wallsContainer != null)
        {
            _wallsContainer.gameObject.SetActive(false);
        }

        ReleaseAllResources();
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR
        EditorApplication.delayCall -= DelayedProcessPendingChanges;
        AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        if (!Application.isPlaying && _editorAssemblyReloading)
        {
            ReleaseTransientResourcesForAssemblyReload();
            return;
        }
#endif

        ReleaseAllResources();
    }

    private void OnApplicationQuit()
    {
        if (_autosavePending)
        {
            SaveDefaultConfiguration(out _);
        }
    }

    private void OnValidate()
    {
        if (!Enum.IsDefined(typeof(SetupShape), setupShape))
        {
            setupShape = SetupShape.Room;
        }

        if (!Enum.IsDefined(typeof(DomeUnwrapMode), domeUnwrapMode))
        {
            domeUnwrapMode = DomeUnwrapMode.DomemasterEquidistant;
        }

        roomWidth = PositiveFinite(roomWidth, 5f);
        roomHeight = PositiveFinite(roomHeight, 3f);
        roomDepth = PositiveFinite(roomDepth, 5f);
        cylinderRadius = PositiveFinite(cylinderRadius, 5f);
        cylinderBaseHeight = FiniteOr(cylinderBaseHeight, 0f);
        cylinderPanelHeight = PositiveFinite(cylinderPanelHeight, 3f);
        cylinderAngle = Mathf.Clamp(FiniteOr(cylinderAngle, 180f), 1f, 360f);
        domeFloorRadius = PositiveFinite(domeFloorRadius, 5f);
        domeCenterHeight = PositiveFinite(domeCenterHeight, 5f);
        cameraOffsetFromAnchor = new Vector3(
            FiniteOr(cameraOffsetFromAnchor.x, 0f),
            FiniteOr(cameraOffsetFromAnchor.y, 0f),
            FiniteOr(cameraOffsetFromAnchor.z, 0f));
        desiredResolutionValue = Mathf.Max(16, desiredResolutionValue);
        resolutionHeight = Mathf.Max(16, resolutionHeight);
        resolutionWidth = Mathf.Max(16, resolutionWidth);
        resolutionDepth = Mathf.Max(16, resolutionDepth);
        resolutionDivider = Mathf.Clamp(resolutionDivider, 1, 4);
        autosaveDelay = Mathf.Max(0f, autosaveDelay);
        depthBufferBits = NormalizeDepthBufferBits(depthBufferBits);

        NormalizeResolutionInputs();

        if (!isActiveAndEnabled)
        {
            return;
        }

        EnsureContainers();
        _requiresSync = true;

#if UNITY_EDITOR
        EditorApplication.delayCall -= DelayedProcessPendingChanges;
        EditorApplication.delayCall += DelayedProcessPendingChanges;
#endif
    }

#if UNITY_EDITOR
    private static void OnBeforeAssemblyReload()
    {
        _editorAssemblyReloading = true;
    }

    private void ReleaseTransientResourcesForAssemblyReload()
    {
        foreach (var pair in _rigs)
        {
            var rig = pair.Value;
            if (rig == null)
            {
                continue;
            }

            ReleaseCurvedResources(rig);
            if (rig.generatedPreviewMesh == null)
            {
                continue;
            }

            var meshFilter = rig.wall != null
                ? rig.wall.GetComponent<MeshFilter>()
                : null;
            if (meshFilter != null
                && meshFilter.sharedMesh == rig.generatedPreviewMesh)
            {
                meshFilter.sharedMesh =
                    rig.planarPreviewMesh != null
                        ? rig.planarPreviewMesh
                        : GetBuiltInQuadMesh();
            }

            SafeDestroy(rig.generatedPreviewMesh);
            rig.generatedPreviewMesh = null;
        }
    }

    private void DelayedProcessPendingChanges()
    {
        if (!this)
        {
            return;
        }

        ProcessPendingChanges();
    }
#endif

    private void Update()
    {
        UpdateAnchorTransformHeight();
        ProcessPendingChanges();
        UpdateRigs();
        ProcessAutosave();
    }

    public RuntimeConfiguration CaptureConfiguration()
    {
        return new RuntimeConfiguration
        {
            version = CurrentConfigurationVersion,
            setupShape = setupShape,
            roomWidth = roomWidth,
            roomHeight = roomHeight,
            roomDepth = roomDepth,
            roomAlignment = roomAlignment,
            cameraOffsetFromAnchor = cameraOffsetFromAnchor,
            cylinderRadius = cylinderRadius,
            cylinderBaseHeight = cylinderBaseHeight,
            cylinderPanelHeight = cylinderPanelHeight,
            cylinderAngle = cylinderAngle,
            domeFloorRadius = domeFloorRadius,
            domeCenterHeight = domeCenterHeight,
            domeUnwrapMode = domeUnwrapMode,
            leftWall = leftWall,
            rightWall = rightWall,
            frontWall = frontWall,
            backWall = backWall,
            floor = floor,
            ceiling = ceiling,
            resolutionMode = resolutionMode,
            desiredResolutionValue = desiredResolutionValue,
            resolutionDivider = resolutionDivider,
            depthBufferBits = depthBufferBits,
            renderTextureFormat = renderTextureFormat,
            visualMode = visualMode,
            enableSpoutSender = enableSpoutSender,
            enableNdiSender = enableNdiSender
        };
    }

    public void ApplyConfiguration(RuntimeConfiguration configuration, bool requestAutosave = true)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        NormalizeConfiguration(configuration);

        var surfaceTopologyChanged = setupShape != configuration.setupShape
            || leftWall != configuration.leftWall
            || rightWall != configuration.rightWall
            || frontWall != configuration.frontWall
            || backWall != configuration.backWall
            || floor != configuration.floor
            || ceiling != configuration.ceiling;

        setupShape = configuration.setupShape;
        roomWidth = configuration.roomWidth;
        roomHeight = configuration.roomHeight;
        roomDepth = configuration.roomDepth;
        roomAlignment = configuration.roomAlignment;
        cameraOffsetFromAnchor = configuration.cameraOffsetFromAnchor;
        cylinderRadius = configuration.cylinderRadius;
        cylinderBaseHeight = configuration.cylinderBaseHeight;
        cylinderPanelHeight = configuration.cylinderPanelHeight;
        cylinderAngle = configuration.cylinderAngle;
        domeFloorRadius = configuration.domeFloorRadius;
        domeCenterHeight = configuration.domeCenterHeight;
        domeUnwrapMode = configuration.domeUnwrapMode;

        leftWall = configuration.leftWall;
        rightWall = configuration.rightWall;
        frontWall = configuration.frontWall;
        backWall = configuration.backWall;
        floor = configuration.floor;
        ceiling = configuration.ceiling;

        resolutionMode = configuration.resolutionMode;
        desiredResolutionValue = configuration.desiredResolutionValue;
        resolutionDivider = configuration.resolutionDivider;
        depthBufferBits = configuration.depthBufferBits;
        renderTextureFormat = configuration.renderTextureFormat;

        visualMode = configuration.visualMode;
        enableSpoutSender = configuration.enableSpoutSender;
        enableNdiSender = configuration.enableNdiSender;

        NormalizeResolutionInputs();
        _requiresSync |= surfaceTopologyChanged;

        if (isActiveAndEnabled)
        {
            ProcessPendingChanges();
            UpdateAnchorTransformHeight();
            UpdateRigs();
        }

        if (requestAutosave)
        {
            QueueAutosave();
        }

        ConfigurationChanged?.Invoke(CaptureConfiguration());
    }

    public string GetConfigurationJson(bool prettyPrint = true)
    {
        return JsonUtility.ToJson(CaptureConfiguration(), prettyPrint);
    }

    public bool ApplyConfigurationJson(string json, bool requestAutosave, out string message)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            message = "The configuration JSON is empty.";
            return false;
        }

        try
        {
            var configuration = JsonUtility.FromJson<RuntimeConfiguration>(json);
            if (configuration == null)
            {
                message = "The configuration JSON could not be read.";
                return false;
            }

            if (configuration.version > CurrentConfigurationVersion)
            {
                message = $"Configuration version {configuration.version} is newer than supported version {CurrentConfigurationVersion}.";
                return false;
            }

            ApplyConfiguration(configuration, requestAutosave);
            message = "Configuration applied.";
            return true;
        }
        catch (Exception exception)
        {
            message = "Could not apply configuration: " + exception.Message;
            return false;
        }
    }

    public bool SaveDefaultConfiguration(out string message)
    {
        var saved = ExportConfiguration(DefaultConfigurationPath, out message);
        if (saved)
        {
            _autosavePending = false;
        }

        return saved;
    }

    public bool ReloadDefaultConfiguration(out string message)
    {
        return LoadDefaultConfiguration(false, true, out message);
    }

    public bool ExportConfiguration(string path, out string message)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            message = "Choose a configuration file path.";
            return false;
        }

        try
        {
            path = EnsureJsonExtension(Path.GetFullPath(path));
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, GetConfigurationJson(true));
            message = "Configuration exported to " + path;
            return true;
        }
        catch (Exception exception)
        {
            message = "Could not export configuration: " + exception.Message;
            return false;
        }
    }

    public bool ImportConfiguration(string path, out string message)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            message = "Choose a configuration file path.";
            return false;
        }

        try
        {
            path = Path.GetFullPath(path);
            if (!File.Exists(path))
            {
                message = "Configuration file not found: " + path;
                return false;
            }

            if (!ApplyConfigurationJson(File.ReadAllText(path), false, out message))
            {
                return false;
            }

            if (!SaveDefaultConfiguration(out var saveMessage))
            {
                message = "Configuration imported, but autosave failed. " + saveMessage;
                return true;
            }

            message = "Configuration imported from " + path;
            return true;
        }
        catch (Exception exception)
        {
            message = "Could not import configuration: " + exception.Message;
            return false;
        }
    }

    public string GetRenderTextureSummary()
    {
        if (setupShape == SetupShape.Cylinder)
        {
            return GetRenderTextureLabel("Cylinder", frontRT);
        }

        if (setupShape == SetupShape.Dome)
        {
            return GetRenderTextureLabel("Dome", frontRT);
        }

        return string.Join("  |  ",
            GetRenderTextureLabel("L", leftRT),
            GetRenderTextureLabel("R", rightRT),
            GetRenderTextureLabel("F", frontRT),
            GetRenderTextureLabel("B", backRT),
            GetRenderTextureLabel("Floor", floorRT),
            GetRenderTextureLabel("Ceil", ceilingRT));
    }

    private static string GetRenderTextureLabel(string label, RenderTexture texture)
    {
        return texture == null ? label + " off" : $"{label} {texture.width}x{texture.height}";
    }

    private void NormalizeConfiguration(RuntimeConfiguration configuration)
    {
        var sourceVersion = configuration.version;
        if (sourceVersion < 2)
        {
            configuration.setupShape = SetupShape.Room;
            configuration.cylinderRadius = 5f;
            configuration.cylinderBaseHeight = 0f;
            configuration.cylinderPanelHeight = 3f;
            configuration.cylinderAngle = 180f;
            configuration.domeFloorRadius = 5f;
            configuration.domeCenterHeight = 5f;
            configuration.domeUnwrapMode = DomeUnwrapMode.DomemasterEquidistant;
        }

        configuration.version = CurrentConfigurationVersion;
        configuration.roomWidth = PositiveFinite(configuration.roomWidth, 5f);
        configuration.roomHeight = PositiveFinite(configuration.roomHeight, 3f);
        configuration.roomDepth = PositiveFinite(configuration.roomDepth, 5f);
        configuration.cameraOffsetFromAnchor = new Vector3(
            FiniteOr(configuration.cameraOffsetFromAnchor.x, 0f),
            FiniteOr(configuration.cameraOffsetFromAnchor.y, 0f),
            FiniteOr(configuration.cameraOffsetFromAnchor.z, 0f));
        configuration.cylinderRadius = PositiveFinite(configuration.cylinderRadius, 5f);
        configuration.cylinderBaseHeight = FiniteOr(configuration.cylinderBaseHeight, 0f);
        configuration.cylinderPanelHeight =
            PositiveFinite(configuration.cylinderPanelHeight, 3f);
        configuration.cylinderAngle = Mathf.Clamp(
            FiniteOr(configuration.cylinderAngle, 180f),
            1f,
            360f);
        configuration.domeFloorRadius = PositiveFinite(configuration.domeFloorRadius, 5f);
        configuration.domeCenterHeight = PositiveFinite(configuration.domeCenterHeight, 5f);
        configuration.desiredResolutionValue = Mathf.Max(16, configuration.desiredResolutionValue);
        configuration.resolutionDivider = Mathf.Clamp(configuration.resolutionDivider, 1, 4);
        configuration.depthBufferBits = NormalizeDepthBufferBits(configuration.depthBufferBits);

        if (!Enum.IsDefined(typeof(SetupShape), configuration.setupShape))
        {
            configuration.setupShape = SetupShape.Room;
        }

        if (!Enum.IsDefined(typeof(DomeUnwrapMode), configuration.domeUnwrapMode))
        {
            configuration.domeUnwrapMode = DomeUnwrapMode.DomemasterEquidistant;
        }

        if (!Enum.IsDefined(typeof(RoomAlignmentMode), configuration.roomAlignment))
        {
            configuration.roomAlignment = RoomAlignmentMode.FrontWall;
        }

        if (!Enum.IsDefined(typeof(ResolutionMode), configuration.resolutionMode))
        {
            configuration.resolutionMode = ResolutionMode.Height;
        }

        if (!Enum.IsDefined(typeof(VisualMode), configuration.visualMode))
        {
            configuration.visualMode = VisualMode.Default;
        }

        if (!Enum.IsDefined(typeof(RenderTextureFormat), configuration.renderTextureFormat)
            || !SystemInfo.SupportsRenderTextureFormat(configuration.renderTextureFormat))
        {
            configuration.renderTextureFormat = RenderTextureFormat.ARGB32;
        }
    }

    private static float PositiveFinite(float value, float fallback)
    {
        return Mathf.Max(MinimumDimension, FiniteOr(value, fallback));
    }

    private static float FiniteOr(float value, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }

    private static int NormalizeDepthBufferBits(int value)
    {
        if (value <= 0)
        {
            return 0;
        }

        if (value <= 16)
        {
            return 16;
        }

        return 24;
    }

    private void QueueAutosave()
    {
        if (!Application.isPlaying || !autosaveRuntimeChanges)
        {
            return;
        }

        _autosavePending = true;
        _autosaveAt = Time.unscaledTime + autosaveDelay;
    }

    private void ProcessAutosave()
    {
        if (!_autosavePending || Time.unscaledTime < _autosaveAt)
        {
            return;
        }

        if (!SaveDefaultConfiguration(out var message))
        {
            _autosavePending = false;
            Debug.LogWarning(message, this);
        }
    }

    private bool LoadDefaultConfiguration(bool requestAutosave, bool logErrors, out string message)
    {
        if (!File.Exists(DefaultConfigurationPath))
        {
            message = "No saved configuration exists yet. Scene defaults are active.";
            return false;
        }

        try
        {
            var loaded = ApplyConfigurationJson(File.ReadAllText(DefaultConfigurationPath), requestAutosave, out message);
            if (!loaded && logErrors)
            {
                Debug.LogWarning(message, this);
            }

            if (loaded)
            {
                message = "Reloaded " + DefaultConfigurationPath;
            }

            return loaded;
        }
        catch (Exception exception)
        {
            message = "Could not load saved configuration: " + exception.Message;
            if (logErrors)
            {
                Debug.LogWarning(message, this);
            }

            return false;
        }
    }

    private string GetSafeConfigurationFileName()
    {
        var fileName = Path.GetFileName(configurationFileName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "immersive-config.json";
        }

        return EnsureJsonExtension(fileName);
    }

    private static string EnsureJsonExtension(string path)
    {
        return string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)
            ? path
            : path + ".json";
    }

    private void UpdateAnchorTransformHeight()
    {
        if (anchorTransform == null)
        {
            return;
        }

        var localPosition = anchorTransform.localPosition;
        var effectiveCameraOffset = GetEffectiveCameraOffsetFromAnchor();
        if (Mathf.Approximately(localPosition.y, effectiveCameraOffset.y))
        {
            return;
        }

        localPosition.y = effectiveCameraOffset.y;
        anchorTransform.localPosition = localPosition;
    }

    private void ProcessPendingChanges()
    {
        if (!this)
        {
            return;
        }

        if (!isActiveAndEnabled)
        {
            return;
        }

        EnsureContainers();
        CleanupDuplicateGeneratedChildren();

        if (_requiresSync)
        {
            SyncRigs();
            _requiresSync = false;
        }
    }

    private void EnsureContainers()
    {
        if (_camerasContainer == null)
        {
            _camerasContainer = FindOrCreateContainer(CamerasContainerName);
        }

        if (_wallsContainer == null)
        {
            _wallsContainer = FindOrCreateContainer(WallsContainerName);
        }

        _camerasContainer.gameObject.SetActive(_outputEnabled);
        _wallsContainer.gameObject.SetActive(_outputEnabled);
    }

    private Transform FindOrCreateContainer(string containerName)
    {
        var child = transform.Find(containerName);
        if (child != null)
        {
            return child;
        }

        var go = new GameObject(containerName);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    private void SyncRigs()
    {
        var room = setupShape == SetupShape.Room;
        SyncSingleRig(SurfaceId.Front, room ? frontWall : true);
        SyncSingleRig(SurfaceId.Back, room && backWall);
        SyncSingleRig(SurfaceId.Left, room && leftWall);
        SyncSingleRig(SurfaceId.Right, room && rightWall);
        SyncSingleRig(SurfaceId.Floor, room && floor);
        SyncSingleRig(SurfaceId.Ceiling, room && ceiling);
    }

    private void SyncSingleRig(SurfaceId id, bool shouldExist)
    {
        _rigs.TryGetValue(id, out var rig);

        if (shouldExist)
        {
            if (rig == null)
            {
                rig = TryRebindExistingRig(id) ?? CreateRig(id);
                _rigs[id] = rig;
            }
            return;
        }

        if (rig != null)
        {
            DestroyRig(rig);
            _rigs.Remove(id);
        }
        else
        {
            DestroyExistingRigObjects(id);
        }

        SetRenderTextureOutput(id, null);
    }

    private SurfaceRig TryRebindExistingRig(SurfaceId id)
    {
        if (_wallsContainer == null || _camerasContainer == null)
        {
            return null;
        }

        var wall = FindFirstChildByExactName(_wallsContainer, id + "_Wall");
        var cameraTransform = FindFirstChildByExactName(_camerasContainer, id + "_Camera");

        if (wall == null || cameraTransform == null)
        {
            return null;
        }

        var camera = cameraTransform.GetComponent<Camera>();
        if (camera == null)
        {
            camera = cameraTransform.gameObject.AddComponent<Camera>();
        }

        var renderer = wall.GetComponent<MeshRenderer>();
        if (renderer == null)
        {
            return null;
        }

        var meshFilter = wall.GetComponent<MeshFilter>();
        var existingPreviewMesh = meshFilter != null ? meshFilter.sharedMesh : null;
        var generatedPreviewMesh = IsGeneratedPreviewMesh(existingPreviewMesh)
            ? existingPreviewMesh
            : null;
        var rig = new SurfaceRig
        {
            id = id,
            wall = wall.gameObject,
            renderer = renderer,
            camera = camera,
            renderTexture = camera.targetTexture as RenderTexture,
            planarPreviewMesh = generatedPreviewMesh == null
                ? existingPreviewMesh
                : null,
            generatedPreviewMesh = generatedPreviewMesh
        };

        if (generatedPreviewMesh != null)
        {
            rig.previewMeshSignature = IsPreviewMeshForCurrentSetup(generatedPreviewMesh)
                ? GetPreviewMeshSignature()
                : int.MinValue;
        }

        rig.runtimeMaterial = renderer.sharedMaterial;
        return rig;
    }

    private void DestroyExistingRigObjects(SurfaceId id)
    {
        DestroyAllChildrenByExactName(_wallsContainer, id + "_Wall");
        DestroyAllChildrenByExactName(_camerasContainer, id + "_Camera");
        DestroyAllChildrenByExactName(_camerasContainer, id + CurvedCaptureRootSuffix);
    }

    private void CleanupDuplicateGeneratedChildren()
    {
        CleanupDuplicatesForSurface(SurfaceId.Front);
        CleanupDuplicatesForSurface(SurfaceId.Back);
        CleanupDuplicatesForSurface(SurfaceId.Left);
        CleanupDuplicatesForSurface(SurfaceId.Right);
        CleanupDuplicatesForSurface(SurfaceId.Floor);
        CleanupDuplicatesForSurface(SurfaceId.Ceiling);
    }

    private void CleanupDuplicatesForSurface(SurfaceId id)
    {
        KeepOnlyFirstChildByExactName(_wallsContainer, id + "_Wall");
        KeepOnlyFirstChildByExactName(_camerasContainer, id + "_Camera");
        KeepOnlyFirstChildByExactName(_camerasContainer, id + CurvedCaptureRootSuffix);
    }

    private static Transform FindFirstChildByExactName(Transform parent, string name)
    {
        if (parent == null)
        {
            return null;
        }

        for (var i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (string.Equals(child.name, name, StringComparison.Ordinal))
            {
                return child;
            }
        }

        return null;
    }

    private static void KeepOnlyFirstChildByExactName(Transform parent, string name)
    {
        if (parent == null)
        {
            return;
        }

        Transform first = null;

        for (var i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (!string.Equals(child.name, name, StringComparison.Ordinal))
            {
                continue;
            }

            if (first == null)
            {
                first = child;
                continue;
            }

            SafeDestroy(child.gameObject);
        }
    }

    private static void DestroyAllChildrenByExactName(Transform parent, string name)
    {
        if (parent == null)
        {
            return;
        }

        for (var i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (string.Equals(child.name, name, StringComparison.Ordinal))
            {
                SafeDestroy(child.gameObject);
            }
        }
    }

    private SurfaceRig CreateRig(SurfaceId id)
    {
        var rig = new SurfaceRig { id = id };

        rig.wall = GameObject.CreatePrimitive(PrimitiveType.Quad);
        rig.wall.name = id + "_Wall";
        rig.wall.transform.SetParent(_wallsContainer, false);

        var collider = rig.wall.GetComponent<Collider>();
        if (collider != null)
        {
            SafeDestroy(collider);
        }

        rig.renderer = rig.wall.GetComponent<MeshRenderer>();
        rig.planarPreviewMesh = rig.wall.GetComponent<MeshFilter>()?.sharedMesh;
        rig.runtimeMaterial = new Material(Shader.Find("Unlit/Texture"))
        {
            name = id + "_WallRuntimeMat"
        };
        rig.renderer.sharedMaterial = rig.runtimeMaterial;

        if (cameraPrefab != null)
        {
            var camGo = Instantiate(cameraPrefab, _camerasContainer);
            camGo.name = id + "_Camera";
            rig.camera = camGo.GetComponent<Camera>();
            if (rig.camera == null)
            {
                rig.camera = camGo.AddComponent<Camera>();
            }
        }
        else
        {
            var camGo = new GameObject(id + "_Camera");
            camGo.transform.SetParent(_camerasContainer, false);
            rig.camera = camGo.AddComponent<Camera>();
        }

        rig.camera.enabled = _outputEnabled;
        return rig;
    }

    private void UpdateRigs()
    {
        if (_camerasContainer == null)
        {
            return;
        }

        if (!_outputEnabled)
        {
            return;
        }

        if (_previewLayer < 0)
        {
            _previewLayer = GetOrCreatePreviewLayer();
        }

        if (_projectionLayer < 0)
        {
            _projectionLayer = GetOrCreateLayer(ProjectionLayerName);
        }

        if (_subtitleOverlayLayer < 0)
        {
            _subtitleOverlayLayer = GetOrCreateLayer(SubtitleOverlayLayerName);
        }

        if (_aimOverlayLayer < 0)
        {
            _aimOverlayLayer = GetOrCreateLayer(AimOverlayLayerName);
        }

        var eye = _camerasContainer.position;

        foreach (var pair in _rigs)
        {
            var rig = pair.Value;
            if (rig.wall != null)
            {
                rig.wall.SetActive(_outputEnabled);
            }

            if (rig.camera != null)
            {
                rig.camera.enabled = _outputEnabled;
            }

            ResetSurfaceCamera(rig.camera);
            UpdatePreviewLayer(rig);
            UpdateSurfaceGeometry(rig);
            UpdateRenderTexture(rig);
            UpdateWallMaterial(rig);
            if (setupShape == SetupShape.Room)
            {
                ReleaseCurvedResources(rig);
                UpdateCameraProjection(rig, eye);
            }
            else
            {
                UpdateCurvedProjection(rig, eye);
            }

            UpdateSenderState(rig);
        }
    }

    private void UpdateSurfaceGeometry(SurfaceRig rig)
    {
        if (setupShape != SetupShape.Room)
        {
            UpdateCurvedSurfaceGeometry(rig);
            return;
        }

        EnsurePreviewMesh(rig);
        GetSurfaceData(rig.id, out var centerLocal, out var rightLocal, out var upLocal, out var width, out var height);

        var normalLocal = Vector3.Cross(rightLocal, upLocal).normalized;
        var worldCenter = transform.TransformPoint(centerLocal);
        var worldRight = transform.TransformDirection(rightLocal).normalized;
        var worldUp = transform.TransformDirection(upLocal).normalized;

        rig.cornersWorld[0] = worldCenter - worldRight * (width * 0.5f) - worldUp * (height * 0.5f);
        rig.cornersWorld[1] = worldCenter + worldRight * (width * 0.5f) - worldUp * (height * 0.5f);
        rig.cornersWorld[2] = worldCenter - worldRight * (width * 0.5f) + worldUp * (height * 0.5f);
        rig.cornersWorld[3] = worldCenter + worldRight * (width * 0.5f) + worldUp * (height * 0.5f);

        rig.wall.transform.localPosition = centerLocal;
        rig.wall.transform.localRotation = Quaternion.LookRotation(normalLocal, upLocal);
        rig.wall.transform.localScale = new Vector3(width, height, 1f);
    }

    private void UpdateCurvedSurfaceGeometry(SurfaceRig rig)
    {
        EnsurePreviewMesh(rig);
        rig.wall.transform.localPosition = GetCurvedAlignmentOffset();
        rig.wall.transform.localRotation = Quaternion.identity;
        rig.wall.transform.localScale = Vector3.one;
    }

    private void EnsurePreviewMesh(SurfaceRig rig)
    {
        if (rig?.wall == null)
        {
            return;
        }

        var signature = GetPreviewMeshSignature();
        var meshFilter = rig.wall.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = rig.wall.AddComponent<MeshFilter>();
        }

        if (setupShape == SetupShape.Room)
        {
            if (rig.planarPreviewMesh == null
                || IsGeneratedPreviewMesh(rig.planarPreviewMesh))
            {
                rig.planarPreviewMesh = GetBuiltInQuadMesh();
            }

            meshFilter.sharedMesh = rig.planarPreviewMesh;
            if (rig.generatedPreviewMesh != null
                && rig.generatedPreviewMesh != rig.planarPreviewMesh)
            {
                SafeDestroy(rig.generatedPreviewMesh);
            }

            rig.generatedPreviewMesh = null;
            rig.previewMeshSignature = signature;
            return;
        }

        if (rig.generatedPreviewMesh != null
            && rig.previewMeshSignature == signature
            && meshFilter.sharedMesh == rig.generatedPreviewMesh)
        {
            return;
        }

        if (rig.generatedPreviewMesh != null)
        {
            SafeDestroy(rig.generatedPreviewMesh);
        }

        switch (setupShape)
        {
            case SetupShape.Cylinder:
                rig.generatedPreviewMesh = CreateCylinderPreviewMesh();
                break;

            case SetupShape.Dome:
                rig.generatedPreviewMesh = CreateDomePreviewMesh();
                break;

            case SetupShape.Room:
            default:
                rig.generatedPreviewMesh = null;
                break;
        }

        rig.previewMeshSignature = signature;
        meshFilter.sharedMesh = rig.generatedPreviewMesh;
    }

    private int GetPreviewMeshSignature()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + (int)setupShape;
            if (setupShape == SetupShape.Cylinder)
            {
                hash = hash * 31 + cylinderRadius.GetHashCode();
                hash = hash * 31 + cylinderBaseHeight.GetHashCode();
                hash = hash * 31 + cylinderPanelHeight.GetHashCode();
                hash = hash * 31 + cylinderAngle.GetHashCode();
            }
            else if (setupShape == SetupShape.Dome)
            {
                hash = hash * 31 + domeFloorRadius.GetHashCode();
                hash = hash * 31 + domeCenterHeight.GetHashCode();
                hash = hash * 31 + (int)domeUnwrapMode;
            }

            return hash;
        }
    }

    private static bool IsGeneratedPreviewMesh(Mesh mesh)
    {
        return mesh != null
            && mesh.name.StartsWith("Immersive ", StringComparison.Ordinal);
    }

    private bool IsPreviewMeshForCurrentSetup(Mesh mesh)
    {
        if (mesh == null)
        {
            return false;
        }

        return setupShape == SetupShape.Cylinder
            ? string.Equals(
                mesh.name,
                "Immersive Cylinder Surface",
                StringComparison.Ordinal)
            : setupShape == SetupShape.Dome
                && string.Equals(
                    mesh.name,
                    "Immersive Dome Surface",
                    StringComparison.Ordinal);
    }

    private static Mesh GetBuiltInQuadMesh()
    {
        if (_builtInQuadMesh != null)
        {
            return _builtInQuadMesh;
        }

        _builtInQuadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        if (_builtInQuadMesh != null)
        {
            return _builtInQuadMesh;
        }

        var temporaryQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _builtInQuadMesh = temporaryQuad.GetComponent<MeshFilter>()?.sharedMesh;
        SafeDestroy(temporaryQuad);
        return _builtInQuadMesh;
    }

    private Mesh CreateCylinderPreviewMesh()
    {
        var segmentCount = Mathf.Clamp(Mathf.CeilToInt(cylinderAngle / 3f), 4, 240);
        var vertices = new Vector3[(segmentCount + 1) * 2];
        var normals = new Vector3[vertices.Length];
        var uvs = new Vector2[vertices.Length];
        var triangles = new int[segmentCount * 12];
        var halfAngle = cylinderAngle * .5f;

        for (var segment = 0; segment <= segmentCount; segment++)
        {
            var t = segment / (float)segmentCount;
            var angle = Mathf.Lerp(-halfAngle, halfAngle, t) * Mathf.Deg2Rad;
            var radial = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * cylinderRadius;
            var vertex = segment * 2;
            vertices[vertex] = radial + Vector3.up * cylinderBaseHeight;
            vertices[vertex + 1] =
                radial + Vector3.up * (cylinderBaseHeight + cylinderPanelHeight);
            normals[vertex] = -radial.normalized;
            normals[vertex + 1] = normals[vertex];
            uvs[vertex] = new Vector2(t, 0f);
            uvs[vertex + 1] = new Vector2(t, 1f);

            if (segment == segmentCount)
            {
                continue;
            }

            var next = vertex + 2;
            var triangle = segment * 12;
            triangles[triangle] = vertex;
            triangles[triangle + 1] = vertex + 1;
            triangles[triangle + 2] = next;
            triangles[triangle + 3] = next;
            triangles[triangle + 4] = vertex + 1;
            triangles[triangle + 5] = next + 1;
            triangles[triangle + 6] = vertex;
            triangles[triangle + 7] = next;
            triangles[triangle + 8] = vertex + 1;
            triangles[triangle + 9] = next;
            triangles[triangle + 10] = next + 1;
            triangles[triangle + 11] = vertex + 1;
        }

        var mesh = new Mesh
        {
            name = "Immersive Cylinder Surface",
            hideFlags = HideFlags.DontSave
        };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private Mesh CreateDomePreviewMesh()
    {
        const int segmentCount = 96;
        const int ringCount = 32;
        GetDomeSphere(out var sphereRadius, out var sphereCenterY, out var maximumPolar);
        var rowSize = segmentCount + 1;
        var vertices = new Vector3[(ringCount + 1) * rowSize];
        var normals = new Vector3[vertices.Length];
        var uvs = new Vector2[vertices.Length];
        var triangles = new int[ringCount * segmentCount * 12];

        for (var ring = 0; ring <= ringCount; ring++)
        {
            var polar01 = ring / (float)ringCount;
            var polar = polar01 * maximumPolar;
            var radial = sphereRadius * Mathf.Sin(polar);
            var halfPolarSin = Mathf.Sin(polar * .5f);
            var y =
                domeCenterHeight
                - 2f * sphereRadius * halfPolarSin * halfPolarSin;

            for (var segment = 0; segment <= segmentCount; segment++)
            {
                var longitude01 = segment / (float)segmentCount;
                var longitude = Mathf.Lerp(-Mathf.PI, Mathf.PI, longitude01);
                var index = ring * rowSize + segment;
                vertices[index] = new Vector3(
                    radial * Mathf.Sin(longitude),
                    y,
                    radial * Mathf.Cos(longitude));
                normals[index] = -new Vector3(
                    vertices[index].x,
                    vertices[index].y - sphereCenterY,
                    vertices[index].z).normalized;
                uvs[index] = GetDomePreviewUv(longitude, polar, maximumPolar);

                if (ring == ringCount || segment == segmentCount)
                {
                    continue;
                }

                var nextRing = index + rowSize;
                var triangle = (ring * segmentCount + segment) * 12;
                triangles[triangle] = index;
                triangles[triangle + 1] = nextRing;
                triangles[triangle + 2] = index + 1;
                triangles[triangle + 3] = index + 1;
                triangles[triangle + 4] = nextRing;
                triangles[triangle + 5] = nextRing + 1;
                triangles[triangle + 6] = index;
                triangles[triangle + 7] = index + 1;
                triangles[triangle + 8] = nextRing;
                triangles[triangle + 9] = index + 1;
                triangles[triangle + 10] = nextRing + 1;
                triangles[triangle + 11] = nextRing;
            }
        }

        var mesh = new Mesh
        {
            name = "Immersive Dome Surface",
            hideFlags = HideFlags.DontSave
        };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private Vector2 GetDomePreviewUv(
        float longitude,
        float polar,
        float maximumPolar)
    {
        if (domeUnwrapMode == DomeUnwrapMode.Equirectangular)
        {
            return new Vector2(
                (longitude + Mathf.PI) / (Mathf.PI * 2f),
                1f - polar / maximumPolar);
        }

        var radial = domeUnwrapMode == DomeUnwrapMode.DomemasterEqualArea
            ? Mathf.Sin(polar * .5f) / Mathf.Sin(maximumPolar * .5f)
            : polar / maximumPolar;
        return new Vector2(
            .5f + .5f * radial * Mathf.Sin(longitude),
            .5f + .5f * radial * Mathf.Cos(longitude));
    }

    private void GetSurfaceData(
        SurfaceId id,
        out Vector3 center,
        out Vector3 right,
        out Vector3 up,
        out float width,
        out float height)
    {
        switch (id)
        {
            case SurfaceId.Front:
                center = new Vector3(0f, roomHeight * 0.5f, 0f);
                right = Vector3.right;
                up = Vector3.up;
                width = roomWidth;
                height = roomHeight;
                break;

            case SurfaceId.Back:
                center = new Vector3(0f, roomHeight * 0.5f, -roomDepth);
                right = -Vector3.right;
                up = Vector3.up;
                width = roomWidth;
                height = roomHeight;
                break;

            case SurfaceId.Left:
                center = new Vector3(-roomWidth * 0.5f, roomHeight * 0.5f, -roomDepth * 0.5f);
                right = Vector3.forward;
                up = Vector3.up;
                width = roomDepth;
                height = roomHeight;
                break;

            case SurfaceId.Right:
                center = new Vector3(roomWidth * 0.5f, roomHeight * 0.5f, -roomDepth * 0.5f);
                right = -Vector3.forward;
                up = Vector3.up;
                width = roomDepth;
                height = roomHeight;
                break;

            case SurfaceId.Floor:
                center = new Vector3(0f, 0f, -roomDepth * 0.5f);
                right = Vector3.right;
                up = -Vector3.forward;
                width = roomWidth;
                height = roomDepth;
                break;

            case SurfaceId.Ceiling:
                center = new Vector3(0f, roomHeight, -roomDepth * 0.5f);
                right = Vector3.right;
                up = Vector3.forward;
                width = roomWidth;
                height = roomDepth;
                break;

            default:
                center = Vector3.zero;
                right = Vector3.right;
                up = Vector3.up;
                width = 1f;
                height = 1f;
                break;
        }

        center += GetRoomAlignmentOffset();
    }

    private Vector3 GetRoomAlignmentOffset()
    {
        var cameraLocalPosition = _camerasContainer != null
            ? transform.InverseTransformPoint(_camerasContainer.position)
            : Vector3.zero;

        // The camera position is expressed as: selected room anchor + camera offset.
        // Moving the room by the inverse offset keeps the generated cameras at the
        // Cameras container while allowing the room anchor to be chosen freely.
        return cameraLocalPosition - GetEffectiveCameraOffsetFromAnchor() - GetRoomAnchorPoint();
    }

    private Vector3 GetEffectiveCameraOffsetFromAnchor()
    {
        return _cameraOffsetEnabled ? cameraOffsetFromAnchor : Vector3.zero;
    }

    private Vector3 GetRoomAnchorPoint()
    {
        var halfWidth = roomWidth * 0.5f;
        var halfDepth = roomDepth * 0.5f;

        switch (roomAlignment)
        {
            case RoomAlignmentMode.BackWall:
                return new Vector3(0f, 0f, -roomDepth);

            case RoomAlignmentMode.LeftWall:
                return new Vector3(-halfWidth, 0f, -halfDepth);

            case RoomAlignmentMode.RightWall:
                return new Vector3(halfWidth, 0f, -halfDepth);

            case RoomAlignmentMode.RoomCenter:
                return new Vector3(0f, 0f, -halfDepth);

            case RoomAlignmentMode.FrontWall:
            default:
                return Vector3.zero;
        }
    }

    private Vector3 GetCurvedAlignmentOffset()
    {
        var cameraLocalPosition = _camerasContainer != null
            ? transform.InverseTransformPoint(_camerasContainer.position)
            : Vector3.zero;
        return cameraLocalPosition - GetEffectiveCameraOffsetFromAnchor();
    }

    private void GetOutputSurfaceSize(SurfaceId id, out float width, out float height)
    {
        if (setupShape == SetupShape.Cylinder)
        {
            width = cylinderRadius * cylinderAngle * Mathf.Deg2Rad;
            height = cylinderPanelHeight;
            return;
        }

        if (setupShape == SetupShape.Dome)
        {
            GetDomeSphere(out var sphereRadius, out _, out var maximumPolar);
            if (domeUnwrapMode == DomeUnwrapMode.Equirectangular)
            {
                width = Mathf.PI * 2f * sphereRadius;
                height = sphereRadius * maximumPolar;
            }
            else
            {
                width = sphereRadius * maximumPolar * 2f;
                height = width;
            }

            return;
        }

        GetSurfaceData(id, out _, out _, out _, out width, out height);
    }

    private void GetSetupDimensions(out float width, out float height, out float depth)
    {
        if (setupShape == SetupShape.Cylinder)
        {
            width = cylinderRadius * cylinderAngle * Mathf.Deg2Rad;
            height = cylinderPanelHeight;
            depth = cylinderRadius * 2f;
            return;
        }

        if (setupShape == SetupShape.Dome)
        {
            GetOutputSurfaceSize(SurfaceId.Front, out width, out height);
            depth = domeFloorRadius * 2f;
            return;
        }

        width = roomWidth;
        height = roomHeight;
        depth = roomDepth;
    }

    private void GetDomeSphere(
        out float sphereRadius,
        out float sphereCenterY,
        out float maximumPolar)
    {
        sphereRadius =
            (domeFloorRadius * domeFloorRadius + domeCenterHeight * domeCenterHeight)
            / (2f * domeCenterHeight);
        sphereCenterY = domeCenterHeight - sphereRadius;
        maximumPolar = 2f * Mathf.Atan2(domeCenterHeight, domeFloorRadius);
    }

    private void UpdatePreviewLayer(SurfaceRig rig)
    {
        if (_previewLayer < 0)
        {
            return;
        }

        if (rig.wall != null)
        {
            rig.wall.layer = _previewLayer;
        }

        if (rig.camera != null)
        {
            var cullingMask = ~(1 << _previewLayer);
            if (_subtitleOverlayLayer >= 0)
            {
                cullingMask &= ~(1 << _subtitleOverlayLayer);
            }

            if (_aimOverlayLayer >= 0)
            {
                cullingMask &= ~(1 << _aimOverlayLayer);
            }

            rig.camera.cullingMask = cullingMask;
        }
    }

    private void ResetSurfaceCamera(Camera camera)
    {
        if (camera == null)
        {
            return;
        }

        var source = cameraPrefab != null ? cameraPrefab.GetComponent<Camera>() : null;
        if (source != null && source != camera)
        {
            camera.CopyFrom(source);
            var sourceData = source.GetComponent<UniversalAdditionalCameraData>();
            var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            if (sourceData != null && cameraData != null)
            {
                cameraData.renderType = sourceData.renderType;
                cameraData.allowXRRendering = sourceData.allowXRRendering;
            }
        }
        else
        {
            camera.orthographic = false;
            camera.nearClipPlane = .3f;
            camera.farClipPlane = 1000f;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.ResetProjectionMatrix();
        }

        camera.targetTexture = null;
        camera.enabled = _outputEnabled;
    }

    private void UpdateCurvedProjection(SurfaceRig rig, Vector3 eyeWorld)
    {
        if (rig?.camera == null || rig.renderTexture == null || rig.wall == null)
        {
            return;
        }

        if (_projectionLayer < 0)
        {
            if (!_projectionLayerWarningLogged)
            {
                Debug.LogError(
                    $"The '{ProjectionLayerName}' layer is required for curved immersive output.",
                    this);
                _projectionLayerWarningLogged = true;
            }

            rig.camera.enabled = false;
            return;
        }

        _projectionLayerWarningLogged = false;
        if (!EnsureCurvedResources(rig))
        {
            rig.camera.enabled = false;
            return;
        }

        ConfigureCurvedOutputCamera(rig, eyeWorld);
        var faceSize = GetCurvedCaptureFaceSize(rig.renderTexture);
        var sceneCullingMask = GetCurvedCaptureCullingMask();
        var shapeRotation = rig.wall.transform.rotation;

        for (var index = 0; index < rig.captureCameras.Length; index++)
        {
            if (!UpdateCurvedCaptureTexture(rig, index, faceSize))
            {
                rig.camera.enabled = false;
                for (var captureIndex = 0;
                     captureIndex < rig.captureCameras.Length;
                     captureIndex++)
                {
                    if (rig.captureCameras[captureIndex] != null)
                    {
                        rig.captureCameras[captureIndex].enabled = false;
                        rig.captureCameras[captureIndex].targetTexture = null;
                    }

                    if (rig.captureTextures[captureIndex] != null)
                    {
                        ReleaseAndDestroyRenderTexture(
                            rig.captureTextures[captureIndex]);
                        rig.captureTextures[captureIndex] = null;
                    }
                }

                return;
            }

            var captureCamera = rig.captureCameras[index];
            if (captureCamera == null)
            {
                continue;
            }

            ResetCaptureCamera(captureCamera);
            captureCamera.transform.SetPositionAndRotation(
                eyeWorld,
                shapeRotation
                * Quaternion.LookRotation(
                    CaptureDirections[index],
                    CaptureUpDirections[index]));
            captureCamera.targetTexture = rig.captureTextures[index];
            captureCamera.cullingMask = sceneCullingMask;
            captureCamera.depth = rig.camera.depth - 10f + index * .01f;
            captureCamera.enabled = _outputEnabled;
        }

        _captureTextureWarningLogged = false;
        UpdateCurvedProjectionMaterial(rig, eyeWorld);
    }

    private bool EnsureCurvedResources(SurfaceRig rig)
    {
        if (rig.curvedCaptureRoot != null
            && rig.projectionQuad != null
            && rig.projectionMaterial != null)
        {
            return true;
        }

        var shader = Resources.Load<Shader>("Immersive/CurvedProjection")
            ?? Shader.Find(CurvedProjectionShaderName);
        if (shader == null || !shader.isSupported)
        {
            if (!_projectionShaderWarningLogged)
            {
                var reason = shader == null
                    ? "was not found"
                    : $"is not supported by {SystemInfo.graphicsDeviceType}";
                Debug.LogError(
                    $"Shader '{CurvedProjectionShaderName}' {reason}; curved immersive output is disabled.",
                    this);
                _projectionShaderWarningLogged = true;
            }

            return false;
        }

        _projectionShaderWarningLogged = false;
        DestroyAllChildrenByExactName(
            _camerasContainer,
            rig.id + CurvedCaptureRootSuffix);
        DestroyAllChildrenByExactName(rig.camera.transform, ProjectionQuadName);

        rig.curvedCaptureRoot = new GameObject(rig.id + CurvedCaptureRootSuffix);
        rig.curvedCaptureRoot.hideFlags = HideFlags.DontSave;
        rig.curvedCaptureRoot.transform.SetParent(_camerasContainer, false);
        rig.curvedCaptureRoot.SetActive(false);

        for (var index = 0; index < rig.captureCameras.Length; index++)
        {
            GameObject cameraObject;
            if (cameraPrefab != null)
            {
                cameraObject = Instantiate(
                    cameraPrefab,
                    rig.curvedCaptureRoot.transform);
                DisableCaptureSenderComponents(cameraObject);
            }
            else
            {
                cameraObject = new GameObject();
                cameraObject.transform.SetParent(
                    rig.curvedCaptureRoot.transform,
                    false);
            }

            cameraObject.name = "Capture_" + CaptureFaceNames[index];
            cameraObject.hideFlags = HideFlags.DontSave;
            rig.captureCameras[index] = cameraObject.GetComponent<Camera>()
                ?? cameraObject.AddComponent<Camera>();
        }

        rig.curvedCaptureRoot.SetActive(_outputEnabled);

        rig.projectionQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        rig.projectionQuad.name = ProjectionQuadName;
        rig.projectionQuad.hideFlags = HideFlags.DontSave;
        rig.projectionQuad.layer = _projectionLayer;
        rig.projectionQuad.transform.SetParent(rig.camera.transform, false);
        var collider = rig.projectionQuad.GetComponent<Collider>();
        if (collider != null)
        {
            SafeDestroy(collider);
        }

        rig.projectionMaterial = new Material(shader)
        {
            name = setupShape + "_CurvedProjectionRuntimeMat",
            hideFlags = HideFlags.DontSave
        };
        var projectionRenderer = rig.projectionQuad.GetComponent<MeshRenderer>();
        projectionRenderer.sharedMaterial = rig.projectionMaterial;
        projectionRenderer.shadowCastingMode = ShadowCastingMode.Off;
        projectionRenderer.receiveShadows = false;
        projectionRenderer.lightProbeUsage = LightProbeUsage.Off;
        projectionRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return true;
    }

    private void ConfigureCurvedOutputCamera(SurfaceRig rig, Vector3 eyeWorld)
    {
        var camera = rig.camera;
        var aspect = rig.renderTexture.width / Mathf.Max(1f, rig.renderTexture.height);
        var isolatedPosition =
            eyeWorld - transform.up * CurvedOutputIsolationDistance;

        camera.transform.SetPositionAndRotation(isolatedPosition, transform.rotation);
        camera.orthographic = true;
        camera.orthographicSize = .5f;
        camera.aspect = aspect;
        camera.nearClipPlane = .01f;
        camera.farClipPlane = 2f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = 1 << _projectionLayer;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.allowDynamicResolution = false;
        camera.targetTexture = rig.renderTexture;
        camera.ResetProjectionMatrix();
        var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
        if (cameraData != null)
        {
            cameraData.renderType = CameraRenderType.Base;
            cameraData.allowXRRendering = false;
        }

        rig.projectionQuad.layer = _projectionLayer;
        rig.projectionQuad.transform.localPosition = new Vector3(0f, 0f, 1f);
        rig.projectionQuad.transform.localRotation = Quaternion.identity;
        rig.projectionQuad.transform.localScale = new Vector3(aspect, 1f, 1f);
    }

    private void ResetCaptureCamera(Camera camera)
    {
        var source = cameraPrefab != null ? cameraPrefab.GetComponent<Camera>() : null;
        if (source != null)
        {
            camera.CopyFrom(source);
        }

        camera.orthographic = false;
        camera.fieldOfView = 90f;
        camera.aspect = 1f;
        camera.rect = new Rect(0f, 0f, 1f, 1f);
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.allowDynamicResolution = false;
        camera.ResetProjectionMatrix();
        var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
        if (cameraData != null)
        {
            cameraData.renderType = CameraRenderType.Base;
            cameraData.allowXRRendering = false;
        }
    }

    private static void DisableCaptureSenderComponents(GameObject cameraObject)
    {
        if (cameraObject == null)
        {
            return;
        }

        var behaviours = cameraObject.GetComponents<MonoBehaviour>();
        for (var index = 0; index < behaviours.Length; index++)
        {
            var behaviour = behaviours[index];
            if (behaviour != null
                && MatchesTypeName(
                    behaviour.GetType(),
                    new[] { "SpoutSender", "NDISender", "NdiSender" }))
            {
                behaviour.enabled = false;
            }
        }
    }

    private int GetCurvedCaptureCullingMask()
    {
        var source = cameraPrefab != null ? cameraPrefab.GetComponent<Camera>() : null;
        var mask = source != null ? source.cullingMask : ~0;
        mask = ExcludeLayer(mask, _previewLayer);
        mask = ExcludeLayer(mask, _projectionLayer);
        mask = ExcludeLayer(mask, _subtitleOverlayLayer);
        mask = ExcludeLayer(mask, _aimOverlayLayer);
        return mask;
    }

    private static int ExcludeLayer(int mask, int layer)
    {
        return layer < 0 ? mask : mask & ~(1 << layer);
    }

    private int GetCurvedCaptureFaceSize(RenderTexture output)
    {
        return Mathf.Min(
            GetRequestedCurvedCaptureFaceSize(output),
            GetMaximumCurvedCaptureFaceSize());
    }

    private int GetRequestedCurvedCaptureFaceSize(RenderTexture output)
    {
        var pixelsPerMeter = GetPixelsPerMeter() / Mathf.Max(1, resolutionDivider);
        var angularRadius = cylinderRadius;
        if (setupShape == SetupShape.Dome)
        {
            GetDomeSphere(out angularRadius, out _, out _);
        }

        // Every capture camera spans 90 degrees, even when the requested output
        // covers only a narrow cylinder segment or a shallow dome cap.
        var angularPixels = Mathf.CeilToInt(
            angularRadius * Mathf.PI * .5f * pixelsPerMeter);
        var verticalPixels = setupShape == SetupShape.Cylinder
            ? output.height
            : output.height / 2;
        var requested = Mathf.Max(
            256,
            Mathf.Max(angularPixels, verticalPixels));
        return AlignUpToMultiple(requested, 16);
    }

    private static int GetMaximumCurvedCaptureFaceSize()
    {
        var maximum = Mathf.Max(
            16,
            Mathf.Min(SystemInfo.maxTextureSize, MaximumCurvedCaptureFaceSize));
        maximum -= maximum % 16;
        return maximum;
    }

    private bool UpdateCurvedCaptureTexture(
        SurfaceRig rig,
        int index,
        int faceSize)
    {
        var texture = rig.captureTextures[index];
        var captureDepth = Mathf.Max(16, depthBufferBits);
        if (rig.failedCaptureFaceSize == faceSize
            && rig.failedCaptureDepth == captureDepth
            && rig.failedCaptureFormat == renderTextureFormat)
        {
            return false;
        }

        if (texture != null
            && (texture.width != faceSize
                || texture.height != faceSize
                || texture.depth != captureDepth
                || texture.format != renderTextureFormat))
        {
            if (rig.captureCameras[index] != null)
            {
                rig.captureCameras[index].targetTexture = null;
            }

            ReleaseAndDestroyRenderTexture(texture);
            texture = null;
        }

        if (texture == null)
        {
            texture = new RenderTexture(
                faceSize,
                faceSize,
                captureDepth,
                renderTextureFormat)
            {
                name = setupShape + "_Capture_" + CaptureFaceNames[index],
                hideFlags = HideFlags.DontSave,
                antiAliasing = 1,
                autoGenerateMips = false,
                useMipMap = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            if (!texture.Create())
            {
                ReleaseAndDestroyRenderTexture(texture);
                rig.captureTextures[index] = null;
                rig.failedCaptureFaceSize = faceSize;
                rig.failedCaptureDepth = captureDepth;
                rig.failedCaptureFormat = renderTextureFormat;
                if (!_captureTextureWarningLogged)
                {
                    Debug.LogError(
                        $"Could not allocate {faceSize}x{faceSize} curved capture textures "
                        + $"using {renderTextureFormat}; curved immersive output is disabled.",
                        this);
                    _captureTextureWarningLogged = true;
                }

                return false;
            }

            rig.captureTextures[index] = texture;
        }
        else if (!texture.IsCreated())
        {
            if (!texture.Create())
            {
                rig.failedCaptureFaceSize = faceSize;
                rig.failedCaptureDepth = captureDepth;
                rig.failedCaptureFormat = renderTextureFormat;
                if (!_captureTextureWarningLogged)
                {
                    Debug.LogError(
                        $"Could not recreate {faceSize}x{faceSize} curved capture textures "
                        + $"using {renderTextureFormat}; curved immersive output is disabled.",
                        this);
                    _captureTextureWarningLogged = true;
                }

                return false;
            }
        }

        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        rig.failedCaptureFaceSize = 0;
        rig.failedCaptureDepth = 0;
        return true;
    }

    private void UpdateCurvedProjectionMaterial(SurfaceRig rig, Vector3 eyeWorld)
    {
        var material = rig.projectionMaterial;
        if (material == null)
        {
            return;
        }

        for (var index = 0; index < rig.captureCameras.Length; index++)
        {
            var captureCamera = rig.captureCameras[index];
            material.SetTexture(
                CaptureTextureProperties[index],
                rig.captureTextures[index]);
            if (captureCamera != null)
            {
                var gpuProjection = GL.GetGPUProjectionMatrix(
                    captureCamera.projectionMatrix,
                    true);
                material.SetMatrix(
                    CaptureMatrixProperties[index],
                    gpuProjection * captureCamera.worldToCameraMatrix);
            }
        }

        GetDomeSphere(out var domeSphereRadius, out _, out var domeMaximumPolar);
        material.SetInt("_SetupShape", (int)setupShape);
        material.SetInt("_DomeUnwrapMode", (int)domeUnwrapMode);
        material.SetFloat("_CylinderRadius", cylinderRadius);
        material.SetFloat("_CylinderBaseHeight", cylinderBaseHeight);
        material.SetFloat("_CylinderPanelHeight", cylinderPanelHeight);
        material.SetFloat("_CylinderAngleRadians", cylinderAngle * Mathf.Deg2Rad);
        material.SetFloat("_DomeSphereRadius", domeSphereRadius);
        material.SetFloat("_DomeCenterHeight", domeCenterHeight);
        material.SetFloat("_DomeMaximumPolar", domeMaximumPolar);
        material.SetVector(
            "_EyeShape",
            rig.wall.transform.InverseTransformPoint(eyeWorld));
        material.SetMatrix("_ShapeToWorld", rig.wall.transform.localToWorldMatrix);
        material.SetMatrix(
            "_WorldToCaptureAxes",
            Matrix4x4.Rotate(Quaternion.Inverse(rig.wall.transform.rotation)));
    }

    private void ReleaseCurvedResources(SurfaceRig rig)
    {
        if (rig == null)
        {
            return;
        }

        for (var index = 0; index < rig.captureCameras.Length; index++)
        {
            if (rig.captureCameras[index] != null)
            {
                rig.captureCameras[index].enabled = false;
                rig.captureCameras[index].targetTexture = null;
                rig.captureCameras[index] = null;
            }

            if (rig.captureTextures[index] != null)
            {
                ReleaseAndDestroyRenderTexture(rig.captureTextures[index]);
                rig.captureTextures[index] = null;
            }
        }

        rig.failedCaptureFaceSize = 0;
        rig.failedCaptureDepth = 0;
        _captureTextureWarningLogged = false;

        if (rig.curvedCaptureRoot != null)
        {
            rig.curvedCaptureRoot.SetActive(false);
            SafeDestroy(rig.curvedCaptureRoot);
            rig.curvedCaptureRoot = null;
        }

        if (rig.projectionQuad != null)
        {
            rig.projectionQuad.SetActive(false);
            SafeDestroy(rig.projectionQuad);
            rig.projectionQuad = null;
        }

        if (rig.projectionMaterial != null)
        {
            SafeDestroy(rig.projectionMaterial);
            rig.projectionMaterial = null;
        }
    }

    private static int GetOrCreatePreviewLayer()
    {
        return GetOrCreateLayer(PreviewLayerName);
    }

    private static int GetOrCreateLayer(string requestedLayerName)
    {
        var layer = LayerMask.NameToLayer(requestedLayerName);
        if (layer >= 0)
        {
            return layer;
        }

#if UNITY_EDITOR
        var tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (tagManagerAssets == null || tagManagerAssets.Length == 0)
        {
            Debug.LogError("Unable to load TagManager.asset to create the '" + requestedLayerName + "' layer.");
            return -1;
        }

        var tagManager = new SerializedObject(tagManagerAssets[0]);
        var layers = tagManager.FindProperty("layers");
        if (layers == null)
        {
            Debug.LogError("Unable to find the layers list in TagManager.asset.");
            return -1;
        }

        for (var i = 8; i < layers.arraySize; i++)
        {
            var layerNameProperty = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(layerNameProperty.stringValue))
            {
                continue;
            }

            layerNameProperty.stringValue = requestedLayerName;
            tagManager.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            return i;
        }

        Debug.LogError("Unable to create the '" + requestedLayerName + "' layer because all user layers are occupied.");
#endif

        return -1;
    }

    private void UpdateRenderTexture(SurfaceRig rig)
    {
        GetOutputSurfaceSize(rig.id, out var wallWidth, out var wallHeight);
        var size = ComputeRenderTextureSize(wallWidth, wallHeight);
        var configuredAsset = GetRenderTextureAsset(rig.id);

        // Keep the persistent asset object alive so Recorder settings retain their
        // reference while room dimensions and output resolution change.
        if (rig.renderTexture != configuredAsset
            && (configuredAsset != null || !rig.ownsRenderTexture))
        {
            DetachRenderTexture(rig);

            if (rig.ownsRenderTexture)
            {
                ReleaseAndDestroyRenderTexture(rig.renderTexture);
            }

            rig.renderTexture = configuredAsset;
            rig.ownsRenderTexture = false;
        }

        var textureNeedsRebuild = rig.renderTexture != null
            && (rig.renderTexture.width != size.x
                || rig.renderTexture.height != size.y
                || rig.renderTexture.depth != depthBufferBits
                || rig.renderTexture.format != renderTextureFormat);

        if (textureNeedsRebuild)
        {
            DetachRenderTexture(rig);

            if (rig.ownsRenderTexture)
            {
                ReleaseAndDestroyRenderTexture(rig.renderTexture);
                rig.renderTexture = null;
            }
            else
            {
                ConfigureRenderTexture(
                    rig.renderTexture,
                    size.x,
                    size.y,
                    depthBufferBits,
                    renderTextureFormat);
            }
        }

        if (rig.renderTexture == null)
        {
            rig.renderTexture = new RenderTexture(size.x, size.y, depthBufferBits, renderTextureFormat)
            {
                name = rig.id.ToString(),
                antiAliasing = 1,
                autoGenerateMips = false,
                useMipMap = false
            };
            rig.renderTexture.Create();
            rig.ownsRenderTexture = true;
        }

        if (!rig.renderTexture.IsCreated())
        {
            rig.renderTexture.Create();
        }

        if (rig.ownsRenderTexture)
        {
            rig.renderTexture.name = rig.id.ToString();
        }

        rig.camera.targetTexture = rig.renderTexture;
        SetRenderTextureOutput(rig.id, rig.renderTexture);
    }

    private static void ConfigureRenderTexture(
        RenderTexture texture,
        int width,
        int height,
        int depth,
        RenderTextureFormat format)
    {
        if (texture == null)
        {
            return;
        }

        if (texture.IsCreated())
        {
            texture.Release();
        }

        texture.width = width;
        texture.height = height;
        texture.depth = depth;
        texture.format = format;
        texture.antiAliasing = 1;
        texture.autoGenerateMips = false;
        texture.useMipMap = false;
        texture.Create();

#if UNITY_EDITOR
        if (EditorUtility.IsPersistent(texture))
        {
            EditorUtility.SetDirty(texture);
        }
#endif
    }

    private static void DetachRenderTexture(SurfaceRig rig)
    {
        if (rig.camera != null && rig.camera.targetTexture == rig.renderTexture)
        {
            rig.camera.targetTexture = null;
        }

        if (rig.runtimeMaterial != null)
        {
            SetMaterialTexture(rig.runtimeMaterial, null);
        }
    }

    private void SetRenderTextureOutput(SurfaceId id, RenderTexture texture)
    {
        switch (id)
        {
            case SurfaceId.Left:
                leftRT = texture;
                break;
            case SurfaceId.Right:
                rightRT = texture;
                break;
            case SurfaceId.Front:
                frontRT = texture;
                break;
            case SurfaceId.Back:
                backRT = texture;
                break;
            case SurfaceId.Floor:
                floorRT = texture;
                break;
            case SurfaceId.Ceiling:
                ceilingRT = texture;
                break;
        }
    }

    private Vector2Int ComputeRenderTextureSize(float wallWidth, float wallHeight)
    {
        var pixelsPerMeter = GetPixelsPerMeter() / Mathf.Max(1, resolutionDivider);
        var width = ComputeAlignedResolution(wallWidth, pixelsPerMeter);
        var height = ComputeAlignedResolution(wallHeight, pixelsPerMeter);

        return new Vector2Int(width, height);
    }

    private static int ComputeAlignedResolution(float surfaceSizeMeters, float pixelsPerMeter)
    {
        var resolution = Mathf.Max(16, Mathf.RoundToInt(surfaceSizeMeters * pixelsPerMeter));

        // NDI requires dimensions aligned to 16-pixel blocks.
        resolution = AlignUpToMultiple(resolution, 16);
        var maximum = Mathf.Max(16, SystemInfo.maxTextureSize);
        maximum -= maximum % 16;
        return Mathf.Min(resolution, maximum);
    }

    private static int AlignUpToMultiple(int value, int multiple)
    {
        if (multiple <= 1)
        {
            return value;
        }

        var remainder = value % multiple;
        return remainder == 0 ? value : value + (multiple - remainder);
    }

    private float GetPixelsPerMeter()
    {
        var referenceSizeMeters = GetReferenceDimensionMeters();
        var pixelsPerMeter = desiredResolutionValue / Mathf.Max(0.01f, referenceSizeMeters);
        return pixelsPerMeter;
    }

    private void NormalizeResolutionInputs()
    {
        var referenceSizeMeters = GetReferenceDimensionMeters();
        var pixelsPerMeter = desiredResolutionValue / Mathf.Max(0.01f, referenceSizeMeters);

        GetSetupDimensions(out var width, out var height, out var depth);
        resolutionWidth = ComputeAlignedResolution(width, pixelsPerMeter);
        resolutionHeight = ComputeAlignedResolution(height, pixelsPerMeter);
        resolutionDepth = ComputeAlignedResolution(depth, pixelsPerMeter);
    }

    private float GetReferenceDimensionMeters()
    {
        GetSetupDimensions(out var width, out var height, out var depth);
        switch (resolutionMode)
        {
            case ResolutionMode.Width:
                return width;

            case ResolutionMode.Depth:
                return depth;

            case ResolutionMode.Height:
            default:
                return height;
        }
    }

    private void UpdateWallMaterial(SurfaceRig rig)
    {
        if (rig.renderer == null)
        {
            return;
        }

        if (visualMode == VisualMode.DebugMaterialA && debugMaterialA != null)
        {
            rig.renderer.sharedMaterial = debugMaterialA;
            return;
        }

        if (visualMode == VisualMode.DebugMaterialB && debugMaterialB != null)
        {
            rig.renderer.sharedMaterial = debugMaterialB;
            return;
        }

        if (rig.runtimeMaterial == null)
        {
            rig.runtimeMaterial = new Material(Shader.Find("Unlit/Texture"))
            {
                name = rig.id + "_WallRuntimeMat"
            };
        }

        SetMaterialTexture(rig.runtimeMaterial, rig.renderTexture);
        rig.renderer.sharedMaterial = rig.runtimeMaterial;
    }

    private static void SetMaterialTexture(Material material, Texture texture)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
        }

        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
        }

        if (material.HasProperty("_BlitTexture"))
        {
            material.SetTexture("_BlitTexture", texture);
        }
    }

    private bool TryProjectRoomRay(
        Vector3 originWorld,
        Vector3 directionWorld,
        out SurfaceId surface,
        out Camera outputCamera,
        out Vector3 viewportPosition)
    {
        surface = SurfaceId.Front;
        outputCamera = null;
        viewportPosition = default;

        var worldPoint = originWorld + directionWorld.normalized * 1000f;
        var bestScore = float.NegativeInfinity;
        for (var index = 0; index < RoomSurfaces.Length; index++)
        {
            var candidateSurface = RoomSurfaces[index];
            if (!_rigs.TryGetValue(candidateSurface, out var candidateRig)
                || candidateRig?.camera == null
                || !candidateRig.camera.isActiveAndEnabled)
            {
                continue;
            }

            var candidateViewport = candidateRig.camera.WorldToViewportPoint(worldPoint);
            if (candidateViewport.z <= 0f
                || candidateViewport.x < -.0001f
                || candidateViewport.x > 1.0001f
                || candidateViewport.y < -.0001f
                || candidateViewport.y > 1.0001f)
            {
                continue;
            }

            var score = Mathf.Min(
                Mathf.Min(candidateViewport.x, 1f - candidateViewport.x),
                Mathf.Min(candidateViewport.y, 1f - candidateViewport.y));
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            surface = candidateSurface;
            outputCamera = candidateRig.camera;
            viewportPosition = new Vector3(
                Mathf.Clamp01(candidateViewport.x),
                Mathf.Clamp01(candidateViewport.y),
                candidateViewport.z);
        }

        return outputCamera != null;
    }

    private bool TryIntersectCylinder(
        Vector3 origin,
        Vector3 direction,
        out Vector2 uv,
        out float distance)
    {
        uv = default;
        distance = 0f;
        var a = direction.x * direction.x + direction.z * direction.z;
        if (a <= Mathf.Epsilon)
        {
            return false;
        }

        var b = 2f * (origin.x * direction.x + origin.z * direction.z);
        var c = origin.x * origin.x + origin.z * origin.z
            - cylinderRadius * cylinderRadius;
        if (!TrySolveQuadratic(a, b, c, out var first, out var second))
        {
            return false;
        }

        return TryGetCylinderHitUv(origin, direction, first, out uv, out distance)
            || TryGetCylinderHitUv(origin, direction, second, out uv, out distance);
    }

    private bool TryGetCylinderHitUv(
        Vector3 origin,
        Vector3 direction,
        float candidateDistance,
        out Vector2 uv,
        out float distance)
    {
        uv = default;
        distance = 0f;
        if (candidateDistance <= .0001f)
        {
            return false;
        }

        var hit = origin + direction * candidateDistance;
        var top = cylinderBaseHeight + cylinderPanelHeight;
        if (hit.y < cylinderBaseHeight - .0001f || hit.y > top + .0001f)
        {
            return false;
        }

        var angle = Mathf.Atan2(hit.x, hit.z);
        var angleRange = cylinderAngle * Mathf.Deg2Rad;
        if (Mathf.Abs(angle) > angleRange * .5f + .0001f)
        {
            return false;
        }

        uv = new Vector2(
            Mathf.Clamp01(angle / angleRange + .5f),
            Mathf.Clamp01((hit.y - cylinderBaseHeight) / cylinderPanelHeight));
        distance = candidateDistance;
        return true;
    }

    private bool TryIntersectDome(
        Vector3 origin,
        Vector3 direction,
        out Vector2 uv,
        out float distance)
    {
        uv = default;
        distance = 0f;
        GetDomeSphere(out var sphereRadius, out var sphereCenterY, out var maximumPolar);
        var a = direction.sqrMagnitude;
        var b = 2f * (
            Vector3.Dot(origin, direction)
            - sphereCenterY * direction.y);
        var c =
            origin.sqrMagnitude
            - 2f * sphereCenterY * origin.y
            - domeFloorRadius * domeFloorRadius;
        if (!TrySolveQuadratic(a, b, c, out var first, out var second))
        {
            return false;
        }

        return TryGetDomeHitUv(
                origin,
                direction,
                first,
                sphereRadius,
                sphereCenterY,
                maximumPolar,
                out uv,
                out distance)
            || TryGetDomeHitUv(
                origin,
                direction,
                second,
                sphereRadius,
                sphereCenterY,
                maximumPolar,
                out uv,
                out distance);
    }

    private bool TryGetDomeHitUv(
        Vector3 origin,
        Vector3 direction,
        float candidateDistance,
        float sphereRadius,
        float sphereCenterY,
        float maximumPolar,
        out Vector2 uv,
        out float distance)
    {
        uv = default;
        distance = 0f;
        if (candidateDistance <= .0001f)
        {
            return false;
        }

        var hit = origin + direction * candidateDistance;
        var sphereY = hit.y - sphereCenterY;
        var polar = Mathf.Atan2(
            new Vector2(hit.x, hit.z).magnitude,
            sphereY);
        if (polar > maximumPolar + .0001f)
        {
            return false;
        }

        polar = Mathf.Min(polar, maximumPolar);
        var longitude = Mathf.Atan2(hit.x, hit.z);
        if (domeUnwrapMode == DomeUnwrapMode.Equirectangular)
        {
            uv = new Vector2(
                Mathf.Repeat(longitude / (Mathf.PI * 2f) + .5f, 1f),
                Mathf.Clamp01(1f - polar / maximumPolar));
        }
        else
        {
            var radial = domeUnwrapMode == DomeUnwrapMode.DomemasterEqualArea
                ? Mathf.Sin(polar * .5f) / Mathf.Sin(maximumPolar * .5f)
                : polar / maximumPolar;
            uv = new Vector2(
                .5f + .5f * radial * Mathf.Sin(longitude),
                .5f + .5f * radial * Mathf.Cos(longitude));
        }

        distance = candidateDistance;
        return true;
    }

    private static bool TrySolveQuadratic(
        float a,
        float b,
        float c,
        out float first,
        out float second)
    {
        first = 0f;
        second = 0f;
        if (Mathf.Abs(a) <= Mathf.Epsilon)
        {
            return false;
        }

        var discriminant = b * b - 4f * a * c;
        if (discriminant < 0f)
        {
            return false;
        }

        var squareRoot = Mathf.Sqrt(discriminant);
        var q = -.5f * (b + (b >= 0f ? squareRoot : -squareRoot));
        if (Mathf.Abs(q) <= Mathf.Epsilon)
        {
            first = second = -b / (2f * a);
            return true;
        }

        first = q / a;
        second = c / q;
        if (first > second)
        {
            (first, second) = (second, first);
        }

        return true;
    }

    private void UpdateCameraProjection(SurfaceRig rig, Vector3 eyeWorld)
    {
        if (rig.camera == null)
        {
            return;
        }

        var pa = rig.cornersWorld[0];
        var pb = rig.cornersWorld[1];
        var pc = rig.cornersWorld[2];

        var screenRight = (pb - pa).normalized;
        var screenUp = (pc - pa).normalized;
        var screenNormal = Vector3.Cross(screenRight, screenUp).normalized;

        if (Vector3.Dot(screenNormal, eyeWorld - pa) < 0f)
        {
            screenNormal = -screenNormal;
        }

        var cameraForward = -screenNormal;
        if (cameraForward.sqrMagnitude <= 0f)
        {
            return;
        }

        rig.camera.transform.SetPositionAndRotation(
            eyeWorld,
            Quaternion.LookRotation(cameraForward, screenUp));

        var va = pa - eyeWorld;
        var vb = pb - eyeWorld;
        var vc = pc - eyeWorld;

        var distanceToPlane = Vector3.Dot(cameraForward, va);
        if (distanceToPlane <= 0.001f)
        {
            return;
        }

        var near = rig.camera.nearClipPlane;
        var far = rig.camera.farClipPlane;

        var left = Vector3.Dot(screenRight, va) * near / distanceToPlane;
        var right = Vector3.Dot(screenRight, vb) * near / distanceToPlane;
        var bottom = Vector3.Dot(screenUp, va) * near / distanceToPlane;
        var top = Vector3.Dot(screenUp, vc) * near / distanceToPlane;

        rig.camera.projectionMatrix = PerspectiveOffCenter(left, right, bottom, top, near, far);
    }

    private static Matrix4x4 PerspectiveOffCenter(float left, float right, float bottom, float top, float near, float far)
    {
        var m = new Matrix4x4();

        var x = (2f * near) / (right - left);
        var y = (2f * near) / (top - bottom);
        var a = (right + left) / (right - left);
        var b = (top + bottom) / (top - bottom);
        var c = -(far + near) / (far - near);
        var d = -(2f * far * near) / (far - near);

        m[0, 0] = x;
        m[0, 1] = 0f;
        m[0, 2] = a;
        m[0, 3] = 0f;

        m[1, 0] = 0f;
        m[1, 1] = y;
        m[1, 2] = b;
        m[1, 3] = 0f;

        m[2, 0] = 0f;
        m[2, 1] = 0f;
        m[2, 2] = c;
        m[2, 3] = d;

        m[3, 0] = 0f;
        m[3, 1] = 0f;
        m[3, 2] = -1f;
        m[3, 3] = 0f;

        return m;
    }

    private void UpdateSenderState(SurfaceRig rig)
    {
        if (rig.camera == null)
        {
            return;
        }

        var cameraObject = rig.camera.gameObject;
        var streamName = setupShape == SetupShape.Room
            ? rig.id.ToString()
            : setupShape.ToString();
        var outputAvailable = _outputEnabled && rig.camera.enabled;
        var spoutAllowed = outputAvailable
            && enableSpoutSender
            && IsSpoutAllowedOnCurrentGraphicsApi();

        ConfigureSenderComponent(cameraObject, new[] { "SpoutSender" }, spoutAllowed, streamName, rig.camera, rig.renderTexture, true);
        ConfigureSenderComponent(cameraObject, new[] { "NDISender", "NdiSender" }, outputAvailable && enableNdiSender, streamName, rig.camera, rig.renderTexture, true);
    }

    private static bool IsSpoutAllowedOnCurrentGraphicsApi()
    {
        // Klak Spout uses the D3D11 shared texture path.
        return SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11;
    }

    private static void ConfigureSenderComponent(
        GameObject go,
        string[] typeNames,
        bool enabled,
        string streamName,
        Camera sourceCamera,
        RenderTexture sourceTexture,
        bool forceTextureCapture)
    {
        var behaviours = go.GetComponents<MonoBehaviour>();
        for (var i = 0; i < behaviours.Length; i++)
        {
            var behaviour = behaviours[i];
            if (behaviour == null)
            {
                continue;
            }

            if (!MatchesTypeName(behaviour.GetType(), typeNames))
            {
                continue;
            }

            if (!enabled)
            {
                behaviour.enabled = false;
            }

            SetSenderName(behaviour, streamName);
            SetCameraMember(behaviour, "sourceCamera", sourceCamera);
            SetTextureMember(behaviour, "sourceTexture", sourceTexture);

            if (forceTextureCapture)
            {
                SetEnumMemberByName(behaviour, "captureMethod", "Texture");
            }

            if (enabled)
            {
                behaviour.enabled = true;
            }
        }
    }

    private static bool MatchesTypeName(Type type, string[] typeNames)
    {
        for (var i = 0; i < typeNames.Length; i++)
        {
            if (string.Equals(type.Name, typeNames[i], StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void SetSenderName(MonoBehaviour behaviour, string value)
    {
        SetStringMember(behaviour, "spoutName", value);
        SetStringMember(behaviour, "ndiName", value);
        SetStringMember(behaviour, "senderName", value);
        SetStringMember(behaviour, "streamName", value);
    }

    private static void SetStringMember(object target, string memberName, string value)
    {
        var type = target.GetType();
        var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanWrite && property.PropertyType == typeof(string))
        {
            property.SetValue(target, value);
            return;
        }

        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && field.FieldType == typeof(string))
        {
            field.SetValue(target, value);
        }
    }

    private static void SetTextureMember(object target, string memberName, Texture value)
    {
        var type = target.GetType();
        var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanWrite && typeof(Texture).IsAssignableFrom(property.PropertyType))
        {
            property.SetValue(target, value);
            return;
        }

        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && typeof(Texture).IsAssignableFrom(field.FieldType))
        {
            field.SetValue(target, value);
        }
    }

    private static void SetCameraMember(object target, string memberName, Camera value)
    {
        var type = target.GetType();
        var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanWrite && typeof(Camera).IsAssignableFrom(property.PropertyType))
        {
            property.SetValue(target, value);
            return;
        }

        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && typeof(Camera).IsAssignableFrom(field.FieldType))
        {
            field.SetValue(target, value);
        }
    }

    private static void SetEnumMemberByName(object target, string memberName, string enumValueName)
    {
        var type = target.GetType();
        var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanWrite && property.PropertyType.IsEnum)
        {
            var enumValue = FindEnumValue(property.PropertyType, enumValueName);
            if (enumValue != null)
            {
                property.SetValue(target, enumValue);
            }
            return;
        }

        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && field.FieldType.IsEnum)
        {
            var enumValue = FindEnumValue(field.FieldType, enumValueName);
            if (enumValue != null)
            {
                field.SetValue(target, enumValue);
            }
        }
    }

    private static object FindEnumValue(Type enumType, string enumValueName)
    {
        var names = Enum.GetNames(enumType);
        for (var i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], enumValueName, StringComparison.OrdinalIgnoreCase))
            {
                return Enum.Parse(enumType, names[i]);
            }
        }

        return null;
    }

    private void DestroyRig(SurfaceRig rig)
    {
        ReleaseCurvedResources(rig);

        if (rig.runtimeMaterial != null)
        {
            SetMaterialTexture(rig.runtimeMaterial, null);
            SafeDestroy(rig.runtimeMaterial);
        }

        if (rig.renderTexture != null)
        {
            if (rig.camera != null && rig.camera.targetTexture == rig.renderTexture)
            {
                rig.camera.targetTexture = null;
            }

            if (rig.ownsRenderTexture)
            {
                ReleaseAndDestroyRenderTexture(rig.renderTexture);
            }
        }

        SetRenderTextureOutput(rig.id, null);

        if (rig.generatedPreviewMesh != null)
        {
            SafeDestroy(rig.generatedPreviewMesh);
            rig.generatedPreviewMesh = null;
        }

        if (rig.wall != null)
        {
            SafeDestroy(rig.wall);
        }

        if (rig.camera != null)
        {
            SafeDestroy(rig.camera.gameObject);
        }
    }

    private void ReleaseAllResources()
    {
        foreach (var pair in _rigs)
        {
            DestroyRig(pair.Value);
        }

        _rigs.Clear();
        leftRT = null;
        rightRT = null;
        frontRT = null;
        backRT = null;
        floorRT = null;
        ceilingRT = null;
    }

    private static void ReleaseAndDestroyRenderTexture(RenderTexture texture)
    {
        if (texture == null)
        {
            return;
        }

        if (texture.IsCreated())
        {
            texture.Release();
        }

        SafeDestroy(texture);
    }

    private static void SafeDestroy(UnityEngine.Object obj)
    {
        if (obj == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }
}
