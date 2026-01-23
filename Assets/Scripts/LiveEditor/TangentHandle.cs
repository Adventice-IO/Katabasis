using UnityEngine;

[ExecuteAlways]
public class TangentHandle : MonoBehaviour
{

    LineRenderer r;
    Transform parent;

    private void OnEnable()
    {
        r = GetComponent<LineRenderer>();
        parent = transform.parent;
    }

    private void OnStart()
    {
        parent = transform.parent;
    }

    void Update()
    {
        if (transform.lossyScale == Vector3.zero || transform.localPosition == Vector3.zero)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(transform.localPosition.normalized);
        r.positionCount = 2;
        r.SetPosition(0, parent.position);
        r.SetPosition(1, parent.position + (transform.position - parent.position) * transform.localScale.x);
    }
}
