using System;
using BAPointCloudRenderer.CloudData;
using BAPointCloudRenderer.ObjectCreation;
using BAPointCloudRenderer.Utility;
using System.Collections.Generic;
using UnityEngine;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct MaskBox
{
    public MaskBox(Matrix4x4 worldToLocal, Vector3 extents, float alpha, float feather, float soloWhenInside)
    {
        this.worldToLocal = worldToLocal;
        this.extents = extents;
        this.alpha = alpha;
        this.settings = new Vector4(feather, soloWhenInside, 0f, 0f);
    }

    public Matrix4x4 worldToLocal; // 64 bytes
    public Vector3 extents;        // 12 bytes
    public float alpha;           // 4 bytes (Total: 80 bytes, 16-byte aligned)
    public Vector4 settings;       // 16 bytes (x: feather, y: solo, z: unused, w: unused)
}

public class KatabasisMeshConfiguration : MeshConfiguration
{
    public enum PointRenderingMode
    {
        Point,
        Size
    }

    [Serializable]
    public sealed class RuntimeConfiguration
    {
        public PointRenderingMode renderingMode = PointRenderingMode.Point;
        public float pointSize = 2f;
        public float alpha = 1f;
        public bool linkMaxDistanceToCamera = true;
        public float maxViewDistance = 20f;
        public float viewDistanceMultiplier = 1f;
        public float distanceFade = 0.3f;
        public float fadeIn = 1f;
        public float fadeOut = 1f;
        public float boxFeather = 0.5f;
    }

    private const string PointShaderName = "Point Cloud/Optimized_Masked_VR";
    private const string SizeShaderName = "Point Cloud/Optimized_Masked_VR_Size";

    public PointCloudProfile profile = null;
    public Material material;
    public Camera renderCamera = null;
    public bool displayLOD = false;
    public Transform root;
    public bool allowDeferredBlockCleanup = false;

    [Header("Point Rendering")]
    public PointRenderingMode renderingMode = PointRenderingMode.Point;
    [Min(0.1f)] public float pointSize = 2f;

    private HashSet<PointCloudBlock> gameObjectCollection = null;
    private GraphicsBuffer _maskBuffer;
    private PointCloudMask[] masks;
    private MaskBox[] _boxes;
    private Material _sourceMaterial;
    private PointCloudProfile _sourceProfile;

