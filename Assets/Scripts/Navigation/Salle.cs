using Framework.Utils.Editor;
using UnityEngine;

public class Salle : MonoBehaviour
{
    public Color color = Color.green;

    public Vector3 size = Vector3.one * 10;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        if(Application.isPlaying)
        {
            UnityPlayModeSaver.SaveComponent(transform);
        }
    }

    // Update is called once per frame
    void Update()
    {
    }

    private void OnDrawGizmos()
    {
        Matrix4x4 mat = new Matrix4x4();
        mat.SetTRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.matrix = mat;

        Gizmos.color = color;
        Gizmos.DrawWireCube(Vector3.up * size.y / 2, size);

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
