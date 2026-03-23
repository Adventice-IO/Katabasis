using BAPointCloudRenderer.CloudData;
using BAPointCloudRenderer.DataStructures;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace BAPointCloudRenderer.Loading {
    /// <summary>
    /// The traversal thread of the V2 Rendering System. Checks constantly, which nodes are visible and should be rendered and which not. Described in the Bachelor Thesis in chapter 3.2.4 "Traversal Thread".
    /// This is the place, where most of the magic happens.
    /// </summary>
    class V2TraversalThread {

        private GameObject parent;
        private object locker = new object();
        private List<Node> rootNodes;
        private double minNodeSize; //Min projected node size
        private uint pointBudget;   //Point Budget
        private uint nodesLoadedPerFrame;
        private uint nodesGOsPerFrame;
        private bool render360;
        private bool running = true;

        //Camera Data
        Vector3 cameraPosition;
        float screenHeight;
        float fieldOfView;
        float farClipPlane;
        Plane[] frustum;
        Vector3 camForward;

        private Queue<Node> toDelete;
        private Queue<Node> toRender;
        private HashSet<Node> visibleNodes;

        private V2Renderer mainThread;
        private V2LoadingThread loadingThread;
        private V2Cache cache;

        private Thread thread;

        /// <summary>
        /// Creates the object, but does not start the thread yet
        /// </summary>
        public V2TraversalThread(GameObject parent, V2Renderer mainThread, V2LoadingThread loadingThread, List<Node> rootNodes, double minNodeSize, uint pointBudget, uint nodesLoadedPerFrame, uint nodesGOsPerFrame, V2Cache cache, bool render360) {
            this.parent = parent;
            this.mainThread = mainThread;
            this.loadingThread = loadingThread;
            this.rootNodes = rootNodes;
            this.minNodeSize = minNodeSize;
            this.pointBudget = pointBudget;
            this.render360 = render360;
            visibleNodes = new HashSet<Node>();
            this.cache = cache;
            this.nodesLoadedPerFrame = nodesLoadedPerFrame;
            this.nodesGOsPerFrame = nodesGOsPerFrame;
        }

        /// <summary>
        /// Starts the thread
        /// </summary>
        public void Start() {
            thread = new Thread(Run);
            running = true;
            thread.Start();
        }

        private void Run() {
            try {
                while (running) {
                    toDelete = new Queue<Node>();
                    toRender = new Queue<Node>();
                    uint pointcount = TraverseAndBuildRenderingQueue();
                    mainThread.SetQueues(toRender, toDelete, pointcount);
                    lock (this) {
                        if (running) {
                            Monitor.Wait(this);
                        }
                    }
                }
            } catch (Exception ex) {
                Debug.LogError(ex);
            }
        }

        /// <summary>
        /// Sets the current camera data
        /// </summary>
        /// <param name="cameraPosition">Camera Position</param>
        /// <param name="camForward">Forward Vector</param>
        /// <param name="frustum">View Frustum</param>
        /// <param name="screenHeight">Screen Height</param>
        /// <param name="fieldOfView">Field of View</param>
        /// <param name="farClipPlane">Far Clip Plane</param>
        public void SetNextCameraData(Vector3 cameraPosition, Vector3 camForward, Plane[] frustum, float screenHeight, float fieldOfView, float farClipPlane) {
            lock (locker) {
                this.cameraPosition = parent.transform.InverseTransformPoint(cameraPosition);
                this.camForward = parent.transform.InverseTransformDirection(camForward);
                this.frustum = frustum;
                this.screenHeight = screenHeight;
                this.fieldOfView = fieldOfView;
                this.farClipPlane = farClipPlane;
            }
        }

        private bool TryGetNodePriority(Node node, Vector3 cameraPosition, Vector3 camForward, float screenHeight, float fieldOfView, float farClipPlane, out double priority) {
            Vector3 center = node.BoundingBox.GetBoundsObject().center;
            double radius = node.BoundingBox.Radius();
            double distance = Math.Max((center - cameraPosition).magnitude, 0.0001f);

            if (render360) {
                double nearestDistance = Math.Max(0.0, distance - radius);
                if (nearestDistance > farClipPlane) {
                    priority = 0;
                    return false;
                }

                priority = farClipPlane - nearestDistance;
                return true;
            }

            double slope = Math.Tan(fieldOfView / 2 * Mathf.Deg2Rad);
            double projectedSize = (screenHeight / 2.0) * radius / (slope * distance);
            if (projectedSize <= minNodeSize) {
                priority = 0;
                return false;
            }

            Vector3 camToNodeCenterDir = (center - cameraPosition).normalized;
            double dot = camForward.x * camToNodeCenterDir.x + camForward.y * camToNodeCenterDir.y + camForward.z * camToNodeCenterDir.z;
            dot = Math.Max(-1.0, Math.Min(1.0, dot));
            double angle = Math.Acos(dot);
            double angleWeight = Math.Abs(angle) + 1.0;
            priority = projectedSize / angleWeight;
            return true;
        }

        private bool IsNodeVisible(Node node, Vector3 cameraPosition, Plane[] frustum, float farClipPlane) {
            if (render360) {
                Vector3 center = node.BoundingBox.GetBoundsObject().center;
                double distance = (center - cameraPosition).magnitude;
                double nearestDistance = Math.Max(0.0, distance - node.BoundingBox.Radius());
                return nearestDistance <= farClipPlane;
            }

            return Util.InsideFrustum(node.BoundingBox, frustum);
        }

        private uint TraverseAndBuildRenderingQueue() {
            //Camera Data
            Vector3 cameraPosition;
            Vector3 camForward;
            Plane[] frustum;
            float screenHeight;
            float fieldOfView;
            float farClipPlane;

            PriorityQueue<double, Node> toProcess = new HeapPriorityQueue<double, Node>();

            lock (locker) {
                if (!render360 && this.frustum == null) {
                    return 0;
                }
                if (render360 && this.farClipPlane <= 0) {
                    return 0;
                }
                cameraPosition = this.cameraPosition;
                camForward = this.camForward;
                frustum = this.frustum;
                screenHeight = this.screenHeight;
                fieldOfView = this.fieldOfView;
                farClipPlane = this.farClipPlane;
            }
            //Clearing Queues
            uint renderingpointcount = 0;
            uint maxnodestoload = nodesLoadedPerFrame;
            uint maxnodestorender = nodesGOsPerFrame;
            HashSet<Node> newVisibleNodes = new HashSet<Node>();

            foreach (Node rootNode in rootNodes) {
                double priority;
                if (TryGetNodePriority(rootNode, cameraPosition, camForward, screenHeight, fieldOfView, farClipPlane, out priority)) {
                    toProcess.Enqueue(rootNode, priority);
                } else {
                    DeleteNode(rootNode);
                }
            }
            
            while (!toProcess.IsEmpty() && running) {
                Node n = toProcess.Dequeue(); //Min Node Size was already checked

                //Is Node inside frustum?
                if (IsNodeVisible(n, cameraPosition, frustum, farClipPlane)) {

                    bool loadchildren = false;
                    lock (n) {
                        if (n.PointCount == -1) {
                            if (maxnodestoload > 0) {
                                loadingThread.ScheduleForLoading(n);
                                --maxnodestoload;
                                loadchildren = true;
                            }
                        } else if (renderingpointcount + n.PointCount <= pointBudget) {
                            if (n.HasGameObjects()) {
                                renderingpointcount += (uint)n.PointCount;
                                visibleNodes.Remove(n);
                                newVisibleNodes.Add(n);
                                loadchildren = true;
                            } else if (n.HasPointsToRender()) {
                                //Might be in Cache -> Withdraw
                                if (maxnodestorender > 0) {
                                    cache.Withdraw(n);
                                    renderingpointcount += (uint)n.PointCount;
                                    toRender.Enqueue(n);
                                    --maxnodestorender;
                                    newVisibleNodes.Add(n);
                                    loadchildren = true;
                                }
                            } else {
                                if (maxnodestoload > 0) {
                                    loadingThread.ScheduleForLoading(n);
                                    --maxnodestoload;
                                    loadchildren = true;
                                }
                            }
                        } else {
                            maxnodestoload = 0;
                            maxnodestorender = 0;
                            if (n.HasGameObjects()) {
                                visibleNodes.Remove(n);
                                DeleteNode(n);
                            }
                        }
                    }

                    if (loadchildren) {
                        foreach (Node child in n) {
                            double priority;
                            if (TryGetNodePriority(child, cameraPosition, camForward, screenHeight, fieldOfView, farClipPlane, out priority)) {
                                toProcess.Enqueue(child, priority);
                            } else {
                                DeleteNode(child);
                            }
                        }
                    }

                } else {
                    //This node or its children might be visible
                    DeleteNode(n);
                }
            }
            foreach (Node n in visibleNodes) {
                DeleteNode(n);
            }
            visibleNodes = newVisibleNodes;
            return renderingpointcount;
        }

        private void DeleteNode(Node currentNode) {
            lock (currentNode) {
                if (!currentNode.HasGameObjects()) {
                    return;
                }
            }
            Queue<Node> nodesToDelete = new Queue<Node>();
            nodesToDelete.Enqueue(currentNode);
            Stack<Node> tempToDelete = new Stack<Node>();   //To assure better order in cache

            while (nodesToDelete.Count != 0) {
                Node child = nodesToDelete.Dequeue();
                Monitor.Enter(child);
                if (child.HasGameObjects()) {
                    Monitor.Exit(child);
                    tempToDelete.Push(child);

                    foreach (Node childchild in child) {
                        nodesToDelete.Enqueue(childchild);
                    }
                } else {
                    Monitor.Exit(child);
                }
            }
            while (tempToDelete.Count != 0) {
                Node n = tempToDelete.Pop();
                toDelete.Enqueue(n);
            }
        }

        public void Stop() {
            lock (this) {
                running = false;
            }
        }

        public void StopAndWait() {
            running = false;
            if (thread != null) {
                thread.Join();
                thread = null;
            }

        }

    }
}
