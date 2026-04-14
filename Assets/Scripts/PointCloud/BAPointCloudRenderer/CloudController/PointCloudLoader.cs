using BAPointCloudRenderer.CloudData;
using BAPointCloudRenderer.Loading;
using System;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEditor;

namespace BAPointCloudRenderer.CloudController
{
    /// <summary>
    /// Use this script to load a single PointCloud from a directory.
    ///
    /// Streaming Assets support provided by Pablo Vidaurre
    /// </summary>
    [ExecuteAlways]
    public class PointCloudLoader : MonoBehaviour
    {
        private const string LogPrefix = "[PointCloudLoader]";

        /// <summary>
        /// Path to the folder which contains the cloud.js file or URL to download the cloud from. In the latter case, it will be downloaded to a /temp folder
        /// </summary>
        public string cloudPath;

        /// <summary>
        /// When true, the cloudPath is relative to the streaming assets directory
        /// </summary>
        public bool streamingAssetsAsRoot = false;

        /// <summary>
        /// The PointSetController to use
        /// </summary>
        public AbstractPointCloudSet setController;

        /// <summary>
        /// True if the point cloud should be loaded when the behaviour is started. Otherwise the point cloud is loaded when LoadPointCloud is loaded.
        /// </summary>
        public bool loadOnStart = true;

        private Node rootNode;
        bool waitingForPotreeData;
        string resolvedCloudPath;
        private Thread hierarchyThread;

        DataManager dataManager;
        private void Awake()
        {

        }

        void Start()
        {
            dataManager = GameObject.FindAnyObjectByType<DataManager>();

            if (Application.isPlaying)
            {
                if (loadOnStart)
                {
                    LoadPointCloud();
                }
            }
        }

        private void LoadHierarchy()
        {
            try
            {
                PointCloudMetaData metaData = CloudLoader.LoadMetaData(resolvedCloudPath, false, false);

                setController.UpdateBoundingBox(this, metaData.boundingBox_transformed, metaData.tightBoundingBox_transformed);

                rootNode = CloudLoader.LoadHierarchyOnly(metaData);

                setController.AddRootNode(this, rootNode, metaData);
                Debug.Log($"{LogPrefix} Loaded hierarchy for '{metaData.cloudName}' from '{resolvedCloudPath}'.");

            }
            catch (System.IO.FileNotFoundException ex)
            {
                Debug.LogError($"{LogPrefix} Could not find file while loading '{resolvedCloudPath}': {ex.FileName}");
            }
            catch (System.IO.DirectoryNotFoundException ex)
            {
                Debug.LogError($"{LogPrefix} Could not find directory while loading '{resolvedCloudPath}': {ex.Message}");
            }
            catch (System.Net.WebException ex)
            {
                Debug.LogError($"{LogPrefix} Could not access web address while loading '{resolvedCloudPath}': {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogPrefix} Unexpected hierarchy load failure on '{resolvedCloudPath}' in thread '{Thread.CurrentThread.Name}': {ex}");
            }
            finally
            {
                hierarchyThread = null;
            }
        }

        /// <summary>
        /// Starts loading the point cloud. When the hierarchy is loaded it is registered at the corresponding point cloud set
        /// </summary>
        public void LoadPointCloud()
        {
            if (rootNode == null && setController != null && cloudPath != null)
            {
                if (streamingAssetsAsRoot && !dataManager.IsFolderReady(DataManager.DataFolder.Potree))
                {
                    if (!waitingForPotreeData)
                    {
                        waitingForPotreeData = true;
                        dataManager.PreloadFolder(DataManager.DataFolder.Potree, (success, path) =>
                        {
                            waitingForPotreeData = false;
                            if (success)
                            {
                                LoadPointCloud();
                            }
                        });
                    }
                    return;
                }

                resolvedCloudPath = GetResolvedCloudPath();
                setController.RegisterController(this);
                hierarchyThread = new Thread(LoadHierarchy);
                hierarchyThread.Name = "Loader for " + resolvedCloudPath;
                hierarchyThread.IsBackground = true;
                Debug.Log($"{LogPrefix} Starting hierarchy load for '{resolvedCloudPath}'.");
                hierarchyThread.Start();
            }
        }

        public string GetResolvedCloudPath()
        {
            if (!streamingAssetsAsRoot)
            {
                return cloudPath;
            }

            string normalizedPath = (cloudPath ?? string.Empty).Replace("\\", "/").Trim('/');
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return dataManager.GetBasePath(DataManager.DataFolder.Potree);
            }

            if (Uri.IsWellFormedUriString(normalizedPath, UriKind.Absolute) || Path.IsPathRooted(normalizedPath))
            {
                return normalizedPath;
            }

            if (string.Equals(normalizedPath, "potree", StringComparison.OrdinalIgnoreCase))
            {
                return dataManager.GetBasePath(DataManager.DataFolder.Potree);
            }

            if (normalizedPath.StartsWith("potree/", StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = normalizedPath.Substring("potree/".Length);
            }

            return dataManager.GetFolderPath(DataManager.DataFolder.Potree, normalizedPath);
        }

        /// <summary>
        /// Removes the point cloud from the scene. Should only be called from the main thread!
        /// </summary>
        /// <returns>True if the cloud was removed. False, when the cloud hasn't even been loaded yet.</returns>
        public bool RemovePointCloud()
        {
            if (rootNode == null)
            {
                return false;
            }
            setController.RemoveRootNode(this, rootNode);
            Debug.Log($"{LogPrefix} Removed point cloud '{rootNode.MetaData.cloudName}' from '{resolvedCloudPath}'.");
            rootNode = null;
            return true;
        }

    }
}
