using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[InitializeOnLoad]
public class VRBatchPersister : MonoBehaviour
{
    // Static storage to survive through the Play Mode session
    private static readonly Dictionary<string, (Vector3 pos, Quaternion rot)> _stagedChanges = new();
    private static GameObject _sallesAsset;

    static VRBatchPersister()
    {
        // Hook into the Editor state change
        EditorApplication.playModeStateChanged += OnStateChanged;
    }

    [Header("Configuration")]
    public GameObject sallesPrefabAsset;

    private void Awake()
    {
        _sallesAsset = sallesPrefabAsset;
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
        if (state == PlayModeStateChange.ExitingPlayMode && _stagedChanges.Count > 0)
        {
            CommitAllToDisk();
        }
    }

    private static void CommitAllToDisk()
    {
        if (_sallesAsset == null) return;

        string assetPath = AssetDatabase.GetAssetPath(_sallesAsset);
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