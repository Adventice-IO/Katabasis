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

        float reveal = Mathf.Min(fadeInVal, fadeOutVal);

        if (render == null || block == null) return;

        render.GetPropertyBlock(block);
        block.SetFloat("_Reveal", reveal);
        block.SetFloat("_Alpha", profile._Alpha);

        float maxDistance = profile.linkMaxDistanceToCamera ? Camera.main.farClipPlane : profile._MaxDistance;
        block.SetFloat("_MaxDistance", maxDistance);
        block.SetFloat("_DistFade", profile._DistanceFade  * maxDistance);
        render.SetPropertyBlock(block);
    }

    public void kill()
    {
        timeAtKill = Time.time;
    }
}
