using BAPointCloudRenderer.CloudData;
using BAPointCloudRenderer.Controllers;
using BAPointCloudRenderer.Loading;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace BAPointCloudRenderer.CloudController
{
    /// <summary>
    /// This class enables previewing the point clouds in the editor.
    /// By default, it displays the bounding box of the attached point cloud set.
    /// If ShowPoints is set to true it also loads in points (only from the first Level of Detail) 
    /// to give a coarse approximation of the final point cloud. The points will be approximately equally
    /// distributed from all the given point clouds. The points will be rendered as 1px-Points.
    /// In general, the preview doesn't always update live, so please use the "Update Preview"-Button in the editor
    /// to update the preview after you made changes.
    /// </summary>
    [ExecuteAlways]
    public class MultiPreview : MonoBehaviour
    {
        private List<PointCloudLoader> _loaders = null;
        private List<Node> _nodes = null;
        private List<PointCloudLoader> _nodeLoaders = null;
        private BoundingBox _currentBB = null;
        private Transform _setTransform;
        private AbstractPointCloudSet _setToPreview;
        private bool _showPoints;
        private int _pointBudget;
        public Material material;
        private bool _createMesh = false;
        private Thread loadingThread = null;

#if UNITY_EDITOR
        /// <summary>
        /// PointCloudSet for which to create the preview
        /// </summary>
        public AbstractPointCloudSet SetToPreview;
        /// <summary>
        /// Whether points should be loaded as well
        /// </summary>
        public bool ShowPoints = false;
        /// <summary>
        /// The maximum number of points to load
        /// </summary>
        public int PointBudget = 65000;
        /// <summary>
        /// Maximum points per generated mesh. If a node has more points than this, it will be split into multiple meshes.
        /// </summary>
        public int MaxPointsPerMesh = 65000;
        /// <summary>
        /// Minimum node depth (inclusive) to include in the preview. 0 = root. Increase for finer detail.
        /// </summary>
        public int MinPreviewDepth = 0;
        /// <summary>
        /// Maximum node depth (inclusive) to include in the preview. -1 = no limit. Increase for finer detail at the cost of more points.
        /// </summary>
        public int MaxPreviewDepth = -1;

        private const string PreviewScenePath = "Assets/Scenes/Local/PreviewData.unity";
        private GameObject _previewRoot;
        private Scene previewScene;

#if UNITY_EDITOR
        public void OnEnable()
        {
            //Check when exiting edit mode to unload the preview scene, so we don't keep it around accidentally with all the preview objects when entering play mode
            EditorApplication.playModeStateChanged += HandlePlayModeStateChange;
        }

        public void OnDisable()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChange;
        }

        private void HandlePlayModeStateChange(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                Debug.Log("Exiting edit mode, unloading preview scene");
                if (previewScene.IsValid() && previewScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(previewScene, true);
                }
            }
        }
