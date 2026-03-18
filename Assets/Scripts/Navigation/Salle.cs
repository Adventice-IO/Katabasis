using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using Framework.Utils.Editor;
#endif

[ExecuteAlways]
public class Salle : MonoBehaviour
{
    public Color color = Color.green;

    public Vector3 size = Vector3.one * 10;
    public bool isExit = false;
    [Range(0, 4)]
    public int niveau = 0;
    public int maxPlayedInterview = 1;

    public Transform origin { get; private set; }

    [Header("Audio Settings")]
    public AudioStateRefSO audioSO;


    void Awake()
    {


        origin = transform.Find("Origin");
    }

    private void OnEnable()
    {
        origin = transform.Find("Origin");
        foreach (var i in interviews)
        {
            i.OnInterviewEnded += onInterviewEnd;
        }
    }

    // Update is called once per frame
    void Update()
    {
        GetComponentsInChildren<Interview>().ToList().ForEach(itw =>
        {
            Vector3 lookAt = origin.position;
            lookAt.y = itw.transform.position.y;
            itw.transform.localRotation = Quaternion.Euler(0, 90, 0);
            itw.transform.parent.LookAt(lookAt, Vector3.up);
        });
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


    public void onInterviewEnd(Interview interview)
    {
        int playedInterviewCount = interviews.Count(i => i.state == Interview.State.Ending);
        if (playedInterviewCount >= maxPlayedInterview)
        {
            foreach (var i in interviews)
            {
                if (i.state != Interview.State.Ending)
                {
                    i.evaporate();
                }
            }
        }
    }

    public Interview[] interviews { get { return GetComponentsInChildren<Interview>(); } }
}
