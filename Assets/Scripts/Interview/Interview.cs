using Depthkit;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;
using UnityEngine.Video;

[ExecuteAlways]
public class Interview : MonoBehaviour
{
    Clip clip;
    VideoPlayer videoPlayer;
    Texture2D posterTex;
    VisualEffect vfx;

    public string itwName;
    [Range(1, 4)]
    public int level;
    string basePath;

    public bool isFocused { get; set; } = false;

    public float focusTime = 3f;
    [Range(0, 1)]
    public float progression;
    [Range(0, 1)]
    public float evaporateProg;

    public float evaporatePreDelay = 0.5f;
    public float evaporateTime = 3f;
    public bool shouldEvaporate = false;

    Salle salle;

    [Header("Audio Settings")]
    public AudioEventRefSO loadingEvent;
    public AudioEventRefSO validateEvent;
    public AudioEventRefSO videoEvent;
    public AudioEventRefSO evaporateEvent;
    public AudioRTPCRefSO progRTPC;
    public enum State
    {
        Idle,
        Loaded,
        Playing,
        Ending
    }


    public State state;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
        void OnEnable()
    {
        init();
        set(itwName, level);
        progression = 0;
        evaporateProg = 0;
        shouldEvaporate = false;
    }

    void OnDisable()
    {
    }

    void init()
    {
        clip = GetComponent<Depthkit.Clip>();
        videoPlayer = GetComponent<VideoPlayer>();

        //videoPlayer.loopPointReached += (VideoPlayer vp) =>
        //{
        //    stop();
        //    shouldEvaporate = true;
        //};

        vfx = GetComponentInChildren<VisualEffect>();
        salle = GetComponentInParent<Salle>();

    }

    // Update is called once per frame
    void Update()
    {

        if (clip == null || videoPlayer == null || vfx == null) init();

        
        if (Application.isPlaying)
        {
            if (salle != null)
            {
                vfx.enabled = MainController.instance.isInSalle(salle);
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

        if (evaporateProg == 1) return; // finished evaporating

        if (progression < 1)
        {
            if (Application.isPlaying)
            {
                float focusProg = Time.deltaTime * (isFocused ? 1 : -1) / focusTime;

                float newProg = Mathf.Clamp01(progression + focusProg);

                if (newProg != progression)
                {
                    if (newProg > 0 && progression == 0)
                    {
                        loadingEvent.evt.Post(gameObject);
                    }
                    else if (newProg == 0 && progression > 0)
                    {
                        loadingEvent.evt.Stop(gameObject);
                    }

                    progression = newProg;
                    progRTPC.rtpc.SetValue(gameObject, progression);

                    if (progression >= 1)
                    {
                        play();
                        loadingEvent.evt.Stop(gameObject);
                        validateEvent.evt.Post(gameObject);
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
                    if (videoPlayer.time > videoPlayer.length - evaporatePreDelay)
                    {
                        evaporate();
                    }
                }
            }

            if (shouldEvaporate && evaporateProg < 1)
            {
                float evapProg = Time.deltaTime / evaporateTime;
                evaporateProg = Mathf.Clamp(evaporateProg + evapProg, 0, 1);
            }
        }


        vfx.SetFloat("Progression", progression);
        vfx.SetFloat("Evaporate", evaporateProg);
    }

    public void set(string itwName, int level)
    {
        this.itwName = itwName;
        this.level = level;

        if (clip == null)
        {
            init();
        }

        basePath = System.IO.Path.Combine(Application.streamingAssetsPath, "depthkit", itwName, "level" + level).Replace("\\", "/") + "/" + itwName + level;
        videoPlayer.url = "file:///" + basePath + ".mp4";
    }

    public void load()
    {
        clip.metadataFile = null;
        string metaData = System.IO.File.ReadAllText(basePath + ".txt");
        clip.LoadMetadata(metaData);

        posterTex = new Texture2D(2, 2);
        byte[] pngData = System.IO.File.ReadAllBytes(basePath + ".png");
        posterTex.LoadImage(pngData);
        clip.poster = posterTex;

        state = State.Loaded;
    }

    public void play()
    {
        videoPlayer.Play();
        videoEvent.evt.Post(gameObject);
    }

    public void evaporate()
    {
        shouldEvaporate = true;
        videoEvent.evt.Stop(gameObject);
        evaporateEvent.evt.Post(gameObject);
    }



    private void OnDrawGizmos()
    {

    }
    }
