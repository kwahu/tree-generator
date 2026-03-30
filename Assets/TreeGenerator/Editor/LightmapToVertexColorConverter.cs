#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

    /// <summary>
/// Samples baked lightmaps at mesh lightmap UVs and multiplies irradiance into vertex colors (linear RGBM decode).
/// Vertex colors are written with Mesh.SetColors; UV channels are captured first and re-applied after, because Unity can repack vertex streams and corrupt lightmap UVs (UV1) on wood/branches.
/// Trunk/branches (submesh 0): irradiance is sampled at triangle UV centroids, vertex colors are averages of incident triangle values, then a short Laplacian smooth along wood edges to reduce lightmap-chart seam contrast.
/// Renderer lightmapIndex / scaleOffset stay unchanged unless <paramref name="clearLightmapReferences"/> is true.
/// Does not regenerate lightmap UVs automatically (to keep sampled UVs stable).
/// </summary>
public static class LightmapToVertexColorConverter
{
    public const string VertexLitCompareNameSuffix = "_VertexLitCompare";

    private const float RgbmMaxLinear = 34.493242f;
    private const float RgbmMaxGamma = 5f;
    private const float RgbmExponentLinear = 2.2f;
    /// <summary>Soft cap so vertex colors stay in a safe range for half-precision in shaders (avoids INF/NaN / broken output).</summary>
    private const float MaxIrradianceForVertexColor = 128f;

    private const int WoodVertexColorLaplacianIterations = 2;
    private const float WoodVertexColorLaplacianLambda = 0.32f;

    public sealed class BakeResult
    {
        public int RenderersProcessed;
        public int RenderersSkipped;
        /// <summary>True if <see cref="TreeGenerator.GenerateLightmapUVs"/> ran after bake (same as manual „Generuj Lightmap UV”).</summary>
        public bool RegeneratedLightmapUvs;
        public readonly List<string> Warnings = new List<string>();
    }

