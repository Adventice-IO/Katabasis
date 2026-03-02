using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[InitializeOnLoad]
public class VRBatchPersister : MonoBehaviour
{
    struct StagedChange
    {
        public string Path;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public float colliderScale;
    }
    // Staged changes grouped per prefab asset path
    private static readonly Dictionary<string, Dictionary<string, StagedChange>> _stagedByPrefab = new();

    static VRBatchPersister()
    {
        EditorApplication.playModeStateChanged += OnStateChanged;
    }

    [Header("Configuration")]
    public string prefabName;

    /// <summary>
    /// Call this from your VR script. It only saves to RAM.
    /// </summary>
    public void StageChange(Transform movedObject, float colliderScale = 0)
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
            buffer = new Dictionary<string, StagedChange>();
            _stagedByPrefab[assetPath] = buffer;
        }

        buffer[path] = new StagedChange
        {
            Path = path,
            Position = movedObject.localPosition,
            Rotation = movedObject.localRotation,
            Scale = movedObject.localScale,
            colliderScale = colliderScale
        };
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
            Dictionary<string, StagedChange> changes = kvp.Value;
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
                        target.localPosition = change.Value.Position;
                        target.localRotation = change.Value.Rotation;
                        target.localScale = change.Value.Scale;

                        if (change.Value.colliderScale > 0)
                        {
                            if (target.TryGetComponent(out BoxCollider collider))
                            {
                                collider.size = change.Value.colliderScale * Vector3.one;
                            }
                            else if (target.TryGetComponent(out SphereCollider sphereCollider))
                            {
                                sphereCollider.radius = change.Value.colliderScale;
                            }
                        }
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