using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class VerticalHandleDriver : MonoBehaviour
{
    [Header("Auto-Find Settings")]
    public Rigidbody parentRigidbody;

    private XRGrabInteractable interactable;
    private Transform currentInteractor;

    private Vector3 localOffsetFromHand;

    private Vector3 initLocalPosition;
    private Vector3 initWorldPosition;
    private Vector3 initParentPosition;

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
        initWorldPosition = transform.position;
        initParentPosition = parentRigidbody.position;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        currentInteractor = null;
        transform.localPosition = initLocalPosition;
    }

    void Update()
    {
        if (currentInteractor != null && parentRigidbody != null)
        {
            Vector3 delta = transform.position - initWorldPosition;
            parentRigidbody.position = initParentPosition+delta;
        }
    }
}