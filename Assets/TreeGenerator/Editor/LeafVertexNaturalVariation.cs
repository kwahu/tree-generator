#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Multiplies existing leaf vertex colors by subtle, tree-like factors: height in crown, distance from trunk axis (XZ), sun exposure (world normal · sun).
/// </summary>
public static class LeafVertexNaturalVariation
{
    public sealed class VariationResult
    {
        public int MeshesProcessed;
        public readonly List<string> Messages = new List<string>();
    }

    /// <param name="globalBlend">0 = zostaw oryginalne kolory, 1 = pełna wariacja po mnożnikach (mieszanie liniowe RGB).</param>
    /// <param name="heightWeight">Ile włączyć mnożnik „wysokość w koronie” (0 = ten aspekt wyłączony).</param>
    /// <param name="radialWeight">Ile włączyć mnożnik „odległość od osi pnia” (XZ).</param>
    /// <param name="sunWeight">Ile włączyć mnożnik „ekspozycja / słońce”.</param>
    public static VariationResult Apply(
        Transform root,
        float globalBlend,
        float heightWeight,
        float radialWeight,
        float sunWeight)
    {
        var result = new VariationResult();
        if (root == null)
        {
            result.Messages.Add("Root jest null.");
            return result;
        }

        globalBlend = Mathf.Clamp(globalBlend, 0f, 10f);
        heightWeight = Mathf.Clamp(heightWeight, 0f, 10f);
        radialWeight = Mathf.Clamp(radialWeight, 0f, 10f);
        sunWeight = Mathf.Clamp(sunWeight, 0f, 10f);

        if (globalBlend < 1e-5f)
        {
            result.Messages.Add("Mieszanie (oryginał → wariacja) = 0 — nic nie zmieniono.");
            return result;
        }

        Vector3 sunDirWorld = GetSunDirectionWorld();

        Undo.SetCurrentGroupName("Natural leaf vertex color variation");
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
            LeafVertexColorRandomTest.CollectLeafVertexIndices(mesh, leafVerts);
            if (leafVerts.Count == 0)
            {
                result.Messages.Add($"{mf.name}: brak trójkątów liści (submeshe).");
                continue;
            }

            int vc = mesh.vertexCount;
            Vector3[] verts = mesh.vertices;
            Vector3[] normals = mesh.normals;
            if (verts == null || verts.Length != vc)
            {
                result.Messages.Add($"{mf.name}: niepoprawne wierzchołki.");
                continue;
            }

            if (normals == null || normals.Length != vc)
            {
                mesh.RecalculateNormals();
                normals = mesh.normals;
            }

            ComputeLeafBounds(leafVerts, verts, out float minY, out float maxY, out float maxRad);
            float radDenom = Mathf.Max(maxRad, 1e-4f);

            var list = new List<Color>(vc);
            mesh.GetColors(list);
            if (list.Count != vc)
            {
                list.Clear();
                for (int i = 0; i < vc; i++)
                    list.Add(Color.white);
            }

            Transform t = mf.transform;
            Undo.RecordObject(mesh, "Natural leaf vertex variation");
            Undo.RecordObject(mf, "Natural leaf vertex variation");

            foreach (int idx in leafVerts)
            {
                Vector3 p = verts[idx];
                float height01 = Mathf.InverseLerp(minY, maxY, p.y);
                float xz = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                float radial01 = Mathf.Clamp01(xz / radDenom);

                Vector3 wn = t.TransformDirection(normals[idx]);
                if (wn.sqrMagnitude < 1e-8f)
                    wn = Vector3.up;
                else
                    wn.Normalize();

                float sunDot = Mathf.Clamp01(Vector3.Dot(wn, sunDirWorld));
                // Slight mix with “sky” (up) so fully shaded leaves still get a gradient.
                float skyDot = Mathf.Clamp01(Vector3.Dot(wn, Vector3.up) * 0.5f + 0.5f);
                float exposure = Mathf.Lerp(skyDot, sunDot, 0.85f);

                Vector3 mulH = HeightAspectMultipliers(height01);
                Vector3 mulR = RadialAspectMultipliers(radial01);
                Vector3 mulS = SunAspectMultipliers(exposure);

                Vector3 h = ApplyAspectStrength(mulH, heightWeight);
                Vector3 r = ApplyAspectStrength(mulR, radialWeight);
                Vector3 s = ApplyAspectStrength(mulS, sunWeight);
                Vector3 combined = new Vector3(h.x * r.x * s.x, h.y * r.y * s.y, h.z * r.z * s.z);

                Color c = list[idx];
                Vector3 origRgb = new Vector3(c.r, c.g, c.b);
                Vector3 variedRgb = new Vector3(
                    Mathf.Clamp(c.r * combined.x, 0f, 10f),
                    Mathf.Clamp(c.g * combined.y, 0f, 10f),
                    Mathf.Clamp(c.b * combined.z, 0f, 10f));
                Vector3 finalRgb = Vector3.LerpUnclamped(origRgb, variedRgb, globalBlend);
                finalRgb.x = Mathf.Clamp(finalRgb.x, 0f, 10f);
                finalRgb.y = Mathf.Clamp(finalRgb.y, 0f, 10f);
                finalRgb.z = Mathf.Clamp(finalRgb.z, 0f, 10f);

                list[idx] = new Color(finalRgb.x, finalRgb.y, finalRgb.z, c.a);
            }

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

        LeafVertexColorRandomTest.EnsureLeafVertexBakedLitSeesVertexColors(root);

        Undo.CollapseUndoOperations(undoGroup);
        SceneView.RepaintAll();
        return result;
    }

