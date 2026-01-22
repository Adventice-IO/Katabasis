using UnityEngine;

[ExecuteAlways]
public class TangentHandle : MonoBehaviour
{

    LineRenderer r;
    private void OnEnable()
    {
        r = GetComponent<LineRenderer>();
    }
    void Update()
    {
        if(transform.localScale == Vector3.zero)
        {
            r.positionCount = 0;
            return;
        }

        transform.rotation = Quaternion.LookRotation(transform.localPosition.normalized);
        r.positionCount = 2;
        r.SetPosition(0, transform.parent.position);
        r.SetPosition(1, transform.parent.position + (transform.position - transform.parent.position) * transform.localScale.x);
    }
}
