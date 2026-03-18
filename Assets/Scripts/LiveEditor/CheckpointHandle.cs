#if UNITY_EDITOR
using Framework.Utils.Editor;
#endif
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class CheckpointHandle : MonoBehaviour
{

    public Tunnel.SpeedCheckpoint checkpoint;
    public Tunnel tunnel;

    public bool isHover;
    public bool isGrabbing;

    public int index;

    [SerializeField] InputActionProperty spawnRemoveAction;


    Renderer[] renderers;

    UIDocument doc;
    Slider slider;
    Label label;

    float initSpeed;

    Gradient gradient = new Gradient();
    Color color;

    public void init()
    {

        SplineGrabInteractable grab = GetComponent<SplineGrabInteractable>();
        if (grab != null) grab.splineContainer = tunnel.splineContainer;

        initSpeed = checkpoint.speed;

    }

    public void Start()
    {
        gradient.colorKeys = new GradientColorKey[] {
            new GradientColorKey(Color.cyan, 0f),
            new GradientColorKey(Color.green, .3f),
            new GradientColorKey(Color.orange, .6f),
            new GradientColorKey(Color.red, 1f)
        };

        renderers = GetComponentsInChildren<Renderer>();

        if (tunnel == null)
        {
            tunnel = GetComponentInParent<Tunnel>();
        }

        doc = GetComponentInChildren<UIDocument>();
        slider = doc.rootVisualElement.Q<Slider>("slider");
        label = doc.rootVisualElement.Q<Label>("label");

        slider.highValue = MainController.instance.maxSpeed;
        slider.lowValue = 1;
        slider.value = checkpoint.speed;

        slider.RegisterValueChangedCallback(evt =>
        {
            if (tunnel == null) return;
            float speed = evt.newValue;
            checkpoint.speed = speed;

            setColorFromSpeed();
        });

        slider.RegisterCallback<BlurEvent>(evt =>
        {
            if (tunnel == null) return;
            RuntimeUndoManager.changeCheckpointSpeed(checkpoint, initSpeed, checkpoint.speed);
            initSpeed = checkpoint.speed;
            UnityPlayModeSaver.SaveComponent(tunnel.GetComponent<CheckpointContainer>());
        });

        if (checkpoint != null)
        {
            slider.value = checkpoint.speed;

            setColorFromSpeed();
        }
    }

    public void OnEnable()
    {
        if (spawnRemoveAction != null && spawnRemoveAction.action != null)
        {
            spawnRemoveAction.action.Enable();

            spawnRemoveAction.action.performed += ctx =>
            {
                if (isHover)
                {
                    Debug.Log($"CheckpointHandle spawnRemoveAction performed, isHover: {isHover}");
                    RuntimeUndoManager.removeCheckpoint(tunnel, checkpoint);
                    MainController.instance.removedCheckpoint = true;
                }
                else
                {

                }
            };
        }
    }

    private void OnDisable()
    {
        if (spawnRemoveAction != null && spawnRemoveAction.action != null)
        {
            spawnRemoveAction.action.Disable();
        }
    }


    void Update()
    {
        if (tunnel == null) return;


        if (!isGrabbing)
        {
            Vector3 forward = tunnel.getSplineForwardAtPosition(checkpoint.pos);
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

            transform.position = tunnel.getPositionOnTrack(checkpoint.pos);
        }

        label.text = $"Speed : {Mathf.RoundToInt(checkpoint.speed)} km/h";
    }


    void setColor(Color c)
    {
        if (renderers == null) return;
        foreach (Renderer r in renderers)
        {
            r.material.color = c;
        }
    }

    void setColorFromSpeed()
    {
        color = gradient.Evaluate(checkpoint.speed / MainController.instance.maxSpeed);
        setColor(color);
    }

    public void setHover()
    {
        Debug.Log("CheckpointHandle setHover");
        isHover = true;
        setColor(Color.yellow);
    }

    public void setNone()
    {
        Debug.Log("CheckpointHandle setNone");
        isHover = false;
        if (!isGrabbing) setColorFromSpeed();
    }

    public void setGrabbing()
    {
        Debug.Log("CheckpointHandle setGrabbing");
        isGrabbing = true;
        setColor(Color.purple);
    }

    public void clearGrabbing()
    {
        Debug.Log("CheckpointHandle clearGrabbing");
        isGrabbing = false;
        if (!isHover) setColorFromSpeed();


        if (tunnel != null)
        {
            float pos = tunnel.getClosestTrackPosition(transform.position);
            RuntimeUndoManager.moveCheckpoint(checkpoint, pos);

            transform.position = tunnel.getPositionOnTrack(checkpoint.pos);

#if UNITY_EDITOR
            UnityPlayModeSaver.SaveComponent(tunnel.GetComponent<CheckpointContainer>());
#endif

        }
    }
}
