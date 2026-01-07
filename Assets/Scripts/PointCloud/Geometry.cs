
using UnityEngine;
using System.Collections.Generic;

public class Geometry
{
    static public Mesh Particles(int count, Vector2 quadSubdivision, Vector3 boundsSize)
    {
        // Mesh generation
        Mesh mesh = new Mesh();
        mesh.name = "Generated Mesh";
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        // Quad attributes
        int[] triangles = new int[] { 0, 3, 1, 3, 0, 2 };
        Vector2[] uv = new Vector2[] {
            new Vector2(-1f, -1f),
            new Vector2(1f, -1f),
            new Vector2(-1f, 1f),
            new Vector2(1f, 1f)
        };

        // Subdivide quad
        if (quadSubdivision.x > 1 || quadSubdivision.y > 1)
        {
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();
            for (int y = 0; y < quadSubdivision.y + 1; ++y)
            {
                for (int x = 0; x < quadSubdivision.x + 1; ++x)
                {
                    uvs.Add(new Vector2(((float)x / (float)quadSubdivision.x) * 2f - 1f, ((float)y / (float)quadSubdivision.y) * 2f - 1f));
                }
            }
            int vIndex = 0;
            for (int y = 0; y < quadSubdivision.y; ++y)
            {
                for (int x = 0; x < quadSubdivision.x; ++x)
                {
                    tris.Add(vIndex);
                    tris.Add(vIndex + 1);
                    tris.Add(vIndex + 1 + (int)quadSubdivision.x);
                    tris.Add(vIndex + 1 + (int)quadSubdivision.x);
                    tris.Add(vIndex + 1);
                    tris.Add(vIndex + 2 + (int)quadSubdivision.x);
                    vIndex += 1;
                }
                vIndex += 1;
            }
            uv = uvs.ToArray();
            triangles = tris.ToArray();
        }

        // Generated attributes
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> coordinates = new List<Vector2>();
        List<Vector2> quantity = new List<Vector2>();
        List<int> indices = new List<int>();

        for (int index = 0; index < count; ++index)
        {
            Vector3 position = Random.insideUnitSphere;
            Vector2 q = new Vector2((float)index / (float)(count - 1), index);
            // Fill attributes
            for (int v = 0; v < uv.Length; ++v)
            {
                vertices.Add(position);
                coordinates.Add(uv[v]);
                quantity.Add(q);
            }

            // Triangle indices
            for (int i = 0; i < triangles.Length; ++i)
            {
                indices.Add(index * uv.Length + triangles[i]);
            }
        }

        mesh.vertices = vertices.ToArray();
        mesh.uv = coordinates.ToArray();
        mesh.uv2 = quantity.ToArray();
        mesh.SetIndices(indices, MeshTopology.Triangles, 0);

        mesh.bounds = new Bounds(Vector3.zero, boundsSize);

        return mesh;
    }

    static public Mesh Quads(Vector3[] positions, Color[] colors, Vector2 quadSubdivision, Vector3 boundsSize)
    {
        // Mesh generation
        Mesh mesh = new Mesh();
        mesh.name = "Generated Mesh";
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        // Quad attributes
        int[] triangles = new int[] { 0, 3, 1, 3, 0, 2 };
        Vector2[] uv = new Vector2[] {
            new Vector2(-1f, -1f),
            new Vector2(1f, -1f),
            new Vector2(-1f, 1f),
            new Vector2(1f, 1f)
        };

        // Subdivide quad
        if (quadSubdivision.x > 1 || quadSubdivision.y > 1)
        {
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();
            for (int y = 0; y < quadSubdivision.y + 1; ++y)
            {
                for (int x = 0; x < quadSubdivision.x + 1; ++x)
                {
                    uvs.Add(new Vector2(((float)x / (float)quadSubdivision.x) * 2f - 1f, ((float)y / (float)quadSubdivision.y) * 2f - 1f));
                }
            }
            int vIndex = 0;
            for (int y = 0; y < quadSubdivision.y; ++y)
            {
                for (int x = 0; x < quadSubdivision.x; ++x)
                {
                    tris.Add(vIndex);
                    tris.Add(vIndex + 1);
                    tris.Add(vIndex + 1 + (int)quadSubdivision.x);
                    tris.Add(vIndex + 1 + (int)quadSubdivision.x);
                    tris.Add(vIndex + 1);
                    tris.Add(vIndex + 2 + (int)quadSubdivision.x);
                    vIndex += 1;
                }
                vIndex += 1;
            }
            uv = uvs.ToArray();
            triangles = tris.ToArray();
        }

        // Generated attributes
        int count = positions.Length;
        int attrSize = count * uv.Length;
        Vector3[] vertices = new Vector3[attrSize];
        Color[] tints = new Color[attrSize];
        Vector2[] coordinates = new Vector2[attrSize];
        Vector2[] quantity = new Vector2[attrSize];
        int[] indices = new int[count * triangles.Length];

        for (int index = 0; index < count; ++index)
        {
            Vector3 position = positions[index];
            Color c = colors[index];
            Vector2 q = new Vector2((float)index / (float)(count - 1), index);
            // Fill attributes
            int basIdx = index * uv.Length;
            for (int v = 0; v < uv.Length; ++v)
            {
                vertices[basIdx + v] = position;
                tints[basIdx + v] = c;
                coordinates[basIdx + v] = uv[v];
                quantity[basIdx + v] = q;
            }

            // Triangle indices
            for (int i = 0; i < triangles.Length; ++i)
            {
                indices[index * triangles.Length + i] = index * uv.Length + triangles[i];
            }
        }

        mesh.vertices = vertices;
        mesh.colors = tints;
        mesh.uv = coordinates;
        mesh.uv2 = quantity;
        mesh.SetIndices(indices, MeshTopology.Triangles, 0);

        mesh.bounds = new Bounds(Vector3.zero, boundsSize);

        return mesh;
    }

    static public Mesh Clone(Mesh[] meshesToClone, int count, Vector3 boundsSize)
    {
        // Mesh generation
        Mesh mesh = new Mesh();
        mesh.name = "Generated Mesh";
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        // Generated attributes
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> coordinates = new List<Vector2>();
        List<Vector2> quantity = new List<Vector2>();
        List<int> indices = new List<int>();

        int indexOffset = 0;

        for (int index = 0; index < count; ++index)
        {
            // random choice of mesh
            Mesh meshToClone = meshesToClone[Random.Range(0, meshesToClone.Length)];
            Vector2[] uvs = meshToClone.uv;
            Vector3[] positions = meshToClone.vertices;
            Vector3[] norms = meshToClone.normals;
            int[] triangles = meshToClone.triangles;

            // unique number of instance
            Vector2 q = new Vector2((float)index / (float)(count - 1), index);

            // Fill attributes
            for (int v = 0; v < uvs.Length; ++v)
            {
                vertices.Add(positions[v]);
                normals.Add(norms[v]);
                coordinates.Add(uvs[v]);
                quantity.Add(q);
            }

            // Triangle indices
            for (int i = 0; i < triangles.Length; ++i)
            {
                indices.Add(indexOffset + triangles[i]);
            }

            indexOffset += uvs.Length;
        }

        mesh.vertices = vertices.ToArray();
        mesh.normals = normals.ToArray();
        mesh.uv = coordinates.ToArray();
        mesh.uv2 = quantity.ToArray();
        mesh.SetIndices(indices, MeshTopology.Triangles, 0);

        mesh.bounds = new Bounds(Vector3.zero, boundsSize);

        return mesh;
    }
}