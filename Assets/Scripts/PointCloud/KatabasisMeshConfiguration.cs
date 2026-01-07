using BAPointCloudRenderer.CloudData;
using BAPointCloudRenderer.ObjectCreation;
using BAPointCloudRenderer.Utility;
using System.Collections.Generic;
using UnityEngine;

class KatabasisMeshConfiguration : MeshConfiguration
{
    public PointCloudProfile profile = null;
    public Material material;
    public Camera renderCamera = null;
    public bool displayLOD = false;
    public Transform root;

    private HashSet<GameObject> gameObjectCollection = null;

    private void LoadShaders()
    {
        material.enableInstancing = true;

        if (renderCamera == null)
        {
            renderCamera = Camera.main;
        }
    }

    public void Start()
    {
        LoadShaders();
    }

    public void Update()
    {
        if (gameObjectCollection != null)
        {
            LoadShaders();
            foreach (GameObject go in gameObjectCollection)
            {
                go.GetComponent<MeshRenderer>().material = material;
            }
        }
        if (displayLOD)
        {
            foreach (GameObject go in gameObjectCollection)
            {
                BoundingBoxComponent bbc = go.GetComponent<BoundingBoxComponent>();
                BBDraw.DrawBoundingBox(bbc.boundingBox, bbc.parent, Color.red, false);
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

        if (gameObjectCollection != null)
        {
            gameObjectCollection.Add(gameObject);
        }

        gameObject.AddComponent<PointCloudBlock>().init(profile);
        gameObject.GetComponent<PointCloudBlock>().onKill += () =>
        {
            gameObjectCollection?.Remove(gameObject);
            Destroy(gameObject);
        };

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