    public static BakeResult BakeUnderTransform(
        Transform root,
        bool clearLightmapReferences,
        bool applyVertexLeafMaterial,
        Material vertexLeafMaterialOverride)
    {
        var result = new BakeResult();
        if (root == null)
        {
            result.Warnings.Add("Root transform is null.");
            return result;
        }

        var mrs = root.GetComponentsInChildren<MeshRenderer>(true);
        var readableCache = new Dictionary<Texture2D, Texture2D>();

        Undo.SetCurrentGroupName("Lightmap to vertex colors");
        int undoGroup = Undo.GetCurrentGroup();

        // Unity 6 may mark LightmapEncodingQuality internal; read via reflection (FULL_HDR vs RGBM decode).
        bool lightmapEncodingHigh = TryGetPlayerSettingsLightmapEncodingIsHigh();

        foreach (MeshRenderer mr in mrs)
        {
            if (mr == null) continue;

            if (mr.name.EndsWith(VertexLitCompareNameSuffix, System.StringComparison.Ordinal))
                continue;

            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
            {
                result.RenderersSkipped++;
                continue;
            }

            if (mr.lightmapIndex < 0 || mr.lightmapIndex >= LightmapSettings.lightmaps.Length)
            {
                result.RenderersSkipped++;
                result.Warnings.Add($"Pominięto {mr.name}: brak przypisanej lightmapy (lightmapIndex).");
                continue;
            }

            LightmapData lmData = LightmapSettings.lightmaps[mr.lightmapIndex];
            Texture2D lmTex = lmData.lightmapColor;
            if (lmTex == null)
            {
                result.RenderersSkipped++;
                result.Warnings.Add($"Pominięto {mr.name}: brak tekstury lightmapy.");
                continue;
            }

            MeshRenderer targetMr = mr;
            if (!TryEnsureReadableMesh(mf, mf.sharedMesh, out Mesh meshToWrite, out string meshError))
            {
                result.RenderersSkipped++;
                result.Warnings.Add($"{mr.name}: {meshError}");
                continue;
            }

            MeshFilter meshFilterForUndo = mf;

            if (!TryGetLightmapUvs(meshToWrite, out List<Vector2> uv2, out string uvError))
            {
                result.RenderersSkipped++;
                result.Warnings.Add($"{mr.name}: {uvError}");
                continue;
            }

            if (!readableCache.TryGetValue(lmTex, out Texture2D readableLm))
            {
                readableLm = CreateReadableTextureCopy(lmTex);
                if (readableLm == null)
                {
                    result.RenderersSkipped++;
                    result.Warnings.Add($"Nie można odczytać tekstury lightmapy (readable): {lmTex.name}");
                    continue;
                }
                readableCache[lmTex] = readableLm;
            }

            int vc = meshToWrite.vertexCount;
            if (uv2.Count != vc)
            {
                result.RenderersSkipped++;
                result.Warnings.Add($"{mr.name}: liczba UV2 ({uv2.Count}) != vertexCount ({vc}).");
                continue;
            }

            var colorList = new List<Color>(vc);
            for (int c = 0; c < vc; c++)
                colorList.Add(Color.white);

            bool linear = PlayerSettings.colorSpace == ColorSpace.Linear;
            Vector4 st = mr.lightmapScaleOffset;

            Undo.RecordObject(meshToWrite, "Vertex colors from lightmap");
            Undo.RecordObject(meshFilterForUndo, "Vertex colors from lightmap");
            bool willModifyRenderer =
                clearLightmapReferences ||
                (applyVertexLeafMaterial && vertexLeafMaterialOverride != null);
            if (willModifyRenderer)
                Undo.RecordObject(targetMr, "Vertex bake: renderer");

            for (int i = 0; i < vc; i++)
            {
                Vector2 uv = new Vector2(
                    uv2[i].x * st.x + st.z,
                    uv2[i].y * st.y + st.w);

                Color encoded = SampleBilinear(readableLm, uv.x, uv.y);
                Vector3 irradiance = DecodeLightmapSample(encoded, linear, lightmapEncodingHigh);
                irradiance.x = Mathf.Max(0f, Mathf.Min(irradiance.x, MaxIrradianceForVertexColor));
                irradiance.y = Mathf.Max(0f, Mathf.Min(irradiance.y, MaxIrradianceForVertexColor));
                irradiance.z = Mathf.Max(0f, Mathf.Min(irradiance.z, MaxIrradianceForVertexColor));

                colorList[i] = new Color(
                    irradiance.x,
                    irradiance.y,
                    irradiance.z,
                    1f);
            }

            var uvSnapshot = CaptureUvChannels(meshToWrite, vc);
            meshToWrite.SetColors(colorList);
            meshToWrite.RecalculateBounds();
            ReapplyUvChannels(meshToWrite, uvSnapshot);
            EditorUtility.SetDirty(meshToWrite);
            EditorUtility.SetDirty(meshFilterForUndo);

            if (meshFilterForUndo.TryGetComponent<TreeGenerator>(out TreeGenerator treeGen))
                treeGen.SyncMeshAndColorsFromMeshFilter();

            if (clearLightmapReferences)
            {
                targetMr.lightmapIndex = -1;
                targetMr.realtimeLightmapIndex = -1;
                EditorUtility.SetDirty(targetMr);
            }

            if (applyVertexLeafMaterial && vertexLeafMaterialOverride != null)
            {
                Material[] mats = targetMr.sharedMaterials;
                if (mats != null && mats.Length > 0)
                {
                    if (mats.Length == 1)
                        mats[0] = vertexLeafMaterialOverride;
                    else
                    {
                        for (int s = 1; s < mats.Length && s < 3; s++)
                        {
                            if (mats[s] != null)
                                mats[s] = vertexLeafMaterialOverride;
                        }
                    }
                    targetMr.sharedMaterials = mats;
                    EditorUtility.SetDirty(targetMr);
                }
            }

            result.RenderersProcessed++;
        }

        foreach (Texture2D t in readableCache.Values)
        {
            if (t != null)
                Object.DestroyImmediate(t);
        }

        result.RegeneratedLightmapUvs = false;

        Undo.CollapseUndoOperations(undoGroup);
        SceneView.RepaintAll();
        return result;
    }

