using System;
using System.Collections.Generic;
using System.IO;
using BAPointCloudRenderer.CloudController;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
#endif

[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class SettingsMenu : MonoBehaviour
{
    public const int CurrentSettingsVersion = 16;

    private const string PanelSettingsResource = "Immersive/ImmersivePanelSettings";
    private const string StyleSheetResource = "Immersive/ImmersiveRuntimePanel";
    private const string PcVrSpectatorPrefabResource = "PCVR/PcVrSpectatorCamera";
    private const string SettingsFileName = "katabasis-settings.json";

    private enum Category
    {
        Orb,
        PcVr,
        Capture,
        Immersive,
        Rendering,
        Subtitles,
        Navigation,
        Game,
        Settings
    }

    public enum GlobalMode
    {
        Immersive,
        PcVr,
        Capture
    }

    [Serializable]
    public sealed class OrbSettings
    {
        public float panSensitivity = 0.15f;
        public float tiltSensitivity = 0.15f;
        public float panSmoothing = 0.08f;
        public float tiltSmoothing = 0.08f;
        public bool requireRightMouseButton;
        public bool invertPan;
        public bool invertTilt;
        public bool lockPan;
        public bool lockTilt;
        public float viewResetTimeout = 10f;
        public float viewResetDuration = 1f;
        public bool followPathOrientation;
        public float followPathOrientationEntryBlendDuration = 3f;
        public float followPathOrientationSmoothing = 0.35f;
    }

    [Serializable]
    public sealed class GameSettings
    {
        public string language = "en";
        public float globalSpeedMultiplier = 1f;
        public bool followPath = true;
        public MainController.GameResetPoint resetPoint = MainController.GameResetPoint.GameMenu;
        public bool infinitePlaying;
        public bool hideExitPortalsInInfinitePlaying;
        public bool playInterviewIntrosInInfinitePlaying = true;
        public bool demoMode;
        public float demoModeTimeoutSeconds = 60f;
    }

    [Serializable]
    public sealed class AimSettings
    {
        public float verticalOffset = -10f;
        public bool showOverlay;
        public float sizePixels = 36f;
        public float thicknessPixels = 3f;
        public float opacity = .9f;
        public Color color = Color.white;
    }

    [Serializable]
    public sealed class SubtitleSettings
    {
        public bool immersiveMode = true;
        public ImmersiveController.SurfaceId surface = ImmersiveController.SurfaceId.Front;
        public Vector2 position = new Vector2(.5f, .12f);
        public float size = .8f;
    }

    [Serializable]
    public sealed class UnifiedSettings
    {
        public int version = CurrentSettingsVersion;
        public GlobalMode globalMode = GlobalMode.Immersive;
        public OrbSettings orb = new OrbSettings();
        public AimSettings aim = new AimSettings();
        public PcVrSpectatorCamera.RuntimeConfiguration pcVr =
            new PcVrSpectatorCamera.RuntimeConfiguration();
        public CaptureTool.RuntimeConfiguration capture =
            new CaptureTool.RuntimeConfiguration();
        public ImmersiveController.RuntimeConfiguration immersive =
            new ImmersiveController.RuntimeConfiguration();
        public KatabasisMeshConfiguration.RuntimeConfiguration rendering =
            new KatabasisMeshConfiguration.RuntimeConfiguration();
        public SubtitleSettings subtitles = new SubtitleSettings();
        public GameSettings game = new GameSettings();
    }

    [Header("Runtime UI")]
    [SerializeField] private bool enableRuntimeUI = true;
    [SerializeField] private bool runtimeUIStartsOpen;

    [Header("Persistence")]
    [SerializeField] private bool loadSavedSettingsOnStart = true;
    [SerializeField] private bool autosaveRuntimeChanges = true;
    [Min(0f)][SerializeField] private float autosaveDelay = 0.5f;

    private OrbController _orbController;
    private PcVrSpectatorCamera _pcVrSpectatorCamera;
    private CaptureTool _captureTool;
    private ImmersiveController _immersiveController;
    private KatabasisMeshConfiguration _pointCloudConfiguration;
    private DynamicPointCloudSet[] _pointCloudSets = Array.Empty<DynamicPointCloudSet>();
    private MainController _mainController;
    private GameMenu _gameMenu;
    private Subtitles _subtitles;
    private TransformFollower _gazeFollower;
    private GazeAimOverlay _gazeAimOverlay;
    private GameObject _runtimeUIHost;
    private UIDocument _document;
    private VisualElement _window;
    private Button _launcher;
    private Label _status;
    private GlobalMode _globalMode = GlobalMode.Immersive;
    private Category _activeCategory = Category.Orb;
    private Button _immersiveModeButton;
    private Button _pcVrModeButton;
    private Button _captureModeButton;

    private readonly Dictionary<Category, Button> _categoryButtons = new Dictionary<Category, Button>();
    private readonly Dictionary<Category, VisualElement> _categoryContents = new Dictionary<Category, VisualElement>();
    private readonly Dictionary<Salle, Button> _roomButtons = new Dictionary<Salle, Button>();

    private FloatField _panSensitivity;
    private FloatField _tiltSensitivity;
    private FloatField _panSmoothing;
    private FloatField _tiltSmoothing;
    private FloatField _viewResetTimeout;
    private FloatField _viewResetDuration;
    private Toggle _requireRightMouseButton;
    private Toggle _invertPan;
    private Toggle _invertTilt;
    private Toggle _lockPan;
    private Toggle _lockTilt;
    private Toggle _followPathOrientation;
    private FloatField _followPathOrientationEntryBlendDuration;
    private FloatField _followPathOrientationSmoothing;
    private Label _orbReadout;

    private FloatField _gazeVerticalOffset;
    private Toggle _showAimOverlay;
    private Slider _aimSize;
    private Slider _aimThickness;
    private Slider _aimOpacity;
    private TextField _aimColor;
    private Label _aimSummary;

    private FloatField _pcVrPositionSmoothing;
    private FloatField _pcVrRotationSmoothing;
    private FloatField _pcVrMaxPositionSpeed;
    private FloatField _pcVrMaxRotationSpeed;
    private Slider _pcVrHorizonLock;
    private Toggle _pcVrOneEuroEnabled;
    private FloatField _pcVrOneEuroPositionDeadZone;
    private FloatField _pcVrOneEuroRotationDeadZone;
    private FloatField _pcVrOneEuroPositionMinCutoff;
    private FloatField _pcVrOneEuroPositionBeta;
    private FloatField _pcVrOneEuroRotationMinCutoff;
    private FloatField _pcVrOneEuroRotationBeta;
    private FloatField _pcVrPositionX;
    private FloatField _pcVrPositionY;
    private FloatField _pcVrPositionZ;
    private FloatField _pcVrRotationX;
    private FloatField _pcVrRotationY;
    private FloatField _pcVrRotationZ;
    private Slider _pcVrFieldOfView;
    private FloatField _pcVrNearClip;
    private FloatField _pcVrFarClip;
    private IntegerField _pcVrTargetDisplay;
    private IntegerField _pcVrOutputWidth;
    private IntegerField _pcVrOutputHeight;
    private EnumField _pcVrPipCorner;
    private Slider _pcVrPipWidth;
    private IntegerField _pcVrPipMargin;
    private TextField _pcVrStreamName;
    private Toggle _pcVrSpout;
    private Toggle _pcVrNdi;
    private Label _pcVrSummary;
    private EnumField _pcVrPointRenderingMode;
    private Slider _pcVrPointSize;
    private Slider _pcVrPointAlpha;
    private Label _pcVrPointRenderingSummary;

    private Slider _captureFocalDistance;
    private Slider _captureFocalWidth;
    private Slider _captureDotsThreshold;
    private Toggle _captureBlackAndWhite;
    private Slider _captureFieldOfView;
    private TextField _captureScreenshotName;
    private IntegerField _capturePrintWidth;
    private IntegerField _capturePrintHeight;
    private IntegerField _capturePointBudget;
    private Label _captureSummary;

    private EnumField _setupShape;
    private VisualElement _roomSection;
    private VisualElement _roomSurfacesSection;
    private VisualElement _cylinderSection;
    private VisualElement _domeSection;
    private FloatField _roomWidth;
    private FloatField _roomHeight;
    private FloatField _roomDepth;
    private EnumField _roomAlignment;
    private FloatField _cylinderRadius;
    private FloatField _cylinderBaseHeight;
    private FloatField _cylinderPanelHeight;
    private FloatField _cylinderAngle;
    private FloatField _domeFloorRadius;
    private FloatField _domeCenterHeight;
    private EnumField _domeUnwrapMode;
    private FloatField _cameraX;
    private FloatField _cameraY;
    private FloatField _cameraZ;
    private Toggle _leftWall;
    private Toggle _rightWall;
    private Toggle _frontWall;
    private Toggle _backWall;
    private Toggle _floor;
    private Toggle _ceiling;
    private EnumField _resolutionMode;
    private IntegerField _resolutionValue;
    private Toggle _spout;
    private Toggle _ndi;
    private Label _textureSummary;
    private Label _spoutSupport;

    private EnumField _pointRenderingMode;
    private Slider _pointSize;
    private Slider _pointAlpha;
    private Toggle _linkMaxDistanceToCamera;
    private FloatField _pointMaxViewDistance;
    private Slider _pointDistanceFade;
    private Label _pointRenderingSummary;

    private FloatField _subtitlePositionX;
    private FloatField _subtitlePositionY;
    private FloatField _subtitleSize;
    private Toggle _subtitleImmersiveMode;
    private EnumField _subtitleSurface;
    private Label _subtitleSummary;

    private Label _navigationSummary;

    private DropdownField _language;
    private FloatField _globalSpeedMultiplier;
    private EnumField _resetPoint;
    private Toggle _infinitePlaying;
    private Toggle _hideExitPortalsInInfinitePlaying;
    private Toggle _playInterviewIntrosInInfinitePlaying;
    private Toggle _demoMode;
    private FloatField _demoModeTimeoutSeconds;

    private bool _built;
    private bool _refreshing;
    private bool _applyingConfiguration;
    private bool _autosavePending;
    private float _autosaveAt;
    private IVisualElementScheduledItem _runtimeStatusSchedule;
    private IVisualElementScheduledItem _languageChoicesSchedule;

    public string ConfigurationDirectory => Path.Combine(Application.persistentDataPath, "Katabasis");
    public string DefaultConfigurationPath => Path.Combine(ConfigurationDirectory, SettingsFileName);

    private void Start()
    {
        Build();
    }

    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            Build();
        }
    }

    private void Update()
    {
        if (_built && enableRuntimeUI && _document != null && !_document.enabled)
        {
            _document.enabled = true;
        }

        if (!_autosavePending || Time.unscaledTime < _autosaveAt)
        {
            return;
        }

        var success = SaveDefaultConfiguration(out var message);
        SetStatus(message, !success);
    }

    private void OnDisable()
    {
        UnsubscribeFromControllers();

        if (Application.isPlaying && _autosavePending)
        {
            SaveDefaultConfiguration(out _);
        }

        TearDownRuntimeUI();
    }

    private void OnApplicationQuit()
    {
        if (_built)
        {
            SaveDefaultConfiguration(out _);
        }
    }

    private void Build()
    {
        if (_built)
        {
            return;
        }

        ResolveControllers();
        if (_orbController == null
            || _pcVrSpectatorCamera == null
            || _captureTool == null
            || _immersiveController == null
            || _mainController == null)
        {
            Debug.LogError(
                "The unified Settings Menu requires OrbController, PcVrSpectatorCamera, "
                + "CaptureTool, ImmersiveController, and MainController.",
                this);
            enabled = false;
            return;
        }

        _immersiveController.UseExternalConfigurationPersistence();

        var panelSettings = Resources.Load<PanelSettings>(PanelSettingsResource);
        if (panelSettings == null)
        {
            Debug.LogError("Settings Menu PanelSettings resource is missing.", this);
            enabled = false;
            return;
        }

        // A UIDocument automatically joins the document hierarchy formed by its
        // Transform ancestors. This component lives on the XR Origin, whose
        // Camera Offset contains a separate world-space UIDocument. Hosting the
        // settings document at scene root keeps those two panels independent.
        _runtimeUIHost = new GameObject("Katabasis Settings Runtime UI");
        _runtimeUIHost.SetActive(false);
        _document = _runtimeUIHost.AddComponent<UIDocument>();
        _document.panelSettings = panelSettings;
        _document.sortingOrder = 1002;
        _runtimeUIHost.SetActive(true);

        var root = _document.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("Settings Menu failed to create its runtime visual tree.", this);
            DestroyRuntimeUIHost();
            enabled = false;
            return;
        }

        root.Clear();
        root.AddToClassList("immersive-screen");
        root.pickingMode = PickingMode.Ignore;

        var styleSheet = Resources.Load<StyleSheet>(StyleSheetResource);
        if (styleSheet != null)
        {
            root.styleSheets.Add(styleSheet);
        }

        _launcher = new Button(() => SetOpen(true)) { text = "SETTINGS" };
        _launcher.AddToClassList("immersive-launcher");
        root.Add(_launcher);

        _window = new VisualElement { name = "katabasis-settings-window" };
        _window.AddToClassList("immersive-window");
        _window.AddToClassList("settings-window");
        _window.pickingMode = PickingMode.Position;
        root.Add(_window);

        BuildHeader();
        BuildGlobalModeSwitcher();
        BuildCategoryNavigation();

        var scrollView = new ScrollView(ScrollViewMode.Vertical);
        scrollView.AddToClassList("immersive-scroll");
        _window.Add(scrollView);

        BuildOrbCategory(scrollView);
        BuildPcVrCategory(scrollView);
        BuildCaptureCategory(scrollView);
        BuildImmersiveCategory(scrollView);
        BuildRenderingCategory(scrollView);
        BuildSubtitlesCategory(scrollView);
        BuildNavigationCategory(scrollView);
        BuildGameCategory(scrollView);
        BuildSettingsCategory(scrollView);

        _status = new Label("Ready");
        _status.AddToClassList("immersive-status");
        _window.Add(_status);

        _captureTool.SetManagedBySettingsMenu(true);
        _built = true;
        SubscribeToControllers();

        var defaultFileExists = File.Exists(DefaultConfigurationPath);
        var loaded = false;
        if (loadSavedSettingsOnStart && defaultFileExists)
        {
            loaded = ReloadDefaultConfiguration(out var loadMessage);
            SetStatus(loadMessage, !loaded);
        }

        if (loaded)
        {
            RefreshAllControls();
        }
        else if (!defaultFileExists)
        {
            var migrated = TryMigrateLegacyImmersiveConfiguration();
            RefreshAllControls();
            var saved = SaveDefaultConfiguration(out var saveMessage);
            SetStatus(
                migrated && saved ? "Legacy immersive settings migrated to " + DefaultConfigurationPath : saveMessage,
                !saved);
        }
        else
        {
            RefreshAllControls();
            if (!loadSavedSettingsOnStart)
            {
                SetStatus("Saved settings were not loaded; scene values are active.", false);
            }
        }

        ApplyGlobalModeState();
        RefreshAllControls();

        SetCategory(Category.Orb);
        SetOpen(runtimeUIStartsOpen);
        ApplyRuntimeUIVisibility();

        _runtimeStatusSchedule = root.schedule.Execute(RefreshRuntimeStatus).Every(250);
        _languageChoicesSchedule = root.schedule.Execute(RefreshLanguageChoices).Every(1000);
    }

    private void TearDownRuntimeUI()
    {
        _runtimeStatusSchedule?.Pause();
        _languageChoicesSchedule?.Pause();
        _runtimeStatusSchedule = null;
        _languageChoicesSchedule = null;

        _categoryButtons.Clear();
        _categoryContents.Clear();
        _roomButtons.Clear();
        _window = null;
        _launcher = null;
        _status = null;
        _aimSummary = null;
        _pcVrSummary = null;
        _pcVrPointRenderingSummary = null;
        _immersiveModeButton = null;
        _pcVrModeButton = null;
        _captureModeButton = null;
        _captureSummary = null;
        _subtitleSummary = null;
        _navigationSummary = null;
        if (_captureTool != null)
        {
            _captureTool.SetCaptureModeActive(false);
            _captureTool.SetManagedBySettingsMenu(false);
        }
        DestroyRuntimeUIHost();
        _built = false;
        _refreshing = false;
        _applyingConfiguration = false;
    }

    private void DestroyRuntimeUIHost()
    {
        var host = _runtimeUIHost;
        _runtimeUIHost = null;
        _document = null;

        if (host == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(host);
        }
        else
        {
            DestroyImmediate(host);
        }
    }

    private void ResolveControllers()
    {
        _orbController = GetComponentInParent<OrbController>();
        if (_orbController == null)
        {
            _orbController = FindAnyObjectByType<OrbController>(FindObjectsInactive.Include);
        }

        _immersiveController = FindAnyObjectByType<ImmersiveController>(FindObjectsInactive.Include);
        _pcVrSpectatorCamera = FindAnyObjectByType<PcVrSpectatorCamera>(FindObjectsInactive.Include);
        _captureTool = FindAnyObjectByType<CaptureTool>(FindObjectsInactive.Include);
        if (_pcVrSpectatorCamera == null)
        {
            var spectatorPrefab = Resources.Load<PcVrSpectatorCamera>(PcVrSpectatorPrefabResource);
            if (spectatorPrefab != null)
            {
                _pcVrSpectatorCamera = Instantiate(spectatorPrefab);
                _pcVrSpectatorCamera.name = "PC-VR Spectator Camera";
            }
            else
            {
                Debug.LogError(
                    "PC-VR spectator prefab is missing from Resources/"
                    + PcVrSpectatorPrefabResource + ".",
                    this);
            }
        }

        _pointCloudConfiguration = FindAnyObjectByType<KatabasisMeshConfiguration>(FindObjectsInactive.Include);
        _pointCloudSets = FindObjectsByType<DynamicPointCloudSet>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        _mainController = FindAnyObjectByType<MainController>(FindObjectsInactive.Include);
        _subtitles = FindAnyObjectByType<Subtitles>(FindObjectsInactive.Include);
        _gazeFollower = FindGazeFollower();
        _gazeAimOverlay = GetComponent<GazeAimOverlay>();
        if (_gazeAimOverlay == null)
        {
            _gazeAimOverlay = gameObject.AddComponent<GazeAimOverlay>();
        }

        _gazeAimOverlay.Initialize(_gazeFollower, _immersiveController);
        _gameMenu = _mainController != null ? _mainController.menu : null;

        if (_gameMenu == null)
        {
            _gameMenu = FindAnyObjectByType<GameMenu>(FindObjectsInactive.Include);
        }
    }

    private static TransformFollower FindGazeFollower()
    {
        var followers = FindObjectsByType<TransformFollower>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (var index = 0; index < followers.Length; index++)
        {
            if (followers[index] != null
                && followers[index].GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>() != null)
            {
                return followers[index];
            }
        }

        return followers.Length > 0 ? followers[0] : null;
    }

    private void BuildHeader()
    {
        var header = new VisualElement();
        header.AddToClassList("immersive-header");

        var titleGroup = new VisualElement();
        titleGroup.AddToClassList("immersive-title-group");
        titleGroup.Add(new Label("KATABASIS SETTINGS") { name = "immersive-title" });
        titleGroup.Add(new Label("Orb, PC-VR spectator, capture, immersive output, rendering, subtitles, navigation & game configuration") { name = "immersive-subtitle" });
        header.Add(titleGroup);

        var close = new Button(() => SetOpen(false)) { text = "X", tooltip = "Close settings" };
        close.AddToClassList("immersive-close");
        header.Add(close);
        _window.Add(header);
    }

    private void BuildGlobalModeSwitcher()
    {
        var switcher = new VisualElement();
        switcher.AddToClassList("settings-global-mode");

        var label = new Label("GLOBAL MODE");
        label.AddToClassList("settings-global-mode-label");
        switcher.Add(label);

        var buttons = new VisualElement();
        buttons.AddToClassList("settings-global-mode-buttons");
        _immersiveModeButton = new Button(() => SetGlobalMode(GlobalMode.Immersive, true))
        {
            text = "IMMERSIVE"
        };
        _pcVrModeButton = new Button(() => SetGlobalMode(GlobalMode.PcVr, true))
        {
            text = "PC-VR"
        };
        _captureModeButton = new Button(() => SetGlobalMode(GlobalMode.Capture, true))
        {
            text = "CAPTURE"
        };
        _immersiveModeButton.AddToClassList("settings-global-mode-button");
        _pcVrModeButton.AddToClassList("settings-global-mode-button");
        _captureModeButton.AddToClassList("settings-global-mode-button");
        buttons.Add(_immersiveModeButton);
        buttons.Add(_pcVrModeButton);
        buttons.Add(_captureModeButton);
        switcher.Add(buttons);
        _window.Add(switcher);

        RefreshGlobalModeControls();
    }

    private void SetGlobalMode(GlobalMode mode, bool apply)
    {
        _globalMode = mode;
        RefreshGlobalModeControls();
        if (apply)
        {
            ApplyControls();
        }
    }

    private void RefreshGlobalModeControls()
    {
        var pcVrMode = _globalMode == GlobalMode.PcVr;
        var captureMode = _globalMode == GlobalMode.Capture;
        _immersiveModeButton?.EnableInClassList(
            "settings-global-mode-button-active",
            !pcVrMode && !captureMode);
        _pcVrModeButton?.EnableInClassList(
            "settings-global-mode-button-active",
            pcVrMode);
        _captureModeButton?.EnableInClassList(
            "settings-global-mode-button-active",
            captureMode);

        if (_categoryContents.TryGetValue(Category.Orb, out var orbContent))
        {
            orbContent.SetEnabled(!pcVrMode);
        }

        foreach (var pair in _categoryButtons)
        {
            pair.Value.style.display = IsCategoryAvailable(pair.Key)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        if (_categoryContents.Count > 0)
        {
            SetCategory(_activeCategory);
        }
    }

    private void BuildCategoryNavigation()
    {
        var navigation = new VisualElement();
        navigation.AddToClassList("settings-category-navigation");

        AddCategoryButton(navigation, Category.Orb, "ORB");
        AddCategoryButton(navigation, Category.PcVr, "PC-VR");
        AddCategoryButton(navigation, Category.Capture, "CAPTURE");
        AddCategoryButton(navigation, Category.Immersive, "ROOM SETUP");
        AddCategoryButton(navigation, Category.Rendering, "RENDERING");
        AddCategoryButton(navigation, Category.Subtitles, "SUBTITLES");
        AddCategoryButton(navigation, Category.Navigation, "NAVIGATION");
        AddCategoryButton(navigation, Category.Game, "GAME");
        AddCategoryButton(navigation, Category.Settings, "SETTINGS");

        _window.Add(navigation);
    }

    private void AddCategoryButton(VisualElement parent, Category category, string label)
    {
        var button = new Button(() => SetCategory(category)) { text = label };
        button.AddToClassList("settings-category-button");
        button.AddToClassList(GetCategoryStyleClass(category));
        parent.Add(button);
        _categoryButtons[category] = button;
    }

    private VisualElement CreateCategoryContent(VisualElement parent, Category category)
    {
        var content = new VisualElement();
        content.AddToClassList("settings-category-content");
        content.AddToClassList(GetCategoryStyleClass(category));
        parent.Add(content);
        _categoryContents[category] = content;
        return content;
    }

    private static string GetCategoryStyleClass(Category category)
    {
        return "settings-category-accent-" + category.ToString().ToLowerInvariant();
    }

    private void BuildOrbCategory(VisualElement parent)
    {
        var content = CreateCategoryContent(parent, Category.Orb);

        var movement = CreateSection(content, "MOVEMENT", "Mouse motion with an optional RMB gate");
        _requireRightMouseButton = CreateToggle(movement, "Require right mouse button");
        _requireRightMouseButton.AddToClassList("immersive-toggle-wide");
        _panSensitivity = CreateFloatField(movement, "Pan sensitivity (deg/pixel)");
        _tiltSensitivity = CreateFloatField(movement, "Tilt sensitivity (deg/pixel)");
        _panSmoothing = CreateFloatField(movement, "Pan smoothing (seconds)");
        _tiltSmoothing = CreateFloatField(movement, "Tilt smoothing (seconds)");

        var direction = CreateSection(content, "DIRECTION", "Invert each axis independently");
        var directionGrid = CreateToggleGrid(direction);
        _invertPan = CreateToggle(directionGrid, "Invert pan");
        _invertTilt = CreateToggle(directionGrid, "Invert tilt");

        var locks = CreateSection(content, "AXIS LOCKS", "Lock each axis independently");
        var lockGrid = CreateToggleGrid(locks);
        _lockPan = CreateToggle(lockGrid, "Lock pan");
        _lockTilt = CreateToggle(lockGrid, "Lock tilt");

        var orientation = CreateSection(content, "PATH ORIENTATION", "Rotate the rig along the tunnel tangent");
        _followPathOrientation = CreateToggle(orientation, "Follow path orientation");
        _followPathOrientation.AddToClassList("immersive-toggle-wide");
        _followPathOrientationEntryBlendDuration =
            CreateFloatField(orientation, "Tunnel entry blend (seconds)");
        _followPathOrientationSmoothing =
            CreateFloatField(orientation, "Continuous smoothing (seconds)");

        var reset = CreateSection(content, "VIEW RESET", "Zero disables the idle reset");
        _viewResetTimeout = CreateFloatField(reset, "Idle timeout (seconds)");
        _viewResetDuration = CreateFloatField(reset, "Reset duration (seconds)");
        var resetButtons = CreateButtonRow(reset);
        resetButtons.Add(CreateButton("Reset view now", () => _orbController.ResetView(), true));
        _orbReadout = new Label();
        _orbReadout.AddToClassList("immersive-texture-summary");
        reset.Add(_orbReadout);

        BuildAimingSections(content);

        RegisterLiveToggle(_requireRightMouseButton);
        RegisterLiveField(_panSensitivity);
        RegisterLiveField(_tiltSensitivity);
        RegisterLiveField(_panSmoothing);
        RegisterLiveField(_tiltSmoothing);
        RegisterLiveToggle(_invertPan);
        RegisterLiveToggle(_invertTilt);
        RegisterLiveToggle(_lockPan);
        RegisterLiveToggle(_lockTilt);
        RegisterLiveToggle(_followPathOrientation);
        RegisterLiveField(_followPathOrientationEntryBlendDuration);
        RegisterLiveField(_followPathOrientationSmoothing);
        RegisterLiveField(_viewResetTimeout);
        RegisterLiveField(_viewResetDuration);
    }

    private void BuildAimingSections(VisualElement content)
    {
        var compensation = CreateSection(
            content,
            "GAZE COMPENSATION",
            "Pitch applied to portal and interview selection in immersive builds");
        _gazeVerticalOffset = CreateFloatField(compensation, "Vertical offset (degrees)");

        var overlay = CreateSection(
            content,
            "AIM CIRCLE",
            "Fixed theoretical aim point without gaze stabilization or smoothing");
        _showAimOverlay = CreateToggle(overlay, "Show aim circle");
        _showAimOverlay.AddToClassList("immersive-toggle-wide");

        _aimSize = new Slider("Diameter (pixels)", 4f, 512f) { showInputField = true };
        _aimSize.AddToClassList("immersive-field");
        overlay.Add(_aimSize);

        _aimThickness = new Slider("Thickness (pixels)", .5f, 128f) { showInputField = true };
        _aimThickness.AddToClassList("immersive-field");
        overlay.Add(_aimThickness);

        _aimOpacity = new Slider("Opacity", 0f, 1f) { showInputField = true };
        _aimOpacity.AddToClassList("immersive-field");
        overlay.Add(_aimOpacity);

        _aimColor = new TextField("Color (hex RGB)") { isDelayed = true };
        _aimColor.AddToClassList("immersive-field");
        overlay.Add(_aimColor);

        _aimSummary = new Label();
        _aimSummary.AddToClassList("immersive-texture-summary");
        overlay.Add(_aimSummary);

        RegisterLiveField(_gazeVerticalOffset);
        RegisterLiveToggle(_showAimOverlay);
        _aimSize.RegisterValueChangedCallback(_ => ApplyControls());
        _aimThickness.RegisterValueChangedCallback(_ => ApplyControls());
        _aimOpacity.RegisterValueChangedCallback(_ => ApplyControls());
        _aimColor.RegisterValueChangedCallback(_ => ApplyControls());
    }

    private void BuildPcVrCategory(VisualElement parent)
    {
        var content = CreateCategoryContent(parent, Category.PcVr);

        var mode = CreateSection(
            content,
            "SPECTATOR VIEW",
            "Enabled by the Global Mode switch; follows the XR headset without changing its view");
        var modeButtons = CreateButtonRow(mode);
        modeButtons.Add(CreateButton(
            "Snap to player now",
            () => _pcVrSpectatorCamera.SnapToSource(),
            true));
        _pcVrSummary = new Label();
        _pcVrSummary.AddToClassList("immersive-texture-summary");
        mode.Add(_pcVrSummary);

        var appearance = CreateSection(
            content,
            "SPECTATOR POINT APPEARANCE",
            "Overrides point mode, size, and alpha only in the PC-VR spectator camera");
        _pcVrPointRenderingMode = new EnumField(
            "Render mode",
            KatabasisMeshConfiguration.PointRenderingMode.Point);
        _pcVrPointRenderingMode.AddToClassList("immersive-field");
        appearance.Add(_pcVrPointRenderingMode);
        _pcVrPointSize = new Slider("Point diameter (pixels)", .1f, 64f)
        {
            showInputField = true
        };
        _pcVrPointSize.AddToClassList("immersive-field");
        appearance.Add(_pcVrPointSize);
        _pcVrPointAlpha = new Slider("Alpha", 0f, 1f) { showInputField = true };
        _pcVrPointAlpha.AddToClassList("immersive-field");
        appearance.Add(_pcVrPointAlpha);
        _pcVrPointRenderingSummary = new Label();
        _pcVrPointRenderingSummary.AddToClassList("immersive-texture-summary");
        appearance.Add(_pcVrPointRenderingSummary);

        var smoothing = CreateSection(
            content,
            "AUDIENCE COMFORT",
            "Existing response smoothing stays active; One Euro dead zones hold the view still at rest");
        _pcVrPositionSmoothing = CreateFloatField(smoothing, "Position response time");
        _pcVrRotationSmoothing = CreateFloatField(smoothing, "Rotation response time");
        _pcVrMaxPositionSpeed = CreateFloatField(smoothing, "Maximum position speed (m/s)");
        _pcVrMaxRotationSpeed = CreateFloatField(smoothing, "Maximum rotation speed (deg/s)");
        _pcVrHorizonLock = new Slider("Horizon lock", 0f, 1f) { showInputField = true };
        _pcVrHorizonLock.AddToClassList("immersive-field");
        smoothing.Add(_pcVrHorizonLock);
        _pcVrOneEuroEnabled = CreateToggle(smoothing, "One Euro resting stabilization");
        _pcVrOneEuroEnabled.AddToClassList("immersive-toggle-wide");
        _pcVrOneEuroPositionDeadZone = CreateFloatField(
            smoothing,
            "Resting position dead zone (m)");
        _pcVrOneEuroRotationDeadZone = CreateFloatField(
            smoothing,
            "Resting rotation dead zone (degrees)");
        _pcVrOneEuroPositionMinCutoff = CreateFloatField(
            smoothing,
            "Position low-speed cutoff (Hz)");
        _pcVrOneEuroPositionBeta = CreateFloatField(
            smoothing,
            "Position speed response");
        _pcVrOneEuroRotationMinCutoff = CreateFloatField(
            smoothing,
            "Rotation low-speed cutoff (Hz)");
        _pcVrOneEuroRotationBeta = CreateFloatField(
            smoothing,
            "Rotation speed response");

        var framing = CreateSection(
            content,
            "FRAMING",
            "Local offsets are relative to the tracked player's head");
        var positionLabel = new Label("Position offset (meters)");
        positionLabel.AddToClassList("immersive-inline-label");
        framing.Add(positionLabel);
        var positionRow = CreateRow(framing);
        _pcVrPositionX = CreateFloatField(positionRow, "X", true);
        _pcVrPositionY = CreateFloatField(positionRow, "Y", true);
        _pcVrPositionZ = CreateFloatField(positionRow, "Z", true);

        var rotationLabel = new Label("Rotation offset (degrees)");
        rotationLabel.AddToClassList("immersive-inline-label");
        framing.Add(rotationLabel);
        var rotationRow = CreateRow(framing);
        _pcVrRotationX = CreateFloatField(rotationRow, "Pitch", true);
        _pcVrRotationY = CreateFloatField(rotationRow, "Yaw", true);
        _pcVrRotationZ = CreateFloatField(rotationRow, "Roll", true);

        var lens = CreateSection(content, "LENS", "The headset projection is not modified");
        _pcVrFieldOfView = new Slider("Vertical FOV (degrees)", 10f, 160f) { showInputField = true };
        _pcVrFieldOfView.AddToClassList("immersive-field");
        lens.Add(_pcVrFieldOfView);
        _pcVrNearClip = CreateFloatField(lens, "Near clip (meters)");
        _pcVrFarClip = CreateFloatField(lens, "Far clip (meters)");

        var pip = CreateSection(
            content,
            "PICTURE IN PICTURE",
            "The original player view stays full-screen while the smoothed view is overlaid in a corner");
        _pcVrPipCorner = new EnumField("Corner", PcVrSpectatorCamera.PipCorner.TopRight);
        _pcVrPipCorner.AddToClassList("immersive-field");
        pip.Add(_pcVrPipCorner);
        _pcVrPipWidth = new Slider("Monitor width", .1f, .8f) { showInputField = true };
        _pcVrPipWidth.AddToClassList("immersive-field");
        pip.Add(_pcVrPipWidth);
        _pcVrPipMargin = new IntegerField("Margin (pixels)") { isDelayed = true };
        _pcVrPipMargin.AddToClassList("immersive-field");
        pip.Add(_pcVrPipMargin);
        _pcVrTargetDisplay = new IntegerField("Monitor display (1-8)") { isDelayed = true };
        _pcVrTargetDisplay.AddToClassList("immersive-field");
        pip.Add(_pcVrTargetDisplay);

        var outputs = CreateSection(
            content,
            "SPOUT / NDI",
            "Both protocols publish the clean full-resolution spectator view, without the PiP frame");
        var resolutionRow = CreateRow(outputs);
        _pcVrOutputWidth = new IntegerField("Width") { isDelayed = true };
        _pcVrOutputHeight = new IntegerField("Height") { isDelayed = true };
        _pcVrOutputWidth.AddToClassList("immersive-field");
        _pcVrOutputHeight.AddToClassList("immersive-field");
        _pcVrOutputWidth.AddToClassList("immersive-compact-field");
        _pcVrOutputHeight.AddToClassList("immersive-compact-field");
        resolutionRow.Add(_pcVrOutputWidth);
        resolutionRow.Add(_pcVrOutputHeight);
        _pcVrStreamName = new TextField("Stream name") { isDelayed = true };
        _pcVrStreamName.AddToClassList("immersive-field");
        outputs.Add(_pcVrStreamName);
        _pcVrNdi = CreateToggle(outputs, "NDI sender");
        _pcVrSpout = CreateToggle(outputs, "Spout sender (Direct3D 11)");
        _pcVrNdi.AddToClassList("immersive-toggle-wide");
        _pcVrSpout.AddToClassList("immersive-toggle-wide");

        RegisterLiveEnum(_pcVrPointRenderingMode);
        _pcVrPointSize.RegisterValueChangedCallback(_ => ApplyControls());
        _pcVrPointAlpha.RegisterValueChangedCallback(_ => ApplyControls());
        RegisterLiveField(_pcVrPositionSmoothing);
        RegisterLiveField(_pcVrRotationSmoothing);
        RegisterLiveField(_pcVrMaxPositionSpeed);
        RegisterLiveField(_pcVrMaxRotationSpeed);
        _pcVrHorizonLock.RegisterValueChangedCallback(_ => ApplyControls());
        RegisterLiveToggle(_pcVrOneEuroEnabled);
        RegisterLiveField(_pcVrOneEuroPositionDeadZone);
        RegisterLiveField(_pcVrOneEuroRotationDeadZone);
        RegisterLiveField(_pcVrOneEuroPositionMinCutoff);
        RegisterLiveField(_pcVrOneEuroPositionBeta);
        RegisterLiveField(_pcVrOneEuroRotationMinCutoff);
        RegisterLiveField(_pcVrOneEuroRotationBeta);
        RegisterLiveField(_pcVrPositionX);
        RegisterLiveField(_pcVrPositionY);
        RegisterLiveField(_pcVrPositionZ);
        RegisterLiveField(_pcVrRotationX);
        RegisterLiveField(_pcVrRotationY);
        RegisterLiveField(_pcVrRotationZ);
        _pcVrFieldOfView.RegisterValueChangedCallback(_ => ApplyControls());
        RegisterLiveField(_pcVrNearClip);
        RegisterLiveField(_pcVrFarClip);
        RegisterLiveEnum(_pcVrPipCorner);
        _pcVrPipWidth.RegisterValueChangedCallback(_ => ApplyControls());
        _pcVrPipMargin.RegisterValueChangedCallback(_ => ApplyControls());
        _pcVrTargetDisplay.RegisterValueChangedCallback(_ => ApplyControls());
        _pcVrOutputWidth.RegisterValueChangedCallback(_ => ApplyControls());
        _pcVrOutputHeight.RegisterValueChangedCallback(_ => ApplyControls());
        _pcVrStreamName.RegisterValueChangedCallback(_ => ApplyControls());
        RegisterLiveToggle(_pcVrNdi);
        RegisterLiveToggle(_pcVrSpout);
    }

    private void BuildCaptureCategory(VisualElement parent)
    {
        var content = CreateCategoryContent(parent, Category.Capture);

        var focus = CreateSection(
            content,
            "FOCAL EFFECT",
            "Enabled while Capture is the active Global Mode");
        _captureFocalDistance = new Slider("Focal distance", 0f, 60f)
        {
            showInputField = true
        };
        _captureFocalDistance.AddToClassList("immersive-field");
        focus.Add(_captureFocalDistance);

        _captureFocalWidth = new Slider("Focal width", 0.0001f, 60f)
        {
            showInputField = true
        };
        _captureFocalWidth.AddToClassList("immersive-field");
        focus.Add(_captureFocalWidth);

        var monochrome = CreateSection(
            content,
            "BLACK & WHITE",
            "Threshold is perceptually remapped before being sent to the point shader");
        _captureBlackAndWhite = CreateToggle(monochrome, "Enable black and white");
        _captureBlackAndWhite.AddToClassList("immersive-toggle-wide");
        _captureDotsThreshold = new Slider("White threshold", 0f, 1f)
        {
            showInputField = true
        };
        _captureDotsThreshold.AddToClassList("immersive-field");
        monochrome.Add(_captureDotsThreshold);

        var lens = CreateSection(content, "LENS", "Applied to the player and capture cameras");
        _captureFieldOfView = new Slider("Vertical FOV (degrees)", 1f, 179f)
        {
            showInputField = true
        };
        _captureFieldOfView.AddToClassList("immersive-field");
        lens.Add(_captureFieldOfView);

        var capture = CreateSection(
            content,
            "PNG CAPTURE",
            "Print dimensions use 2 pixels per millimeter; zero uses the current screen dimension");
        _captureScreenshotName = new TextField("Screenshot name") { isDelayed = true };
        _captureScreenshotName.AddToClassList("immersive-field");
        capture.Add(_captureScreenshotName);

        var printSize = CreateRow(capture);
        _capturePrintWidth = new IntegerField("Width (mm)") { isDelayed = true };
        _capturePrintHeight = new IntegerField("Height (mm)") { isDelayed = true };
        _capturePrintWidth.AddToClassList("immersive-field");
        _capturePrintHeight.AddToClassList("immersive-field");
        _capturePrintWidth.AddToClassList("immersive-compact-field");
        _capturePrintHeight.AddToClassList("immersive-compact-field");
        printSize.Add(_capturePrintWidth);
        printSize.Add(_capturePrintHeight);

        var captureButtons = CreateButtonRow(capture);
        captureButtons.Add(CreateButton("Capture PNG", CaptureFrameFromSettings, true));

        _captureSummary = new Label();
        _captureSummary.AddToClassList("immersive-texture-summary");
        capture.Add(_captureSummary);

        var density = CreateSection(
            content,
            "POINT DENSITY",
            "Changing the budget updates the point-cloud set used by KataDraw");
        _capturePointBudget = new IntegerField("Point budget") { isDelayed = true };
        _capturePointBudget.AddToClassList("immersive-field");
        density.Add(_capturePointBudget);
        var budgetButtons = CreateButtonRow(density);
        budgetButtons.Add(CreateButton("Apply point budget", ApplyCapturePointBudget, true));

        _captureFocalDistance.RegisterValueChangedCallback(_ => ApplyControls());
        _captureFocalWidth.RegisterValueChangedCallback(_ => ApplyControls());
        RegisterLiveToggle(_captureBlackAndWhite);
        _captureDotsThreshold.RegisterValueChangedCallback(_ => ApplyControls());
        _captureFieldOfView.RegisterValueChangedCallback(_ => ApplyControls());
        _captureScreenshotName.RegisterValueChangedCallback(_ => ApplyControls());
        _capturePrintWidth.RegisterValueChangedCallback(_ => ApplyControls());
        _capturePrintHeight.RegisterValueChangedCallback(_ => ApplyControls());
        _capturePointBudget.RegisterValueChangedCallback(_ => ApplyControls());
    }

    private void BuildImmersiveCategory(VisualElement parent)
    {
        var content = CreateCategoryContent(parent, Category.Immersive);

        var setup = CreateSection(
            content,
            "SETUP SHAPE",
            "Choose the geometry used by the immersive output");
        _setupShape = new EnumField("Shape", ImmersiveController.SetupShape.Room);
        _setupShape.tooltip =
            "Room uses the existing planar surfaces. Cylinder and dome use one curved output.";
        _setupShape.AddToClassList("immersive-field");
        setup.Add(_setupShape);

        _roomSection = CreateSection(content, "ROOM GEOMETRY", "Dimensions are in meters");
        var dimensionRow = CreateRow(_roomSection);
        _roomWidth = CreateFloatField(dimensionRow, "Width", true);
        _roomHeight = CreateFloatField(dimensionRow, "Height", true);
        _roomDepth = CreateFloatField(dimensionRow, "Depth", true);

        _roomAlignment = new EnumField("Alignment", ImmersiveController.RoomAlignmentMode.FrontWall);
        _roomAlignment.tooltip =
            "Selects the floor-level room point that remains fixed when dimensions change.";
        _roomAlignment.AddToClassList("immersive-field");
        _roomSection.Add(_roomAlignment);

        _cylinderSection = CreateSection(
            content,
            "CYLINDER GEOMETRY",
            "Radius, elevations and angular span are parametric");
        var cylinderSizeRow = CreateRow(_cylinderSection);
        _cylinderRadius = CreateFloatField(cylinderSizeRow, "Radius", true);
        _cylinderRadius.tooltip = "Cylinder radius in meters.";
        _cylinderBaseHeight = CreateFloatField(cylinderSizeRow, "Base Y", true);
        _cylinderBaseHeight.tooltip =
            "Height of the lower panel edge above the setup floor, in meters.";
        var cylinderPanelRow = CreateRow(_cylinderSection);
        _cylinderPanelHeight = CreateFloatField(cylinderPanelRow, "Panel height", true);
        _cylinderPanelHeight.tooltip = "Vertical panel height in meters.";
        _cylinderAngle = CreateFloatField(cylinderPanelRow, "Arc angle", true);
        _cylinderAngle.tooltip = "Horizontal angular span in degrees.";

        _domeSection = CreateSection(
            content,
            "DOME GEOMETRY",
            "A spherical cap defined at the floor and center");
        var domeSizeRow = CreateRow(_domeSection);
        _domeFloorRadius = CreateFloatField(domeSizeRow, "Floor radius", true);
        _domeFloorRadius.tooltip = "Radius of the dome footprint on the floor, in meters.";
        _domeCenterHeight = CreateFloatField(domeSizeRow, "Center height", true);
        _domeCenterHeight.tooltip =
            "Height of the dome at its center above the setup floor, in meters.";
        _domeUnwrapMode = new EnumField(
            "Unwrapping",
            ImmersiveController.DomeUnwrapMode.DomemasterEquidistant);
        _domeUnwrapMode.tooltip =
            "Projection used to unwrap the dome into its rectangular output texture.";
        _domeUnwrapMode.AddToClassList("immersive-field");
        _domeSection.Add(_domeUnwrapMode);

        var camera = CreateSection(
            content,
            "CAMERA",
            "Offset from the selected setup anchor, in meters");
        var cameraRow = CreateRow(camera);
        _cameraX = CreateFloatField(cameraRow, "X", true);
        _cameraY = CreateFloatField(cameraRow, "Y", true);
        _cameraZ = CreateFloatField(cameraRow, "Z", true);

        _roomSurfacesSection = CreateSection(
            content,
            "ROOM SURFACES",
            "Cameras and textures follow surface state");
        var surfaceGrid = CreateToggleGrid(_roomSurfacesSection);
        _frontWall = CreateToggle(surfaceGrid, "Front");
        _backWall = CreateToggle(surfaceGrid, "Back");
        _leftWall = CreateToggle(surfaceGrid, "Left");
        _rightWall = CreateToggle(surfaceGrid, "Right");
        _floor = CreateToggle(surfaceGrid, "Floor");
        _ceiling = CreateToggle(surfaceGrid, "Ceiling");

        var rendering = CreateSection(content, "TEXTURES", "Render textures rebuild automatically");
        _resolutionMode = new EnumField("Reference dimension", ImmersiveController.ResolutionMode.Height);
        _resolutionMode.AddToClassList("immersive-field");
        rendering.Add(_resolutionMode);

        _resolutionValue = new IntegerField("Reference pixels") { isDelayed = true };
        _resolutionValue.AddToClassList("immersive-field");
        rendering.Add(_resolutionValue);

        _textureSummary = new Label();
        _textureSummary.AddToClassList("immersive-texture-summary");
        rendering.Add(_textureSummary);

        var outputs = CreateSection(content, "OUTPUTS", "Each stream uses its surface name");
        _ndi = CreateToggle(outputs, "NDI senders");
        _spout = CreateToggle(outputs, "Spout senders");
        _ndi.AddToClassList("immersive-toggle-wide");
        _spout.AddToClassList("immersive-toggle-wide");
        _spoutSupport = new Label();
        _spoutSupport.AddToClassList("immersive-hint");
        outputs.Add(_spoutSupport);

        RegisterLiveEnum(_setupShape);
        RegisterLiveField(_roomWidth);
        RegisterLiveField(_roomHeight);
        RegisterLiveField(_roomDepth);
        RegisterLiveField(_cylinderRadius);
        RegisterLiveField(_cylinderBaseHeight);
        RegisterLiveField(_cylinderPanelHeight);
        RegisterLiveField(_cylinderAngle);
        RegisterLiveField(_domeFloorRadius);
        RegisterLiveField(_domeCenterHeight);
        RegisterLiveEnum(_domeUnwrapMode);
        RegisterLiveField(_cameraX);
        RegisterLiveField(_cameraY);
        RegisterLiveField(_cameraZ);
        RegisterLiveEnum(_roomAlignment);
        RegisterLiveToggle(_leftWall);
        RegisterLiveToggle(_rightWall);
        RegisterLiveToggle(_frontWall);
        RegisterLiveToggle(_backWall);
        RegisterLiveToggle(_floor);
        RegisterLiveToggle(_ceiling);
        RegisterLiveEnum(_resolutionMode);
        _resolutionValue.RegisterValueChangedCallback(_ => ApplyControls());
        RegisterLiveToggle(_ndi);
        RegisterLiveToggle(_spout);
    }

    private void BuildRenderingCategory(VisualElement parent)
    {
        var content = CreateCategoryContent(parent, Category.Rendering);

        var appearance = CreateSection(
            content,
            "VR / IMMERSIVE POINT APPEARANCE",
            "PC-VR spectator appearance is configured separately in the PC-VR category");
        _pointRenderingMode = new EnumField("Render mode", KatabasisMeshConfiguration.PointRenderingMode.Point);
        _pointRenderingMode.AddToClassList("immersive-field");
        appearance.Add(_pointRenderingMode);

        _pointSize = new Slider("Point diameter (pixels)", 0.1f, 64f) { showInputField = true };
        _pointSize.AddToClassList("immersive-field");
        appearance.Add(_pointSize);

        _pointAlpha = new Slider("Alpha", 0f, 1f) { showInputField = true };
        _pointAlpha.AddToClassList("immersive-field");
        appearance.Add(_pointAlpha);

        var distance = CreateSection(content, "VIEW DISTANCE", "Fade is a fraction of the effective maximum distance");
        _linkMaxDistanceToCamera = CreateToggle(distance, "Use active camera far distance");
        _linkMaxDistanceToCamera.AddToClassList("immersive-toggle-wide");
        _pointMaxViewDistance = CreateFloatField(distance, "Max view distance (meters)");
        _pointDistanceFade = new Slider("Distance fade", 0f, 1f) { showInputField = true };
        _pointDistanceFade.AddToClassList("immersive-field");
        distance.Add(_pointDistanceFade);

        _pointRenderingSummary = new Label();
        _pointRenderingSummary.AddToClassList("immersive-texture-summary");
        appearance.Add(_pointRenderingSummary);

        RegisterLiveEnum(_pointRenderingMode);
        _pointSize.RegisterValueChangedCallback(_ => ApplyControls());
        _pointAlpha.RegisterValueChangedCallback(_ => ApplyControls());
        RegisterLiveToggle(_linkMaxDistanceToCamera);
        RegisterLiveField(_pointMaxViewDistance);
        _pointDistanceFade.RegisterValueChangedCallback(_ => ApplyControls());
    }

    private void BuildSubtitlesCategory(VisualElement parent)
    {
        var content = CreateCategoryContent(parent, Category.Subtitles);

        var mode = CreateSection(
            content,
            "OUTPUT MODE",
            "PC-VR always shows natural 3D headset subtitles plus a fixed 2D spectator overlay");
        _subtitleImmersiveMode = CreateToggle(mode, "Immersive surface overlay");
        _subtitleImmersiveMode.AddToClassList("immersive-toggle-wide");

        _subtitleSurface = new EnumField("Surface", ImmersiveController.SurfaceId.Front);
        _subtitleSurface.AddToClassList("immersive-field");
        mode.Add(_subtitleSurface);

        var placement = CreateSection(
            content,
            "2D OVERLAY",
            "Normalized output coordinates: X 0-1 left to right, Y 0-1 bottom to top");
        var positionRow = CreateRow(placement);
        _subtitlePositionX = CreateFloatField(positionRow, "X", true);
        _subtitlePositionY = CreateFloatField(positionRow, "Y", true);
        _subtitleSize = CreateFloatField(placement, "Width (fraction of output)");

        _subtitleSummary = new Label();
        _subtitleSummary.AddToClassList("immersive-texture-summary");
        placement.Add(_subtitleSummary);

        RegisterLiveToggle(_subtitleImmersiveMode);
        RegisterLiveEnum(_subtitleSurface);
        RegisterLiveField(_subtitlePositionX);
        RegisterLiveField(_subtitlePositionY);
        RegisterLiveField(_subtitleSize);
    }

    private void BuildGameCategory(VisualElement parent)
    {
        var content = CreateCategoryContent(parent, Category.Game);

        var languageSection = CreateSection(content, "LANGUAGE", "Available choices come from menu/languages.txt");
        var languages = GetLanguageChoices();
        var languageIndex = FindChoiceIndex(languages, _mainController.language);
        _language = new DropdownField("Language", languages, Mathf.Max(0, languageIndex));
        _language.AddToClassList("immersive-field");
        languageSection.Add(_language);

        var playback = CreateSection(content, "PLAYBACK", "Global experience controls");
        _globalSpeedMultiplier = CreateFloatField(playback, "Global speed multiplier");
        _infinitePlaying = CreateToggle(playback, "Infinite playing");
        _infinitePlaying.AddToClassList("immersive-toggle-wide");
        _hideExitPortalsInInfinitePlaying = CreateToggle(playback, "Hide portals to exit salles");
        _hideExitPortalsInInfinitePlaying.AddToClassList("immersive-toggle-wide");
        _playInterviewIntrosInInfinitePlaying = CreateToggle(playback, "Play intro before every interview");
        _playInterviewIntrosInInfinitePlaying.AddToClassList("immersive-toggle-wide");

        var infiniteHint = new Label("The two options above only affect infinite playing. Turning interview intros off always skips them.");
        infiniteHint.AddToClassList("immersive-hint");
        playback.Add(infiniteHint);

        var reset = CreateSection(content, "RESET", "Choose where Reset game starts the experience again");
        _resetPoint = new EnumField("Reset point", _mainController.resetPoint);
        _resetPoint.AddToClassList("immersive-field");
        reset.Add(_resetPoint);

        var demo = CreateSection(content, "DEMO MODE", "Continue automatically when nobody is using or viewing the experience");
        _demoMode = CreateToggle(demo, "Enable demo mode");
        _demoMode.AddToClassList("immersive-toggle-wide");
        _demoModeTimeoutSeconds = CreateFloatField(demo, "Inactivity timeout (seconds)");

        var demoHint = new Label("The timer runs only inside a salle while no interview is playing. Viewer input or movement restarts it.");
        demoHint.AddToClassList("immersive-hint");
        demo.Add(demoHint);

        _language.RegisterValueChangedCallback(_ => ApplyControls());
        RegisterLiveField(_globalSpeedMultiplier);
        RegisterLiveEnum(_resetPoint);
        RegisterLiveToggle(_infinitePlaying);
        RegisterLiveToggle(_hideExitPortalsInInfinitePlaying);
        RegisterLiveToggle(_playInterviewIntrosInInfinitePlaying);
        RegisterLiveToggle(_demoMode);
        RegisterLiveField(_demoModeTimeoutSeconds);
    }

    private void BuildNavigationCategory(VisualElement parent)
    {
        var content = CreateCategoryContent(parent, Category.Navigation);
        var gameSection = CreateSection(content, "GAME FLOW", "Reset or jump directly to a major game state");
        var firstGameRow = CreateButtonRow(gameSection);
        firstGameRow.Add(CreateButton("Reset game", ResetGameFromNavigation, true));
        firstGameRow.Add(CreateButton("Go to menu", GoToMenuFromNavigation));

        var secondGameRow = CreateButtonRow(gameSection);
        secondGameRow.Add(CreateButton("Go to intro", GoToIntroFromNavigation));
        secondGameRow.Add(CreateButton("Go to credits", GoToCreditsFromNavigation));

        var roomsSection = CreateSection(content, "ROOMS", "Teleport immediately to any room in the loaded scene");

        _navigationSummary = new Label();
        _navigationSummary.AddToClassList("immersive-texture-summary");
        roomsSection.Add(_navigationSummary);

        var rooms = FindObjectsByType<Salle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Array.Sort(rooms, CompareRooms);

        if (rooms.Length == 0)
        {
            var noRooms = new Label("No rooms were found in the loaded scene.");
            noRooms.AddToClassList("immersive-hint");
            roomsSection.Add(noRooms);
            RefreshNavigationStatus();
            return;
        }

        VisualElement row = null;
        for (var index = 0; index < rooms.Length; index++)
        {
            var room = rooms[index];
            if (index % 2 == 0)
            {
                row = CreateButtonRow(roomsSection);
            }

            var label = room.isExit ? room.name + " (EXIT)" : room.name;
            var roomButton = CreateButton(label, () => TeleportToRoom(room));
            roomButton.tooltip = "Teleport to " + room.name;
            row.Add(roomButton);
            _roomButtons[room] = roomButton;
        }

        RefreshNavigationStatus();
    }

    private void ResetGameFromNavigation()
    {
        _mainController.ResetGame();
        RefreshNavigationStatus();
        SetStatus("Game reset to " + GetResetPointLabel(_mainController.resetPoint) + ".", false);
    }

    private void GoToMenuFromNavigation()
    {
        _mainController.GoToMenu();
        RefreshNavigationStatus();
        SetStatus("Opened the game menu.", false);
    }

    private void GoToIntroFromNavigation()
    {
        _mainController.GoToIntro();
        RefreshNavigationStatus();
        SetStatus("Started the game intro.", false);
    }

    private void GoToCreditsFromNavigation()
    {
        _mainController.GoToCredits();
        RefreshNavigationStatus();
        SetStatus("Opened the credits.", false);
    }

    private static string GetResetPointLabel(MainController.GameResetPoint resetPoint)
    {
        switch (resetPoint)
        {
            case MainController.GameResetPoint.GameIntro:
                return "Game Intro";
            case MainController.GameResetPoint.GamePlayingAtRoomA:
                return "Game Playing at Room A";
            case MainController.GameResetPoint.GamePlayingAtRoomC:
                return "Game Playing at Room C";
            default:
                return "Game Menu";
        }
    }

    private static int CompareRooms(Salle left, Salle right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        var levelComparison = left.niveau.CompareTo(right.niveau);
        return levelComparison != 0
            ? levelComparison
            : string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase);
    }

    private void TeleportToRoom(Salle room)
    {
        if (_mainController == null || room == null)
        {
            SetStatus("The selected room is no longer available.", true);
            return;
        }

        _mainController.TeleportToSalle(room);
        RefreshNavigationStatus();
        SetStatus("Teleported to " + room.name + ".", false);
    }

    private void BuildSettingsCategory(VisualElement parent)
    {
        var content = CreateCategoryContent(parent, Category.Settings);
        BuildPersistenceSection(content);
    }

    private void BuildPersistenceSection(VisualElement parent)
    {
        var section = CreateSection(parent, "SETTINGS FILE", "All categories use this single file");
        section.AddToClassList("settings-persistence-section");

        var filePath = new Label(DefaultConfigurationPath);
        filePath.AddToClassList("immersive-path");
        section.Add(filePath);

        var primaryButtons = CreateButtonRow(section);
        primaryButtons.Add(CreateButton("Save now", SaveNow, true));
        primaryButtons.Add(CreateButton("Reload saved", ReloadSaved));

        var fileButtons = CreateButtonRow(section);
        fileButtons.Add(CreateButton("Export as...", ExportAs));
        fileButtons.Add(CreateButton("Import...", Import));
        fileButtons.Add(CreateButton("Open folder", OpenConfigurationFolder));

        var clipboardButtons = CreateButtonRow(section);
        clipboardButtons.Add(CreateButton("Copy JSON", CopyJson));
        clipboardButtons.Add(CreateButton("Import clipboard", ImportClipboard));
    }

    private static VisualElement CreateSection(VisualElement parent, string title, string note)
    {
        var section = new VisualElement();
        section.AddToClassList("immersive-section");

        var heading = new VisualElement();
        heading.AddToClassList("immersive-section-heading");
        heading.Add(new Label(title) { name = "immersive-section-title" });
        heading.Add(new Label(note) { name = "immersive-section-note" });
        section.Add(heading);
        parent.Add(section);
        return section;
    }

    private static VisualElement CreateRow(VisualElement parent)
    {
        var row = new VisualElement();
        row.AddToClassList("immersive-row");
        parent.Add(row);
        return row;
    }

    private static VisualElement CreateToggleGrid(VisualElement parent)
    {
        var grid = new VisualElement();
        grid.AddToClassList("immersive-toggle-grid");
        parent.Add(grid);
        return grid;
    }

    private static VisualElement CreateButtonRow(VisualElement parent)
    {
        var row = new VisualElement();
        row.AddToClassList("immersive-button-row");
        parent.Add(row);
        return row;
    }

    private static FloatField CreateFloatField(VisualElement parent, string label, bool compact = false)
    {
        var field = new FloatField(label) { isDelayed = true };
        field.AddToClassList("immersive-field");
        if (compact)
        {
            field.AddToClassList("immersive-compact-field");
        }

        parent.Add(field);
        return field;
    }

    private static Toggle CreateToggle(VisualElement parent, string label)
    {
        var toggle = new Toggle(label);
        toggle.AddToClassList("immersive-toggle");

        var state = new Label("OFF");
        state.AddToClassList("immersive-toggle-state");
        state.pickingMode = PickingMode.Ignore;
        toggle.Add(state);
        toggle.RegisterValueChangedCallback(evt => UpdateToggleState(toggle, evt.newValue));

        parent.Add(toggle);
        return toggle;
    }

    private static Button CreateButton(string label, Action callback, bool primary = false)
    {
        var button = new Button(callback) { text = label };
        button.AddToClassList(primary ? "immersive-button-primary" : "immersive-button");
        return button;
    }

    private static void UpdateToggleState(Toggle toggle, bool isOn)
    {
        var state = toggle.Q<Label>(className: "immersive-toggle-state");
        if (state != null)
        {
            state.text = isOn ? "ON" : "OFF";
        }
    }

    private static void SetToggleWithoutNotify(Toggle toggle, bool value)
    {
        toggle.SetValueWithoutNotify(value);
        UpdateToggleState(toggle, value);
    }

    private void RegisterLiveField(FloatField field)
    {
        field.RegisterValueChangedCallback(_ => ApplyControls());
    }

    private void RegisterLiveToggle(Toggle toggle)
    {
        toggle.RegisterValueChangedCallback(_ => ApplyControls());
    }

    private void RegisterLiveEnum(EnumField field)
    {
        field.RegisterValueChangedCallback(_ => ApplyControls());
    }

    private void ApplyControls()
    {
        if (_refreshing || !_built)
        {
            return;
        }

        _applyingConfiguration = true;
        try
        {
            _orbController.PanSensitivity = NonNegative(_panSensitivity.value);
            _orbController.TiltSensitivity = NonNegative(_tiltSensitivity.value);
            _orbController.PanSmoothing = NonNegative(_panSmoothing.value);
            _orbController.TiltSmoothing = NonNegative(_tiltSmoothing.value);
            _orbController.RequireRightMouseButton = _requireRightMouseButton.value;
            _orbController.InvertPan = _invertPan.value;
            _orbController.InvertTilt = _invertTilt.value;
            _orbController.LockPan = _lockPan.value;
            _orbController.LockTilt = _lockTilt.value;
            _orbController.ViewResetTimeout = NonNegative(_viewResetTimeout.value);
            _orbController.ViewResetDuration = NonNegative(_viewResetDuration.value);
            _mainController.followPathOrientation = _followPathOrientation.value;
            _mainController.followPathOrientationEntryBlendDuration =
                NonNegative(_followPathOrientationEntryBlendDuration.value);
            _mainController.followPathOrientationSmoothing =
                NonNegative(_followPathOrientationSmoothing.value);

            if (_gazeFollower != null)
            {
                _gazeFollower.ActiveVerticalOffset = Finite(_gazeVerticalOffset.value);
            }

            if (_gazeAimOverlay != null)
            {
                _gazeAimOverlay.Configure(
                    _showAimOverlay.value,
                    _aimSize.value,
                    _aimThickness.value,
                    _aimOpacity.value,
                    ParseAimColor(_aimColor.value, _gazeAimOverlay.Color));
            }

            var pcVr = _pcVrSpectatorCamera.CaptureConfiguration();
            pcVr.enabled = _globalMode == GlobalMode.PcVr;
            pcVr.pointRenderingMode =
                (KatabasisMeshConfiguration.PointRenderingMode)_pcVrPointRenderingMode.value;
            pcVr.pointSize = _pcVrPointSize.value;
            pcVr.pointAlpha = _pcVrPointAlpha.value;
            pcVr.positionSmoothing = _pcVrPositionSmoothing.value;
            pcVr.rotationSmoothing = _pcVrRotationSmoothing.value;
            pcVr.maxPositionSpeed = _pcVrMaxPositionSpeed.value;
            pcVr.maxRotationSpeed = _pcVrMaxRotationSpeed.value;
            pcVr.horizonLock = _pcVrHorizonLock.value;
            pcVr.oneEuroEnabled = _pcVrOneEuroEnabled.value;
            pcVr.oneEuroPositionDeadZone = _pcVrOneEuroPositionDeadZone.value;
            pcVr.oneEuroRotationDeadZone = _pcVrOneEuroRotationDeadZone.value;
            pcVr.oneEuroPositionMinCutoff = _pcVrOneEuroPositionMinCutoff.value;
            pcVr.oneEuroPositionBeta = _pcVrOneEuroPositionBeta.value;
            pcVr.oneEuroRotationMinCutoff = _pcVrOneEuroRotationMinCutoff.value;
            pcVr.oneEuroRotationBeta = _pcVrOneEuroRotationBeta.value;
            pcVr.positionOffset = new Vector3(
                _pcVrPositionX.value,
                _pcVrPositionY.value,
                _pcVrPositionZ.value);
            pcVr.rotationOffset = new Vector3(
                _pcVrRotationX.value,
                _pcVrRotationY.value,
                _pcVrRotationZ.value);
            pcVr.fieldOfView = _pcVrFieldOfView.value;
            pcVr.nearClipPlane = _pcVrNearClip.value;
            pcVr.farClipPlane = _pcVrFarClip.value;
            pcVr.targetDisplay = _pcVrTargetDisplay.value - 1;
            pcVr.outputWidth = _pcVrOutputWidth.value;
            pcVr.outputHeight = _pcVrOutputHeight.value;
            pcVr.pipCorner = (PcVrSpectatorCamera.PipCorner)_pcVrPipCorner.value;
            pcVr.pipWidth = _pcVrPipWidth.value;
            pcVr.pipMargin = _pcVrPipMargin.value;
            pcVr.streamName = _pcVrStreamName.value;
            pcVr.enableSpoutSender = _pcVrSpout.value;
            pcVr.enableNdiSender = _pcVrNdi.value;
            _pcVrSpectatorCamera.ApplyConfiguration(pcVr, false);

            _captureTool.ApplyConfiguration(CaptureToolConfigurationFromControls(), false);

            var immersive = _immersiveController.CaptureConfiguration();
            immersive.setupShape = (ImmersiveController.SetupShape)_setupShape.value;
            immersive.roomWidth = _roomWidth.value;
            immersive.roomHeight = _roomHeight.value;
            immersive.roomDepth = _roomDepth.value;
            immersive.roomAlignment = (ImmersiveController.RoomAlignmentMode)_roomAlignment.value;
            immersive.cylinderRadius = _cylinderRadius.value;
            immersive.cylinderBaseHeight = _cylinderBaseHeight.value;
            immersive.cylinderPanelHeight = _cylinderPanelHeight.value;
            immersive.cylinderAngle = _cylinderAngle.value;
            immersive.domeFloorRadius = _domeFloorRadius.value;
            immersive.domeCenterHeight = _domeCenterHeight.value;
            immersive.domeUnwrapMode =
                (ImmersiveController.DomeUnwrapMode)_domeUnwrapMode.value;
            immersive.cameraOffsetFromAnchor = new Vector3(_cameraX.value, _cameraY.value, _cameraZ.value);
            immersive.leftWall = _leftWall.value;
            immersive.rightWall = _rightWall.value;
            immersive.frontWall = _frontWall.value;
            immersive.backWall = _backWall.value;
            immersive.floor = _floor.value;
            immersive.ceiling = _ceiling.value;
            immersive.resolutionMode = (ImmersiveController.ResolutionMode)_resolutionMode.value;
            immersive.desiredResolutionValue = _resolutionValue.value;
            immersive.enableSpoutSender = _spout.value;
            immersive.enableNdiSender = _ndi.value;
            _immersiveController.ApplyConfiguration(immersive, false);
            ApplyGlobalModeState();

            var rendering = CapturePointCloudRenderingConfiguration();
            rendering.renderingMode = (KatabasisMeshConfiguration.PointRenderingMode)_pointRenderingMode.value;
            rendering.pointSize = _pointSize.value;
            rendering.alpha = _pointAlpha.value;
            rendering.linkMaxDistanceToCamera = _linkMaxDistanceToCamera.value;
            rendering.maxViewDistance = _pointMaxViewDistance.value;
            rendering.distanceFade = _pointDistanceFade.value;
            _pointCloudConfiguration?.ApplyConfiguration(rendering);

            if (_subtitles != null)
            {
                _subtitles.ImmersiveMode = _subtitleImmersiveMode.value;
                _subtitles.ImmersiveSurface = (ImmersiveController.SurfaceId)_subtitleSurface.value;
                _subtitles.ImmersivePosition = new Vector2(
                    Mathf.Clamp01(Finite(_subtitlePositionX.value)),
                    Mathf.Clamp01(Finite(_subtitlePositionY.value)));
                _subtitles.ImmersiveSize = Mathf.Clamp(
                    NonNegative(_subtitleSize.value),
                    .01f,
                    2f);
            }

            _mainController.language = string.IsNullOrWhiteSpace(_language.value)
                ? "en"
                : _language.value.Trim();
            _mainController.globalSpeedMultiplier = NonNegative(_globalSpeedMultiplier.value);
            _mainController.resetPoint = (MainController.GameResetPoint)_resetPoint.value;
            _mainController.infinitePlaying = _infinitePlaying.value;
            _mainController.hideExitPortalsInInfinitePlaying = _hideExitPortalsInInfinitePlaying.value;
            _mainController.playInterviewIntrosInInfinitePlaying = _playInterviewIntrosInInfinitePlaying.value;
            _mainController.demoMode = _demoMode.value;
            _mainController.demoModeTimeoutSeconds = Mathf.Max(1f, NonNegative(_demoModeTimeoutSeconds.value));
            _gameMenu?.SelectLanguage(_mainController.language);
        }
        finally
        {
            _applyingConfiguration = false;
        }

        RefreshAllControls();
        QueueAutosave();
    }

    private void RefreshAllControls()
    {
        if (!_built)
        {
            return;
        }

        _refreshing = true;

        RefreshGlobalModeControls();

        _panSensitivity.SetValueWithoutNotify(_orbController.PanSensitivity);
        _tiltSensitivity.SetValueWithoutNotify(_orbController.TiltSensitivity);
        _panSmoothing.SetValueWithoutNotify(_orbController.PanSmoothing);
        _tiltSmoothing.SetValueWithoutNotify(_orbController.TiltSmoothing);
        _viewResetTimeout.SetValueWithoutNotify(_orbController.ViewResetTimeout);
        _viewResetDuration.SetValueWithoutNotify(_orbController.ViewResetDuration);
        SetToggleWithoutNotify(_requireRightMouseButton, _orbController.RequireRightMouseButton);
        SetToggleWithoutNotify(_invertPan, _orbController.InvertPan);
        SetToggleWithoutNotify(_invertTilt, _orbController.InvertTilt);
        SetToggleWithoutNotify(_lockPan, _orbController.LockPan);
        SetToggleWithoutNotify(_lockTilt, _orbController.LockTilt);
        SetToggleWithoutNotify(_followPathOrientation, _mainController.followPathOrientation);
        _followPathOrientationEntryBlendDuration.SetValueWithoutNotify(
            _mainController.followPathOrientationEntryBlendDuration);
        _followPathOrientationSmoothing.SetValueWithoutNotify(
            _mainController.followPathOrientationSmoothing);

        RefreshAimControls(CaptureAimConfiguration());
        RefreshPcVrControls(_pcVrSpectatorCamera.CaptureConfiguration());
        RefreshCaptureControls(_captureTool.CaptureConfiguration());

        RefreshImmersiveControls(_immersiveController.CaptureConfiguration());
        RefreshPointCloudRenderingControls(CapturePointCloudRenderingConfiguration());
        RefreshSubtitleControls(CaptureSubtitleConfiguration());

        RefreshLanguageChoices();
        _language.SetValueWithoutNotify(_mainController.language);
        _globalSpeedMultiplier.SetValueWithoutNotify(_mainController.globalSpeedMultiplier);
        _resetPoint.SetValueWithoutNotify(_mainController.resetPoint);
        SetToggleWithoutNotify(_infinitePlaying, _mainController.infinitePlaying);
        SetToggleWithoutNotify(_hideExitPortalsInInfinitePlaying, _mainController.hideExitPortalsInInfinitePlaying);
        SetToggleWithoutNotify(_playInterviewIntrosInInfinitePlaying, _mainController.playInterviewIntrosInInfinitePlaying);
        SetToggleWithoutNotify(_demoMode, _mainController.demoMode);
        _demoModeTimeoutSeconds.SetValueWithoutNotify(_mainController.demoModeTimeoutSeconds);
        _demoModeTimeoutSeconds.SetEnabled(_mainController.demoMode);

        _refreshing = false;
        RefreshRuntimeStatus();
    }

    private CaptureTool.RuntimeConfiguration CaptureToolConfigurationFromControls()
    {
        return new CaptureTool.RuntimeConfiguration
        {
            focalDistance = _captureFocalDistance.value,
            focalWidth = _captureFocalWidth.value,
            dotsThreshold = _captureDotsThreshold.value,
            blackAndWhite = _captureBlackAndWhite.value,
            fieldOfView = _captureFieldOfView.value,
            screenshotName = _captureScreenshotName.value,
            printWidthMm = Mathf.Max(0, _capturePrintWidth.value),
            printHeightMm = Mathf.Max(0, _capturePrintHeight.value),
            pointBudget = Mathf.Max(1, _capturePointBudget.value)
        };
    }

    private void RefreshCaptureControls(CaptureTool.RuntimeConfiguration configuration)
    {
        if (!_built || configuration == null)
        {
            return;
        }

        var wasRefreshing = _refreshing;
        _refreshing = true;

        _captureFocalDistance.SetValueWithoutNotify(configuration.focalDistance);
        _captureFocalWidth.SetValueWithoutNotify(configuration.focalWidth);
        _captureDotsThreshold.SetValueWithoutNotify(configuration.dotsThreshold);
        SetToggleWithoutNotify(_captureBlackAndWhite, configuration.blackAndWhite);
        _captureFieldOfView.SetValueWithoutNotify(configuration.fieldOfView);
        _captureScreenshotName.SetValueWithoutNotify(configuration.screenshotName);
        _capturePrintWidth.SetValueWithoutNotify(configuration.printWidthMm);
        _capturePrintHeight.SetValueWithoutNotify(configuration.printHeightMm);
        _capturePointBudget.SetValueWithoutNotify(configuration.pointBudget);
        _captureDotsThreshold.SetEnabled(configuration.blackAndWhite);

        _refreshing = wasRefreshing;
        RefreshCaptureSummary();
    }

    private void ApplyGlobalModeState()
    {
        var immersiveMode = _globalMode == GlobalMode.Immersive;
        var pcVrMode = _globalMode == GlobalMode.PcVr;
        var captureMode = _globalMode == GlobalMode.Capture;
        var pcVr = _pcVrSpectatorCamera.CaptureConfiguration();
        if (pcVr.enabled != pcVrMode)
        {
            pcVr.enabled = pcVrMode;
            _pcVrSpectatorCamera.ApplyConfiguration(pcVr, false);
        }

        _captureTool.SetCaptureModeActive(captureMode);
        _immersiveController.SetCameraOffsetEnabled(
            immersiveMode || IsXrSimulatorEnabledInEditor());
        _immersiveController.SetOutputEnabled(immersiveMode);

        for (var index = 0; index < _pointCloudSets.Length; index++)
        {
            _pointCloudSets[index]?.SetRender360(immersiveMode);
        }

        if (pcVrMode)
        {
            if (_orbController.enabled)
            {
                _orbController.ResetView(true);
                _orbController.enabled = false;
                _pcVrSpectatorCamera.SnapToSource();
            }
        }
        else if (!_orbController.enabled)
        {
            _orbController.enabled = true;
        }

        _gazeFollower?.SetVerticalOffsetEnabled(immersiveMode);
    }

    private static bool IsXrSimulatorEnabledInEditor()
    {
#if UNITY_EDITOR
        var simulators = FindObjectsByType<XRInteractionSimulator>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (var index = 0; index < simulators.Length; index++)
        {
            if (simulators[index].isActiveAndEnabled)
            {
                return true;
            }
        }
#endif

        return false;
    }

    private KatabasisMeshConfiguration.RuntimeConfiguration CapturePointCloudRenderingConfiguration()
    {
        return _pointCloudConfiguration != null
            ? _pointCloudConfiguration.CaptureConfiguration()
            : new KatabasisMeshConfiguration.RuntimeConfiguration();
    }

    private AimSettings CaptureAimConfiguration()
    {
        return new AimSettings
        {
            verticalOffset = _gazeFollower != null
                ? _gazeFollower.ConfiguredVerticalOffset
                : -10f,
            showOverlay = _gazeAimOverlay != null && _gazeAimOverlay.Visible,
            sizePixels = _gazeAimOverlay != null ? _gazeAimOverlay.SizePixels : 36f,
            thicknessPixels = _gazeAimOverlay != null ? _gazeAimOverlay.ThicknessPixels : 3f,
            opacity = _gazeAimOverlay != null ? _gazeAimOverlay.Opacity : .9f,
            color = _gazeAimOverlay != null ? _gazeAimOverlay.Color : Color.white
        };
    }

    private void RefreshAimControls(AimSettings configuration)
    {
        if (!_built || configuration == null)
        {
            return;
        }

        var wasRefreshing = _refreshing;
        _refreshing = true;

        _gazeVerticalOffset.SetValueWithoutNotify(configuration.verticalOffset);
        SetToggleWithoutNotify(_showAimOverlay, configuration.showOverlay);
        _aimSize.SetValueWithoutNotify(configuration.sizePixels);
        _aimThickness.SetValueWithoutNotify(configuration.thicknessPixels);
        _aimOpacity.SetValueWithoutNotify(configuration.opacity);
        _aimColor.SetValueWithoutNotify("#" + ColorUtility.ToHtmlStringRGB(configuration.color));

        var hasFollower = _gazeFollower != null;
        var hasOverlay = hasFollower && _gazeAimOverlay != null && _immersiveController != null;
        _gazeVerticalOffset.SetEnabled(hasFollower);
        _showAimOverlay.SetEnabled(hasOverlay);
        _aimSize.SetEnabled(hasOverlay && configuration.showOverlay);
        _aimThickness.SetEnabled(hasOverlay && configuration.showOverlay);
        _aimOpacity.SetEnabled(hasOverlay && configuration.showOverlay);
        _aimColor.SetEnabled(hasOverlay && configuration.showOverlay);

        _refreshing = wasRefreshing;
    }

    private void RefreshPcVrControls(PcVrSpectatorCamera.RuntimeConfiguration configuration)
    {
        if (!_built || configuration == null)
        {
            return;
        }

        var wasRefreshing = _refreshing;
        _refreshing = true;

        _pcVrPointRenderingMode.SetValueWithoutNotify(configuration.pointRenderingMode);
        _pcVrPointSize.SetValueWithoutNotify(configuration.pointSize);
        _pcVrPointAlpha.SetValueWithoutNotify(configuration.pointAlpha);
        _pcVrPositionSmoothing.SetValueWithoutNotify(configuration.positionSmoothing);
        _pcVrRotationSmoothing.SetValueWithoutNotify(configuration.rotationSmoothing);
        _pcVrMaxPositionSpeed.SetValueWithoutNotify(configuration.maxPositionSpeed);
        _pcVrMaxRotationSpeed.SetValueWithoutNotify(configuration.maxRotationSpeed);
        _pcVrHorizonLock.SetValueWithoutNotify(configuration.horizonLock);
        SetToggleWithoutNotify(_pcVrOneEuroEnabled, configuration.oneEuroEnabled);
        _pcVrOneEuroPositionDeadZone.SetValueWithoutNotify(
            configuration.oneEuroPositionDeadZone);
        _pcVrOneEuroRotationDeadZone.SetValueWithoutNotify(
            configuration.oneEuroRotationDeadZone);
        _pcVrOneEuroPositionMinCutoff.SetValueWithoutNotify(
            configuration.oneEuroPositionMinCutoff);
        _pcVrOneEuroPositionBeta.SetValueWithoutNotify(configuration.oneEuroPositionBeta);
        _pcVrOneEuroRotationMinCutoff.SetValueWithoutNotify(
            configuration.oneEuroRotationMinCutoff);
        _pcVrOneEuroRotationBeta.SetValueWithoutNotify(configuration.oneEuroRotationBeta);
        _pcVrPositionX.SetValueWithoutNotify(configuration.positionOffset.x);
        _pcVrPositionY.SetValueWithoutNotify(configuration.positionOffset.y);
        _pcVrPositionZ.SetValueWithoutNotify(configuration.positionOffset.z);
        _pcVrRotationX.SetValueWithoutNotify(configuration.rotationOffset.x);
        _pcVrRotationY.SetValueWithoutNotify(configuration.rotationOffset.y);
        _pcVrRotationZ.SetValueWithoutNotify(configuration.rotationOffset.z);
        _pcVrFieldOfView.SetValueWithoutNotify(configuration.fieldOfView);
        _pcVrNearClip.SetValueWithoutNotify(configuration.nearClipPlane);
        _pcVrFarClip.SetValueWithoutNotify(configuration.farClipPlane);
        _pcVrTargetDisplay.SetValueWithoutNotify(configuration.targetDisplay + 1);
        _pcVrOutputWidth.SetValueWithoutNotify(configuration.outputWidth);
        _pcVrOutputHeight.SetValueWithoutNotify(configuration.outputHeight);
        _pcVrPipCorner.SetValueWithoutNotify(configuration.pipCorner);
        _pcVrPipWidth.SetValueWithoutNotify(configuration.pipWidth);
        _pcVrPipMargin.SetValueWithoutNotify(configuration.pipMargin);
        _pcVrStreamName.SetValueWithoutNotify(configuration.streamName);
        SetToggleWithoutNotify(_pcVrSpout, configuration.enableSpoutSender);
        SetToggleWithoutNotify(_pcVrNdi, configuration.enableNdiSender);

        _pcVrPointSize.SetEnabled(
            configuration.pointRenderingMode == KatabasisMeshConfiguration.PointRenderingMode.Size);
        _pcVrOneEuroPositionDeadZone.SetEnabled(configuration.oneEuroEnabled);
        _pcVrOneEuroRotationDeadZone.SetEnabled(configuration.oneEuroEnabled);
        _pcVrOneEuroPositionMinCutoff.SetEnabled(configuration.oneEuroEnabled);
        _pcVrOneEuroPositionBeta.SetEnabled(configuration.oneEuroEnabled);
        _pcVrOneEuroRotationMinCutoff.SetEnabled(configuration.oneEuroEnabled);
        _pcVrOneEuroRotationBeta.SetEnabled(configuration.oneEuroEnabled);
        _pcVrPointRenderingSummary.text = configuration.pointRenderingMode
            == KatabasisMeshConfiguration.PointRenderingMode.Size
                ? $"Spectator only | {configuration.pointSize:F1}px circular points | {configuration.pointAlpha:F2} alpha"
                : $"Spectator only | point mode | {configuration.pointAlpha:F2} alpha";

        var hasSource = _pcVrSpectatorCamera.SourceCamera != null;
        _pcVrSummary.EnableInClassList("immersive-warning", configuration.enabled && !hasSource);
        _refreshing = wasRefreshing;
    }

    private SubtitleSettings CaptureSubtitleConfiguration()
    {
        return _subtitles != null
            ? new SubtitleSettings
            {
                immersiveMode = _subtitles.ImmersiveMode,
                surface = _subtitles.ImmersiveSurface,
                position = _subtitles.ImmersivePosition,
                size = _subtitles.ImmersiveSize
            }
            : new SubtitleSettings();
    }

    private void RefreshSubtitleControls(SubtitleSettings configuration)
    {
        if (!_built || configuration == null)
        {
            return;
        }

        var wasRefreshing = _refreshing;
        _refreshing = true;

        _subtitlePositionX.SetValueWithoutNotify(configuration.position.x);
        _subtitlePositionY.SetValueWithoutNotify(configuration.position.y);
        _subtitleSize.SetValueWithoutNotify(configuration.size);
        SetToggleWithoutNotify(_subtitleImmersiveMode, configuration.immersiveMode);
        _subtitleSurface.SetValueWithoutNotify(configuration.surface);

        var controlsEnabled = _subtitles != null;
        _subtitleImmersiveMode.SetEnabled(controlsEnabled);
        var pcVrMode = _globalMode == GlobalMode.PcVr;
        var curvedOutput = _immersiveController != null
            && _immersiveController.CurrentSetupShape != ImmersiveController.SetupShape.Room;
        _subtitleSurface.SetEnabled(
            controlsEnabled
            && configuration.immersiveMode
            && !pcVrMode
            && !curvedOutput);
        _subtitlePositionX.SetEnabled(controlsEnabled && (configuration.immersiveMode || pcVrMode));
        _subtitlePositionY.SetEnabled(controlsEnabled && (configuration.immersiveMode || pcVrMode));
        _subtitleSize.SetEnabled(controlsEnabled && (configuration.immersiveMode || pcVrMode));

        _refreshing = wasRefreshing;
    }

    private void RefreshPointCloudRenderingControls(
        KatabasisMeshConfiguration.RuntimeConfiguration configuration)
    {
        if (!_built || configuration == null)
        {
            return;
        }

        var wasRefreshing = _refreshing;
        _refreshing = true;

        _pointRenderingMode.SetValueWithoutNotify(configuration.renderingMode);
        _pointSize.SetValueWithoutNotify(configuration.pointSize);
        _pointAlpha.SetValueWithoutNotify(configuration.alpha);
        SetToggleWithoutNotify(_linkMaxDistanceToCamera, configuration.linkMaxDistanceToCamera);
        _pointMaxViewDistance.SetValueWithoutNotify(configuration.maxViewDistance);
        _pointDistanceFade.SetValueWithoutNotify(configuration.distanceFade);

        _pointSize.SetEnabled(configuration.renderingMode == KatabasisMeshConfiguration.PointRenderingMode.Size);
        _pointMaxViewDistance.SetEnabled(!configuration.linkMaxDistanceToCamera);

        _refreshing = wasRefreshing;
    }

    private void RefreshImmersiveControls(ImmersiveController.RuntimeConfiguration configuration)
    {
        if (!_built || configuration == null)
        {
            return;
        }

        var wasRefreshing = _refreshing;
        _refreshing = true;

        _setupShape.SetValueWithoutNotify(configuration.setupShape);
        _roomWidth.SetValueWithoutNotify(configuration.roomWidth);
        _roomHeight.SetValueWithoutNotify(configuration.roomHeight);
        _roomDepth.SetValueWithoutNotify(configuration.roomDepth);
        _roomAlignment.SetValueWithoutNotify(configuration.roomAlignment);
        _cylinderRadius.SetValueWithoutNotify(configuration.cylinderRadius);
        _cylinderBaseHeight.SetValueWithoutNotify(configuration.cylinderBaseHeight);
        _cylinderPanelHeight.SetValueWithoutNotify(configuration.cylinderPanelHeight);
        _cylinderAngle.SetValueWithoutNotify(configuration.cylinderAngle);
        _domeFloorRadius.SetValueWithoutNotify(configuration.domeFloorRadius);
        _domeCenterHeight.SetValueWithoutNotify(configuration.domeCenterHeight);
        _domeUnwrapMode.SetValueWithoutNotify(configuration.domeUnwrapMode);
        RefreshImmersiveShapeVisibility(configuration.setupShape);
        _cameraX.SetValueWithoutNotify(configuration.cameraOffsetFromAnchor.x);
        _cameraY.SetValueWithoutNotify(configuration.cameraOffsetFromAnchor.y);
        _cameraZ.SetValueWithoutNotify(configuration.cameraOffsetFromAnchor.z);
        var cameraOffsetEnabled = _globalMode == GlobalMode.Immersive;
        _cameraX.SetEnabled(cameraOffsetEnabled);
        _cameraY.SetEnabled(cameraOffsetEnabled);
        _cameraZ.SetEnabled(cameraOffsetEnabled);
        SetToggleWithoutNotify(_leftWall, configuration.leftWall);
        SetToggleWithoutNotify(_rightWall, configuration.rightWall);
        SetToggleWithoutNotify(_frontWall, configuration.frontWall);
        SetToggleWithoutNotify(_backWall, configuration.backWall);
        SetToggleWithoutNotify(_floor, configuration.floor);
        SetToggleWithoutNotify(_ceiling, configuration.ceiling);
        _resolutionMode.SetValueWithoutNotify(configuration.resolutionMode);
        _resolutionValue.SetValueWithoutNotify(configuration.desiredResolutionValue);
        SetToggleWithoutNotify(_spout, configuration.enableSpoutSender);
        SetToggleWithoutNotify(_ndi, configuration.enableNdiSender);

        _refreshing = wasRefreshing;
        RefreshSubtitleControls(CaptureSubtitleConfiguration());
        RefreshRuntimeStatus();
    }

    private void RefreshImmersiveShapeVisibility(ImmersiveController.SetupShape setupShape)
    {
        var roomVisible = setupShape == ImmersiveController.SetupShape.Room;
        var cylinderVisible = setupShape == ImmersiveController.SetupShape.Cylinder;
        var domeVisible = setupShape == ImmersiveController.SetupShape.Dome;

        if (_roomSection != null)
        {
            _roomSection.style.display = roomVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (_roomSurfacesSection != null)
        {
            _roomSurfacesSection.style.display =
                roomVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (_cylinderSection != null)
        {
            _cylinderSection.style.display =
                cylinderVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (_domeSection != null)
        {
            _domeSection.style.display = domeVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void RefreshRuntimeStatus()
    {
        if (!_built)
        {
            return;
        }

        _orbReadout.text = $"Pan {_orbController.Pan:F1} deg  |  Tilt {_orbController.Tilt:F1} deg";
        RefreshCaptureSummary();
        var pcVr = _pcVrSpectatorCamera.CaptureConfiguration();
        _pcVrSummary.text = _pcVrSpectatorCamera.GetStatusSummary();
        _pcVrSummary.EnableInClassList(
            "immersive-warning",
            pcVr.enabled
            && (_pcVrSpectatorCamera.SourceCamera == null
                || !_pcVrSpectatorCamera.IsTargetDisplayAvailable
                || (pcVr.enableSpoutSender && !_pcVrSpectatorCamera.IsSpoutSupported)));
        _textureSummary.text = _immersiveController.GetRenderTextureSummary();
        var hasSetupWarning = _immersiveController.TryGetSetupWarning(
            out var setupWarning);
        if (hasSetupWarning)
        {
            _textureSummary.text += "\n" + setupWarning;
        }

        _textureSummary.EnableInClassList("immersive-warning", hasSetupWarning);
        _spoutSupport.text = _immersiveController.IsSpoutSupported
            ? "Spout available - Direct3D 11"
            : "Spout configured but inactive - requires Direct3D 11";
        _spoutSupport.EnableInClassList("immersive-warning", !_immersiveController.IsSpoutSupported);
        if (_gazeFollower == null)
        {
            _aimSummary.text = "No TransformFollower gaze source is active in the loaded scene.";
            _aimSummary.EnableInClassList("immersive-warning", true);
        }
        else if (_gazeAimOverlay == null || !_gazeAimOverlay.Visible)
        {
            _aimSummary.text = $"Compensated gaze pitch: {_gazeFollower.ActiveVerticalOffset:F1} degrees | circle hidden";
            _aimSummary.EnableInClassList("immersive-warning", false);
        }
        else if (_gazeAimOverlay.IsRendering)
        {
            _aimSummary.text =
                $"Compensated gaze pitch: {_gazeFollower.ActiveVerticalOffset:F1} degrees | "
                + $"circle on {_gazeAimOverlay.CurrentSurface}";
            _aimSummary.EnableInClassList("immersive-warning", false);
        }
        else
        {
            _aimSummary.text =
                $"Compensated gaze pitch: {_gazeFollower.ActiveVerticalOffset:F1} degrees | "
                + "the aim ray is outside the enabled immersive surfaces";
            _aimSummary.EnableInClassList("immersive-warning", true);
        }

        if (_subtitles == null)
        {
            _subtitleSummary.text = "No subtitle renderer is active in the loaded scene.";
            _subtitleSummary.EnableInClassList("immersive-warning", true);
        }
        else
        {
            if (_globalMode == GlobalMode.PcVr)
            {
                var spectatorAvailable = _pcVrSpectatorCamera != null
                    && _pcVrSpectatorCamera.SpectatorCamera != null;
                _subtitleSummary.EnableInClassList("immersive-warning", !spectatorAvailable);
                _subtitleSummary.text = spectatorAvailable
                    ? "Natural 3D subtitles in the headset + fixed 2D overlay for spectators (display, PiP, Spout and NDI)."
                    : "Natural 3D headset subtitles are active, but no spectator camera is available for the 2D overlay.";
            }
            else
            {
                var curvedOutput = _immersiveController.CurrentSetupShape
                    != ImmersiveController.SetupShape.Room;
                var surfaceAvailable = !_subtitles.ImmersiveMode
                    || _immersiveController.TryGetSurfaceCamera(_subtitles.ImmersiveSurface, out _);
                _subtitleSummary.EnableInClassList("immersive-warning", !surfaceAvailable);
                _subtitleSummary.text = !_subtitles.ImmersiveMode
                    ? "Immersive overlay disabled; standard camera placement is active."
                    : surfaceAvailable
                        ? curvedOutput
                            ? $"Fixed 2D overlay on the {_immersiveController.CurrentSetupShape} output "
                                + "(included in Spout/NDI)."
                            : $"Fixed 2D overlay on {_subtitles.ImmersiveSurface} (included in Spout/NDI)."
                        : curvedOutput
                            ? $"{_immersiveController.CurrentSetupShape} output is unavailable, "
                                + "so no subtitle overlay can be rendered."
                            : $"{_subtitles.ImmersiveSurface} is disabled, "
                                + "so no subtitle overlay can be rendered.";
            }
        }

        RefreshNavigationStatus();

        if (_pointCloudConfiguration == null)
        {
            _pointRenderingSummary.text = "No Katabasis point-cloud renderer is active.";
            _pointRenderingSummary.EnableInClassList("immersive-warning", true);
            return;
        }

        var rendering = _pointCloudConfiguration.CaptureConfiguration();
        _pointRenderingSummary.EnableInClassList("immersive-warning", false);
        _pointRenderingSummary.text = rendering.renderingMode == KatabasisMeshConfiguration.PointRenderingMode.Size
            ? $"Sized circular points | {rendering.pointSize:F1}px diameter | {rendering.alpha:F2} alpha"
            : $"Point mode | 1 render pixel | {rendering.alpha:F2} alpha";
    }

    private void RefreshCaptureSummary()
    {
        if (_captureSummary == null || _captureTool == null)
        {
            return;
        }

        var modeText = _globalMode == GlobalMode.Capture
            ? "Capture effects active"
            : "Select the Capture Global Mode to enable capture effects";
        _captureSummary.text = modeText + " | PNG folder: " + _captureTool.CaptureOutputDirectory;
        _captureSummary.EnableInClassList(
            "immersive-warning",
            _globalMode != GlobalMode.Capture);
    }

    private void RefreshNavigationStatus()
    {
        if (_navigationSummary == null)
        {
            return;
        }

        var currentRoom = _mainController != null ? _mainController.salle : null;
        _navigationSummary.text = currentRoom != null
            ? "Current room: " + currentRoom.name
            : "Current room: none";

        foreach (var pair in _roomButtons)
        {
            if (pair.Value != null)
            {
                pair.Value.EnableInClassList("immersive-button-primary", pair.Key == currentRoom);
            }
        }
    }

    private void RefreshLanguageChoices()
    {
        if (!_built || _language == null)
        {
            return;
        }

        var choices = GetLanguageChoices();
        if (SameChoices(_language.choices, choices))
        {
            return;
        }

        var selected = _language.value;
        _language.choices = choices;
        var selectedIndex = FindChoiceIndex(choices, selected);
        _language.SetValueWithoutNotify(selectedIndex >= 0 ? choices[selectedIndex] : _mainController.language);
    }

    private List<string> GetLanguageChoices()
    {
        var source = _gameMenu != null
            ? _gameMenu.GetAvailableLanguages()
            : new List<string>();
        var choices = new List<string>();

        for (var index = 0; index < source.Count; index++)
        {
            var language = source[index]?.Trim();
            if (!string.IsNullOrWhiteSpace(language) && FindChoiceIndex(choices, language) < 0)
            {
                choices.Add(language);
            }
        }

        if (!string.IsNullOrWhiteSpace(_mainController.language)
            && FindChoiceIndex(choices, _mainController.language) < 0)
        {
            choices.Add(_mainController.language);
        }

        if (choices.Count == 0)
        {
            choices.Add("en");
        }

        return choices;
    }

    private static int FindChoiceIndex(IList<string> choices, string value)
    {
        for (var index = 0; index < choices.Count; index++)
        {
            if (string.Equals(choices[index], value, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool SameChoices(IList<string> left, IList<string> right)
    {
        if (left == null || right == null || left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void SubscribeToControllers()
    {
        _immersiveController.ConfigurationChanged -= OnImmersiveConfigurationChanged;
        _immersiveController.ConfigurationChanged += OnImmersiveConfigurationChanged;
    }

    private void UnsubscribeFromControllers()
    {
        if (_immersiveController != null)
        {
            _immersiveController.ConfigurationChanged -= OnImmersiveConfigurationChanged;
        }
    }

    private void OnImmersiveConfigurationChanged(ImmersiveController.RuntimeConfiguration configuration)
    {
        if (_applyingConfiguration)
        {
            return;
        }

        RefreshImmersiveControls(configuration);
        QueueAutosave();
    }

    private UnifiedSettings CaptureConfiguration()
    {
        var pcVr = _pcVrSpectatorCamera.CaptureConfiguration();
        pcVr.enabled = _globalMode == GlobalMode.PcVr;

        return new UnifiedSettings
        {
            version = CurrentSettingsVersion,
            globalMode = _globalMode,
            orb = new OrbSettings
            {
                panSensitivity = _orbController.PanSensitivity,
                tiltSensitivity = _orbController.TiltSensitivity,
                panSmoothing = _orbController.PanSmoothing,
                tiltSmoothing = _orbController.TiltSmoothing,
                requireRightMouseButton = _orbController.RequireRightMouseButton,
                invertPan = _orbController.InvertPan,
                invertTilt = _orbController.InvertTilt,
                lockPan = _orbController.LockPan,
                lockTilt = _orbController.LockTilt,
                viewResetTimeout = _orbController.ViewResetTimeout,
                viewResetDuration = _orbController.ViewResetDuration,
                followPathOrientation = _mainController.followPathOrientation,
                followPathOrientationEntryBlendDuration =
                    _mainController.followPathOrientationEntryBlendDuration,
                followPathOrientationSmoothing =
                    _mainController.followPathOrientationSmoothing
            },
            aim = CaptureAimConfiguration(),
            pcVr = pcVr,
            capture = _captureTool.CaptureConfiguration(),
            immersive = _immersiveController.CaptureConfiguration(),
            rendering = CapturePointCloudRenderingConfiguration(),
            subtitles = CaptureSubtitleConfiguration(),
            game = new GameSettings
            {
                language = _mainController.language,
                globalSpeedMultiplier = _mainController.globalSpeedMultiplier,
                followPath = !_mainController.freeMotion,
                resetPoint = _mainController.resetPoint,
                infinitePlaying = _mainController.infinitePlaying,
                hideExitPortalsInInfinitePlaying = _mainController.hideExitPortalsInInfinitePlaying,
                playInterviewIntrosInInfinitePlaying = _mainController.playInterviewIntrosInInfinitePlaying,
                demoMode = _mainController.demoMode,
                demoModeTimeoutSeconds = _mainController.demoModeTimeoutSeconds
            }
        };
    }

    private void ApplyConfiguration(UnifiedSettings configuration, bool requestAutosave)
    {
        if (configuration.version < 15 || configuration.capture == null)
        {
            configuration.capture = _captureTool.CaptureConfiguration();
        }

        if (configuration.version < 11 || configuration.pcVr == null)
        {
            configuration.pcVr = new PcVrSpectatorCamera.RuntimeConfiguration();
        }

        if (configuration.version < 13)
        {
            configuration.globalMode = configuration.pcVr.enabled
                ? GlobalMode.PcVr
                : GlobalMode.Immersive;
            if (configuration.rendering != null)
            {
                configuration.pcVr.pointRenderingMode = configuration.rendering.renderingMode;
                configuration.pcVr.pointSize = configuration.rendering.pointSize;
                configuration.pcVr.pointAlpha = configuration.rendering.alpha;
                configuration.pcVr.version = PcVrSpectatorCamera.CurrentConfigurationVersion;
            }
        }

        if (configuration.version < 14)
        {
            configuration.pcVr.oneEuroEnabled = true;
            configuration.pcVr.oneEuroPositionDeadZone = .01f;
            configuration.pcVr.oneEuroRotationDeadZone = 1f;
            configuration.pcVr.oneEuroPositionMinCutoff = .1f;
            configuration.pcVr.oneEuroPositionBeta = 4f;
            configuration.pcVr.oneEuroRotationMinCutoff = .1f;
            configuration.pcVr.oneEuroRotationBeta = 1.5f;
            configuration.pcVr.version = PcVrSpectatorCamera.CurrentConfigurationVersion;
        }

        if (configuration.version < 10)
        {
            configuration.game.resetPoint = MainController.GameResetPoint.GameMenu;
        }

        if (configuration.version < 9 || configuration.aim == null)
        {
            configuration.aim = CaptureAimConfiguration();
        }

        if (configuration.version < 8 || configuration.subtitles == null)
        {
            configuration.subtitles = new SubtitleSettings();
        }

        if (configuration.version < 6)
        {
            configuration.orb.viewResetDuration = 1f;
        }

        if (configuration.version < 5)
        {
            configuration.game.demoMode = false;
            configuration.game.demoModeTimeoutSeconds = 60f;
        }

        if (configuration.version < 4)
        {
            configuration.orb.followPathOrientationEntryBlendDuration = 3f;
            configuration.orb.followPathOrientationSmoothing = 0.35f;
        }

        if (configuration.version < 3 || configuration.rendering == null)
        {
            configuration.rendering = CapturePointCloudRenderingConfiguration();
        }

        NormalizeConfiguration(configuration);
        _applyingConfiguration = true;

        try
        {
            _globalMode = configuration.globalMode;
            _orbController.PanSensitivity = configuration.orb.panSensitivity;
            _orbController.TiltSensitivity = configuration.orb.tiltSensitivity;
            _orbController.PanSmoothing = configuration.orb.panSmoothing;
            _orbController.TiltSmoothing = configuration.orb.tiltSmoothing;
            _orbController.RequireRightMouseButton = configuration.orb.requireRightMouseButton;
            _orbController.InvertPan = configuration.orb.invertPan;
            _orbController.InvertTilt = configuration.orb.invertTilt;
            _orbController.LockPan = configuration.orb.lockPan;
            _orbController.LockTilt = configuration.orb.lockTilt;
            _orbController.ViewResetTimeout = configuration.orb.viewResetTimeout;
            _orbController.ViewResetDuration = configuration.orb.viewResetDuration;
            _mainController.followPathOrientation = configuration.orb.followPathOrientation;
            _mainController.followPathOrientationEntryBlendDuration =
                configuration.orb.followPathOrientationEntryBlendDuration;
            _mainController.followPathOrientationSmoothing =
                configuration.orb.followPathOrientationSmoothing;

            if (_gazeFollower != null)
            {
                _gazeFollower.ActiveVerticalOffset = configuration.aim.verticalOffset;
            }

            _gazeAimOverlay?.Configure(
                configuration.aim.showOverlay,
                configuration.aim.sizePixels,
                configuration.aim.thicknessPixels,
                configuration.aim.opacity,
                configuration.aim.color);

            configuration.pcVr.enabled = _globalMode == GlobalMode.PcVr;
            _pcVrSpectatorCamera.ApplyConfiguration(configuration.pcVr, false);
            _captureTool.ApplyConfiguration(configuration.capture, true);
            _immersiveController.ApplyConfiguration(configuration.immersive, false);
            _pointCloudConfiguration?.ApplyConfiguration(configuration.rendering);
            ApplyGlobalModeState();

            if (_subtitles != null)
            {
                _subtitles.ImmersiveMode = configuration.subtitles.immersiveMode;
                _subtitles.ImmersiveSurface = configuration.subtitles.surface;
                _subtitles.ImmersivePosition = configuration.subtitles.position;
                _subtitles.ImmersiveSize = configuration.subtitles.size;
            }

            _mainController.language = configuration.game.language;
            _mainController.globalSpeedMultiplier = configuration.game.globalSpeedMultiplier;
            _mainController.freeMotion = !configuration.game.followPath;
            _mainController.resetPoint = configuration.game.resetPoint;
            _mainController.infinitePlaying = configuration.game.infinitePlaying;
            _mainController.hideExitPortalsInInfinitePlaying = configuration.game.hideExitPortalsInInfinitePlaying;
            _mainController.playInterviewIntrosInInfinitePlaying = configuration.game.playInterviewIntrosInInfinitePlaying;
            _mainController.demoMode = configuration.game.demoMode;
            _mainController.demoModeTimeoutSeconds = configuration.game.demoModeTimeoutSeconds;
            _gameMenu?.SelectLanguage(_mainController.language);
        }
        finally
        {
            _applyingConfiguration = false;
        }

        RefreshAllControls();
        if (requestAutosave)
        {
            QueueAutosave();
        }
    }

    private static void NormalizeConfiguration(UnifiedSettings configuration)
    {
        if (configuration.version < 2)
        {
            configuration.game.playInterviewIntrosInInfinitePlaying = true;
        }

        configuration.version = CurrentSettingsVersion;
        if (!Enum.IsDefined(typeof(GlobalMode), configuration.globalMode))
        {
            configuration.globalMode = GlobalMode.Immersive;
        }
        configuration.pcVr.enabled = configuration.globalMode == GlobalMode.PcVr;
        configuration.orb.panSensitivity = NonNegative(configuration.orb.panSensitivity);
        configuration.orb.tiltSensitivity = NonNegative(configuration.orb.tiltSensitivity);
        configuration.orb.panSmoothing = NonNegative(configuration.orb.panSmoothing);
        configuration.orb.tiltSmoothing = NonNegative(configuration.orb.tiltSmoothing);
        configuration.orb.viewResetTimeout = NonNegative(configuration.orb.viewResetTimeout);
        configuration.orb.viewResetDuration = NonNegative(configuration.orb.viewResetDuration);
        configuration.orb.followPathOrientationEntryBlendDuration =
            NonNegative(configuration.orb.followPathOrientationEntryBlendDuration);
        configuration.orb.followPathOrientationSmoothing =
            NonNegative(configuration.orb.followPathOrientationSmoothing);
        configuration.aim.verticalOffset = Finite(configuration.aim.verticalOffset);
        configuration.aim.sizePixels = Mathf.Clamp(
            NonNegative(configuration.aim.sizePixels),
            4f,
            512f);
        configuration.aim.thicknessPixels = Mathf.Clamp(
            NonNegative(configuration.aim.thicknessPixels),
            .5f,
            configuration.aim.sizePixels * .5f);
        configuration.aim.opacity = Mathf.Clamp01(NonNegative(configuration.aim.opacity));
        configuration.aim.color = SanitizeColor(configuration.aim.color);
        PcVrSpectatorCamera.NormalizeConfiguration(configuration.pcVr);
        configuration.capture.focalDistance = NonNegative(configuration.capture.focalDistance);
        configuration.capture.focalWidth = Mathf.Max(
            .0001f,
            NonNegative(configuration.capture.focalWidth));
        configuration.capture.dotsThreshold = Mathf.Clamp01(
            NonNegative(configuration.capture.dotsThreshold));
        configuration.capture.fieldOfView = Mathf.Clamp(
            NonNegative(configuration.capture.fieldOfView),
            1f,
            179f);
        configuration.capture.screenshotName =
            string.IsNullOrWhiteSpace(configuration.capture.screenshotName)
                ? "Unnamed"
                : configuration.capture.screenshotName.Trim();
        configuration.capture.printWidthMm = Mathf.Max(
            0,
            configuration.capture.printWidthMm);
        configuration.capture.printHeightMm = Mathf.Max(
            0,
            configuration.capture.printHeightMm);
        configuration.capture.pointBudget = Mathf.Max(
            1,
            configuration.capture.pointBudget);
        configuration.rendering.pointSize = Mathf.Max(0.1f, NonNegative(configuration.rendering.pointSize));
        configuration.rendering.alpha = Mathf.Clamp01(NonNegative(configuration.rendering.alpha));
        configuration.rendering.maxViewDistance = NonNegative(configuration.rendering.maxViewDistance);
        configuration.rendering.viewDistanceMultiplier = NonNegative(configuration.rendering.viewDistanceMultiplier);
        configuration.rendering.distanceFade = Mathf.Clamp01(NonNegative(configuration.rendering.distanceFade));
        configuration.rendering.fadeIn = NonNegative(configuration.rendering.fadeIn);
        configuration.rendering.fadeOut = NonNegative(configuration.rendering.fadeOut);
        configuration.rendering.boxFeather = NonNegative(configuration.rendering.boxFeather);
        configuration.subtitles.position = new Vector2(
            Mathf.Clamp01(Finite(configuration.subtitles.position.x)),
            Mathf.Clamp01(Finite(configuration.subtitles.position.y)));
        if (!Enum.IsDefined(typeof(ImmersiveController.SurfaceId), configuration.subtitles.surface))
        {
            configuration.subtitles.surface = ImmersiveController.SurfaceId.Front;
        }

        configuration.subtitles.size = Mathf.Clamp(
            NonNegative(configuration.subtitles.size),
            .01f,
            2f);
        configuration.game.language = string.IsNullOrWhiteSpace(configuration.game.language)
            ? "en"
            : configuration.game.language.Trim();
        configuration.game.globalSpeedMultiplier = NonNegative(configuration.game.globalSpeedMultiplier);
        if (!Enum.IsDefined(typeof(MainController.GameResetPoint), configuration.game.resetPoint))
        {
            configuration.game.resetPoint = MainController.GameResetPoint.GameMenu;
        }

        configuration.game.demoModeTimeoutSeconds = Mathf.Max(
            1f,
            NonNegative(configuration.game.demoModeTimeoutSeconds));
    }

    private static float NonNegative(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
    }

    private static float Finite(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    private static Color ParseAimColor(string html, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return SanitizeColor(fallback);
        }

        html = html.Trim();
        if (!html.StartsWith("#", StringComparison.Ordinal))
        {
            html = "#" + html;
        }

        if (!ColorUtility.TryParseHtmlString(html, out var parsed))
        {
            return SanitizeColor(fallback);
        }

        parsed.a = 1f;
        return SanitizeColor(parsed);
    }

    private static Color SanitizeColor(Color value)
    {
        return new Color(
            Mathf.Clamp01(Finite(value.r)),
            Mathf.Clamp01(Finite(value.g)),
            Mathf.Clamp01(Finite(value.b)),
            1f);
    }

    public string GetConfigurationJson(bool prettyPrint = true)
    {
        return JsonUtility.ToJson(CaptureConfiguration(), prettyPrint);
    }

    public bool ApplyConfigurationJson(string json, bool requestAutosave, out string message)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            message = "The settings JSON is empty.";
            return false;
        }

        if (json.IndexOf("\"orb\"", StringComparison.OrdinalIgnoreCase) < 0
            || json.IndexOf("\"immersive\"", StringComparison.OrdinalIgnoreCase) < 0
            || json.IndexOf("\"game\"", StringComparison.OrdinalIgnoreCase) < 0)
        {
            message = "This is not a unified Katabasis settings file.";
            return false;
        }

        try
        {
            var configuration = JsonUtility.FromJson<UnifiedSettings>(json);
            if (configuration?.orb == null || configuration.immersive == null || configuration.game == null)
            {
                message = "The unified settings file is incomplete.";
                return false;
            }

            if (configuration.version > CurrentSettingsVersion)
            {
                message = $"Settings version {configuration.version} is newer than supported version {CurrentSettingsVersion}.";
                return false;
            }

            if (configuration.immersive.version > ImmersiveController.CurrentConfigurationVersion)
            {
                message = "The immersive settings use a newer unsupported version.";
                return false;
            }

            if (configuration.pcVr != null
                && configuration.pcVr.version > PcVrSpectatorCamera.CurrentConfigurationVersion)
            {
                message = "The PC-VR settings use a newer unsupported version.";
                return false;
            }

            ApplyConfiguration(configuration, requestAutosave);
            message = "All settings applied.";
            return true;
        }
        catch (Exception exception)
        {
            message = "Could not apply settings: " + exception.Message;
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
        if (!File.Exists(DefaultConfigurationPath))
        {
            message = "No unified settings file exists yet.";
            return false;
        }

        try
        {
            var loaded = ApplyConfigurationJson(File.ReadAllText(DefaultConfigurationPath), false, out message);
            if (loaded)
            {
                _autosavePending = false;
                message = "Reloaded all settings from " + DefaultConfigurationPath;
            }

            return loaded;
        }
        catch (Exception exception)
        {
            message = "Could not load settings: " + exception.Message;
            return false;
        }
    }

    public bool ExportConfiguration(string path, out string message)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            message = "Choose a settings file path.";
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
            message = "All settings saved to " + path;
            return true;
        }
        catch (Exception exception)
        {
            message = "Could not save settings: " + exception.Message;
            return false;
        }
    }

    public bool ImportConfiguration(string path, out string message)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            message = "Choose a settings file path.";
            return false;
        }

        try
        {
            path = Path.GetFullPath(path);
            if (!File.Exists(path))
            {
                message = "Settings file not found: " + path;
                return false;
            }

            if (!ApplyConfigurationJson(File.ReadAllText(path), false, out message))
            {
                return false;
            }

            if (!SaveDefaultConfiguration(out var saveMessage))
            {
                message = "Settings imported, but the default file could not be saved. " + saveMessage;
                return true;
            }

            message = "All settings imported from " + path;
            return true;
        }
        catch (Exception exception)
        {
            message = "Could not import settings: " + exception.Message;
            return false;
        }
    }

    private bool TryMigrateLegacyImmersiveConfiguration()
    {
        var legacyPath = _immersiveController.DefaultConfigurationPath;
        if (string.Equals(legacyPath, DefaultConfigurationPath, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(legacyPath))
        {
            return false;
        }

        return _immersiveController.ReloadDefaultConfiguration(out _);
    }

    private void QueueAutosave()
    {
        if (!Application.isPlaying || !autosaveRuntimeChanges)
        {
            SetStatus("Settings applied.", false);
            return;
        }

        _autosavePending = true;
        _autosaveAt = Time.unscaledTime + Mathf.Max(0f, autosaveDelay);
        SetStatus("Settings applied - autosave queued.", false);
    }

    private static string EnsureJsonExtension(string path)
    {
        return string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)
            ? path
            : path + ".json";
    }

    private void SetCategory(Category category)
    {
        _activeCategory = IsCategoryAvailable(category)
            ? category
            : _globalMode == GlobalMode.PcVr
                ? Category.PcVr
                : _globalMode == GlobalMode.Capture
                    ? Category.Capture
                    : Category.Immersive;

        foreach (var pair in _categoryContents)
        {
            pair.Value.style.display = pair.Key == _activeCategory
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        foreach (var pair in _categoryButtons)
        {
            pair.Value.EnableInClassList(
                "settings-category-button-active",
                pair.Key == _activeCategory);
        }
    }

    private bool IsCategoryAvailable(Category category)
    {
        if (_globalMode == GlobalMode.PcVr)
        {
            return category != Category.Orb
                && category != Category.Capture
                && category != Category.Immersive;
        }

        if (_globalMode == GlobalMode.Capture)
        {
            return category != Category.PcVr
                && category != Category.Immersive
                && category != Category.Subtitles;
        }

        return category != Category.PcVr && category != Category.Capture;
    }

    private void SetOpen(bool open)
    {
        _window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        _launcher.style.display = open ? DisplayStyle.None : DisplayStyle.Flex;
    }

    private void ApplyRuntimeUIVisibility()
    {
        if (_document == null)
        {
            return;
        }

        _document.enabled = true;
        var root = _document.rootVisualElement;
        if (root != null)
        {
            root.style.display = enableRuntimeUI ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void CaptureFrameFromSettings()
    {
        var configuration = CaptureToolConfigurationFromControls();
        _captureTool.ApplyConfiguration(configuration, false);
        var success = _captureTool.TryCaptureFrame(
            configuration.screenshotName,
            configuration.printWidthMm,
            configuration.printHeightMm,
            out _,
            out var message);
        if (success)
        {
            QueueAutosave();
        }

        SetStatus(message, !success);
        RefreshCaptureControls(_captureTool.CaptureConfiguration());
    }

    private void ApplyCapturePointBudget()
    {
        var configuration = CaptureToolConfigurationFromControls();
        _captureTool.ApplyConfiguration(configuration, false);
        var success = _captureTool.ApplyPointBudget(
            configuration.pointBudget,
            out var message);
        if (success)
        {
            QueueAutosave();
        }

        SetStatus(message, !success);
        RefreshCaptureControls(_captureTool.CaptureConfiguration());
    }

    private void SaveNow()
    {
        var success = SaveDefaultConfiguration(out var message);
        SetStatus(message, !success);
    }

    private void ReloadSaved()
    {
        var success = ReloadDefaultConfiguration(out var message);
        SetStatus(message, !success);
    }

    private void ExportAs()
    {
        var suggestedPath = Path.Combine(
            ConfigurationDirectory,
            "katabasis-settings-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");

        if (ImmersiveRuntimeFileDialog.IsSupported)
        {
            if (!ImmersiveRuntimeFileDialog.TrySaveJson("Export Katabasis settings", suggestedPath, out var path))
            {
                SetStatus("Export canceled.", false);
                return;
            }

            var success = ExportConfiguration(path, out var message);
            SetStatus(message, !success);
            return;
        }

        var fallbackSuccess = ExportConfiguration(suggestedPath, out var fallbackMessage);
        SetStatus(fallbackMessage, !fallbackSuccess);
    }

    private void Import()
    {
        if (!ImmersiveRuntimeFileDialog.IsSupported)
        {
            SetStatus("Use Import clipboard, or replace the saved JSON in the settings folder.", true);
            return;
        }

        if (!ImmersiveRuntimeFileDialog.TryOpenJson("Import Katabasis settings", ConfigurationDirectory, out var path))
        {
            SetStatus("Import canceled.", false);
            return;
        }

        var success = ImportConfiguration(path, out var message);
        SetStatus(message, !success);
    }

    private void CopyJson()
    {
        GUIUtility.systemCopyBuffer = GetConfigurationJson(true);
        SetStatus("Unified settings JSON copied to clipboard.", false);
    }

    private void ImportClipboard()
    {
        var success = ApplyConfigurationJson(GUIUtility.systemCopyBuffer, false, out var message);
        if (success && !SaveDefaultConfiguration(out var saveMessage))
        {
            message += " " + saveMessage;
        }

        SetStatus(message, !success);
    }

    private void OpenConfigurationFolder()
    {
        try
        {
            Directory.CreateDirectory(ConfigurationDirectory);
            Application.OpenURL(new Uri(ConfigurationDirectory).AbsoluteUri);
            SetStatus(ConfigurationDirectory, false);
        }
        catch (Exception exception)
        {
            SetStatus("Could not open folder: " + exception.Message, true);
        }
    }

    private void SetStatus(string message, bool isError)
    {
        if (_status == null)
        {
            return;
        }

        _status.text = message;
        _status.EnableInClassList("immersive-status-error", isError);
    }
}
