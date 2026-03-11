using BAPointCloudRenderer.CloudController;
using BAPointCloudRenderer.CloudData;
using BAPointCloudRenderer.Controllers;
using BAPointCloudRenderer.ObjectCreation;
using BAPointCloudRenderer.Utility;
using System.Collections.Generic;
using UnityEngine;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct MaskBox
{
    public MaskBox(Matrix4x4 worldToLocal, Vector3 extents, float alpha)
    {
        this.worldToLocal = worldToLocal;
        this.extents = extents;
        this.alpha = alpha;
    }

    public Matrix4x4 worldToLocal; // 64 bytes
    public Vector3 extents;        // 12 bytes
    public float alpha;           // 4 bytes (Total: 80 bytes, 16-byte aligned)
}

[ExecuteInEditMode]
public class KatabasisMeshConfiguration : MeshConfiguration
{
    public PointCloudProfile profile = null;
    public Material material;
    public Camera renderCamera = null;
    public bool displayLOD = false;
    public Transform root;

    private HashSet<PointCloudBlock> gameObjectCollection = null;
    private GraphicsBuffer _maskBuffer;
    private PointCloudMask[] masks;
    private MaskBox[] _boxes;


    public void Start()
    {
        gameObjectCollection = new HashSet<PointCloudBlock>();
        renderCamera = Camera.main;
        material.enableInstancing = true;

        masks = GetComponentsInChildren<PointCloudMask>();
        _boxes = new MaskBox[masks.Length];
        const int maskBoxSize = 80; // Size of MaskBox struct in bytes (16-byte aligned)
        _maskBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _boxes.Length, maskBoxSize);
    }

    public void Onable()
    {
        masks = GetComponentsInChildren<PointCloudMask>();
        _boxes = new MaskBox[masks.Length];
    }

    public void Update()
    {
        if (displayLOD)
        {
            foreach (PointCloudBlock go in gameObjectCollection)
            {
                BoundingBoxComponent bbc = go.GetComponent<BoundingBoxComponent>();
                BBDraw.DrawBoundingBox(bbc.boundingBox, bbc.parent, Color.red, false);
            }
        }

        if (masks == null || masks.Length == 0)
        {
            masks = GetComponentsInChildren<PointCloudMask>();
        }

        if (_boxes == null || _boxes.Length != masks.Length)
        {
            _boxes = new MaskBox[masks.Length];
        }

        for (int i = 0; i < masks.Length; i++)
        {
            PointCloudMask mask = masks[i];
            _boxes[i] = mask.maskBox;
        }

        if (_maskBuffer == null || _maskBuffer.count != _boxes.Length)
        {
            const int maskBoxSize = 80; // Size of MaskBox struct in bytes (16-byte aligned)
            _maskBuffer?.Release();
            _maskBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _boxes.Length, maskBoxSize);
        }
        _maskBuffer.SetData(_boxes);

        if (Application.isPlaying)
        {
            foreach (PointCloudBlock go in gameObjectCollection)
            {
                go.updateMasks(_maskBuffer, _boxes.Length);
            }
        }
        else
        {
            MultiPreview multiPreview = GetComponentInChildren<MultiPreview>();

            foreach (PreviewObject go in multiPreview.previewObjects)
            {
                if (go != null)
                {
                    go.updateMasks(_maskBuffer, _boxes.Length);
                }
            }
        }
    }

    public override GameObject CreateGameObject(string name, Vector3[] vertexData, Color[] colorData, BoundingBox boundingBox, Transform parent, string version, Vector3d translationV2)
    {
        GameObject gameObject = new GameObject(name);

        Mesh mesh = new Mesh();

        MeshFilter filter = gameObject.AddComponent<MeshFilter>();
        filter.mesh = mesh;
        MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = material;

        int[] indecies = new int[vertexData.Length];
        for (int i = 0; i < vertexData.Length; ++i)
        {
            indecies[i] = i;
        }
        mesh.vertices = vertexData;
        mesh.colors = colorData;
        mesh.SetIndices(indecies, MeshTopology.Points, 0);

        //Set Translation
        if (version == "2.0")
        {
            // 20230125: potree v2 vertices have absolute coordinates,
            // hence all gameobjects need to reside at Vector.Zero.
            // And: the position must be set after parenthood has been granted.
            //gameObject.transform.Translate(boundingBox.Min().ToFloatVector());
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = translationV2.ToFloatVector();
        }
        else
        {
            gameObject.transform.Translate(boundingBox.Min().ToFloatVector());
            gameObject.transform.SetParent(parent, false);
        }

        BoundingBoxComponent bbc = gameObject.AddComponent<BoundingBoxComponent>();
        bbc.boundingBox = boundingBox; ;
        bbc.parent = parent;



        PointCloudBlock pointCloudBlock = gameObject.AddComponent<PointCloudBlock>();
        pointCloudBlock.init(profile);
        pointCloudBlock.GetComponent<MeshRenderer>().material = material;
        pointCloudBlock.onKill += () =>
        {
            gameObjectCollection?.Remove(pointCloudBlock);
            Destroy(gameObject);
        };

        if (gameObjectCollection != null)
        {
            gameObjectCollection.Add(pointCloudBlock);
        }

        gameObject.transform.SetParent(root);
        return gameObject;
    }

    public override int GetMaximumPointsPerMesh()
    {
        return 65535;
    }

    public override void RemoveGameObject(GameObject gameObject)
    {
        gameObject.GetComponent<PointCloudBlock>()?.kill();
    }
}