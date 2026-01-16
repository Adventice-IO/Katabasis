using System;
using System.Threading;
using UnityEngine;
using UnityEditor;

using BAPointCloudRenderer.CloudData;
using BAPointCloudRenderer.Loading;
using System.Collections.Generic;
using BAPointCloudRenderer.Controllers;

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
        private BoundingBox _currentBB = null;
        private Transform _setTransform;
        private AbstractPointCloudSet _setToPreview;
        private bool _showPoints;
        private int _pointBudget;
        public Material material;
        private bool _createMesh = false;
        private Thread loadingThread = null;
        private const int MaxVerticesPerMesh = 65000; // Unity 16-bit index limit safe cap
        private Dictionary<PointCloudLoader, PointCloudMetaData> _loaderMeta = null; // meta per loader
        private Vector3 _centerOffset = Vector3.zero; // applied to all preview GOs

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

        public void Start()
        {
            gameObject.SetActive(!Application.isPlaying);
        }

        public void UpdatePreview()
        {
            if (SetToPreview == null)
            {
                Debug.Log("No PointCloudSet given. Preview aborted.");
                return;
            }
            if (_loaders != null && _loaders.Count != 0)
            {
                //Debug.Log("Another updating process seems to be in progress. Please wait, recreate this object or restart.");
                //return;
                //clear loaders
                _loaders = null;
                _createMesh = false;
            }
            //Delete Preview of old set
            KillPreview();
            //Copy current values to make sure they are consistent
            _setToPreview = SetToPreview;
            _showPoints = ShowPoints;
            _setTransform = _setToPreview.transform;
            _pointBudget = PointBudget;

            //Look for loaders for the given set
            PointCloudLoader[] allLoaders = FindObjectsOfType<PointCloudLoader>();
            _loaders = new List<PointCloudLoader>();
            _nodes = new List<Node>();
            _loaderMeta = new Dictionary<PointCloudLoader, PointCloudMetaData>();
            for (int i = 0; i < allLoaders.Length; ++i)
            {
                if (allLoaders[i].enabled && allLoaders[i].setController == _setToPreview)
                {
                    _loaders.Add(allLoaders[i]);
                }
            }
            loadingThread = new Thread(LoadBoundingBoxes);
            loadingThread.Start();
        }


        public void KillPreview()
        {
            PreviewObject[] previewChildren = GetComponentsInChildren<PreviewObject>(true);
            for (int i = 0; i < previewChildren.Length; ++i)
            {
                DestroyImmediate(previewChildren[i].gameObject);
            }

            // Backward compatibility: also remove previews parented directly to the point cloud set transform
            if (_setTransform != null)
            {
                List<GameObject> toRemove = new List<GameObject>();
                for (int i = 0; i < _setTransform.childCount; i++)
                {
                    Transform child = _setTransform.GetChild(i);
                    if (child.GetComponent<PreviewObject>() != null)
                    {
                        toRemove.Add(child.gameObject);
                    }
                }
                for (int i = 0; i < toRemove.Count; i++)
                {
                    DestroyImmediate(toRemove[i]);
                }
            }

            _currentBB = null;

        }

        //This loads bounding boxes and also point cloud meta data (if showpoints is enabled).
        //The meshes itself have to be created on the MainThread, so if it's necessary,
        //this function only sets the flag _createMesh, which will be used later
        private void LoadBoundingBoxes()
        {
            BoundingBox overallBoundingBox = new BoundingBox(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity,
                                                                    double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);

            // Collect roots and compute overall BB
            List<Node> roots = new List<Node>();
            foreach (PointCloudLoader loader in _loaders)
            {
                string path = loader.cloudPath;
                if (!path.EndsWith("/"))
                {
                    path += "/";
                }
                PointCloudMetaData metaData = CloudLoader.LoadMetaData(path, false);
                lock (this)
                {
                    _loaderMeta[loader] = metaData;
                }
                BoundingBox currentBoundingBox = metaData.tightBoundingBox_transformed;
                overallBoundingBox.Lx = Math.Min(overallBoundingBox.Lx, currentBoundingBox.Lx);
                overallBoundingBox.Ly = Math.Min(overallBoundingBox.Ly, currentBoundingBox.Ly);
                overallBoundingBox.Lz = Math.Min(overallBoundingBox.Lz, currentBoundingBox.Lz);
                overallBoundingBox.Ux = Math.Max(overallBoundingBox.Ux, currentBoundingBox.Ux);
                overallBoundingBox.Uy = Math.Max(overallBoundingBox.Uy, currentBoundingBox.Uy);
                overallBoundingBox.Uz = Math.Max(overallBoundingBox.Uz, currentBoundingBox.Uz);

                if (_showPoints)
                {
                    // Build full hierarchy but don't load all points at once
                    Node rootNode = CloudLoader.LoadHierarchyOnly(metaData);
                    roots.Add(rootNode);
                }
            }

            // BFS across nodes until we fill the budget
            if (_showPoints)
            {
                _nodes = new List<Node>();
                Queue<Node> queue = new Queue<Node>();
                foreach (Node root in roots)
                {
                    queue.Enqueue(root);
                }

                long loaded = 0;
                while (queue.Count > 0 && loaded < _pointBudget)
                {
                    Node n = queue.Dequeue();
                    try
                    {
                        CloudLoader.LoadPointsForNode(n);
                        if (n.HasPointsToRender() && n.PointCount > 0)
                        {
                            _nodes.Add(n);
                            loaded += n.PointCount;
                        }
                        // Enqueue children for finer detail
                        for (int i = 0; i < 8; i++)
                        {
                            if (n.HasChild(i))
                            {
                                queue.Enqueue(n.GetChild(i));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("Preview: failed to load node points: " + ex.Message);
                    }
                }
            }

            if (_setToPreview.moveCenterToTransformPosition)
            {
                Vector3d moving = -overallBoundingBox.Center();
                overallBoundingBox.MoveAlong(moving);
                _centerOffset = moving.ToFloatVector();
                if (_showPoints && _nodes != null)
                {
                    foreach (Node n in _nodes)
                    {
                        n.BoundingBox.MoveAlong(moving);
                    }
                }
            }
            _currentBB = overallBoundingBox;
            if (_showPoints)
            {
                _createMesh = true;
            }
            else
            {
                _loaders = null;
                _nodes = null;
            }
        }

        public void OnDrawGizmos()
        {
            if (_createMesh)
            {
                //If mesh has to be created, do it now!
                CreateMesh();
                _createMesh = false;
                _loaders = null;
                _nodes = null;
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
            List<Tuple<PointCloudLoader, Vector3[], Color[]>> data = ChoosePoints();

            foreach (Tuple<PointCloudLoader, Vector3[], Color[]> cloud in data)
            {
                Vector3[] vertexData = cloud.Item2;
                Color[] colorData = cloud.Item3;
                if (vertexData.Length == 0)
                {
                    continue;
                }

                int createdParts = 0;
                for (int start = 0; start < vertexData.Length; start += MaxVerticesPerMesh)
                {
                    int count = Math.Min(MaxVerticesPerMesh, vertexData.Length - start);
                    if (count <= 0)
                    {
                        break;
                    }

                    string nameSuffix = vertexData.Length > MaxVerticesPerMesh ? " (part " + (createdParts + 1) + ")" : string.Empty;
                    GameObject go = new GameObject("Preview: " + cloud.Item1.cloudPath + nameSuffix);

                    MeshFilter filter = go.AddComponent<MeshFilter>();
                    Mesh mesh = new Mesh();
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    filter.mesh = mesh;

                    MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.material = material;

                    Vector3[] vChunk = new Vector3[count];
                    Color[] cChunk = new Color[count];
                    Array.Copy(vertexData, start, vChunk, 0, count);
                    Array.Copy(colorData, start, cChunk, 0, count);

                    int[] indices = new int[count];
                    for (int i = 0; i < count; ++i)
                    {
                        indices[i] = i;
                    }

                    mesh.Clear();
                    mesh.vertices = vChunk;
                    mesh.colors = cChunk;
                    mesh.SetIndices(indices, MeshTopology.Points, 0);

                    go.AddComponent<PreviewObject>();

                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = new Vector3(1, 1, 1);
                    go.transform.SetParent(transform, false);
                    go.hideFlags = HideFlags.DontSave;

                    // Apply version-specific translation and center offset
                    Vector3 extra = Vector3.zero;
                    PointCloudLoader ldr = cloud.Item1;
                    if (_loaderMeta != null && _loaderMeta.TryGetValue(ldr, out var md) && md != null)
                    {
                        if (md.version == "2.0")
                        {
                            // potree v2 vertices are absolute; move by additionalTranslation and center offset
                            extra = (md as PointCloudMetaDataV2_0)?.getAdditionalTranslation().ToFloatVector() ?? Vector3.zero;
                        }
                        else
                        {
                            // v1 data expect local Min offset was applied to vertices below; just center offset
                            extra = Vector3.zero;
                        }
                    }
                    go.transform.localPosition += extra + _centerOffset;

                    createdParts++;
                }
            }
        }

        //Samples the point clouds, so to choose the points equally from all the clouds.
        private List<Tuple<PointCloudLoader, Vector3[], Color[]>> ChoosePoints()
        {
            // Sum available points
            long totalAvailable = 0;
            foreach (Node n in _nodes)
            {
                totalAvailable += n.PointCount;
            }

            int target = (int)Math.Min(totalAvailable, Math.Max(0, _pointBudget));
            if (target <= 0)
            {
                return new List<Tuple<PointCloudLoader, Vector3[], Color[]>>();
            }

            // Proportional assignment per node
            int[] assigned = new int[_nodes.Count];
            double[] residuals = new double[_nodes.Count];
            int assignedSum = 0;
            for (int i = 0; i < _nodes.Count; i++)
            {
                Node n = _nodes[i];
                if (n.PointCount <= 0)
                {
                    assigned[i] = 0;
                    residuals[i] = 0;
                    continue;
                }
                double exact = (double)target * (double)n.PointCount / (double)totalAvailable;
                int a = (int)Math.Floor(exact);
                a = Math.Min(a, n.PointCount);
                assigned[i] = a;
                residuals[i] = exact - a;
                assignedSum += a;
            }

            // Distribute remaining points by largest residuals and availability
            int remaining = target - assignedSum;
            while (remaining > 0)
            {
                int bestIndex = -1;
                double bestResidual = -1;
                for (int i = 0; i < _nodes.Count; i++)
                {
                    if (assigned[i] < _nodes[i].PointCount && residuals[i] > bestResidual)
                    {
                        bestResidual = residuals[i];
                        bestIndex = i;
                    }
                }
                if (bestIndex == -1)
                {
                    break; // no capacity left
                }
                assigned[bestIndex]++;
                residuals[bestIndex] = 0; // avoid repeatedly picking same if many remain
                remaining--;
            }

            // Group sampled points by loader
            Dictionary<PointCloudLoader, List<Vector3>> vertsByLoader = new Dictionary<PointCloudLoader, List<Vector3>>();
            Dictionary<PointCloudLoader, List<Color>> colsByLoader = new Dictionary<PointCloudLoader, List<Color>>();

            foreach (var l in _loaders)
            {
                vertsByLoader[l] = new List<Vector3>();
                colsByLoader[l] = new List<Color>();
            }

            for (int i = 0; i < _nodes.Count; i++)
            {
                Node n = _nodes[i];
                int count = assigned[i];
                if (count <= 0 || n.VerticesToStore == null || n.VerticesToStore.Length == 0)
                {
                    continue;
                }

                // Map node to its loader by normalized path
                string nPath = n.MetaData.cloudPath;
                if (!nPath.EndsWith("/")) nPath += "/";
                PointCloudLoader loader = null;
                foreach (var l in _loaders)
                {
                    string lPath = l.cloudPath.EndsWith("/") ? l.cloudPath : l.cloudPath + "/";
                    if (string.Equals(lPath, nPath, StringComparison.OrdinalIgnoreCase))
                    {
                        loader = l;
                        break;
                    }
                }
                if (loader == null)
                {
                    continue; // skip if no matching loader found (shouldn't happen)
                }

                Vector3[] nodeVertices = n.VerticesToStore;
                Color[] nodeColors = n.ColorsToStore;

                // For v1 datasets, vertices are relative to node min; for v2 they are absolute.
                // We already add per-loader extra and center offset on the GameObject, so do NOT apply extra here; only node-local translation for v1.
                Vector3 translation = n.MetaData.version == "2.0" ? Vector3.zero : n.BoundingBox.Min().ToFloatVector();

                double step = (double)n.PointCount / (double)count;
                System.Random rng = new System.Random(n.Name.GetHashCode());
                for (int k = 0; k < count; k++)
                {
                    // jitter inside stride to avoid visible patterns
                    double baseIdx = k * step;
                    double jitter = rng.NextDouble();
                    int src = (int)Math.Floor(Math.Min(baseIdx + jitter, n.PointCount - 1));
                    if (src < 0) src = 0;
                    vertsByLoader[loader].Add(nodeVertices[src] + translation);
                    colsByLoader[loader].Add(nodeColors[src]);
                }
            }

            var result = new List<Tuple<PointCloudLoader, Vector3[], Color[]>>();
            foreach (var l in _loaders)
            {
                result.Add(new Tuple<PointCloudLoader, Vector3[], Color[]>(
                    l,
                    vertsByLoader[l].ToArray(),
                    colsByLoader[l].ToArray()
                ));
            }

            return result;
        }
    }
}