    /// <summary>
    /// Unity may repack vertex attributes when <see cref="Mesh.SetColors"/> runs; re-applying UVs keeps lightmap UVs (typically channel 1) intact for trunk/branches.
    /// </summary>
    public static List<(int channel, List<Vector2> uvs)> CaptureUvChannels(Mesh mesh, int vertexCount)
    {
        var list = new List<(int, List<Vector2>)>();
        for (int ch = 0; ch < 8; ch++)
        {
            var uv = new List<Vector2>();
            mesh.GetUVs(ch, uv);
            if (uv.Count == vertexCount)
                list.Add((ch, uv));
        }

        return list;
    }

    public static void ReapplyUvChannels(Mesh mesh, List<(int channel, List<Vector2> uvs)> snapshot)
    {
        if (mesh == null || snapshot == null)
            return;
        foreach (var (channel, uvs) in snapshot)
            mesh.SetUVs(channel, uvs);
    }

    private static void CollectSubmeshVertexIndices(Mesh mesh, int submesh, HashSet<int> outIndices)
    {
        outIndices.Clear();
        if (mesh == null || submesh < 0 || submesh >= mesh.subMeshCount)
            return;
        var indices = new List<int>();
        mesh.GetIndices(indices, submesh, true);
        for (int i = 0; i < indices.Count; i++)
            outIndices.Add(indices[i]);
    }

    /// <summary>
    /// One lightmap sample per wood triangle (UV centroid), then each wood vertex gets the mean of its incident triangle colors — reduces seams from distant UV islands.
    /// </summary>
    private static void ApplyWoodVertexColorsFromIncidentTriangleCentroids(
        List<Color> colorList,
        Mesh mesh,
        List<Vector2> uv2,
        HashSet<int> woodVerts,
        Vector4 st,
        Texture2D readableLm,
        bool linear,
        bool lightmapEncodingHigh)
    {
        var tris = new List<int>();
        mesh.GetIndices(tris, 0, true);
        int triCount = tris.Count / 3;
        if (triCount < 1)
            return;

        var triColors = new Color[triCount];
        var vertToTris = new Dictionary<int, List<int>>();

        for (int t = 0; t < triCount; t++)
        {
            int ia = tris[t * 3];
            int ib = tris[t * 3 + 1];
            int ic = tris[t * 3 + 2];

            void AddVert(int v)
            {
                if (!vertToTris.TryGetValue(v, out var list))
                {
                    list = new List<int>();
                    vertToTris[v] = list;
                }

                list.Add(t);
            }

            AddVert(ia);
            AddVert(ib);
            AddVert(ic);

            Vector2 uvCent = (uv2[ia] + uv2[ib] + uv2[ic]) / 3f;
            Vector2 uvL = new Vector2(
                uvCent.x * st.x + st.z,
                uvCent.y * st.y + st.w);

            Color encoded = SampleBilinear(readableLm, uvL.x, uvL.y);
            Vector3 irr = DecodeLightmapSample(encoded, linear, lightmapEncodingHigh);
            irr.x = Mathf.Max(0f, Mathf.Min(irr.x, MaxIrradianceForVertexColor));
            irr.y = Mathf.Max(0f, Mathf.Min(irr.y, MaxIrradianceForVertexColor));
            irr.z = Mathf.Max(0f, Mathf.Min(irr.z, MaxIrradianceForVertexColor));

            Color baseAvg = (colorList[ia] + colorList[ib] + colorList[ic]) / 3f;
            triColors[t] = new Color(
                baseAvg.r * irr.x,
                baseAvg.g * irr.y,
                baseAvg.b * irr.z,
                baseAvg.a);
        }

        foreach (int v in woodVerts)
        {
            if (!vertToTris.TryGetValue(v, out var triIndices) || triIndices.Count == 0)
                continue;

            Color sum = new Color(0f, 0f, 0f, 0f);
            for (int k = 0; k < triIndices.Count; k++)
                sum += triColors[triIndices[k]];

            float inv = 1f / triIndices.Count;
            colorList[v] = new Color(sum.r * inv, sum.g * inv, sum.b * inv, sum.a * inv);
        }
    }

