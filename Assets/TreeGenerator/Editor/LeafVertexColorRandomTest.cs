#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only: writes random RGB into vertex colors for vertices referenced by leaf submeshes (debug / pipeline check).
/// </summary>
public static class LeafVertexColorRandomTest
{
    public sealed class TestResult
    {
        public int MeshesProcessed;
        public readonly List<string> Messages = new List<string>();
    }

    public static TestResult ApplyRandomLeafVertexColors(Transform root)
    {
        var result = new TestResult();
        if (root == null)
        {
            result.Messages.Add("Root jest null.");
            return result;
        }

        Undo.SetCurrentGroupName("Test random leaf vertex colors");
        int undoGroup = Undo.GetCurrentGroup();

        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter mf in filters)
        {
            if (mf == null || mf.sharedMesh == null)
                continue;
            if (mf.name.EndsWith(LightmapToVertexColorConverter.VertexLitCompareNameSuffix, System.StringComparison.Ordinal))
                continue;

            if (!LightmapToVertexColorConverter.TryEnsureMeshWritableForVertexColors(mf, out Mesh mesh, out string err))
            {
                result.Messages.Add($"{mf.name}: {err}");
                continue;
            }

            var leafVerts = new HashSet<int>();
            CollectLeafVertexIndices(mesh, leafVerts);
            if (leafVerts.Count == 0)
            {
                result.Messages.Add($"{mf.name}: brak trójkątów liści (submeshe).");
                continue;
            }

            int vc = mesh.vertexCount;
            var list = new List<Color>(vc);
            mesh.GetColors(list);
            if (list.Count != vc)
            {
                list.Clear();
                for (int i = 0; i < vc; i++)
                    list.Add(Color.white);
            }

            Undo.RecordObject(mesh, "Random leaf vertex colors test");
            Undo.RecordObject(mf, "Random leaf vertex colors test");
            foreach (int idx in leafVerts)
                list[idx] = new Color(Random.value, Random.value, Random.value, 1f);

            // SetColors can repack vertex streams and corrupt UV1 (lightmap); re-apply captured UVs after.
            var uvSnapshot = LightmapToVertexColorConverter.CaptureUvChannels(mesh, vc);
            mesh.SetColors(list);
            mesh.RecalculateBounds();
            LightmapToVertexColorConverter.ReapplyUvChannels(mesh, uvSnapshot);
            EditorUtility.SetDirty(mesh);
            EditorUtility.SetDirty(mf);

            if (mf.TryGetComponent<TreeGenerator>(out TreeGenerator gen))
                gen.SyncMeshAndColorsFromMeshFilter();

            result.MeshesProcessed++;
        }

        // Leaf Vertex Baked Lit uses vertex color as GI; when _GIInfluence is 0 the shader ignores it entirely.
        EnsureLeafVertexBakedLitSeesVertexColors(root, result);

        Undo.CollapseUndoOperations(undoGroup);
        SceneView.RepaintAll();
        return result;
    }

    /// <summary> Same MPB pass as random test — call after other leaf vertex color tools. </summary>
    public static void EnsureLeafVertexBakedLitSeesVertexColors(Transform root)
    {
        EnsureLeafVertexBakedLitSeesVertexColors(root, new TestResult());
    }

    /// <summary>
    /// Forces _GIInfluence = 1 on leaf material slots via MPB so vertex colors are not lerped away to 1.0.
    /// </summary>
    private static void EnsureLeafVertexBakedLitSeesVertexColors(Transform root, TestResult result)
    {
        MeshRenderer[] mrs = root.GetComponentsInChildren<MeshRenderer>(true);
        int slotsTouched = 0;
        foreach (MeshRenderer mr in mrs)
        {
            if (mr.name.EndsWith(LightmapToVertexColorConverter.VertexLitCompareNameSuffix, System.StringComparison.Ordinal))
                continue;
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
                continue;

            int subMeshCount = mf.sharedMesh.subMeshCount;
            int matCount = mr.sharedMaterials.Length;
            int[] leafSlots = subMeshCount >= 3 ? new[] { 1, 2 } : subMeshCount == 2 ? new[] { 1 } : new[] { 0 };

            foreach (int slot in leafSlots)
            {
                if (slot < 0 || slot >= matCount)
                    continue;
                Material m = mr.sharedMaterials[slot];
                if (m == null || !m.HasProperty("_GIInfluence"))
                    continue;
                if (!m.shader.name.Contains("Leaf Vertex Baked Lit"))
                    continue;

                MaterialPropertyBlock block = new MaterialPropertyBlock();
                mr.GetPropertyBlock(block, slot);
                block.SetFloat("_GIInfluence", 1f);
                mr.SetPropertyBlock(block, slot);
                slotsTouched++;
            }
        }

        if (slotsTouched > 0)
            result.Messages.Insert(0, $"Vertex Baked Lit: ustawiono _GIInfluence=1 na {slotsTouched} slotach (MaterialPropertyBlock).");
    }

    public static void CollectLeafVertexIndices(Mesh mesh, HashSet<int> outIndices)
    {
        int sm = mesh.subMeshCount;
        if (sm >= 3)
        {
            AddTriangles(mesh, 1, outIndices);
            AddTriangles(mesh, 2, outIndices);
        }
        else if (sm == 2)
        {
            AddTriangles(mesh, 1, outIndices);
        }
        else if (sm == 1)
        {
            AddTriangles(mesh, 0, outIndices);
        }
    }

    private static void AddTriangles(Mesh mesh, int submesh, HashSet<int> outIndices)
    {
        // GetTriangles() is unreliable with UInt32 index buffers; GetIndices works for both formats.
        var indices = new List<int>();
        mesh.GetIndices(indices, submesh, true);
        for (int i = 0; i < indices.Count; i++)
            outIndices.Add(indices[i]);
    }
}
#endif
