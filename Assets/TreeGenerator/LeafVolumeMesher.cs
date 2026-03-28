using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional post-process for the canopy volume submesh after marching extraction.</summary>
public enum LeafVolumeGeometryOptimizeMode
{
    None,
    /// <summary>Drop near-zero-area triangles and compact unused canopy vertices.</summary>
    RemoveDegenerateTriangles,
    /// <summary>Merge canopy vertices that fall in the same quantization cell (epsilon).</summary>
    WeldVertices,
    /// <summary>Weld vertices, then remove degenerate triangles.</summary>
    WeldAndRemoveDegenerate,
    /// <summary>Stronger weld (coarser grid), then remove degenerates.</summary>
    AggressiveWeld,
    /// <summary>Weld + degenerate cleanup + remove duplicate triangles (same three indices).</summary>
    WeldDegenerateAndDedupe
}

/// <summary>
/// Builds a canopy volume mesh from leaf triangles using a sampled density field
/// and marching tetrahedra (compact marching-cubes alternative).
/// </summary>
public static class LeafVolumeMesher
{
    private static readonly int[,] TetEdges = new int[,]
    {
        { 0, 1 }, { 0, 2 }, { 0, 3 }, { 1, 2 }, { 1, 3 }, { 2, 3 }
    };

    private static readonly int[,] CubeCorners = new int[,]
    {
        { 0, 0, 0 }, { 1, 0, 0 }, { 1, 1, 0 }, { 0, 1, 0 },
        { 0, 0, 1 }, { 1, 0, 1 }, { 1, 1, 1 }, { 0, 1, 1 }
    };

    private static readonly int[,] CubeTets = new int[,]
    {
        { 0, 5, 1, 6 },
        { 0, 1, 2, 6 },
        { 0, 2, 3, 6 },
        { 0, 3, 7, 6 },
        { 0, 7, 4, 6 },
        { 0, 4, 5, 6 }
    };

