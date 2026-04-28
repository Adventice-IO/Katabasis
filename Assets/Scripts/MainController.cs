using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using TMPro;
using Unity.XR.CompositionLayers.UIInteraction;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.Rendering;



#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways] // This makes the script run even when NOT in Play mode
public class MainController : MonoBehaviour
{
    [Header("Setup")]
    public Salle initialSalle;


    [Header("Audio Settings")]
    public AudioStateRefSO noAudioSO;
    public bool debugAudioStates = false;
    public AudioStateRefSO menuRefSO;
    public AudioStateRefSO introRefSO;
    public AudioStateRefSO playingSO;
    public AudioStateRefSO outroRefSO;
    public AudioStateRefSO endRefSO;

    public enum GameState
    {
        Menu,
        Intro,
        Playing,
        Outro,
        End
    }
    [Header("State")]
    public GameState gameState = GameState.Menu;
    GameState lastGameState;
    float timeAtStateChange;
    public string language = "en";
    public Salle salle;
    public Tunnel tunnel;

    [Header("Controls")]
    public bool animateRotation = false;

    [Range(0f, 1f)]
    public float trackPosition; // 0.0 = Start, 1.0 = End

    [Header("Camera")]
    public float defaultCamMaxDistance = 50f;

    [Header("Physics Settings")]
    public float baseSpeed = 4f; // km/h, for reference
    public float maxSpeed = 50f; // km/h, for editing
    public float maxAcceleration = 5f; // km/h/s
    public float playFullSpeedTime = 2f; // seconds after which we ignore acceleration and just set the speed to the target speed
    float timeAtPlay;
    Vector3 posAtPlay;
    public float globalSpeedMultiplier = 1f;
    public float smoothGoToPath = 0.5f; // time in seconds to smooth the transition when going to a new path

    public AnimationCurve speedCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    AnimationCurve smoothGotoCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Read Only")]
    [SerializeField] private float currentSpeed = 0f;
    [SerializeField] private float targetSpeed = 0f;
    private AudioStateRefSO _lastTunnelAudioSO;
    [SerializeField] public bool isRunning = false;
    [SerializeField] private bool isReversed = false;
    private SplineContainer splineContainer;
    private float pathLength;

#if UNITY_EDITOR
    private double lastEditorTime;
#endif


    [Header("Interaction")]
    public bool editMode = true;
    bool _lastEditMode = true;

    public float editMaxSpeed = 5f;
    float editSmoothSpeed = 0f;

    public bool freeMotion;
    public bool followPathOrientation;

    public ContinuousMoveProvider moveProvider;
    [SerializeField] private InputActionProperty verticalMoveAction;
    [SerializeField] private InputActionProperty joystickAction;
    [SerializeField] private InputActionProperty toggleFreeMoveAction;
    [SerializeField] private InputActionProperty spawnAction;
    [SerializeField] private InputActionProperty cancelAction;
    [SerializeField] private InputActionProperty snapGroundAction;
    [SerializeField] private InputActionProperty spawnCheckPointAction;
    [SerializeField] private InputActionReference nativeTeleportAction;
    [SerializeField] private InputActionProperty prevNextSpeedTopAction;

    bool spawningMode;
    public bool removedInSpawnMode;
    public bool removedCheckpoint;


    bool verticalMove;

    float timeAtSpawnMode;

    float timeAtToggleFreeMotion;
    bool freeMotionSwitched;

    [Header("Links")]
    public GameMenu menu;
    public GameIntro intro;
    public GameOutro outro;
    public GameEnd end;
    public GameObject lockInfoPlane;
    public GameObject speedInfo;
    public GameObject teleportationLoco;
    public GameObject turnLoco;

    [Header("Point Cloud")]
    [Range(0.01f, 1f)]
    public float viewDistanceAnimSpeed = 0.2f;
    public float viewDistanceHideSpeed = 10f;
    [Range(0f, 1f)]
    public float pointCloudViewDistanceMultiplier = 1.0f;
    public bool enablePointCloudDiagnostics = true;
    public float pointCloudDiagnosticsLogIntervalSeconds = 10f;

    [Header("Run Start Diagnostics")]
    public bool debugRunStartCamera = false;
    public float runStartDiagnosticsDurationSeconds = 0.6f;
    public float runStartDiagnosticsLogIntervalSeconds = 0.05f;
    public float runStartDiagnosticsJumpDistance = 0.2f;
    public float runStartDiagnosticsJumpAngle = 12f;

    [Header("Tunnel Entry Diagnostics")]
    public bool logTunnelEntrySync = true;
    public float tunnelEntrySyncWarningDistance = 0.02f;
    public bool snapRigToTunnelEntryOnPlay = false;

    [Header("Rig Motion Diagnostics")]
    public bool logRigMotionAttribution = true;
    public float rigMotionAttributionDistanceThreshold = 0.05f;
    public float rigMotionAttributionAngleThreshold = 2f;
    public float rigMotionAttributionCooldownSeconds = 0.25f;

    float timeAtArrived; //in a salle or tunnel
    public float timeSinceArrived
    {
        get
        {
            return Time.time - timeAtArrived;
        }
    }

    public Tunnel comingFromTunnel { get; private set; }

    //Game memory
    List<Salle> visitedSalles = new List<Salle>();



    Tunnel[] allTunnels;

    GameObject sallesGO;
    GameObject tunnelsGO;
    bool wasUserPresent;
    float nextPointCloudDiagnosticsLogTime;
    float lastLoggedVisionZoneDistance = -1f;
    float runStartDiagnosticsUntilTime = -1f;
    float nextRunStartDiagnosticsLogTime = -1f;
    Vector3 lastRunStartCameraPosition;
    Quaternion lastRunStartCameraRotation;
    bool hasLastRunStartCameraPose;
    Vector3 lastObservedRigPosition;
    Quaternion lastObservedRigRotation;
    bool hasLastObservedRigPose;
    string pendingRigMotionReason;
    float nextRigMotionAttributionLogTime;

