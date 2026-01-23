using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Splines;

public class KnotHandle : MonoBehaviour
{


    Transform prevHandle;
    LineRenderer prevLine;

    Transform nextHandle;
    LineRenderer nextLine;

    Transform manipPlane;
    Transform knot;
    Transform upKnot;
    Transform snap;

    MainController mainController;

    BezierKnot originalKnot;

    public int knotIndex = 0;
    public SplineContainer splineContainer;

    Renderer[] knotRenderers;
    Renderer upKnotRenderer;
    Renderer nextRenderer;
    Renderer prevRenderer;
    Renderer[] snapRenderers;

    [SerializeField] private InputActionProperty removeAction;


    bool showHandles = true;
    float showAnim = 1.0f;

    public enum ManipState
    {
        None,
        HoverKnot,
        HoverUpKnot,
        HoverPrevHandle,
        HoverNextHandle,
        HoverSnap,
        MovingKnot,
        MovingUpKnot,
        MovingPrevHandle,
        MovingNextHandle,
        RemoveKnot
    }

    //make it usable in an event
    public ManipState manipState = ManipState.None;
    private ManipState lastManipState = ManipState.None;
    bool removePressed = false;

    void OnEnable()
    {
        prevHandle = transform.Find("PrevHandle");
        prevLine = prevHandle.GetComponent<LineRenderer>();

        nextHandle = transform.Find("NextHandle");
        nextLine = nextHandle.GetComponent<LineRenderer>();
        nextRenderer = nextHandle.GetComponentInChildren<MeshRenderer>();
        prevRenderer = prevHandle.GetComponentInChildren<MeshRenderer>();

        prevLine.positionCount = 2;
        nextLine.positionCount = 2;

        manipPlane = transform.Find("ManipPlane");

        knot = transform.Find("Knot");
        knotRenderers = knot.GetComponentsInChildren<Renderer>();

        upKnot = transform.Find("Up");
        upKnotRenderer = upKnot.GetComponentInChildren<Renderer>();


        snap = transform.Find("Snap");
        snapRenderers = snap.GetComponentsInChildren<Renderer>();

        mainController = MainController.instance;

        if (Application.isPlaying)
        {
            prevHandle.GetComponent<Renderer>().material.color = Color.red;
            nextHandle.GetComponent<Renderer>().material.color = Color.green;
            prevLine.material.color = Color.lightPink;
            nextLine.material.color = Color.lightGreen;

            if (removeAction != null && removeAction.action != null)
            {
                removeAction.action.Enable();

                removeAction.action.performed += ctx =>
                {
                    if (!removePressed)
                    {
                        if (isHover())
                        {
                            removePressed = true;
                            manipState = ManipState.RemoveKnot;
                            mainController.removedInSpawnMode = true;
                        }
                    }
                };
                removeAction.action.canceled += ctx =>
                {
                    removePressed = false;
                    if (manipState == ManipState.RemoveKnot)
                    {
                        RuntimeUndoManager.removeKnot(splineContainer.Spline, knotIndex);
                    }
                };
            }
        }

    }

    private void OnDisable()
    {
        if (Application.isPlaying)
        {
            if (removeAction != null && removeAction.action != null)
            {
                removeAction.action.Disable();
            }
        }
    }

