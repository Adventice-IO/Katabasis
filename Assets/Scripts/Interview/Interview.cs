using Depthkit;
using UnityEngine;
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

    public bool tLoad;
    public bool tPlay;
    public bool tStop;

    public bool isFocused { get; set; } = false;

    public float focusTime = 3f;
    [Range(0, 1)]
    public float progression;
    [Range(0, 1)]
    public float evaporate;

    public float evaporateTime = 3f;
    public bool shouldEvaporate = false;

    Salle salle;

    public enum State
    {
        Idle,
        Loaded,
        Playing,
        Ending
    }


    public State state;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void OnEnable()
    {
        init();
        load(itwName, level);
        progression = 0;
        evaporate = 0;
        shouldEvaporate = false;
    }

    private void OnDisable()
    {
    }

    void init()
    {
        clip = GetComponent<Depthkit.Clip>();
        videoPlayer = GetComponent<VideoPlayer>();
        vfx = GetComponent<VisualEffect>();
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


        if (tLoad)
        {
            tLoad = false;
            load(itwName, level);
        }
        if (tPlay)
        {
            tPlay = false;
            play();
        }

        if (tStop)
        {
            tStop = false;
            videoPlayer.Stop();
        }

        if (evaporate == 1) return; // finished evaporating

        if (progression < 1)
        {
            if (Application.isPlaying)
            {
                float focusProg = Time.deltaTime * (isFocused ? 1 : -1) / focusTime;

                progression = Mathf.Clamp01(progression + focusProg);
                if (progression >= 1)
                {
                    play();
                }
            }
        }
        else
        {

            if (videoPlayer.isPlaying)
            {
                if (!shouldEvaporate)
                {
                    if (videoPlayer.time > videoPlayer.length - 0.1f)
                    {
                        shouldEvaporate = true;
                    }
                }
            }

            if (shouldEvaporate && evaporate < 1)
            {
                float evapProg = Time.deltaTime / evaporateTime;
                evaporate = Mathf.Clamp(evaporate + evapProg, 0, 1);
            }
        }


        vfx.SetFloat("Progression", progression);
        vfx.SetFloat("Evaporate", evaporate);
    }

    public void load(string itwName, int level)
    {
        this.itwName = itwName;
        this.level = level;

        if (clip == null)
        {
            init();
        }

        string basePath = System.IO.Path.Combine(Application.streamingAssetsPath, "depthkit", itwName, "level" + level).Replace("\\", "/") + "/" + itwName + level;

        videoPlayer.url = "file:///" + basePath + ".mp4";

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
    }

    private void OnDrawGizmos()
    {

    }
}