    private static void SmoothWoodVertexColorsLaplacian(
        List<Color> colors,
        Mesh mesh,
        HashSet<int> woodVerts,
        int iterations,
        float lambda)
    {
        if (woodVerts.Count < 2 || iterations < 1 || lambda < 1e-6f)
            return;

        Dictionary<int, HashSet<int>> adj = BuildAdjacencyForSubmesh(mesh, 0);

        for (int iter = 0; iter < iterations; iter++)
        {
            var next = new List<Color>(colors.Count);
            for (int i = 0; i < colors.Count; i++)
                next.Add(colors[i]);

            foreach (int i in woodVerts)
            {
                if (!adj.TryGetValue(i, out HashSet<int> ns) || ns.Count == 0)
                    continue;

                Color sum = new Color(0f, 0f, 0f, 0f);
                int n = 0;
                foreach (int j in ns)
                {
                    if (!woodVerts.Contains(j))
                        continue;
                    sum += colors[j];
                    n++;
                }

                if (n == 0)
                    continue;

                Color avg = new Color(sum.r / n, sum.g / n, sum.b / n, sum.a / n);
                next[i] = Color.Lerp(colors[i], avg, lambda);
            }

            for (int i = 0; i < colors.Count; i++)
                colors[i] = next[i];
        }
    }

    private static Dictionary<int, HashSet<int>> BuildAdjacencyForSubmesh(Mesh mesh, int submesh)
    {
        var adj = new Dictionary<int, HashSet<int>>();
        if (mesh == null || submesh < 0 || submesh >= mesh.subMeshCount)
            return adj;

        var indices = new List<int>();
        mesh.GetIndices(indices, submesh, true);
        for (int t = 0; t < indices.Count; t += 3)
        {
            int a = indices[t];
            int b = indices[t + 1];
            int c = indices[t + 2];
            AddUndirectedEdge(adj, a, b);
            AddUndirectedEdge(adj, b, c);
            AddUndirectedEdge(adj, c, a);
        }

        return adj;
    }

    private static void AddUndirectedEdge(Dictionary<int, HashSet<int>> adj, int u, int v)
    {
        if (!adj.TryGetValue(u, out HashSet<int> su))
        {
            su = new HashSet<int>();
            adj[u] = su;
        }

        su.Add(v);

        if (!adj.TryGetValue(v, out HashSet<int> sv))
        {
            sv = new HashSet<int>();
            adj[v] = sv;
        }

        sv.Add(u);
    }

    private static bool TryGetLightmapUvs(Mesh mesh, out List<Vector2> uv2, out string error)
    {
        uv2 = new List<Vector2>();
        mesh.GetUVs(1, uv2);
        if (uv2.Count == 0 && mesh.uv2 != null && mesh.uv2.Length == mesh.vertexCount)
        {
            uv2.AddRange(mesh.uv2);
        }
        if (uv2.Count != mesh.vertexCount)
        {
            error = "Brak UV1 (lightmap). Wygeneruj UV2 (Generate Lightmap UVs).";
            return false;
        }
        error = null;
        return true;
    }

    private static bool TryCloneMeshWritable(Mesh source, out Mesh dst, out string error)
    {
        dst = null;
        error = null;
        if (source == null)
        {
            error = "Mesh jest null.";
            return false;
        }
        if (source.isReadable)
        {
            dst = Object.Instantiate(source);
            return true;
        }
        if (TryRebuildMeshCpuReadableForEditor(source, out dst) && dst != null)
            return true;
        Mesh inst = Object.Instantiate(source);
        if (inst != null && inst.isReadable)
        {
            dst = inst;
            return true;
        }
        if (inst != null)
            Object.DestroyImmediate(inst);
        string path = AssetDatabase.GetAssetPath(source);
        if (!string.IsNullOrEmpty(path))
            error = "Nie można odczytać kopii mesha z Assets. W imporcie modelu włącz „Read/Write Enabled”.";
        else
            error = "Nie można odczytać proceduralnego mesha do kolorów wierzchołków (MeshUtility / Instantiate).";
        return false;
    }

