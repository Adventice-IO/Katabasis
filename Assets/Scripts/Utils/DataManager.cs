using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public class DataManager : MonoBehaviour
{
    public static DataManager instance;

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
    public string desktopLocalStoragePath = "KatabasisData";
    public string androidLocalStoragePath = "/storage/emulated/0/Android/data/com.DefaultCompany.Katabasis/files/KatabasisData";

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
    Coroutine archiveDownloadCoroutine;
    bool archiveDownloadSucceeded;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        HideDownloadInfo();

        if (preloadOnStart && Application.isPlaying)
        {
            PreloadAll();
        }
    }

    void Update()
    {
    }

    public static string GetBasePath(DataFolder folder)
    {
        return EnsureInstance().GetBasePathInternal(folder);
    }

    public static string GetStreamingAssetsFolderPath(DataFolder folder)
    {
        return EnsureInstance().GetStreamingAssetsFolderPathInternal(folder);
    }

    public static string GetLocalStorageRootPath()
    {
        return EnsureInstance().GetExternalDataRoot();
    }

    public static string GetLocalStorageFolderPath(DataFolder folder)
    {
        return EnsureInstance().GetLocalStorageFolderPathInternal(folder);
    }

    public static string GetDownloadUrlForFolder(DataFolder folder)
    {
        return EnsureInstance().GetDownloadUrl(folder);
    }

    public static string GetFolderPath(DataFolder folder, string relativePath = "")
    {
        return EnsureInstance().GetFolderPathInternal(folder, relativePath);
    }

    public static string GetRootFilePath(string relativePath)
    {
        return EnsureInstance().GetRootFilePathInternal(relativePath);
    }

    public static string GetFilePath(DataFolder folder, string relativePath)
    {
        return EnsureInstance().GetFolderPathInternal(folder, relativePath);
    }

    public static string GetFileUrl(DataFolder folder, string relativePath)
    {
        string filePath = GetFilePath(folder, relativePath);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        return new Uri(filePath).AbsoluteUri;
    }

    public static bool EnsureFolderAvailable(DataFolder folder)
    {
        return EnsureInstance().EnsureFolderAvailableInternal(folder);
    }

    public static Coroutine EnsureFolderAvailable(DataFolder folder, Action<bool, string> onCompleted)
    {
        return EnsureInstance().StartCoroutine(EnsureInstance().EnsureFolderAvailableCoroutine(folder, onCompleted));
    }

    public static Coroutine PreloadFolder(DataFolder folder, Action<bool, string> onCompleted = null)
    {
        return EnsureInstance().StartPreload(folder, onCompleted);
    }

    public static void PreloadAll(Action<DataFolder, bool, string> onFolderCompleted = null, Action allCompleted = null)
    {
        EnsureInstance().StartCoroutine(EnsureInstance().PreloadAllCoroutine(onFolderCompleted, allCompleted));
    }

    public static bool IsFolderReady(DataFolder folder)
    {
        return EnsureInstance().GetIsFolderReadyInternal(folder);
    }

    public static bool IsPreloading(DataFolder folder)
    {
        return EnsureInstance().runningPreloads.ContainsKey(folder);
    }

    string GetBasePathInternal(DataFolder folder)
    {
        if (cachedBasePaths.TryGetValue(folder, out string cachedPath) && Directory.Exists(cachedPath))
        {
            return cachedPath;
        }

        string path = ResolveExistingBasePath(folder);
        if (!string.IsNullOrWhiteSpace(path))
        {
            cachedBasePaths[folder] = path;
            return path;
        }

        return string.Empty;
    }

    string GetStreamingAssetsFolderPathInternal(DataFolder folder)
    {
        return Path.Combine(Application.streamingAssetsPath, GetFolderName(folder)).Replace("\\", "/");
    }

    string GetLocalStorageFolderPathInternal(DataFolder folder)
    {
        return Path.Combine(GetExternalDataRoot(), GetFolderName(folder)).Replace("\\", "/");
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
        string streamingPath = Path.Combine(Application.streamingAssetsPath, normalizedRelativePath);
        if (File.Exists(streamingPath))
        {
            return streamingPath.Replace("\\", "/");
        }

        string externalPath = Path.Combine(GetExternalDataRoot(), normalizedRelativePath);
        if (File.Exists(externalPath))
        {
            return externalPath.Replace("\\", "/");
        }

        return string.Empty;
    }

    bool EnsureFolderAvailableInternal(DataFolder folder)
    {
        string path = GetBasePathInternal(folder);
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
        string existingPath = GetBasePathInternal(folder);
        if (!string.IsNullOrWhiteSpace(existingPath) && Directory.Exists(existingPath))
        {
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

        if (archiveDownloadCoroutine == null)
        {
            archiveDownloadCoroutine = StartCoroutine(DownloadArchiveCoroutine(downloadUrl));
        }

        while (archiveDownloadCoroutine != null)
        {
            yield return null;
        }

        if (!archiveDownloadSucceeded)
        {
            HideDownloadInfo();
            onCompleted?.Invoke(false, string.Empty);
            yield break;
        }

        existingPath = GetBasePathInternal(folder);
        if (!string.IsNullOrWhiteSpace(existingPath) && Directory.Exists(existingPath))
        {
            HideDownloadInfo();
            onCompleted?.Invoke(true, existingPath);
            yield break;
        }

        HideDownloadInfo();
        onCompleted?.Invoke(false, string.Empty);
    }

    IEnumerator DownloadArchiveCoroutine(string downloadUrl)
    {
        archiveDownloadSucceeded = false;

        string externalRoot = GetExternalDataRoot();
        Directory.CreateDirectory(externalRoot);

        string zipPath = Path.Combine(externalRoot, "data.zip");

        ShowDownloadInfo("data", 0f);

        using (UnityWebRequest request = UnityWebRequest.Get(downloadUrl))
        {
            request.downloadHandler = new DownloadHandlerFile(zipPath);
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                ShowDownloadInfo("data", request.downloadProgress);
                yield return null;
            }

            ShowDownloadInfo("data", 1f);

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Failed to download data archive: " + request.error);
                HideDownloadInfo();
                archiveDownloadCoroutine = null;
                yield break;
            }
        }

        try
        {
            Array folders = Enum.GetValues(typeof(DataFolder));
            for (int i = 0; i < folders.Length; i++)
            {
                string folderPath = GetLocalStorageFolderPathInternal((DataFolder)folders.GetValue(i));
                if (Directory.Exists(folderPath))
                {
                    Directory.Delete(folderPath, true);
                }
            }

            ZipFile.ExtractToDirectory(zipPath, externalRoot);
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to extract data archive: " + ex.Message);
            HideDownloadInfo();
            archiveDownloadCoroutine = null;
            yield break;
        }

        archiveDownloadSucceeded = true;
        HideDownloadInfo();
        archiveDownloadCoroutine = null;
    }

    string ResolveExistingBasePath(DataFolder folder)
    {
        string streamingAssetsPath = GetStreamingAssetsFolderPathInternal(folder);
        if (Directory.Exists(streamingAssetsPath))
        {
            return streamingAssetsPath.Replace("\\", "/");
        }

        string externalPath = GetLocalStorageFolderPathInternal(folder);
        if (Directory.Exists(externalPath))
        {
            return externalPath.Replace("\\", "/");
        }

        return string.Empty;
    }

    string GetDownloadUrl(DataFolder folder)
    {
        return dataZipUrl;
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

        if (string.IsNullOrWhiteSpace(root))
        {
            root = Application.platform == RuntimePlatform.Android
                ? Path.Combine("/storage/emulated/0", "Android", "data", Application.identifier, "files", "KatabasisData")
                : Path.Combine(Application.persistentDataPath, "KatabasisData");
        }

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

    static DataManager EnsureInstance()
    {
        if (instance != null)
        {
            return instance;
        }

        instance = FindAnyObjectByType<DataManager>();
        if (instance != null)
        {
            return instance;
        }

        GameObject go = new GameObject("DataManager");
        instance = go.AddComponent<DataManager>();
        return instance;
    }
}
