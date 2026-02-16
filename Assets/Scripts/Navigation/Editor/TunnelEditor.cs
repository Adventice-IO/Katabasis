using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

[CustomEditor(typeof(Tunnel))]
public class TunnelEditor : Editor
{
    private void OnSceneGUI()
    {
        var script = (Tunnel)target;
        var container = script.GetComponent<SplineContainer>();

        if (container == null || container.Spline == null)
        {
            return;
        }

        HandleAddKnot(container);

       
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        Tunnel script = (Tunnel)target;
        GUILayout.Space(10);

    }

    private static Vector3 ClosestPointOnRay(Vector3 point, Ray ray)
    {
        float t = Vector3.Dot(point - ray.origin, ray.direction);
        if (t < 0f)
        {
            t = 0f;
        }

        return ray.origin + ray.direction * t;
    }

    private void HandleAddKnot(SplineContainer container)
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.MouseDown || e.button != 0 || !e.control || !e.shift)
        {
            return;
        }

        var spline = container.Spline;
        if (spline == null || spline.Count == 0)
        {
            return;
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

        // Coarse search along the spline to find an approximate t closest to the ray
        int coarseSamples = Mathf.Max(64, spline.Count * 8);
        float bestT = 0f;
        float bestDist = float.PositiveInfinity;
        Vector3 bestPoint = Vector3.zero;

        for (int i = 0; i <= coarseSamples; i++)
        {
            float t = i / (float)coarseSamples;
            Vector3 p = container.EvaluatePosition(t);
            Vector3 q = ClosestPointOnRay(p, ray);
            float d = (p - q).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                bestT = t;
                bestPoint = p;
            }
        }

        // Refine using nearest point utility with the closest coarse projection
        Vector3 projOnRay = ClosestPointOnRay(bestPoint, ray);

        Tunnel script = (Tunnel)target;
        script.AddKnotAtPosition(projOnRay);
  
        e.Use();
    }
}