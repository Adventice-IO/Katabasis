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
    public float endAutoNextTime = 3f;

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

    public AnimationCurve speedCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Read Only")]
    [SerializeField] private float currentSpeed = 0f;
    [SerializeField] private float targetSpeed = 0f;
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

    private void Start()
    {
        sallesGO = GameObject.Find("Salles");
        tunnelsGO = GameObject.Find("Tunnels");

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
                        transform.position = groundPos;
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
    }

    private void Update()
    {
        if (allTunnels == null)
        {
            allTunnels = FindObjectsByType<Tunnel>(FindObjectsSortMode.None);
        }

        Camera.main.farClipPlane = getAverageVisionZoneMaxDistance();

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
            
            if (gameState == GameState.End && Time.time - timeAtStateChange > endAutoNextTime)
            {
                ResetGame();
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
                transform.position += Vector3.up * joystickInput.y * deltaTime;
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
            transform.position = salle.origin.position;
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
            float actualTrackPosition = isReversed ? (1f - trackPosition) : trackPosition;
            targetSpeed = Mathf.Min(tunnel.getDesiredSpeedAtPosition(actualTrackPosition, isReversed), maxSpeed);
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, maxAcceleration * deltaTime);

            float multipliedSpeed = currentSpeed;
            if (Time.time - timeAtPlay < playFullSpeedTime)
            {
                float speedFactor = speedCurve.Evaluate((Time.time - timeAtPlay) / playFullSpeedTime);
                multipliedSpeed *= speedFactor;
            }

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
                    TeleportToSalle(tunnel.salleArrivee, false);
                }
                else
                {
                    AudioStateRefSO audioSO = tunnel.getAudioSOForPosition(0);
                    if (audioSO != null && audioSO.state != null)
                    {
                        Debug.Log("Setting tunnel audio state: " + audioSO.state.Name + " at track position: " + trackPosition);
                        audioSO.state.SetValue();
                    }
                    else
                    {
                        noAudioSO.state.SetValue();
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
            float actualTrackPosition = isReversed ? (1f - trackPosition) : trackPosition;
            transform.position = splineContainer.EvaluatePosition(actualTrackPosition);
            if (animateRotation)
            {
                Vector3 forward = splineContainer.EvaluateTangent(actualTrackPosition);
                Vector3 up = Vector3.up;
                if (forward != Vector3.zero)
                {
                    transform.rotation = Quaternion.LookRotation(forward, up);
                }
            }
        }

    }



    // --- Game State Management ---
    void gameStateUpdate()
    {
        if (!Application.isPlaying) return;

        menu.setActive(gameState == GameState.Menu);
        intro.setActive(gameState == GameState.Intro);
        outro.setActive(gameState == GameState.Outro);


        sallesGO.SetActive(gameState != GameState.Menu);
        tunnelsGO.SetActive(gameState != GameState.Menu);

        timeAtStateChange = Time.time;

        switch (gameState)
        {
            case GameState.Menu:
                ResetGame();
                menuRefSO?.state.SetValue();
                break;

            case GameState.Intro:
                introRefSO?.state.SetValue();
                break;

            case GameState.Playing:
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

    public void ResetGame()
    {
        visitedSalles.Clear();
        gameState = GameState.Menu;
        if (salle != null) salle.setActive(false);
        salle = null;
        pointCloudViewDistanceMultiplier = 0f;
    }

    public void GoToSalle(Salle targetSalle)
    {
        if (Application.isPlaying)
        {
            InterviewManager manager = FindAnyObjectByType<InterviewManager>();
            if (manager != null)
            {
                manager.RefreshAssignmentsForSalle(targetSalle);
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
        if (allTunnels == null)
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
        timeAtPlay = Time.time;
        freeMotion = false;
        isRunning = true;
        currentSpeed = 0;
        // Optional: If we are at the end, restart
        if (trackPosition >= 0.99f)
        {
            trackPosition = 0f;
            currentSpeed = 0f;
        }
    }

    public void Pause()
    {
        isRunning = false;
        currentSpeed = 0f;
    }

    public void setPosition(float position)
    {
        trackPosition = Mathf.Clamp01(position);
    }

    public void Reset()
    {
        visitedSalles = new List<Salle>();
        TeleportToSalle(initialSalle);
        ResetPosition();
    }
    public void ResetPosition(bool resetRotation = false)
    {
        if (isInASalle())
        {
            if (salle.origin == null) return;
            transform.position = salle.origin.position;
            timeAtArrived = Time.time;

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
            }
        }
        else
        {
            trackPosition = 0f;
            currentSpeed = 0f;
            if (isInATunnel())
            {
                transform.position = tunnel.getPositionOnTrack(0);
                Vector3 lookAtPos = tunnel.getPositionOnTrack(0.01f);
                lookAtPos.y = transform.position.y;
                if (resetRotation) transform.LookAt(lookAtPos, Vector3.up);

                if (gameState == GameState.Playing)
                {
                    AudioStateRefSO audioSO = tunnel.getAudioSOForPosition(0);
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
        return getAllOutTunnels().Contains(checkTunnel);
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