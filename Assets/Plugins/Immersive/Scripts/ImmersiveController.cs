using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public sealed class ImmersiveController : MonoBehaviour
{
    public const int CurrentConfigurationVersion = 1;

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

        public float roomWidth;
        public float roomHeight;
        public float roomDepth;
        public RoomAlignmentMode roomAlignment;
        public Vector3 cameraOffsetFromAnchor;

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
        public readonly Vector3[] cornersWorld = new Vector3[4];
    }

    private const string CamerasContainerName = "Cameras";
    private const string WallsContainerName = "Walls";
    private const string PreviewLayerName = "Immersive";
    public const string SubtitleOverlayLayerName = "SubtitleOverlay";
    public const string AimOverlayLayerName = "AimOverlay";

    [Header("References")]
    [SerializeField] private GameObject cameraPrefab;

    [Header("Room Dimensions (meters)")]
    [Min(0.01f)][SerializeField] private float roomWidth = 5f;
    [Min(0.01f)][SerializeField] private float roomHeight = 3f;
    [Min(0.01f)][SerializeField] private float roomDepth = 5f;

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
    private int _subtitleOverlayLayer = -1;
    private int _aimOverlayLayer = -1;
    private bool _requiresSync = true;
    private bool _outputEnabled = true;
    private bool _cameraOffsetEnabled = true;
    private bool _autosavePending;
    private float _autosaveAt;

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

    public bool TryGetSurfaceCamera(SurfaceId surface, out Camera surfaceCamera)
    {
        if (!_outputEnabled)
        {
            surfaceCamera = null;
            return false;
        }

        ProcessPendingChanges();
        if (_rigs.TryGetValue(surface, out var rig) && rig != null && rig.camera != null)
        {
            surfaceCamera = rig.camera;
            return true;
        }

        surfaceCamera = null;
        return false;
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
        _previewLayer = GetOrCreatePreviewLayer();
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
#endif

        if (Application.isPlaying && _autosavePending)
        {
            SaveDefaultConfiguration(out _);
        }

        if (!Application.isPlaying)
        {
            ReleaseAllResources();
        }
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR
        EditorApplication.delayCall -= DelayedProcessPendingChanges;
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
        roomWidth = Mathf.Max(0.01f, roomWidth);
        roomHeight = Mathf.Max(0.01f, roomHeight);
        roomDepth = Mathf.Max(0.01f, roomDepth);
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
            roomWidth = roomWidth,
            roomHeight = roomHeight,
            roomDepth = roomDepth,
            roomAlignment = roomAlignment,
            cameraOffsetFromAnchor = cameraOffsetFromAnchor,
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

        var surfaceTopologyChanged = leftWall != configuration.leftWall
            || rightWall != configuration.rightWall
            || frontWall != configuration.frontWall
            || backWall != configuration.backWall
            || floor != configuration.floor
            || ceiling != configuration.ceiling;

        roomWidth = configuration.roomWidth;
        roomHeight = configuration.roomHeight;
        roomDepth = configuration.roomDepth;
        roomAlignment = configuration.roomAlignment;
        cameraOffsetFromAnchor = configuration.cameraOffsetFromAnchor;

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
        configuration.version = CurrentConfigurationVersion;
        configuration.roomWidth = Mathf.Max(0.01f, configuration.roomWidth);
        configuration.roomHeight = Mathf.Max(0.01f, configuration.roomHeight);
        configuration.roomDepth = Mathf.Max(0.01f, configuration.roomDepth);
        configuration.desiredResolutionValue = Mathf.Max(16, configuration.desiredResolutionValue);
        configuration.resolutionDivider = Mathf.Clamp(configuration.resolutionDivider, 1, 4);
        configuration.depthBufferBits = NormalizeDepthBufferBits(configuration.depthBufferBits);

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
        SyncSingleRig(SurfaceId.Front, frontWall);
        SyncSingleRig(SurfaceId.Back, backWall);
        SyncSingleRig(SurfaceId.Left, leftWall);
        SyncSingleRig(SurfaceId.Right, rightWall);
        SyncSingleRig(SurfaceId.Floor, floor);
        SyncSingleRig(SurfaceId.Ceiling, ceiling);
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

        var rig = new SurfaceRig
        {
            id = id,
            wall = wall.gameObject,
            renderer = renderer,
            camera = camera,
            renderTexture = camera.targetTexture as RenderTexture
        };

        rig.runtimeMaterial = renderer.sharedMaterial;
        return rig;
    }

    private void DestroyExistingRigObjects(SurfaceId id)
    {
        DestroyAllChildrenByExactName(_wallsContainer, id + "_Wall");
        DestroyAllChildrenByExactName(_camerasContainer, id + "_Camera");
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

            UpdatePreviewLayer(rig);
            UpdateSurfaceGeometry(rig);
            UpdateRenderTexture(rig);
            UpdateWallMaterial(rig);
            UpdateCameraProjection(rig, eye);
            UpdateSenderState(rig);
        }
    }

    private void UpdateSurfaceGeometry(SurfaceRig rig)
    {
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
        GetSurfaceData(rig.id, out _, out _, out _, out var wallWidth, out var wallHeight);
        var size = ComputeRenderTextureSize(wallWidth, wallHeight);

        // Scene/editor render textures are never reused in a build. Every active
        // surface owns a texture that can be recreated as settings change.
        if (!rig.ownsRenderTexture && rig.renderTexture != null)
        {
            if (rig.camera != null && rig.camera.targetTexture == rig.renderTexture)
            {
                rig.camera.targetTexture = null;
            }

            ReleaseAndDestroyRenderTexture(rig.renderTexture);
            rig.renderTexture = null;
        }

        var textureNeedsRebuild = rig.renderTexture != null
            && (rig.renderTexture.width != size.x
                || rig.renderTexture.height != size.y
                || rig.renderTexture.depth != depthBufferBits
                || rig.renderTexture.format != renderTextureFormat);

        if (textureNeedsRebuild)
        {
            if (rig.camera != null && rig.camera.targetTexture == rig.renderTexture)
            {
                rig.camera.targetTexture = null;
            }

            if (rig.runtimeMaterial != null)
            {
                SetMaterialTexture(rig.runtimeMaterial, null);
            }

            ReleaseAndDestroyRenderTexture(rig.renderTexture);
            rig.renderTexture = null;
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

        if (rig.ownsRenderTexture)
        {
            rig.renderTexture.name = rig.id.ToString();
        }

        rig.camera.targetTexture = rig.renderTexture;
        SetRenderTextureOutput(rig.id, rig.renderTexture);
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
        return AlignUpToMultiple(resolution, 16);
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

        resolutionWidth = ComputeAlignedResolution(roomWidth, pixelsPerMeter);
        resolutionHeight = ComputeAlignedResolution(roomHeight, pixelsPerMeter);
        resolutionDepth = ComputeAlignedResolution(roomDepth, pixelsPerMeter);
    }

    private float GetReferenceDimensionMeters()
    {
        switch (resolutionMode)
        {
            case ResolutionMode.Width:
                return roomWidth;

            case ResolutionMode.Depth:
                return roomDepth;

            case ResolutionMode.Height:
            default:
                return roomHeight;
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
        var streamName = rig.id.ToString();
        var spoutAllowed = _outputEnabled
            && enableSpoutSender
            && IsSpoutAllowedOnCurrentGraphicsApi();

        ConfigureSenderComponent(cameraObject, new[] { "SpoutSender" }, spoutAllowed, streamName, rig.camera, rig.renderTexture, true);
        ConfigureSenderComponent(cameraObject, new[] { "NDISender", "NdiSender" }, _outputEnabled && enableNdiSender, streamName, rig.camera, rig.renderTexture, true);
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
