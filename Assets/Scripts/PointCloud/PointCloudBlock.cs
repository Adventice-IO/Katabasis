using System;
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

    GraphicsBuffer masksBuffer;
    int masksCount = 0;

    MainController mainController;
    MeshFilter meshFilter;
    bool hasLocalBounds;
    Vector3 localMin;
    Vector3 localMax;

    Vector3 boxMin;
    Vector3 boxMax;

    public void init(PointCloudProfile profile)
    {
        this.profile = profile;
    }

    void Start()
    {
        mainController = GameObject.FindAnyObjectByType<MainController>();
        block = new MaterialPropertyBlock();
        render = GetComponent<Renderer>();
        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            Bounds bounds = meshFilter.sharedMesh.bounds;
            localMin = bounds.min;
            localMax = bounds.max;
            hasLocalBounds = true;
        }
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


        //Get world space bounding box of the block
        if (!hasLocalBounds)
        {
            return;
        }

        boxMin = transform.TransformPoint(localMin);
        boxMax = transform.TransformPoint(localMax);

        render.GetPropertyBlock(block);
        block.SetFloat("_Reveal", reveal);
        block.SetFloat("_Alpha", profile._Alpha);

        float maxDistance = (profile.linkMaxDistanceToCamera ? Camera.main.farClipPlane : profile._MaxDistance) * mainController.pointCloudViewDistanceMultiplier;
        block.SetFloat("_MaxDistance", maxDistance);
        block.SetFloat("_DistFade", profile._DistanceFade * maxDistance);
        block.SetFloat("_NoiseAmplitude", profile._NoiseAmplitude);
        block.SetFloat("_NoiseThickness", profile._NoiseThickness);
        block.SetFloat("_NoiseScale", profile._NoiseScale);
        block.SetFloat("_NoiseAlphaMultiplier", profile._NoiseAlphaMultiplier);
        block.SetVector("_BoxMin", boxMin);
        block.SetVector("_BoxMax", boxMax);
        block.SetFloat("_BoxFeather", profile._BoxFeather);

        if (masksBuffer != null)
        {
            block.SetBuffer("_MaskBoxes", masksBuffer);
            block.SetInt("_MaskCount", masksCount);
        }

        render.SetPropertyBlock(block);
    }

    public void kill()
    {
        if (timeAtKill < 0f)
        {
            timeAtKill = Time.time;
        }
    }

    public void forceKillImmediate()
    {
        timeAtKill = 0f;
        onKill?.Invoke();
    }

    public void updateMasks(GraphicsBuffer maskBuffer, int count)
    {
        masksBuffer = maskBuffer;
        masksCount = count;

    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = (boxMin + boxMax) / 2;
        Vector3 size = boxMax - boxMin;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(center, size);
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(boxMin, 0.1f);
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(boxMax, 0.1f);
    }
}
