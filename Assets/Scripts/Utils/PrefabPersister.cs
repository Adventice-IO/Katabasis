using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[InitializeOnLoad]
public class VRBatchPersister : MonoBehaviour
{
    // Staged changes grouped per prefab asset path
    private static readonly Dictionary<string, Dictionary<string, (Vector3 pos, Quaternion rot)>> _stagedByPrefab = new();

    static VRBatchPersister()
    {
        EditorApplication.playModeStateChanged += OnStateChanged;
    }

    [Header("Configuration")]
    public string prefabName;

    /// <summary>
    /// Call this from your VR script. It only saves to RAM.
    /// </summary>
    public void StageChange(Transform movedObject)
    {
        string assetPath = GetPrefabAssetPath();
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            Debug.LogWarning($"[VRBatchPersister] prefabName is empty on {name}, cannot stage change.");
            return;
        }

        string path = GetRelativePath(movedObject, transform);

        if (!_stagedByPrefab.TryGetValue(assetPath, out var buffer))
        {
            buffer = new Dictionary<string, (Vector3 pos, Quaternion rot)>();
            _stagedByPrefab[assetPath] = buffer;
        }

        buffer[path] = (movedObject.localPosition, movedObject.localRotation);
        Debug.Log($"<color=yellow>[{assetPath}] Staged:</color> {path} (Buffer count: {buffer.Count})");
    }

    private static void OnStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode && _stagedByPrefab.Count > 0)
        {
            CommitAllToDisk();
        }
    }

    private static void CommitAllToDisk()
    {
        foreach (var kvp in _stagedByPrefab)
        {
            string assetPath = kvp.Key;
            Dictionary<string, (Vector3 pos, Quaternion rot)> changes = kvp.Value;
            if (changes.Count == 0) continue;

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
            if (prefabRoot == null)
            {
                Debug.LogError($"[{assetPath}] Could not load prefab contents.");
                continue;
            }

            try
            {
                foreach (var change in changes)
                {
                    Transform target = prefabRoot.transform.Find(change.Key);
                    if (target != null)
                    {
                        target.localPosition = change.Value.pos;
                        target.localRotation = change.Value.rot;
                    }
                    else
                    {
                        Debug.LogWarning($"[{assetPath}] Could not find transform '{change.Key}' to apply staged change.");
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, assetPath);
                Debug.Log($"<color=lime>Success:</color> Flushed {changes.Count} changes to {assetPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[{assetPath}] Could not save prefab changes: {e.Message}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        _stagedByPrefab.Clear();
    }

    private string GetPrefabAssetPath()
    {
        return string.IsNullOrWhiteSpace(prefabName) ? null : $"Assets/Prefabs/{prefabName}.prefab";
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