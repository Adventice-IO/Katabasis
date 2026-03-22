using UnityEngine;

[ExecuteAlways]
public class ImmersivePreview : MonoBehaviour
{
    public ImmersiveController immersiveController;

    public GameObject leftWall;
    public GameObject rightWall;
    public GameObject frontWall;
    public GameObject backWall;

    bool lockRoomToCamera = false;

    void OnEnable()
    {
        RefreshPreview();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        RefreshPreview();
    }

    // Update is called once per frame
    void Update()
    {
        RefreshPreview();
    }

    void RefreshPreview()
    {
        if (immersiveController == null)
        {
            return;
        }

        float roomWidth = Mathf.Max(0.01f, immersiveController.roomWidth);
        float roomDepth = Mathf.Max(0.01f, immersiveController.roomDepth);
        float roomHeight = Mathf.Max(0.01f, immersiveController.roomHeight);
        Vector3 previewOrigin = immersiveController.transform.position;
        Quaternion previewRotation = immersiveController.transform.rotation;

        if(lockRoomToCamera)
        {
            transform.position = previewOrigin;
            transform.rotation = previewRotation;
        }

        CreateWall(ref leftWall, "Left Wall");
        CreateWall(ref rightWall, "Right Wall");
        CreateWall(ref frontWall, "Front Wall");
        CreateWall(ref backWall, "Back Wall");

        SetupWall(leftWall, new Vector3(-roomWidth * 0.5f, roomHeight * 0.5f, 0f), Quaternion.Euler(0f, -90f, 0f), new Vector3(roomDepth, roomHeight, 1f), immersiveController.leftRenderTexture);
        SetupWall(rightWall, new Vector3(roomWidth * 0.5f, roomHeight * 0.5f, 0f), Quaternion.Euler(0f, 90f, 0f), new Vector3(roomDepth, roomHeight, 1f), immersiveController.rightRenderTexture);
        SetupWall(frontWall, new Vector3(0f, roomHeight * 0.5f, roomDepth * 0.5f), Quaternion.identity, new Vector3(roomWidth, roomHeight, 1f), immersiveController.frontRenderTexture);
        SetupWall(backWall, new Vector3(0f, roomHeight * 0.5f, -roomDepth * 0.5f), Quaternion.Euler(0f, 180f, 0f), new Vector3(roomWidth, roomHeight, 1f), immersiveController.backRenderTexture);
    }

    void CreateWall(ref GameObject wall, string name)
    {
        if (wall != null)
        {
            return;
        }
        wall = GameObject.CreatePrimitive(PrimitiveType.Quad);
        wall.name = name;
        wall.transform.parent = this.transform;
    }

    void SetupWall(GameObject wall, Vector3 position, Quaternion rotation, Vector3 scale, RenderTexture renderTexture)
    {
        if (wall == null)
        {
            return;
        }

        Transform wallTransform = wall.transform;
        wallTransform.localPosition = position;
        wallTransform.localRotation = rotation;
        wallTransform.localScale = scale;

        Renderer wallRenderer = wall.GetComponent<Renderer>();
        if (wallRenderer == null || wallRenderer.sharedMaterial == null)
        {
            return;
        }

        wallRenderer.sharedMaterial.mainTexture = renderTexture;
    }
}
