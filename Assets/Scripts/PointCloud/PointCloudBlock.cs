using UnityEngine;

public class PointCloudBlock : MonoBehaviour
{
    PointCloudProfile profile;

    MaterialPropertyBlock block;
    Renderer render;

    public float timeAtStart = 0;
    public float timeAtKill = -1;

    //delegate onKill
    public delegate void onKillEvent();
    public event onKillEvent onKill;

    public void init(PointCloudProfile profile)
    {
        this.profile = profile;
    }

    void Start()
    {
        block = new MaterialPropertyBlock();
        render = GetComponent<Renderer>();
        timeAtStart = Time.time;
    }

    // Update is called once per frame
    void Update()
    {

        float fadeInVal = Mathf.Clamp01((Time.time - timeAtStart) / profile.fadeIn);
        float fadeOutVal = timeAtKill > -1 ? 1f - Mathf.Clamp01((Time.time - (float)timeAtKill) / profile.fadeOut) : 1f;


        if (timeAtKill > -1 && fadeOutVal == 0f)
        {
            onKill?.Invoke();
            return;
        }

        float timeIn = Mathf.Min(fadeInVal, fadeOutVal);

        render.GetPropertyBlock(block);
        block.SetFloat("_Alpha", profile._Alpha);
        block.SetFloat("_ModePoint", profile.modePoint ? 1f : 0f);
        block.SetFloat("_FadeDistanceRange", profile._FadeDistanceRange);
        block.SetFloat("_FadeDistanceFeather", profile._FadeDistanceFeather);
        block.SetFloat("_DensityCropRange", profile._DensityCropRange);
        block.SetFloat("_DensityCropFeather", profile._DensityCropFeather);
        block.SetFloat("_DensityCropMax", profile._DensityCropMax);
        block.SetFloat("_TimeIn", timeIn);
        //block.SetFloat("_Explode", _Explode);
        //block.SetVector("_Effector", effector != null ? effector.position : Vector3.zero);
        render.SetPropertyBlock(block);
    }

    public void kill()
    {
        timeAtKill = Time.time;
    }
}