    private static Vector3 GetSunDirectionWorld()
    {
        Light sun = RenderSettings.sun;
        if (sun != null && sun.type == LightType.Directional && sun.enabled)
            return (-sun.transform.forward).normalized;
        return Vector3.Normalize(new Vector3(0.35f, 0.85f, 0.2f));
    }

    private static void ComputeLeafBounds(HashSet<int> leafIdx, Vector3[] verts, out float minY, out float maxY, out float maxRad)
    {
        minY = float.MaxValue;
        maxY = float.MinValue;
        maxRad = 0f;
        foreach (int i in leafIdx)
        {
            Vector3 p = verts[i];
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
            float r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
            if (r > maxRad)
                maxRad = r;
        }
    }

    /// <summary> Height in crown: slightly warmer / brighter toward top. </summary>
    private static Vector3 HeightAspectMultipliers(float height01)
    {
        float hR = Mathf.Lerp(0.94f, 1.06f, height01);
        float hG = Mathf.Lerp(0.93f, 1.10f, height01);
        float hB = Mathf.Lerp(0.98f, 0.90f, height01);
        return new Vector3(hR, hG, hB);
    }

    /// <summary> Distance from trunk axis (XZ): outer canopy lighter, inner darker. </summary>
    private static Vector3 RadialAspectMultipliers(float radial01)
    {
        float rMul = Mathf.Lerp(0.92f, 1.08f, radial01);
        return new Vector3(rMul, rMul, rMul);
    }

    /// <summary> Sun / exposure: sun-facing brighter (HDR-safe range). </summary>
    private static Vector3 SunAspectMultipliers(float exposure01)
    {
        float sR = Mathf.Lerp(0.88f, 1.12f, exposure01);
        float sG = Mathf.Lerp(0.90f, 1.10f, exposure01);
        float sB = Mathf.Lerp(0.92f, 1.08f, exposure01);
        return new Vector3(sR, sG, sB);
    }

    /// <summary>
    /// 0 = brak wpływu (x1), 1 = bazowy wpływ, >1 = wielokrotne wzmocnienie odchylenia od 1.
    /// </summary>
    private static Vector3 ApplyAspectStrength(Vector3 aspectMultiplier, float strength)
    {
        return new Vector3(
            1f + (aspectMultiplier.x - 1f) * strength,
            1f + (aspectMultiplier.y - 1f) * strength,
            1f + (aspectMultiplier.z - 1f) * strength);
    }
}
#endif
