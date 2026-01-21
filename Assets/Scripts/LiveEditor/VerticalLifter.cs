using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class VerticalHandleDriver : MonoBehaviour
{
    [Header("Auto-Find Settings")]
    public Rigidbody parentRigidbody;

    private XRGrabInteractable interactable;
    private Transform currentInteractor;

    // We store the position of the parent relative to the controller
    private Vector3 localOffsetFromHand;

    // State memory
    private bool wasKinematic;
    private bool usedGravity;

    private Vector3 initLocalPosition;

    void Awake()
    {
        interactable = GetComponent<XRGrabInteractable>();
        if (parentRigidbody == null) parentRigidbody = GetComponentInParent<Rigidbody>();

        initLocalPosition = transform.localPosition;
    }

    void OnEnable()
    {
        interactable.selectEntered.AddListener(OnGrab);
        interactable.selectExited.AddListener(OnRelease);
    }

    void OnDisable()
    {
        interactable.selectEntered.RemoveListener(OnGrab);
        interactable.selectExited.RemoveListener(OnRelease);
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        if (parentRigidbody == null) return;

        currentInteractor = args.interactorObject.transform;

        // 1. Snapshot Physics
        wasKinematic = parentRigidbody.isKinematic;
        usedGravity = parentRigidbody.useGravity;
        parentRigidbody.isKinematic = true;
        parentRigidbody.useGravity = false;

        // 2. Calculate "Smart" Offset (Relative to Controller Rotation/Position)
        // This converts the Parent's world position into the Controller's LOCAL space.
        // It effectively "saves" the distance and angle of the beam.
        localOffsetFromHand = currentInteractor.InverseTransformPoint(parentRigidbody.position);
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        currentInteractor = null;

        if (parentRigidbody != null)
        {
            parentRigidbody.isKinematic = wasKinematic;
            parentRigidbody.useGravity = usedGravity;
        }

        // Reset local position to initial
        transform.localPosition = initLocalPosition;
    }

    void Update()
    {
        if (currentInteractor != null && parentRigidbody != null)
        {
            // 3. Calculate where the object SHOULD be in World Space
            // based on the controller's current rotation and position.
            Vector3 virtualTargetPosition = currentInteractor.TransformPoint(localOffsetFromHand);

            // 4. Move Parent ONLY on Y
            // We take the X and Z from the current parent (locking them)
            // And take the Y from our calculated "virtual" beam target
            Vector3 finalPosition = new Vector3(
                parentRigidbody.position.x,
                virtualTargetPosition.y,
                parentRigidbody.position.z
            );

            parentRigidbody.position = finalPosition;
        }
    }
}