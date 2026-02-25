using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using TMPro;
using Unity.XR.CompositionLayers.UIInteraction;





#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways] // This makes the script run even when NOT in Play mode
public class MainController : MonoBehaviour
{
    public static MainController instance;
    [Header("Setup")]
    public Salle initialSalle;

    [Header("Audio Settings")]
    public AudioStateRefSO noAudioSO;
    public bool debugAudioStates = false;

    [Header("State")]
    public Salle salle;
    public Tunnel tunnel;

    [Header("Controls")]
    public bool animateRotation = false;

    [Range(0f, 1f)]
    public float trackPosition; // 0.0 = Start, 1.0 = End


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
    [SerializeField] private InputActionProperty teleportForwardAction;
    [SerializeField] private InputActionProperty teleportBackwardAction; //XRI SnapTurn

    bool spawningMode;
    public bool removedInSpawnMode;
    public bool removedCheckpoint;


    bool verticalMove;

    float timeAtSpawnMode;

    float timeAtToggleFreeMotion;
    bool freeMotionSwitched;

    public GameObject lockInfoPlane;
    public GameObject speedInfo;
    public GameObject teleportationLoco;

    float timeAtArrived; //in a salle or tunnel
    public float timeSinceArrived
    {
        get
        {
            return Time.time - timeAtArrived;
        }
    }

    public Tunnel comingFromTunnel { get; private set; }
    List<Salle> visitedSalles = new List<Salle>();

    Tunnel[] allTunnels;

    private void Start()
    {
        Reset();
    }

#if UNITY_EDITOR
    private void OnEnable()
    {
        if (instance != null && instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        instance = this;

        allTunnels = FindObjectsByType<Tunnel>(FindObjectsSortMode.None);

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

            if(teleportForwardAction.action != null)
            {
                teleportForwardAction.action.Enable();
                teleportForwardAction.action.performed += ctx =>
                {
                    if (!freeMotion && isInATunnel())
                    {
                        float nextSpeedPos = tunnel.getNextSpeedCheckpointPosition(trackPosition, isReversed);
                        if (nextSpeedPos >= 0)                        
                        {
                            setPosition(nextSpeedPos);
                        }
                    }
                };
            }

            if (teleportBackwardAction.action != null)
            {
                teleportBackwardAction.action.Enable();
                //XRI Snap Turn, check that it's joystick down
                teleportBackwardAction.action.performed += ctx =>
                {
                    if (!freeMotion && isInATunnel())
                    {
                        float prevSpeedPos = tunnel.getPreviousSpeedCheckpointPosition(trackPosition, isReversed);
                        if (prevSpeedPos >= 0)
                        {
                            setPosition(prevSpeedPos);
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
        if (lockInfoPlane != null)
        {
            lockInfoPlane.SetActive(!freeMotion);
        }

        if (speedInfo != null)
        {
            speedInfo.SetActive(isInATunnel() && !freeMotion);
            if (isInATunnel() && !freeMotion)
            {
                TextMeshPro textMesh = speedInfo.GetComponent<TextMeshPro>();
                if (textMesh != null)
                {
                    textMesh.text = Mathf.RoundToInt(currentSpeed) + " km/h - Target : " + Mathf.RoundToInt(targetSpeed) + " km/h";
                }
            }
        }


        if (Application.isPlaying)
        {
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
                    kt.updateActive();
                }

            }

            if (spawnAction.action != null)
            {
                bool pressed = spawnAction.action.IsPressed();
                if (pressed != spawningMode)
                {
                    spawningMode = pressed;
                    if (spawningMode) timeAtSpawnMode = (float)EditorApplication.timeSinceStartup;
                    else
                    {
                        if (!removedInSpawnMode)
                        {
                            Debug.Log("Adding knot at end of spawn mode");
                            float duration = (float)(EditorApplication.timeSinceStartup - timeAtSpawnMode);
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
                if(joystickInput.y != 0f)
                {
                    Pause();
                }
                editSmoothSpeed = Mathf.MoveTowards(editSmoothSpeed, joystickInput.y * editMaxSpeed, maxAcceleration * deltaTime);
                if (editSmoothSpeed != 0f && !isRunning)
                {
                    setPosition(trackPosition + editSmoothSpeed * deltaTime / (splineContainer != null ? splineContainer.Spline.GetLength() : 1f)) ;
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
                    TeleportToSalle(tunnel.salleArrivee);
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

    // --- Public API for Buttons ---

    public void GoToSalle(Salle targetSalle)
    {
        //find tunnel from current salle to target salle
        List<Tunnel> outTunnels = getAllOutTunnels();
        foreach (Tunnel tunnel in outTunnels)
        {
            if (tunnel.salleArrivee == targetSalle)
            {
                this.tunnel = tunnel;
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
                salle = null;
                splineContainer = null; //force re-cache
                isReversed = true;
                ResetPosition();
                Play();
                return;
            }
        }
    }

    public void TeleportToSalle(Salle targetSalle)
    {
        freeMotion = true;
        comingFromTunnel = tunnel;
        tunnel = null;
        salle = targetSalle;
        timeAtArrived = Time.time;
        if (!visitedSalles.Contains(targetSalle))
        {
            visitedSalles.Add(targetSalle);
        }
        ResetPosition();
    }

    public List<Tunnel> getAllOutTunnels()
    {
        if (!isInASalle()) return new List<Tunnel>();
        List<Tunnel> outTunnels = new List<Tunnel>();
        foreach (Tunnel tunnel in allTunnels)
        {
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
            transform.position = salle.origin.position;
            timeAtArrived = Time.time;

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