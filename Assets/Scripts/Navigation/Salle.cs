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

    public Interview[] interviews;

    private void Start()
    {
        origin = transform.Find("Origin");
        interviews = GetComponentsInChildren<Interview>(true);
        foreach (var i in interviews)
        {
            i.OnInterviewEnded += onInterviewEnd;
        }
    }


    public void setActive(bool active)
    {
        if (!active)
        {
            foreach (var i in interviews)
            {
                i.cleanup();
            }
        }
        else
        {
            interviews.ToList().ForEach(itw => itw.gameObject.SetActive(true));

        }
    }

    // Update is called once per frame
    void Update()
    {
        interviews.ToList().ForEach(itw =>
        {
            Vector3 lookAt = origin.position;
            if (itw == null) return;
            lookAt.y = itw.transform.position.y;
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
}