    public static Mesh BuildCombinedWoodAndLeafVolume(
        Mesh source,
        int gridResolution,
        float sampleRadiusInVoxels,
        float isoLevel,
        int smoothIterations,
        float boundsPadding,
        LeafVolumeGeometryOptimizeMode geometryOptimize = LeafVolumeGeometryOptimizeMode.None,
        float weldEpsilon = 0.0025f,
        float minTriangleAreaSq = 0f,
        bool closeFieldHoles = true,
        int holeCloseRadius = 2,
        int smoothPassesAfterHoleClose = 1)
    {
        if (source == null || source.subMeshCount < 2)
            return null;

        int[] woodTris = source.GetTriangles(0);
        int[] leafTrisA = source.GetTriangles(1);
        int[] leafTrisB = source.subMeshCount > 2 ? source.GetTriangles(2) : System.Array.Empty<int>();
        if ((leafTrisA == null || leafTrisA.Length == 0) && (leafTrisB == null || leafTrisB.Length == 0))
            return null;

        Vector3[] srcVerts = source.vertices;
        if (srcVerts == null || srcVerts.Length == 0)
            return null;

        if (!CollectLeafBoundsAndSamples(srcVerts, leafTrisA, leafTrisB, out Bounds leafBounds, out List<Vector3> samples))
            return null;

        float maxExtent = Mathf.Max(leafBounds.size.x, Mathf.Max(leafBounds.size.y, leafBounds.size.z));
        if (maxExtent < 1e-5f)
            return null;

        int targetResolution = Mathf.Clamp(gridResolution, 8, 64);
        float voxelSize = maxExtent / Mathf.Max(2, targetResolution - 1);
        float padding = Mathf.Max(0f, boundsPadding) * maxExtent + voxelSize * 2f;
        Vector3 gridMin = leafBounds.min - Vector3.one * padding;
        Vector3 gridMax = leafBounds.max + Vector3.one * padding;

        int nx = Mathf.Clamp(Mathf.CeilToInt((gridMax.x - gridMin.x) / voxelSize) + 1, 6, 96);
        int ny = Mathf.Clamp(Mathf.CeilToInt((gridMax.y - gridMin.y) / voxelSize) + 1, 6, 96);
        int nz = Mathf.Clamp(Mathf.CeilToInt((gridMax.z - gridMin.z) / voxelSize) + 1, 6, 96);

        float[] density = new float[nx * ny * nz];
        SplatSamples(samples, density, nx, ny, nz, gridMin, voxelSize, Mathf.Max(0.25f, sampleRadiusInVoxels));
        NormalizeDensity(density);

        int blurPasses = Mathf.Clamp(smoothIterations, 0, 4);
        for (int i = 0; i < blurPasses; i++)
            BlurDensity(density, nx, ny, nz);

        float isoClamped = Mathf.Clamp01(isoLevel);
        if (closeFieldHoles)
        {
            int closePasses = Mathf.Clamp(holeCloseRadius, 1, 4);
            MorphologicalCloseDensityAboveIso(density, nx, ny, nz, isoClamped, closePasses);
            int afterCloseBlur = Mathf.Clamp(smoothPassesAfterHoleClose, 0, 3);
            for (int i = 0; i < afterCloseBlur; i++)
                BlurDensity(density, nx, ny, nz);
        }

        List<Vector3> volVerts = new List<Vector3>(4096);
        List<int> volTris = new List<int>(8192);
        GenerateIsosurface(density, nx, ny, nz, gridMin, voxelSize, isoClamped, leafBounds.center, volVerts, volTris);
        if (volTris.Count == 0 || volVerts.Count == 0)
            return null;

        // Build final mesh: keep wood as submesh 0, canopy volume as submesh 1, empty submesh 2.
        Vector2[] srcUv = source.uv;
        Color[] srcColors = source.colors;
        if (srcUv == null || srcUv.Length != srcVerts.Length)
            srcUv = BuildDefaultUvs(srcVerts.Length);
        if (srcColors == null || srcColors.Length != srcVerts.Length)
            srcColors = BuildDefaultColors(srcVerts.Length, Color.white);

        Color leafColor = EstimateLeafColor(srcColors, leafTrisA, leafTrisB);
        int baseVertex = srcVerts.Length;
        var finalVerts = new List<Vector3>(srcVerts.Length + volVerts.Count);
        var finalUvs = new List<Vector2>(srcVerts.Length + volVerts.Count);
        var finalColors = new List<Color>(srcVerts.Length + volVerts.Count);
        finalVerts.AddRange(srcVerts);
        finalUvs.AddRange(srcUv);
        finalColors.AddRange(srcColors);

        for (int i = 0; i < volVerts.Count; i++)
        {
            Vector3 v = volVerts[i];
            finalVerts.Add(v);
            float u = Mathf.InverseLerp(leafBounds.min.x, leafBounds.max.x, v.x);
            float vv = Mathf.InverseLerp(leafBounds.min.z, leafBounds.max.z, v.z);
            finalUvs.Add(new Vector2(u, vv));
            finalColors.Add(leafColor);
        }

        var finalLeafTris = new List<int>(volTris.Count);
        for (int i = 0; i < volTris.Count; i++)
            finalLeafTris.Add(baseVertex + volTris[i]);

        Mesh result = new Mesh { name = $"{source.name}_LeafVolume" };
        result.indexFormat = finalVerts.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        result.SetVertices(finalVerts);
        result.SetUVs(0, finalUvs);
        result.SetColors(finalColors);
        result.subMeshCount = 3;
        result.SetTriangles(woodTris, 0, true);
        result.SetTriangles(finalLeafTris, 1, true);
        result.SetTriangles(System.Array.Empty<int>(), 2, true);
        ApplyCanopySubmeshOptimization(
            result,
            srcVerts.Length,
            geometryOptimize,
            weldEpsilon,
            minTriangleAreaSq);
        result.RecalculateNormals();
        result.RecalculateBounds();
        return result;
    }

