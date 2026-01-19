using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

[CustomEditor(typeof(Tunnel))]
public class TunnelEditor : Editor
{
    // PSEUDOCODE PLAN:
    // - In OnSceneGUI:
    //   - Retrieve Tunnel and its SplineContainer; early-out if missing.
    //   - Handle Ctrl+Shift+LeftClick:
    //     - If Event is MouseDown with left button and control+shift:
    //       - Build a world ray from the mouse.
    //       - Coarse sample along spline to find t whose world position is closest to this ray.
    //       - Refine this t: project the closest coarse point onto the ray, then call SplineUtility.GetNearestPoint
    //         (with that projected point in local space) to get more accurate t and nearest local pos.
    //       - Determine segment index from t (curve count based).
    //       - Build a BezierKnot at the refined nearest position:
    //         - Position: local-space nearest point.
    //         - Tangents: ±tangentDir * handleLen (tangentDir from container.EvaluateTangent at t).
    //         - Rotation: look rotation along tangent (fallback math.up if needed).
    //       - Undo.RecordObject on the container, insert the knot at segIndex + 1, mark dirty, consume the event.
    //   - Draw existing slowdown handles (unchanged).
    //
    // - Helper methods:
    //   - ClosestPointOnRay: returns closest point on a ray to a given position.

    private void OnSceneGUI()
    {
        var script = (Tunnel)target;
        var container = script.GetComponent<SplineContainer>();

        if (container == null || container.Spline == null)
        {
            return;
        }

        HandleAddKnot(container);

        for (int i = 0; i < script.manualSlowdowns.Count; i++)
        {
            var slowdown = script.manualSlowdowns[i];
            Vector3 worldPos = container.EvaluatePosition(slowdown.position);

            // --- 1. Position Handle (Sphere) ---
            EditorGUI.BeginChangeCheck();

            Handles.color = Color.red;
            float handleSize = HandleUtility.GetHandleSize(worldPos) * 0.15f;

            Vector3 newWorldPos = Handles.FreeMoveHandle(
                worldPos, handleSize, Vector3.zero, Handles.SphereHandleCap);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(script, "Move Slowdown");

                // Snap to nearest point on spline
                float3 localPos = container.transform.InverseTransformPoint(newWorldPos);
                SplineUtility.GetNearestPoint(container.Spline, localPos, out float3 nearest, out float t);
                slowdown.position = t;
            }

            // --- 2. Radius Handle (Ring) ---
            EditorGUI.BeginChangeCheck();
            Handles.color = new Color(1f, 0.4f, 0f, 1f); // Orange

            // Orientation of the ring matches the track direction
            Quaternion rot = Quaternion.LookRotation(container.EvaluateTangent(slowdown.position), Vector3.up);

            float newRadius = Handles.RadiusHandle(rot, worldPos, slowdown.radius);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(script, "Resize Radius");
                slowdown.radius = Mathf.Max(0.5f, newRadius);
            }
        }
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        Tunnel script = (Tunnel)target;
        GUILayout.Space(10);

        if (GUILayout.Button("Add Slowdown Zone", GUILayout.Height(30)))
        {
            Undo.RecordObject(script, "Add Slowdown");
            script.manualSlowdowns.Add(new Tunnel.ManualSlowdown
            {
                position = 0.5f,
                radius = 5f,
                strength = 0.5f
            });
        }
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