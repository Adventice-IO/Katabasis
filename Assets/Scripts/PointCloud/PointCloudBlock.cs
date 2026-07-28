using System;
using System.Threading;
using UnityEngine;

public class PointCloudBlock : MonoBehaviour
{
    private const string LogPrefix = "[PointCloudBlock]";

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

    private bool trackedAsLive;
    private bool trackedAsPendingKill;
    private bool cleanupInvoked;

    private static int _liveBlockCount;
    private static int _pendingKillCount;
    private static long _createdBlockCount;
    private static long _destroyedBlockCount;

    public static int LiveBlockCount => Interlocked.CompareExchange(ref _liveBlockCount, 0, 0);
    public static int PendingKillCount => Interlocked.CompareExchange(ref _pendingKillCount, 0, 0);
    public static long CreatedBlockCount => Interlocked.Read(ref _createdBlockCount);
    public static long DestroyedBlockCount => Interlocked.Read(ref _destroyedBlockCount);

    private void Awake()
    {
        TrackLive();
    }

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

        float fadeInVal = profile.fadeIn > 0f
            ? Mathf.Clamp01((Time.time - timeAtStart) / profile.fadeIn)
            : 1f;
        float fadeOutVal = timeAtKill > -1
            ? profile.fadeOut > 0f
                ? 1f - Mathf.Clamp01((Time.time - (float)timeAtKill) / profile.fadeOut)
                : 0f
            : 1f;

        if (timeAtKill > -1 && fadeOutVal == 0f)
        {
            InvokeCleanup();
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

        float cameraDistance = Camera.main != null ? Camera.main.farClipPlane : profile._MaxDistance;
        float baseMaxDistance = profile.linkMaxDistanceToCamera ? cameraDistance : profile._MaxDistance;
        float gameplayDistanceMultiplier = mainController != null
            ? mainController.pointCloudViewDistanceMultiplier
            : 1f;
        float maxDistance = baseMaxDistance * profile._DistanceMultiplier * gameplayDistanceMultiplier;
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
        if (!cleanupInvoked && timeAtKill < 0f)
        {
            timeAtKill = Time.time;
            TrackPendingKill();
        }
    }

    public void forceKillImmediate()
    {
        if (cleanupInvoked)
        {
            return;
        }

        if (timeAtKill < 0f)
        {
            timeAtKill = Time.time;
        }

        InvokeCleanup();
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

    private void OnDestroy()
    {
        if (trackedAsPendingKill)
        {
            Interlocked.Decrement(ref _pendingKillCount);
            trackedAsPendingKill = false;
        }

        if (trackedAsLive)
        {
            Interlocked.Decrement(ref _liveBlockCount);
            Interlocked.Increment(ref _destroyedBlockCount);
            trackedAsLive = false;
        }
    }

    private void TrackLive()
    {
        if (trackedAsLive)
        {
            return;
        }

        trackedAsLive = true;
        Interlocked.Increment(ref _liveBlockCount);
        Interlocked.Increment(ref _createdBlockCount);
    }

    private void TrackPendingKill()
    {
        if (trackedAsPendingKill)
        {
            return;
        }

        trackedAsPendingKill = true;
        int pendingKillCount = Interlocked.Increment(ref _pendingKillCount);
        if (pendingKillCount >= 128 && pendingKillCount % 128 == 0)
        {
            Debug.LogWarning($"{LogPrefix} Pending kill block count is high: {pendingKillCount}. This usually means deferred cleanup is lagging behind traversal.");
        }
    }

    private void InvokeCleanup()
    {
        if (cleanupInvoked)
        {
            return;
        }

        cleanupInvoked = true;

        // Destroy is deferred until the end of the frame. Stop rendering now
        // so a released material buffer cannot be used in the meantime.
        Renderer currentRenderer = render != null ? render : GetComponent<Renderer>();
        if (currentRenderer != null)
        {
            currentRenderer.enabled = false;
        }

        if (trackedAsPendingKill)
        {
            Interlocked.Decrement(ref _pendingKillCount);
            trackedAsPendingKill = false;
        }

        onKill?.Invoke();
    }
}