    public void Start()
    {
        CreateRuntimeResources();
        gameObjectCollection = new HashSet<PointCloudBlock>();
        renderCamera = Camera.main;
        if (material != null)
        {
            material.enableInstancing = true;
            ApplyPointAppearance();
        }

        masks = GetComponentsInChildren<PointCloudMask>();
        _boxes = new MaskBox[masks.Length];
        if (_boxes.Length > 0)
        {
            const int maskBoxSize = 96; // Size of MaskBox struct in bytes (16-byte aligned)
            _maskBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _boxes.Length, maskBoxSize);
        }
    }

    public RuntimeConfiguration CaptureConfiguration()
    {
        var configuration = new RuntimeConfiguration
        {
            renderingMode = renderingMode,
            pointSize = pointSize
        };

        if (profile != null)
        {
            configuration.alpha = profile._Alpha;
            configuration.linkMaxDistanceToCamera = profile.linkMaxDistanceToCamera;
            configuration.maxViewDistance = profile._MaxDistance;
            configuration.viewDistanceMultiplier = profile._DistanceMultiplier;
            configuration.distanceFade = profile._DistanceFade;
            configuration.fadeIn = profile.fadeIn;
            configuration.fadeOut = profile.fadeOut;
            configuration.boxFeather = profile._BoxFeather;
        }

        return configuration;
    }

    public void ApplyConfiguration(RuntimeConfiguration configuration)
    {
        if (configuration == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            CreateRuntimeResources();
        }

        renderingMode = Enum.IsDefined(typeof(PointRenderingMode), configuration.renderingMode)
            ? configuration.renderingMode
            : PointRenderingMode.Point;
        pointSize = FiniteAtLeast(configuration.pointSize, 0.1f, 2f);

        if (profile != null)
        {
            profile._Alpha = Mathf.Clamp01(FiniteOr(configuration.alpha, 1f));
            profile.linkMaxDistanceToCamera = configuration.linkMaxDistanceToCamera;
            profile._MaxDistance = FiniteAtLeast(configuration.maxViewDistance, 0f, 20f);
            profile._DistanceMultiplier = FiniteAtLeast(configuration.viewDistanceMultiplier, 0f, 1f);
            profile._DistanceFade = Mathf.Clamp01(FiniteOr(configuration.distanceFade, 0.3f));
            profile.fadeIn = FiniteAtLeast(configuration.fadeIn, 0f, 1f);
            profile.fadeOut = FiniteAtLeast(configuration.fadeOut, 0f, 1f);
            profile._BoxFeather = FiniteAtLeast(configuration.boxFeather, 0f, 0.5f);
        }

        ApplyPointAppearance();
    }

    private void CreateRuntimeResources()
    {
        if (material != null && _sourceMaterial == null)
        {
            _sourceMaterial = material;
            material = new Material(_sourceMaterial)
            {
                name = _sourceMaterial.name + " (Runtime)"
            };
        }

        if (profile != null && _sourceProfile == null)
        {
            _sourceProfile = profile;
            profile = Instantiate(_sourceProfile);
            profile.name = _sourceProfile.name + " (Runtime)";
        }
    }

    private void ApplyPointAppearance()
    {
        if (material == null)
        {
            return;
        }

        string shaderName = renderingMode == PointRenderingMode.Size ? SizeShaderName : PointShaderName;
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"Point cloud shader '{shaderName}' could not be found.", this);
            return;
        }

        if (material.shader != shader)
        {
            material.shader = shader;
        }

        material.SetFloat("_PointSize", pointSize);
    }

    private static float FiniteOr(float value, float fallback)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }

    private static float FiniteAtLeast(float value, float minimum, float fallback)
    {
        return Mathf.Max(minimum, FiniteOr(value, fallback));
    }

    public void Update()
    {
       if(masks == null || _boxes == null)
        {
            return;
        }

        if (displayLOD)
        {
            foreach (PointCloudBlock go in gameObjectCollection)
            {
                BoundingBoxComponent bbc = go.GetComponent<BoundingBoxComponent>();
                BBDraw.DrawBoundingBox(bbc.boundingBox, bbc.parent, Color.red, false);
            }
        }

        for (int i = 0; i < masks.Length; i++)
        {
            PointCloudMask mask = masks[i];
            _boxes[i] = mask.maskBox;
        }

        if (_maskBuffer != null)
        {
            _maskBuffer.SetData(_boxes);
        }

        foreach (PointCloudBlock go in gameObjectCollection)
        {
            go.updateMasks(_maskBuffer, _boxes.Length);
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
        pointCloudBlock.GetComponent<MeshRenderer>().sharedMaterial = material;
        pointCloudBlock.onKill += () =>
        {
            gameObjectCollection?.Remove(pointCloudBlock);
            if (filter != null && filter.sharedMesh != null)
            {
                Destroy(filter.sharedMesh);
            }
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
        PointCloudBlock block = gameObject.GetComponent<PointCloudBlock>();
        if (block == null)
        {
            MeshFilter filter = gameObject.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                Destroy(filter.sharedMesh);
            }
            Destroy(gameObject);
            return;
        }

        if (allowDeferredBlockCleanup)
        {
            block.kill();
            return;
        }

        block.forceKillImmediate();
    }

    private void OnDisable()
    {
        if (gameObjectCollection != null)
        {
            foreach (PointCloudBlock block in new List<PointCloudBlock>(gameObjectCollection))
            {
                if (block != null)
                {
                    block.forceKillImmediate();
                }
            }
            gameObjectCollection.Clear();
        }

        if (_maskBuffer != null)
        {
            _maskBuffer.Release();
            _maskBuffer = null;
        }

        if (_sourceMaterial != null)
        {
            Destroy(material);
            material = _sourceMaterial;
            _sourceMaterial = null;
        }

        if (_sourceProfile != null)
        {
            Destroy(profile);
            profile = _sourceProfile;
            _sourceProfile = null;
        }
    }
}
