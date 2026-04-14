using BAPointCloudRenderer.CloudController;
using BAPointCloudRenderer.CloudData;
using BAPointCloudRenderer.ObjectCreation;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace BAPointCloudRenderer.Loading {
    /// <summary>
    /// The multithreaded Real-Time-Renderer as described in the Bachelor Thesis in chapter 3.2.2 - 3.2.7
    /// </summary>
    class V2Renderer : AbstractRenderer {
        private const string LogPrefix = "[PointCloudRenderer]";

        private AbstractPointCloudSet pcset;

        private bool paused = false;  //true, iff everything should be stopped (the point loading will stop and every method will not do anything anymore)

        private V2TraversalThread traversalThread;
        private V2LoadingThread loadingThread;
        private V2Cache cache;
        private List<Node> rootNodes;   //List of root nodes of the point clouds
        private Thread unityThread = null;

        private MeshConfiguration config;
        private uint renderingpointcount;
        private readonly uint pointBudget;

        //Camera Info
        private Camera camera;
        private bool render360;

        private object locker = new object();
        private Queue<Node> toRender;
        private Queue<Node> toDelete;
        private Queue<Node> toDeleteExternal; //Nodes that have been scheduled for removal via removeRoot
        private float nextDiagnosticsLogTime;

        /// <summary>
        /// Creates a new V2Renderer and starts all the threads
        /// </summary>
        /// <param name="minNodeSize">Minimum Node Size</param>
        /// <param name="pointBudget">Point Budget</param>
        /// <param name="nodesLoadedPerFrame">Maximum number of nodes loaded per frame</param>
        /// <param name="nodesGOsperFrame">Maximum number of nodes for which GameObjects should be created per frame</param>
        /// <param name="camera">User Camera</param>
        /// <param name="config">MeshConfiguration, defining how the points should be rendered</param>
        /// <param name="cacheSize">Size of cache in points</param>
        /// <param name="render360">If true, loading is based on camera position and far clip plane in all directions</param>
        public V2Renderer(AbstractPointCloudSet pcset, int minNodeSize, uint pointBudget, uint nodesLoadedPerFrame, uint nodesGOsperFrame, Camera camera, MeshConfiguration config, uint cacheSize, bool render360) {
            this.pcset = pcset;
            rootNodes = new List<Node>();
            this.camera = camera;
            this.config = config;
            this.render360 = render360;
            this.pointBudget = pointBudget;
            cache = new V2Cache(cacheSize);
            loadingThread = new V2LoadingThread(cache);
            loadingThread.Start();
            traversalThread = new V2TraversalThread(pcset.gameObject, this, loadingThread, rootNodes, minNodeSize, pointBudget, nodesLoadedPerFrame, nodesGOsperFrame, cache, render360);
            traversalThread.Start();
            toDeleteExternal = new Queue<Node>();
            nextDiagnosticsLogTime = 0f;
            Debug.Log($"{LogPrefix} Started renderer for '{pcset.name}' with pointBudget={pointBudget}, cacheSize={cacheSize}, nodesLoadedPerFrame={nodesLoadedPerFrame}, nodesGOsPerFrame={nodesGOsperFrame}, render360={render360}.");
        }

        /// <summary>
        /// Registers the root node of a point cloud in the renderer.
        /// </summary>
        /// <param name="rootNode">not null</param>
        public void AddRootNode(Node rootNode, PointCloudLoader loader) {
            rootNodes.Add(rootNode);
            Debug.Log($"{LogPrefix} Added root node '{rootNode}' for loader '{loader.name}'. Root count is now {rootNodes.Count}.");
        }

        /// <summary>
        /// Removes the root node of a point cloud from the renderer. The node will not be rendered any more.
        /// This has to be called from the main thread!
        /// </summary>
        /// <param name="rootNode">not null</param>
        public void RemoveRootNode(Node rootNode, PointCloudLoader loader) {
            lock (toDeleteExternal) {
                toDeleteExternal.Enqueue(rootNode);
            }
            Debug.Log($"{LogPrefix} Scheduled root node '{rootNode}' for removal from loader '{loader.name}'. Pending root removals: {toDeleteExternal.Count}.");
        }

        /// <summary>
        /// Returns how many root nodes have been added
        /// </summary>
        public int GetRootNodeCount() {
            lock (toDeleteExternal) {
                return rootNodes.Count - toDeleteExternal.Count;
            }
        }

        /// <summary>
        /// True, if ShutDown() has not been called yet
        /// </summary>
        public bool IsRunning() {
            return !paused;
        }

        /// <summary>
        /// Gives the current camera data to the traversal thread and updates the GameObjects. Called from the MainThread. As described in the Bachelor Thesis in chapter 3.1.3 "Main Thread"
        /// </summary>
        public void Update() {
            unityThread = Thread.CurrentThread;
            if (paused) return;
            //Set new Camera Data
            traversalThread.SetNextCameraData(camera.transform.position, camera.transform.forward, GeometryUtility.CalculateFrustumPlanes(camera.projectionMatrix * camera.worldToCameraMatrix * pcset.transform.localToWorldMatrix), camera.pixelRect.height, camera.fieldOfView, camera.farClipPlane);
            
            //Update GameObjects
            Queue<Node> toRender;
            Queue<Node> toDelete;
            lock (locker) {
                toRender = this.toRender;
                toDelete = this.toDelete;
                this.toRender = null;
                this.toDelete = null;
            }
            if (toRender == null) {
                return;
            }
            while (toDelete.Count != 0) {
                Node n = toDelete.Dequeue();
                lock (n) {
                    if (n.HasGameObjects()) {
                        n.RemoveGameObjects(config);
                        cache.Insert(n);
                    }
                }
            }
            while (toRender.Count != 0) {
                Node n = toRender.Dequeue();
                lock (n) {
                    if (n.HasPointsToRender() && (n.MetaData.version == "2.0" || n.Parent == null || n.Parent.HasGameObjects())) {
                        n.CreateGameObjects(config, pcset.transform);
                    }
                }
            }
            Monitor.Enter(toDeleteExternal);
            while (toDeleteExternal.Count != 0) {
                Node rootNode = toDeleteExternal.Dequeue();
                rootNodes.Remove(rootNode);
                Queue<Node> toRemove = new Queue<Node>();
                toRemove.Enqueue(rootNode);
                while (toRemove.Count != 0) {
                    Node n = toRemove.Dequeue();
                    cache.Withdraw(n);
                    if (n.HasGameObjects()) {
                        n.RemoveGameObjects(config);
                    }
                    if (n.HasPointsToRender()) {
                        n.ForgetPoints();
                        foreach (Node child in n) {
                            toRemove.Enqueue(child);
                        }
                    }
                }
            }
            Monitor.Exit(toDeleteExternal);
            
            //Notify Traversal Thread
            lock (traversalThread) {
                Monitor.PulseAll(traversalThread);
            }
        }

        /// <summary>
        /// Stops the rendering process and all threads
        /// Must be called from the main thread!
        /// </summary>
        public void ShutDown() {
            if (unityThread != null && Thread.CurrentThread != unityThread) {
                throw new System.Exception("ShutDown() has to be called from the Unity Main Thread!");
            }
            Debug.Log($"{LogPrefix} Shutdown requested for '{pcset.name}'. {BuildDiagnosticsSnapshot()}");
            Pause();
            foreach (Node node in rootNodes) {
                node.RemoveAllGameObjects(config);
            }
            Debug.Log($"{LogPrefix} Shutdown completed for '{pcset.name}'.");
        }

        /// <summary>
        /// Pauses the updating of the rendering.
        /// </summary>
        public void Pause() {
            paused = true;
            traversalThread.Stop();
            lock (traversalThread) {
                Monitor.PulseAll(traversalThread);
            }
            traversalThread.StopAndWait();
            loadingThread.Stop();
        }

        /// <summary>
        /// Continues the rendering after pausing
        /// </summary>
        public void Continue() {
            loadingThread.Start();
            traversalThread.Start();
            paused = false;
        }

        /// <summary>
        /// Pauses the rendering and hides all visible point clouds.
        /// </summary>
        public void Hide() {
            Pause();
            foreach (Node node in rootNodes) {
                node.DeactivateAllGameObjects();
            }
        }

        /// <summary>
        /// Continues the rendering and displays all visible point clouds after them being hidden via hide.
        /// </summary>
        public void Display() {
            foreach (Node node in rootNodes) {
                node.ReactivateAllGameObjects();
            }
            Continue();
        }

        /// <summary>
        /// Returns the current PointCount, so how many points are loaded / visible
        /// </summary>
        public uint GetPointCount() {
            return renderingpointcount;
        }

        /// <summary>
        /// Sets the new GO-update-queues. Called from the TraversalThread.
        /// </summary>
        public void SetQueues(Queue<Node> toRender, Queue<Node> toDelete, uint pointcount) {
            lock (locker) {
                this.toRender = toRender;
                this.toDelete = toDelete;
                this.renderingpointcount = pointcount;
            }
        }

        public void LogPeriodicSnapshot(float intervalSeconds) {
            float now = Time.unscaledTime;
            if (now < nextDiagnosticsLogTime) {
                return;
            }

            nextDiagnosticsLogTime = now + Mathf.Max(1f, intervalSeconds);
            Debug.Log(BuildDiagnosticsSnapshot());
        }

        public string BuildDiagnosticsSnapshot() {
            int queuedRenderCount;
            int queuedDeleteCount;
            lock (locker) {
                queuedRenderCount = toRender != null ? toRender.Count : 0;
                queuedDeleteCount = toDelete != null ? toDelete.Count : 0;
            }

            int pendingRootDeletes;
            lock (toDeleteExternal) {
                pendingRootDeletes = toDeleteExternal.Count;
            }

            string loadingFailure = loadingThread.LastFailure();
            string traversalFailure = traversalThread.LastFailure();

            string cameraName = camera != null ? camera.name : "null";
            float cameraFarClip = camera != null ? camera.farClipPlane : 0f;
            float cameraFieldOfView = camera != null ? camera.fieldOfView : 0f;
            float cameraPixelHeight = camera != null ? camera.pixelRect.height : 0f;
            string cameraTargetTexture = "none";
            if (camera != null && camera.targetTexture != null) {
                cameraTargetTexture = camera.targetTexture.width + "x" + camera.targetTexture.height;
            }

            return $"{LogPrefix} Snapshot set='{pcset.name}' camera='{cameraName}' camFar={cameraFarClip:F2} camFov={cameraFieldOfView:F2} camPixelHeight={cameraPixelHeight:F0} camTarget={cameraTargetTexture} renderedPoints={renderingpointcount}/{pointBudget} rootNodes={rootNodes.Count} visibleNodes={traversalThread.VisibleNodeCount()} queuedRender={queuedRenderCount} queuedDelete={queuedDeleteCount} pendingRootDeletes={pendingRootDeletes} cachePoints={cache.PointCount()}/{cache.MaxPointCount()} cacheNodes={cache.NodeCount()} cacheEvictions={cache.EvictionCount()} cacheDirectDrops={cache.DirectDropCount()} loadingQueue={loadingThread.PendingQueueCount()} loadingScheduled={loadingThread.ScheduledNodeCount()} loadingCompleted={loadingThread.CompletedLoadCount()} loadingFailures={loadingThread.FailedLoadCount()} traversalIterations={traversalThread.TraversalIterationCount()} traversalFailures={traversalThread.TraversalFailureCount()} blocksLive={PointCloudBlock.LiveBlockCount} blocksPendingKill={PointCloudBlock.PendingKillCount} loaderThreadAlive={loadingThread.IsAlive()} traversalThreadAlive={traversalThread.IsAlive()} lastLoadingFailure='{loadingFailure ?? ""}' lastTraversalFailure='{traversalFailure ?? ""}'";
        }
    }
}
