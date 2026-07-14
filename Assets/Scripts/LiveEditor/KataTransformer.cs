
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Splines;

public class KataTransformer : MonoBehaviour
{
    //Transform manipPlane;
    Transform baseT;
    Transform up;
    Transform snap;

    Vector3 originalPos;

    MainController mainController;

    Renderer[] renderers;
    Renderer upRenderers;
    Renderer[] snapRenderers;

    public Transform extraSaveTransform;

    public GameObject extraEditObject;

    public bool forceDisabled = false;


    public enum ManipState
    {
        None,
        HoverBase,
        HoverUp,
        HoverSnap,
        MovingBase,
        MovingUp,
        Remove
    }

    //make it usable in an event
    public ManipState manipState = ManipState.None;
    private ManipState lastManipState = ManipState.None;
    //bool removePressed = false;

    void OnEnable()
    {
        baseT = transform.Find("Base");
        renderers = baseT.GetComponentsInChildren<Renderer>();

        up = transform.Find("Up");
        upRenderers = up.GetComponentInChildren<Renderer>();

        snap = transform.Find("Snap");
        snapRenderers = snap.GetComponentsInChildren<Renderer>();


        baseT?.gameObject.SetActive(false);
        up?.gameObject.SetActive(false);
        snap?.gameObject.SetActive(false);

    }

    private void OnDisable()
    {

    }

    void Start()
    {
        mainController = GameObject.FindAnyObjectByType<MainController>();
        updateActive();
    }

    void Update()
    {

#if UNITY_EDITOR
        if (manipState != lastManipState)
        {
            lastManipState = manipState;

            if (manipState == ManipState.Remove)
            {
                foreach (var rend in renderers)
                {
                    rend.material.color = Color.red;
                }
                upRenderers.material.color = Color.red;
                foreach (var rend in snapRenderers)
                {
                    rend.material.color = Color.red;
                }
            }
            else
            {
                Color hoverColor = new Color(1, 1, 0, 1);
                Color movingColor = new Color(1, 0, 1, 1);
                Color baseColor = new Color(1, 1, 1, 1);

                lastManipState = manipState;
                foreach (var rend in renderers)
                {
                    rend.material.color = manipState == ManipState.HoverBase ? hoverColor : (manipState == ManipState.MovingBase ? movingColor : baseColor);
                }

                upRenderers.material.color = manipState == ManipState.HoverUp ? hoverColor : (manipState == ManipState.MovingUp ? movingColor : baseColor);

                foreach (var rend in snapRenderers)
                {
                    rend.material.color = manipState == ManipState.HoverSnap ? hoverColor : baseColor;
                }
            }
        }

        updateActive();
#endif

    }


    public void updateActive()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            baseT.gameObject.SetActive(true);
            up.gameObject.SetActive(true);
            snap.gameObject.SetActive(true);
            if (extraEditObject != null)
            {
                extraEditObject.SetActive(true);
            }
            return;
        }

#endif
        Collider collider = GetComponent<Collider>();

#if UNITY_EDITOR
        if (mainController == null)
        {
            mainController = GameObject.FindAnyObjectByType<MainController>();
        }
        
        bool editMode = mainController.editMode;
        bool finalActive = editMode && !forceDisabled;

        baseT.gameObject.SetActive(finalActive);
        up.gameObject.SetActive(finalActive);
        snap.gameObject.SetActive(finalActive);
        if (extraEditObject != null)
        {
            extraEditObject.SetActive(finalActive);
        }

        if (collider != null) collider.enabled = finalActive;
#else
         if(collider != null) collider.enabled = true;
#endif
    }
    public bool isMoving()
    {
        return manipState == ManipState.MovingBase || manipState == ManipState.MovingUp;
    }

    public bool isHover()
    {
        return manipState == ManipState.HoverBase || manipState == ManipState.HoverUp;
    }
    public void hover() { if (!isMoving()) manipState = ManipState.HoverBase; }
    public void hoverUp() { if (!isMoving()) manipState = ManipState.HoverUp; }
    public void move() { manipState = ManipState.MovingBase; originalPos = transform.position; }

    public void moveUp() { manipState = ManipState.MovingUp; originalPos = transform.position; }


    public void clearHoverState()
    {
        if (!isMoving())
            manipState = ManipState.None;
    }

    public void clearManipState()
    {
        if (isMoving())
        {
            RuntimeUndoManager.moveTransformFrom(transform, originalPos, transform.rotation, transform.position, transform.rotation);
            saveAfterPlay();
        }
        manipState = ManipState.None;
    }


    public void snapHover(bool value)
    {
        if (!isMoving())
            manipState = value ? ManipState.HoverSnap : ManipState.None;
    }

    public void snapTouch()
    {
        Vector3 groundPos = GroundFinder.getGroundForPosition(transform.position, .3f, 1.5f, 6);
        RuntimeUndoManager.moveTransform(transform, groundPos, transform.rotation);
        snapHover(false);
    }

    void saveAfterPlay()
    {
        // UnityPlayModeSaver.SaveComponent(transform);
        VRBatchPersister batchPersister = GetComponentInParent<VRBatchPersister>();
        if (batchPersister != null)
        {
            Debug.Log("Found batch persister on game object " + batchPersister.gameObject.name + ", staging change.");
            batchPersister.StageChange(transform);

            if (extraSaveTransform != null)
            {
                batchPersister.StageChange(extraSaveTransform);
            }
        }
    }

}
