using Framework.Utils.Editor;
using UnityEditor;
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

    public void init()
    {

        SplineGrabInteractable grab = GetComponent<SplineGrabInteractable>();
        if (grab != null) grab.splineContainer = tunnel.splineContainer;

        initSpeed = checkpoint.speed;
    }

    public void Start()
    {

        renderers = GetComponentsInChildren<Renderer>();

        if (tunnel == null)
        {
            tunnel = GetComponentInParent<Tunnel>();
        }

        doc = GetComponentInChildren<UIDocument>();
        slider = doc.rootVisualElement.Q<Slider>("slider");
        label = doc.rootVisualElement.Q<Label>("label");

        slider.RegisterValueChangedCallback(evt =>
        {
            if (tunnel == null) return;
            float speed = evt.newValue;
            label.text = $"Speed : {checkpoint.speed}";

            checkpoint.speed = speed;
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
            slider.value = checkpoint.pos;
            label.text = $"Speed : {checkpoint.speed}";
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

        label.text = $"Speed : {checkpoint.speed}";
    }


    void setColor(Color c)
    {
        if (renderers == null) return;
        foreach (Renderer r in renderers)
        {
            r.material.color = c;
        }
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
        if (!isGrabbing) setColor(Color.white);
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
        if (!isHover) setColor(Color.white);


        if (tunnel != null)
        {
            float pos = tunnel.getClosestTrackPosition(transform.position);
            RuntimeUndoManager.moveCheckpoint(checkpoint, pos);

            transform.position = tunnel.getPositionOnTrack(checkpoint.pos);

            UnityPlayModeSaver.SaveComponent(tunnel.GetComponent<CheckpointContainer>());


        }
    }
}