    private static void ApplyCanopySubmeshOptimization(
        Mesh mesh,
        int woodVertexCount,
        LeafVolumeGeometryOptimizeMode mode,
        float weldEpsilon,
        float minTriangleAreaSq)
    {
        if (mesh == null || mode == LeafVolumeGeometryOptimizeMode.None)
            return;

        float areaThreshold = minTriangleAreaSq > 0f ? minTriangleAreaSq : 1e-10f;
        Vector3[] verts = mesh.vertices;
        Vector2[] uvs = mesh.uv;
        Color[] colors = mesh.colors;
        int[] woodTris = mesh.GetTriangles(0);
        int[] leafTris = mesh.GetTriangles(1);
        if (leafTris == null || leafTris.Length < 3 || verts == null || verts.Length < woodVertexCount)
            return;

        if (uvs == null || uvs.Length != verts.Length)
            uvs = BuildDefaultUvs(verts.Length);
        if (colors == null || colors.Length != verts.Length)
            colors = BuildDefaultColors(verts.Length, Color.white);

        bool weld = mode == LeafVolumeGeometryOptimizeMode.WeldVertices
                    || mode == LeafVolumeGeometryOptimizeMode.WeldAndRemoveDegenerate
                    || mode == LeafVolumeGeometryOptimizeMode.AggressiveWeld
                    || mode == LeafVolumeGeometryOptimizeMode.WeldDegenerateAndDedupe;
        bool removeDegenerate = mode != LeafVolumeGeometryOptimizeMode.None
                                && mode != LeafVolumeGeometryOptimizeMode.WeldVertices;
        bool dedupe = mode == LeafVolumeGeometryOptimizeMode.WeldDegenerateAndDedupe;

        float eps = Mathf.Max(1e-6f, weldEpsilon);
        if (mode == LeafVolumeGeometryOptimizeMode.AggressiveWeld)
            eps *= 2.75f;

        float invEps = 1f / eps;

        var woodVertList = new List<Vector3>(woodVertexCount);
        var woodUvList = new List<Vector2>(woodVertexCount);
        var woodColList = new List<Color>(woodVertexCount);
        for (int i = 0; i < woodVertexCount && i < verts.Length; i++)
        {
            woodVertList.Add(verts[i]);
            woodUvList.Add(uvs[i]);
            woodColList.Add(colors[i]);
        }

        var newLeafVerts = new List<Vector3>();
        var newLeafUvs = new List<Vector2>();
        var newLeafCols = new List<Color>();
        var newLeafTris = new List<int>(leafTris.Length);

        if (weld)
        {
            var keyToCompact = new Dictionary<GridKey, int>();
            var globalRemap = new int[verts.Length];
            for (int i = 0; i < globalRemap.Length; i++)
                globalRemap[i] = -1;

            int MapGlobalToCompact(int g)
            {
                if (g < 0 || g >= verts.Length)
                    return -1;
                if (globalRemap[g] >= 0)
                    return globalRemap[g];

                var key = new GridKey(verts[g], invEps);
                if (keyToCompact.TryGetValue(key, out int existing))
                {
                    globalRemap[g] = existing;
                    return existing;
                }

                int ni = newLeafVerts.Count;
                newLeafVerts.Add(verts[g]);
                newLeafUvs.Add(uvs[g]);
                newLeafCols.Add(colors[g]);
                keyToCompact[key] = ni;
                globalRemap[g] = ni;
                return ni;
            }

            for (int t = 0; t <= leafTris.Length - 3; t += 3)
            {
                int a = leafTris[t];
                int b = leafTris[t + 1];
                int c = leafTris[t + 2];
                if (a < woodVertexCount || b < woodVertexCount || c < woodVertexCount)
                    continue;

                int ca = MapGlobalToCompact(a);
                int cb = MapGlobalToCompact(b);
                int cc = MapGlobalToCompact(c);
                if (ca < 0 || cb < 0 || cc < 0)
                    continue;

                int ga = woodVertexCount + ca;
                int gb = woodVertexCount + cb;
                int gc = woodVertexCount + cc;

                if (removeDegenerate && TriangleAreaSq(newLeafVerts[ca], newLeafVerts[cb], newLeafVerts[cc]) < areaThreshold)
                    continue;

                newLeafTris.Add(ga);
                newLeafTris.Add(gb);
                newLeafTris.Add(gc);
            }
        }
        else
        {
            for (int t = 0; t <= leafTris.Length - 3; t += 3)
            {
                int a = leafTris[t];
                int b = leafTris[t + 1];
                int c = leafTris[t + 2];
                if (a < woodVertexCount || b < woodVertexCount || c < woodVertexCount)
                    continue;
                if (TriangleAreaSq(verts[a], verts[b], verts[c]) < areaThreshold)
                    continue;
                newLeafTris.Add(a);
                newLeafTris.Add(b);
                newLeafTris.Add(c);
            }

            var oldToNew = new int[verts.Length];
            for (int i = 0; i < oldToNew.Length; i++)
                oldToNew[i] = -1;
            for (int i = 0; i < woodVertexCount; i++)
                oldToNew[i] = i;

            int next = woodVertexCount;
            for (int i = 0; i < newLeafTris.Count; i++)
            {
                int idx = newLeafTris[i];
                if (oldToNew[idx] >= 0)
                    continue;
                oldToNew[idx] = next++;
            }

            int leafSlotCount = next - woodVertexCount;
            for (int li = 0; li < leafSlotCount; li++)
            {
                newLeafVerts.Add(default);
                newLeafUvs.Add(default);
                newLeafCols.Add(default);
            }

            for (int g = woodVertexCount; g < verts.Length; g++)
            {
                int ni = oldToNew[g];
                if (ni < 0)
                    continue;
                int li = ni - woodVertexCount;
                newLeafVerts[li] = verts[g];
                newLeafUvs[li] = uvs[g];
                newLeafCols[li] = colors[g];
            }

            for (int i = 0; i < newLeafTris.Count; i++)
                newLeafTris[i] = oldToNew[newLeafTris[i]];
        }

        if (dedupe && newLeafTris.Count >= 3)
            RemoveDuplicateTriangles(newLeafTris);

        int totalVerts = woodVertList.Count + newLeafVerts.Count;
        var allVerts = new List<Vector3>(totalVerts);
        var allUvs = new List<Vector2>(totalVerts);
        var allCols = new List<Color>(totalVerts);
        allVerts.AddRange(woodVertList);
        allUvs.AddRange(woodUvList);
        allCols.AddRange(woodColList);
        allVerts.AddRange(newLeafVerts);
        allUvs.AddRange(newLeafUvs);
        allCols.AddRange(newLeafCols);

        mesh.indexFormat = totalVerts > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.Clear();
        mesh.SetVertices(allVerts);
        mesh.SetUVs(0, allUvs);
        mesh.SetColors(allCols);
        mesh.subMeshCount = 3;
        mesh.SetTriangles(woodTris, 0, true);
        mesh.SetTriangles(newLeafTris, 1, true);
        mesh.SetTriangles(System.Array.Empty<int>(), 2, true);
    }

