using UnityEngine;

public class Salle : MonoBehaviour
{
    public Color color = Color.green;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = color;
        Gizmos.DrawWireCube(transform.position + Vector3.up * 1.5f, new Vector3(5, 3 , 5));

        // draw centered text at position
#if UNITY_EDITOR
        var labelPos = transform.position + Vector3.up * 10;
        var style = new GUIStyle
        {
            alignment = TextAnchor.MiddleCenter
        };
        style.normal.textColor = color;
        UnityEditor.Handles.Label(labelPos, gameObject.name, style);
#endif
    }

    public Transform origin { get { return transform.Find("Origin"); } }
    public Interview[] interviews { get { return GetComponentsInChildren<Interview>(); } }
}
