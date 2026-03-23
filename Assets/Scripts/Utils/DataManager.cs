using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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

    public void CheckStoragePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
    using (var buildVersion = new AndroidJavaClass("android.os.Build$VERSION")) {
        if (buildVersion.GetStatic<int>("SDK_INT") >= 30) { // Android 11+
            var environment = new AndroidJavaClass("android.os.Environment");
            if (!environment.CallStatic<bool>("isExternalStorageManager")) {
                var intent = new AndroidJavaClass("android.content.Intent");
                var settings = new AndroidJavaClass("android.provider.Settings");
                var uri = new AndroidJavaClass("android.net.Uri");
                
                var intentObj = new AndroidJavaObject("android.content.Intent", 
                    settings.GetStatic<string>("ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION"));
                
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer")) {
                    var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    currentActivity.Call("startActivity", intentObj);
                }
            }
        }
    }
#endif
    }
    void Awake()
    {

        CheckStoragePermission();

        if (preloadOnStart && Application.isPlaying)
        {
            PreloadAll();
        }

        //Log the resolved paths for debugging
        foreach (DataFolder folder in Enum.GetValues(typeof(DataFolder)))
        {
            string path = GetBasePath(folder);
            Debug.Log("Resolved path for " + folder + ": " + (string.IsNullOrWhiteSpace(path) ? "Not found ('" + folder + "')" : path));
        }

        Debug.Log("Folder Check : " + Application.persistentDataPath + " -- " + Application.dataPath);
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
        Debug.Log("Looking for root file at custom path : " + externalPath);
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
        Debug.Log("Looking for root file at streamingassets: " + streamingPath);
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
        Debug.Log("Looking for root file at executable path: " + executablePath);
        if (File.Exists(executablePath))
        {
            return executablePath.Replace("\\", "/");
        }

        Debug.LogWarning("Root file not found at: " + streamingPath + " or " + externalPath);

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

        ShowDownloadInfo(folderName, 0f);

        using (UnityWebRequest request = UnityWebRequest.Get(downloadUrl))
        {
            request.downloadHandler = new DownloadHandlerFile(zipPath);
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                ShowDownloadInfo(folderName, request.downloadProgress);
                yield return null;
            }

            ShowDownloadInfo(folderName, 1f);

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Failed to download data archive for " + folderName + ": " + request.error);
                HideDownloadInfo();
                archiveDownloadCoroutines[folder] = null;
                yield break;
            }
        }

        try
        {
            string folderPath = GetLocalStorageFolderPathInternal(folder);
            if (Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath, true);
            }

            ZipFile.ExtractToDirectory(zipPath, externalRoot);
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to extract data archive for " + folderName + ": " + ex.Message);
            HideDownloadInfo();
            archiveDownloadCoroutines[folder] = null;
            yield break;
        }

        archiveDownloadResults[folder] = true;
        HideDownloadInfo();
        archiveDownloadCoroutines[folder] = null;
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

        infoTM.text = "Downloading " + folderName + "... " + Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f) + "%";
    }

    void HideDownloadInfo()
    {
        if (infoTM != null)
        {
            infoTM.gameObject.SetActive(false);
        }
    }

}
