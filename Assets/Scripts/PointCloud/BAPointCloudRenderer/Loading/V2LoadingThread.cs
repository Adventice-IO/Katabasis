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
        private const string LogPrefix = "[PointCloudLoading]";

        private ThreadSafeQueue<Node> loadingQueue;
        private bool running = true;
        private V2Cache cache;
        private readonly HashSet<Node> scheduledNodes = new HashSet<Node>();
        private readonly object scheduledNodesLock = new object();
        private Thread thread;
        private int pendingQueueCount;
        private long completedLoadCount;
        private long failedLoadCount;
        private string lastFailure;
        
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
            while (running) {
                try {
                    Node n;
                    if (loadingQueue.TryDequeue(out n)) {
                        Interlocked.Decrement(ref pendingQueueCount);
                        lock (scheduledNodesLock) {
                            scheduledNodes.Remove(n);
                        }

                        bool shouldLoad = false;
                        Monitor.Enter(n);
                        try {
                            shouldLoad = !n.HasPointsToRender() && !n.HasGameObjects();
                        } finally {
                            Monitor.Exit(n);
                        }

                        if (!shouldLoad) {
                            continue;
                        }

                        CloudLoader.LoadPointsForNode(n);
                        cache.Insert(n);
                        Interlocked.Increment(ref completedLoadCount);
                    } else {
                        Thread.Sleep(1);
                    }
                } catch (Exception ex) {
                    lastFailure = ex.ToString();
                    Interlocked.Increment(ref failedLoadCount);
                    Debug.LogError($"{LogPrefix} Node load failed but the loading thread will continue: {ex}");
                }
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
            Interlocked.Increment(ref pendingQueueCount);
        }

        public bool IsAlive() {
            return thread != null && thread.IsAlive;
        }

        public int PendingQueueCount() {
            return Interlocked.CompareExchange(ref pendingQueueCount, 0, 0);
        }

        public int ScheduledNodeCount() {
            lock (scheduledNodesLock) {
                return scheduledNodes.Count;
            }
        }

        public long CompletedLoadCount() {
            return Interlocked.Read(ref completedLoadCount);
        }

        public long FailedLoadCount() {
            return Interlocked.Read(ref failedLoadCount);
        }

        public string LastFailure() {
            return lastFailure;
        }

    }
}
