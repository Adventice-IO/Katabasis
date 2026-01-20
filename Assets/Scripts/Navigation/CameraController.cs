using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;



#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways] // This makes the script run even when NOT in Play mode
public class CameraController : MonoBehaviour
{
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

    private SplineContainer splineContainer;
    private float pathLength;

#if UNITY_EDITOR
    private double lastEditorTime;
#endif


    public enum CurrentTool
    {
        Move,
        Rotate
    }


    [Header("Interaction")]
    public bool freeMotion;
    public DynamicMoveProvider moveProvider;
    public CurrentTool currentTool = CurrentTool.Move;
    [SerializeField] private InputActionProperty joystickAction;



    private void Start()
    {
        // Initialize salle

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

        if (Application.isPlaying && joystickAction.action != null)
        {
            joystickAction.action.Enable();
        }
    }

    private void OnDisable()
    {
        EditorApplication.update -= EditorTick;

        if (Application.isPlaying && joystickAction.action != null)
        {
            joystickAction.action.Disable();
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
        if (Application.isPlaying)
            return;

        // In edit mode we run from EditorApplication.update for consistent updates.
    }

    private void Tick(float deltaTime)
    {
        moveProvider.enabled = freeMotion;
        if (freeMotion) return;

        if (salle != null)
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
            float targetSpeedLimit = tunnel.GetTargetSpeedAt(trackPosition, minSpeed, maxSpeed, acceleration, deceleration);

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
                    salle = tunnel.salleArrivee;
                    tunnel = null;
                }
            }
        }

        if (splineContainer != null)
        {
            transform.position = splineContainer.EvaluatePosition(trackPosition);
            if (animateRotation)
            {
                Vector3 forward = splineContainer.EvaluateTangent(trackPosition);
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
                this.salle = null;
                this.splineContainer = null; //force re-cache
                ResetPosition();
                Play();
                return;
            }
        }
    }

    public void TeleportToSalle(Salle targetSalle)
    {
        salle = targetSalle;
        tunnel = null;
        ResetPosition();
    }

    public List<Tunnel> getAllOutTunnels()
    {
        if (salle == null) return new List<Tunnel>();
        Tunnel[] allTunnels = FindObjectsByType<Tunnel>(FindObjectsSortMode.None);
        List<Tunnel> outTunnels = new List<Tunnel>();
        foreach (Tunnel tunnel in allTunnels)
        {
            if (tunnel.salleDepart == salle)
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
        salle = initialSalle;
        tunnel = null;
        ResetPosition();
    }
    public void ResetPosition()
    {
        if (salle != null)
        {
            transform.position = salle.origin.position;
        }
        isRunning = false;
        trackPosition = 0f;
        currentSpeed = 0f;
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