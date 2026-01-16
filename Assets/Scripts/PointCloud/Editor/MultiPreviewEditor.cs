using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using BAPointCloudRenderer.CloudController;

[CustomEditor(typeof(MultiPreview))]
public class MultiPreviewEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        MultiPreview previewscript = (MultiPreview)target;
        if (!EditorApplication.isPlaying)
        {
            if (GUILayout.Button("Update Preview"))
            {
                previewscript.UpdatePreview();
            }
            if (GUILayout.Button("Delete Preview"))
            {
                previewscript.KillPreview();
            }
        }
    }
}
