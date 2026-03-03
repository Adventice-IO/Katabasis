using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;


[ExecuteAlways]
public class PortalTransformer : MonoBehaviour
{
    public Transform portal;
    SphereCollider pCollider;

    Transform sizeT;
    Transform colliderT;
    Transform sizePlane;
    Transform colliderPlane;
    bool isGrabbing = false;
    //bool _lastGrabbing = false;

    XRGrabInteractable sizeXR;
    XRGrabInteractable colliderXR;

    Vector3 lastScale;
    float lastCollider = 0;

    void OnEnable()
    {
        sizeT = transform.Find("Size");
        colliderT = transform.Find("Collider");
        sizePlane = transform.Find("SizePlane");
        colliderPlane = transform.Find("ColliderPlane");

        pCollider = portal.GetComponent<SphereCollider>();

        sizeXR = sizeT.GetComponent<XRGrabInteractable>();
        colliderXR = colliderT.GetComponent<XRGrabInteractable>();

        sizeXR.selectEntered.AddListener((SelectEnterEventArgs args) =>
        {
            isGrabbing = true;
            lastScale = sizeT.transform.localScale;
        });

        sizeXR.selectExited.AddListener((SelectExitEventArgs args) =>
        {
            isGrabbing = false;
            RuntimeUndoManager.scaleTransformFrom(portal, lastScale, portal.transform.localScale);
            GetComponentInParent<VRBatchPersister>().StageChange(portal);
        }
        );

        colliderXR.selectEntered.AddListener((SelectEnterEventArgs args) =>
        {
            isGrabbing = true;
            lastCollider = pCollider.radius;
        });

        colliderXR.selectExited.AddListener((SelectExitEventArgs args) =>
        {
            isGrabbing = false;
            RuntimeUndoManager.resizeColliderFrom(pCollider, lastCollider, pCollider.radius);
            GetComponentInParent<VRBatchPersister>().StageChange(portal, pCollider.radius);
        }
        );
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if (pCollider == null)
        {
            pCollider = portal.GetComponent<SphereCollider>();
        }


        colliderPlane.localScale = pCollider.bounds.size / 10;
        sizePlane.localScale = portal.localScale / 5;


        if (!isGrabbing)
        {
            sizeT.transform.localPosition = Vector3.right * portal.localScale.x;
            colliderT.transform.localPosition = Vector3.left * portal.localScale.x * pCollider.radius;
        }
        else
        {
            portal.localScale = Vector3.Distance(sizeT.transform.position, transform.position) * Vector3.one;
            pCollider.radius = Vector3.Distance(colliderT.transform.position, transform.position) / portal.localScale.x;
        }
    }
}
