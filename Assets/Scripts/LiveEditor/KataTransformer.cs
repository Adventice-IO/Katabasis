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

    public int knotIndex = 0;
    public bool isFirstOrLast = false;
    public SplineContainer splineContainer;

    Renderer[] renderers;
    Renderer upRenderers;
    Renderer[] snapRenderers;

    bool showHandles = true;
    float showAnim = 1.0f;

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
    bool removePressed = false;

    void OnEnable()
    {
        baseT = transform.Find("Base");
        renderers = baseT.GetComponentsInChildren<Renderer>();

        up = transform.Find("Up");
        upRenderers = up.GetComponentInChildren<Renderer>();

        snap = transform.Find("Snap");
        snapRenderers = snap.GetComponentsInChildren<Renderer>();

        mainController = MainController.instance;
    }

    private void OnDisable()
    {

    }

    void Update()
    {


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

        if (manipState != ManipState.None)
        {
            showAnim = 1.0f;
        }
        else
        {
            showHandles = !isFirstOrLast && Camera.main != null && Vector3.Distance(Camera.main.transform.position, transform.position) < 10.0f;
        }

        if (showHandles)
        {
            showAnim += Time.deltaTime * 5;
        }
        else
        {
            showAnim -= Time.deltaTime * 5;
        }

        showAnim = Mathf.Clamp01(showAnim);


        up.localScale = Vector3.one * showAnim;
        snap.localScale = Vector3.one * showAnim;



        if (isMoving())
        {
        }
        else
        {
            if (splineContainer == null)
            {
                return;
            }

            var spline = splineContainer.Spline;
            if (knotIndex < spline.Count)
            {
                var knot = spline[knotIndex];
                transform.position = splineContainer.transform.TransformPoint(knot.Position);
                transform.rotation = knot.Rotation;
            }
            else
            {
                Debug.LogWarning(" index out of range in Handle: " + knotIndex + " / " + spline.Count);
            }

        }
    }


    public void updateActive()
    {
        bool isMiddle = knotIndex > 0 && knotIndex < splineContainer.Spline.Count - 1;

        baseT.gameObject.SetActive(isMiddle);
        up.gameObject.SetActive(isMiddle);
        snap.gameObject.SetActive(isMiddle);

        GetComponent<Collider>().enabled = isMiddle;
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

}