    private void Start()
    {
        int q = QualitySettings.GetQualityLevel();
        string qName = (q >= 0 && q < QualitySettings.names.Length) ? QualitySettings.names[q] : "unknown";
        string rp = GraphicsSettings.currentRenderPipeline != null
            ? GraphicsSettings.currentRenderPipeline.name
            : "Built-in";

        Debug.Log(
            $"DebugBuild={Debug.isDebugBuild} | " +
            $"Quality={q}:{qName} | " +
            $"API={SystemInfo.graphicsDeviceType} | " +
            $"RP={rp}"
        );

        sallesGO = GameObject.Find("Salles");
        tunnelsGO = GameObject.Find("Tunnels");

        wasUserPresent = IsUserPresent();
        lastObservedRigPosition = transform.position;
        lastObservedRigRotation = transform.rotation;
        hasLastObservedRigPose = true;

        gameStateUpdate();
    }

#if UNITY_EDITOR
    private void OnEnable()
    {

        if (!Application.isPlaying)
        {
            lastEditorTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= EditorTick;
            EditorApplication.update += EditorTick;
        }

        if (Application.isPlaying)
        {
            if (joystickAction.action != null) joystickAction.action.Enable();
            if (toggleFreeMoveAction.action != null)
            {
                toggleFreeMoveAction.action.Enable();
                toggleFreeMoveAction.action.performed += ctx =>
                {
                    freeMotionSwitched = false;
                    timeAtToggleFreeMotion = Time.time;
                };

                toggleFreeMoveAction.action.canceled += ctx =>
                {
                    if (!freeMotionSwitched)
                    {
                        if (isRunning) Pause();
                        else Play();
                    }
                };
            }

            if (spawnAction.action != null)
            {
                spawnAction.action.Enable();

            }

            if (cancelAction.action != null)
            {
                cancelAction.action.Enable();
                cancelAction.action.performed += ctx =>
                {
                    RuntimeUndoManager.instance.Undo();
                };
            }

            if (verticalMoveAction.action != null)
            {
                verticalMoveAction.action.Enable();
                verticalMoveAction.action.performed += ctx =>
                {
                    Debug.Log("Vertical move started");
                    verticalMove = true;

                };
                verticalMoveAction.action.canceled += ctx =>
                {
                    verticalMove = false;
                };
            }

            if (snapGroundAction.action != null)
            {
                snapGroundAction.action.Enable();
                snapGroundAction.action.performed += ctx =>
                {
                    if (freeMotion)
                    {
                        Vector3 groundPos = GroundFinder.getGroundForPosition(transform.position, .2f, 1.0f, 6);
                        SetRigPosition(groundPos, "SnapGroundAction");
                    }
                };
            }

            if (spawnCheckPointAction.action != null)
            {
                spawnCheckPointAction.action.Enable();
                spawnCheckPointAction.action.canceled += ctx =>
                {
                    if (!removedCheckpoint)
                    {
                        Tunnel tTunnel = tunnel;
                        Debug.Log("Adding speed checkpoint at current position");
                        if (tTunnel == null)
                        {
                            tTunnel = getClosestTunnel();
                        }
                        if (tTunnel != null)
                        {
                            float pos = tTunnel.getClosestTrackPosition(transform.position);
                            RuntimeUndoManager.addCheckpoint(tTunnel, pos);
                        }
                    }
                    removedCheckpoint = false;

                };
            }

            if (prevNextSpeedTopAction.action != null)
            {
                prevNextSpeedTopAction.action.Enable();
                prevNextSpeedTopAction.action.performed += ctx =>
                {
                    if (!freeMotion && isInATunnel())
                    {
                        Debug.Log("Teleporting to nearest speed checkpoint");
                        Cardinal cardinal = CardinalUtility.GetNearestCardinal(prevNextSpeedTopAction.action.ReadValue<Vector2>());

                        switch (cardinal)
                        {
                            case Cardinal.North:
                                {
                                    Debug.Log("Teleporting forward to next speed checkpoint");
                                    float nextSpeedPos = tunnel.getNextSpeedCheckpointPosition(trackPosition + .01f, isReversed);
                                    if (nextSpeedPos >= 0)
                                    {
                                        setPosition(nextSpeedPos);
                                    }
                                }
                                break;
                            case Cardinal.South:
                                {
                                    Debug.Log("Teleporting backward to previous speed checkpoint");
                                    float prevSpeedPos = tunnel.getPreviousSpeedCheckpointPosition(trackPosition - .01f, isReversed);
                                    if (prevSpeedPos >= 0)
                                    {
                                        setPosition(prevSpeedPos);
                                    }
                                }
                                break;
                        }
                    }
                };
            }
        }
    }

    private void OnDisable()
    {
        EditorApplication.update -= EditorTick;
        if (Application.isPlaying)
        {
            if (joystickAction.action != null) joystickAction.action.Disable();
            if (toggleFreeMoveAction.action != null) toggleFreeMoveAction.action.Disable();
            if (spawnAction.action != null) spawnAction.action.Disable();
            if (cancelAction.action != null) cancelAction.action.Disable();
            if (verticalMoveAction.action != null) verticalMoveAction.action.Disable();
            if (snapGroundAction.action != null) snapGroundAction.action.Disable();
            if (spawnCheckPointAction.action != null) spawnCheckPointAction.action.Disable();
        }


    }

    private void EditorTick()
    {
        if (this == null)
        {
            EditorApplication.update -= EditorTick;
            return;
        }

        if (Application.isPlaying)
            return;

        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - lastEditorTime);
        lastEditorTime = now;

        // Run simulation even if Scene view isn't repainting
        Tick(Mathf.Clamp(dt, 0f, 0.05f));

