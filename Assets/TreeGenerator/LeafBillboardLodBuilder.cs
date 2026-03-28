using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds camera-facing billboard quads for distant LOD canopy from LOD0 leaf triangle samples.
/// </summary>
public static class LeafBillboardLodBuilder
{
    public static List<Vector3> CollectLeafTriangleCentroidsWorld(Mesh mesh, Transform root)
    {
        var list = new List<Vector3>(1024);
        if (mesh == null || root == null)
            return list;

        Vector3[] verts = mesh.vertices;
        if (verts == null || verts.Length == 0)
            return list;

        for (int sm = 1; sm < mesh.subMeshCount && sm <= 2; sm++)
        {
            int[] tris = mesh.GetTriangles(sm);
            if (tris == null || tris.Length < 3)
                continue;

            for (int i = 0; i <= tris.Length - 3; i += 3)
            {
                int i0 = tris[i];
                int i1 = tris[i + 1];
                int i2 = tris[i + 2];
                if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= verts.Length || i1 >= verts.Length || i2 >= verts.Length)
                    continue;

                Vector3 c = (verts[i0] + verts[i1] + verts[i2]) / 3f;
                list.Add(root.TransformPoint(c));
            }
        }

        return list;
    }

    public static void SubsampleCentroids(IReadOnlyList<Vector3> src, List<Vector3> dst, int maxCount, int seed)
    {
        dst.Clear();
        if (src == null || src.Count == 0 || maxCount <= 0)
            return;

        if (src.Count <= maxCount)
        {
            for (int i = 0; i < src.Count; i++)
                dst.Add(src[i]);
            return;
        }

        var rng = new System.Random(seed ^ unchecked((int)0x9E3779B9));
        var pick = new int[src.Count];
        for (int i = 0; i < pick.Length; i++)
            pick[i] = i;

        for (int i = 0; i < maxCount; i++)
        {
            int j = rng.Next(i, pick.Length);
            (pick[i], pick[j]) = (pick[j], pick[i]);
        }

        for (int i = 0; i < maxCount; i++)
            dst.Add(src[pick[i]]);
    }

    /// <summary>
    /// Quads in parent space: vertex.xy = offset from center; UV1.xyz = center in parent local space (per vertex).
    /// </summary>
    public static Mesh BuildBillboardQuadMesh(
        IReadOnlyList<Vector3> centroidsWorld,
        Transform parent,
        float quadWidth,
        float quadHeight,
        int seed,
        float jitterWorldRadius)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var uv1 = new List<Vector3>();
        var tris = new List<int>(centroidsWorld.Count * 6);

        float hw = quadWidth * 0.5f;
        float hh = quadHeight * 0.5f;
        var rng = new System.Random(seed ^ 0x27D4EB2F);

        for (int i = 0; i < centroidsWorld.Count; i++)
        {
            Vector3 w = centroidsWorld[i];
            if (jitterWorldRadius > 1e-6f)
            {
                Vector3 j = RandomUnitSphere(rng) * (jitterWorldRadius * (float)rng.NextDouble());
                w += j;
            }

            Vector3 centerLocal = parent.InverseTransformPoint(w);

            void AddCorner(float ox, float oy, float u, float v)
            {
                verts.Add(new Vector3(ox, oy, 0f));
                uvs.Add(new Vector2(u, v));
                uv1.Add(centerLocal);
            }

            int b = verts.Count;
            AddCorner(-hw, -hh, 0f, 0f);
            AddCorner(hw, -hh, 1f, 0f);
            AddCorner(hw, hh, 1f, 1f);
            AddCorner(-hw, hh, 0f, 1f);

            tris.Add(b);
            tris.Add(b + 1);
            tris.Add(b + 2);
            tris.Add(b);
            tris.Add(b + 2);
            tris.Add(b + 3);
        }

        if (verts.Count == 0)
            return null;

        var mesh = new Mesh { name = "BillboardCanopy" };
        mesh.indexFormat = verts.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uv1);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Vector3 RandomUnitSphere(System.Random rng)
    {
        float z = (float)rng.NextDouble() * 2f - 1f;
        float t = (float)(rng.NextDouble() * System.Math.PI * 2);
        float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
        return new Vector3(r * Mathf.Cos(t), r * Mathf.Sin(t), z);
    }
}
