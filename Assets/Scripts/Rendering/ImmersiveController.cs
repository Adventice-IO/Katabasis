using UnityEngine;

[ExecuteAlways]
public class ImmersiveController : MonoBehaviour
{

    public float roomWidth = 15f;
    public float roomDepth = 30f;
    public float roomHeight = 4f;
    public float cameraHeightOffset = 0f;
    [Range(0f, 1f)] public float cameraWallBlend = 1f;

    public int resolutionWidth = 5236;
    public int resolutionHeight = 698;
    public int resolutionDepth = 2618;

    public float farClipPlaneSides = 100f;
    public float farClipPlaneFrontback = 100f;

    Camera leftCam;
    Camera rightCam;
    Camera frontCam;
    Camera backCam;

    public RenderTexture leftRenderTexture;
    public RenderTexture rightRenderTexture;
    public RenderTexture frontRenderTexture;
    public RenderTexture backRenderTexture;

    int currentResolutionWidth;
    int currentResolutionHeight;
    int currentResolutionDepth;

    public bool dynamicTextures = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        leftCam = GameObject.Find("LeftCam").GetComponent<Camera>();
        rightCam = GameObject.Find("RightCam").GetComponent<Camera>();
        frontCam = GameObject.Find("FrontCam").GetComponent<Camera>();
        backCam = GameObject.Find("BackCam").GetComponent<Camera>();

        SetupRenderTextures();
        updateCameras();
    }

    // Update is called once per frame
    void Update()
    {
        if (resolutionWidth != currentResolutionWidth || resolutionHeight != currentResolutionHeight || resolutionDepth != currentResolutionDepth)
        {
            SetupRenderTextures();
        }

        updateCameras();

    }

    void updateCameras()
    {
        float safeRoomWidth = Mathf.Max(0.01f, roomWidth);
        float safeRoomDepth = Mathf.Max(0.01f, roomDepth);
        float safeRoomHeight = Mathf.Max(0.01f, roomHeight);
        float cameraY = (safeRoomHeight * 0.5f) + cameraHeightOffset;
        float blend = Mathf.Clamp01(cameraWallBlend);
        float halfRoomWidth = safeRoomWidth * 0.5f;
        float halfRoomDepth = safeRoomDepth * 0.5f;
        float cameraX = Mathf.Lerp(0f, halfRoomWidth, blend);
        float cameraZ = Mathf.Lerp(0f, halfRoomDepth, blend);
        float leftRightViewDistance = Mathf.Max(0.01f, halfRoomWidth + cameraX);
        float frontBackViewDistance = Mathf.Max(0.01f, halfRoomDepth + cameraZ);

        SetupProjection(leftCam, new Vector3(cameraX, cameraY, 0f), Quaternion.Euler(0f, -90f, 0f), safeRoomDepth, safeRoomHeight, leftRightViewDistance);
        SetupProjection(rightCam, new Vector3(-cameraX, cameraY, 0f), Quaternion.Euler(0f, 90f, 0f), safeRoomDepth, safeRoomHeight, leftRightViewDistance);
        SetupProjection(frontCam, new Vector3(0f, cameraY, -cameraZ), Quaternion.identity, safeRoomWidth, safeRoomHeight, frontBackViewDistance);
        SetupProjection(backCam, new Vector3(0f, cameraY, cameraZ), Quaternion.Euler(0f, 180f, 0f), safeRoomWidth, safeRoomHeight, frontBackViewDistance);
    }

    void SetupRenderTextures()
    {
        if(!dynamicTextures)
        {
            return;
        }
        int safeResolutionWidth = Mathf.Max(1, resolutionWidth);
        int safeResolutionHeight = Mathf.Max(1, resolutionHeight);
        int safeResolutionDepth = Mathf.Max(1, resolutionDepth);

        ReleaseRenderTexture(leftRenderTexture);
        ReleaseRenderTexture(rightRenderTexture);
        ReleaseRenderTexture(frontRenderTexture);
        ReleaseRenderTexture(backRenderTexture);

        leftRenderTexture = new RenderTexture(safeResolutionDepth, safeResolutionHeight, 24);
        rightRenderTexture = new RenderTexture(safeResolutionDepth, safeResolutionHeight, 24);
        frontRenderTexture = new RenderTexture(safeResolutionWidth, safeResolutionHeight, 24);
        backRenderTexture = new RenderTexture(safeResolutionWidth, safeResolutionHeight, 24);

        leftCam.targetTexture = leftRenderTexture;
        rightCam.targetTexture = rightRenderTexture;
        frontCam.targetTexture = frontRenderTexture;
        backCam.targetTexture = backRenderTexture;

        currentResolutionWidth = safeResolutionWidth;
        currentResolutionHeight = safeResolutionHeight;
        currentResolutionDepth = safeResolutionDepth;

        resolutionWidth = safeResolutionWidth;
        resolutionHeight = safeResolutionHeight;
        resolutionDepth = safeResolutionDepth;
    }

    void SetupProjection(Camera cam, Vector3 localPosition, Quaternion localRotation, float wallWidth, float wallHeight, float viewDistance)
    {
        if (cam == null)
        {
            return;
        }

        Transform camTransform = cam.transform;
        camTransform.position = transform.TransformPoint(localPosition);
        camTransform.rotation = transform.rotation * localRotation;

        float safeWallWidth = Mathf.Max(0.01f, wallWidth);
        float safeWallHeight = Mathf.Max(0.01f, wallHeight);
        float halfWallHeight = safeWallHeight * 0.5f;
        float safeViewDistance = Mathf.Max(0.01f, viewDistance);
        float verticalFov = Mathf.Atan(halfWallHeight / safeViewDistance) * 2f * Mathf.Rad2Deg;

        cam.orthographic = false;
        cam.aspect = safeWallWidth / safeWallHeight;
        cam.fieldOfView = verticalFov;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane =  cam == leftCam || cam == rightCam ? farClipPlaneSides : farClipPlaneFrontback;
    }

    void ReleaseRenderTexture(RenderTexture renderTexture)
    {
        if(!dynamicTextures)
        {
            return;
        }

        if (renderTexture == null)
        {
            return;
        }

        if (renderTexture.IsCreated())
        {
            renderTexture.Release();
        }

        if(Application.isPlaying)
        {
            Destroy(renderTexture);
        }
        else
        {
            DestroyImmediate(renderTexture);
        }
    }

    void OnDestroy()
    {
        ReleaseRenderTexture(leftRenderTexture);
        ReleaseRenderTexture(rightRenderTexture);
        ReleaseRenderTexture(frontRenderTexture);
        ReleaseRenderTexture(backRenderTexture);
    }
}