        // Force scene repaint so movement is visible
        SceneView.RepaintAll();
    }
#endif

    private void FixedUpdate()
    {
        if (!Application.isPlaying)
            return;

        Tick(Time.deltaTime);
        TrackRigMotion("FixedUpdate");
    }

    private void Update()
    {
       // Debug.Log("MainConroller position : " + transform.position+", camera position :" + (Camera.main != null ? Camera.main.transform.position.ToString() : "null"));
        if (allTunnels == null)
        {
            allTunnels = FindObjectsByType<Tunnel>(FindObjectsSortMode.None);
        }

        if (Camera.main != null)
        {
            float targetFarClip = getAverageVisionZoneMaxDistance();
            Camera.main.farClipPlane = targetFarClip;

            if (enablePointCloudDiagnostics && Application.isPlaying)
            {
                bool shouldLogByInterval = Time.unscaledTime >= nextPointCloudDiagnosticsLogTime;
                bool shouldLogByDistanceChange = lastLoggedVisionZoneDistance < 0f || Mathf.Abs(lastLoggedVisionZoneDistance - targetFarClip) >= 5f;
                if (shouldLogByInterval || shouldLogByDistanceChange)
                {
                    nextPointCloudDiagnosticsLogTime = Time.unscaledTime + Mathf.Max(1f, pointCloudDiagnosticsLogIntervalSeconds);
                    lastLoggedVisionZoneDistance = targetFarClip;
                    Debug.Log(BuildPointCloudDiagnostics(targetFarClip));
                }
            }
        }

#if UNITY_EDITOR
        if (lockInfoPlane != null)
        {
            lockInfoPlane.SetActive(!freeMotion && editMode);
        }

        if (speedInfo != null)
        {
            speedInfo.SetActive(isInATunnel() && !freeMotion && editMode);
            if (isInATunnel() && !freeMotion && editMode)
            {
                TextMeshPro textMesh = speedInfo.GetComponent<TextMeshPro>();
                if (textMesh != null)
                {
                    textMesh.text = Mathf.RoundToInt(currentSpeed) + " km/h - Target : " + Mathf.RoundToInt(targetSpeed) + " km/h";
                }
            }
        }
#endif


        if (Application.isPlaying)
        {
            HandleHeadsetPresence();
            UpdateRunStartDiagnostics();

            if (gameState != lastGameState)
            {
                gameStateUpdate();
                lastGameState = gameState;
            }

            if (gameState == GameState.Playing)
            {
                float viewOffset = Time.deltaTime * viewDistanceAnimSpeed;
                pointCloudViewDistanceMultiplier = Mathf.Clamp01(pointCloudViewDistanceMultiplier + viewOffset);
            }

            if (editMode != _lastEditMode)
            {
                _lastEditMode = editMode;
                Tunnel[] tunnels = FindObjectsByType<Tunnel>(FindObjectsSortMode.None);
                foreach (var tunnel in tunnels)
                {
                    tunnel.UpdateLineRenderer();
                    tunnel.updateHandles();
                    tunnel.updateSpeedCheckpoints();
                }

                KataTransformer[] kataTransformers = FindObjectsByType<KataTransformer>(FindObjectsSortMode.None);
                foreach (var kt in kataTransformers)
                {
                    if (kt != null) kt.updateActive();
                }


            }


            if (spawnAction.action != null)
            {
                bool pressed = spawnAction.action.IsPressed();
                if (pressed != spawningMode)
                {
                    spawningMode = pressed;
                    if (spawningMode) timeAtSpawnMode = (float)Time.time;
                    else
                    {
                        if (!removedInSpawnMode)
                        {
                            Debug.Log("Adding knot at end of spawn mode");
                            float duration = (float)(Time.time - timeAtSpawnMode);
                            if (duration < .3f)
                            {
                                Tunnel tTunnel = tunnel;
                                if (tTunnel == null)
                                {
                                    tTunnel = getClosestTunnel();
                                }
                                if (tTunnel != null) tTunnel.AddKnotAtPosition(GroundFinder.getGroundForPosition(transform.position, .2f, 1.0f, 6));
                            }
                            timeAtSpawnMode = 0f;
                        }
                        removedInSpawnMode = false;
                    }
                }

                bool freeMotionPressed = toggleFreeMoveAction.action.IsPressed();
                if (freeMotionPressed && !freeMotionSwitched)
                {
                    if (Time.time - timeAtToggleFreeMotion > 0.6f)
                    {
                        freeMotion = !freeMotion;
                        freeMotionSwitched = true;
                    }
                }
            }

            if (nativeTeleportAction.action != null)
            {
                if (nativeTeleportAction.action.enabled != freeMotion)
                {
                    if (freeMotion) nativeTeleportAction.action.Enable();
                    else nativeTeleportAction.action.Disable();
                }
            }
            teleportationLoco.SetActive(freeMotion);
            turnLoco.SetActive(freeMotion);
        }

        TrackRigMotion("Update");
    }

    void MarkRigMotionExpected(string reason)
    {
        pendingRigMotionReason = reason;
    }

    void SetRigPosition(Vector3 position, string reason)
    {
        MarkRigMotionExpected(reason);
        transform.position = position;
    }

    void MoveRig(Vector3 delta, string reason)
    {
        MarkRigMotionExpected(reason);
        transform.position += delta;
    }

    void SetRigRotation(Quaternion rotation, string reason)
    {
        MarkRigMotionExpected(reason);
        transform.rotation = rotation;
    }

    void LookRigAt(Vector3 worldPosition, Vector3 worldUp, string reason)
    {
        MarkRigMotionExpected(reason);
        transform.LookAt(worldPosition, worldUp);
    }

    void RotateRigAround(Vector3 point, Vector3 axis, float angle, string reason)
    {
        MarkRigMotionExpected(reason);
        transform.RotateAround(point, axis, angle);
    }

    void TrackRigMotion(string phase)
    {
        if (!Application.isPlaying)
        {
            return;
        }

        Vector3 rigPosition = transform.position;
        Quaternion rigRotation = transform.rotation;
        if (!hasLastObservedRigPose)
        {
            lastObservedRigPosition = rigPosition;
            lastObservedRigRotation = rigRotation;
            hasLastObservedRigPose = true;
            pendingRigMotionReason = null;
            return;
        }

        float positionDelta = Vector3.Distance(lastObservedRigPosition, rigPosition);
        float rotationDelta = Quaternion.Angle(lastObservedRigRotation, rigRotation);
        bool rigMoved = positionDelta >= rigMotionAttributionDistanceThreshold || rotationDelta >= rigMotionAttributionAngleThreshold;
        if (!rigMoved)
        {
            pendingRigMotionReason = null;
            return;
        }

        if (logRigMotionAttribution && Time.unscaledTime >= nextRigMotionAttributionLogTime)
        {
            Camera mainCam = Camera.main;
            Vector3 cameraLocalPosition = mainCam != null ? transform.InverseTransformPoint(mainCam.transform.position) : Vector3.zero;
            string motionSource = string.IsNullOrEmpty(pendingRigMotionReason) ? "UNTRACKED" : pendingRigMotionReason;
            string tunnelName = tunnel != null ? tunnel.name : "null";
            string salleName = salle != null ? salle.name : "null";
            bool teleportEnabled = nativeTeleportAction != null && nativeTeleportAction.action != null && nativeTeleportAction.action.enabled;
            bool moveProviderEnabled = moveProvider != null && moveProvider.enabled;
            string message =
                $"[RigMotion] source='{motionSource}' phase='{phase}' posDelta={positionDelta:F4} rotDelta={rotationDelta:F2} rigPos={rigPosition:F3} prevRigPos={lastObservedRigPosition:F3} camLocalPos={cameraLocalPosition:F3} freeMotion={freeMotion} moveProviderEnabled={moveProviderEnabled} teleportEnabled={teleportEnabled} verticalMove={verticalMove} isRunning={isRunning} salle='{salleName}' tunnel='{tunnelName}'";

            if (string.IsNullOrEmpty(pendingRigMotionReason))
            {
                Debug.LogWarning(message, this);
            }
            else
            {
                Debug.Log(message, this);
            }

            nextRigMotionAttributionLogTime = Time.unscaledTime + Mathf.Max(0.05f, rigMotionAttributionCooldownSeconds);
        }

        lastObservedRigPosition = rigPosition;
        lastObservedRigRotation = rigRotation;
        pendingRigMotionReason = null;
    }

    void HandleHeadsetPresence()
    {
        bool isUserPresent = IsUserPresent();
        if (!wasUserPresent && isUserPresent)
        {
            ResetViewForward();
        }

        wasUserPresent = isUserPresent;
    }

    bool IsUserPresent()
    {
        UnityEngine.XR.InputDevice headDevice = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
        if (!headDevice.isValid)
        {
            return true;
        }

        if (headDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.userPresence, out bool userPresent))
        {
            return userPresent;
        }

        return true;
    }

    public void BeginRunStartCameraDiagnostics(string reason)
    {
        if (!Application.isPlaying || !debugRunStartCamera)
        {
            return;
        }

        runStartDiagnosticsUntilTime = Time.unscaledTime + Mathf.Max(0.05f, runStartDiagnosticsDurationSeconds);
        nextRunStartDiagnosticsLogTime = Time.unscaledTime;
        hasLastRunStartCameraPose = false;
        LogRunStartCameraState(reason);
    }

    void UpdateRunStartDiagnostics()
    {
        if (!debugRunStartCamera || !Application.isPlaying)
        {
            return;
        }

        if (runStartDiagnosticsUntilTime < 0f)
        {
            return;
        }

        if (Time.unscaledTime > runStartDiagnosticsUntilTime)
        {
            LogRunStartCameraState("run-start window end");
            runStartDiagnosticsUntilTime = -1f;
            nextRunStartDiagnosticsLogTime = -1f;
            hasLastRunStartCameraPose = false;
            return;
        }

        if (Time.unscaledTime < nextRunStartDiagnosticsLogTime)
        {
            return;
        }

        LogRunStartCameraState("run-start tick");
        nextRunStartDiagnosticsLogTime = Time.unscaledTime + Mathf.Max(0.01f, runStartDiagnosticsLogIntervalSeconds);
    }

    void LogRunStartCameraState(string reason)
    {
        Camera mainCam = Camera.main;
        string cameraName = mainCam != null ? mainCam.name : "null";
        Vector3 rigPosition = transform.position;
        Vector3 rigEuler = transform.rotation.eulerAngles;
        Vector3 cameraPosition = mainCam != null ? mainCam.transform.position : Vector3.zero;
        Vector3 cameraEuler = mainCam != null ? mainCam.transform.rotation.eulerAngles : Vector3.zero;
        Vector3 cameraLocalPosition = mainCam != null ? transform.InverseTransformPoint(mainCam.transform.position) : Vector3.zero;
        Vector3 cameraLocalEuler = mainCam != null ? (Quaternion.Inverse(transform.rotation) * mainCam.transform.rotation).eulerAngles : Vector3.zero;
        float cameraDeltaDistance = 0f;
        float cameraDeltaAngle = 0f;

        if (mainCam != null && hasLastRunStartCameraPose)
        {
            cameraDeltaDistance = Vector3.Distance(lastRunStartCameraPosition, mainCam.transform.position);
            cameraDeltaAngle = Quaternion.Angle(lastRunStartCameraRotation, mainCam.transform.rotation);
        }

        string tunnelName = tunnel != null ? tunnel.name : "null";
        string salleName = salle != null ? salle.name : "null";
        string jumpMarker = string.Empty;
        if (mainCam != null && hasLastRunStartCameraPose &&
            (cameraDeltaDistance >= runStartDiagnosticsJumpDistance || cameraDeltaAngle >= runStartDiagnosticsJumpAngle))
        {
            jumpMarker = " JUMP";
        }

        Debug.Log(
            $"[RunStartCamera]{jumpMarker} reason='{reason}' freeMotion={freeMotion} isRunning={isRunning} reversed={isReversed} track={trackPosition:F3} tunnel='{tunnelName}' salle='{salleName}' rigPos={rigPosition:F3} rigRot={rigEuler:F1} cam='{cameraName}' camPos={cameraPosition:F3} camRot={cameraEuler:F1} camLocalPos={cameraLocalPosition:F3} camLocalRot={cameraLocalEuler:F1} camDeltaDist={cameraDeltaDistance:F3} camDeltaAngle={cameraDeltaAngle:F1}",
            this
        );

        if (mainCam != null)
        {
            lastRunStartCameraPosition = mainCam.transform.position;
            lastRunStartCameraRotation = mainCam.transform.rotation;
            hasLastRunStartCameraPose = true;
        }
    }

    void ResetViewForward()
    {
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            return;
        }

        Vector3 cameraForward = mainCam.transform.forward;
        cameraForward.y = 0f;

        if (cameraForward.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float yawToWorldForward = Vector3.SignedAngle(cameraForward.normalized, Vector3.forward, Vector3.up);
        RotateRigAround(transform.position, Vector3.up, yawToWorldForward, "ResetViewForward");
    }

    void LogTunnelEntrySync(string reason)
    {
        if (!Application.isPlaying || !logTunnelEntrySync || tunnel == null)
        {
            return;
        }

        Salle sourceSalle = isReversed ? tunnel.salleArrivee : tunnel.salleDepart;
        if (sourceSalle == null || sourceSalle.origin == null)
        {
            return;
        }

        float trackStartPosition = GetActualTrackPosition(0f);
        Vector3 roomOriginPosition = sourceSalle.origin.position;
        Vector3 rigPosition = transform.position;
        Vector3 tunnelStartPosition = tunnel.getPositionOnTrack(trackStartPosition);
        float roomOriginToTrackStartDistance = Vector3.Distance(roomOriginPosition, tunnelStartPosition);
        float rigToRoomOriginDistance = Vector3.Distance(rigPosition, roomOriginPosition);
        float rigToTrackStartDistance = Vector3.Distance(rigPosition, tunnelStartPosition);
        bool hasMismatch = roomOriginToTrackStartDistance > tunnelEntrySyncWarningDistance || rigToTrackStartDistance > tunnelEntrySyncWarningDistance;
        string direction = isReversed ? "reverse" : "forward";
        string message =
            $"[TunnelEntrySync] {(hasMismatch ? "MISMATCH" : "OK")} reason='{reason}' tunnel='{tunnel.name}' direction={direction} salle='{sourceSalle.name}' rigPos={rigPosition:F3} roomOrigin={roomOriginPosition:F3} trackStart={tunnelStartPosition:F3} roomToTrack={roomOriginToTrackStartDistance:F4} rigToRoom={rigToRoomOriginDistance:F4} rigToTrack={rigToTrackStartDistance:F4}";

        if (hasMismatch)
        {
            Debug.LogWarning(message, this);
        }
        else
        {
            Debug.Log(message, this);
        }
    }

    void PrepareTunnelEntryForPlay()
    {
        if (!Application.isPlaying || tunnel == null || trackPosition > 0.01f)
        {
            return;
        }

        LogTunnelEntrySync("Play start before sync");

        bool tunnelWasAdjusted = tunnel.SyncEndpointsToSalleOrigins();
        float trackStartPosition = GetActualTrackPosition(0f);
        Vector3 tunnelEntryPosition = tunnel.getPositionOnTrack(trackStartPosition);
        float rigToTrackDistance = Vector3.Distance(transform.position, tunnelEntryPosition);

        if (tunnelWasAdjusted)
        {
            Debug.LogWarning(
                $"[TunnelEntrySync] Adjusted tunnel endpoint before play tunnel='{tunnel.name}' entryPos={tunnelEntryPosition:F3}",
                this
            );
        }

        if (rigToTrackDistance > 0.001f)
        {
            if (snapRigToTunnelEntryOnPlay || rigToTrackDistance > tunnelEntrySyncWarningDistance)
            {
                Debug.LogWarning(
                    $"[TunnelEntrySync] Aligning rig to tunnel entry before play tunnel='{tunnel.name}' rigPos={transform.position:F3} entryPos={tunnelEntryPosition:F3} distance={rigToTrackDistance:F4}",
                    this
                );
            }

            SetRigPosition(tunnelEntryPosition, "PrepareTunnelEntryForPlay align");
        }

        LogTunnelEntrySync("Play start after sync");
    }

    private void Tick(float deltaTime)
    {
        moveProvider.enabled = freeMotion && !verticalMove;
        teleportationLoco.SetActive(freeMotion);

        if (Application.isPlaying)
        {

            Vector2 joystickInput = joystickAction.action?.ReadValue<Vector2>() ?? Vector2.zero;

            if (verticalMove)
            {
                MoveRig(Vector3.up * joystickInput.y * deltaTime, "VerticalMove");
            }
            else if (!freeMotion && isInATunnel())
            {
                if (joystickInput.y != 0f)
                {
                    Pause();
                }
                editSmoothSpeed = Mathf.MoveTowards(editSmoothSpeed, joystickInput.y * editMaxSpeed, maxAcceleration * deltaTime);
                if (editSmoothSpeed != 0f && !isRunning)
                {
                    setPosition(trackPosition + editSmoothSpeed * deltaTime / (splineContainer != null ? splineContainer.Spline.GetLength() : 1f));
                }
            }
        }

        if (tunnel == null)
            return;

        if (freeMotion) return;

        if (isInASalle())
        {
            SetRigPosition(salle.origin.position, "Tick salle origin lock");
            return;
        }

        // Cache the container if we switched paths
        var tunnelContainer = tunnel.GetComponent<SplineContainer>();
        if (splineContainer != tunnelContainer)
        {
            splineContainer = tunnelContainer;
            pathLength = splineContainer != null ? splineContainer.Spline.GetLength() : 0f;
        }


        if (isRunning)
        {
            float actualTrackPosition = GetActualTrackPosition(trackPosition);
            targetSpeed = Mathf.Min(tunnel.getDesiredSpeedAtPosition(actualTrackPosition, isReversed), maxSpeed);
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, maxAcceleration * deltaTime);

            float multipliedSpeed = currentSpeed;
            if (Time.time - timeAtPlay < playFullSpeedTime)
            {
                float speedFactor = speedCurve.Evaluate((Time.time - timeAtPlay) / playFullSpeedTime);
                multipliedSpeed *= speedFactor;
            }

            multipliedSpeed *= globalSpeedMultiplier;

            if (pathLength > 0)
            {
                //currentSpeed in km/h
                float step = (multipliedSpeed * 1000f / 3600f) * deltaTime / pathLength; // Convert speed to m/s and then to track position
                setPosition(trackPosition + step);

                if (trackPosition >= 1f)
                {
                    trackPosition = 1f;
                    currentSpeed = 0f;
                    isRunning = false;
                    Salle tSalle =isReversed?tunnel.salleDepart : tunnel.salleArrivee;
                    Debug.Log("Arrived at end of tunnel, teleporting to salle " + tSalle.name);
                    TeleportToSalle(tSalle, false);
                }
                else
                {
                    AudioStateRefSO audioSO = tunnel.getAudioSOForPosition(actualTrackPosition);
                    if (audioSO != _lastTunnelAudioSO)
                    {
                        _lastTunnelAudioSO = audioSO;
                        if (audioSO != null && audioSO.state != null)
                        {
                            if (debugAudioStates) Debug.Log("Setting tunnel audio state: " + audioSO.state.Name + " at " + actualTrackPosition);
                            audioSO.state.SetValue();
                        }
                        else
                        {
                            noAudioSO.state.SetValue();
                        }
                    }
                }
            }
        }
        else
        {
            targetSpeed = 0f;
        }

        if (splineContainer != null)
        {
            float actualTrackPosition = GetActualTrackPosition(trackPosition);
            Vector3 targetFinalPos = splineContainer.EvaluatePosition(actualTrackPosition);

            // float relTimeSincePlay = (Time.time - timeAtPlay) / smoothGoToPath;
            // if (relTimeSincePlay < 1f)
            // {
            //     float curvedSmooth = speedCurve.Evaluate(relTimeSincePlay);
            //     targetFinalPos = Vector3.Lerp(transform.position , targetFinalPos, curvedSmooth);
            //     Debug.Log($"Smoothing go-to path transition: relTimeSincePlay={relTimeSincePlay:F2} curvedSmooth={curvedSmooth:F2} posAtPlay={posAtPlay:F3} targetFinalPos={targetFinalPos:F3}");
            // }

            SetRigPosition(targetFinalPos, "Tick spline follow");
            
            if (animateRotation)
            {
                Vector3 forward = splineContainer.EvaluateTangent(actualTrackPosition);
                Vector3 up = Vector3.up;
                if (forward != Vector3.zero)
                {
                    SetRigRotation(Quaternion.LookRotation(forward, up), "Tick spline rotation");
                }
            }
        }

    }



    // --- Game State Management ---
    void gameStateUpdate()
    {
        if (!Application.isPlaying) return;

        if (gameState == GameState.Menu)
        {
            ResetGame();
        }

        menu.setActive(gameState == GameState.Menu);
        intro.setActive(gameState == GameState.Intro);
        outro.setActive(gameState == GameState.Outro);
        end.setActive(gameState == GameState.End);


        sallesGO.SetActive(gameState != GameState.Menu);
        tunnelsGO.SetActive(gameState != GameState.Menu);

        timeAtStateChange = Time.time;

        switch (gameState)
        {
            case GameState.Menu:
                menuRefSO?.state.SetValue();
                break;

            case GameState.Intro:
                introRefSO?.state.SetValue();
                break;

            case GameState.Playing:
                playingSO?.state.SetValue();
                Reset();
                break;

            case GameState.Outro:
                outroRefSO?.state.SetValue();
                break;

            case GameState.End:
                endRefSO.state.SetValue();
                break;
        }
    }



    // --- Public API for Buttons ---

    InterviewManager GetInterviewManager()
    {
        return FindAnyObjectByType<InterviewManager>();
    }

    public void ResetGame()
    {
        visitedSalles.Clear();
        GetInterviewManager()?.ResetGame();
        tunnel?.audioEventSO?.evt.Stop(gameObject);
        FindAnyObjectByType<Subtitles>()?.stop();

        KataPortal[] portals = FindObjectsByType<KataPortal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < portals.Length; i++)
        {
            if (portals[i] == null)
            {
                continue;
            }

            portals[i].show(false);
        }

        Pause();
        targetSpeed = 0f;
        trackPosition = 0f;
        freeMotion = true;
        tunnel = null;
        comingFromTunnel = null;
        _lastTunnelAudioSO = null;
        splineContainer = null;
        isReversed = false;
        gameState = GameState.Menu;
        if (salle != null) salle.setActive(false);
        salle = null;
        pointCloudViewDistanceMultiplier = 0f;
    }

    public void GoToSalle(Salle targetSalle)
    {
        BeginRunStartCameraDiagnostics($"GoToSalle begin target='{targetSalle?.name}'");

        if (Application.isPlaying)
        {
            InterviewManager manager = FindAnyObjectByType<InterviewManager>();
            if (manager != null)
            {
                manager.PrepareAssignmentsForSalle(targetSalle);
            }
        }

        //find tunnel from current salle to target salle
        List<Tunnel> outTunnels = getAllOutTunnels();
        foreach (Tunnel tunnel in outTunnels)
        {
            if (tunnel.salleArrivee == targetSalle)
            {
                this.tunnel = tunnel;
                if (salle != null) salle.setActive(false);
                salle = null;
                splineContainer = null; //force re-cache
                isReversed = false;
                ResetPosition();
                BeginRunStartCameraDiagnostics($"GoToSalle forward reset target='{targetSalle?.name}'");
                Play();
                return;
            }
            else if (tunnel.canReverse && tunnel.salleDepart == targetSalle)
            {

                this.tunnel = tunnel;
                if (salle != null) salle.setActive(false);
                salle = null;
                splineContainer = null; //force re-cache
                isReversed = true;
                ResetPosition();
                BeginRunStartCameraDiagnostics($"GoToSalle reverse reset target='{targetSalle?.name}'");
                Play();

                return;
            }
        }
    }

    public void TeleportToSalle(Salle targetSalle, bool assignInterviews = true)
    {
        freeMotion = true;
        comingFromTunnel = tunnel;
        tunnel = null;
        _lastTunnelAudioSO = null;
        if (salle != null) salle.setActive(false);
        salle = targetSalle;
        timeAtArrived = Time.time;
        if (!visitedSalles.Contains(targetSalle))
        {
            visitedSalles.Add(targetSalle);
        }
        ResetPosition();
        if (salle.isExit && !editMode)
        {
            Debug.Log("Teleporting to " + salle.name + ", Arrived at exit salle, going to outro");
            gameState = GameState.Outro;
        }

        if (Application.isPlaying && assignInterviews)
        {
            InterviewManager manager = FindAnyObjectByType<InterviewManager>();
            if (manager != null)
            {
                manager.RefreshAssignmentsForSalle(salle);
            }
        }

        if (salle != null) salle.setActive(true);
    }

    public List<Tunnel> getAllOutTunnels()
    {
        if (!isInASalle()) return new List<Tunnel>();
        List<Tunnel> outTunnels = new List<Tunnel>();
        if (allTunnels == null || allTunnels.Length == 0)
        {
            allTunnels = FindObjectsByType<Tunnel>(FindObjectsSortMode.None);
        }
        foreach (Tunnel tunnel in allTunnels)
        {
            if (tunnel == null)
            {
                continue;
            }

            if (tunnel.salleDepart == salle)
            {
                outTunnels.Add(tunnel);
            }
            else if (tunnel.canReverse && tunnel.salleArrivee == salle)
            {
                outTunnels.Add(tunnel);
            }
        }
        return outTunnels;
    }

    public void Toggle()
    {
        if (isRunning)
            Pause();
        else
            Play();
    }

    public void Play()
    {
        BeginRunStartCameraDiagnostics("Play before state change");

        if (trackPosition >= 0.99f)
        {
            trackPosition = 0f;
            currentSpeed = 0f;
        }

        PrepareTunnelEntryForPlay();

        timeAtPlay = Time.time;
        posAtPlay = transform.position;
        freeMotion = false;
        isRunning = true;


        currentSpeed = 0;

        if (tunnel != null)
        {
            if (tunnel.subtitlesPath != "")
            {
                Subtitles subtitleManager = FindAnyObjectByType<Subtitles>();
                if (subtitleManager != null)
                {
                    subtitleManager.play("off/" + tunnel.subtitlesPath + "_" + language + ".srt");
                }
            }

            if (tunnel.audioEventSO != null)
            {
                tunnel.audioEventSO.evt.Post(gameObject);
            }
        }

        BeginRunStartCameraDiagnostics("Play after state change");


    }

    public void Pause()
    {
        isRunning = false;
        currentSpeed = 0f;
    }

    float GetActualTrackPosition(float logicalTrackPosition)
    {
        return isReversed ? (1f - logicalTrackPosition) : logicalTrackPosition;
    }

    public void setPosition(float position)
    {
        trackPosition = Mathf.Clamp01(position);
        if (isRunning && followPathOrientation && splineContainer != null)
        {
            float actualTrackPosition = GetActualTrackPosition(trackPosition);
            Vector3 forward = splineContainer.EvaluateTangent(actualTrackPosition);
            Vector3 up = Vector3.up;
            if (forward != Vector3.zero)
            {
                SetRigRotation(Quaternion.LookRotation(forward, up), "setPosition followPathOrientation");
            }
        }
    }

    public void Reset()
    {
        visitedSalles = new List<Salle>();
        GetInterviewManager()?.ResetGame();
        TeleportToSalle(initialSalle);
        ResetPosition();
    }
    public void ResetPosition(bool resetRotation = false)
    {
        if (isInASalle())
        {
            if (salle.origin == null) return;
            SetRigPosition(salle.origin.position, "ResetPosition salle");
            timeAtArrived = Time.time;
            BeginRunStartCameraDiagnostics($"ResetPosition salle='{salle.name}' resetRotation={resetRotation}");

            if (gameState == GameState.Playing)
            {
                if (salle.audioSO != null && salle.audioSO.state != null)
                {
                    if (debugAudioStates) Debug.Log("Setting salle audio state: " + salle.audioSO.state.Name);
                    salle.audioSO.state.SetValue();
                }
                else
                {
                    noAudioSO.state.SetValue();
                }
                salle.audioEventSO?.evt.Post(gameObject);
            }
        }
        else
        {
            trackPosition = 0f;
            currentSpeed = 0f;
            if (isInATunnel())
            {
                float actualTrackPosition = GetActualTrackPosition(trackPosition);
                Vector3 tunnelEntryPosition = tunnel.getPositionOnTrack(actualTrackPosition);
                SetRigPosition(tunnelEntryPosition, "ResetPosition tunnel entry");
                float lookAheadTrackPosition = Mathf.Clamp01(actualTrackPosition + (isReversed ? -0.01f : 0.01f));
                Vector3 lookAtPos = tunnel.getPositionOnTrack(lookAheadTrackPosition);
                lookAtPos.y = transform.position.y;
                if (resetRotation) LookRigAt(lookAtPos, Vector3.up, "ResetPosition tunnel rotation");
                BeginRunStartCameraDiagnostics($"ResetPosition tunnel='{tunnel.name}' resetRotation={resetRotation}");

                if (gameState == GameState.Playing)
                {
                    AudioStateRefSO audioSO = tunnel.getAudioSOForPosition(actualTrackPosition);
                    if (audioSO != null && audioSO.state != null)
                    {
                        if (debugAudioStates) Debug.Log("Setting tunnel audio state: " + audioSO.state.Name);
                        audioSO.state.SetValue();
                    }
                    else
                    {
                        noAudioSO.state.SetValue();
                    }
                }
            }
            isRunning = false;
        }
    }


    public bool isInASalle()
    {
        return salle != null;
    }

    public bool isInATunnel()
    {
        return salle == null && tunnel != null;
    }

    public bool isInSalle(Salle checkSalle)
    {
        return salle == checkSalle;
    }

    public bool hasVisitedSalle(Salle checkSalle)
    {
        return visitedSalles.Contains(checkSalle);
    }

    public bool isInTunnel(Tunnel checkTunnel)
    {
        return salle == null && tunnel == checkTunnel;
    }

    public bool isTunnelACurrentOut(Tunnel checkTunnel)
    {
        return isInASalle() && getAllOutTunnels().Contains(checkTunnel);
    }

    Tunnel getClosestTunnel()
    {
        Tunnel[] allTunnels = FindObjectsByType<Tunnel>(FindObjectsSortMode.None);
        Tunnel closestTunnel = null;
        float minDist = 1000;
        foreach (var t in allTunnels)
        {
            float dist = t.getNearestDistance(transform.position);
            if (dist < minDist)
            {
                closestTunnel = t;
                minDist = dist;
            }
        }

        return closestTunnel;
    }

    public bool isRunningReversed()
    {
        return isRunning && isReversed;
    }


    //Vision zones

    float getAverageVisionZoneMaxDistance()
    {
        VisionZone[] zones = FindObjectsByType<VisionZone>(FindObjectsSortMode.None);

        //total zones weight 0 to 1 = use defaultMaxDistance and zones max distance
        //total zones weight > 1 = use only zones max distance, divided by total weight to avoid stacking too much

        float totalWeight = 0f;
        float weightedMaxDistance = 0f;
        foreach (var zone in zones)
        {
            float weight = zone.getWeight();
            totalWeight += weight;
            weightedMaxDistance += weight * zone.maxDistance;
        }

        if (totalWeight == 0f)
        {
            return defaultCamMaxDistance;
        }

        float target = weightedMaxDistance / totalWeight;

        if (totalWeight < 1f)
        {
            return Mathf.Lerp(defaultCamMaxDistance, target, totalWeight);
        }

        return target;
    }

    string BuildPointCloudDiagnostics(float targetFarClip)
    {
        Camera mainCamera = Camera.main;
        VisionZone[] zones = FindObjectsByType<VisionZone>(FindObjectsSortMode.None);

        System.Text.StringBuilder activeZones = new System.Text.StringBuilder();
        int activeZoneCount = 0;
        foreach (VisionZone zone in zones)
        {
            float weight = zone.getWeight();
            if (weight <= 0.01f)
            {
                continue;
            }

            if (activeZones.Length > 0)
            {
                activeZones.Append(", ");
            }

            activeZones.Append(zone.name);
            activeZones.Append(":w=");
            activeZones.Append(weight.ToString("F2"));
            activeZones.Append(",max=");
            activeZones.Append(zone.maxDistance.ToString("F1"));
            activeZoneCount++;
        }

        string cameraName = mainCamera != null ? mainCamera.name : "null";
        float cameraFarClip = mainCamera != null ? mainCamera.farClipPlane : 0f;
        float cameraFieldOfView = mainCamera != null ? mainCamera.fieldOfView : 0f;
        float cameraPixelHeight = mainCamera != null ? mainCamera.pixelRect.height : 0f;
        string cameraTargetTexture = "none";
        if (mainCamera != null && mainCamera.targetTexture != null)
        {
            cameraTargetTexture = mainCamera.targetTexture.width + "x" + mainCamera.targetTexture.height;
        }

        return $"[PointCloudCamera] camera='{cameraName}' camFar={cameraFarClip:F2} targetFar={targetFarClip:F2} camFov={cameraFieldOfView:F2} camPixelHeight={cameraPixelHeight:F0} camTarget={cameraTargetTexture} viewDistanceMultiplier={pointCloudViewDistanceMultiplier:F2} gameState={gameState} activeVisionZones={activeZoneCount} zones=[{activeZones}]";
    }


    //Spawning

    public void handleFakeFloorSelect(HoverExitEventArgs args)
    {
        if (!spawningMode) return;
        if (isInATunnel())
        {
            IXRRayProvider rayProvider = args.interactorObject as IXRRayProvider;
            if (rayProvider != null && rayProvider.rayEndPoint != null)
            {
                tunnel.AddKnotAtPosition(rayProvider.rayEndPoint);
            }
        }
    }



    public string getLanguageSuffix()
    {
        if (language == "" || language == "vo")
        {
            return "";
        }
        return "_" + language;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Color prev = Handles.color;
        Handles.color = new Color(1f, 0.85f, 0.1f, 0.9f);

        // Keep a minimum visible size in the scene view
        Handles.Label(transform.position + Vector3.up * 0.2f, "CAM");
        Handles.color = prev;
    }
#endif
}