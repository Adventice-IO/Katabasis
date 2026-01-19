using UnityEngine;

public class SalleOrigin : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.5f, new Vector3(1, 1, 1));

        //Draw down arrow

        Gizmos.DrawLine(transform.position + Vector3.up * 1.5f, transform.position + Vector3.up * 0.5f);
        Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, transform.position + Vector3.right * 0.2f + Vector3.up * 0.7f);
        Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, transform.position + Vector3.left * 0.2f + Vector3.up * 0.7f);
    }
}
