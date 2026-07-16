using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class SettingsMenu : MonoBehaviour
{
    public const int CurrentSettingsVersion = 9;

    private const string PanelSettingsResource = "Immersive/ImmersivePanelSettings";
    private const string StyleSheetResource = "Immersive/ImmersiveRuntimePanel";
    private const string SettingsFileName = "katabasis-settings.json";

    private enum Category
    {
        Orb,
        Immersive,
        Rendering,
        Subtitles,
        Navigation,
        Game,
        Settings
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
        public OrbSettings orb = new OrbSettings();
        public AimSettings aim = new AimSettings();
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
    private ImmersiveController _immersiveController;
    private KatabasisMeshConfiguration _pointCloudConfiguration;
    private MainController _mainController;
    private GameMenu _gameMenu;
    private Subtitles _subtitles;
    private TransformFollower _gazeFollower;
    private GazeAimOverlay _gazeAimOverlay;
    private UIDocument _document;
    private VisualElement _window;
    private Button _launcher;
    private Label _status;

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

    private FloatField _roomWidth;
    private FloatField _roomHeight;
    private FloatField _roomDepth;
    private EnumField _roomAlignment;
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
        if (_orbController == null || _immersiveController == null || _mainController == null)
        {
            Debug.LogError(
                "The unified Settings Menu requires OrbController, ImmersiveController, and MainController.",
                this);
            enabled = false;
            return;
        }

        _immersiveController.UseExternalConfigurationPersistence();

        _document = GetComponent<UIDocument>();
        if (_document == null)
        {
            _document = gameObject.AddComponent<UIDocument>();
        }

        var panelSettings = Resources.Load<PanelSettings>(PanelSettingsResource);
        if (panelSettings == null)
        {
            Debug.LogError("Settings Menu PanelSettings resource is missing.", this);
            enabled = false;
            return;
        }

        _document.enabled = true;
        _document.panelSettings = panelSettings;
        _document.sortingOrder = 1002;

        var root = _document.rootVisualElement;
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
        BuildCategoryNavigation();

        var scrollView = new ScrollView(ScrollViewMode.Vertical);
        scrollView.AddToClassList("immersive-scroll");
        _window.Add(scrollView);

        BuildOrbCategory(scrollView);
        BuildImmersiveCategory(scrollView);
        BuildRenderingCategory(scrollView);
        BuildSubtitlesCategory(scrollView);
        BuildNavigationCategory(scrollView);
        BuildGameCategory(scrollView);
        BuildSettingsCategory(scrollView);

        _status = new Label("Ready");
        _status.AddToClassList("immersive-status");
        _window.Add(_status);

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
        _subtitleSummary = null;
        _navigationSummary = null;
        _built = false;
        _refreshing = false;
        _applyingConfiguration = false;
    }

    private void ResolveControllers()
    {
        _orbController = GetComponentInParent<OrbController>();
        if (_orbController == null)
        {
            _orbController = FindAnyObjectByType<OrbController>(FindObjectsInactive.Include);
        }

        _immersiveController = FindAnyObjectByType<ImmersiveController>(FindObjectsInactive.Include);
        _pointCloudConfiguration = FindAnyObjectByType<KatabasisMeshConfiguration>(FindObjectsInactive.Include);
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
        titleGroup.Add(new Label("Orb, aiming, immersive output, rendering, subtitles, navigation & game configuration") { name = "immersive-subtitle" });
        header.Add(titleGroup);

        var close = new Button(() => SetOpen(false)) { text = "X", tooltip = "Close settings" };
        close.AddToClassList("immersive-close");
        header.Add(close);
        _window.Add(header);
    }

    private void BuildCategoryNavigation()
    {
        var navigation = new VisualElement();
        navigation.AddToClassList("settings-category-navigation");

        AddCategoryButton(navigation, Category.Orb, "ORB");
        AddCategoryButton(navigation, Category.Immersive, "IMMERSIVE");
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
        parent.Add(button);
        _categoryButtons[category] = button;
    }

    private VisualElement CreateCategoryContent(VisualElement parent, Category category)
    {
        var content = new VisualElement();
        content.AddToClassList("settings-category-content");
        parent.Add(content);
        _categoryContents[category] = content;
        return content;
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

    private void BuildImmersiveCategory(VisualElement parent)
    {
        var content = CreateCategoryContent(parent, Category.Immersive);

        var room = CreateSection(content, "ROOM", "Dimensions are in meters");
        var dimensionRow = CreateRow(room);
        _roomWidth = CreateFloatField(dimensionRow, "Width", true);
        _roomHeight = CreateFloatField(dimensionRow, "Height", true);
        _roomDepth = CreateFloatField(dimensionRow, "Depth", true);

        _roomAlignment = new EnumField("Alignment", ImmersiveController.RoomAlignmentMode.FrontWall);
        _roomAlignment.AddToClassList("immersive-field");
        room.Add(_roomAlignment);

        var cameraLabel = new Label("Camera offset from room anchor");
        cameraLabel.AddToClassList("immersive-inline-label");
        room.Add(cameraLabel);

        var cameraRow = CreateRow(room);
        _cameraX = CreateFloatField(cameraRow, "X", true);
        _cameraY = CreateFloatField(cameraRow, "Y", true);
        _cameraZ = CreateFloatField(cameraRow, "Z", true);

        var surfaces = CreateSection(content, "SURFACES", "Cameras and textures follow surface state");
        var surfaceGrid = CreateToggleGrid(surfaces);
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

        RegisterLiveField(_roomWidth);
        RegisterLiveField(_roomHeight);
        RegisterLiveField(_roomDepth);
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

        var appearance = CreateSection(content, "POINT APPEARANCE", "Size mode expands points in render-target pixels");
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

        var mode = CreateSection(content, "IMMERSIVE MODE", "Render subtitles as an overlay on exactly one immersive output");
        _subtitleImmersiveMode = CreateToggle(mode, "Subtitle immersive mode");
        _subtitleImmersiveMode.AddToClassList("immersive-toggle-wide");

        _subtitleSurface = new EnumField("Surface", ImmersiveController.SurfaceId.Front);
        _subtitleSurface.AddToClassList("immersive-field");
        mode.Add(_subtitleSurface);

        var placement = CreateSection(content, "OVERLAY", "Normalized surface coordinates: X 0-1 left to right, Y 0-1 bottom to top");
        var positionRow = CreateRow(placement);
        _subtitlePositionX = CreateFloatField(positionRow, "X", true);
        _subtitlePositionY = CreateFloatField(positionRow, "Y", true);
        _subtitleSize = CreateFloatField(placement, "Width (fraction of surface)");

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

        var demo = CreateSection(content, "DEMO MODE", "Continue automatically when nobody is using or viewing the experience");
        _demoMode = CreateToggle(demo, "Enable demo mode");
        _demoMode.AddToClassList("immersive-toggle-wide");
        _demoModeTimeoutSeconds = CreateFloatField(demo, "Inactivity timeout (seconds)");

        var demoHint = new Label("The timer runs only inside a salle while no interview is playing. Viewer input or movement restarts it.");
        demoHint.AddToClassList("immersive-hint");
        demo.Add(demoHint);

        _language.RegisterValueChangedCallback(_ => ApplyControls());
        RegisterLiveField(_globalSpeedMultiplier);
        RegisterLiveToggle(_infinitePlaying);
        RegisterLiveToggle(_hideExitPortalsInInfinitePlaying);
        RegisterLiveToggle(_playInterviewIntrosInInfinitePlaying);
        RegisterLiveToggle(_demoMode);
        RegisterLiveField(_demoModeTimeoutSeconds);
    }

    private void BuildNavigationCategory(VisualElement parent)
    {
        var content = CreateCategoryContent(parent, Category.Navigation);
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

            var immersive = _immersiveController.CaptureConfiguration();
            immersive.roomWidth = _roomWidth.value;
            immersive.roomHeight = _roomHeight.value;
            immersive.roomDepth = _roomDepth.value;
            immersive.roomAlignment = (ImmersiveController.RoomAlignmentMode)_roomAlignment.value;
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

        RefreshImmersiveControls(_immersiveController.CaptureConfiguration());
        RefreshPointCloudRenderingControls(CapturePointCloudRenderingConfiguration());
        RefreshSubtitleControls(CaptureSubtitleConfiguration());

        RefreshLanguageChoices();
        _language.SetValueWithoutNotify(_mainController.language);
        _globalSpeedMultiplier.SetValueWithoutNotify(_mainController.globalSpeedMultiplier);
        SetToggleWithoutNotify(_infinitePlaying, _mainController.infinitePlaying);
        SetToggleWithoutNotify(_hideExitPortalsInInfinitePlaying, _mainController.hideExitPortalsInInfinitePlaying);
        SetToggleWithoutNotify(_playInterviewIntrosInInfinitePlaying, _mainController.playInterviewIntrosInInfinitePlaying);
        SetToggleWithoutNotify(_demoMode, _mainController.demoMode);
        _demoModeTimeoutSeconds.SetValueWithoutNotify(_mainController.demoModeTimeoutSeconds);
        _demoModeTimeoutSeconds.SetEnabled(_mainController.demoMode);

        _refreshing = false;
        RefreshRuntimeStatus();
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
                ? _gazeFollower.ActiveVerticalOffset
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
        _subtitleSurface.SetEnabled(controlsEnabled && configuration.immersiveMode);
        _subtitlePositionX.SetEnabled(controlsEnabled && configuration.immersiveMode);
        _subtitlePositionY.SetEnabled(controlsEnabled && configuration.immersiveMode);
        _subtitleSize.SetEnabled(controlsEnabled && configuration.immersiveMode);

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

        _roomWidth.SetValueWithoutNotify(configuration.roomWidth);
        _roomHeight.SetValueWithoutNotify(configuration.roomHeight);
        _roomDepth.SetValueWithoutNotify(configuration.roomDepth);
        _roomAlignment.SetValueWithoutNotify(configuration.roomAlignment);
        _cameraX.SetValueWithoutNotify(configuration.cameraOffsetFromAnchor.x);
        _cameraY.SetValueWithoutNotify(configuration.cameraOffsetFromAnchor.y);
        _cameraZ.SetValueWithoutNotify(configuration.cameraOffsetFromAnchor.z);
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
        RefreshRuntimeStatus();
    }

    private void RefreshRuntimeStatus()
    {
        if (!_built)
        {
            return;
        }

        _orbReadout.text = $"Pan {_orbController.Pan:F1} deg  |  Tilt {_orbController.Tilt:F1} deg";
        _textureSummary.text = _immersiveController.GetRenderTextureSummary();
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
            var surfaceAvailable = !_subtitles.ImmersiveMode
                || _immersiveController.TryGetSurfaceCamera(_subtitles.ImmersiveSurface, out _);
            _subtitleSummary.EnableInClassList("immersive-warning", !surfaceAvailable);
            _subtitleSummary.text = !_subtitles.ImmersiveMode
                ? "Immersive overlay disabled; standard camera placement is active."
                : surfaceAvailable
                    ? $"Fixed 2D overlay on {_subtitles.ImmersiveSurface} (included in Spout/NDI)."
                    : $"{_subtitles.ImmersiveSurface} is disabled, so no subtitle overlay can be rendered.";
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
            : $"Hardware points | 1 render pixel | {rendering.alpha:F2} alpha";
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
        return new UnifiedSettings
        {
            version = CurrentSettingsVersion,
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
            immersive = _immersiveController.CaptureConfiguration(),
            rendering = CapturePointCloudRenderingConfiguration(),
            subtitles = CaptureSubtitleConfiguration(),
            game = new GameSettings
            {
                language = _mainController.language,
                globalSpeedMultiplier = _mainController.globalSpeedMultiplier,
                followPath = !_mainController.freeMotion,
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

            _immersiveController.ApplyConfiguration(configuration.immersive, false);
            _pointCloudConfiguration?.ApplyConfiguration(configuration.rendering);

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
        foreach (var pair in _categoryContents)
        {
            pair.Value.style.display = pair.Key == category ? DisplayStyle.Flex : DisplayStyle.None;
        }

        foreach (var pair in _categoryButtons)
        {
            pair.Value.EnableInClassList("settings-category-button-active", pair.Key == category);
        }
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
