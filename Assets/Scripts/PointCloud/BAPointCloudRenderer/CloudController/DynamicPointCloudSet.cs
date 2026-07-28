using BAPointCloudRenderer.Loading;
using BAPointCloudRenderer.ObjectCreation;
using UnityEngine;
using UnityEditor;

namespace BAPointCloudRenderer.CloudController {

    /// <summary>
    /// Point Cloud Set to display a large point cloud. All the time, only the points which are needed for the current camera position are loaded from the disk (as described in the thesis).
    /// </summary>
    public class DynamicPointCloudSet : AbstractPointCloudSet {
        /// <summary>
        /// Point Budget - Maximum Number of Points in Memory / to Render
        /// </summary>
        public uint pointBudget = 1000000;
        /// <summary>
        /// Minimum Node Size
        /// </summary>
        public int minNodeSize = 10;
        /// <summary>
        /// Maximum number of nodes loaded per frame
        /// </summary>
        public uint nodesLoadedPerFrame = 15;
        /// <summary>
        /// Maximum number of nodes having their gameobjects created per frame
        /// </summary>
        public uint nodesGOsPerFrame = 30;
        /// <summary>
        /// Cache Size in POints
        /// </summary>
        public uint cacheSizeInPoints = 1000000;
        /// <summary>
        /// Camera to use. If none is specified, Camera.main is used
        /// </summary>
        public Camera userCamera;
        /// <summary>
        /// If enabled, nodes are loaded in all directions based only on camera position and far clip plane.
        /// </summary>
        public bool render360 = false;
        /// <summary>
        /// If enabled, periodic runtime diagnostics are logged to help track leaks or stalled loading.
        /// </summary>
        public bool enableDiagnostics = true;
        /// <summary>
        /// Interval in seconds between diagnostic snapshots.
        /// </summary>
        public float diagnosticsLogIntervalSeconds = 10f;

        private V2Renderer runtimeRenderer;

        /// <summary>
        /// Changes whether point-cloud traversal loads nodes in every direction.
        /// This can be called before or after the runtime renderer is initialized.
        /// </summary>
        public void SetRender360(bool enabled) {
            render360 = enabled;
            runtimeRenderer?.SetRender360(enabled);
        }

        // Use this for initialization
        protected override void Initialize() {
            if (userCamera == null) {
                userCamera = Camera.main;
            }
            runtimeRenderer = new V2Renderer(this, minNodeSize, pointBudget, nodesLoadedPerFrame, nodesGOsPerFrame, userCamera, meshConfiguration, cacheSizeInPoints, render360);
            PointRenderer = runtimeRenderer;
        }


        // Update is called once per frame
        void Update()
        {
            if (!CheckReady())
            {
                return;
            }
            PointRenderer.Update();
            if (enableDiagnostics && runtimeRenderer != null)
            {
                runtimeRenderer.LogPeriodicSnapshot(Mathf.Max(1f, diagnosticsLogIntervalSeconds));
            }
            DrawDebugInfo();
        }
    }
}
