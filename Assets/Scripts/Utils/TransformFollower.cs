using UnityEngine;

public class TransformFollower : MonoBehaviour
{
    public Transform target;
    public GameObject simulatorObject;

    public float editorVerticalOffset = 10.0f;
    public float verticalOffset = -10.0f;
    public bool useOutsideEditor = true;
    private bool _verticalOffsetEnabled = true;

    public float ConfiguredVerticalOffset
    {
        get
        {
#if UNITY_EDITOR
            return editorVerticalOffset;
#else
            return verticalOffset;
#endif
        }
        set
        {
            value = float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
#if UNITY_EDITOR
            editorVerticalOffset = value;
#else
            verticalOffset = value;
#endif
        }
    }

    public float ActiveVerticalOffset
    {
        get => _verticalOffsetEnabled ? ConfiguredVerticalOffset : 0f;
        set => ConfiguredVerticalOffset = value;
    }

    public void SetVerticalOffsetEnabled(bool enabled)
    {
        _verticalOffsetEnabled = enabled;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if (simulatorObject != null && simulatorObject.activeInHierarchy) return;
        if (target == null) return;


#if !UNITY_EDITOR
        if (!useOutsideEditor) return;
#endif

        Vector3 newPosition = target.position;
        transform.position = newPosition;
        transform.rotation = target.rotation;

        transform.Rotate(Vector3.right, ActiveVerticalOffset);

    }
}
