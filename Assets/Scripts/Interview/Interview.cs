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
    static readonly Dictionary<string, double> videoLengthCache = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    Clip clip;
    VideoPlayer videoPlayer;
    Texture2D posterTex;
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
    float resumeTimeSeconds;
    bool hasResumeTime;
    bool leaveRequestedWhileListening;

    const float ResumeLeadSeconds = 5f;

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
            Debug.Log("loop point reached subscribed");
        }

        vfx = GetComponentInChildren<VisualEffect>();
        salle = GetComponentInParent<Salle>();

        subtitles = FindAnyObjectByType<Subtitles>();
    }

    public void cleanup()
    {
        HandleSalleExitWhileListening();
        evaporate();
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


    void resetPlaybackState()
    {
        progression = 0;
        evaporateProg = 0;
        shouldEvaporate = false;
        previewLoadQueued = false;
        previewLoadInProgress = false;
        revealStartTime = -1f;
        playbackSequence = null;
        playbackIndex = -1;
        isResolvedPlayback = false;
        isTransitioningSequence = false;
        currentEntryIsIntro = false;
        currentPerson = null;
        resourcesReleased = false;
        state = State.Idle;
    }

    void prepareForNextAssignment()
    {
        LogDebug("Resetting slot state for next assignment");
        ClearResumeState();
        resetPlaybackState();
        ReleasePlaybackResources(true);

        InterviewManager manager = FindAnyObjectByType<InterviewManager>();
        manager?.NotifyInterviewStopped(this);
    }

    public void ResetForPreviewAssignment()
    {
        prepareForNextAssignment();
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
                bool shouldEnable = mainController.isInSalle(salle);
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

        if (evaporateProg == 1)
        {
            if (!resourcesReleased)
            {
                ReleasePlaybackResources(true);
                resourcesReleased = true;
            }

            return;
        }

        if (progression < 1 && !mainController.editMode)
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
                    bool hasPendingSequenceEntry = playbackSequence != null && playbackIndex >= 0 && playbackIndex < playbackSequence.Length - 1;
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
        vfx.enabled = shouldShow;
        if (shouldShow)
        {
            if (!previewLoadQueued && !previewLoadInProgress)
            {
                BeginPreviewLoad();
            }
        }
        else
        {
            HandleSalleExitWhileListening();
            if (leaveRequestedWhileListening)
            {
                state = State.Ending;
                progression = 0;
                shouldEvaporate = true;
            }
            else
            {
                ClearResumeState();
                ReleasePlaybackResources(true);
                resetPlaybackState();
            }
        }

    }

    public void set(InterviewManager.InterviewData data)
    {
        if (vfx == null)
        {
            init();
        }
        this.itwName = data.depthkitPath;
        this.interviewId = data.mediaPath;
        this.currentPerson = data.person;
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

        if (IsPreviewLoadedForCurrentAssignment())
        {
            return;
        }

        ReleasePosterTexture();
        clip.metadataFile = null;
        clip.poster = null;
        if (videoPlayer != null)
        {
            videoPlayer.Stop();
            videoPlayer.playOnAwake = false;
            videoPlayer.waitForFirstFrame = true;
        }

        LogDebug("Loading preview assets from '" + previewBasePath + "'");

        string metadataPath = previewBasePath + ".txt";
        if (!File.Exists(metadataPath))
        {
            Debug.LogWarning("Metadata doesn't exist for " + metadataPath);
            return;
        }

        string metaData = LoadMetadataCached(metadataPath);
        bool result = clip.LoadMetadata(metaData);

        string posterPath = previewBasePath + ".png";
        if (File.Exists(posterPath))
        {
            posterTex = new Texture2D(2, 2);
            byte[] pngData = LoadPosterCached(posterPath);
            posterTex.LoadImage(pngData);
            clip.poster = posterTex;
        }
        else
        {
            Debug.LogWarning("Poster doesn't exist for " + posterPath);
        }

        Debug.Log("Meta data load result " + result);
        LogDebug("Loaded metadata/poster. Poster assigned=" + (clip.poster != null) + ", videoPrepared=" + (videoPlayer != null && videoPlayer.isPrepared) + ", clipSetup=" + (clip != null && clip.isSetup));

        revealStartTime = Time.time;
        loadedPreviewBasePath = previewBasePath;
        loadedInterviewId = interviewId;
        state = State.Loaded;
    }

    public void BeginPreviewLoad()
    {
        if (!Application.isPlaying)
        {
            load();
            return;
        }

        if (previewLoadInProgress || IsPreviewLoadedForCurrentAssignment())
        {
            return;
        }

        if (!isActiveAndEnabled)
        {
            load();
            return;
        }

        if (previewLoadCoroutine != null)
        {
            StopCoroutine(previewLoadCoroutine);
        }

        previewLoadCoroutine = StartCoroutine(loadPreviewAsync());
    }

    System.Collections.IEnumerator loadPreviewAsync()
    {
        previewLoadQueued = false;
        previewLoadInProgress = true;

        if (IsPreviewLoadedForCurrentAssignment())
        {
            previewLoadInProgress = false;
            previewLoadCoroutine = null;
            yield break;
        }

        ReleasePosterTexture();
        clip.metadataFile = null;
        clip.poster = null;
        if (videoPlayer != null)
        {
            videoPlayer.Stop();
            videoPlayer.playOnAwake = false;
            videoPlayer.waitForFirstFrame = true;
        }

        LogDebug("Async loading preview assets from '" + previewBasePath + "'");

        string metadataPath = previewBasePath + ".txt";
        if (!File.Exists(metadataPath))
        {
            Debug.LogWarning("Metadata doesn't exist for " + metadataPath);
            previewLoadInProgress = false;
            previewLoadCoroutine = null;
            yield break;
        }

        WarmPreviewDataAsync();
        while (metadataLoadTask != null && !metadataLoadTask.IsCompleted)
        {
            yield return null;
        }

        string metaData = metadataLoadTask != null && metadataLoadTask.Status == TaskStatus.RanToCompletion
            ? metadataLoadTask.Result
            : LoadMetadataCached(metadataPath);
        metadataCache[metadataPath] = metaData;

        bool result = clip.LoadMetadata(metaData);
        yield return null;

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

            posterTex = new Texture2D(2, 2);
            posterTex.LoadImage(pngData);
            clip.poster = posterTex;
        }
        else
        {
            Debug.LogWarning("Poster doesn't exist for " + posterPath);
        }

        Debug.Log("Meta data load result " + result);
        LogDebug("Loaded metadata/poster. Poster assigned=" + (clip.poster != null) + ", videoPrepared=" + (videoPlayer != null && videoPlayer.isPrepared) + ", clipSetup=" + (clip != null && clip.isSetup));

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
        

        videoPlayer.Play();

        double knownVideoLength = GetKnownVideoLength();

        float startTime = 0f;
        bool applyResume = !isIntroEntry && hasResumeTime;
        if (applyResume)
        {
            float maxResumeTime = knownVideoLength > 0d
                ? Mathf.Max(0f, (float)knownVideoLength - 0.01f)
                : Mathf.Max(0f, resumeTimeSeconds);
            startTime = Mathf.Clamp(resumeTimeSeconds, 0f, maxResumeTime);
            if (startTime > 0f)
            {
                Debug.Log("Resuming video at " + startTime + " seconds");
                videoPlayer.time = startTime;
                if (videoEventID != AkUnitySoundEngine.AK_INVALID_PLAYING_ID)
                {
                    Debug.Log("Seeking Wwise Event to " + startTime + " seconds");
                    AkUnitySoundEngine.SeekOnEvent(wwiseEventName, gameObject, Mathf.RoundToInt(startTime * 1000f));
                }
            }
        }

        state = State.Playing;

        if (subtitles != null)
        {
            string languageSuffix = mainController != null ? mainController.getLanguageSuffix() : "";
            string subtitlePath = interviewId + languageSuffix + ".srt";
            subtitles.play(subtitlePath, startTime);
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
        CaptureResumeTimeForCurrentEntry();
        leaveRequestedWhileListening = false;
        if (stopWwiseEvent)
        {
            stopWwiseVideoEvent(true);
        }
        ReleasePlaybackResources(false);
        shouldEvaporate = false;
        evaporateProg = 0;
        progression = 0;
        playbackSequence = null;
        playbackIndex = -1;
        isResolvedPlayback = false;
        isTransitioningSequence = false;
        state = State.Loaded;

        InterviewManager manager = FindAnyObjectByType<InterviewManager>();
        manager?.NotifyInterviewStopped(this);
    }

    public void evaporate()
    {

        if (evaporateProg > 0 || shouldEvaporate)
        {
            return;
        }

        LogDebug("Evaporating interview '" + interviewId + "'");
        shouldEvaporate = true;

        evaporateEvent?.evt.Post(gameObject);
        state = State.Ending;
        OnInterviewEnded?.Invoke(this);

        Invoke(nameof(endEvaporate), evaporateTime + 5f);
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
        if (videoPlayer != null) videoPlayer.Stop();
        ReleasePlaybackResources(true);
        gameObject.SetActive(false);
    }
    void resolveAndPlay()
    {
        LogDebug("Resolving playback at validation time for slot '" + name + "'");
        if (!isResolvedPlayback && Application.isPlaying)
        {
            InterviewManager manager = FindAnyObjectByType<InterviewManager>();
            if (manager != null && manager.TryResolvePlaybackForSlot(this, out InterviewManager.ResolvedInterviewPlayback playback))
            {
                playbackSequence = playback.sequence;
                playbackIndex = 0;
                isResolvedPlayback = true;
                SkipStartedIntroAtSequenceStart();
                if (playbackSequence != null && playbackSequence.Length > 0 && playbackIndex >= 0 && playbackIndex < playbackSequence.Length)
                {
                    LogDebug("Resolved sequence length=" + playbackSequence.Length + ", first mediaPath='" + playbackSequence[playbackIndex].mediaPath + "'");
                    loadPlaybackEntry(playbackSequence[playbackIndex]);
                }
            }
        }

        if (!isResolvedPlayback)
        {
            LogDebug("No resolved sequence found, using current assignment directly");
            playbackSequence = null;
            playbackIndex = -1;
        }

        play();
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
        loadPlaybackEntry(playbackSequence[playbackIndex]);
        play();
        isTransitioningSequence = false;
        return true;
    }

    void SkipStartedIntroAtSequenceStart()
    {
        if (playbackSequence == null || playbackSequence.Length == 0)
        {
            return;
        }

        InterviewManager manager = FindAnyObjectByType<InterviewManager>();
        while (playbackIndex >= 0 && playbackIndex < playbackSequence.Length)
        {
            InterviewManager.InterviewData data = playbackSequence[playbackIndex];
            if (!data.isIntro || manager == null || !manager.HasIntroStarted(data.person))
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
        if (currentEntryIsIntro || videoPlayer == null)
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

        CaptureResumeTimeForCurrentEntry();
        leaveRequestedWhileListening = true;
        loadingEvent?.evt.Stop(gameObject);
        subtitles?.stop();
        if (videoPlayer != null)
        {
            videoPlayer.Stop();
        }
    }


    void OnVideoFinished(VideoPlayer vp)
    {
        LogDebug("Video finished for mediaPath='" + interviewId + "'");
        GetKnownVideoLength();
        string previousInterviewId = interviewId;
        InterviewManager.InterviewData[] completedSequence = playbackSequence;
        ClearResumeState();

        if (tryAdvanceSequence())
        {
            return;
        }

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

    void OnVideoPrepared(VideoPlayer vp)
    {
        CacheCurrentVideoLength();
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

        subtitles?.stop();
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

        if (Application.isPlaying)
        {
            Destroy(posterTex);
        }
        else
        {
            DestroyImmediate(posterTex);
        }

        posterTex = null;
    }



    private void OnDrawGizmos()
    {

    }
}
