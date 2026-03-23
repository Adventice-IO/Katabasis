using System;
using BAPointCloudRenderer.DataStructures;
using BAPointCloudRenderer.CloudData;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;

namespace BAPointCloudRenderer.Loading {
    /// <summary>
    /// The Loading Thread of the V2-Rendering-System (see Bachelor Thesis chapter 3.2.6 "The Loading Thread").
    /// Responsible for loading the point data.
    /// </summary>
    class V2LoadingThread {

        private ThreadSafeQueue<Node> loadingQueue;
        private bool running = true;
        private V2Cache cache;
        private readonly HashSet<Node> scheduledNodes = new HashSet<Node>();
        private readonly object scheduledNodesLock = new object();
        private Thread thread;
        
        public V2LoadingThread(V2Cache cache) {
            loadingQueue = new ThreadSafeQueue<Node>();
            this.cache = cache;
        }

        public void Start() {
            running = true;
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Start();
        }

        private void Run() {
            try {
                while (running) {
                    Node n;
                    if (loadingQueue.TryDequeue(out n)) {
                        lock (scheduledNodesLock) {
                            scheduledNodes.Remove(n);
                        }
                        Monitor.Enter(n);
                        if (!n.HasPointsToRender() && !n.HasGameObjects()) {
                            Monitor.Exit(n);
                            CloudLoader.LoadPointsForNode(n);
                            cache.Insert(n);
                        } else {
                            Monitor.Exit(n);
                        }
                    } else {
                        Thread.Sleep(1);
                    }
                }
            } catch (Exception ex) {
                Debug.LogError(ex);
            }
        }

        public void Stop() {
            running = false;
            if (thread != null) {
                thread.Join();
                thread = null;
            }
            loadingQueue.Clear();
            lock (scheduledNodesLock) {
                scheduledNodes.Clear();
            }
        }

        /// <summary>
        /// Schedules the given node for loading.
        /// </summary>
        /// <param name="node">not null</param>
        public void ScheduleForLoading(Node node) {
            lock (scheduledNodesLock) {
                if (!scheduledNodes.Add(node)) {
                    return;
                }
            }
            loadingQueue.Enqueue(node);
        }

    }
}
