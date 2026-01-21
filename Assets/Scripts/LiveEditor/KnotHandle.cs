using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.UIElements;

public class KnotHandle : MonoBehaviour
{


    Transform prevHandle;
    LineRenderer prevLine;

    Transform nextHandle;
    LineRenderer nextLine;

    Transform manipPlane;
    Transform knot;
    Transform upKnot;


    public int knotIndex = 0;
    public SplineContainer splineContainer;

    Renderer[] snapRenderers;

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
        MovingNextHandle
    }

    //make it usable in an event
    public ManipState manipState = ManipState.None;

    void OnEnable()
    {
        prevHandle = transform.Find("PrevHandle");
        prevLine = prevHandle.GetComponent<LineRenderer>();

        nextHandle = transform.Find("NextHandle");
        nextLine = nextHandle.GetComponent<LineRenderer>();

        prevLine.positionCount = 2;
        nextLine.positionCount = 2;

        manipPlane = transform.Find("ManipPlane");
        knot = transform.Find("Knot");
        upKnot = transform.Find("Up");


        snapRenderers = transform.Find("Snap").GetComponentsInChildren<Renderer>();

        if (Application.isPlaying)
        {
            prevHandle.GetComponent<Renderer>().material.color = Color.red;
            nextHandle.GetComponent<Renderer>().material.color = Color.green;
            prevLine.material.color = Color.lightPink;
            nextLine.material.color = Color.lightGreen;
        }

    }

    void Update()
    {

        prevLine.SetPosition(0, transform.position);
        prevLine.SetPosition(1, prevHandle.position);

        nextLine.SetPosition(0, transform.position);
        nextLine.SetPosition(1, nextHandle.position);

        Vector3 localInPos = transform.InverseTransformPoint(prevHandle.position);
        Vector3 localOutPos = transform.InverseTransformPoint(nextHandle.position);
        float maxDist = Mathf.Max(localInPos.magnitude, localOutPos.magnitude);

        manipPlane.localScale = Vector3.one * maxDist * 2 / 10;

        switch (manipState)
        {
            case ManipState.HoverKnot:
                knot.GetComponent<Renderer>().material.color = new Color(1, 1, 0, 1);
                break;

            case ManipState.HoverUpKnot:
                upKnot.GetComponent<Renderer>().material.color = new Color(1, 1, 0, 1);
                break;


            case ManipState.HoverPrevHandle:
                prevHandle.GetComponent<Renderer>().material.color = new Color(1, 1, 0, 1);
                prevLine.material.color = new Color(1, 1, 0, 1);
                break;

            case ManipState.HoverNextHandle:
                nextHandle.GetComponent<Renderer>().material.color = new Color(1, 1, 0, 1);
                nextLine.material.color = new Color(1, 1, 0, 1);
                break;

            case ManipState.MovingKnot:
                knot.GetComponent<Renderer>().material.color = new Color(1, 0, 1, 1);
                break;

            case ManipState.MovingUpKnot:
                upKnot.GetComponent<Renderer>().material.color = new Color(1, 0, 1, 1);
                break;

            case ManipState.MovingNextHandle:
                prevHandle.GetComponent<Renderer>().material.color = new Color(1, 0, 1, 1);

                break;
            case ManipState.MovingPrevHandle:
                nextHandle.GetComponent<Renderer>().material.color = new Color(1, 0, 1, 1);
                break;

            case ManipState.HoverSnap:
                foreach (var rend in snapRenderers)
                {
                    rend.material.color = new Color(0, 1, 1, 1);
                }
                break;

            case ManipState.None:
            default:
                knot.GetComponent<Renderer>().material.color = Color.white;
                upKnot.GetComponent<Renderer>().material.color = Color.white;
                prevHandle.GetComponent<Renderer>().material.color = Color.red;
                nextHandle.GetComponent<Renderer>().material.color = Color.green;
                prevLine.material.color = Color.lightPink;
                nextLine.material.color = Color.lightGreen;
                foreach (var rend in snapRenderers)
                {
                    rend.material.color = Color.cyan;
                }

                break;
        }

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
            var knot = spline[knotIndex];
            transform.position = splineContainer.transform.TransformPoint(knot.Position);
            transform.rotation = knot.Rotation;
            prevHandle.localPosition = knot.TangentIn;
            nextHandle.localPosition = knot.TangentOut;
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

    public bool isMoving()
    {
        return manipState == ManipState.MovingKnot || manipState == ManipState.MovingNextHandle || manipState == ManipState.MovingPrevHandle || manipState == ManipState.MovingUpKnot;
    }
    public void hoverKnot() { if (!isMoving()) manipState = ManipState.HoverKnot; }
    public void hoverUpKnot() { if (!isMoving()) manipState = ManipState.HoverUpKnot; }
    public void hoverPrevHandle() { if (!isMoving()) manipState = ManipState.HoverPrevHandle; }
    public void hoverNextHandle() { if (!isMoving()) manipState = ManipState.HoverNextHandle; }
    public void moveKnot() { manipState = ManipState.MovingKnot; }

    public void moveUpKnot() { manipState = ManipState.MovingUpKnot; }

    public void movePrevHandle() { manipState = ManipState.MovingPrevHandle; }
    public void moveNextHandle() { manipState = ManipState.MovingNextHandle; }
    public void clearHoverState()
    {
        if (!isMoving())
            manipState = ManipState.None;
    }

    public void clearManipState()
    {
        manipState = ManipState.None;
    }


    public void snapHover(bool value)
    {
        if (!isMoving())
            manipState = value ? ManipState.HoverSnap : ManipState.None;
    }

    public void snapTouch()
    {
        Vector3 groundPos = GroundFinder.getGroundForPosition(transform.position, .2f, 1.0f, 6);
        transform.position = groundPos;
        updateKnotPosition();
        snapHover(false);
    }
}