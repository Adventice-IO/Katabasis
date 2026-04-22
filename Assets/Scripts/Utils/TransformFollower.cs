using UnityEngine;

public class TransformFollower : MonoBehaviour
{
    public Transform target;
    public GameObject simulatorObject;

    public float editorVerticalOffset = 10.0f;
    public float verticalOffset = -10.0f;
    public bool useOutsideEditor = true;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if (simulatorObject != null && simulatorObject.activeInHierarchy) return;
        if (target == null) return;


#if UNITY_EDITOR
        float offset = editorVerticalOffset;
#else
        float offset = verticalOffset;
        if (!useOutsideEditor) return;
        
#endif

        Vector3 newPosition = target.position;
        transform.position = newPosition;
        transform.rotation = target.rotation;

        transform.Rotate(Vector3.right, offset);

    }
}
