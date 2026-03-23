using Depthkit;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.Video;

[ExecuteAlways]
public class Interview : MonoBehaviour
{
    static readonly Dictionary<string, string> metadataCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, byte[]> posterCache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

    Clip clip;
    VideoPlayer videoPlayer;
    Texture2D posterTex;
    VisualEffect vfx;
    bool loopPointReachedSubscribed;
    bool previewLoadQueued;
    InterviewManager.InterviewData[] playbackSequence;
    int playbackIndex = -1;
    bool isResolvedPlayback;
    bool isTransitioningSequence;
    bool resourcesReleased;
    string loadedPreviewBasePath;
    string loadedInterviewId;

    public string itwName;
    public string interviewId;
    List<float> cutTimes;

    string wwiseEventName;

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
    bool debugWorkflow = true;

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
            Debug.Log("loop point reached subscribed");
        }

        vfx = GetComponentInChildren<VisualEffect>();
        salle = GetComponentInParent<Salle>();

        subtitles = FindAnyObjectByType<Subtitles>();
    }

    public void cleanup()
    {
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

    static byte[] LoadPosterCached(string path)
    {
        if (!posterCache.TryGetValue(path, out byte[] cached))
        {
            cached = File.ReadAllBytes(path);
            posterCache[path] = cached;
        }

        return cached;
    }


    void resetPlaybackState()
    {
        progression = 0;
        evaporateProg = 0;
        shouldEvaporate = false;
        previewLoadQueued = false;
        revealStartTime = -1f;
        playbackSequence = null;
        playbackIndex = -1;
        isResolvedPlayback = false;
        isTransitioningSequence = false;
        resourcesReleased = false;
        state = State.Idle;
    }

    void prepareForNextAssignment()
    {
        LogDebug("Resetting slot state for next assignment");
        resetPlaybackState();
        ReleasePlaybackResources(true);

        InterviewManager manager = FindAnyObjectByType<InterviewManager>();
        manager?.NotifyInterviewStopped(this);
    }

    public void ResetForPreviewAssignment()
    {
        prepareForNextAssignment();
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
            if (!previewLoadQueued)
            {
                load();
            }
        }
        else
        {
            ReleasePlaybackResources(true);
            resetPlaybackState();
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

        cutTimes = data.cutTimes != null ? new List<float>(data.cutTimes) : new List<float>();

        stopWwiseVideoEvent();
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

    public void load()
    {
        previewLoadQueued = false;

        if (IsPreviewLoadedForCurrentAssignment())
        {
            return;
        }

        ReleasePosterTexture();
        clip.metadataFile = null;
        clip.poster = null;
        if (videoPlayer != null)
        {
            stopWwiseVideoEvent(); 
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

    public void play()
    {
        LogDebug("Starting playback for mediaPath='" + interviewId + "' depthkitPath='" + itwName + "'");
        OnInterviewStarted?.Invoke(this);
        InterviewManager manager = FindAnyObjectByType<InterviewManager>();
        manager?.NotifyInterviewStarted(this);

        revealStartTime = -1f;
        vfx.SetFloat("SpawnRate", 1f);
        
        stopWwiseVideoEvent();
        Debug.Log("Start Wwise Event from play");
        AkUnitySoundEngine.PostEvent(wwiseEventName, gameObject);
        

        videoPlayer.Play();
        state = State.Playing;

        if (subtitles != null)
        {
            string languageSuffix = mainController != null ? mainController.getLanguageSuffix() : "";
            string subtitlePath = interviewId + languageSuffix + ".srt";
            subtitles.play(subtitlePath);
        }
    }

    public void StopPlaybackForAnotherInterview()
    {
        if (videoPlayer == null)
        {
            init();
        }

        if (videoPlayer == null || !videoPlayer.isPlaying)
        {
            return;
        }

        LogDebug("Stopping playback because another interview started");
        stopWwiseVideoEvent();
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
        
        stopWwiseVideoEvent();

        evaporateEvent?.evt.Post(gameObject);
        state = State.Ending;
        OnInterviewEnded?.Invoke(this);

        Invoke(nameof(endEvaporate), evaporateTime + 5f);
    }

    void stopWwiseVideoEvent()
     //Stop video
    {
        Debug.Log("Stop Wwise Event " + videoEventID);
            // Stop the specific instance
            // AkCurveInterpolation defines the fade curve (e.g., Linear, Sine)
            AkUnitySoundEngine.ExecuteActionOnEvent(wwiseEventName, AkActionOnEventType.AkActionOnEventType_Stop,
                                                gameObject,
                                                (int)videoStopFade*1000,
                                                AkCurveInterpolation.AkCurveInterpolation_Linear);

            videoEventID = AkUnitySoundEngine.AK_INVALID_PLAYING_ID;

    }


    void endEvaporate()
    {
        if (videoPlayer != null) videoPlayer.Stop();
        stopWwiseVideoEvent();
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
                LogDebug("Resolved sequence length=" + playbackSequence.Length + ", first mediaPath='" + playbackSequence[0].mediaPath + "'");
                loadPlaybackEntry(playbackSequence[playbackIndex]);
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


    void OnVideoFinished(VideoPlayer vp)
    {
        LogDebug("Video finished for mediaPath='" + interviewId + "'");
        string previousInterviewId = interviewId;
        InterviewManager.InterviewData[] completedSequence = playbackSequence;

        if (tryAdvanceSequence())
        {
            return;
        }

        InterviewManager consumedManager = FindAnyObjectByType<InterviewManager>();
        consumedManager?.MarkSlotConsumed(this);

        if (videoPlayer != null)
        {
            videoPlayer.Stop();
        stopWwiseVideoEvent();

        }

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

    float getDiffToClosestCut()
    {
        if (!videoPlayer.isPlaying)
        {
            return -1;
        }
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

        if (videoPlayer.length > 0)
        {
            float finalDiff = Mathf.Abs((float)videoPlayer.length - (float)t);
            if (finalDiff < minDiff)
            {
                minDiff = finalDiff;
                closestCut = (float)videoPlayer.length;
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
         stopWwiseVideoEvent();


        if (clearDepthkitAssets && clip != null)
        {
            clip.poster = null;
            clip.metadataFile = null;
            clip.metadataFilePath = string.Empty;
            loadedPreviewBasePath = null;
            loadedInterviewId = null;
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
