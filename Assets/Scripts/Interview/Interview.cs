using Depthkit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.Video;

[ExecuteAlways]
public class Interview : MonoBehaviour
{
    static readonly Dictionary<string, string> metadataCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, byte[]> posterCache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, Texture2D> posterTextureCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, double> videoLengthCache = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    Clip clip;
    VideoPlayer videoPlayer;
    Texture2D posterTex;
    bool posterTexFromSharedCache;
    VisualEffect vfx;
    bool loopPointReachedSubscribed;
    bool prepareCompletedSubscribed;
    bool previewLoadQueued;
    bool previewLoadInProgress;
    InterviewManager.InterviewData[] playbackSequence;
    int playbackIndex = -1;
    bool isResolvedPlayback;
    bool isTransitioningSequence;
    bool resourcesReleased;
    bool currentEntryIsIntro;
    string loadedPreviewBasePath;
    string loadedInterviewId;
    Coroutine previewLoadCoroutine;
    Task<string> metadataLoadTask;
    Task<byte[]> posterLoadTask;

    public string itwName;
    public string interviewId;
    List<float> cutTimes;

    string wwiseEventName;
    string currentPerson;
    Vector3 currentOffset;
    float currentAngle;
    float resumeTimeSeconds;
    bool hasResumeTime;
    bool leaveRequestedWhileListening;
    bool keepVfxAliveAfterSalleExit;
    float playbackRealtimeOrigin = -1f;
    float playbackExpectedEndTime = -1f;
    bool playbackCompletionHandled;

    const float ResumeLeadSeconds = 5f;
    const float PlaybackCompletionToleranceSeconds = 0.05f;

    [Range(0, 4)]
    public int level;
    string basePath;
    string previewBasePath;

    public bool isFocused { get; set; } = false;

    public float focusTime = 3f;
    [Range(0, 1)]
    public float progression;
    [Range(0, 1)]
    public float evaporateProg;
    [Range(0, 1)]
    public float glitchFactor;

    [Header("Preview Reveal")]
    public float revealTime = 2f;
    float revealStartTime = -1f;
    [Min(0)]
    public int previewLoadFramesBetweenHeavySteps = 1;

    [Header("Glitch Settings")]
    public float glitchTimeAroundCut = 1f;
    public float glitchTimeBeforeActivate = 1f;
    [Range(0, 1)]
    public float glitchIntensity = 1f;
    public AnimationCurve glitchCurve;

    [Header("Evaporation Settings")]
    public float evaporatePreDelay = 0.5f;
    public float evaporateTime = 3f;
    public bool shouldEvaporate = false;
    [SerializeField] float evaporatePostDelay = 5f;
    float evaporateReachedFullTime = -1f;

    Salle salle;



    [Header("Audio Settings")]
    public AudioEventRefSO loadingEvent;
    public AudioEventRefSO validateEvent;
    // public AudioEventRefSO videoEvent;
    public AudioEventRefSO evaporateEvent;
    public AudioRTPCRefSO progRTPC;

    public float videoStopFade = 1f;
    uint videoEventID = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
    bool debugWorkflow = false;

    Subtitles subtitles;

    public delegate void InterviewEvent(Interview interview);
    public event InterviewEvent OnInterviewStarted;
    public event InterviewEvent OnInterviewEnded;
    public enum State
    {
        Idle,
        Loaded,
        Playing,
        Ending
    }


    public State state;

    // Start is called once before the first execution of Update after the MonoBehaviour is created

    MainController mainController;

    DataManager dataManager;

    void Start()
    {
        dataManager = GameObject.FindAnyObjectByType<DataManager>();
        init();
        resetPlaybackState();
    }

