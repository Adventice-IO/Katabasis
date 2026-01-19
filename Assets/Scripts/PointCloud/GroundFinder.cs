using BAPointCloudRenderer.CloudController;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;


[ExecuteAlways]
public class GroundFinder : MonoBehaviour
{
    public bool liveDebug;

    [Range(0,3f)]
    public float horizontalSearch = 0.5f;
    [Range(0, 10f)]
    public float verticalSearch = 0.8f;
    [Range(0,10)]
    public int maxSearchRenderers = 5;

    void OnEnable()
    {
    }

    void Update()
    {

    }

    static Transform getRoot()
    {
        KatabasisMeshConfiguration meshConfig = GameObject.FindAnyObjectByType<KatabasisMeshConfiguration>();
        if (Application.isPlaying)
        {
            return meshConfig.root;
        }
        else
        {
            return meshConfig.transform.GetComponentInChildren<MultiPreview>().getPreviewRoot();
        }

    }

    public static List<Renderer> getContainingRenderers(Vector3 position, int maxRenderers)
    {
        List<Renderer> renderers = new List<Renderer>();
        Transform root = getRoot();
        if (root == null) return renderers;

        foreach (Transform block in root)
        {
            Renderer rend = block.GetComponent<Renderer>();
            if (rend == null) continue;
            Bounds b = rend.bounds;
            //check xz overlap
            if (position.x < b.min.x || position.x > b.max.x || position.z < b.min.z || position.z > b.max.z)
            {
                // no overlap
                continue;
            }


            renderers.Add(rend);
        }

        renderers.Sort((a, b) => (a.bounds.size.magnitude).CompareTo(b.bounds.size.magnitude)); // sort by lowest min y
        List<Renderer> smallestRenderers = renderers.GetRange(0, Mathf.Min(maxRenderers, renderers.Count));

        return smallestRenderers;
    }
    public static Vector3 getGroundForPosition(Vector3 position, float hThreshold, float vThreshold, int maxRenderers)
    {


        float targetY = position.y;

        List<Renderer> renderers = getContainingRenderers(position, maxRenderers);
        //Debug.Log("Snap threshold " + threshold + " considering " + smallestRenderers.Count + " renderers for position " + position);
        Vector3 lowestPoint = Vector3.positiveInfinity;
        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer rend = renderers[i];
            Bounds b = rend.bounds;

            MeshFilter mf = rend.GetComponent<MeshFilter>();
            if (mf != null)
            {
                // get all points around a cylinder of radius 1 unit, get average of the lowest half

                Vector3[] vertices = mf.sharedMesh.vertices;
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 worldPos = rend.transform.TransformPoint(vertex);
                    float horizontalDistance = Vector2.Distance(new Vector2(worldPos.x, worldPos.z), new Vector2(position.x, position.z));
                    float verticalDistance = Mathf.Abs(worldPos.y - position.y);
                    if (horizontalDistance < hThreshold && verticalDistance < vThreshold)
                    {
                        if (worldPos.y < lowestPoint.y)
                        {
                            lowestPoint = worldPos;
                            targetY = lowestPoint.y;
                            Gizmos.color = Color.green;
                        }
                    }
                }


            }
        }

        return new Vector3(position.x, targetY, position.z);

    }

    void OnDrawGizmos()
    {
#if UNITY_EDITOR
        if (!liveDebug)
        {
            return;
        }


        Vector3 groundPos = getGroundForPosition(transform.position, horizontalSearch, verticalSearch, maxSearchRenderers);
        Gizmos.color = groundPos == transform.position ? Color.yellow : Color.green;
        Gizmos.DrawLine(transform.position, groundPos);
        Gizmos.DrawWireCube(groundPos, new Vector3(2f, .1f, 2f));
        Gizmos.DrawCube(groundPos, new Vector3(1f, 0.1f, 1f));
        Gizmos.DrawLine(groundPos, transform.position);

        List<Renderer> renderers = getContainingRenderers(transform.position, maxSearchRenderers);
        for(int i = 0; i < renderers.Count; i++)
        {
            Renderer rend = renderers[i];
            Bounds b = rend.bounds;
            Color gColor = Color.red;
            gColor.a = 0.2f;
            Gizmos.color = gColor;
            Gizmos.DrawWireCube(b.center, b.size);
        }
#endif
    }
}