#endif

        public void Start()
        {
            //Debug.Log("MultiPreview.Start");
            gameObject.hideFlags = HideFlags.DontSaveInBuild;
            gameObject.SetActive(!Application.isPlaying);

            // Never keep the preview scene around when entering play mode.
            // if (Application.isPlaying)
            // {
            //     var existing = SceneManager.GetSceneByPath(PreviewScenePath);
            //     if (existing.IsValid() && existing.isLoaded)
            //     {
            //         // disable all root objects of the existing scene
            //         foreach (var rootObject in existing.GetRootGameObjects())
            //         {
            //             rootObject.SetActive(false);
            //         }
            //         //     EditorSceneManager.CloseScene(existing, true);
            //     }



            //     return;
            // }
            // else
            // {

            if (!Application.isPlaying)
            {
                previewScene = EnsurePreviewSceneLoaded();
            }

            material ??= new Material(Shader.Find("Custom/PointShader"));
        }

        private void OnValidate()
        {
            PointBudget = Mathf.Max(1, PointBudget);
            MaxPointsPerMesh = Mathf.Max(1, MaxPointsPerMesh);
            MinPreviewDepth = Mathf.Max(0, MinPreviewDepth);
            if (MaxPreviewDepth >= 0 && MaxPreviewDepth < MinPreviewDepth)
            {
                MaxPreviewDepth = MinPreviewDepth;
            }
        }

        public void OnDestroy()
        {
            //Debug.Log("MultiPreview.OnDestroy");
            //unload preview scene
            if (previewScene.IsValid() && previewScene.isLoaded)
            {
                EditorSceneManager.CloseScene(previewScene, true);
            }
        }


        public void UpdatePreview()
        {
            Debug.Log("MultiPreview.UpdatePreview invoked");
            if (SetToPreview == null)
            {
                Debug.Log("No PointCloudSet given. Preview aborted.");
                return;
            }

            //Delete Preview of old set
            KillPreview();


            _previewRoot = new GameObject("Preview_Root");
            _previewRoot.transform.rotation = transform.rotation;
            _previewRoot.transform.position = transform.position;
            Debug.Log($"Preview root created at {transform.position}");

            //Copy current values to make sure they are consistent
            _setToPreview = SetToPreview;
            _showPoints = ShowPoints;
            _setTransform = _setToPreview.transform;
            _pointBudget = PointBudget;
            Debug.Log($"Preview settings -> showPoints:{_showPoints} budget:{_pointBudget} minDepth:{MinPreviewDepth} maxDepth:{MaxPreviewDepth}");


            //Look for loaders for the given set
            PointCloudLoader[] allLoaders = FindObjectsByType<PointCloudLoader>(FindObjectsSortMode.None);
            _loaders = new List<PointCloudLoader>();
            _nodes = new List<Node>();
            _nodeLoaders = new List<PointCloudLoader>();
            for (int i = 0; i < allLoaders.Length; ++i)
            {
                if (allLoaders[i].enabled && allLoaders[i].setController == _setToPreview)
                {
                    _loaders.Add(allLoaders[i]);
                }
            }
            Debug.Log($"Found {_loaders.Count} point cloud loaders for set {_setToPreview.name}");
            loadingThread = new Thread(LoadBoundingBoxes);
            loadingThread.Start();
        }

        //This loads bounding boxes and also point cloud meta data (if showpoints is enabled).
        //The meshes itself have to be created on the MainThread, so if it's necessary,
        //this function only sets the flag _createMesh, which will be used later
        private void LoadBoundingBoxes()
        {
            Debug.Log("Preview: LoadBoundingBoxes started");
            BoundingBox overallBoundingBox = new BoundingBox(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity,
                                                                    double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
            List<Node> collectedNodes = new List<Node>();
            List<PointCloudLoader> collectedLoaders = new List<PointCloudLoader>();
            foreach (PointCloudLoader loader in _loaders)
            {
                Debug.Log($"Preview: processing loader {loader.cloudPath}");
                string path = loader.cloudPath;
                if (!path.EndsWith("/"))
                {
                    path += "/";
                }
                PointCloudMetaData metaData = CloudLoader.LoadMetaData(path, false);
                Debug.Log($"Preview: loaded metadata for {path} (version {metaData.version})");
                BoundingBox currentBoundingBox = metaData.tightBoundingBox_transformed;
                overallBoundingBox.Lx = Math.Min(overallBoundingBox.Lx, currentBoundingBox.Lx);
                overallBoundingBox.Ly = Math.Min(overallBoundingBox.Ly, currentBoundingBox.Ly);
                overallBoundingBox.Lz = Math.Min(overallBoundingBox.Lz, currentBoundingBox.Lz);
                overallBoundingBox.Ux = Math.Max(overallBoundingBox.Ux, currentBoundingBox.Ux);
                overallBoundingBox.Uy = Math.Max(overallBoundingBox.Uy, currentBoundingBox.Uy);
                overallBoundingBox.Uz = Math.Max(overallBoundingBox.Uz, currentBoundingBox.Uz);

                if (_showPoints)
                {
                    // Load hierarchy and points breadth-first until we have a generous pool to sample from.
                    // This avoids the v2 root-with-no-points issue and collects deeper nodes for better sampling.
                    Node rootNode = CloudLoader.LoadPointCloud(metaData, false);
                    Queue<Node> q = new Queue<Node>();
                    q.Enqueue(rootNode);

                    long loadedPoints = 0;
                    long loadTarget = Math.Max((long)_pointBudget * 2L, 200000L); // ensure we gather more than the budget for sampling
                    int depthLimit = MaxPreviewDepth < 0 ? int.MaxValue : MaxPreviewDepth;

                    while (q.Count > 0 && loadedPoints < loadTarget)
                    {
                        Node n = q.Dequeue();
                        int depth = n.Name != null ? n.Name.Length : n.GetLevel();
                        if (depth < MinPreviewDepth)
                        {
                            // Still need to drill down to reach min depth
                            for (int i = 0; i < 8; i++)
                            {
                                if (n.HasChild(i)) q.Enqueue(n.GetChild(i));
                            }
                            continue;
                        }

                        if (depth <= depthLimit)
                        {
                            try
                            {
                                CloudLoader.LoadPointsForNode(n);
                                if (n.HasPointsToRender() && n.PointCount > 0)
                                {
                                    collectedNodes.Add(n);
                                    collectedLoaders.Add(loader);
                                    loadedPoints += n.PointCount;
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning("Preview: failed to load node points: " + ex.Message);
                            }
                        }

                        // enqueue children if within depth limit
                        if (depth < depthLimit)
                        {
                            for (int i = 0; i < 8; i++)
                            {
                                if (n.HasChild(i))
                                {
                                    q.Enqueue(n.GetChild(i));
                                }
                            }
                        }
                    }

                    // Fallback: if nothing loaded, add the root to avoid empty preview
                    if (collectedNodes.Count == 0)
                    {
                        CloudLoader.LoadPointsForNode(rootNode);
                        if (rootNode.HasPointsToRender() && rootNode.PointCount > 0)
                        {
                            collectedNodes.Add(rootNode);
                            collectedLoaders.Add(loader);
                        }
                    }

                    Debug.Log($"Preview: collected {collectedNodes.Count} nodes totalling ~{loadedPoints} points (target {loadTarget})");
                }
            }
            if (_setToPreview.moveCenterToTransformPosition)
            {
                Debug.Log("Preview: moving center to transform position");
                Vector3d moving = -overallBoundingBox.Center();
                overallBoundingBox.MoveAlong(moving);
                foreach (Node n in collectedNodes)
                {
                    n.BoundingBox.MoveAlong(moving);
                }
            }
            _currentBB = overallBoundingBox;
            Debug.Log("Preview: bounding boxes loaded");
            if (_showPoints)
            {
                _nodes = collectedNodes;
                _nodeLoaders = collectedLoaders;
                _createMesh = _nodes.Count > 0;
                Debug.Log($"Preview: prepared {_nodes.Count} nodes for mesh creation");
            }
            else
            {
                _loaders = null;
                _nodes = null;
                _nodeLoaders = null;
            }
        }

        public void OnDrawGizmos()
        {
            if (_createMesh)
            {
                Debug.Log("Preview: OnDrawGizmos creating meshes for nodes");
                CreateMesh();
                _createMesh = false;
                _loaders = null;
                _nodes = null;
                _nodeLoaders = null;
            }
            DrawBoundingBox();
        }

        public void DrawBoundingBox()
        {
            if (_currentBB != null)
            {
                Utility.BBDraw.DrawBoundingBoxInEditor(_currentBB, _setTransform);
            }
        }

        //Creates a mesh on each point cloud loader!
        private void CreateMesh()
        {
            Debug.Log("Preview: ChoosePoints start");
            List<Tuple<PointCloudLoader, Vector3[], Color[]>> data = ChoosePoints();
            Debug.Log($"Preview: ChoosePoints returned {data.Count} entries");

            Debug.Log("Preview: Creating preview meshes for " + data.Count + " point clouds.");
            foreach (Tuple<PointCloudLoader, Vector3[], Color[]> cloud in data)
            {
                Vector3[] vertexData = cloud.Item2;
                Color[] colorData = cloud.Item3;
                string cloudPath = cloud.Item1 != null ? cloud.Item1.cloudPath : "unknown";

                int maxPointsPerMesh = Mathf.Max(1, MaxPointsPerMesh);
                //Debug.Log ("Preview: Creating mesh for cloud " + cloudPath + " with " + vertexData.Length + " points (chunk size " + maxPointsPerMesh + ").");
                for (int i = 0; i < vertexData.Length; i += maxPointsPerMesh)
                {
                    int count = Math.Min(maxPointsPerMesh, vertexData.Length - i);
                    CreateMeshPart(cloud, vertexData, colorData, i, count);
                }
            }

            Debug.Log("Preview: Created preview with total " + data.Count + " point clouds.");
            SceneManager.MoveGameObjectToScene(_previewRoot, previewScene);
            EditorSceneManager.SaveScene(previewScene, PreviewScenePath);
        }

        private void CreateMeshPart(Tuple<PointCloudLoader, Vector3[], Color[]> cloud, Vector3[] vertexData, Color[] colorData, int startIndex, int count)
        {
            string cloudPath = cloud.Item1 != null ? cloud.Item1.cloudPath : "unknown";
            //Debug.Log("Preview: Creating mesh part from " + startIndex + " with " + count + " points for cloud " + cloudPath);
            GameObject go = new GameObject("Preview (part " + startIndex + ") " + cloudPath);
            MeshFilter filter = go.GetComponent<MeshFilter>();
            Mesh mesh;
            if (filter == null)
            {
                filter = go.AddComponent<MeshFilter>();
                mesh = new Mesh();
                filter.mesh = mesh;
            }
            else
            {
                mesh = filter.sharedMesh;
                if (mesh == null)
                {
                    mesh = new Mesh();
                    filter.mesh = mesh;
                }
            }
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = go.AddComponent<MeshRenderer>();
            }
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.material = material;

            if (count == 0)
            {
                filter.mesh = null;
            }
            else
            {
                Vector3[] verticesSlice = new Vector3[count];
                Color[] colorsSlice = new Color[count];
                Array.Copy(vertexData, startIndex, verticesSlice, 0, count);
                Array.Copy(colorData, startIndex, colorsSlice, 0, count);

                int[] indecies = new int[count];
                for (int i = 0; i < count; ++i)
                {
                    indecies[i] = i;
                }
                mesh.indexFormat = count > 65534 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.Clear();
                mesh.vertices = verticesSlice;
                mesh.colors = colorsSlice;
                mesh.SetIndices(indecies, MeshTopology.Points, 0);
            }
            go.AddComponent<PreviewObject>();

            go.transform.localPosition = new Vector3(0, 0, 0);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(1, 1, 1);
            go.transform.SetParent(_previewRoot.transform, false);
        }

        //Samples the point clouds, so to choose the points equally from all the clouds.
        private List<Tuple<PointCloudLoader, Vector3[], Color[]>> ChoosePoints()
        {
            List<Tuple<PointCloudLoader, Vector3[], Color[]>> result = new List<Tuple<PointCloudLoader, Vector3[], Color[]>>();
            Debug.Log($"Preview: selecting points across {_nodes?.Count ?? 0} nodes with budget {_pointBudget}");
            int sumpoints = 0;  //Sum of points in all nodes
            int[] assignedPointCounts = new int[_nodes.Count];   //Assigned Count for each node (Assigned = will be displayed)
            int[] remainingPointCounts = new int[_nodes.Count];  //Not-yet-Assigned Count for each node
            int minPC = int.MaxValue;  //Smallest point count of a node
            int j = 0;
            //Initialize sumpoints, remainingPointCounts and minPC
            foreach (Node n in _nodes)
            {
                // Ensure points are loaded for this node on demand
                if (!n.HasPointsToRender())
                {
                    CloudLoader.LoadPointsForNode(n);
                }
                sumpoints += n.PointCount;
                remainingPointCounts[j] = n.PointCount;
                minPC = Math.Min(minPC, remainingPointCounts[j]);
                ++j;
            }
            if (_nodes.Count == 0 || minPC == int.MaxValue)
            {
                Debug.Log("Preview: no nodes found or invalid minPC");
                return result;
            }
            int remainingNodeCount = _nodes.Count;   //The count of nodes that still have unassigned points
            int currentPointCount = 0; //The number of points that are assigned
            int finalsumpoints = Math.Min(sumpoints, _pointBudget); //Number of points we'll display eventually
            Debug.Log($"Preview: total points {sumpoints}, final sum {finalsumpoints}");
            //As long as we still need to assign more points
            while (currentPointCount < finalsumpoints)
            {
                //Find a value that we can reduce from each remainingPointCount without exceeding the limit
                //The smallest value of: Smallest remaining point count, remaining point count to fill up divided by the number of remaining nodes
                int reduce = Math.Min(minPC, (finalsumpoints - currentPointCount) / remainingNodeCount);
                if (reduce == 0) reduce = 1;
                //Reduce each remainingPointCount by this value
                for (j = 0; j < remainingPointCounts.Length && currentPointCount < finalsumpoints; ++j)
                {
                    //if it's still remaining
                    if (remainingPointCounts[j] != 0)
                    {
                        remainingPointCounts[j] -= reduce;
                        assignedPointCounts[j] += reduce;
                        currentPointCount += reduce;
                        if (remainingPointCounts[j] == 0)
                        {
                            --remainingNodeCount;
                        }
                        else
                        {
                            minPC = Math.Min(minPC, remainingPointCounts[j]);
                        }
                    }
                }
            }
            //Build Vertices-Array
            j = 0;
            foreach (Node n in _nodes)
            {
                //Debug.Log($"Preview: node {j} has {n.PointCount} points, assigned {assignedPointCounts[j]}");
                Vector3[] nodeVertices = n.VerticesToStore;
                Color[] nodeColors = n.ColorsToStore;
                int assignedCount = assignedPointCounts[j];
                if (assignedCount == 0)
                {
                    ++j;
                    continue;
                }

                Vector3[] filteredVertices = new Vector3[assignedCount];
                Color[] filteredColors = new Color[assignedCount];
                int stride = Math.Max(1, n.PointCount / assignedCount);
                Vector3 translation = n.MetaData.version == "2.0"
                    ? n.MetaData.getAdditionalTranslation().ToFloatVector()
                    : n.BoundingBox.Min().ToFloatVector();
                for (int newIndex = 0, oldIndex = 0; newIndex < assignedCount && oldIndex < n.PointCount; oldIndex += stride, ++newIndex)
                {
                    filteredVertices[newIndex] = nodeVertices[oldIndex] + translation;
                    filteredColors[newIndex] = nodeColors[oldIndex];
                }
                PointCloudLoader loader = j < _nodeLoaders.Count ? _nodeLoaders[j] : null;
                result.Add(new Tuple<PointCloudLoader, Vector3[], Color[]>(loader, filteredVertices, filteredColors));
                ++j;
            }
            //Debug.Log($"Preview: selected total {result.Count} node slices");
            return result;
        }

        private void CollectNodesWithPoints(Node node, List<Node> list, List<PointCloudLoader> loaderList, PointCloudLoader loader)
        {
            if (node == null)
            {
                return;
            }

            if (node.PointCount > 0 && node.VerticesToStore != null && node.ColorsToStore != null)
            {
                Debug.Log($"Preview: collect node {node.Name} with {node.PointCount} points");
                list.Add(node);
                loaderList.Add(loader);
            }

            for (int i = 0; i < 8; i++)
            {
                if (node.HasChild(i))
                {
                    CollectNodesWithPoints(node.GetChild(i), list, loaderList, loader);
                }
            }
        }

        private void CollectAllNodes(Node node, List<Node> list)
        {
            CollectAllNodes(node, list, 0, -1);
        }

        private void CollectAllNodes(Node node, List<Node> list, int minDepth, int maxDepth)
        {
            if (node == null)
            {
                return;
            }

            int depth = node.Name != null ? node.Name.Length : node.GetLevel();
            bool depthAllowed = (depth >= minDepth) && (maxDepth < 0 || depth <= maxDepth);
            if (depthAllowed)
            {
                list.Add(node);
            }

            bool canRecurse = (maxDepth < 0 || depth < maxDepth);
            if (!canRecurse) return;

            for (int i = 0; i < 8; i++)
            {
                if (node.HasChild(i))
                {
                    CollectAllNodes(node.GetChild(i), list, minDepth, maxDepth);
                }
            }
        }

        public void KillPreview()
        {
            Debug.Log("Kil Preview");
            if (_loaders != null && _loaders.Count != 0)
            {
                //Stop the process
                Debug.Log("Preview is already running.Killing the process");
                loadingThread?.Abort();
            }

            if (previewScene != null)
            {
                GameObject[] rootObjects = previewScene.GetRootGameObjects();

                for (int i = 0; i < rootObjects.Length; i++)
                {
                    DestroyImmediate(rootObjects[i]);
                }
                EditorSceneManager.SaveScene(previewScene, PreviewScenePath);
            }

            //PreviewObject[] previewChildren = GetComponentsInChildren<PreviewObject>(true);
            //for (int i = 0; i < previewChildren.Length; ++i)
            //{
            //    DestroyImmediate(previewChildren[i].gameObject);
            //}

            //// Backward compatibility: also remove previews parented directly to the point cloud set transform
            //if (_setTransform != null)
            //{
            //    List<GameObject> toRemove = new List<GameObject>();
            //    for (int i = 0; i < _setTransform.childCount; i++)
            //    {
            //        Transform child = _setTransform.GetChild(i);
            //        if (child.GetComponent<PreviewObject>() != null)
            //        {
            //            toRemove.Add(child.gameObject);
            //        }
            //    }
            //    for (int i = 0; i < toRemove.Count; i++)
            //    {
            //        DestroyImmediate(toRemove[i]);
            //    }
            //}

            _currentBB = null;
            _nodes = null;
            _nodeLoaders = null;
            _loaders = null;

        }

        private Scene EnsurePreviewSceneLoaded()
        {
            if (Application.isPlaying)
            {
                return default;
            }

            Scene scene = SceneManager.GetSceneByPath(PreviewScenePath);

            // Case 1: Scene is already loaded in hierarchy
            if (scene.IsValid() && scene.isLoaded) return scene;

            // Case 2: Scene file exists on disk, but not loaded -> Open it Additively
            if (System.IO.File.Exists(PreviewScenePath))
            {
                return EditorSceneManager.OpenScene(PreviewScenePath, OpenSceneMode.Additive);
            }

            // Case 3: File doesn't exist -> Create new Scene
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(scene, PreviewScenePath);
            return scene;
        }

        public Transform getPreviewRoot()
        {
            if (Application.isPlaying)
            {
                return null;
            }

            previewScene = EnsurePreviewSceneLoaded();
            GameObject[] previewRoots = previewScene.GetRootGameObjects();
            foreach (var go in previewRoots)
            {
                if (go.name == "Preview_Root") return go.transform;
            }

            return null;
        }
#endif 
    }
}
