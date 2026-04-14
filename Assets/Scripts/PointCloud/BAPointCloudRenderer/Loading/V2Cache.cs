using BAPointCloudRenderer.DataStructures;
using BAPointCloudRenderer.CloudData;

namespace BAPointCloudRenderer.Loading {
    /// <summary>
    /// The LRU Cache used by the V2Renderer (See Bachelor Thesis chapter 3.2.7 "LRU Cache").
    /// </summary>
    class V2Cache {

        private uint maxPoints;
        private uint cachePointCount = 0;
        private int cacheNodeCount = 0;
        private int evictionCount = 0;
        private int directDropCount = 0;
        private RandomAccessQueue<Node> queue = new RandomAccessQueue<Node>();

        /// <summary>
        /// Creates a new Cache
        /// </summary>
        /// <param name="maxPoints">Maximum number of points in this cache</param>
        public V2Cache(uint maxPoints) {
            this.maxPoints = maxPoints;
        }
        
        /// <summary>
        /// Inserts the node into this cache. If the node is already inside the cache, it is moved to the front.
        /// If theres no place inside the cache for this node, other nodes get removed from the cache (and their point data gets deleted) in order to free up space for this node.
        /// If this node still does not fit inside the cache its points are deleted right away.
        /// </summary>
        /// <param name="node">Node, which has its points in memory right now</param>
        public void Insert(Node node) {
            lock (queue) {
                Withdraw(node); //it might be in the queue already but has to be moved to the front
                //Alte Objekte aus Cache entfernen
                while (cachePointCount + node.PointCount > maxPoints && !queue.IsEmpty()) {
                    Node old = queue.Dequeue();
                    cachePointCount -= (uint)old.PointCount;
                    cacheNodeCount--;
                    evictionCount++;
                    old.ForgetPoints();
                }
                if (cachePointCount + node.PointCount <= maxPoints) {
                    //In Cache einfügen
                    queue.Enqueue(node);
                    cachePointCount += (uint)node.PointCount;
                    cacheNodeCount++;
                } else {
                    //Nicht in Cache einfügen -> direkt entfernen
                    directDropCount++;
                    node.ForgetPoints();
                }
            }
        }

        /// <summary>
        /// Removes a node from the cache (without deleting the point data), if the node exists inside the cache. If the node is not in the cache, nothing happens
        /// </summary>
        public void Withdraw(Node node) {
            lock (queue) {
                if (queue.Contains(node)) {
                    queue.Remove(node);
                    cachePointCount -= (uint)node.PointCount;
                    cacheNodeCount--;
                }
            }
        }

        /// <summary>
        /// Returns how many points are stored inside the cache right now
        /// </summary>
        public uint PointCount() {
            lock (queue) {
                return cachePointCount;
            }
        }

        public uint MaxPointCount() {
            return maxPoints;
        }

        public int NodeCount() {
            lock (queue) {
                return cacheNodeCount;
            }
        }

        public int EvictionCount() {
            lock (queue) {
                return evictionCount;
            }
        }

        public int DirectDropCount() {
            lock (queue) {
                return directDropCount;
            }
        }
    }
}
