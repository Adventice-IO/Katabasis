using UnityEngine;

public class TransformFollower : MonoBehaviour
{
    public Transform target;
    public GameObject simulatorObject;

    public float verticalOffset = 0.0f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
#if UNITY_EDITOR
        if (simulatorObject != null && simulatorObject.activeInHierarchy) return;
        if (target == null) return;

        Vector3 newPosition = target.position;
        transform.position = newPosition;
        transform.rotation = target.rotation;
        transform.Rotate(Vector3.right, verticalOffset);
#endif
    }
}
