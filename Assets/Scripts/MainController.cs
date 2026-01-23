using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using System.Runtime.CompilerServices;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;


#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways] // This makes the script run even when NOT in Play mode
public class MainController : MonoBehaviour
{
    public static MainController instance;
    [Header("Setup")]
    public Salle initialSalle;

    [Header("State")]
    public Salle salle;
    public Tunnel tunnel;

    [Header("Controls")]
    public bool animateRotation = false;

    [Range(0f, 1f)]
    public float trackPosition; // 0.0 = Start, 1.0 = End


    [Header("Physics Settings")]
    public float minSpeed = 0.05f; // Units per second
    public float maxSpeed = 10f; // Units per second
    public float acceleration = 5f; // Units per second squared
    public float deceleration = 5f; // Units per second squared


    [Header("Read Only")]
    [SerializeField] private float currentSpeed = 0f;
    [SerializeField] private bool isRunning = false;
    [SerializeField] private bool isReversed = false;
    private SplineContainer splineContainer;
    private float pathLength;

#if UNITY_EDITOR
    private double lastEditorTime;
#endif


    [Header("Interaction")]
    public bool freeMotion;
    public ContinuousMoveProvider moveProvider;
    [SerializeField] private InputActionProperty joystickAction;
    [SerializeField] private InputActionProperty toggleFreeMoveAction;
    [SerializeField] private InputActionProperty spawnAction;
    [SerializeField] private InputActionProperty cancelAction;

    bool spawningMode;
    public bool removedInSpawnMode;

    float timeAtSpawnMode;

    public GameObject lockInfoPlane;

    float timeAtArrived; //in a salle or tunnel
    public float timeSinceArrived
    {
        get
        {
            return Time.time - timeAtArrived;
        }
    }


    private void Start()
    {
        TeleportToSalle(initialSalle);
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
                    bool newFreeMotion = !freeMotion;
                    if (!newFreeMotion && isInATunnel())
                    {
                        trackPosition = tunnel.getClosestTrackPosition(transform.position);
                    }
                    freeMotion = newFreeMotion;
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

        if (Application.isPlaying)
        {
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
                            float duration = (float)(EditorApplication.timeSinceStartup - timeAtSpawnMode);
                            if (duration < .3f)
                            {
                                if(tunnel != null) tunnel.AddKnotAtPosition(GroundFinder.getGroundForPosition(transform.position, .2f, 1.0f, 6));
                            }
                            timeAtSpawnMode = 0f;
                        }
                        removedInSpawnMode = false;
                    }
                }
            }
        }
    }

    private void Tick(float deltaTime)
    {
        moveProvider.enabled = freeMotion;
        if (freeMotion) return;

        if (isInASalle())
        {
            transform.position = salle.origin.position;
            return;
        }

        if (tunnel == null)
            return;

        if (Application.isPlaying)
        {

            Vector2 joystickInput = joystickAction.action?.ReadValue<Vector2>() ?? Vector2.zero;
            trackPosition += joystickInput.y * maxSpeed * deltaTime / (splineContainer != null ? splineContainer.Spline.GetLength() : 1f);
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
            float targetSpeedLimit = tunnel.GetTargetSpeedAt(actualTrackPosition, minSpeed, maxSpeed, acceleration, deceleration);
            float accelRate = (currentSpeed < targetSpeedLimit) ? acceleration : deceleration;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeedLimit, accelRate * deltaTime);

            if (pathLength > 0)
            {
                float step = (currentSpeed * deltaTime) / pathLength;
                trackPosition += step;

                if (trackPosition >= 1f)
                {
                    trackPosition = 1f;
                    currentSpeed = 0f;
                    isRunning = false;
                    TeleportToSalle(tunnel.salleArrivee);
                }
            }
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
        tunnel = null;
        salle = targetSalle;
        timeAtArrived = Time.time;
        ResetPosition();
    }

    public List<Tunnel> getAllOutTunnels()
    {
        if (!isInASalle()) return new List<Tunnel>();
        Tunnel[] allTunnels = FindObjectsByType<Tunnel>(FindObjectsSortMode.None);
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
        freeMotion = false;
        isRunning = true;
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
    }

    public void setPosition(float position)
    {
        trackPosition = Mathf.Clamp01(position);
    }

    public void Reset()
    {
        TeleportToSalle(initialSalle);
        ResetPosition();
    }
    public void ResetPosition()
    {
        if (isInASalle())
        {
            transform.position = salle.origin.position;
            timeAtArrived = Time.time;
        }
        isRunning = false;
        trackPosition = 0f;
        currentSpeed = 0f;
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

    public bool isInTunnel(Tunnel checkTunnel)
    {
        return salle == null && tunnel == checkTunnel;
    }

    public bool isTunnelACurrentOut(Tunnel checkTunnel)
    {
        return getAllOutTunnels().Contains(checkTunnel);
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