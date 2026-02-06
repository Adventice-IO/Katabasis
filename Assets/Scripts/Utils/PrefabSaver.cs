using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class SelfSavingPrefab : MonoBehaviour
{
#if UNITY_EDITOR
    private void OnEnable()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            ApplySelfToPrefab();
        }
    }

    public void ApplySelfToPrefab()
    {
        PrefabUtility.ApplyPrefabInstance(this.gameObject, InteractionMode.AutomatedAction);
        AssetDatabase.SaveAssets();
        Debug.Log($"<color=lime>Saved:</color> {gameObject.name} asset updated on disk.");
    }

#endif
}