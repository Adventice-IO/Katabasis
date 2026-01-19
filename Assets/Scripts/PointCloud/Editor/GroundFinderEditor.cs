using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

[CustomEditor(typeof(GroundFinder))]

public class GroundFinderr : Editor
{
    private void OnSceneGUI()
    {
        var script = (GroundFinder)target;

    }
}
