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
        Vector3 localPos = transform.InverseTransformPoint(Camera.main.transform.position);
        Vector3 absPos = new Vector3(Mathf.Abs(localPos.x), Mathf.Abs(localPos.y), Mathf.Abs(localPos.z));
        Vector3 size = Vector3.one * 0.5f - Vector3.one * feather;
        float weight = 1;
        if (absPos.x > size.x)
        {
            weight *= 1 - (absPos.x - size.x) / feather;
        }
        if (absPos.y > size.y)
        {
            weight *= 1 - (absPos.y - size.y) / feather;
        }
        if (absPos.z > size.z)
        {
            weight *= 1 - (absPos.z - size.z) / feather;
        }
        return Mathf.Clamp01(weight);
    }

    private void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0, .6f, 1f, 0.5f);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);

        Gizmos.color = new Color(0, .8f, .8f, 0.5f);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one * (1 - feather));

    }
}