    void OnDisable()
    {
        ReleasePlaybackResources(true);

        if (videoPlayer != null && loopPointReachedSubscribed)
        {
            videoPlayer.loopPointReached -= OnVideoFinished;
            loopPointReachedSubscribed = false;
        }

        if (videoPlayer != null && prepareCompletedSubscribed)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            prepareCompletedSubscribed = false;
        }
    }


    void init()
    {
        mainController = GameObject.FindAnyObjectByType<MainController>();
        dataManager = GameObject.FindAnyObjectByType<DataManager>();
        clip = GetComponent<Depthkit.Clip>();
        videoPlayer = GetComponent<VideoPlayer>();
        Depthkit.MeshSource meshSource = GetComponent<Depthkit.MeshSource>();
        if (meshSource != null)
        {
            meshSource.pauseDataGenerationWhenInvisible = false;
            meshSource.pausePlayerWhenInvisible = false;
        }

        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= OnVideoFinished;
            videoPlayer.loopPointReached += OnVideoFinished;
            loopPointReachedSubscribed = true;
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.prepareCompleted += OnVideoPrepared;
            prepareCompletedSubscribed = true;
        }

        vfx = GetComponentInChildren<VisualEffect>();
        salle = GetComponentInParent<Salle>();

        subtitles = FindAnyObjectByType<Subtitles>();
    }

    public void cleanup()
    {
        HandleSalleExitWhileListening();

        if (leaveRequestedWhileListening)
        {
            TracePlayback("cleanup() starting visual-only evaporation after salle exit while keeping playback continuity alive");
            StartSalleExitEvaporation();
            return;
        }

        evaporate();
    }

    void StartSalleExitEvaporation()
    {
        if (shouldEvaporate)
        {
            return;
        }

        shouldEvaporate = true;
        evaporateProg = 0f;
        evaporateReachedFullTime = -1f;
        keepVfxAliveAfterSalleExit = true;
        evaporateEvent?.evt.Post(gameObject);
    }

    public bool MatchesPreviewAssignment(InterviewManager.InterviewData data)
    {
        return string.Equals(itwName, data.depthkitPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(interviewId, data.mediaPath, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsPreviewLoadedForCurrentAssignment()
    {
        return state == State.Loaded
            && string.Equals(loadedPreviewBasePath, previewBasePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(loadedInterviewId, interviewId, StringComparison.OrdinalIgnoreCase);
    }

    bool HasPreviewAssignment()
    {
        return !string.IsNullOrWhiteSpace(previewBasePath)
            && !string.IsNullOrWhiteSpace(itwName)
            && !string.IsNullOrWhiteSpace(interviewId);
    }

    public void SetPreviewLoadQueued(bool queued)
    {
        previewLoadQueued = queued;
    }

    static string LoadMetadataCached(string path)
    {
        if (!metadataCache.TryGetValue(path, out string cached))
        {
            cached = File.ReadAllText(path);
            metadataCache[path] = cached;
        }

        return cached;
    }

    static string LoadMetadataNoCache(string path)
    {
        return File.ReadAllText(path);
    }

    static byte[] LoadPosterCached(string path)
    {
        if (!posterCache.TryGetValue(path, out byte[] cached))
        {
            cached = File.ReadAllBytes(path);
            posterCache[path] = cached;
        }

        return cached;
    }

    static byte[] LoadPosterNoCache(string path)
    {
        return File.ReadAllBytes(path);
    }

    static Texture2D DecodePosterTexture(string path, byte[] pngData)
    {
        if (pngData == null || pngData.Length == 0)
        {
            return null;
        }

        Texture2D decodedTexture = new Texture2D(2, 2);
        if (!decodedTexture.LoadImage(pngData, true))
        {
            if (Application.isPlaying)
            {
                Destroy(decodedTexture);
            }
            else
            {
                DestroyImmediate(decodedTexture);
            }

            Debug.LogWarning("Failed to decode poster texture at " + path);
            return null;
        }

        decodedTexture.name = Path.GetFileNameWithoutExtension(path) + "_Poster";
        return decodedTexture;
    }

    public static Texture2D GetOrCreatePosterTextureCached(string path, byte[] pngData = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (posterTextureCache.TryGetValue(path, out Texture2D cachedTexture) && cachedTexture != null)
        {
            return cachedTexture;
        }

        if (pngData == null)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            pngData = LoadPosterCached(path);
        }
        else
        {
            posterCache[path] = pngData;
        }

        Texture2D decodedTexture = DecodePosterTexture(path, pngData);
        if (decodedTexture != null)
        {
            posterTextureCache[path] = decodedTexture;
        }

        return decodedTexture;
    }

    public static void WarmPosterTextureCache(string path)
    {
        GetOrCreatePosterTextureCached(path);
    }


    void resetPlaybackState()
    {
        CancelInvoke(nameof(endEvaporate));
        isFocused = false;
        progression = 0;
        evaporateProg = 0;
        evaporateReachedFullTime = -1f;
        shouldEvaporate = false;
        keepVfxAliveAfterSalleExit = false;
        previewLoadQueued = false;
        previewLoadInProgress = false;
        revealStartTime = -1f;
        posterTexFromSharedCache = false;
        playbackSequence = null;
        playbackIndex = -1;
        isResolvedPlayback = false;
        isTransitioningSequence = false;
        currentEntryIsIntro = false;
        currentPerson = null;
        currentOffset = Vector3.zero;
        currentAngle = 0f;
        ResetPlaybackTimer();
        resourcesReleased = false;
        state = State.Idle;
    }

    void prepareForNextAssignment()
    {
        LogDebug("Resetting slot state for next assignment");
        ClearResumeState();
        resetPlaybackState();
        ReleasePlaybackResources(true);
        ClearAssignmentIdentity();

        InterviewManager manager = FindAnyObjectByType<InterviewManager>();
        manager?.NotifyInterviewStopped(this);
    }

    void ClearAssignmentIdentity()
    {
        itwName = string.Empty;
        interviewId = string.Empty;
        wwiseEventName = string.Empty;
        basePath = string.Empty;
        previewBasePath = string.Empty;
        metadataLoadTask = null;
        posterLoadTask = null;
        cutTimes = null;
    }

    public void ResetForPreviewAssignment()
    {
        prepareForNextAssignment();
    }

    public void ResetForFullGameReset()
    {
        if (clip == null || videoPlayer == null || vfx == null)
        {
            init();
        }

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        if (previewLoadCoroutine != null)
        {
            StopCoroutine(previewLoadCoroutine);
            previewLoadCoroutine = null;
        }

        loadingEvent?.evt.Stop(gameObject);
        subtitles?.stop();
        stopWwiseVideoEvent(true);
        ClearResumeState();
        ReleasePlaybackResources(true);
        resetPlaybackState();
        isFocused = false;
        glitchFactor = 0f;
        progRTPC?.rtpc.SetValue(gameObject, 0f);

        if (vfx != null)
        {
            vfx.enabled = false;
            vfx.SetFloat("Progression", 0f);
            vfx.SetFloat("Evaporate", 0f);
            vfx.SetFloat("GlitchFactor", 0f);
            vfx.SetFloat("SpawnRate", 0f);
        }
    }

    public void ClearResumeState()
    {
        resumeTimeSeconds = 0f;
        hasResumeTime = false;
    }

    public bool IsPreviewBusy()
    {
        return previewLoadQueued || previewLoadInProgress;
    }

    void LogPreviewLoadEvent(string source, string message, LogType logType = LogType.Log)
    {
        string details = "[InterviewPreview] " + name
            + " | source=" + source
            + " | state=" + state
            + " | person='" + (string.IsNullOrWhiteSpace(currentPerson) ? "None" : currentPerson) + "'"
            + " | interviewId='" + (string.IsNullOrWhiteSpace(interviewId) ? "None" : interviewId) + "'"
            + " | depthkit='" + (string.IsNullOrWhiteSpace(itwName) ? "None" : itwName) + "'"
            + " | previewBasePath='" + (string.IsNullOrWhiteSpace(previewBasePath) ? "None" : previewBasePath) + "'"
            + " | queued=" + previewLoadQueued
            + " | inProgress=" + previewLoadInProgress
            + " | active=" + isActiveAndEnabled
            + " | " + message;

        switch (logType)
        {
            case LogType.Warning:
                Debug.LogWarning(details, this);
                break;
            case LogType.Error:
            case LogType.Assert:
            case LogType.Exception:
                Debug.LogError(details, this);
                break;
            default:
                Debug.Log(details, this);
                break;
        }
    }

    void TracePlayback(string message)
    {
        Debug.Log("[InterviewTrace] " + name + " | state=" + state + " | interviewId='" + interviewId + "' | " + message, this);
    }

    void ResetPlaybackTimer()
    {
        playbackRealtimeOrigin = -1f;
        playbackExpectedEndTime = -1f;
        playbackCompletionHandled = false;
    }

    bool HasPendingSequenceEntry()
    {
        return playbackSequence != null
            && playbackIndex >= 0
            && playbackIndex < playbackSequence.Length - 1;
    }

    void TryArmPlaybackTimer()
    {
        if (playbackRealtimeOrigin < 0f || playbackExpectedEndTime >= 0f)
        {
            return;
        }

        double knownVideoLength = GetKnownVideoLength();
        if (knownVideoLength <= 0d)
        {
            return;
        }

        playbackExpectedEndTime = playbackRealtimeOrigin + (float)knownVideoLength;
        TracePlayback("Playback timer armed. realtimeOrigin=" + playbackRealtimeOrigin.ToString("0.00") + ", knownVideoLength=" + knownVideoLength.ToString("0.00") + ", expectedEndTime=" + playbackExpectedEndTime.ToString("0.00"));
        LogDebug("Armed playback timer for '" + interviewId + "' at t=" + playbackExpectedEndTime.ToString("0.00"));
    }

    void StartPlaybackTimer(float startTime)
    {
        playbackCompletionHandled = false;
        playbackRealtimeOrigin = Time.time - Mathf.Max(0f, startTime);
        playbackExpectedEndTime = -1f;
        TracePlayback("Starting playback timer with startTime=" + startTime.ToString("0.00") + ", realtimeOrigin=" + playbackRealtimeOrigin.ToString("0.00"));
        TryArmPlaybackTimer();

        if (playbackExpectedEndTime < 0f)
        {
            TracePlayback("Playback timer waiting for video length. Calling PrepareVideoMetadata().");
            PrepareVideoMetadata();
        }
    }

    bool UpdateTimedPlaybackCompletion()
    {
        if (state != State.Playing || playbackCompletionHandled)
        {
            return false;
        }

        if (playbackExpectedEndTime < 0f)
        {
            TryArmPlaybackTimer();
            return false;
        }

        if (Time.time < playbackExpectedEndTime + PlaybackCompletionToleranceSeconds)
        {
            return false;
        }

        TracePlayback("Playback timer reached completion threshold at Time.time=" + Time.time.ToString("0.00") + ", expectedEndTime=" + playbackExpectedEndTime.ToString("0.00"));
        CompleteCurrentPlayback("timer");
        return true;
    }

    bool CanInteractInCurrentSalle()
    {
        return Application.isPlaying
            && mainController != null
            && salle != null
            && mainController.isInSalle(salle);
    }

    void CancelPendingInteraction()
    {
        isFocused = false;

        if (progression <= 0f)
        {
            return;
        }

        progression = 0f;
        loadingEvent?.evt.Stop(gameObject);
        progRTPC?.rtpc.SetValue(gameObject, progression);
    }

    bool ShouldPlayDepthkitVideo()
    {
        bool isStillInSalle = salle == null || mainController == null || mainController.isInSalle(salle);
        return isStillInSalle
            && isActiveAndEnabled
            && videoPlayer != null
            && videoPlayer.isActiveAndEnabled
            && clip != null
            && clip.isActiveAndEnabled
            && (vfx == null || vfx.enabled);
    }

    void WarmPreviewDataAsync()
    {
        if (string.IsNullOrWhiteSpace(previewBasePath))
        {
            return;
        }

        string metadataPath = previewBasePath + ".txt";
        if (metadataLoadTask == null && File.Exists(metadataPath))
        {
            metadataLoadTask = Task.Run(() => LoadMetadataNoCache(metadataPath));
        }

        string posterPath = previewBasePath + ".png";
        if (posterLoadTask == null && File.Exists(posterPath))
        {
            posterLoadTask = Task.Run(() => LoadPosterNoCache(posterPath));
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (!Application.isPlaying) return;

        if (clip == null || videoPlayer == null || vfx == null) init();


        if (Application.isPlaying)
        {
            if (salle != null)
            {
                bool shouldEnable = keepVfxAliveAfterSalleExit || mainController.isInSalle(salle);
                if (shouldEnable != vfx.enabled)
                {
                    show(shouldEnable);
                }
            }
        }
        else
        {
            //View port camera distance
#if UNITY_EDITOR
            Camera sceneCam = UnityEditor.SceneView.lastActiveSceneView.camera;
            vfx.enabled = Vector3.Distance(sceneCam.transform.position, transform.position) < 20f;
#endif
        }

        if (evaporateProg >= 1f)
        {
            if (evaporateReachedFullTime < 0f)
            {
                evaporateReachedFullTime = Time.time;
            }

            if (Time.time >= evaporateReachedFullTime + evaporatePostDelay)
            {
                endEvaporate();
            }
            return;
        }

        if (UpdateTimedPlaybackCompletion())
        {
            return;
        }

        bool canInteractInCurrentSalle = CanInteractInCurrentSalle();
        if (!canInteractInCurrentSalle)
        {
            CancelPendingInteraction();
        }

        if (progression < 1 && !mainController.editMode && canInteractInCurrentSalle)
        {
            if (Application.isPlaying)
            {
                float focusProg = Time.deltaTime * (isFocused ? 1 : -1) / focusTime;

                float newProg = Mathf.Clamp01(progression + focusProg);

                if (newProg != progression)
                {
                    if (newProg > 0 && progression == 0)
                    {
                        loadingEvent?.evt.Post(gameObject);
                    }
                    else if (newProg == 0 && progression > 0)
                    {
                        loadingEvent?.evt.Stop(gameObject);
                    }

                    progression = newProg;
                    progRTPC?.rtpc.SetValue(gameObject, progression);

                    if (progression >= 1)
                    {
                        resolveAndPlay();
                        loadingEvent?.evt.Stop(gameObject);
                        Debug.Log("Posting validate event");
                        validateEvent?.evt.Post(gameObject);
                    }
                }
            }
        }
        else
        {

            if (videoPlayer.isPlaying)
            {
                if (!shouldEvaporate)
                {
                    bool hasPendingSequenceEntry = HasPendingSequenceEntry();
                    if (!hasPendingSequenceEntry && videoPlayer.time > videoPlayer.length - evaporatePreDelay)
                    {
                        evaporate();
                    }
                }
            }
        }

        if (shouldEvaporate && evaporateProg < 1)
        {
            float evapProg = Time.deltaTime / evaporateTime;
            evaporateProg = Mathf.Clamp(evaporateProg + evapProg, 0, 1);
            if (evaporateProg >= 1f && evaporateReachedFullTime < 0f)
            {
                evaporateReachedFullTime = Time.time;
            }
        }

        float diffToClosestCut = getDiffToClosestCut();
        float glitchCutF = diffToClosestCut == -1 ? 0f : 1 - Mathf.Clamp01(diffToClosestCut / glitchTimeAroundCut);
        float progTime = progression == 1 && videoPlayer.isPlaying ? 0f : progression * focusTime;
        float glitchStartTime = Mathf.Clamp01(focusTime - glitchTimeBeforeActivate);
        float glitchActivateF = (progTime - glitchStartTime) / glitchTimeBeforeActivate;

        glitchFactor = glitchIntensity * glitchCurve.Evaluate(Mathf.Max(glitchCutF, glitchActivateF));

        float spawnRate = 1f;
        if (state == State.Loaded && revealStartTime >= 0f)
        {
            spawnRate = Mathf.Clamp01((Time.time - revealStartTime) / Mathf.Max(0.01f, revealTime));
        }

        vfx.SetFloat("Progression", progression);
        vfx.SetFloat("Evaporate", evaporateProg);
        vfx.SetFloat("GlitchFactor", glitchFactor);
        vfx.SetFloat("SpawnRate", spawnRate);
    }

    void show(bool shouldShow)
    {
        LogDebug("Show(" + shouldShow + ") for salle " + (salle != null ? salle.name : "None"));
        if (shouldShow)
        {
            vfx.enabled = true;
            if (!previewLoadQueued && !previewLoadInProgress)
            {
                BeginPreviewLoad();
            }
        }
        else
        {
            if (shouldEvaporate || evaporateProg > 0f || state == State.Ending)
            {
                vfx.enabled = true;
                return;
            }

            HandleSalleExitWhileListening();
            if (leaveRequestedWhileListening && !keepVfxAliveAfterSalleExit)
            {
                TracePlayback("show(false) kept playback running after salle exit; hiding VFX without moving to Ending state");
                vfx.enabled = false;
            }
            else
            {
                vfx.enabled = false;
                ClearResumeState();
                ReleasePlaybackResources(true);
                resetPlaybackState();
            }
        }

    }

    public void set(InterviewManager.InterviewData data)
    {
        if (!gameObject.activeSelf)
        {
            LogPreviewLoadEvent("assign", "Reactivating disabled interview slot for a new assignment");
            gameObject.SetActive(true);
        }

        if (vfx == null)
        {
            init();
        }
        this.itwName = data.depthkitPath;
        this.interviewId = data.mediaPath;
        this.currentPerson = data.person;
        this.currentOffset = data.offset;
        this.currentAngle = data.angle;
        this.currentEntryIsIntro = data.isIntro;
        this.level = data.level;
        vfx.SetVector3("Offset", data.offset);
        vfx.SetFloat("OffsetAngle", data.angle);

        if (playbackSequence == null || playbackIndex < 0 || playbackIndex >= playbackSequence.Length)
        {
            isResolvedPlayback = false;
        }

        if (clip == null)
        {
            init();
        }

        basePath = BuildMediaBasePath(itwName);
        previewBasePath = BuildPreviewBasePath(itwName);
        videoPlayer.url = BuildVideoUrl(interviewId);
        metadataLoadTask = null;
        posterLoadTask = null;
        WarmPreviewDataAsync();

        cutTimes = data.cutTimes != null ? new List<float>(data.cutTimes) : new List<float>();
        wwiseEventName = Path.GetFileNameWithoutExtension(data.mediaPath);

        LogDebug("Assigned interview slot -> depthkitPath='" + itwName + "', mediaPath='" + interviewId + "', wwiseEventName = " + wwiseEventName + ", level=" + level + ", basePath='" + basePath + "', previewBasePath='" + previewBasePath + "', videoUrl='" + videoPlayer.url + "'");
    }

    string BuildMediaBasePath(string mediaPath)
    {
        return dataManager.GetFolderPath(DataManager.DataFolder.Interviews, mediaPath);
    }

    string BuildVideoUrl(string mediaPath)
    {
        return dataManager.GetFileUrl(DataManager.DataFolder.Interviews, mediaPath + ".mp4");
    }

    string BuildPreviewBasePath(string depthkitPath)
    {
        string normalizedPath = (depthkitPath ?? string.Empty).Replace("\\", "/").Trim('/');
        string[] pathParts = normalizedPath.Split('/');
        string personFolder = pathParts.Length > 0 && !string.IsNullOrWhiteSpace(pathParts[0]) ? pathParts[0] : normalizedPath;
        return BuildMediaBasePath(depthkitPath);
    }

    double GetKnownVideoLength()
    {
        if (videoPlayer != null && videoPlayer.length > 0d)
        {
            if (!string.IsNullOrWhiteSpace(interviewId))
            {
                videoLengthCache[interviewId] = videoPlayer.length;
            }

            return videoPlayer.length;
        }

        if (!string.IsNullOrWhiteSpace(interviewId) && videoLengthCache.TryGetValue(interviewId, out double cachedLength))
        {
            return cachedLength;
        }

        return 0d;
    }

    void CacheCurrentVideoLength()
    {
        GetKnownVideoLength();
    }

    void PrepareVideoMetadata()
    {
        if (videoPlayer == null || string.IsNullOrWhiteSpace(interviewId) || string.IsNullOrWhiteSpace(videoPlayer.url))
        {
            return;
        }

        if (videoLengthCache.ContainsKey(interviewId) || videoPlayer.isPrepared)
        {
            CacheCurrentVideoLength();
            return;
        }

        videoPlayer.Prepare();
    }

    public void load()
    {
        if (previewLoadCoroutine != null)
        {
            StopCoroutine(previewLoadCoroutine);
            previewLoadCoroutine = null;
        }

        previewLoadQueued = false;
        previewLoadInProgress = false;

        if (!HasPreviewAssignment())
        {
            LogPreviewLoadEvent("sync", "Skipped before metadata load started because no preview assignment is set", LogType.Warning);
            return;
        }

        if (IsPreviewLoadedForCurrentAssignment())
        {
            LogPreviewLoadEvent("sync", "Skipped because preview is already loaded for the current assignment");
            return;
        }

        LogPreviewLoadEvent("sync", "Starting preview load");

        ReleasePosterTexture();
        if(clip == null)
        {
            LogPreviewLoadEvent("sync", "Aborted before metadata load because clip is null", LogType.Warning);
            return;
        }
        clip.metadataFile = null;
        clip.poster = null;
        if (videoPlayer != null)
        {
            videoPlayer.Stop();
            videoPlayer.playOnAwake = false;
            videoPlayer.waitForFirstFrame = true;
        }

        string metadataPath = previewBasePath + ".txt";
        if (!File.Exists(metadataPath))
        {
            LogPreviewLoadEvent("sync", "Aborted before metadata load because metadata file is missing at '" + metadataPath + "'", LogType.Warning);
            return;
        }

        try
        {
            string metaData = LoadMetadataCached(metadataPath);
            bool result = clip.LoadMetadata(metaData);
            LogPreviewLoadEvent("sync", "Metadata load finished with result=" + result + " from '" + metadataPath + "'", result ? LogType.Log : LogType.Warning);

            string posterPath = previewBasePath + ".png";
            if (File.Exists(posterPath))
            {
                posterTex = GetOrCreatePosterTextureCached(posterPath);
                posterTexFromSharedCache = posterTex != null;
                clip.poster = posterTex;
            }
            else
            {
                LogPreviewLoadEvent("sync", "Poster is missing at '" + posterPath + "'", LogType.Warning);
            }

            LogPreviewLoadEvent("sync", "Preview load completed. posterAssigned=" + (clip.poster != null) + ", videoPrepared=" + (videoPlayer != null && videoPlayer.isPrepared) + ", clipSetup=" + (clip != null && clip.isSetup));

            revealStartTime = Time.time;
            loadedPreviewBasePath = previewBasePath;
            loadedInterviewId = interviewId;
            state = State.Loaded;
        }
        catch (Exception ex)
        {
            LogPreviewLoadEvent("sync", "Metadata load threw an exception for '" + metadataPath + "': " + ex.Message, LogType.Error);
        }
    }

    public void BeginPreviewLoad()
    {
        if (!HasPreviewAssignment())
        {
            previewLoadQueued = false;
            previewLoadInProgress = false;
            LogPreviewLoadEvent("begin", "Skipped before starting because no preview assignment is set", LogType.Warning);
            return;
        }

        if (!Application.isPlaying)
        {
            LogPreviewLoadEvent("begin", "Application is not playing; falling back to synchronous preview load");
            load();
            return;
        }

        if (previewLoadInProgress || IsPreviewLoadedForCurrentAssignment())
        {
            string reason = previewLoadInProgress
                ? "Skipped because a preview load is already in progress"
                : "Skipped because preview is already loaded for the current assignment";
            LogPreviewLoadEvent("begin", reason);
            return;
        }

        if (!isActiveAndEnabled)
        {
            LogPreviewLoadEvent("begin", "Slot is inactive; falling back to synchronous preview load");
            load();
            return;
        }

        if (previewLoadCoroutine != null)
        {
            StopCoroutine(previewLoadCoroutine);
        }

        LogPreviewLoadEvent("begin", "Starting asynchronous preview load coroutine");
        previewLoadCoroutine = StartCoroutine(loadPreviewAsync());
    }

    System.Collections.IEnumerator loadPreviewAsync()
    {
        previewLoadQueued = false;
        previewLoadInProgress = true;

        if (!HasPreviewAssignment())
        {
            LogPreviewLoadEvent("async", "Cancelled before metadata load started because no preview assignment is set", LogType.Warning);
            previewLoadInProgress = false;
            previewLoadCoroutine = null;
            yield break;
        }

        if (IsPreviewLoadedForCurrentAssignment())
        {
            LogPreviewLoadEvent("async", "Cancelled because preview is already loaded for the current assignment");
            previewLoadInProgress = false;
            previewLoadCoroutine = null;
            yield break;
        }

        LogPreviewLoadEvent("async", "Coroutine entered; preparing metadata load");

        ReleasePosterTexture();
        clip.metadataFile = null;
        clip.poster = null;
        if (videoPlayer != null)
        {
            videoPlayer.Stop();
            videoPlayer.playOnAwake = false;
            videoPlayer.waitForFirstFrame = true;
        }

        string metadataPath = previewBasePath + ".txt";
        if (!File.Exists(metadataPath))
        {
            LogPreviewLoadEvent("async", "Aborted before metadata load because metadata file is missing at '" + metadataPath + "'", LogType.Warning);
            previewLoadInProgress = false;
            previewLoadCoroutine = null;
            yield break;
        }

        WarmPreviewDataAsync();
        if (metadataLoadTask == null)
        {
            LogPreviewLoadEvent("async", "Metadata background task did not start; using synchronous fallback for '" + metadataPath + "'", LogType.Warning);
        }

        while (metadataLoadTask != null && !metadataLoadTask.IsCompleted)
        {
            yield return null;
        }

        if (metadataLoadTask != null)
        {
            if (metadataLoadTask.IsFaulted)
            {
                LogPreviewLoadEvent("async", "Metadata background task faulted for '" + metadataPath + "': " + metadataLoadTask.Exception?.GetBaseException().Message, LogType.Error);
            }
            else if (metadataLoadTask.IsCanceled)
            {
                LogPreviewLoadEvent("async", "Metadata background task was canceled for '" + metadataPath + "'", LogType.Warning);
            }
            else
            {
                LogPreviewLoadEvent("async", "Metadata background task completed with status=" + metadataLoadTask.Status + " for '" + metadataPath + "'");
            }
        }

        string metaData;
        try
        {
            metaData = metadataLoadTask != null && metadataLoadTask.Status == TaskStatus.RanToCompletion
                ? metadataLoadTask.Result
                : LoadMetadataCached(metadataPath);
            metadataCache[metadataPath] = metaData;
        }
        catch (Exception ex)
        {
            LogPreviewLoadEvent("async", "Metadata read failed for '" + metadataPath + "': " + ex.Message, LogType.Error);
            previewLoadInProgress = false;
            previewLoadCoroutine = null;
            yield break;
        }

        for (int i = 0; i < previewLoadFramesBetweenHeavySteps; i++)
        {
            yield return null;
        }

        bool result;
        try
        {
            result = clip.LoadMetadata(metaData);
        }
        catch (Exception ex)
        {
            LogPreviewLoadEvent("async", "clip.LoadMetadata threw for '" + metadataPath + "': " + ex.Message, LogType.Error);
            previewLoadInProgress = false;
            previewLoadCoroutine = null;
            yield break;
        }

        LogPreviewLoadEvent("async", "Metadata load finished with result=" + result + " from '" + metadataPath + "'", result ? LogType.Log : LogType.Warning);
        for (int i = 0; i < previewLoadFramesBetweenHeavySteps + 1; i++)
        {
            yield return null;
        }

        string posterPath = previewBasePath + ".png";
        if (File.Exists(posterPath))
        {
            while (posterLoadTask != null && !posterLoadTask.IsCompleted)
            {
                yield return null;
            }

            byte[] pngData = posterLoadTask != null && posterLoadTask.Status == TaskStatus.RanToCompletion
                ? posterLoadTask.Result
                : LoadPosterCached(posterPath);
            posterCache[posterPath] = pngData;

            for (int i = 0; i < previewLoadFramesBetweenHeavySteps; i++)
            {
                yield return null;
            }

            posterTex = GetOrCreatePosterTextureCached(posterPath, pngData);
            posterTexFromSharedCache = posterTex != null;
            clip.poster = posterTex;
        }
        else
        {
            LogPreviewLoadEvent("async", "Poster is missing at '" + posterPath + "'", LogType.Warning);
        }

        LogPreviewLoadEvent("async", "Preview load completed. posterAssigned=" + (clip.poster != null) + ", videoPrepared=" + (videoPlayer != null && videoPlayer.isPrepared) + ", clipSetup=" + (clip != null && clip.isSetup));

        revealStartTime = Time.time;
        loadedPreviewBasePath = previewBasePath;
        loadedInterviewId = interviewId;
        state = State.Loaded;
        previewLoadInProgress = false;
        previewLoadCoroutine = null;
    }

    public void play()
    {
        LogDebug("Starting playback for mediaPath='" + interviewId + "' depthkitPath='" + itwName + "'");
        TracePlayback("play() called. playbackIndex=" + playbackIndex + ", hasSequence=" + (playbackSequence != null) + ", hasResumeTime=" + hasResumeTime + ", leaveRequestedWhileListening=" + leaveRequestedWhileListening);
        leaveRequestedWhileListening = false;
        OnInterviewStarted?.Invoke(this);
        InterviewManager manager = FindAnyObjectByType<InterviewManager>();
        manager?.NotifyInterviewStarted(this);

        InterviewManager.InterviewData? activeEntry = GetCurrentPlaybackEntry();
        bool isIntroEntry = activeEntry.HasValue && activeEntry.Value.isIntro;
        if (activeEntry.HasValue)
        {
            manager?.NotifyPlaybackEntryStarted(this, activeEntry.Value);
        }
        else if (!string.IsNullOrWhiteSpace(currentPerson))
        {
            isIntroEntry = manager != null && !manager.HasIntroStarted(currentPerson);
            if (isIntroEntry)
            {
                manager?.MarkIntroStarted(currentPerson);
            }
        }

        currentEntryIsIntro = isIntroEntry;

        revealStartTime = -1f;
        vfx.SetFloat("SpawnRate", 1f);

        Debug.Log("Start Wwise Event from play");
        videoEventID = AkUnitySoundEngine.PostEvent(wwiseEventName, gameObject);
    TracePlayback("Posted Wwise event '" + wwiseEventName + "' with playingId=" + videoEventID);

        double knownVideoLength = GetKnownVideoLength();
    TracePlayback("Known video length before playback start: " + knownVideoLength.ToString("0.00"));

        float startTime = 0f;
        bool applyResume = hasResumeTime;
        if (applyResume)
        {
            float maxResumeTime = knownVideoLength > 0d
                ? Mathf.Max(0f, (float)knownVideoLength - 0.01f)
                : Mathf.Max(0f, resumeTimeSeconds);
            startTime = Mathf.Clamp(resumeTimeSeconds, 0f, maxResumeTime);
            if (startTime > 0f)
            {
                Debug.Log("Resuming video at " + startTime + " seconds");
                if (videoPlayer != null)
                {
                    videoPlayer.time = startTime;
                }
                if (videoEventID != AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
                {
                    Debug.Log("Seeking Wwise Event to " + startTime + " seconds");
                    AkUnitySoundEngine.SeekOnEvent(wwiseEventName, gameObject, Mathf.RoundToInt(startTime * 1000f));
                }
            }
        }

        bool playDepthkitVideo = ShouldPlayDepthkitVideo();
        bool isStillInSalle = salle == null || mainController == null || mainController.isInSalle(salle);
        TracePlayback("Playback mode decision: playDepthkitVideo=" + playDepthkitVideo
            + ", isStillInSalle=" + isStillInSalle
            + ", isActiveAndEnabled=" + isActiveAndEnabled
            + ", videoPlayerActive=" + (videoPlayer != null && videoPlayer.isActiveAndEnabled)
            + ", clipActive=" + (clip != null && clip.isActiveAndEnabled)
            + ", vfxEnabled=" + (vfx == null || vfx.enabled));
        if (playDepthkitVideo)
        {
            videoPlayer.Play();
            TracePlayback("VideoPlayer.Play() called. isPlaying=" + (videoPlayer != null && videoPlayer.isPlaying));
        }
        else if (videoPlayer != null)
        {
            TracePlayback("Skipping VideoPlayer playback and launching audio/subtitles only");
            LogDebug("Skipping VideoPlayer playback and launching audio/subtitles only");
            videoPlayer.Stop();
        }

        StartPlaybackTimer(startTime);

        state = State.Playing;
        Debug.Log($"Starting interview playback: slot='{name}', person='{currentPerson}', media='{interviewId}', depthkit='{itwName}', offset={currentOffset}, angle={currentAngle}, intro={currentEntryIsIntro}", this);

        if (subtitles != null)
        {
            string languageSuffix = mainController != null ? mainController.getLanguageSuffix() : "";
            string subtitlePath = interviewId + languageSuffix + ".srt";
            Debug.Log("Starting subtitles with path '" + subtitlePath + "' and startTime=" + startTime);
            TracePlayback("Calling subtitles.play with path='" + subtitlePath + "' and startTime=" + startTime.ToString("0.00"));
            subtitles.play(subtitlePath, startTime);
        }
        else
        {
            TracePlayback("Subtitles component not found. No subtitle playback will start.");
        }
    }

    public void StopPlaybackForAnotherInterview()
    {
        StopPlaybackForInterviewChange(true);
    }

    public void StopPlaybackForInterviewChange(bool stopWwiseEvent)
    {
        if (videoPlayer == null)
        {
            init();
        }

        if ((videoPlayer == null || !videoPlayer.isPlaying)
            && videoEventID == AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
        {
            return;
        }

        LogDebug("Stopping playback because another interview started");
        FinalizeInterruptedPlayback(stopWwiseEvent);
    }

    public void evaporate()
    {

        if (evaporateProg > 0 || shouldEvaporate)
        {
            return;
        }

        LogDebug("Evaporating interview '" + interviewId + "'");
        shouldEvaporate = true;
        evaporateReachedFullTime = -1f;
        keepVfxAliveAfterSalleExit = true;

        evaporateEvent?.evt.Post(gameObject);
        state = State.Ending;
        OnInterviewEnded?.Invoke(this);
    }

    void stopWwiseVideoEvent(bool forceStop = false)
     //Stop video
    {
        if (string.IsNullOrWhiteSpace(wwiseEventName))
        {
            return;
        }

        Debug.Log("Stop Wwise Event " + videoEventID);
            // Stop the specific instance
            // AkCurveInterpolation defines the fade curve (e.g., Linear, Sine)
            AkUnitySoundEngine.ExecuteActionOnEvent(wwiseEventName, AkActionOnEventType.AkActionOnEventType_Stop,
                                                gameObject,
                                                Mathf.RoundToInt(videoStopFade * 1000f),
                                                AkCurveInterpolation.AkCurveInterpolation_Linear);

        videoEventID = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
        leaveRequestedWhileListening = false;

    }


    void endEvaporate()
    {
        if (!gameObject.activeSelf)
        {
            return;
        }

        if (leaveRequestedWhileListening && state == State.Playing)
        {
            TracePlayback("endEvaporate() completed visual-only salle-exit evaporation; hiding VFX while playback continues");
            keepVfxAliveAfterSalleExit = false;
            shouldEvaporate = false;
            evaporateProg = 0f;
            evaporateReachedFullTime = -1f;
            if (vfx != null)
            {
                vfx.enabled = false;
            }
            return;
        }

        keepVfxAliveAfterSalleExit = false;
        if (videoPlayer != null) videoPlayer.Stop();
        ReleasePlaybackResources(true);
        LogPreviewLoadEvent("lifecycle", "Disabling interview slot after evaporation completed");
        gameObject.SetActive(false);
    }
    void resolveAndPlay()
    {
        if (!CanInteractInCurrentSalle())
        {
            TracePlayback("resolveAndPlay() aborted because the player is not currently inside this salle");
            CancelPendingInteraction();
            return;
        }

        LogDebug("Resolving playback at validation time for slot '" + name + "'");
        TracePlayback("resolveAndPlay() called. isResolvedPlayback=" + isResolvedPlayback + ", current interviewId='" + interviewId + "'");
        if (!isResolvedPlayback && Application.isPlaying)
        {
            InterviewManager manager = FindAnyObjectByType<InterviewManager>();
            if (manager != null && manager.TryResolvePlaybackForSlot(this, out InterviewManager.ResolvedInterviewPlayback playback))
            {
                playbackSequence = playback.sequence;
                playbackIndex = 0;
                isResolvedPlayback = true;
                TracePlayback("Resolved playback sequence with length=" + (playbackSequence != null ? playbackSequence.Length : 0) + ", firstEntry='" + (playbackSequence != null && playbackSequence.Length > 0 ? playbackSequence[0].mediaPath : "<none>") + "'");
                SkipStartedIntroAtSequenceStart();
            }
            else
            {
                TracePlayback("TryResolvePlaybackForSlot returned false");
            }
        }

        if (!isResolvedPlayback)
        {
            LogDebug("No resolved sequence found, using current assignment directly");
            playbackSequence = null;
            playbackIndex = -1;
            play();
            return;
        }

        if (!HasActivePlaybackEntry())
        {
            TracePlayback("Resolved playlist has no active entry after intro skipping. playbackIndex=" + playbackIndex + ", sequenceLength=" + (playbackSequence != null ? playbackSequence.Length : 0));
            LogDebug("Resolved playlist is empty after intro skipping");
            return;
        }

        StartCurrentPlaybackEntry();
    }

    void loadPlaybackEntry(InterviewManager.InterviewData data)
    {
        LogDebug("Loading playback entry -> person='" + data.person + "', depthkitPath='" + data.depthkitPath + "', mediaPath='" + data.mediaPath + "', intro=" + data.isIntro);
        set(data);
        load();
    }

    bool tryAdvanceSequence()
    {
        if (playbackSequence == null)
        {
            return false;
        }

        int nextIndex = playbackIndex + 1;
        if (nextIndex < 0 || nextIndex >= playbackSequence.Length)
        {
            return false;
        }

        LogDebug("Advancing playback sequence to index " + nextIndex + " / " + playbackSequence.Length);
        isTransitioningSequence = true;
        playbackIndex = nextIndex;
        shouldEvaporate = false;
        evaporateProg = 0;
        StartCurrentPlaybackEntry();
        isTransitioningSequence = false;
        return true;
    }

    bool HasActivePlaybackEntry()
    {
        return playbackSequence != null
            && playbackIndex >= 0
            && playbackIndex < playbackSequence.Length;
    }

    void StartCurrentPlaybackEntry()
    {
        if (!HasActivePlaybackEntry())
        {
            TracePlayback("StartCurrentPlaybackEntry() aborted because there is no active playback entry");
            return;
        }

        InterviewManager.InterviewData entry = playbackSequence[playbackIndex];
        TracePlayback("Starting playlist entry " + (playbackIndex + 1) + " / " + playbackSequence.Length + ": person='" + entry.person + "', mediaPath='" + entry.mediaPath + "', intro=" + entry.isIntro + ", depthkitPath='" + entry.depthkitPath + "'");
        LogDebug("Starting playlist entry " + (playbackIndex + 1) + " / " + playbackSequence.Length + " -> mediaPath='" + entry.mediaPath + "'");
        loadPlaybackEntry(entry);
        play();
    }

    void SkipStartedIntroAtSequenceStart()
    {
        if (playbackSequence == null || playbackSequence.Length == 0)
        {
            return;
        }

        InterviewManager manager = FindAnyObjectByType<InterviewManager>();
        bool shouldResumeCurrentIntro = currentEntryIsIntro;
        while (playbackIndex >= 0 && playbackIndex < playbackSequence.Length)
        {
            InterviewManager.InterviewData data = playbackSequence[playbackIndex];
            if (!data.isIntro || manager == null || !manager.HasIntroStarted(data.person) || shouldResumeCurrentIntro)
            {
                break;
            }

            playbackIndex++;
        }

        if (playbackIndex >= playbackSequence.Length)
        {
            playbackSequence = null;
            playbackIndex = -1;
            isResolvedPlayback = false;
            currentEntryIsIntro = false;
        }
    }

    InterviewManager.InterviewData? GetCurrentPlaybackEntry()
    {
        if (playbackSequence != null && playbackIndex >= 0 && playbackIndex < playbackSequence.Length)
        {
            return playbackSequence[playbackIndex];
        }

        return null;
    }

    void CaptureResumeTimeForCurrentEntry()
    {
        if (videoPlayer == null)
        {
            return;
        }

        double currentTime = videoPlayer.time;
        if (currentTime <= 0d)
        {
            return;
        }

        resumeTimeSeconds = Mathf.Max(0f, (float)currentTime - ResumeLeadSeconds);
        hasResumeTime = true;
    }

    bool IsActivelyListening()
    {
        return state == State.Playing
            && ((videoPlayer != null && videoPlayer.isPlaying)
                || videoEventID != AkUnitySoundEngine.AK_INVALID_PLAYING_ID);
    }

    void HandleSalleExitWhileListening()
    {
        if (!IsActivelyListening())
        {
            leaveRequestedWhileListening = false;
            return;
        }

        TracePlayback("HandleSalleExitWhileListening() stopping video because the player left the salle. hasPendingSequenceEntry=" + HasPendingSequenceEntry() + ", videoIsPlaying=" + (videoPlayer != null && videoPlayer.isPlaying) + ", audioPlaying=" + (videoEventID != AkUnitySoundEngine.AK_INVALID_PLAYING_ID));
        FinalizeInterruptedPlayback(true);
    }

    void FinalizeInterruptedPlayback(bool stopWwiseEvent)
    {
        TracePlayback("FinalizeInterruptedPlayback() marking current playback as viewed after interruption");

        InterviewManager manager = FindAnyObjectByType<InterviewManager>();
        InterviewManager.InterviewData[] interruptedSequence = playbackSequence;
        string interruptedInterviewId = interviewId;

        ClearResumeState();
        ResetPlaybackTimer();
        leaveRequestedWhileListening = false;
        loadingEvent?.evt.Stop(gameObject);
        subtitles?.stop();

        if (stopWwiseEvent)
        {
            stopWwiseVideoEvent(true);
        }
        else
        {
            videoEventID = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
        }

        if (videoPlayer != null)
        {
            videoPlayer.Stop();
        }

        ReleasePlaybackResources(false);
        shouldEvaporate = false;
        evaporateProg = 0;
        evaporateReachedFullTime = -1f;
        keepVfxAliveAfterSalleExit = false;
        progression = 0;
        playbackSequence = null;
        playbackIndex = -1;
        isResolvedPlayback = false;
        isTransitioningSequence = false;
        state = State.Loaded;

        manager?.MarkSlotConsumed(this);
        manager?.NotifyInterviewStopped(this);

        if (interruptedSequence != null && interruptedSequence.Length > 0)
        {
            manager?.MarkInterviewSequenceVisited(interruptedSequence);
        }
        else if (!string.IsNullOrWhiteSpace(interruptedInterviewId))
        {
            manager?.MarkInterviewVisited(interruptedInterviewId);
        }
    }


    void CompleteCurrentPlayback(string completionSource)
    {
        if (playbackCompletionHandled)
        {
            return;
        }

        playbackCompletionHandled = true;
        TracePlayback("CompleteCurrentPlayback() entered via " + completionSource + ". hasPendingSequenceEntry=" + HasPendingSequenceEntry() + ", playbackIndex=" + playbackIndex + ", sequenceLength=" + (playbackSequence != null ? playbackSequence.Length : 0));
        LogDebug("Playback completed via " + completionSource + " for mediaPath='" + interviewId + "'");
        GetKnownVideoLength();
        string previousInterviewId = interviewId;
        InterviewManager.InterviewData[] completedSequence = playbackSequence;
        ClearResumeState();

        if (tryAdvanceSequence())
        {
            TracePlayback("Sequence advanced successfully after completion via " + completionSource);
            return;
        }

        TracePlayback("No further sequence entry to advance to after completion via " + completionSource);

        ResetPlaybackTimer();

        InterviewManager consumedManager = FindAnyObjectByType<InterviewManager>();
        consumedManager?.MarkSlotConsumed(this);

        if (videoPlayer != null)
        {
            videoPlayer.Stop();

        }

        videoEventID = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;
        leaveRequestedWhileListening = false;

        ReleasePlaybackResources(false);
        consumedManager?.NotifyInterviewStopped(this);

        if (completedSequence != null && completedSequence.Length > 0)
        {
            InterviewManager manager = consumedManager;
            if (manager != null)
            {
                manager.MarkInterviewSequenceVisited(completedSequence);
            }
        }
        else if (!string.IsNullOrWhiteSpace(interviewId))
        {
            InterviewManager manager = consumedManager;
            if (manager != null)
            {
                manager.MarkInterviewVisited(interviewId);
            }
        }

        if (!isTransitioningSequence && string.Equals(interviewId, previousInterviewId, System.StringComparison.OrdinalIgnoreCase))
        {
            evaporate();
        }
    }


    void OnVideoFinished(VideoPlayer vp)
    {
        CompleteCurrentPlayback("video callback");
    }

    void OnVideoPrepared(VideoPlayer vp)
    {
        CacheCurrentVideoLength();
        TracePlayback("OnVideoPrepared fired. videoLength=" + (vp != null ? vp.length.ToString("0.00") : "<null>") + ", isPrepared=" + (vp != null && vp.isPrepared));
        TryArmPlaybackTimer();
    }

    float getDiffToClosestCut()
    {
        if (!videoPlayer.isPlaying)
        {
            return -1;
        }

        double knownVideoLength = GetKnownVideoLength();
        float t = (float)videoPlayer.time;
        float minDiff = float.MaxValue;
        float closestCut = -1;
        foreach (float cut in cutTimes)
        {
            float diff = Mathf.Abs(cut - (float)t);
            if (diff < minDiff)
            {
                minDiff = diff;
                closestCut = cut;
            }
        }

        if (knownVideoLength > 0d)
        {
            float finalDiff = Mathf.Abs((float)knownVideoLength - t);
            if (finalDiff < minDiff)
            {
                minDiff = finalDiff;
                closestCut = (float)knownVideoLength;
            }
        }


        return minDiff;
    }

    void LogDebug(string message)
    {
        if (!debugWorkflow)
        {
            return;
        }

        Debug.Log("[Interview] " + name + " | state=" + state + " | progression=" + progression.ToString("0.00") + " | " + message, this);
    }

    void ReleasePlaybackResources(bool clearDepthkitAssets)
    {
        if (videoPlayer != null)
        {
            videoPlayer.Stop();
            videoPlayer.clip = null;
            videoPlayer.targetTexture = null;
            if (clearDepthkitAssets)
            {
                videoPlayer.url = string.Empty;
            }
        }

        //subtitles?.stop();
        loadingEvent?.evt.Stop(gameObject);


        if (clearDepthkitAssets && clip != null)
        {
            clip.poster = null;
            clip.metadataFile = null;
            clip.metadataFilePath = string.Empty;
            loadedPreviewBasePath = null;
            loadedInterviewId = null;
        }

        if (clearDepthkitAssets && previewLoadCoroutine != null)
        {
            StopCoroutine(previewLoadCoroutine);
            previewLoadCoroutine = null;
            previewLoadInProgress = false;
        }

        if (clearDepthkitAssets)
        {
            ReleasePosterTexture();
        }

        resourcesReleased = true;
    }

    void ReleasePosterTexture()
    {
        if (posterTex == null)
        {
            return;
        }

        if (clip != null && ReferenceEquals(clip.poster, posterTex))
        {
            clip.poster = null;
        }

        if (posterTexFromSharedCache)
        {
            posterTex = null;
            posterTexFromSharedCache = false;
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(posterTex);
        }
        else
        {
            DestroyImmediate(posterTex);
        }

        posterTex = null;
        posterTexFromSharedCache = false;
    }



    private void OnDrawGizmos()
    {

    }
}
