using UnityEngine;

public class VisionZone : MonoBehaviour
{
    [Range(0, 1)]
    public float feather = 0.1f;

    [Range(0, 100)]
    public float maxDistance = 20f;
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    public float getWeight()
    {
        //inside 1, outside 0, in the feather zone between 1 and 0
        if (Camera.main == null) return 0f;
        Vector3 cp = transform.worldToLocalMatrix.MultiplyPoint(Camera.main.transform.position);
        cp += Vector3.one / 2; // Center the coordinates around (0, 0, 0)

        if (feather == 0)
        {
            return (cp.x > 0 && cp.x < 1 && cp.y > 0 && cp.y < 1 && cp.z > 0 && cp.z < 1) ? 1f : 0f;
        }

        float fD = feather / 2;

        float distX1 = Mathf.Max(cp.x / fD, 0);
        float distX2 = Mathf.Max((1 - cp.x) / fD, 0);
        float distY1 = Mathf.Max(cp.y / fD, 0);
        float distY2 = Mathf.Max((1 - cp.y) / fD, 0);
        float distZ1 = Mathf.Max(cp.z / fD, 0);
        float distZ2 = Mathf.Max((1 - cp.z) / fD, 0);
        float minDist = Mathf.Min(distX1, distX2, distY1, distY2, distZ1, distZ2);
        return minDist;
    }

    private void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0, .6f, 1f, 0.5f);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

        Gizmos.color = new Color(0, .8f, .8f, 0.5f);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one * (1 - feather));

        Gizmos.color = new Color(0, .7f, .7f, getWeight()/3f);
        Gizmos.DrawCube(Vector3.zero, Vector3.one * (1 - feather * 2));

    }
}