    private static float TriangleAreaSq(Vector3 a, Vector3 b, Vector3 c) =>
        Vector3.Cross(b - a, c - a).sqrMagnitude;

    private static void RemoveDuplicateTriangles(List<int> tris)
    {
        var seen = new HashSet<long>();
        int write = 0;
        for (int t = 0; t <= tris.Count - 3; t += 3)
        {
            int a = tris[t];
            int b = tris[t + 1];
            int c = tris[t + 2];
            Sort3(ref a, ref b, ref c);
            long key = a + ((long)b << 21) + ((long)c << 42);
            if (!seen.Add(key))
                continue;
            if (write != t)
            {
                tris[write] = tris[t];
                tris[write + 1] = tris[t + 1];
                tris[write + 2] = tris[t + 2];
            }
            write += 3;
        }
        if (write < tris.Count)
            tris.RemoveRange(write, tris.Count - write);
    }

    private static void Sort3(ref int a, ref int b, ref int c)
    {
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) (a, b) = (b, a);
    }

    private readonly struct GridKey : System.IEquatable<GridKey>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public GridKey(Vector3 v, float invEps)
        {
            X = Mathf.RoundToInt(v.x * invEps);
            Y = Mathf.RoundToInt(v.y * invEps);
            Z = Mathf.RoundToInt(v.z * invEps);
        }

        public bool Equals(GridKey o) => X == o.X && Y == o.Y && Z == o.Z;

        public override int GetHashCode() => ((X * 73856093) ^ (Y * 19349663)) ^ (Z * 83492791);
    }

    private static bool CollectLeafBoundsAndSamples(
        Vector3[] verts,
        int[] leafTrisA,
        int[] leafTrisB,
        out Bounds bounds,
        out List<Vector3> samples)
    {
        var localSamples = new List<Vector3>(8192);
        bool hasAny = false;
        Bounds localBounds = new Bounds();

        void ConsumeTriList(int[] tris)
        {
            if (tris == null || tris.Length < 3) return;
            for (int i = 0; i <= tris.Length - 3; i += 3)
            {
                int i0 = tris[i];
                int i1 = tris[i + 1];
                int i2 = tris[i + 2];
                if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= verts.Length || i1 >= verts.Length || i2 >= verts.Length)
                    continue;
                Vector3 v0 = verts[i0];
                Vector3 v1 = verts[i1];
                Vector3 v2 = verts[i2];
                if (!hasAny)
                {
                    localBounds = new Bounds(v0, Vector3.zero);
                    hasAny = true;
                }
                localBounds.Encapsulate(v1);
                localBounds.Encapsulate(v2);
                AddTriangleSamples(v0, v1, v2, localSamples);
            }
        }

        ConsumeTriList(leafTrisA);
        ConsumeTriList(leafTrisB);
        bounds = localBounds;
        samples = localSamples;
        return hasAny && localSamples.Count > 0;
    }

    private static void AddTriangleSamples(Vector3 v0, Vector3 v1, Vector3 v2, List<Vector3> outSamples)
    {
        outSamples.Add(v0);
        outSamples.Add(v1);
        outSamples.Add(v2);
        outSamples.Add((v0 + v1 + v2) / 3f);
        outSamples.Add((v0 + v1) * 0.5f);
        outSamples.Add((v1 + v2) * 0.5f);
        outSamples.Add((v2 + v0) * 0.5f);
    }

    private static void SplatSamples(
        List<Vector3> samples,
        float[] density,
        int nx,
        int ny,
        int nz,
        Vector3 gridMin,
        float voxelSize,
        float radiusInVoxels)
    {
        float radius = Mathf.Max(voxelSize * 0.5f, radiusInVoxels * voxelSize);
        float r2 = radius * radius;
        int rCells = Mathf.CeilToInt(radius / voxelSize);

        for (int i = 0; i < samples.Count; i++)
        {
            Vector3 p = samples[i];
            float gx = (p.x - gridMin.x) / voxelSize;
            float gy = (p.y - gridMin.y) / voxelSize;
            float gz = (p.z - gridMin.z) / voxelSize;
            int minX = Mathf.Clamp(Mathf.FloorToInt(gx) - rCells, 0, nx - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(gx) + rCells, 0, nx - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(gy) - rCells, 0, ny - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(gy) + rCells, 0, ny - 1);
            int minZ = Mathf.Clamp(Mathf.FloorToInt(gz) - rCells, 0, nz - 1);
            int maxZ = Mathf.Clamp(Mathf.CeilToInt(gz) + rCells, 0, nz - 1);

            for (int z = minZ; z <= maxZ; z++)
            {
                float pz = gridMin.z + z * voxelSize;
                for (int y = minY; y <= maxY; y++)
                {
                    float py = gridMin.y + y * voxelSize;
                    for (int x = minX; x <= maxX; x++)
                    {
                        float px = gridMin.x + x * voxelSize;
                        float dx = px - p.x;
                        float dy = py - p.y;
                        float dz = pz - p.z;
                        float d2 = dx * dx + dy * dy + dz * dz;
                        if (d2 > r2) continue;
                        float w = 1f - Mathf.Sqrt(d2) / radius;
                        int idx = FlattenIndex(x, y, z, nx, ny);
                        density[idx] += w;
                    }
                }
            }
        }
    }

    private static void NormalizeDensity(float[] density)
    {
        float max = 0f;
        for (int i = 0; i < density.Length; i++)
            if (density[i] > max) max = density[i];
        if (max < 1e-6f) return;
        float inv = 1f / max;
        for (int i = 0; i < density.Length; i++)
            density[i] *= inv;
    }

    private static void BlurDensity(float[] density, int nx, int ny, int nz)
    {
        float[] src = (float[])density.Clone();
        for (int z = 1; z < nz - 1; z++)
        {
            for (int y = 1; y < ny - 1; y++)
            {
                for (int x = 1; x < nx - 1; x++)
                {
                    int idx = FlattenIndex(x, y, z, nx, ny);
                    float c = src[idx];
                    float sum =
                        src[FlattenIndex(x - 1, y, z, nx, ny)] +
                        src[FlattenIndex(x + 1, y, z, nx, ny)] +
                        src[FlattenIndex(x, y - 1, z, nx, ny)] +
                        src[FlattenIndex(x, y + 1, z, nx, ny)] +
                        src[FlattenIndex(x, y, z - 1, nx, ny)] +
                        src[FlattenIndex(x, y, z + 1, nx, ny)];
                    density[idx] = Mathf.Lerp(c, (c + sum) / 7f, 0.7f);
                }
            }
        }
    }

    /// <summary>
    /// Morphological closing on the inside mask (density &gt;= iso): dilate then erode in 26-neighborhood.
    /// Bridges sub-voxel gaps in the scalar field so marching extraction has fewer holes.
    /// </summary>
    private static void MorphologicalCloseDensityAboveIso(
        float[] density,
        int nx,
        int ny,
        int nz,
        float iso,
        int passes)
    {
        int n = nx * ny * nz;
        if (density == null || density.Length != n || passes <= 0)
            return;

        var bufA = new bool[n];
        var bufB = new bool[n];
        for (int i = 0; i < n; i++)
            bufA[i] = density[i] >= iso;

        bool[] cur = bufA;
        bool[] next = bufB;
        for (int p = 0; p < passes; p++)
        {
            Dilate26(cur, next, nx, ny, nz);
            bool[] tmp = cur;
            cur = next;
            next = tmp;
        }

        for (int p = 0; p < passes; p++)
        {
            Erode26(cur, next, nx, ny, nz);
            bool[] tmp = cur;
            cur = next;
            next = tmp;
        }

        float boost = Mathf.Min(0.98f, iso + Mathf.Max(0.025f, (1f - iso) * 0.12f));
        for (int i = 0; i < n; i++)
        {
            if (cur[i])
                density[i] = Mathf.Max(density[i], boost);
        }
    }

    private static void Dilate26(bool[] src, bool[] dst, int nx, int ny, int nz)
    {
        for (int z = 0; z < nz; z++)
        {
            for (int y = 0; y < ny; y++)
            {
                for (int x = 0; x < nx; x++)
                {
                    int idx = FlattenIndex(x, y, z, nx, ny);
                    if (src[idx])
                    {
                        dst[idx] = true;
                        continue;
                    }

                    bool on = false;
                    for (int dz = -1; dz <= 1 && !on; dz++)
                    {
                        int zz = z + dz;
                        if (zz < 0 || zz >= nz) continue;
                        for (int dy = -1; dy <= 1 && !on; dy++)
                        {
                            int yy = y + dy;
                            if (yy < 0 || yy >= ny) continue;
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0 && dz == 0) continue;
                                int xx = x + dx;
                                if (xx < 0 || xx >= nx) continue;
                                if (src[FlattenIndex(xx, yy, zz, nx, ny)])
                                {
                                    on = true;
                                    break;
                                }
                            }
                        }
                    }

                    dst[idx] = on;
                }
            }
        }
    }

    private static void Erode26(bool[] src, bool[] dst, int nx, int ny, int nz)
    {
        for (int z = 0; z < nz; z++)
        {
            for (int y = 0; y < ny; y++)
            {
                for (int x = 0; x < nx; x++)
                {
                    bool allOn = true;
                    for (int dz = -1; dz <= 1 && allOn; dz++)
                    {
                        int zz = z + dz;
                        if (zz < 0 || zz >= nz)
                        {
                            allOn = false;
                            break;
                        }

                        for (int dy = -1; dy <= 1 && allOn; dy++)
                        {
                            int yy = y + dy;
                            if (yy < 0 || yy >= ny)
                            {
                                allOn = false;
                                break;
                            }

                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx;
                                if (xx < 0 || xx >= nx)
                                {
                                    allOn = false;
                                    break;
                                }

                                if (!src[FlattenIndex(xx, yy, zz, nx, ny)])
                                {
                                    allOn = false;
                                    break;
                                }
                            }
                        }
                    }

                    dst[FlattenIndex(x, y, z, nx, ny)] = allOn;
                }
            }
        }
    }

    private static void GenerateIsosurface(
        float[] density,
        int nx,
        int ny,
        int nz,
        Vector3 gridMin,
        float voxelSize,
        float iso,
        Vector3 boundsCenter,
        List<Vector3> outVerts,
        List<int> outTris)
    {
        Vector3[] cubePos = new Vector3[8];
        float[] cubeVal = new float[8];
        Vector3[] tetPos = new Vector3[4];
        float[] tetVal = new float[4];
        var poly = new List<Vector3>(4);
        Vector3 gridMid = gridMin + new Vector3((nx - 1) * 0.5f, (ny - 1) * 0.5f, (nz - 1) * 0.5f) * voxelSize;
        Vector3 radial = gridMid - boundsCenter;
        Vector3 fallbackOutwardHint = radial.sqrMagnitude > 1e-10f ? radial.normalized : Vector3.up;

        for (int z = 0; z < nz - 1; z++)
        {
            for (int y = 0; y < ny - 1; y++)
            {
                for (int x = 0; x < nx - 1; x++)
                {
                    for (int c = 0; c < 8; c++)
                    {
                        int cx = x + CubeCorners[c, 0];
                        int cy = y + CubeCorners[c, 1];
                        int cz = z + CubeCorners[c, 2];
                        cubePos[c] = gridMin + new Vector3(cx, cy, cz) * voxelSize;
                        cubeVal[c] = density[FlattenIndex(cx, cy, cz, nx, ny)];
                    }

                    for (int t = 0; t < 6; t++)
                    {
                        for (int k = 0; k < 4; k++)
                        {
                            int idx = CubeTets[t, k];
                            tetPos[k] = cubePos[idx];
                            tetVal[k] = cubeVal[idx];
                        }

                        PolygonizeTet(
                            tetPos,
                            tetVal,
                            iso,
                            density,
                            nx,
                            ny,
                            nz,
                            gridMin,
                            voxelSize,
                            fallbackOutwardHint,
                            outVerts,
                            outTris,
                            poly);
                    }
                }
            }
        }

        float weldEps = Mathf.Max(voxelSize * 1e-4f, 1e-6f);
        WeldIsosurfaceVertices(outVerts, outTris, weldEps);
        RemoveNearZeroAreaTriangles(outVerts, outTris);
    }

    private static void PolygonizeTet(
        Vector3[] p,
        float[] v,
        float iso,
        float[] density,
        int nx,
        int ny,
        int nz,
        Vector3 gridMin,
        float voxelSize,
        Vector3 fallbackOutwardHint,
        List<Vector3> outVerts,
        List<int> outTris,
        List<Vector3> poly)
    {
        poly.Clear();
        for (int e = 0; e < 6; e++)
        {
            int a = TetEdges[e, 0];
            int b = TetEdges[e, 1];
            bool inA = v[a] >= iso;
            bool inB = v[b] >= iso;
            if (inA == inB) continue;

            float va = v[a];
            float vb = v[b];
            float t = Mathf.Abs(vb - va) > 1e-6f ? (iso - va) / (vb - va) : 0.5f;
            poly.Add(Vector3.Lerp(p[a], p[b], Mathf.Clamp01(t)));
        }

        if (poly.Count == 3)
        {
            AddGradientOrientedTriangle(
                poly[0], poly[1], poly[2],
                density, nx, ny, nz, gridMin, voxelSize, fallbackOutwardHint,
                outVerts, outTris);
        }
        else if (poly.Count == 4)
        {
            AddGradientOrientedTriangle(
                poly[0], poly[1], poly[2],
                density, nx, ny, nz, gridMin, voxelSize, fallbackOutwardHint,
                outVerts, outTris);
            AddGradientOrientedTriangle(
                poly[0], poly[2], poly[3],
                density, nx, ny, nz, gridMin, voxelSize, fallbackOutwardHint,
                outVerts, outTris);
        }
    }

    /// <summary>
    /// Outward = toward lower density (air). Uses -∇ρ; avoids bounds-center heuristic that inverts neighbors.
    /// </summary>
    private static void AddGradientOrientedTriangle(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        float[] density,
        int nx,
        int ny,
        int nz,
        Vector3 gridMin,
        float voxelSize,
        Vector3 fallbackOutwardHint,
        List<Vector3> outVerts,
        List<int> outTris)
    {
        Vector3 centroid = (a + b + c) / 3f;
        Vector3 triN = Vector3.Cross(b - a, c - a);
        if (triN.sqrMagnitude > 1e-14f)
        {
            triN.Normalize();
            Vector3 grad = SampleDensityGradientWorld(centroid, density, nx, ny, nz, gridMin, voxelSize);
            Vector3 outward = grad.sqrMagnitude > 1e-18f
                ? (-grad).normalized
                : fallbackOutwardHint.sqrMagnitude > 1e-18f
                    ? fallbackOutwardHint.normalized
                    : Vector3.up;

            if (Vector3.Dot(triN, outward) < 0f)
            {
                Vector3 tmp = b;
                b = c;
                c = tmp;
            }
        }

        int baseIndex = outVerts.Count;
        outVerts.Add(a);
        outVerts.Add(b);
        outVerts.Add(c);
        outTris.Add(baseIndex);
        outTris.Add(baseIndex + 1);
        outTris.Add(baseIndex + 2);
    }

    private static float SampleDensityIndex(float[] d, int x, int y, int z, int nx, int ny, int nz)
    {
        x = Mathf.Clamp(x, 0, nx - 1);
        y = Mathf.Clamp(y, 0, ny - 1);
        z = Mathf.Clamp(z, 0, nz - 1);
        return d[FlattenIndex(x, y, z, nx, ny)];
    }

    /// <summary>∇ρ in world space (central differences on grid).</summary>
    private static Vector3 SampleDensityGradientWorld(
        Vector3 world,
        float[] density,
        int nx,
        int ny,
        int nz,
        Vector3 gridMin,
        float voxelSize)
    {
        if (voxelSize < 1e-8f || density == null)
            return Vector3.zero;

        Vector3 g = (world - gridMin) / voxelSize;
        int x = Mathf.FloorToInt(g.x);
        int y = Mathf.FloorToInt(g.y);
        int z = Mathf.FloorToInt(g.z);

        float dRhoDx = (SampleDensityIndex(density, x + 1, y, z, nx, ny, nz)
                        - SampleDensityIndex(density, x - 1, y, z, nx, ny, nz)) * 0.5f;
        float dRhoDy = (SampleDensityIndex(density, x, y + 1, z, nx, ny, nz)
                        - SampleDensityIndex(density, x, y - 1, z, nx, ny, nz)) * 0.5f;
        float dRhoDz = (SampleDensityIndex(density, x, y, z + 1, nx, ny, nz)
                        - SampleDensityIndex(density, x, y, z - 1, nx, ny, nz)) * 0.5f;

        float invVs = 1f / voxelSize;
        return new Vector3(dRhoDx * invVs, dRhoDy * invVs, dRhoDz * invVs);
    }

    private static void WeldIsosurfaceVertices(List<Vector3> verts, List<int> tris, float epsilon)
    {
        if (verts == null || tris == null || verts.Count == 0 || tris.Count < 3)
            return;

        float invEps = 1f / Mathf.Max(epsilon, 1e-10f);
        var keyToNew = new Dictionary<IsoWeldKey, int>();
        var oldToNew = new int[verts.Count];
        var welded = new List<Vector3>();

        for (int i = 0; i < verts.Count; i++)
        {
            var key = new IsoWeldKey(verts[i], invEps);
            if (!keyToNew.TryGetValue(key, out int ni))
            {
                ni = welded.Count;
                keyToNew[key] = ni;
                welded.Add(verts[i]);
            }

            oldToNew[i] = ni;
        }

        for (int i = 0; i < tris.Count; i++)
            tris[i] = oldToNew[tris[i]];

        verts.Clear();
        verts.AddRange(welded);
    }

    private static void RemoveNearZeroAreaTriangles(List<Vector3> verts, List<int> tris)
    {
        if (verts == null || tris == null || tris.Count < 3)
            return;

        const float minAreaSq = 1e-14f;
        int w = 0;
        for (int t = 0; t <= tris.Count - 3; t += 3)
        {
            int a = tris[t];
            int b = tris[t + 1];
            int c = tris[t + 2];
            if (a == b || b == c || a == c)
                continue;
            if (TriangleAreaSq(verts[a], verts[b], verts[c]) < minAreaSq)
                continue;
            tris[w++] = a;
            tris[w++] = b;
            tris[w++] = c;
        }

        if (w < tris.Count)
            tris.RemoveRange(w, tris.Count - w);
    }

    private readonly struct IsoWeldKey : System.IEquatable<IsoWeldKey>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public IsoWeldKey(Vector3 v, float invEps)
        {
            X = Mathf.RoundToInt(v.x * invEps);
            Y = Mathf.RoundToInt(v.y * invEps);
            Z = Mathf.RoundToInt(v.z * invEps);
        }

        public bool Equals(IsoWeldKey o) => X == o.X && Y == o.Y && Z == o.Z;

        public override int GetHashCode() => ((X * 73856093) ^ (Y * 19349663)) ^ (Z * 83492791);
    }

    private static int FlattenIndex(int x, int y, int z, int nx, int ny) => x + nx * (y + ny * z);

    private static Vector2[] BuildDefaultUvs(int count)
    {
        Vector2[] uv = new Vector2[count];
        for (int i = 0; i < count; i++) uv[i] = Vector2.zero;
        return uv;
    }

    private static Color[] BuildDefaultColors(int count, Color color)
    {
        Color[] colors = new Color[count];
        for (int i = 0; i < count; i++) colors[i] = color;
        return colors;
    }

    private static Color EstimateLeafColor(Color[] srcColors, int[] leafTrisA, int[] leafTrisB)
    {
        Color sum = Color.black;
        int count = 0;

        void Consume(int[] tris)
        {
            if (tris == null) return;
            for (int i = 0; i < tris.Length; i++)
            {
                int idx = tris[i];
                if (idx < 0 || idx >= srcColors.Length) continue;
                sum += srcColors[idx];
                count++;
            }
        }

        Consume(leafTrisA);
        Consume(leafTrisB);
        return count > 0 ? (sum / count) : new Color(0.2f, 0.6f, 0.2f, 1f);
    }
}