    private static bool TryEnsureReadableMesh(MeshFilter mf, Mesh mesh, out Mesh meshToWrite, out string error)
    {
        meshToWrite = mesh;
        error = null;
        if (mesh == null)
        {
            error = "Mesh jest null.";
            return false;
        }
        if (mesh.isReadable)
            return true;
        // Safety mode: do not replace sharedMesh during conversion, because for some assets
        // this can alter/normalize UV streams when reconstructing CPU-readable copies.
        // Skip non-readable meshes and require Read/Write Enabled in importer.
        string path = AssetDatabase.GetAssetPath(mesh);
        if (!string.IsNullOrEmpty(path))
            error = "Mesh nie jest Read/Write i został pominięty, aby nie naruszyć UV lightmapy. Włącz „Read/Write Enabled” w imporcie modelu.";
        else
            error = "Mesh nie jest Read/Write i został pominięty, aby nie naruszyć UV lightmapy.";
        return false;
    }

    /// <summary>
    /// Ensures the mesh is CPU-writable for vertex color edits (clones and assigns to <paramref name="mf"/> if needed).
    /// </summary>
    public static bool TryEnsureMeshWritableForVertexColors(MeshFilter mf, out Mesh meshToWrite, out string error)
    {
        meshToWrite = null;
        if (mf == null)
        {
            error = "MeshFilter jest null.";
            return false;
        }
        return TryEnsureReadableMesh(mf, mf.sharedMesh, out meshToWrite, out error);
    }

    /// <summary>
    /// Builds a new Mesh with CPU-writable arrays from Editor snapshot (works when <see cref="Mesh.isReadable"/> is false).
    /// </summary>
    private static bool TryRebuildMeshCpuReadableForEditor(Mesh source, out Mesh dst)
    {
        dst = null;
        if (source == null) return false;

        try
        {
            using (Mesh.MeshDataArray dataArray = MeshUtility.AcquireReadOnlyMeshData(source))
            {
                Mesh.MeshData md = dataArray[0];
                int vc = md.vertexCount;
                if (vc < 1 || md.subMeshCount < 1)
                    return false;

                var mesh = new Mesh();
                mesh.indexFormat = source.indexFormat;

                using (var verts = new NativeArray<Vector3>(vc, Allocator.Temp))
                {
                    md.GetVertices(verts);
                    mesh.SetVertices(verts);
                }

                if (md.HasVertexAttribute(VertexAttribute.Normal))
                {
                    using (var normals = new NativeArray<Vector3>(vc, Allocator.Temp))
                    {
                        md.GetNormals(normals);
                        mesh.SetNormals(normals);
                    }
                }

                if (md.HasVertexAttribute(VertexAttribute.Tangent))
                {
                    using (var tangents = new NativeArray<Vector4>(vc, Allocator.Temp))
                    {
                        md.GetTangents(tangents);
                        mesh.SetTangents(tangents);
                    }
                }

                for (int ch = 0; ch < 8; ch++)
                {
                    var texAttr = (VertexAttribute)((int)VertexAttribute.TexCoord0 + ch);
                    if (!md.HasVertexAttribute(texAttr))
                        continue;
                    using (var uvs = new NativeArray<Vector2>(vc, Allocator.Temp))
                    {
                        md.GetUVs(ch, uvs);
                        mesh.SetUVs(ch, uvs);
                    }
                }

                if (md.HasVertexAttribute(VertexAttribute.Color))
                {
                    using (var colors = new NativeArray<Color>(vc, Allocator.Temp))
                    {
                        md.GetColors(colors);
                        mesh.SetColors(colors);
                    }
                }

                mesh.subMeshCount = md.subMeshCount;
                if (md.indexFormat == IndexFormat.UInt16)
                {
                    for (int s = 0; s < md.subMeshCount; s++)
                    {
                        SubMeshDescriptor sm = md.GetSubMesh(s);
                        int icount = sm.indexCount;
                        if (icount < 1)
                        {
                            mesh.SetTriangles(System.Array.Empty<int>(), s);
                            continue;
                        }

                        var tris = new int[icount];
                        using (var indices = new NativeArray<ushort>(icount, Allocator.Temp))
                        {
                            md.GetIndices(indices, s, true);
                            for (int i = 0; i < icount; i++)
                                tris[i] = indices[i];
                        }

                        mesh.SetTriangles(tris, s);
                    }
                }
                else
                {
                    // GetIndices() only accepts NativeArray<ushort>; UInt32 index buffers use GetIndexData<uint>().
                    NativeArray<uint> fullIdx = md.GetIndexData<uint>();
                    for (int s = 0; s < md.subMeshCount; s++)
                    {
                        SubMeshDescriptor sm = md.GetSubMesh(s);
                        int icount = sm.indexCount;
                        if (icount < 1)
                        {
                            mesh.SetTriangles(System.Array.Empty<int>(), s);
                            continue;
                        }

                        int start = sm.indexStart;
                        var tris = new int[icount];
                        for (int i = 0; i < icount; i++)
                            tris[i] = (int)fullIdx[start + i] + sm.baseVertex;
                        mesh.SetTriangles(tris, s);
                    }
                }

                mesh.RecalculateBounds();
                dst = mesh;
                return true;
            }
        }
        catch
        {
            if (dst != null)
            {
                Object.DestroyImmediate(dst);
                dst = null;
            }
            return false;
        }
    }

