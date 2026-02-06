using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[InitializeOnLoad]
public class VRBatchPersister : MonoBehaviour
{
    // Static storage to survive through the Play Mode session
    private static readonly Dictionary<string, (Vector3 pos, Quaternion rot)> _stagedChanges = new();
    private static string _prefabName;

    static VRBatchPersister()
    {
        // Hook into the Editor state change
        EditorApplication.playModeStateChanged += OnStateChanged;
    }

    [Header("Configuration")]
    public string prefabName;

    private void Awake()
    {
        _prefabName = prefabName;
    }

    /// <summary>
    /// Call this from your VR script. It only saves to RAM.
    /// </summary>
    public void StageChange(Transform movedObject)
    {
        string path = GetRelativePath(movedObject, transform);

        // Overwrite or add the latest transform state to the buffer
        _stagedChanges[path] = (movedObject.localPosition, movedObject.localRotation);

        Debug.Log($"<color=yellow>Staged:</color> {path} (Buffer count: {_stagedChanges.Count})");
    }

    private static void OnStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode && _stagedChanges.Count > 0)
        {
            CommitAllToDisk();
        }
    }

    private static void CommitAllToDisk()
    {
        // _sallesAsset is in Assets/Prefabs/Salles.pregab
        string assetPath = "Assets/Prefabs/" + _prefabName + ".prefab";
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);

        try
        {
            foreach (var change in _stagedChanges)
            {
                Transform target = prefabRoot.transform.Find(change.Key);
                if (target != null)
                {
                    target.localPosition = change.Value.pos;
                    target.localRotation = change.Value.rot;
                }
            }

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, assetPath);
            Debug.Log($"<color=lime>Success:</color> Flushed {_stagedChanges.Count} changes to {assetPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
            _stagedChanges.Clear();
        }
    }

    private string GetRelativePath(Transform child, Transform root)
    {
        List<string> pathSteps = new List<string>();
        Transform current = child;
        while (current != null && current != root)
        {
            pathSteps.Add(current.name);
            current = current.parent;
        }
        pathSteps.Reverse();
        return string.Join("/", pathSteps);
    }
}