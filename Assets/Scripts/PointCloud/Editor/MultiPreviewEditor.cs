using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using BAPointCloudRenderer.CloudController;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

[CustomEditor(typeof(MultiPreview))]
public class MultiPreviewEditor : Editor
{
    private const string PreviewScenePath = "Assets/Scenes/Local/PreviewData.unity";

    static MultiPreviewEditor()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // Remove the preview scene before Play Mode starts, so gameplay never sees it.
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            var scene = SceneManager.GetSceneByPath(PreviewScenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

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
