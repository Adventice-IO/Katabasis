using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public class DataManager : MonoBehaviour
{
    public bool enableDownload = false;

    public enum DataFolder
    {
        Interviews,
        Intro,
        Outro,
        Potree,
        Menu
    }

    [Serializable]
    public class FolderDownloadConfig
    {
        public DataFolder folder;
        public string zipUrl;
    }

    [Header("Downloads")]
    public string dataZipUrl;

    [Header("Storage")]
    public string desktopLocalStoragePath = "KataData";
    public string androidLocalStoragePath = "/sdcard/KataData";

    [Header("Preload")]
    public bool preloadOnStart = true;

    [Header("UI")]
    public TextMeshPro infoTM;

    readonly Dictionary<DataFolder, string> folderNames = new Dictionary<DataFolder, string>
    {
        { DataFolder.Interviews, "interviews" },
        { DataFolder.Intro, "intro" },
        { DataFolder.Outro, "outro" },
        { DataFolder.Potree, "potree" },
        { DataFolder.Menu, "menu" }
    };

    readonly Dictionary<DataFolder, string> cachedBasePaths = new Dictionary<DataFolder, string>();
    readonly Dictionary<DataFolder, bool> preloadResults = new Dictionary<DataFolder, bool>();
    readonly Dictionary<DataFolder, Coroutine> runningPreloads = new Dictionary<DataFolder, Coroutine>();
    readonly Dictionary<DataFolder, Coroutine> archiveDownloadCoroutines = new Dictionary<DataFolder, Coroutine>();
    readonly Dictionary<DataFolder, bool> archiveDownloadResults = new Dictionary<DataFolder, bool>();

    class ExtractionState
    {
        public readonly object SyncRoot = new object();
        public bool Completed;
        public bool Succeeded;
        public float Progress;
        public string StatusText = string.Empty;
        public string ErrorMessage = string.Empty;
    }

    void Awake()
    {

        if(infoTM != null)
        {
            infoTM.gameObject.SetActive(false);
        }

        if (preloadOnStart && Application.isPlaying)
        {
            PreloadAll();
        }

        bool errored = false;

        try
        {
            //Log the resolved paths for debugging
            foreach (DataFolder folder in Enum.GetValues(typeof(DataFolder)))
            {
                string path = GetBasePath(folder);
                Debug.Log("Resolved path for " + folder + ": " + (string.IsNullOrWhiteSpace(path) ? "Not found ('" + folder + "')" : path));
            }

            Debug.Log("Folder Check : " + Application.persistentDataPath + " -- " + Application.dataPath);
        }
        catch (Exception ex)
        {
            Debug.LogError("Error during DataManager initialization: " + ex.Message);
            errored = true;
        }

        if (errored)
        {
            Debug.Log("Looking for zip folder to extract");
            string folderZip = Path.Combine(Application.dataPath, "KataData.zip");

            if (File.Exists(folderZip))
            {
                Debug.Log("Found zip folder, attempting extraction to: " + Application.persistentDataPath);
                StartCoroutine(ExtractArchiveCoroutine(folderZip, Application.persistentDataPath, "KataData", true));
            }
            else
            {
                Debug.LogWarning("Data zip folder not found at: " + folderZip);
            }   
        }
    }

    //    public void RequestAllFilesAccess()
    //    {
    //#if UNITY_ANDROID && !UNITY_EDITOR
    //    using (var buildVersion = new AndroidJavaClass("android.os.Build$VERSION"))
    //    {
    //        int sdkInt = buildVersion.GetStatic<int>("SDK_INT");
    //        if (sdkInt >= 30) // Android 11+
    //        {
    //            var settings = new AndroidJavaClass("android.provider.Settings");
    //            var uri = new AndroidJavaClass("android.net.Uri");
    //            var intent = new AndroidJavaObject("android.content.Intent", "android.settings.MANAGE_APP_ALL_FILES_ACCESS_PERMISSION");
    //            intent.Call<AndroidJavaObject>("setData", uri.CallStatic<AndroidJavaObject>("parse", "package:" + Application.identifier));

    //            // This will take the user OUT of your app to the Quest Settings page
    //            var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
    //            var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
    //            currentActivity.Call("startActivity", intent);
    //        }
    //    }
    //#endif
    //    }

    void Start()
    {
        HideDownloadInfo();


    }

    void Update()
    {
    }

    public string GetBasePath(DataFolder folder)
    {
        return GetBasePathInternal(folder);
    }

    public string GetStreamingAssetsFolderPath(DataFolder folder)
    {
        return GetStreamingAssetsFolderPathInternal(folder);
    }

    public string GetLocalStorageRootPath()
    {
        return GetExternalDataRoot();
    }

    public string GetLocalStorageFolderPath(DataFolder folder)
    {
        return GetLocalStorageFolderPathInternal(folder);
    }

    public string GetPersistentFolderPath(DataFolder folder)
    {
        return GetPersisentFolderPathInternal(folder);
    }


    public string GetDownloadUrlForFolder(DataFolder folder)
    {
        return GetDownloadUrl(folder);
    }

    public string GetFolderPath(DataFolder folder, string relativePath = "")
    {
        return GetFolderPathInternal(folder, relativePath);
    }

    public string GetRootFilePath(string relativePath)
    {
        return GetRootFilePathInternal(relativePath);
    }

    public string GetFilePath(DataFolder folder, string relativePath)
    {
        return GetFolderPathInternal(folder, relativePath);
    }

    public string GetFileUrl(DataFolder folder, string relativePath)
    {
        string filePath = GetFilePath(folder, relativePath);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        return new Uri(filePath).AbsoluteUri;
    }

    public bool EnsureFolderAvailable(DataFolder folder)
    {
        return EnsureFolderAvailableInternal(folder);
    }

    public Coroutine EnsureFolderAvailable(DataFolder folder, Action<bool, string> onCompleted)
    {
        return StartCoroutine(EnsureFolderAvailableCoroutine(folder, onCompleted));
    }

    public Coroutine PreloadFolder(DataFolder folder, Action<bool, string> onCompleted = null)
    {
        return StartPreload(folder, onCompleted);
    }

    public void PreloadAll(Action<DataFolder, bool, string> onFolderCompleted = null, Action allCompleted = null)
    {
        StartCoroutine(PreloadAllCoroutine(onFolderCompleted, allCompleted));
    }

    public bool IsFolderReady(DataFolder folder)
    {
        return GetIsFolderReadyInternal(folder);
    }

    public bool IsPreloading(DataFolder folder)
    {
        return runningPreloads.ContainsKey(folder);
    }

    string GetBasePathInternal(DataFolder folder)
    {
        string path = ResolveExistingBasePath(folder);
        if (!string.IsNullOrWhiteSpace(path))
        {
            cachedBasePaths[folder] = path;
            return path;
        }

        cachedBasePaths.Remove(folder);
        return string.Empty;
    }

    string GetStreamingAssetsFolderPathInternal(DataFolder folder)
    {
        return Path.Combine(Application.streamingAssetsPath, "KataData", GetFolderName(folder)).Replace("\\", "/");
    }

    string GetLocalStorageFolderPathInternal(DataFolder folder)
    {
        return Path.Combine(GetExternalDataRoot(), "KataData", GetFolderName(folder)).Replace("\\", "/");
    }

    string GetPersisentFolderPathInternal(DataFolder folder)
    {
        return Path.Combine(Application.persistentDataPath, "KataData", GetFolderName(folder)).Replace("\\", "/");
    }

    string GetExecutableFolderPathInternal(DataFolder folder)
    {
        string execPath = Application.dataPath;
        if (Application.platform == RuntimePlatform.OSXPlayer)
        {
            execPath = Path.GetDirectoryName(execPath);
        }
        return Path.Combine(execPath, "KataData", GetFolderName(folder)).Replace("\\", "/");
    }

    string GetFolderPathInternal(DataFolder folder, string relativePath)
    {
        string basePath = GetBasePathInternal(folder);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return basePath;
        }

        string normalizedRelativePath = relativePath.Replace("\\", "/").TrimStart('/');
        return Path.Combine(basePath, normalizedRelativePath).Replace("\\", "/");
    }

    string GetRootFilePathInternal(string relativePath)
    {

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        string normalizedRelativePath = relativePath.Replace("\\", "/").TrimStart('/');

        string externalPath = Path.Combine(GetExternalDataRoot(), "KataData", normalizedRelativePath);
        //Debug.Log("Looking for root file at custom path : " + externalPath);
        if (File.Exists(externalPath))
        {
            return externalPath.Replace("\\", "/");
        }

        string persistentPath = Path.Combine(Application.persistentDataPath, "KataData", normalizedRelativePath);
        Debug.Log("Looking for root file at persistent data path: " + persistentPath);
        if (File.Exists(persistentPath))
        {
            return persistentPath.Replace("\\", "/");
        }

        string streamingPath = Path.Combine(Application.streamingAssetsPath, normalizedRelativePath);
        //Debug.Log("Looking for root file at streamingassets: " + streamingPath);
        if (File.Exists(streamingPath))
        {
            return streamingPath.Replace("\\", "/");
        }

        string execPath = Application.dataPath;
        if (Application.platform == RuntimePlatform.OSXPlayer)
        {
            execPath = Path.GetDirectoryName(execPath);
        }
        string executablePath = Path.Combine(execPath, "KataData", normalizedRelativePath);
        //Debug.Log("Looking for root file at executable path: " + executablePath);
        if (File.Exists(executablePath))
        {
            return executablePath.Replace("\\", "/");
        }

        //Debug.LogWarning("Root file not found at: " + streamingPath + " or " + externalPath);

        return string.Empty;
    }

    bool EnsureFolderAvailableInternal(DataFolder folder)
    {
        string path = ResolveExistingBasePath(folder, true);
        if (string.IsNullOrWhiteSpace(path))
        {
            cachedBasePaths.Remove(folder);
            return false;
        }

        cachedBasePaths[folder] = path;
        return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
    }

    bool GetIsFolderReadyInternal(DataFolder folder)
    {
        return EnsureFolderAvailableInternal(folder);
    }

    Coroutine StartPreload(DataFolder folder, Action<bool, string> onCompleted)
    {
        if (runningPreloads.TryGetValue(folder, out Coroutine existingCoroutine))
        {
            return existingCoroutine;
        }

        Coroutine coroutine = StartCoroutine(PreloadFolderCoroutine(folder, onCompleted));
        runningPreloads[folder] = coroutine;
        return coroutine;
    }

    IEnumerator PreloadFolderCoroutine(DataFolder folder, Action<bool, string> onCompleted)
    {
        bool success = false;
        string resolvedPath = string.Empty;

        yield return EnsureFolderAvailableCoroutine(folder, (result, path) =>
        {
            success = result;
            resolvedPath = path;
        });

        preloadResults[folder] = success;
        runningPreloads.Remove(folder);
        onCompleted?.Invoke(success, resolvedPath);
    }

    IEnumerator PreloadAllCoroutine(Action<DataFolder, bool, string> onFolderCompleted, Action allCompleted)
    {
        Array folders = Enum.GetValues(typeof(DataFolder));
        for (int i = 0; i < folders.Length; i++)
        {
            DataFolder folder = (DataFolder)folders.GetValue(i);
            bool finished = false;
            bool success = false;
            string path = string.Empty;

            yield return StartPreload(folder, (result, resolvedPath) =>
            {
                success = result;
                path = resolvedPath;
                finished = true;
            });

            while (!finished)
            {
                yield return null;
            }

            onFolderCompleted?.Invoke(folder, success, path);
        }

        allCompleted?.Invoke();
    }

    IEnumerator EnsureFolderAvailableCoroutine(DataFolder folder, Action<bool, string> onCompleted)
    {
        string existingPath = ResolveExistingBasePath(folder, true);
        if (!string.IsNullOrWhiteSpace(existingPath) && Directory.Exists(existingPath))
        {
            cachedBasePaths[folder] = existingPath;
            HideDownloadInfo();
            onCompleted?.Invoke(true, existingPath);
            yield break;
        }

        string downloadUrl = GetDownloadUrl(folder);
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            HideDownloadInfo();
            onCompleted?.Invoke(false, string.Empty);
            yield break;
        }

        if (enableDownload)
        {
            if (!archiveDownloadCoroutines.TryGetValue(folder, out Coroutine archiveDownloadCoroutine) || archiveDownloadCoroutine == null)
            {
                archiveDownloadCoroutine = StartCoroutine(DownloadArchiveCoroutine(folder, downloadUrl));
                archiveDownloadCoroutines[folder] = archiveDownloadCoroutine;
            }

            while (archiveDownloadCoroutines.TryGetValue(folder, out archiveDownloadCoroutine) && archiveDownloadCoroutine != null)
            {
                yield return null;
            }

            if (!archiveDownloadResults.TryGetValue(folder, out bool archiveDownloadSucceeded) || !archiveDownloadSucceeded)
            {
                HideDownloadInfo();
                onCompleted?.Invoke(false, string.Empty);
                yield break;
            }
        }

        existingPath = ResolveExistingBasePath(folder, true);
        if (!string.IsNullOrWhiteSpace(existingPath) && Directory.Exists(existingPath))
        {
            cachedBasePaths[folder] = existingPath;
            HideDownloadInfo();
            onCompleted?.Invoke(true, existingPath);
            yield break;
        }

        HideDownloadInfo();
        onCompleted?.Invoke(false, string.Empty);
    }

    IEnumerator DownloadArchiveCoroutine(DataFolder folder, string downloadUrl)
    {
        archiveDownloadResults[folder] = false;

        string externalRoot = GetExternalDataRoot();
        Directory.CreateDirectory(externalRoot);

        string folderName = GetFolderName(folder);
        string zipPath = Path.Combine(externalRoot, folderName + ".zip");

        ShowInfo("Downloading " + folderName + "...", 0f);

        using (UnityWebRequest request = UnityWebRequest.Get(downloadUrl))
        {
            request.downloadHandler = new DownloadHandlerFile(zipPath);
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                ShowInfo("Downloading " + folderName + "...", request.downloadProgress);
                yield return null;
            }

            ShowInfo("Downloading " + folderName + "...", 1f);

            if (request.result != UnityWebRequest.Result.Success)
            {
                //Debug.LogError("Failed to download data archive for " + folderName + ": " + request.error);
                HideDownloadInfo();
                archiveDownloadCoroutines[folder] = null;
                yield break;
            }
        }

        yield return ExtractArchiveCoroutine(zipPath, externalRoot, folderName, true, GetLocalStorageFolderPathInternal(folder), success =>
        {
            archiveDownloadResults[folder] = success;
        });

        archiveDownloadCoroutines[folder] = null;
    }

    IEnumerator ExtractArchiveCoroutine(string zipPath, string destinationRoot, string displayName, bool deleteArchiveWhenDone, string folderToDeleteBeforeExtract = null, Action<bool> onCompleted = null)
    {
        ExtractionState extractionState = new ExtractionState();
        BeginExtraction(displayName, extractionState);

        Thread extractionThread = new Thread(() =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(folderToDeleteBeforeExtract) && Directory.Exists(folderToDeleteBeforeExtract))
                {
                    UpdateExtractionStatus(extractionState, "Preparing " + displayName + "...", 0.05f);
                    Directory.Delete(folderToDeleteBeforeExtract, true);
                }

                ExtractZipWithProgress(zipPath, destinationRoot, displayName, extractionState);

                if (deleteArchiveWhenDone && File.Exists(zipPath))
                {
                    UpdateExtractionStatus(extractionState, "Cleaning up " + displayName + "...", 0.98f);
                    File.Delete(zipPath);
                }

                CompleteExtraction(extractionState, true, string.Empty);
            }
            catch (Exception ex)
            {
                CompleteExtraction(extractionState, false, ex.Message);
            }
        });

        extractionThread.IsBackground = true;
        extractionThread.Start();

        while (!extractionState.Completed)
        {
            ShowInfo(GetExtractionStatusText(extractionState), GetExtractionProgress(extractionState));
            yield return null;
        }

        if (extractionState.Succeeded)
        {
            ShowInfo("Extracting " + displayName + "...", 1f);
            yield return null;
        }
        else if (!string.IsNullOrWhiteSpace(extractionState.ErrorMessage))
        {
            Debug.LogError("Failed to extract data archive for " + displayName + ": " + extractionState.ErrorMessage);
        }

        HideDownloadInfo();
        onCompleted?.Invoke(extractionState.Succeeded);
    }

    string ResolveExistingBasePath(DataFolder folder)
    {
        return ResolveExistingBasePath(folder, false);
    }

    string ResolveExistingBasePath(DataFolder folder, bool preferLocalStorage)
    {
        string primaryPath = preferLocalStorage
            ? GetLocalStorageFolderPathInternal(folder)
            : GetStreamingAssetsFolderPathInternal(folder);

        if (Directory.Exists(primaryPath))
        {
            return primaryPath.Replace("\\", "/");
        }

        string fallbackPath = preferLocalStorage
            ? GetStreamingAssetsFolderPathInternal(folder)
            : GetLocalStorageFolderPathInternal(folder);

        if (Directory.Exists(fallbackPath))
        {
            return fallbackPath.Replace("\\", "/");
        }

        string persistentPath = GetPersisentFolderPathInternal(folder);
        if (Directory.Exists(persistentPath))
        {
            return persistentPath.Replace("\\", "/");
        }

        if (Directory.Exists(GetExecutableFolderPathInternal(folder)))
        {
            return GetExecutableFolderPathInternal(folder).Replace("\\", "/");
        }

        return string.Empty;
    }

    string GetDownloadUrl(DataFolder folder)
    {
        if (string.IsNullOrWhiteSpace(dataZipUrl))
        {
            return string.Empty;
        }

        string normalizedBaseUrl = dataZipUrl.Trim();
        string folderName = GetFolderName(folder);

        if (normalizedBaseUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            string directory = Path.GetDirectoryName(normalizedBaseUrl)?.Replace("\\", "/");
            if (string.IsNullOrWhiteSpace(directory))
            {
                return folderName + ".zip";
            }

            return directory.TrimEnd('/') + "/" + folderName + ".zip";
        }

        return normalizedBaseUrl.TrimEnd('/') + "/" + folderName + ".zip";
    }

    string GetFolderName(DataFolder folder)
    {
        return folderNames.TryGetValue(folder, out string folderName) ? folderName : folder.ToString().ToLowerInvariant();
    }

    string GetExternalDataRoot()
    {

        string root;
        if (Application.platform == RuntimePlatform.Android)
        {
            root = androidLocalStoragePath;
        }
        else
        {
            root = desktopLocalStoragePath;
        }

        if (root == "") root = ".";

        root = Path.GetFullPath(root);

        if (!Directory.Exists(root))
        {
            // relative to executable
            string execPath = Application.dataPath;
            if (Application.platform == RuntimePlatform.OSXPlayer)
            {
                execPath = Path.GetDirectoryName(execPath);
            }

            if (string.IsNullOrWhiteSpace(execPath))
            {
                //Debug.LogError("Failed to resolve executable path for fallback storage location");
                return root.Replace("\\", "/");
            }

            string relativeExecPath = Path.Combine(execPath, root).Replace("\\", "/");
            //Debug.Log("storage not found at " + root + ", checking relative path: " + relativeExecPath);
            root = relativeExecPath;
        }


        //if(Directory.Exists(root))
        //{
        //    Debug.Log("Resolved external data root path: " + root);
        //}
        //else
        //{
        //    Debug.LogWarning("External data root directory does not exist at resolved path: " + root);
        //}

        return root.Replace("\\", "/");
    }

    void ExtractZipWithProgress(string zipPath, string destinationRoot, string displayName)
    {
        using (ZipArchive archive = ZipFile.OpenRead(zipPath))
        {
            int totalEntries = archive.Entries.Count;
            int processedEntries = 0;

            if (totalEntries == 0)
            {
                UpdateExtractionStatus("Extracting " + displayName + "...", 1f);
                return;
            }

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string fullPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
                string normalizedDestinationRoot = Path.GetFullPath(destinationRoot);

                if (!fullPath.StartsWith(normalizedDestinationRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("Entry is outside the target dir: " + entry.FullName);
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(fullPath);
                }
                else
                {
                    string directoryPath = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }

                    entry.ExtractToFile(fullPath, true);
                }

                processedEntries++;
                float progress = Mathf.Clamp01((float)processedEntries / totalEntries);
                UpdateExtractionStatus("Extracting " + displayName + "...", progress);
            }
        }
    }

    void BeginExtraction(string displayName)
    {
        lock (extractionProgressLock)
        {
            extractionInProgress = true;
            extractionCompleted = false;
            extractionSucceeded = false;
            extractionProgress = 0f;
            extractionStatusText = "Extracting " + displayName + "...";
            extractionErrorMessage = string.Empty;
        }
    }

    void UpdateExtractionStatus(string statusText, float progress)
    {
        lock (extractionProgressLock)
        {
            extractionStatusText = statusText;
            extractionProgress = Mathf.Clamp01(progress);
        }
    }

    void CompleteExtraction(bool succeeded, string errorMessage)
    {
        lock (extractionProgressLock)
        {
            extractionSucceeded = succeeded;
            extractionCompleted = true;
            extractionInProgress = false;
            extractionProgress = succeeded ? 1f : extractionProgress;
            extractionErrorMessage = errorMessage;
        }
    }

    float GetExtractionProgress()
    {
        lock (extractionProgressLock)
        {
            return extractionProgress;
        }
    }

    string GetExtractionStatusText()
    {
        lock (extractionProgressLock)
        {
            return extractionStatusText;
        }
    }

    void ShowDownloadInfo(string folderName, float progress)
    {
        if (infoTM == null)
        {
            return;
        }

        if (!infoTM.gameObject.activeSelf)
        {
            infoTM.gameObject.SetActive(true);
        }

        infoTM.text = folderName + " " + Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f) + "%";
    }

    void HideDownloadInfo()
    {
        if (infoTM != null)
        {
            infoTM.gameObject.SetActive(false);
        }
    }

}
