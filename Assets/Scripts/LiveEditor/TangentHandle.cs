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
        if (transform.lossyScale == Vector3.zero && transform.localPosition.magnitude < .01f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(transform.localPosition);
        r.positionCount = 2;
        r.SetPosition(0, transform.parent.position);
        r.SetPosition(1, transform.parent.position + (transform.position - transform.parent.position) * transform.localScale.x);
    }
}