    private static Texture2D CreateReadableTextureCopy(Texture2D src)
    {
        if (src == null) return null;

        // Lightmaps are RGBM / HDR; blitting to ARGB32 destroys the rgb/a relationship and breaks decode (wrong hues, often red bias).
        bool hdr = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf);
        RenderTextureFormat rtFormat = hdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
        TextureFormat texFormat = hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32;

        RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, rtFormat, RenderTextureReadWrite.Linear);
        Graphics.Blit(src, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D copy = new Texture2D(src.width, src.height, texFormat, false, true);
        copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        copy.Apply(false, false);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return copy;
    }

    private static Color SampleBilinear(Texture2D tex, float u, float v)
    {
        u = Mathf.Clamp01(u);
        v = Mathf.Clamp01(v);
        int w = tex.width;
        int h = tex.height;
        if (w < 1 || h < 1) return Color.black;

        float x = u * (w - 1);
        float y = v * (h - 1);
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = Mathf.Min(x0 + 1, w - 1);
        int y1 = Mathf.Min(y0 + 1, h - 1);
        float fx = x - x0;
        float fy = y - y0;

        Color c00 = tex.GetPixel(x0, y0);
        Color c10 = tex.GetPixel(x1, y0);
        Color c01 = tex.GetPixel(x0, y1);
        Color c11 = tex.GetPixel(x1, y1);
        Color cx0 = Color.Lerp(c00, c10, fx);
        Color cx1 = Color.Lerp(c01, c11, fx);
        return Color.Lerp(cx0, cx1, fy);
    }

    /// <summary>
    /// Matches URP <c>EntityLighting.hlsl</c> <c>DecodeLightmap</c>: FULL_HDR vs RGBM vs DLDR depend on project lightmap encoding.
    /// Double-applying RGBM when Unity baked FULL_HDR (common with High encoding in Unity 6) yields wrong uniform hues.
    /// </summary>
    private static bool TryGetPlayerSettingsLightmapEncodingIsHigh()
    {
        try
        {
            PropertyInfo pi = typeof(PlayerSettings).GetProperty(
                "lightmapEncodingQuality",
                BindingFlags.Public | BindingFlags.Static);
            if (pi == null) return false;
            object v = pi.GetValue(null);
            if (v == null) return false;
            return string.Equals(v.ToString(), "High", System.StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static Vector3 DecodeLightmapSample(Color encoded, bool linearColorSpace, bool projectLightmapEncodingHigh)
    {
        float r = encoded.r, g = encoded.g, b = encoded.b, a = Mathf.Max(encoded.a, 1e-5f);
        float mx = Mathf.Max(r, Mathf.Max(g, b));

        // UNITY_LIGHTMAP_FULL_HDR: linear irradiance already in RGB (alpha not used for scale).
        // High encoding (typical Unity 6) + half-float readback often has components > 1 without RGBM packing.
        if (projectLightmapEncodingHigh || mx > 1f)
            return new Vector3(r, g, b);

        // UNITY_LIGHTMAP_RGBM_ENCODING (Normal / Low typical): UnpackLightmapRGBM — matches EntityLighting.hlsl
        if (linearColorSpace)
            return new Vector3(r, g, b) * (Mathf.Pow(a, RgbmExponentLinear) * RgbmMaxLinear);
        return new Vector3(r, g, b) * (a * RgbmMaxGamma);
    }
}
#endif
