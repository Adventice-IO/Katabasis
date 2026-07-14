using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class SettingsMenu : MonoBehaviour
{
    public const int CurrentSettingsVersion = 5;

    private const string PanelSettingsResource = "Immersive/ImmersivePanelSettings";
    private const string StyleSheetResource = "Immersive/ImmersiveRuntimePanel";
    private const string SettingsFileName = "katabasis-settings.json";

    private enum Category
    {
        Orb,
        Immersive,
        Rendering,
        Game
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
    public sealed class UnifiedSettings
    {
        public int version = CurrentSettingsVersion;
        public OrbSettings orb = new OrbSettings();
        public ImmersiveController.RuntimeConfiguration immersive =
            new ImmersiveController.RuntimeConfiguration();
        public KatabasisMeshConfiguration.RuntimeConfiguration rendering =
            new KatabasisMeshConfiguration.RuntimeConfiguration();
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
    private UIDocument _document;
    private VisualElement _window;
    private Button _launcher;
    private Label _status;

    private readonly Dictionary<Category, Button> _categoryButtons = new Dictionary<Category, Button>();
    private readonly Dictionary<Category, VisualElement> _categoryContents = new Dictionary<Category, VisualElement>();

    private FloatField _panSensitivity;
    private FloatField _tiltSensitivity;
    private FloatField _panSmoothing;
    private FloatField _tiltSmoothing;
    private FloatField _viewResetTimeout;
    private Toggle _requireRightMouseButton;
    private Toggle _invertPan;
    private Toggle _invertTilt;
    private Toggle _lockPan;
    private Toggle _lockTilt;
    private Toggle _followPathOrientation;
    private FloatField _followPathOrientationEntryBlendDuration;
    private FloatField _followPathOrientationSmoothing;
    private Label _orbReadout;

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
    private SliderInt _resolutionDivider;
    private DropdownField _depthBuffer;
    private EnumField _textureFormat;
    private EnumField _visualMode;
    private Toggle _spout;
    private Toggle _ndi;
    private Label _textureSummary;
    private Label _spoutSupport;

    private EnumField _pointRenderingMode;
    private Slider _pointSize;
    private Slider _pointAlpha;
    private Toggle _linkMaxDistanceToCamera;
    private FloatField _pointMaxViewDistance;
    private FloatField _pointViewDistanceMultiplier;
    private Slider _pointDistanceFade;
    private FloatField _pointFadeIn;
    private FloatField _pointFadeOut;
    private FloatField _pointBoxFeather;
    private Label _pointRenderingSummary;

    private DropdownField _language;
    private FloatField _globalSpeedMultiplier;
    private Toggle _followPath;
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
        BuildGameCategory(scrollView);
        BuildPersistenceSection(scrollView);

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
        _window = null;
        _launcher = null;
        _status = null;
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
        _gameMenu = _mainController != null ? _mainController.menu : null;

        if (_gameMenu == null)
        {
            _gameMenu = FindAnyObjectByType<GameMenu>(FindObjectsInactive.Include);
        }
    }

    private void BuildHeader()
    {
        var header = new VisualElement();
        header.AddToClassList("immersive-header");

        var titleGroup = new VisualElement();
        titleGroup.AddToClassList("immersive-title-group");
        titleGroup.Add(new Label("KATABASIS SETTINGS") { name = "immersive-title" });
        titleGroup.Add(new Label("Orb, immersive output, point rendering & game configuration") { name = "immersive-subtitle" });
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
        AddCategoryButton(navigation, Category.Game, "GAME");

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
        var resetButtons = CreateButtonRow(reset);
        resetButtons.Add(CreateButton("Reset view now", () => _orbController.ResetView(), true));
        _orbReadout = new Label();
        _orbReadout.AddToClassList("immersive-texture-summary");
        reset.Add(_orbReadout);

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

        _resolutionDivider = new SliderInt("Quality divider", 1, 4) { showInputField = true };
        _resolutionDivider.AddToClassList("immersive-field");
        rendering.Add(_resolutionDivider);

        _depthBuffer = new DropdownField("Depth buffer", new List<string> { "0", "16", "24" }, 2);
        _depthBuffer.AddToClassList("immersive-field");
        rendering.Add(_depthBuffer);

        _textureFormat = new EnumField("Texture format", RenderTextureFormat.ARGB32);
        _textureFormat.AddToClassList("immersive-field");
        rendering.Add(_textureFormat);

        _visualMode = new EnumField("Preview material", ImmersiveController.VisualMode.Default);
        _visualMode.AddToClassList("immersive-field");
        rendering.Add(_visualMode);

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
        _resolutionDivider.RegisterValueChangedCallback(_ => ApplyControls());
        _depthBuffer.RegisterValueChangedCallback(_ => ApplyControls());
        RegisterLiveEnum(_textureFormat);
        RegisterLiveEnum(_visualMode);
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
        _pointViewDistanceMultiplier = CreateFloatField(distance, "View distance multiplier");
        _pointDistanceFade = new Slider("Distance fade", 0f, 1f) { showInputField = true };
        _pointDistanceFade.AddToClassList("immersive-field");
        distance.Add(_pointDistanceFade);

        var transitions = CreateSection(content, "TRANSITIONS & EDGES", "Applied to point-cloud blocks as they appear and disappear");
        var fadeRow = CreateRow(transitions);
        _pointFadeIn = CreateFloatField(fadeRow, "Fade in", true);
        _pointFadeOut = CreateFloatField(fadeRow, "Fade out", true);
        _pointBoxFeather = CreateFloatField(transitions, "Block edge feather");

        _pointRenderingSummary = new Label();
        _pointRenderingSummary.AddToClassList("immersive-texture-summary");
        transitions.Add(_pointRenderingSummary);

        RegisterLiveEnum(_pointRenderingMode);
        _pointSize.RegisterValueChangedCallback(_ => ApplyControls());
        _pointAlpha.RegisterValueChangedCallback(_ => ApplyControls());
        RegisterLiveToggle(_linkMaxDistanceToCamera);
        RegisterLiveField(_pointMaxViewDistance);
        RegisterLiveField(_pointViewDistanceMultiplier);
        _pointDistanceFade.RegisterValueChangedCallback(_ => ApplyControls());
        RegisterLiveField(_pointFadeIn);
        RegisterLiveField(_pointFadeOut);
        RegisterLiveField(_pointBoxFeather);
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
        _followPath = CreateToggle(playback, "Follow path");
        _followPath.AddToClassList("immersive-toggle-wide");
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
        RegisterLiveToggle(_followPath);
        RegisterLiveToggle(_infinitePlaying);
        RegisterLiveToggle(_hideExitPortalsInInfinitePlaying);
        RegisterLiveToggle(_playInterviewIntrosInInfinitePlaying);
        RegisterLiveToggle(_demoMode);
        RegisterLiveField(_demoModeTimeoutSeconds);
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
            _mainController.followPathOrientation = _followPathOrientation.value;
            _mainController.followPathOrientationEntryBlendDuration =
                NonNegative(_followPathOrientationEntryBlendDuration.value);
            _mainController.followPathOrientationSmoothing =
                NonNegative(_followPathOrientationSmoothing.value);

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
            immersive.resolutionDivider = _resolutionDivider.value;
            immersive.depthBufferBits = int.TryParse(_depthBuffer.value, out var depth) ? depth : 24;
            immersive.renderTextureFormat = (RenderTextureFormat)_textureFormat.value;
            immersive.visualMode = (ImmersiveController.VisualMode)_visualMode.value;
            immersive.enableSpoutSender = _spout.value;
            immersive.enableNdiSender = _ndi.value;
            _immersiveController.ApplyConfiguration(immersive, false);

            var rendering = CapturePointCloudRenderingConfiguration();
            rendering.renderingMode = (KatabasisMeshConfiguration.PointRenderingMode)_pointRenderingMode.value;
            rendering.pointSize = _pointSize.value;
            rendering.alpha = _pointAlpha.value;
            rendering.linkMaxDistanceToCamera = _linkMaxDistanceToCamera.value;
            rendering.maxViewDistance = _pointMaxViewDistance.value;
            rendering.viewDistanceMultiplier = _pointViewDistanceMultiplier.value;
            rendering.distanceFade = _pointDistanceFade.value;
            rendering.fadeIn = _pointFadeIn.value;
            rendering.fadeOut = _pointFadeOut.value;
            rendering.boxFeather = _pointBoxFeather.value;
            _pointCloudConfiguration?.ApplyConfiguration(rendering);

            _mainController.language = string.IsNullOrWhiteSpace(_language.value)
                ? "en"
                : _language.value.Trim();
            _mainController.globalSpeedMultiplier = NonNegative(_globalSpeedMultiplier.value);
            _mainController.freeMotion = !_followPath.value;
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

        RefreshImmersiveControls(_immersiveController.CaptureConfiguration());
        RefreshPointCloudRenderingControls(CapturePointCloudRenderingConfiguration());

        RefreshLanguageChoices();
        _language.SetValueWithoutNotify(_mainController.language);
        _globalSpeedMultiplier.SetValueWithoutNotify(_mainController.globalSpeedMultiplier);
        SetToggleWithoutNotify(_followPath, !_mainController.freeMotion);
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
        _pointViewDistanceMultiplier.SetValueWithoutNotify(configuration.viewDistanceMultiplier);
        _pointDistanceFade.SetValueWithoutNotify(configuration.distanceFade);
        _pointFadeIn.SetValueWithoutNotify(configuration.fadeIn);
        _pointFadeOut.SetValueWithoutNotify(configuration.fadeOut);
        _pointBoxFeather.SetValueWithoutNotify(configuration.boxFeather);

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
        _resolutionDivider.SetValueWithoutNotify(configuration.resolutionDivider);
        _depthBuffer.SetValueWithoutNotify(configuration.depthBufferBits.ToString());
        _textureFormat.SetValueWithoutNotify(configuration.renderTextureFormat);
        _visualMode.SetValueWithoutNotify(configuration.visualMode);
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
                followPathOrientation = _mainController.followPathOrientation,
                followPathOrientationEntryBlendDuration =
                    _mainController.followPathOrientationEntryBlendDuration,
                followPathOrientationSmoothing =
                    _mainController.followPathOrientationSmoothing
            },
            immersive = _immersiveController.CaptureConfiguration(),
            rendering = CapturePointCloudRenderingConfiguration(),
            game = new GameSettings
            {
                language = _mainController.language,
                globalSpeedMultiplier = _mainController.globalSpeedMultiplier,
                followPath = _followPath.value,
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
            _mainController.followPathOrientation = configuration.orb.followPathOrientation;
            _mainController.followPathOrientationEntryBlendDuration =
                configuration.orb.followPathOrientationEntryBlendDuration;
            _mainController.followPathOrientationSmoothing =
                configuration.orb.followPathOrientationSmoothing;

            _immersiveController.ApplyConfiguration(configuration.immersive, false);
            _pointCloudConfiguration?.ApplyConfiguration(configuration.rendering);

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
        configuration.orb.followPathOrientationEntryBlendDuration =
            NonNegative(configuration.orb.followPathOrientationEntryBlendDuration);
        configuration.orb.followPathOrientationSmoothing =
            NonNegative(configuration.orb.followPathOrientationSmoothing);
        configuration.rendering.pointSize = Mathf.Max(0.1f, NonNegative(configuration.rendering.pointSize));
        configuration.rendering.alpha = Mathf.Clamp01(NonNegative(configuration.rendering.alpha));
        configuration.rendering.maxViewDistance = NonNegative(configuration.rendering.maxViewDistance);
        configuration.rendering.viewDistanceMultiplier = NonNegative(configuration.rendering.viewDistanceMultiplier);
        configuration.rendering.distanceFade = Mathf.Clamp01(NonNegative(configuration.rendering.distanceFade));
        configuration.rendering.fadeIn = NonNegative(configuration.rendering.fadeIn);
        configuration.rendering.fadeOut = NonNegative(configuration.rendering.fadeOut);
        configuration.rendering.boxFeather = NonNegative(configuration.rendering.boxFeather);
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