    void Update()
    {

        Vector3 localInPos = transform.InverseTransformPoint(prevHandle.position);
        Vector3 localOutPos = transform.InverseTransformPoint(nextHandle.position);
        float maxDist = Mathf.Max(localInPos.magnitude, localOutPos.magnitude);

        if (manipPlane != null) manipPlane.localScale = Vector3.one * maxDist * 2 / 10;

        if (manipState != lastManipState)
        {
            lastManipState = manipState;

            if (manipState == ManipState.RemoveKnot)
            {
                foreach (var rend in knotRenderers)
                {
                    rend.material.color = Color.red;
                }
                upKnotRenderer.material.color = Color.red;
                prevRenderer.material.color = Color.red;
                nextRenderer.material.color = Color.red;
                prevLine.material.color = Color.red;
                nextLine.material.color = Color.red;
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
                foreach (var rend in knotRenderers)
                {
                    rend.material.color = manipState == ManipState.HoverKnot ? hoverColor : (manipState == ManipState.MovingKnot ? movingColor : baseColor);
                }

                upKnotRenderer.material.color = manipState == ManipState.HoverUpKnot ? hoverColor : (manipState == ManipState.MovingUpKnot ? movingColor : baseColor);

                prevRenderer.material.color = manipState == ManipState.HoverPrevHandle ? hoverColor : (manipState == ManipState.MovingPrevHandle ? movingColor : Color.red);
                prevLine.material.color = manipState == ManipState.HoverPrevHandle ? hoverColor : (manipState == ManipState.MovingPrevHandle ? movingColor : Color.lightPink);
                nextRenderer.material.color = manipState == ManipState.HoverNextHandle ? hoverColor : (manipState == ManipState.MovingNextHandle ? movingColor : Color.green);
                nextLine.material.color = manipState == ManipState.HoverNextHandle ? hoverColor : (manipState == ManipState.MovingNextHandle ? movingColor : Color.lightGreen);
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
            showHandles = Camera.main != null && Vector3.Distance(Camera.main.transform.position, transform.position) < 10.0f;
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


        prevHandle.localScale = Vector3.one * showAnim;
        nextHandle.localScale = Vector3.one * showAnim;
        //knot.localScale = Vector3.one * showAnim;
        upKnot.localScale = Vector3.one * showAnim;
        snap.localScale = Vector3.one * showAnim;



        if (isMoving())
        {
            updateKnotPosition();
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
                prevHandle.localPosition = knot.TangentIn;
                nextHandle.localPosition = knot.TangentOut;
            }
            else
            {
                Debug.LogWarning("Knot index out of range in KnotHandle: " + knotIndex + " / " + spline.Count);
            }

        }
    }


    void updateKnotPosition()
    {
        Vector3 localPos = splineContainer.transform.InverseTransformPoint(transform.position);
        Vector3 worldTanInpos = prevHandle.position;
        Vector3 worldTanOutpos = nextHandle.position;

        Vector3 prevMirrorPosition = transform.position + (transform.position - prevHandle.position);
        Vector3 lookWorldPos = manipState == ManipState.MovingPrevHandle ? prevMirrorPosition : worldTanOutpos;
        transform.LookAt(lookWorldPos, Vector3.up);

        Vector3 localTanIn = transform.InverseTransformPoint(worldTanInpos);
        Vector3 localTanOut = transform.InverseTransformPoint(worldTanOutpos);
        var newKnot = splineContainer.Spline[knotIndex];
        newKnot.Position = localPos;
        newKnot.Rotation = transform.rotation;

        if (manipState == ManipState.MovingNextHandle)
        {
            localTanIn = -localTanOut.normalized * prevHandle.localPosition.magnitude;
        }
        else if (manipState == ManipState.MovingPrevHandle)
        {
            localTanOut = -localTanIn.normalized * nextHandle.localPosition.magnitude;
        }

        newKnot.TangentIn = localTanIn;
        newKnot.TangentOut = localTanOut;
        splineContainer.Spline.SetKnot(knotIndex, newKnot);
    }

    public void updateActive()
    {
        bool isMiddle = knotIndex > 0 && knotIndex < splineContainer.Spline.Count - 1;

        knot.gameObject.SetActive(isMiddle);
        upKnot.gameObject.SetActive(isMiddle);

        prevHandle.gameObject.SetActive(knotIndex > 0);
        prevLine.gameObject.SetActive(knotIndex > 0);

        nextHandle.gameObject.SetActive(knotIndex < splineContainer.Spline.Count - 1);
        nextLine.gameObject.SetActive(knotIndex < splineContainer.Spline.Count - 1);

        snap.gameObject.SetActive(isMiddle);

        GetComponent<Collider>().enabled = isMiddle;
    }
    public bool isMoving()
    {
        return manipState == ManipState.MovingKnot || manipState == ManipState.MovingNextHandle || manipState == ManipState.MovingPrevHandle || manipState == ManipState.MovingUpKnot;
    }

    public bool isHover()
    {
        return manipState == ManipState.HoverKnot || manipState == ManipState.HoverPrevHandle || manipState == ManipState.HoverNextHandle || manipState == ManipState.HoverUpKnot;
    }
    public void hoverKnot() { if (!isMoving()) manipState = ManipState.HoverKnot; }
    public void hoverUpKnot() { if (!isMoving()) manipState = ManipState.HoverUpKnot; }
    public void hoverPrevHandle() { if (!isMoving()) manipState = ManipState.HoverPrevHandle; }
    public void hoverNextHandle() { if (!isMoving()) manipState = ManipState.HoverNextHandle; }
    public void moveKnot() { manipState = ManipState.MovingKnot; originalKnot = snapshotKnot(); }

    public void moveUpKnot() { manipState = ManipState.MovingUpKnot; originalKnot = snapshotKnot(); }

    public void movePrevHandle() { manipState = ManipState.MovingPrevHandle; originalKnot = snapshotKnot(); }
    public void moveNextHandle() { manipState = ManipState.MovingNextHandle; originalKnot = snapshotKnot(); }



    public void clearHoverState()
    {
        if (!isMoving())
            manipState = ManipState.None;
    }

    public void clearManipState()
    {
        BezierKnot workingKnot = snapshotKnot();
        RuntimeUndoManager.changeKnot(splineContainer.Spline, knotIndex, workingKnot, originalKnot);
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
        transform.position = groundPos;
        originalKnot = splineContainer.Spline[knotIndex];
        updateKnotPosition();
        RuntimeUndoManager.changeKnot(splineContainer.Spline, knotIndex, splineContainer.Spline[knotIndex], originalKnot);
        snapHover(false);
    }

    public BezierKnot snapshotKnot()
    {
        return splineContainer.Spline[knotIndex];
    }
}