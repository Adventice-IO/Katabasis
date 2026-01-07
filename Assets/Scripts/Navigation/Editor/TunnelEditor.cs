using System.Drawing.Drawing2D;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

[CustomEditor(typeof(Tunnel))]
public class TunnelEditor : Editor
{
    private void OnSceneGUI()
    {
        Tunnel script = (Tunnel)target;
        SplineContainer container = script.GetComponent<SplineContainer>();

        if (container == null || container.Spline == null) return;

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
            Quaternion rot = Quaternion.LookRotation(container.EvaluateTangent(slowdown.position));

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
}