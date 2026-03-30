using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Procedural tree mesh generator.
/// Attach to a GameObject with MeshFilter + MeshRenderer, assign a TreeData asset, then click
/// Generate Tree (or call Generate() from code / the context-menu).
/// Sub-mesh 0 = wood (trunk + branches), sub-mesh 1 = leaves.
/// Assign two materials on the MeshRenderer accordingly.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[ExecuteInEditMode]
[ExecuteAlways]
public class TreeGenerator : MonoBehaviour
{
    public TreeData data;
    public bool autoUpdateWhenValid = false;
    public Material treeMaterial;
    public Material leafMaterial;
    public Material clusterLeafMaterial;
    public bool forceLeafNormalsToSun = true;
    [Range(0f, 0.8f)] public float leafBakeMinSunDot = 0.2f;
    [Min(0.01f)] public float woodUvWorldSize = 0.25f;
    [Header("Wiatr liści (URP: TreeGenerator/Leaf Baked Lit, Leaf Billboard Opaque)")]
    [Tooltip("Wymaga materiału z powyższymi shaderami. Parametry co klatkę trafiają do MaterialPropertyBlock na wszystkich MeshRendererach pod tym obiektem.")]
    public bool leafWindEnabled = false;
    [Min(0f)] public float leafWindStrength = 0.12f;
    [Min(0f)] public float leafWindFrequency = 2f;
    [Range(0f, 2f)] public float leafWindTurbulence = 0.65f;
    [Min(0.01f)] public float leafWindPhaseScale = 2.5f;
    [Range(0.25f, 4f)] public float leafWindMaskExponent = 2f;
    [Tooltip("Kierunek podmuchu w przestrzeni świata (normalizowany przy aplikowaniu).")]
    public Vector3 leafWindDirection = new Vector3(1f, 0.05f, 0.3f);
    public bool autoGenerateLods = true;
    [Range(1, 5)] public int lodLevels = 3;
    [Range(0.1f, 0.9f)] public float lodStartScreenRelativeHeight = 0.6f;
    [Range(0.01f, 0.4f)] public float lodEndScreenRelativeHeight = 0.05f;
    [Tooltip("Od tego indeksu LOD włącznie nie generuje się siatki drewna głównych gałęzi (__AutoLOD_N ma N równe temu indeksowi). LOD0 zawsze pełny. 6 = nigdy nie ukrywaj.")]
    [Range(1, 6)] public int lodHideMainBranchWoodFromLevel = 3;
    [Tooltip("Od tego indeksu LOD włącznie nie generuje się drewna podgałęzi. 6 = nigdy nie ukrywaj.")]
    [Range(1, 6)] public int lodHideSubBranchWoodFromLevel = 2;
    [Tooltip("Od tego indeksu LOD włącznie nie generuje się drewna gałązek (poziom 2). 6 = nigdy nie ukrywaj.")]
    [Range(1, 6)] public int lodHideSubBranchLevel2WoodFromLevel = 1;
    [Range(0.01f, 1f)] public float lodFinalLeafCountMultiplier = 0.08f;
    [Range(1f, 4f)] public float lodFinalLeafSizeMultiplier = 2.2f;
    [Range(0.5f, 3f)] public float lodLeafReductionExponent = 1.5f;
    [Tooltip("Od tego poziomu LOD (1 = pierwszy zdalny mesh) włączana jest redukcja liczby liści (mnożnik + ewentualnie grupowanie). Na niższych poziomach zdalnych liści jest tyle co wynika z TreeData po innych regułach LOD.")]
    [Range(1, 5)] public int lodLeafCountReductionStartLevel = 1;
    public bool useLeafVolumeLods = false;
    [Range(1, 5)] public int leafVolumeStartLodLevel = 2;
    [Range(8, 64)] public int leafVolumeGridResolution = 24;
    [Range(0.25f, 4f)] public float leafVolumeSampleRadiusInVoxels = 1.35f;
    [Range(0.05f, 0.95f)] public float leafVolumeIsoLevel = 0.35f;
    [Range(0, 4)] public int leafVolumeSmoothIterations = 1;
    [Range(0f, 0.8f)] public float leafVolumeBoundsPadding = 0.12f;
    public LeafVolumeGeometryOptimizeMode leafVolumeGeometryOptimize = LeafVolumeGeometryOptimizeMode.WeldAndRemoveDegenerate;
    [Range(0.00005f, 0.08f)] public float leafVolumeWeldEpsilon = 0.0025f;
    [Range(0f, 0.0001f)] public float leafVolumeMinTriangleAreaSq = 0f;
    [Tooltip("Morfologiczne zamykanie maski pola (density >= iso) przed marching — wypełnia małe przerwy w siatce.")]
    public bool leafVolumeCloseFieldHoles = true;
    [Range(1, 4)] public int leafVolumeHoleCloseRadius = 2;
    [Range(0, 3)] public int leafVolumeSmoothAfterHoleClose = 1;
    [Tooltip("Zamiast siatki liści/korony: nieprzezroczyste quady billboard (shader camera-facing) na próbkach z LOD0.")]
    public bool useBillboardLeafLods = false;
    [Range(1, 5)] public int billboardLeafLodStartLevel = 2;
    [Range(4, 256)] public int billboardLeafLodMaxSprites = 48;
    [Min(0.02f)] public float billboardLeafWorldWidth = 0.55f;
    [Min(0.02f)] public float billboardLeafWorldHeight = 0.75f;
    [Range(0f, 0.5f)] public float billboardLeafJitterRadius = 0.08f;
    public Material billboardLeafMaterial;
    [Range(0f, 1f)] public float lod1TopLeafSizeDamping = 0f;
    [Range(0f, 1f)] public float lod2TopLeafSizeDamping = 0.25f;
    [Range(0f, 1f)] public float lod3TopLeafSizeDamping = 0.5f;
    [Range(0f, 1f)] public float lod4TopLeafSizeDamping = 0.65f;
    [Range(0f, 10f)] public float lodTopLeafCurveInfluence = 2f;
    public AnimationCurve lodTopLeafDampingByHeight = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.6f, 0f),
        new Keyframe(0.82f, 0.45f),
        new Keyframe(1f, 1f));
    [Tooltip("Wyłączenie wyłącza zarówno grupy liści w środku korony, jak i tłumienie rozmiaru liści u góry korony na LOD.")]
    public bool enableLodInnerLeafClustering = true;
    [Range(0f, 1f)] public float lodInnerClusterRangeMin01 = 0f;
    [Range(0f, 1f)] public float lodInnerClusterRangeMax01 = 0.36f;
    public AnimationCurve lodInnerClusterByHeight = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(1f, 1f));
    [Range(0f, 1f)] public float lod1InnerClusterReplacement = 0.35f;
    [Range(0f, 1f)] public float lod2InnerClusterReplacement = 0.6f;
    [Range(0f, 1f)] public float lod3InnerClusterReplacement = 0.82f;
    [Range(0f, 1f)] public float lod4InnerClusterReplacement = 0.92f;
    [Range(2f, 100f)] public float lod1InnerClusterGroupLeaves = 2f;
    [Range(2f, 100f)] public float lod2InnerClusterGroupLeaves = 3f;
    [Range(2f, 100f)] public float lod3InnerClusterGroupLeaves = 4f;
    [Range(2f, 100f)] public float lod4InnerClusterGroupLeaves = 5f;
    [Range(1f, 4f)] public float lod1InnerClusterSizeMultiplier = 1.25f;
    [Range(1f, 4f)] public float lod2InnerClusterSizeMultiplier = 1.45f;
    [Range(1f, 4f)] public float lod3InnerClusterSizeMultiplier = 1.7f;
    [Range(1f, 4f)] public float lod4InnerClusterSizeMultiplier = 1.9f;
    public bool debugTintClusteredLeaves = false;
    public bool autoLodScaleInLightmap = true;
    public bool manualLodScaleInLightmap = true;
    [Range(0.01f, 8f)] public float lod0ScaleInLightmap = 1f;
    [Range(0.01f, 8f)] public float lod1ScaleInLightmap = 1f;
    [Range(0.01f, 8f)] public float lod2ScaleInLightmap = 0.85f;
    [Range(0.01f, 8f)] public float lod3ScaleInLightmap = 0.7f;
    [Range(0.01f, 8f)] public float lod4ScaleInLightmap = 0.6f;
    [Range(0.01f, 1f)] public float lodMinScaleInLightmap = 0.12f;
    [Range(0.1f, 2f)] public float lodLightmapScalePower = 0.7f;
    public bool lodLightmapScaleUseWoodOnly = true;
    [Range(0f, 1f)] public float lodLightmapScaleBiasToLod0 = 0.55f;
    [Tooltip("Od wskazanego LOD: główne gałęzie, podgałęzie i gałązki jako cienka wstęga w płaszczyźnie pionowej — z boku widać grubość, z góry/dolu prawie nie. Pień bez zmian.")]
    public bool lodFlatVerticalBranchWood = false;
    [Range(1, 5)] public int lodFlatVerticalBranchStartLevel = 1;
    [Tooltip("Gdy włączone, kolejne LOD zachowują pełną liczbę segmentów głównych gałęzi, podgałęzi i gałązek (nadal redukowane są m.in. boki rur, liście).")]
    public bool lodPreserveBranchSegments = false;
    [Tooltip("Gdy włączone, kolejne LOD zachowują pełną liczbę segmentów pnia (boki rury pnia nadal mogą być redukowane).")]
    public bool lodPreserveTrunkSegments = false;

    // Exposed for the custom inspector info panel
    [HideInInspector] public int lastVertexCount;
    [HideInInspector] public int lastTriangleCount;
    [HideInInspector] public int lastWoodTriangleCount;
    [HideInInspector] public int lastLeafTriangleCount;
    [HideInInspector] public int lastMainBranchCount;
    [HideInInspector] public int lastSubBranchCount;
    [HideInInspector] public int lastLeafCount;
    [HideInInspector] public int lastClusterLeafCount;
    [HideInInspector] public float lastUpwardLeafRatio;
    [HideInInspector] public bool lastUpwardLeafCheckPassed;
    [HideInInspector] public List<int> lastLodWoodTriangleCounts = new List<int>();
    [HideInInspector] public List<int> lastLodLeafTriangleCounts = new List<int>();
    [HideInInspector] public List<int> lastLodClusterLeafCounts = new List<int>();

    // ── Internal state ────────────────────────────────────────────────────────

    private Mesh _mesh;
    private System.Random _rng;
    private System.Random _subBranchRng;
    private System.Random _subBranchLevel2Rng;
    private System.Random _leafMainRng;
    private System.Random _leafSubRng;
    private System.Random _leafSubLevel2Rng;
    private System.Random _leafAlongMainRng;
    private System.Random _leafAlongSubRng;
    private System.Random _leafAlongSubLevel2Rng;

    private readonly List<Vector3> _verts    = new List<Vector3>();
    private readonly List<Vector2> _uvs      = new List<Vector2>();
    private readonly List<Color>   _colors   = new List<Color>();
    private readonly List<int>     _woodTris = new List<int>();
    private readonly List<int>     _leafTris = new List<int>();
    private readonly List<int>     _clusterLeafTris = new List<int>();
    private List<MeshRenderer> _leafWindTargets;
    private MaterialPropertyBlock _leafWindPropertyBlock;
    private bool _leafWindAppliedLastFrame;
    private static readonly int LeafWindEnabledId = Shader.PropertyToID("_LeafWindEnabled");
    private static readonly int LeafWindStrengthId = Shader.PropertyToID("_LeafWindStrength");
    private static readonly int LeafWindFrequencyId = Shader.PropertyToID("_LeafWindFrequency");
    private static readonly int LeafWindTurbulenceId = Shader.PropertyToID("_LeafWindTurbulence");
    private static readonly int LeafWindPhaseScaleId = Shader.PropertyToID("_LeafWindPhaseScale");
    private static readonly int LeafWindMaskExponentId = Shader.PropertyToID("_LeafWindMaskExponent");
    private static readonly int LeafWindDirectionId = Shader.PropertyToID("_LeafWindDirection");
    private List<TubeNode> _currentTrunkNodes = new List<TubeNode>();
    private float _leafDistanceNormalization = 1f;
    private int _activeLodLevel = 0;
    private int _generatedMainBranchCount;
    private int _generatedSubBranchCount;
    private int _generatedLeafCount;
    private int _generatedClusterLeafCount;
    private int _upwardLeafCount;
    private int _checkedLeafCount;

    // ── Tube node ─────────────────────────────────────────────────────────────

    private struct TubeNode
    {
        /// <summary>Center of the cross-section ring.</summary>
        public Vector3    position;
        /// <summary>Orientation: local Y = tube direction at this point.</summary>
        public Quaternion rotation;
        /// <summary>Ring radius.</summary>
        public float      radius;
        /// <summary>Normalized arc-length along the tube (0–1).</summary>
        public float      t;
    }

    private enum LeafTarget
    {
        MainBranch,
        SubBranch,
        SubBranchLevel2
    }

    // ── Public API ────────────────────────────────────────────────────────────

    [ContextMenu("Generate Tree")]
    public void Generate()
    {
        if (!TryValidateSettings(out string validationError))
        {
            Debug.LogWarning($"[TreeGenerator] {validationError}");
            return;
        }

        GenerateSinglePass(data, 0);
        if (autoGenerateLods)
            GenerateLods(data);
        else
            SetLodTriangleStats(new[] { lastWoodTriangleCount }, new[] { lastLeafTriangleCount }, new[] { lastClusterLeafCount });

        if (data != null && data.leaves != null && data.leaves.enabled && data.leaves.validateUpwardOrientation && !lastUpwardLeafCheckPassed)
        {
            Debug.LogWarning(
                $"[TreeGenerator] Leaf upward check failed. Upward ratio: {lastUpwardLeafRatio:P1}, required: {data.leaves.minUpwardLeafRatio:P1}.");
        }

        RefreshLeafWindTargets();
    }

    private void OnEnable()
    {
        RefreshLeafWindTargets();
    }

    private void OnDisable()
    {
        RefreshLeafWindTargets();
        ClearLeafWindPropertyBlocksOnAllTargets();
        _leafWindAppliedLastFrame = false;
    }

    private void LateUpdate()
    {
        if (!isActiveAndEnabled)
            return;

        if (!leafWindEnabled)
        {
            if (_leafWindAppliedLastFrame)
            {
                if (_leafWindTargets == null || _leafWindTargets.Count == 0)
                    RefreshLeafWindTargets();
                ClearLeafWindPropertyBlocksOnAllTargets();
            }
            _leafWindAppliedLastFrame = false;
            return;
        }

        if (_leafWindTargets == null || _leafWindTargets.Count == 0)
            RefreshLeafWindTargets();

        EnsureLeafWindPropertyBlock();
        Vector3 dir = leafWindDirection.sqrMagnitude > 1e-8f
            ? leafWindDirection.normalized
            : Vector3.right;
        _leafWindPropertyBlock.SetFloat(LeafWindEnabledId, 1f);
        _leafWindPropertyBlock.SetFloat(LeafWindStrengthId, leafWindStrength);
        _leafWindPropertyBlock.SetFloat(LeafWindFrequencyId, leafWindFrequency);
        _leafWindPropertyBlock.SetFloat(LeafWindTurbulenceId, leafWindTurbulence);
        _leafWindPropertyBlock.SetFloat(LeafWindPhaseScaleId, leafWindPhaseScale);
        _leafWindPropertyBlock.SetFloat(LeafWindMaskExponentId, leafWindMaskExponent);
        _leafWindPropertyBlock.SetVector(LeafWindDirectionId, new Vector4(dir.x, dir.y, dir.z, 0f));

        for (int i = 0; i < _leafWindTargets.Count; i++)
            ApplyLeafWindPropertyBlockToRenderer(_leafWindTargets[i]);

        _leafWindAppliedLastFrame = true;
    }

    private void RefreshLeafWindTargets()
    {
        _leafWindTargets ??= new List<MeshRenderer>(16);
        _leafWindTargets.Clear();
        _leafWindTargets.AddRange(GetComponentsInChildren<MeshRenderer>(true));
    }

    private void EnsureLeafWindPropertyBlock()
    {
        _leafWindPropertyBlock ??= new MaterialPropertyBlock();
    }

    private void ApplyLeafWindPropertyBlockToRenderer(MeshRenderer mr)
    {
        if (mr == null)
            return;
        Material[] mats = mr.sharedMaterials;
        if (mats == null || mats.Length == 0)
            return;
        for (int i = 0; i < mats.Length; i++)
            mr.SetPropertyBlock(_leafWindPropertyBlock, i);
    }

    private void ClearLeafWindPropertyBlocksOnAllTargets()
    {
        if (_leafWindTargets == null)
            return;
        for (int i = 0; i < _leafWindTargets.Count; i++)
        {
            MeshRenderer mr = _leafWindTargets[i];
            if (mr == null)
                continue;
            Material[] mats = mr.sharedMaterials;
            if (mats == null)
                continue;
            for (int m = 0; m < mats.Length; m++)
                mr.SetPropertyBlock(null, m);
        }
    }

    [ContextMenu("Generate Lightmap UVs")]
    public void GenerateLightmapUVs()
    {
#if UNITY_EDITOR
        GenerateLightmapUvForMesh(GetComponent<MeshFilter>()?.sharedMesh);
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (!child.name.StartsWith("__AutoLOD_")) continue;
            MeshFilter mf = child.GetComponent<MeshFilter>();
            GenerateLightmapUvForMesh(mf != null ? mf.sharedMesh : null);
        }
#else
        Debug.LogWarning("[TreeGenerator] GenerateLightmapUVs is available only in Unity Editor.");
#endif
    }

    /// <summary>
    /// LOD0: zawsze rysuj. Dla przejścia na mesh dziecka z indeksem N — ukryj drewno, gdy N &gt;= próg (1..5).
    /// Wartość 6 = nigdy nie ukrywaj tej kategorii na żadnym LOD.
    /// </summary>
    private bool BranchWoodMeshVisibleThisLod(int hideFromLevelInclusive)
    {
        int h = Mathf.Clamp(hideFromLevelInclusive, 1, 6);
        if (h >= 6) return true;
        return _activeLodLevel < h;
    }

    private void GenerateSinglePass(TreeData sourceData, int lodLevel)
    {
        TreeData previousData = data;
        data = sourceData;
        _activeLodLevel = Mathf.Max(0, lodLevel);
        _leafDistanceNormalization = ComputeLeafDistanceNormalization();
        bool renderMainBranchGeometry = BranchWoodMeshVisibleThisLod(lodHideMainBranchWoodFromLevel);
        bool renderSubBranchGeometry = BranchWoodMeshVisibleThisLod(lodHideSubBranchWoodFromLevel);
        bool renderSubBranchLevel2Geometry = BranchWoodMeshVisibleThisLod(lodHideSubBranchLevel2WoodFromLevel);

        _rng = new System.Random(data.seed);
        _subBranchRng = new System.Random(unchecked((data.seed * 557) ^ (int)0x85EBCA6Bu));
        _subBranchLevel2Rng = new System.Random(unchecked((data.seed * 911) ^ (int)0xC2B2AE35u));
        _leafMainRng = new System.Random(unchecked((data.seed * 397) ^ (int)0x9E3779B9u));
        _leafSubRng = new System.Random(unchecked((data.seed * 733) ^ (int)0x7F4A7C15u));
        _leafSubLevel2Rng = new System.Random(unchecked((data.seed * 1231) ^ (int)0x27D4EB2Fu));
        // Osobne strumienie dla próbkowania krzywych alongDistribution* (nie korelują z tip/węzłami/orientacją).
        _leafAlongMainRng = new System.Random(unchecked((data.seed * 1549) ^ (int)0x85EBCA6Bu));
        _leafAlongSubRng = new System.Random(unchecked((data.seed * 1741) ^ (int)0xC2B2AE35u));
        _leafAlongSubLevel2Rng = new System.Random(unchecked((data.seed * 1949) ^ (int)0x165667B1u));
        _verts.Clear();
        _uvs.Clear();
        _colors.Clear();
        _woodTris.Clear();
        _leafTris.Clear();
        _clusterLeafTris.Clear();
        _generatedMainBranchCount = 0;
        _generatedSubBranchCount = 0;
        _generatedLeafCount = 0;
        _generatedClusterLeafCount = 0;
        _upwardLeafCount = 0;
        _checkedLeafCount = 0;

        bool flatVerticalBranchWood =
            lodFlatVerticalBranchWood
            && _activeLodLevel >= Mathf.Clamp(lodFlatVerticalBranchStartLevel, 1, 5);

        // ── Trunk
        List<TubeNode> trunk = BuildTrunkNodes();
        _currentTrunkNodes = trunk;
        AddTube(trunk, data.trunk.sides, wood: true, reduceSidesPerSegmentToThree: data.trunk.reduceSidesPerSegmentToThree);

        // ── Main branches
        if (data.mainBranches.enabled)
        {
            BranchSettings bs = data.mainBranches;
            var mainBranchSpecs = new List<(TubeNode origin, float phi, float theta, float length, float maxRadius)>();
            float shortestLength = float.MaxValue;
            float longestLength  = float.MinValue;

            foreach (var (origin, phi) in SampleTrunkPositions(trunk, bs))
            {
                float theta  = RandRange(bs.minAngle, bs.maxAngle);
                float length = SampleBranchLength(origin.t, bs);
                float startHeightFactor = Mathf.Max(0f, bs.maxRadiusByStartHeight.Evaluate(origin.t));
                float branchMaxRadius = bs.maxRadius * startHeightFactor;

                mainBranchSpecs.Add((origin, phi, theta, length, branchMaxRadius));
                if (length < shortestLength) shortestLength = length;
                if (length > longestLength)  longestLength = length;
            }

            foreach (var spec in mainBranchSpecs)
            {
                float mainLength01 = (longestLength - shortestLength) > 1e-5f
                    ? Mathf.InverseLerp(shortestLength, longestLength, spec.length)
                    : 1f;

                int branchSegments = bs.segments;
                if (bs.segments > 1)
                {
                    float segT = EvaluateSegmentsByBranchLength01(bs.segmentsByBranchLength, mainLength01);
                    branchSegments = Mathf.RoundToInt(Mathf.Lerp(1f, bs.segments, segT));
                }
                branchSegments = Mathf.Clamp(branchSegments, 1, bs.segments);
                float segmentFactor = bs.segments > 0 ? (float)branchSegments / bs.segments : 1f;
                float branchBendAmount = bs.bendAmount * segmentFactor;
                float branchTwistAmount = bs.twistAmount * segmentFactor;

                List<TubeNode> branch = BuildChildNodes(
                    spec.origin, spec.phi, spec.theta, spec.length,
                    branchBendAmount, branchTwistAmount,
                    branchSegments, bs.radiusCurve,
                    radiusMultiplier: spec.maxRadius,
                    radiusCurveIsNormalized: true);
                _generatedMainBranchCount++;

                if (renderMainBranchGeometry)
                {
                    if (flatVerticalBranchWood)
                        AddVerticalRibbonTube(branch);
                    else
                        AddTube(branch, bs.sides, wood: true, reduceSidesPerSegmentToThree: bs.reduceSidesPerSegmentToThree);
                }

                // ── Sub-branches
                if (data.subBranches.enabled)
                {
                    SubBranchSettings ss = data.subBranches;
                    float branchHeightFactor = Mathf.Max(0f, ss.countByTrunkHeight.Evaluate(spec.origin.t));
                    float branchLength01 = (longestLength - shortestLength) > 1e-5f
                        ? Mathf.InverseLerp(shortestLength, longestLength, spec.length)
                        : 1f;
                    float branchLengthFactor = Mathf.Max(0f, ss.countByParentLength.Evaluate(branchLength01));
                    int subBranchCount = Mathf.RoundToInt(ss.countPerBranch * branchHeightFactor * branchLengthFactor);
                    subBranchCount = Mathf.Clamp(subBranchCount, 0, ss.countPerBranch);
                    float minSubLengthGlobal = shortestLength * ss.lengthRatio * 0.8f;
                    float maxSubLengthGlobal = longestLength * ss.lengthRatio * 1.2f;
                    SubBranchLevel2Settings ss2 = data.subBranchesLevel2;
                    float minSub2LengthGlobal = minSubLengthGlobal * ss2.lengthRatio * 0.8f;
                    float maxSub2LengthGlobal = maxSubLengthGlobal * ss2.lengthRatio * 1.2f;

                    foreach (var (subOrigin, subPhi) in SampleChildPositions(branch, ss.startPosition, ss.endPosition, subBranchCount, _subBranchRng))
                    {
                        float subTheta  = RandRange(_subBranchRng, ss.minAngle, ss.maxAngle);
                        float subLength = spec.length * ss.lengthRatio * RandRange(_subBranchRng, 0.8f, 1.2f);
                        float minSubLength = spec.length * ss.lengthRatio * 0.8f;
                        float maxSubLength = spec.length * ss.lengthRatio * 1.2f;
                        float subLength01 = (maxSubLength - minSubLength) > 1e-5f
                            ? Mathf.InverseLerp(minSubLength, maxSubLength, subLength)
                            : 1f;
                        int subSegments = Mathf.RoundToInt(Mathf.Lerp(1f, ss.segments, subLength01));
                        subSegments = Mathf.Clamp(subSegments, 1, ss.segments);
                        float leafSubLength01 = (maxSubLengthGlobal - minSubLengthGlobal) > 1e-5f
                            ? Mathf.InverseLerp(minSubLengthGlobal, maxSubLengthGlobal, subLength)
                            : 1f;

                        List<TubeNode> sub = BuildChildNodes(
                            subOrigin, subPhi, subTheta, subLength,
                            ss.bendAmount, 0f,
                            subSegments, ss.radiusCurve,
                            rng: _subBranchRng);
                        _generatedSubBranchCount++;

                        if (renderSubBranchGeometry)
                        {
                            if (flatVerticalBranchWood)
                                AddVerticalRibbonTube(sub);
                            else
                                AddTube(sub, ss.sides, wood: true);
                        }

                        if (ss2.enabled)
                        {
                            float subLengthGlobal01 = (maxSubLengthGlobal - minSubLengthGlobal) > 1e-5f
                                ? Mathf.InverseLerp(minSubLengthGlobal, maxSubLengthGlobal, subLength)
                                : 1f;
                            float sub2CountFactor = Mathf.Max(0f, ss2.countByParentLength.Evaluate(subLengthGlobal01));
                            int sub2Count = Mathf.RoundToInt(ss2.countPerBranch * sub2CountFactor);
                            sub2Count = Mathf.Clamp(sub2Count, 0, ss2.countPerBranch);

                            foreach (var (sub2Origin, sub2Phi) in SampleChildPositions(sub, ss2.startPosition, ss2.endPosition, sub2Count, _subBranchLevel2Rng))
                            {
                                float sub2Theta  = RandRange(_subBranchLevel2Rng, ss2.minAngle, ss2.maxAngle);
                                float sub2Length = subLength * ss2.lengthRatio * RandRange(_subBranchLevel2Rng, 0.8f, 1.2f);
                                float minSub2Length = subLength * ss2.lengthRatio * 0.8f;
                                float maxSub2Length = subLength * ss2.lengthRatio * 1.2f;
                                float sub2Length01 = (maxSub2Length - minSub2Length) > 1e-5f
                                    ? Mathf.InverseLerp(minSub2Length, maxSub2Length, sub2Length)
                                    : 1f;
                                int sub2Segments = Mathf.RoundToInt(Mathf.Lerp(1f, ss2.segments, sub2Length01));
                                sub2Segments = Mathf.Clamp(sub2Segments, 1, ss2.segments);
                                float leafSub2Length01 = (maxSub2LengthGlobal - minSub2LengthGlobal) > 1e-5f
                                    ? Mathf.InverseLerp(minSub2LengthGlobal, maxSub2LengthGlobal, sub2Length)
                                    : 1f;

                                List<TubeNode> sub2 = BuildChildNodes(
                                    sub2Origin, sub2Phi, sub2Theta, sub2Length,
                                    ss2.bendAmount, 0f,
                                    sub2Segments, ss2.radiusCurve,
                                    rng: _subBranchLevel2Rng);
                                _generatedSubBranchCount++;

                                if (renderSubBranchLevel2Geometry)
                                {
                                    if (flatVerticalBranchWood)
                                        AddVerticalRibbonTube(sub2);
                                    else
                                        AddTube(sub2, ss2.sides, wood: true);
                                }

                                if (data.leaves.enabled)
                                    AddLeaves(sub2, LeafTarget.SubBranchLevel2, leafSub2Length01);
                            }
                        }

                        if (data.leaves.enabled)
                            AddLeaves(sub, LeafTarget.SubBranch, leafSubLength01);
                    }
                }

                if (data.leaves.enabled)
                    AddLeaves(branch, LeafTarget.MainBranch, mainLength01);
            }
        }

        CommitMesh();
        data = previousData;
    }

    private void GenerateLods(TreeData sourceData)
    {
        int levelCount = Mathf.Clamp(lodLevels, 1, 5);
        int[] woodTrianglesByLod = new int[levelCount];
        int[] leafTrianglesByLod = new int[levelCount];
        int[] clusterLeavesByLod = new int[levelCount];
        LODGroup lodGroup = GetComponent<LODGroup>();
        if (lodGroup == null)
            lodGroup = gameObject.AddComponent<LODGroup>();

        MeshRenderer rootRenderer = GetComponent<MeshRenderer>();
        if (levelCount <= 1)
        {
            CleanupExtraLodObjects(0);
            lodGroup.SetLODs(new[] { new LOD(Mathf.Clamp01(lodStartScreenRelativeHeight), new Renderer[] { rootRenderer }) });
            lodGroup.RecalculateBounds();
            woodTrianglesByLod[0] = lastWoodTriangleCount;
            leafTrianglesByLod[0] = lastLeafTriangleCount;
            clusterLeavesByLod[0] = lastClusterLeafCount;
            SetLodTriangleStats(woodTrianglesByLod, leafTrianglesByLod, clusterLeavesByLod);
            return;
        }

        Material[] sharedMaterials = rootRenderer.sharedMaterials;
        List<Vector3> billboardCentroidsWorld = null;
        if (useBillboardLeafLods && levelCount > 1 && sourceData != null && sourceData.leaves != null && sourceData.leaves.enabled)
        {
            MeshFilter rootMfLod0 = GetComponent<MeshFilter>();
            if (rootMfLod0 != null && rootMfLod0.sharedMesh != null)
            {
                Mesh lod0Snap = Instantiate(rootMfLod0.sharedMesh);
                billboardCentroidsWorld = LeafBillboardLodBuilder.CollectLeafTriangleCentroidsWorld(lod0Snap, transform);
                if (Application.isPlaying) Destroy(lod0Snap);
                else DestroyImmediate(lod0Snap);
            }
        }

        var lodRenderersPerLevel = new List<Renderer[]> { new[] { rootRenderer } };

        for (int level = 1; level < levelCount; level++)
        {
            GameObject lodObject = GetOrCreateLodObject(level);
            var lodFilter = lodObject.GetComponent<MeshFilter>();
            var lodRenderer = lodObject.GetComponent<MeshRenderer>();
            lodRenderer.sharedMaterials = sharedMaterials;

            bool billboardLod = ShouldUseBillboardLeafLods(level);
            TreeData lodData = CreateLodDataClone(sourceData, level, levelCount);
            if (billboardLod)
                lodData.leaves.enabled = false;

            GenerateSinglePass(lodData, level);
            woodTrianglesByLod[level] = lastWoodTriangleCount;
            leafTrianglesByLod[level] = lastLeafTriangleCount;
            clusterLeavesByLod[level] = lastClusterLeafCount;

            Mesh generated = GetComponent<MeshFilter>().sharedMesh;
            Mesh lodMesh = generated != null ? Instantiate(generated) : null;
            if (lodMesh != null)
            {
                lodMesh.name = $"ProceduralTree_LOD{level}";
                if (!billboardLod && ShouldUseLeafVolumeForLod(level))
                {
                    Mesh volumeMesh = LeafVolumeMesher.BuildCombinedWoodAndLeafVolume(
                        lodMesh,
                        leafVolumeGridResolution,
                        leafVolumeSampleRadiusInVoxels,
                        leafVolumeIsoLevel,
                        leafVolumeSmoothIterations,
                        leafVolumeBoundsPadding,
                        leafVolumeGeometryOptimize,
                        leafVolumeWeldEpsilon,
                        leafVolumeMinTriangleAreaSq,
                        leafVolumeCloseFieldHoles,
                        leafVolumeHoleCloseRadius,
                        leafVolumeSmoothAfterHoleClose);

                    if (volumeMesh != null)
                    {
                        if (Application.isPlaying) Destroy(lodMesh);
                        else DestroyImmediate(lodMesh);
                        lodMesh = volumeMesh;
                        lodMesh.name = $"ProceduralTree_LOD{level}_LeafVolume";
                    }
                }
            }

            AssignMeshReplacingOld(lodFilter, lodMesh);

            Renderer[] renderersThisLod;
            if (billboardLod)
            {
                CleanupBillboardUnderLod(lodObject);
                GameObject bbGo = GetOrCreateBillboardChild(lodObject);
                var bbFilter = bbGo.GetComponent<MeshFilter>();
                var bbRenderer = bbGo.GetComponent<MeshRenderer>();

                var subCentroids = new List<Vector3>();
                if (billboardCentroidsWorld != null && billboardCentroidsWorld.Count > 0)
                {
                    LeafBillboardLodBuilder.SubsampleCentroids(
                        billboardCentroidsWorld,
                        subCentroids,
                        Mathf.Clamp(billboardLeafLodMaxSprites, 4, 256),
                        sourceData.seed + level * 977);

                    Mesh bbMesh = LeafBillboardLodBuilder.BuildBillboardQuadMesh(
                        subCentroids,
                        lodObject.transform,
                        billboardLeafWorldWidth,
                        billboardLeafWorldHeight,
                        sourceData.seed + level * 131,
                        billboardLeafJitterRadius);

                    AssignMeshReplacingOld(bbFilter, bbMesh);
                    Material bbMat = billboardLeafMaterial != null ? billboardLeafMaterial : leafMaterial;
                    bbRenderer.sharedMaterial = bbMat;
                    bbRenderer.enabled = bbMesh != null;
                }
                else
                {
                    AssignMeshReplacingOld(bbFilter, null);
                    bbRenderer.sharedMaterial = null;
                    bbRenderer.enabled = false;
                    if (billboardCentroidsWorld == null || billboardCentroidsWorld.Count == 0)
                        Debug.LogWarning("[TreeGenerator] Billboard LOD: brak próbek z liści LOD0 — tylko drewno.");
                }

                if (lodMesh != null && lodMesh.subMeshCount > 0)
                {
                    woodTrianglesByLod[level] = lodMesh.GetTriangles(0).Length / 3;
                    int bbTris = bbRenderer.enabled && bbFilter.sharedMesh != null
                        ? bbFilter.sharedMesh.triangles.Length / 3
                        : 0;
                    leafTrianglesByLod[level] = bbTris;
                    clusterLeavesByLod[level] = 0;
                }

                renderersThisLod = new[] { lodRenderer, bbRenderer };
            }
            else
            {
                CleanupBillboardUnderLod(lodObject);

                if (lodMesh != null && lodMesh.subMeshCount > 0)
                {
                    woodTrianglesByLod[level] = lodMesh.GetTriangles(0).Length / 3;
                    int leafTris = lodMesh.subMeshCount > 1 ? lodMesh.GetTriangles(1).Length / 3 : 0;
                    int clusterLeafTris = lodMesh.subMeshCount > 2 ? lodMesh.GetTriangles(2).Length / 3 : 0;
                    leafTrianglesByLod[level] = leafTris + clusterLeafTris;
                    clusterLeavesByLod[level] = 0;
                }

                renderersThisLod = new[] { lodRenderer };
            }

            lodRenderersPerLevel.Add(renderersThisLod);
            DestroyLodClone(lodData);
        }

        // Restore LOD0 mesh on root object.
        GenerateSinglePass(sourceData, 0);
        woodTrianglesByLod[0] = lastWoodTriangleCount;
        leafTrianglesByLod[0] = lastLeafTriangleCount;
        clusterLeavesByLod[0] = lastClusterLeafCount;
        Mesh lod0FallbackMesh = GetComponent<MeshFilter>().sharedMesh != null
            ? Instantiate(GetComponent<MeshFilter>().sharedMesh)
            : null;

        CleanupExtraLodObjects(levelCount - 1);
        ApplyLodGroupSetup(lodGroup, lodRenderersPerLevel, levelCount);
        SetLodTriangleStats(woodTrianglesByLod, leafTrianglesByLod, clusterLeavesByLod);
        ApplyLodScaleInLightmap(lodRenderersPerLevel, woodTrianglesByLod, leafTrianglesByLod);

        if (lod0FallbackMesh != null)
        {
            for (int level = 1; level < levelCount; level++)
            {
                Transform child = transform.Find($"__AutoLOD_{level}");
                if (child == null) continue;
                MeshFilter mf = child.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (IsMeshValidForBaking(mf.sharedMesh)) continue;

                Mesh replacement = Instantiate(lod0FallbackMesh);
                replacement.name = $"ProceduralTree_LOD{level}_Fallback";
                AssignMeshReplacingOld(mf, replacement);
                Debug.LogWarning($"[TreeGenerator] LOD{level} mesh was invalid for baking. Replaced with fallback mesh.");
            }

            if (Application.isPlaying) Destroy(lod0FallbackMesh);
            else DestroyImmediate(lod0FallbackMesh);
        }
    }

    private bool ShouldUseLeafVolumeForLod(int lodLevel)
    {
        return useLeafVolumeLods && lodLevel >= Mathf.Clamp(leafVolumeStartLodLevel, 1, 5);
    }

    private bool ShouldUseBillboardLeafLods(int lodLevel)
    {
        return useBillboardLeafLods && lodLevel >= Mathf.Clamp(billboardLeafLodStartLevel, 1, 5);
    }

    private static GameObject GetOrCreateBillboardChild(GameObject lodObject)
    {
        Transform existing = lodObject.transform.Find("BillboardCanopy");
        if (existing != null)
            return existing.gameObject;

        var go = new GameObject("BillboardCanopy");
        go.transform.SetParent(lodObject.transform, false);
        go.hideFlags = HideFlags.None;
        go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>();
        return go;
    }

    private static void CleanupBillboardUnderLod(GameObject lodObject)
    {
        if (lodObject == null)
            return;

        Transform t = lodObject.transform.Find("BillboardCanopy");
        if (t == null)
            return;

        var mf = t.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(mf.sharedMesh);
            else UnityEngine.Object.DestroyImmediate(mf.sharedMesh);
        }

        if (Application.isPlaying) UnityEngine.Object.Destroy(t.gameObject);
        else UnityEngine.Object.DestroyImmediate(t.gameObject);
    }

    private void SetLodTriangleStats(
        IReadOnlyList<int> woodTrianglesByLod,
        IReadOnlyList<int> leafTrianglesByLod,
        IReadOnlyList<int> clusterLeavesByLod)
    {
        lastLodWoodTriangleCounts.Clear();
        lastLodLeafTriangleCounts.Clear();
        lastLodClusterLeafCounts.Clear();
        for (int i = 0; i < woodTrianglesByLod.Count; i++)
            lastLodWoodTriangleCounts.Add(woodTrianglesByLod[i]);
        for (int i = 0; i < leafTrianglesByLod.Count; i++)
            lastLodLeafTriangleCounts.Add(leafTrianglesByLod[i]);
        for (int i = 0; i < clusterLeavesByLod.Count; i++)
            lastLodClusterLeafCounts.Add(clusterLeavesByLod[i]);
    }

    private void ApplyLodScaleInLightmap(
        IReadOnlyList<Renderer[]> lodRenderersPerLevel,
        IReadOnlyList<int> woodTrianglesByLod,
        IReadOnlyList<int> leafTrianglesByLod)
    {
#if !UNITY_EDITOR
        return;
#else
        if (!autoLodScaleInLightmap || lodRenderersPerLevel == null || lodRenderersPerLevel.Count == 0)
            return;

        if (manualLodScaleInLightmap)
        {
            for (int i = 0; i < lodRenderersPerLevel.Count; i++)
            {
                Renderer[] group = lodRenderersPerLevel[i];
                if (group == null) continue;
                float scale = Mathf.Max(0.001f, GetManualLodScaleInLightmap(i));
                for (int g = 0; g < group.Length; g++)
                {
                    if (group[g] is MeshRenderer meshRenderer)
                        SetMeshRendererLightmapBakeResolution(meshRenderer, scale);
                }
            }

            return;
        }

        bool canUseWoodOnly = lodLightmapScaleUseWoodOnly &&
                              woodTrianglesByLod != null &&
                              woodTrianglesByLod.Count > 0 &&
                              woodTrianglesByLod[0] > 0;

        int baseTris = canUseWoodOnly
            ? Mathf.Max(0, woodTrianglesByLod[0])
            : 0;
        if (!canUseWoodOnly)
        {
            if (woodTrianglesByLod != null && woodTrianglesByLod.Count > 0)
                baseTris += Mathf.Max(0, woodTrianglesByLod[0]);
            if (leafTrianglesByLod != null && leafTrianglesByLod.Count > 0)
                baseTris += Mathf.Max(0, leafTrianglesByLod[0]);
        }
        baseTris = Mathf.Max(1, baseTris);

        float minScale = Mathf.Clamp(lodMinScaleInLightmap, 0.001f, 1f);
        float power = Mathf.Max(0.01f, lodLightmapScalePower);
        float lod0Bias = Mathf.Clamp01(lodLightmapScaleBiasToLod0);

        for (int i = 0; i < lodRenderersPerLevel.Count; i++)
        {
            Renderer[] group = lodRenderersPerLevel[i];
            if (group == null) continue;

            int tris = canUseWoodOnly
                ? ((woodTrianglesByLod != null && i < woodTrianglesByLod.Count) ? Mathf.Max(0, woodTrianglesByLod[i]) : 0)
                : 0;
            if (!canUseWoodOnly)
            {
                if (woodTrianglesByLod != null && i < woodTrianglesByLod.Count)
                    tris += Mathf.Max(0, woodTrianglesByLod[i]);
                if (leafTrianglesByLod != null && i < leafTrianglesByLod.Count)
                    tris += Mathf.Max(0, leafTrianglesByLod[i]);
            }

            float ratio = Mathf.Clamp01((float)tris / baseTris);
            float rawScale = i == 0 ? 1f : Mathf.Max(minScale, Mathf.Pow(ratio, power));
            float scale = i == 0 ? 1f : Mathf.Lerp(rawScale, 1f, lod0Bias);
            for (int g = 0; g < group.Length; g++)
            {
                if (group[g] is MeshRenderer meshRenderer)
                    SetMeshRendererLightmapBakeResolution(meshRenderer, scale);
            }
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// Unity 6+ no longer exposes <see cref="MeshRenderer"/> lightmap scale / stitch on the public scripting API;
    /// values are editor-only serialized fields.
    /// </summary>
    private static void SetMeshRendererLightmapBakeResolution(MeshRenderer meshRenderer, float scaleInLightmap)
    {
        SerializedObject so = new SerializedObject(meshRenderer);
        SerializedProperty scaleProp = so.FindProperty("m_ScaleInLightmap");
        if (scaleProp != null)
            scaleProp.floatValue = scaleInLightmap;
        SerializedProperty stitchProp = so.FindProperty("m_StitchLightmapSeams");
        if (stitchProp != null)
        {
            if (stitchProp.propertyType == SerializedPropertyType.Boolean)
                stitchProp.boolValue = true;
            else
                stitchProp.intValue = 1;
        }
        so.ApplyModifiedProperties();
    }
#endif

    private float GetManualLodScaleInLightmap(int lodLevel)
    {
        switch (lodLevel)
        {
            case 0: return lod0ScaleInLightmap;
            case 1: return lod1ScaleInLightmap;
            case 2: return lod2ScaleInLightmap;
            case 3: return lod3ScaleInLightmap;
            default: return lod4ScaleInLightmap;
        }
    }

    private void ApplyLodGroupSetup(LODGroup group, List<Renderer[]> lodRenderersPerLevel, int levelCount)
    {
        var lods = new LOD[levelCount];
        for (int i = 0; i < levelCount; i++)
        {
            float t = levelCount <= 1 ? 0f : (float)i / (levelCount - 1);
            float transition = Mathf.Lerp(lodStartScreenRelativeHeight, lodEndScreenRelativeHeight, t);
            Renderer[] r = lodRenderersPerLevel != null && i < lodRenderersPerLevel.Count
                ? lodRenderersPerLevel[i]
                : null;
            if (r == null || r.Length == 0)
                r = new Renderer[] { GetComponent<MeshRenderer>() };
            lods[i] = new LOD(Mathf.Clamp(transition, 0.001f, 1f), r);
        }

        group.fadeMode = LODFadeMode.None;
        group.animateCrossFading = false;
        group.SetLODs(lods);
        group.RecalculateBounds();
    }

    private GameObject GetOrCreateLodObject(int level)
    {
        string name = $"__AutoLOD_{level}";
        Transform child = transform.Find(name);
        GameObject lodObject = child != null ? child.gameObject : new GameObject(name);
        lodObject.transform.SetParent(transform, false);
        lodObject.hideFlags = HideFlags.None;

        if (!lodObject.TryGetComponent<MeshFilter>(out _))
            lodObject.AddComponent<MeshFilter>();
        if (!lodObject.TryGetComponent<MeshRenderer>(out _))
            lodObject.AddComponent<MeshRenderer>();

        return lodObject;
    }

    private void CleanupExtraLodObjects(int maxLevelIndex)
    {
        var toDelete = new List<GameObject>();
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (!child.name.StartsWith("__AutoLOD_")) continue;
            if (!int.TryParse(child.name.Substring("__AutoLOD_".Length), out int level)) continue;
            if (level <= maxLevelIndex) continue;
            toDelete.Add(child.gameObject);
        }

        for (int i = 0; i < toDelete.Count; i++)
        {
            if (Application.isPlaying) Destroy(toDelete[i]);
            else DestroyImmediate(toDelete[i]);
        }
    }

    private TreeData CreateLodDataClone(TreeData sourceData, int level, int levelCount)
    {
        TreeData clone = Instantiate(sourceData);
        float t = levelCount <= 1 ? 1f : (float)level / (levelCount - 1);
        float geoScale = Mathf.Lerp(1f, 0.3f, t);
        float leafT = Mathf.Pow(t, Mathf.Max(0.5f, lodLeafReductionExponent));
        int leafReductionStartLod = Mathf.Clamp(lodLeafCountReductionStartLevel, 1, 5);
        float leafCountScale = level < leafReductionStartLod
            ? 1f
            : Mathf.Lerp(1f, Mathf.Clamp01(lodFinalLeafCountMultiplier), leafT)
              * GetInnerClusterLeafCountReductionForLod(level);
        float leafSizeScale = Mathf.Lerp(1f, Mathf.Max(1f, lodFinalLeafSizeMultiplier), leafT);

        // Preserve tree shape/silhouette: reduce mesh resolution (segments/sides); optional full segment counts.
        clone.trunk.segments = lodPreserveTrunkSegments
            ? Mathf.Max(2, sourceData.trunk.segments)
            : Mathf.Max(2, Mathf.RoundToInt(sourceData.trunk.segments * geoScale));
        clone.trunk.sides = Mathf.Max(3, Mathf.RoundToInt(sourceData.trunk.sides * geoScale));

        clone.mainBranches.segments = lodPreserveBranchSegments
            ? Mathf.Max(1, sourceData.mainBranches.segments)
            : Mathf.Max(1, Mathf.RoundToInt(sourceData.mainBranches.segments * geoScale));
        clone.mainBranches.sides = Mathf.Max(3, Mathf.RoundToInt(sourceData.mainBranches.sides * geoScale));

        clone.subBranches.segments = lodPreserveBranchSegments
            ? Mathf.Max(1, sourceData.subBranches.segments)
            : Mathf.Max(1, Mathf.RoundToInt(sourceData.subBranches.segments * geoScale));
        clone.subBranches.sides = Mathf.Max(3, Mathf.RoundToInt(sourceData.subBranches.sides * geoScale));
        clone.subBranchesLevel2.segments = lodPreserveBranchSegments
            ? Mathf.Max(1, sourceData.subBranchesLevel2.segments)
            : Mathf.Max(1, Mathf.RoundToInt(sourceData.subBranchesLevel2.segments * geoScale));
        clone.subBranchesLevel2.sides = Mathf.Max(3, Mathf.RoundToInt(sourceData.subBranchesLevel2.sides * geoScale));

        // Aggressive leaf reduction for LODs + size compensation.
        clone.leaves.countPerTipMainBranch = Mathf.Max(0, Mathf.RoundToInt(sourceData.leaves.countPerTipMainBranch * leafCountScale));
        clone.leaves.countPerTipSubBranch = Mathf.Max(0, Mathf.RoundToInt(sourceData.leaves.countPerTipSubBranch * leafCountScale));
        clone.leaves.countPerTipSubBranchLevel2 = Mathf.Max(0, Mathf.RoundToInt(sourceData.leaves.countPerTipSubBranchLevel2 * leafCountScale));
        clone.leaves.countPerNodeMainBranch = Mathf.Max(0, Mathf.RoundToInt(sourceData.leaves.countPerNodeMainBranch * leafCountScale));
        clone.leaves.countPerNodeSubBranch = Mathf.Max(0, Mathf.RoundToInt(sourceData.leaves.countPerNodeSubBranch * leafCountScale));
        clone.leaves.countPerNodeSubBranchLevel2 = Mathf.Max(0, Mathf.RoundToInt(sourceData.leaves.countPerNodeSubBranchLevel2 * leafCountScale));
        clone.leaves.countAlongMainBranch = Mathf.Max(0, Mathf.RoundToInt(sourceData.leaves.countAlongMainBranch * leafCountScale));
        clone.leaves.countAlongSubBranch = Mathf.Max(0, Mathf.RoundToInt(sourceData.leaves.countAlongSubBranch * leafCountScale));
        clone.leaves.countAlongSubBranchLevel2 = Mathf.Max(0, Mathf.RoundToInt(sourceData.leaves.countAlongSubBranchLevel2 * leafCountScale));

        // Keep tip/node leaves only on LOD0.
        if (level >= 1)
        {
            clone.leaves.countPerTipMainBranch = 0;
            clone.leaves.countPerTipSubBranch = 0;
            clone.leaves.countPerTipSubBranchLevel2 = 0;
            clone.leaves.countPerNodeMainBranch = 0;
            clone.leaves.countPerNodeSubBranch = 0;
            clone.leaves.countPerNodeSubBranchLevel2 = 0;
        }

        clone.leaves.minSize = sourceData.leaves.minSize * leafSizeScale;
        clone.leaves.maxSize = sourceData.leaves.maxSize * leafSizeScale;
        float topLeafDampingForCurve = enableLodInnerLeafClustering
            ? GetTopLeafDampingForLod(level)
            : 1f;
        clone.leaves.sizeByTreeHeight = CreateLodHeightSizeCurve(
            sourceData.leaves.sizeByTreeHeight,
            leafT,
            topLeafDampingForCurve,
            level);
        clone.leaves.minSeparationMainBranch = sourceData.leaves.minSeparationMainBranch * Mathf.Lerp(1f, 0.55f, leafT);
        clone.leaves.minSeparationSubBranch = sourceData.leaves.minSeparationSubBranch * Mathf.Lerp(1f, 0.55f, leafT);
        clone.leaves.minSeparationSubBranchLevel2 = sourceData.leaves.minSeparationSubBranchLevel2 * Mathf.Lerp(1f, 0.55f, leafT);
        clone.leaves.placementAttemptsPerLeaf = Mathf.Max(1, Mathf.RoundToInt(sourceData.leaves.placementAttemptsPerLeaf * Mathf.Lerp(1f, 0.6f, leafT)));

        return clone;
    }

    private float GetInnerClusterLeafCountReductionForLod(int level)
    {
        if (!enableLodInnerLeafClustering || level <= 0)
            return 1f;

        float replacement;
        float groupLeaves;
        switch (level)
        {
            case 1:
                replacement = lod1InnerClusterReplacement;
                groupLeaves = lod1InnerClusterGroupLeaves;
                break;
            case 2:
                replacement = lod2InnerClusterReplacement;
                groupLeaves = lod2InnerClusterGroupLeaves;
                break;
            case 3:
                replacement = lod3InnerClusterReplacement;
                groupLeaves = lod3InnerClusterGroupLeaves;
                break;
            default:
                replacement = lod4InnerClusterReplacement;
                groupLeaves = lod4InnerClusterGroupLeaves;
                break;
        }

        float clampedReplacement = Mathf.Clamp01(replacement);
        float clampedGroupLeaves = Mathf.Max(1f, groupLeaves);
        float effectiveGroupStrength = Mathf.Lerp(1f, clampedGroupLeaves, clampedReplacement);
        float reduction = 1f / Mathf.Pow(effectiveGroupStrength, 0.65f);
        return Mathf.Clamp(reduction, 0.02f, 1f);
    }

    private AnimationCurve CreateLodHeightSizeCurve(AnimationCurve source, float leafT, float topLeafDamping, int lodLevel)
    {
        if (source == null || source.keys == null || source.keys.Length == 0)
            return source;

        // Reduce top canopy inflation on lower LODs while preserving base silhouette.
        float topTarget = Mathf.Clamp01(topLeafDamping);
        float heightExponent = 1f;

        Keyframe[] keys = source.keys;
        for (int i = 0; i < keys.Length; i++)
        {
            float height01 = Mathf.Clamp01(keys[i].time);
            float heightT = Mathf.Pow(height01, heightExponent);
            float curveT = lodTopLeafDampingByHeight != null
                ? Mathf.Max(0f, lodTopLeafDampingByHeight.Evaluate(height01))
                : heightT;

            // Slightly boost LOD1 to keep its effect noticeable with conservative curves.
            if (lodLevel == 1)
                curveT *= 1.1f;

            float dampingStrength = topTarget; // direct per-LOD control
            float blend = curveT * Mathf.Max(0f, lodTopLeafCurveInfluence);
            float heightFactor = Mathf.LerpUnclamped(1f, dampingStrength, blend);
            heightFactor = Mathf.Max(0f, heightFactor);
            keys[i].value *= heightFactor;
            keys[i].inTangent *= heightFactor;
            keys[i].outTangent *= heightFactor;
        }

        AnimationCurve result = new AnimationCurve(keys);
        result.preWrapMode = source.preWrapMode;
        result.postWrapMode = source.postWrapMode;
        return result;
    }

    private float GetTopLeafDampingForLod(int level)
    {
        return level switch
        {
            1 => lod1TopLeafSizeDamping,
            2 => lod2TopLeafSizeDamping,
            3 => lod3TopLeafSizeDamping,
            _ => lod4TopLeafSizeDamping
        };
    }

    private void AssignMeshReplacingOld(MeshFilter filter, Mesh newMesh)
    {
        Mesh oldMesh = filter.sharedMesh;
        filter.sharedMesh = newMesh;
        if (oldMesh != null && oldMesh != _mesh)
        {
            if (Application.isPlaying) Destroy(oldMesh);
            else DestroyImmediate(oldMesh);
        }
    }

    private void DestroyLodClone(TreeData clone)
    {
        if (clone == null) return;
        if (Application.isPlaying) Destroy(clone);
        else DestroyImmediate(clone);
    }

    private bool IsMeshValidForBaking(Mesh mesh)
    {
        if (mesh == null || mesh.vertexCount < 3) return false;
        int[] triangles = mesh.triangles;
        if (triangles == null || triangles.Length < 3) return false;

        Vector3[] vertices = mesh.vertices;
        if (vertices == null || vertices.Length < 3) return false;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i];
            if (!IsFinite(v.x) || !IsFinite(v.y) || !IsFinite(v.z))
                return false;
        }

        bool hasNonDegenerateTriangle = false;
        for (int i = 0; i <= triangles.Length - 3; i += 3)
        {
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];
            if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
                return false;
            if (a == b || b == c || a == c) continue;

            float areaSq = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).sqrMagnitude;
            if (areaSq > 1e-12f)
            {
                hasNonDegenerateTriangle = true;
                break;
            }
        }

        return hasNonDegenerateTriangle;
    }

    private bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    /// <summary>
    /// Returns true only if current data can be safely generated.
    /// </summary>
    public bool TryValidateSettings(out string error)
    {
        if (data == null)
        {
            error = "Assign a TreeData asset first.";
            return false;
        }

        TrunkSettings t = data.trunk;
        if (t == null)
        {
            error = "Missing trunk settings.";
            return false;
        }

        if (t.height <= 0f)
        {
            error = "Trunk height must be greater than 0.";
            return false;
        }
        if (t.segments < 1)
        {
            error = "Trunk segments must be at least 1.";
            return false;
        }
        if (t.sides < 3)
        {
            error = "Trunk sides must be at least 3.";
            return false;
        }
        if (t.maxRadius <= 0f)
        {
            error = "Trunk max radius must be greater than 0.";
            return false;
        }

        BranchSettings mb = data.mainBranches;
        if (mb != null && mb.enabled)
        {
            if (mb.startHeight >= mb.endHeight)
            {
                error = "Main branches: start height must be lower than end height.";
                return false;
            }
            if (mb.minLength > mb.maxLength)
            {
                error = "Main branches: min length cannot be greater than max length.";
                return false;
            }
            if (mb.minAngle > mb.maxAngle)
            {
                error = "Main branches: min angle cannot be greater than max angle.";
                return false;
            }
            if (mb.maxRadius <= 0f)
            {
                error = "Main branches: max radius must be greater than 0.";
                return false;
            }
            if (mb.segments < 1 || mb.sides < 3)
            {
                error = "Main branches: segments must be >= 1 and sides >= 3.";
                return false;
            }
        }

        SubBranchSettings sb = data.subBranches;
        if (sb != null && sb.enabled)
        {
            if (sb.startPosition >= sb.endPosition)
            {
                error = "Sub-branches: start position must be lower than end position.";
                return false;
            }
            if (sb.minAngle > sb.maxAngle)
            {
                error = "Sub-branches: min angle cannot be greater than max angle.";
                return false;
            }
            if (sb.segments < 1 || sb.sides < 3)
            {
                error = "Sub-branches: segments must be >= 1 and sides >= 3.";
                return false;
            }
        }

        SubBranchLevel2Settings sb2 = data.subBranchesLevel2;
        if (sb2 != null && sb2.enabled)
        {
            if (sb2.startPosition >= sb2.endPosition)
            {
                error = "Sub-branches level 2: start position must be lower than end position.";
                return false;
            }
            if (sb2.minAngle > sb2.maxAngle)
            {
                error = "Sub-branches level 2: min angle cannot be greater than max angle.";
                return false;
            }
            if (sb2.segments < 1 || sb2.sides < 3)
            {
                error = "Sub-branches level 2: segments must be >= 1 and sides >= 3.";
                return false;
            }
        }

        LeafSettings l = data.leaves;
        if (l != null && l.enabled && l.minSize > l.maxSize)
        {
            error = "Leaves: min size cannot be greater than max size.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    // ── Trunk ────────────────────────────────────────────────────────────────

    private List<TubeNode> BuildTrunkNodes()
    {
        TrunkSettings s = data.trunk;
        var nodes = new List<TubeNode>();

        // Horizontal direction the trunk leans toward
        float bdRad = s.bendDirection * Mathf.Deg2Rad;
        Vector3 leanDir = new Vector3(Mathf.Sin(bdRad), 0f, Mathf.Cos(bdRad));

        // Axis around which the trunk bends (perpendicular to lean direction, in XZ plane)
        Vector3 bendAxis = Vector3.Cross(Vector3.up, leanDir);
        if (bendAxis.sqrMagnitude < 1e-5f) bendAxis = Vector3.right;
        else                               bendAxis.Normalize();

        Vector3 pos   = Vector3.zero;
        float   stepH = s.height / s.segments;

        for (int i = 0; i <= s.segments; i++)
        {
            float t = (float)i / s.segments;

            // Trunk twist rotates bend direction along trunk height (analogous to branches).
            Vector3 bendAxisAtT = Quaternion.AngleAxis(s.twist * t, Vector3.up) * bendAxis;
            Quaternion rot = Quaternion.AngleAxis(s.bendAngle * t, bendAxisAtT);

            nodes.Add(new TubeNode
            {
                position = pos,
                rotation = rot,
                radius   = Mathf.Clamp(s.radiusCurve.Evaluate(t), 0.0001f, s.maxRadius),
                t        = t
            });

            if (i < s.segments)
                pos += rot * Vector3.up * stepH;
        }

        return nodes;
    }

    // ── Branch sampling ───────────────────────────────────────────────────────

    /// <summary>Samples branch attachment points on the trunk using rejection sampling with the density curve.</summary>
    private List<(TubeNode origin, float phi)> SampleTrunkPositions(List<TubeNode> trunk, BranchSettings bs)
    {
        var result = new List<(TubeNode, float)>();

        // Normalise density curve
        float maxD = 0f;
        for (int k = 0; k <= 20; k++)
            maxD = Mathf.Max(maxD, bs.densityCurve.Evaluate(k / 20f));
        if (maxD < 1e-4f) maxD = 1f;

        int maxAttempts = bs.count * 300;
        int attempts    = 0;
        while (result.Count < bs.count && attempts < maxAttempts)
        {
            attempts++;
            float relT = NextFloat();
            float absH = Mathf.Lerp(bs.startHeight, bs.endHeight, relT);
            float d    = bs.densityCurve.Evaluate(relT) / maxD;
            if (NextFloat() > d) continue;
            float phi = NextFloat() * 360f;
            if (IsTooCloseToExistingMainBranches(result, absH, phi, bs)) continue;

            result.Add((InterpolateNode(trunk, absH), phi));
        }

        // Fill remainder uniformly while still trying to respect spacing
        int fillAttempts = 0;
        int maxFillAttempts = bs.count * 300;
        while (result.Count < bs.count && fillAttempts < maxFillAttempts)
        {
            fillAttempts++;
            float absH = Mathf.Lerp(bs.startHeight, bs.endHeight, NextFloat());
            float phi = NextFloat() * 360f;
            if (IsTooCloseToExistingMainBranches(result, absH, phi, bs)) continue;
            result.Add((InterpolateNode(trunk, absH), phi));
        }

        // If settings are too restrictive, complete the target count without spacing constraints.
        while (result.Count < bs.count)
        {
            float absH = Mathf.Lerp(bs.startHeight, bs.endHeight, NextFloat());
            result.Add((InterpolateNode(trunk, absH), NextFloat() * 360f));
        }

        return result;
    }

    private bool IsTooCloseToExistingMainBranches(
        List<(TubeNode origin, float phi)> existing, float candidateHeight, float candidatePhi, BranchSettings bs)
    {
        if (!bs.enforceMinSeparation) return false;

        for (int i = 0; i < existing.Count; i++)
        {
            float heightDelta = Mathf.Abs(existing[i].origin.t - candidateHeight);
            float angleDelta  = Mathf.Abs(Mathf.DeltaAngle(existing[i].phi, candidatePhi));
            if (heightDelta < bs.minHeightSeparation && angleDelta < bs.minAngularSeparation)
                return true;
        }

        return false;
    }

    private List<(TubeNode origin, float phi)> SampleChildPositions(
        List<TubeNode> parent, float start, float end, int count, System.Random rng = null)
    {
        System.Random randomSource = rng ?? _rng;
        var result = new List<(TubeNode, float)>();
        for (int i = 0; i < count; i++)
        {
            float h = Mathf.Lerp(start, end, NextFloat(randomSource));
            result.Add((InterpolateNode(parent, h), NextFloat(randomSource) * 360f));
        }
        return result;
    }

    // ── Child node builder (branches and sub-branches) ─────────────────────

    /// <param name="origin">Node on the parent tube where this child starts.</param>
    /// <param name="phi">Azimuthal angle around parent axis (degrees).</param>
    /// <param name="theta">Angle away from parent axis (degrees).</param>
    /// <param name="length">Total length of this child tube.</param>
    /// <param name="bendAmount">Progressive bend amount over the length (degrees total).</param>
    /// <param name="twistAmount">Rotation of bend direction along branch length (degrees total).</param>
    private List<TubeNode> BuildChildNodes(
        TubeNode origin, float phi, float theta, float length,
        float bendAmount, float twistAmount,
        int segments, AnimationCurve radiusCurve,
        float radiusMultiplier = 1f,
        bool radiusCurveIsNormalized = false,
        System.Random rng = null)
    {
        System.Random randomSource = rng ?? _rng;
        // Direction of parent tube at origin
        Vector3 parentDir = origin.rotation * Vector3.up;

        // Radial (outward) direction perpendicular to parent, at angle phi
        Vector3 radialDir = GetRadialDir(parentDir, phi);

        // Branch grows at angle theta from the parent axis, in the radial plane
        Vector3 branchDir = Quaternion.AngleAxis(theta, radialDir) * parentDir;

        // Base orientation: local Y aligned with branchDir
        Quaternion baseRot = Quaternion.FromToRotation(Vector3.up, branchDir);
        // Random phase gives natural variation between branches, twistAmount drives progression along t.
        float bendDirectionPhase = NextFloat(randomSource) * 360f;

        // First ring always starts at the center of trunk cross-section.
        float baseRadius = EvaluateRadius(radiusCurve, 0f, radiusMultiplier, radiusCurveIsNormalized);
        Vector3 startPos = origin.position;
        float   stepL    = length / segments;

        var nodes = new List<TubeNode>();
        Vector3 pos = startPos;

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;

            // Twist controls how bend direction rotates along the branch.
            Vector3 bendAxis = Quaternion.AngleAxis(bendDirectionPhase + (twistAmount * t), branchDir) * radialDir;
            Quaternion droop = Quaternion.AngleAxis(bendAmount * t, bendAxis);
            Quaternion rot   = droop * baseRot;

            nodes.Add(new TubeNode
            {
                position = pos,
                rotation = rot,
                radius   = i == 0 ? baseRadius : EvaluateRadius(radiusCurve, t, radiusMultiplier, radiusCurveIsNormalized),
                t        = t
            });

            if (i < segments)
                pos += rot * Vector3.up * stepL;
        }

        return nodes;
    }

    // ── Leaves ────────────────────────────────────────────────────────────────

    private void AddLeaves(List<TubeNode> branchNodes, LeafTarget target, float length01)
    {
        LeafSettings ls = data.leaves;
        int countPerTip = GetLeafCountPerTip(ls, target);
        int countPerNode = GetLeafCountPerNode(ls, target);
        int countAlongBase = GetLeafCountAlong(ls, target);
        int countAlong = ScaleAlongLeafCountByLength(countAlongBase, length01);
        AnimationCurve alongDistribution = GetLeafAlongDistribution(ls, target);
        float minLeafSeparation = GetLeafMinSeparation(ls, target);
        List<Vector3> placedLeafStems = (ls.enforceMinSeparation && minLeafSeparation > 0f)
            ? new List<Vector3>()
            : null;

        // Leaves at the tip
        TubeNode tip = branchNodes[branchNodes.Count - 1];
        if (countPerTip > 0)
            AddLeavesAtNode(tip, countPerTip, ls, target, placedLeafStems, minLeafSeparation);

        // Intermediate leaves from alongBranchStart toward the tip
        if (countPerNode > 0 && ls.alongBranchStart < 1f)
        {
            for (int i = 0; i < branchNodes.Count - 1; i++)
            {
                if (branchNodes[i].t >= ls.alongBranchStart)
                    AddLeavesAtNode(branchNodes[i], countPerNode, ls, target, placedLeafStems, minLeafSeparation);
            }
        }

        // Additional continuous distribution along branch length (independent from nodes/tips).
        if (countAlong > 0 && ls.alongBranchStart < 1f)
            AddLeavesAlongBranch(branchNodes, countAlong, ls.alongBranchStart, alongDistribution, ls, target, placedLeafStems, minLeafSeparation);
    }

    private int AddLeavesAtNode(
        TubeNode node, int count, LeafSettings ls, LeafTarget target, List<Vector3> placedLeafStems, float minLeafSeparation)
    {
        int placed = 0;
        int generated = 0;

        while (generated < count)
        {
            if (!TryGenerateLeafPlacement(node, ls, target, placedLeafStems, minLeafSeparation,
                    out Vector3 stem, out Vector3 leafDir, out Vector3 radial, out float size))
            {
                generated++;
                continue;
            }

            int representedLeaves = GetRepresentedLeafCount(
                node,
                target,
                count - generated,
                out float clusterSizeFactor,
                out bool isClusteredLeaf,
                out int requestedGroupLeaves);
            float representedT = Mathf.InverseLerp(1f, 100f, isClusteredLeaf ? requestedGroupLeaves : representedLeaves);
            float coverageScale = Mathf.Lerp(1f, 2.25f, representedT);
            float finalSize = size * clusterSizeFactor * coverageScale;
            Color leafColor = ApplyClusterDebugTint(ls.color, isClusteredLeaf ? requestedGroupLeaves : representedLeaves, isClusteredLeaf);
            AddLeafQuad(stem, leafDir, radial, finalSize, leafColor, target, isClusteredLeaf);
            generated += representedLeaves;

            if (placedLeafStems != null)
                placedLeafStems.Add(stem);
            placed++;
        }

        return placed;
    }

    private void AddLeavesAlongBranch(
        List<TubeNode> branchNodes, int count, float startT, AnimationCurve densityCurve, LeafSettings ls, LeafTarget target,
        List<Vector3> placedLeafStems, float minLeafSeparation)
    {
        startT = Mathf.Clamp01(startT);
        if (count <= 0 || startT >= 1f) return;

        float maxD = 0f;
        for (int k = 0; k <= 20; k++)
            maxD = Mathf.Max(maxD, densityCurve.Evaluate(k / 20f));
        if (maxD < 1e-4f) maxD = 1f;

        int represented = 0;
        int attempts = 0;
        int maxAttempts = count * 300;

        while (represented < count && attempts < maxAttempts)
        {
            attempts++;
            float t = Mathf.Lerp(startT, 1f, NextLeafAlongFloat(target));
            float rel = Mathf.InverseLerp(startT, 1f, t);
            float d = densityCurve.Evaluate(rel) / maxD;
            if (NextLeafAlongFloat(target) > d) continue;

            TubeNode node = InterpolateNode(branchNodes, t);
            if (!TryGenerateLeafPlacement(node, ls, target, placedLeafStems, minLeafSeparation,
                    out Vector3 stem, out Vector3 leafDir, out Vector3 radial, out float size))
                continue;

            int representedLeaves = GetRepresentedLeafCount(
                node,
                target,
                count - represented,
                out float clusterSizeFactor,
                out bool isClusteredLeaf,
                out int requestedGroupLeaves);
            float representedT = Mathf.InverseLerp(1f, 100f, isClusteredLeaf ? requestedGroupLeaves : representedLeaves);
            float coverageScale = Mathf.Lerp(1f, 2.25f, representedT);
            float finalSize = size * clusterSizeFactor * coverageScale;
            Color leafColor = ApplyClusterDebugTint(ls.color, isClusteredLeaf ? requestedGroupLeaves : representedLeaves, isClusteredLeaf);
            AddLeafQuad(stem, leafDir, radial, finalSize, leafColor, target, isClusteredLeaf);

            if (placedLeafStems != null)
                placedLeafStems.Add(stem);
            represented += representedLeaves;
        }

        // Fill any missing leaves uniformly to guarantee requested count.
        int fallbackAttempts = 0;
        int maxFallbackAttempts = count * Mathf.Max(5, ls.placementAttemptsPerLeaf) * 10;
        while (represented < count && fallbackAttempts < maxFallbackAttempts)
        {
            fallbackAttempts++;
            float t = Mathf.Lerp(startT, 1f, NextLeafAlongFloat(target));
            TubeNode node = InterpolateNode(branchNodes, t);
            if (!TryGenerateLeafPlacement(node, ls, target, placedLeafStems, minLeafSeparation,
                    out Vector3 stem, out Vector3 leafDir, out Vector3 radial, out float size))
                continue;

            int representedLeaves = GetRepresentedLeafCount(
                node,
                target,
                count - represented,
                out float clusterSizeFactor,
                out bool isClusteredLeaf,
                out int requestedGroupLeaves);
            float representedT = Mathf.InverseLerp(1f, 100f, isClusteredLeaf ? requestedGroupLeaves : representedLeaves);
            float coverageScale = Mathf.Lerp(1f, 2.25f, representedT);
            float finalSize = size * clusterSizeFactor * coverageScale;
            Color leafColor = ApplyClusterDebugTint(ls.color, isClusteredLeaf ? requestedGroupLeaves : representedLeaves, isClusteredLeaf);
            AddLeafQuad(stem, leafDir, radial, finalSize, leafColor, target, isClusteredLeaf);

            if (placedLeafStems != null)
                placedLeafStems.Add(stem);
            represented += representedLeaves;
        }
    }

    private bool TryGenerateLeafPlacement(
        TubeNode node, LeafSettings ls, LeafTarget target, List<Vector3> placedLeafStems, float minLeafSeparation,
        out Vector3 stem, out Vector3 leafDir, out Vector3 radial, out float size)
    {
        Vector3 axis = node.rotation * Vector3.up;
        int attempts = placedLeafStems == null ? 1 : Mathf.Max(1, ls.placementAttemptsPerLeaf);

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            float baseSize = RandLeafRange(ls.minSize, ls.maxSize, target);
            float sizeMultiplier = EvaluateLeafSizeMultiplier(node, ls);
            size    = Mathf.Max(0.01f, baseSize * sizeMultiplier);
            radial  = GetRadialDir(axis, NextLeafFloat(target) * 360f);
            leafDir = RandomInCone(axis, ls.spreadAngle * 0.5f, target);
            Vector3 trunkOutward = GetTrunkOutwardDirection(node.position, radial);
            leafDir = Vector3.Slerp(leafDir, trunkOutward, Mathf.Clamp01(ls.outwardDirectionCorrelation)).normalized;

            if (ls.droop > 0f)
                leafDir = Vector3.Slerp(leafDir, -Vector3.up, ls.droop * 0.5f).normalized;

            float radius = Mathf.Max(node.radius, 0.0001f);
            stem = node.position + radial * (radius + size * 0.06f);

            if (placedLeafStems == null || !IsTooCloseToOtherLeaves(stem, placedLeafStems, minLeafSeparation))
                return true;
        }

        stem = default;
        leafDir = default;
        radial = default;
        size = 0f;
        return false;
    }

    private Vector3 GetTrunkOutwardDirection(Vector3 point, Vector3 fallback)
    {
        if (!TryGetNearestTrunkNode(point, out TubeNode nearest))
            return fallback;

        Vector3 trunkAxis = nearest.rotation * Vector3.up;
        Vector3 toPoint = point - nearest.position;
        Vector3 outward = Vector3.ProjectOnPlane(toPoint, trunkAxis);
        if (outward.sqrMagnitude < 1e-6f)
            return fallback;
        return outward.normalized;
    }

    private bool TryGetNearestTrunkNode(Vector3 point, out TubeNode nearest)
    {
        if (_currentTrunkNodes == null || _currentTrunkNodes.Count == 0)
        {
            nearest = default;
            return false;
        }

        nearest = _currentTrunkNodes[0];
        float nearestSqr = (nearest.position - point).sqrMagnitude;
        for (int i = 1; i < _currentTrunkNodes.Count; i++)
        {
            float sqr = (_currentTrunkNodes[i].position - point).sqrMagnitude;
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = _currentTrunkNodes[i];
            }
        }

        return true;
    }

    private float EvaluateLeafSizeMultiplier(TubeNode node, LeafSettings ls)
    {
        if (!TryGetNearestTrunkNode(node.position, out TubeNode nearest))
            return 1f;

        float height01 = Mathf.Clamp01(nearest.t);
        float byHeight = Mathf.Max(0f, ls.sizeByTreeHeight.Evaluate(height01));

        Vector3 trunkAxis = nearest.rotation * Vector3.up;
        Vector3 toNode = node.position - nearest.position;
        Vector3 radial = Vector3.ProjectOnPlane(toNode, trunkAxis);
        float distanceFromTrunkSurface = Mathf.Max(0f, radial.magnitude - nearest.radius);
        float distance01 = Mathf.Clamp01(distanceFromTrunkSurface / Mathf.Max(0.01f, _leafDistanceNormalization));
        float byDistance = Mathf.Max(0f, ls.sizeByDistanceFromTrunk.Evaluate(distance01));

        return byHeight * byDistance;
    }

    private float EvaluateLeafDistanceFromTrunk01(Vector3 point)
    {
        if (!TryGetNearestTrunkNode(point, out TubeNode nearest))
            return 1f;

        Vector3 trunkAxis = nearest.rotation * Vector3.up;
        Vector3 toPoint = point - nearest.position;
        Vector3 radial = Vector3.ProjectOnPlane(toPoint, trunkAxis);
        float distanceFromTrunkSurface = Mathf.Max(0f, radial.magnitude - nearest.radius);
        return Mathf.Clamp01(distanceFromTrunkSurface / Mathf.Max(0.01f, _leafDistanceNormalization));
    }

    private bool TryGetInnerLeafClusterSettings(
        TubeNode node,
        out float replacementRatio,
        out int groupLeaves,
        out float sizeMultiplier)
    {
        replacementRatio = 0f;
        groupLeaves = 1;
        sizeMultiplier = 1f;

        if (!enableLodInnerLeafClustering || _activeLodLevel <= 0)
            return false;

        float min01 = Mathf.Min(lodInnerClusterRangeMin01, lodInnerClusterRangeMax01);
        float max01 = Mathf.Max(lodInnerClusterRangeMin01, lodInnerClusterRangeMax01);
        float distance01 = EvaluateLeafDistanceFromTrunk01(node.position);
        if (distance01 < min01 || distance01 > max01)
            return false;

        float leafHeight01 = EvaluateLeafTreeHeight01(node.position);
        float heightFactor = lodInnerClusterByHeight != null
            ? Mathf.Max(0f, lodInnerClusterByHeight.Evaluate(leafHeight01))
            : 1f;
        if (heightFactor <= 1e-4f)
            return false;

        switch (_activeLodLevel)
        {
            case 1:
                replacementRatio = lod1InnerClusterReplacement;
                groupLeaves = Mathf.RoundToInt(lod1InnerClusterGroupLeaves);
                sizeMultiplier = lod1InnerClusterSizeMultiplier;
                break;
            case 2:
                replacementRatio = lod2InnerClusterReplacement;
                groupLeaves = Mathf.RoundToInt(lod2InnerClusterGroupLeaves);
                sizeMultiplier = lod2InnerClusterSizeMultiplier;
                break;
            case 3:
                replacementRatio = lod3InnerClusterReplacement;
                groupLeaves = Mathf.RoundToInt(lod3InnerClusterGroupLeaves);
                sizeMultiplier = lod3InnerClusterSizeMultiplier;
                break;
            default:
                replacementRatio = lod4InnerClusterReplacement;
                groupLeaves = Mathf.RoundToInt(lod4InnerClusterGroupLeaves);
                sizeMultiplier = lod4InnerClusterSizeMultiplier;
                break;
        }

        replacementRatio = Mathf.Clamp01(replacementRatio * heightFactor);
        groupLeaves = Mathf.Max(2, groupLeaves);
        sizeMultiplier = Mathf.Max(1f, sizeMultiplier);
        return replacementRatio > 0f;
    }

    private float EvaluateLeafTreeHeight01(Vector3 point)
    {
        if (!TryGetNearestTrunkNode(point, out TubeNode nearest))
            return 0.5f;
        return Mathf.Clamp01(nearest.t);
    }

    private int GetRepresentedLeafCount(
        TubeNode node,
        LeafTarget target,
        int remainingRequestedLeaves,
        out float clusterSizeFactor,
        out bool isClusteredLeaf,
        out int requestedGroupLeaves)
    {
        clusterSizeFactor = 1f;
        isClusteredLeaf = false;
        requestedGroupLeaves = 1;
        if (remainingRequestedLeaves <= 0)
            return 0;

        if (!TryGetInnerLeafClusterSettings(node, out float replacementRatio, out int groupLeaves, out float sizeMultiplier))
            return 1;

        if (NextLeafFloat(target) >= replacementRatio)
            return 1;

        isClusteredLeaf = true;
        requestedGroupLeaves = Mathf.Max(1, groupLeaves);
        clusterSizeFactor = sizeMultiplier;
        return Mathf.Clamp(groupLeaves, 1, remainingRequestedLeaves);
    }

    private Color ApplyClusterDebugTint(Color baseColor, int representedLeaves, bool isClusteredLeaf)
    {
        if (!debugTintClusteredLeaves || !isClusteredLeaf)
            return baseColor;

        float t = Mathf.InverseLerp(1f, 100f, representedLeaves);
        Color debugRed = new Color(1f, 0.22f, 0.22f, baseColor.a);
        Color tinted = Color.Lerp(baseColor, debugRed, Mathf.Lerp(0.5f, 0.9f, t));
        tinted.a = baseColor.a;
        return tinted;
    }

    private bool IsTooCloseToOtherLeaves(Vector3 stem, List<Vector3> placedLeafStems, float minLeafSeparation)
    {
        float minSq = minLeafSeparation * minLeafSeparation;
        for (int i = 0; i < placedLeafStems.Count; i++)
        {
            if ((placedLeafStems[i] - stem).sqrMagnitude < minSq)
                return true;
        }
        return false;
    }

    private void AddLeafQuad(Vector3 stem, Vector3 leafDir, Vector3 surfaceNormal, float size, Color color, LeafTarget target, bool clustered = false)
    {
        if (clustered)
            _generatedClusterLeafCount++;

        List<int> triBuffer = clustered ? _clusterLeafTris : _leafTris;

        // Width direction tangent to branch surface to avoid the base looking centered/inset.
        Vector3 widthDir = Vector3.Cross(leafDir, surfaceNormal).normalized;
        if (widthDir.sqrMagnitude < 1e-6f)
        {
            Vector3 refUp = Mathf.Abs(Vector3.Dot(leafDir, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right;
            widthDir = Vector3.Cross(leafDir, refUp).normalized;
        }
        widthDir = Quaternion.AngleAxis((NextLeafFloat(target) * 60f) - 30f, leafDir) * widthDir;

        if (_activeLodLevel == 0)
        {
            Vector3 avgNormal = AddLeafBentLod0(stem, leafDir, widthDir, size, color, triBuffer);
            RegisterLeafNormal(avgNormal);
            _generatedLeafCount++;
            return;
        }

        float   hw  = size * 0.35f;
        Vector3 tip = stem + leafDir * size;

        // Single-triangle leaf (use two-sided material for backface rendering).
        Vector3 v0 = stem - widthDir * hw; // base-left
        Vector3 v1 = stem + widthDir * hw; // base-right
        Vector3 v2 = tip;                   // tip

        int b = _verts.Count;
        _verts.Add(v0);
        _verts.Add(v1);
        _verts.Add(v2);

        _uvs.Add(new Vector2(0, 0));
        _uvs.Add(new Vector2(1, 0));
        _uvs.Add(new Vector2(0.5f, 1));

        _colors.Add(color);
        _colors.Add(color);
        _colors.Add(color);

        Vector3 n = AddLeafTriangleFacingUp(b, b + 1, b + 2, triBuffer);
        RegisterLeafNormal(n);
        _generatedLeafCount++;
    }

    private Vector3 AddLeafBentLod0(Vector3 stem, Vector3 leafDir, Vector3 widthDir, float size, Color color, List<int> triBuffer)
    {
        float halfWidth = size * 0.34f;
        float length = size;
        Vector3 bendNormal = Vector3.Cross(widthDir, leafDir).normalized;

        // Diamond-like perimeter (rotated ~45° vs previous layout):
        // p0 is the attachment tip at the branch, then left, far tip, right.
        Vector3 p0 = stem; // attachment vertex
        Vector3 p1 = stem + leafDir * (length * 0.46f) - widthDir * halfWidth;
        Vector3 p2 = stem + leafDir * length;
        Vector3 p3 = stem + leafDir * (length * 0.46f) + widthDir * halfWidth;

        // "Boat" curvature: shared center lifted from perimeter plane.
        Vector3 center = (p0 + p1 + p2 + p3) * 0.25f + bendNormal * (size * 0.18f);

        int b = _verts.Count;
        _verts.Add(center);
        _verts.Add(p0);
        _verts.Add(p1);
        _verts.Add(p2);
        _verts.Add(p3);

        _uvs.Add(new Vector2(0.5f, 0.5f)); // center
        _uvs.Add(new Vector2(0f, 0f));
        _uvs.Add(new Vector2(0f, 1f));
        _uvs.Add(new Vector2(1f, 1f));
        _uvs.Add(new Vector2(1f, 0f));

        for (int i = 0; i < 5; i++) _colors.Add(color);

        // 4 triangles sharing the center vertex.
        Vector3 n0 = AddLeafTriangleFacingUp(b + 0, b + 1, b + 2, triBuffer);
        Vector3 n1 = AddLeafTriangleFacingUp(b + 0, b + 2, b + 3, triBuffer);
        Vector3 n2 = AddLeafTriangleFacingUp(b + 0, b + 3, b + 4, triBuffer);
        Vector3 n3 = AddLeafTriangleFacingUp(b + 0, b + 4, b + 1, triBuffer);
        Vector3 avg = (n0 + n1 + n2 + n3) * 0.25f;
        return avg.sqrMagnitude > 1e-6f ? avg.normalized : Vector3.up;
    }

    private Vector3 AddLeafTriangleFacingUp(int i0, int i1, int i2, List<int> triBuffer)
    {
        Vector3 v0 = _verts[i0];
        Vector3 v1 = _verts[i1];
        Vector3 v2 = _verts[i2];
        Vector3 n = Vector3.Cross(v1 - v0, v2 - v0);

        if (n.sqrMagnitude > 1e-12f && Vector3.Dot(n, Vector3.up) < 0f)
        {
            int tmp = i1;
            i1 = i2;
            i2 = tmp;
            v1 = _verts[i1];
            v2 = _verts[i2];
            n = Vector3.Cross(v1 - v0, v2 - v0);
        }

        triBuffer.Add(i0);
        triBuffer.Add(i1);
        triBuffer.Add(i2);
        return n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
    }

    // ── Tube mesh generation ─────────────────────────────────────────────────

    private void AddTube(List<TubeNode> nodes, int sides, bool wood, bool reduceSidesPerSegmentToThree = false, bool addTipCap = true)
    {
        if (nodes == null || nodes.Count < 2) return;

        List<int> tris = wood ? _woodTris : _leafTris;
        var ringStarts = new List<int>(nodes.Count);
        var ringSides  = new List<int>(nodes.Count);
        float uvWorldSize = Mathf.Max(0.01f, woodUvWorldSize);

        // Arc-length along tube centerline (world units) for stable V texel density.
        var cumulativeLen = new float[nodes.Count];
        cumulativeLen[0] = 0f;
        for (int i = 1; i < nodes.Count; i++)
            cumulativeLen[i] = cumulativeLen[i - 1] + Vector3.Distance(nodes[i - 1].position, nodes[i].position);

        // Rings of vertices
        int previousSides = sides;
        for (int ring = 0; ring < nodes.Count; ring++)
        {
            TubeNode node = nodes[ring];
            int currentSides = sides;
            if (reduceSidesPerSegmentToThree)
            {
                float ringT = nodes.Count > 1 ? (float)ring / (nodes.Count - 1) : 1f;
                currentSides = Mathf.RoundToInt(Mathf.Lerp(sides, 3f, ringT));
                currentSides = Mathf.Clamp(currentSides, 3, sides);
                currentSides = Mathf.Min(currentSides, previousSides); // enforce monotonic decrease
            }

            ringStarts.Add(_verts.Count);
            ringSides.Add(currentSides);
            previousSides = currentSides;

            for (int s = 0; s <= currentSides; s++)
            {
                float   angle = (float)s / currentSides * Mathf.PI * 2f;
                Vector3 local = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * node.radius;
                float ringCircumference = Mathf.Max(2f * Mathf.PI * node.radius, 1e-5f);
                float u = ((float)s / currentSides) * (ringCircumference / uvWorldSize);
                float v = cumulativeLen[ring] / uvWorldSize;

                _verts.Add(node.position + node.rotation * local);
                _uvs.Add(new Vector2(u, v));
                _colors.Add(Color.white);
            }
        }

        // Strips between consecutive rings (supports different side counts per ring)
        for (int n = 0; n < nodes.Count - 1; n++)
        {
            int sidesA = ringSides[n];
            int sidesB = ringSides[n + 1];
            int startA = ringStarts[n];
            int startB = ringStarts[n + 1];
            int steps  = Mathf.Max(sidesA, sidesB);

            for (int step = 0; step < steps; step++)
            {
                int a0 = Mathf.FloorToInt((float)step       * sidesA / steps);
                int a1 = Mathf.FloorToInt((float)(step + 1) * sidesA / steps);
                int b0 = Mathf.FloorToInt((float)step       * sidesB / steps);
                int b1 = Mathf.FloorToInt((float)(step + 1) * sidesB / steps);

                bool advanceA = a1 != a0;
                bool advanceB = b1 != b0;
                if (!advanceA && !advanceB) continue;

                int iA0 = startA + a0;
                int iA1 = startA + a1;
                int iB0 = startB + b0;
                int iB1 = startB + b1;

                if (advanceA && advanceB)
                {
                    tris.Add(iA0); tris.Add(iB0); tris.Add(iA1);
                    tris.Add(iA1); tris.Add(iB0); tris.Add(iB1);
                }
                else if (advanceA)
                {
                    tris.Add(iA0); tris.Add(iB0); tris.Add(iA1);
                }
                else
                {
                    tris.Add(iA0); tris.Add(iB0); tris.Add(iB1);
                }
            }
        }

        if (addTipCap)
        {
            // Tip cap (fan triangulation)
            TubeNode tipNode  = nodes[nodes.Count - 1];
            int      capRing  = ringStarts[nodes.Count - 1];
            int      capSides = ringSides[nodes.Count - 1];
            int      capCenter = _verts.Count;

            _verts.Add(tipNode.position);
            _uvs.Add(new Vector2(0.5f, 0.5f));
            _colors.Add(Color.white);

            for (int s = 0; s < capSides; s++)
            {
                tris.Add(capCenter);
                tris.Add(capRing + s + 1);
                tris.Add(capRing + s);
            }
        }
    }

    /// <summary>
    /// Wood-only LOD: ribbon in the vertical plane through the branch tangent and world up
    /// (width = component of up ⟂ tangent). Ignores roll/twist baked into TubeNode.rotation.
    /// Two-sided quads; no tip cap (minimal silhouette from above/below).
    /// </summary>
    private void AddVerticalRibbonTube(List<TubeNode> nodes)
    {
        if (nodes == null || nodes.Count < 2) return;

        List<int> tris = _woodTris;
        float uvWorldSize = Mathf.Max(0.01f, woodUvWorldSize);

        var cumulativeLen = new float[nodes.Count];
        cumulativeLen[0] = 0f;
        for (int i = 1; i < nodes.Count; i++)
            cumulativeLen[i] = cumulativeLen[i - 1] + Vector3.Distance(nodes[i - 1].position, nodes[i].position);

        var ringLeft = new int[nodes.Count];
        Vector3 prevWidthDir = Vector3.zero;
        bool havePrevWidth = false;
        for (int ring = 0; ring < nodes.Count; ring++)
        {
            TubeNode node = nodes[ring];
            Vector3 axis = GetRibbonPolylineTangent(nodes, ring);
            Vector3 widthDir = VerticalRibbonWidthDirFromAxis(axis);
            if (havePrevWidth && Vector3.Dot(widthDir, prevWidthDir) < 0f)
                widthDir = -widthDir;
            prevWidthDir = widthDir;
            havePrevWidth = true;
            float halfW = Mathf.Max(1e-6f, node.radius);
            float v = cumulativeLen[ring] / uvWorldSize;
            float uSpan = (2f * halfW) / uvWorldSize;

            ringLeft[ring] = _verts.Count;
            _verts.Add(node.position - widthDir * halfW);
            _uvs.Add(new Vector2(0f, v));
            _colors.Add(Color.white);

            _verts.Add(node.position + widthDir * halfW);
            _uvs.Add(new Vector2(uSpan, v));
            _colors.Add(Color.white);
        }

        for (int n = 0; n < nodes.Count - 1; n++)
        {
            int l0 = ringLeft[n];
            int r0 = l0 + 1;
            int l1 = ringLeft[n + 1];
            int r1 = l1 + 1;

            tris.Add(l0);
            tris.Add(l1);
            tris.Add(r0);
            tris.Add(r0);
            tris.Add(l1);
            tris.Add(r1);

            tris.Add(l0);
            tris.Add(r0);
            tris.Add(l1);
            tris.Add(r0);
            tris.Add(r1);
            tris.Add(l1);
        }
    }

    /// <summary>Center-difference tangent along the branch polyline (twist/roll does not affect it).</summary>
    private static Vector3 GetRibbonPolylineTangent(List<TubeNode> nodes, int i)
    {
        if (nodes.Count < 2)
            return Vector3.up;
        if (i <= 0)
            return SegmentDirection(nodes[0].position, nodes[1].position);
        if (i >= nodes.Count - 1)
            return SegmentDirection(nodes[nodes.Count - 2].position, nodes[nodes.Count - 1].position);
        return SegmentDirection(nodes[i - 1].position, nodes[i + 1].position);
    }

    private static Vector3 SegmentDirection(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        return d.sqrMagnitude < 1e-12f ? Vector3.up : d.normalized;
    }

    /// <summary>
    /// Unit width vector in the plane spanned by world up and tangent, perpendicular to tangent
    /// (vertical ribbon: thick in screen elevation, thin in top-down view).
    /// </summary>
    private static Vector3 VerticalRibbonWidthDirFromAxis(Vector3 axis)
    {
        if (axis.sqrMagnitude < 1e-12f)
            axis = Vector3.up;
        else
            axis.Normalize();

        Vector3 w = Vector3.up - axis * Vector3.Dot(Vector3.up, axis);
        if (w.sqrMagnitude < 1e-10f)
        {
            Vector3 h = Vector3.Cross(axis, Vector3.forward);
            if (h.sqrMagnitude < 1e-10f)
                h = Vector3.Cross(axis, Vector3.right);
            return h.sqrMagnitude < 1e-10f ? Vector3.right : h.normalized;
        }

        return w.normalized;
    }

    // ── Mesh commit ───────────────────────────────────────────────────────────

    private void CommitMesh()
    {
        if (_mesh == null)
            _mesh = new Mesh { name = "ProceduralTree" };

        _mesh.indexFormat = _verts.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        _mesh.Clear();
        _mesh.SetVertices(_verts);
        _mesh.SetUVs(0, _uvs);
        _mesh.SetColors(_colors);
        _mesh.subMeshCount = 3;
        _mesh.SetTriangles(_woodTris, 0);
        _mesh.SetTriangles(_leafTris, 1);
        _mesh.SetTriangles(_clusterLeafTris, 2);
        _mesh.RecalculateNormals();
        if (forceLeafNormalsToSun)
            AlignLeafNormalsToSun(_mesh);
        _mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = _mesh;

        // Ensure the renderer has three material slots
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr.sharedMaterials.Length != 3)
        {
            var mats = new Material[3];
            mats[0] = mr.sharedMaterials.Length > 0 ? mr.sharedMaterials[0] : null;
            mats[1] = mr.sharedMaterials.Length > 1 ? mr.sharedMaterials[1] : null;
            mats[2] = mr.sharedMaterials.Length > 2 ? mr.sharedMaterials[2] : null;
            mr.sharedMaterials = mats;
        }

        // Apply explicit material slots from the generator component.
        if (treeMaterial != null || leafMaterial != null || clusterLeafMaterial != null)
        {
            var mats = mr.sharedMaterials;
            if (treeMaterial != null) mats[0] = treeMaterial;
            if (leafMaterial != null) mats[1] = leafMaterial;
            if (clusterLeafMaterial != null) mats[2] = clusterLeafMaterial;
            else if (leafMaterial != null) mats[2] = leafMaterial;
            mr.sharedMaterials = mats;
        }

        lastVertexCount   = _verts.Count;
        lastWoodTriangleCount = _woodTris.Count / 3;
        lastLeafTriangleCount = (_leafTris.Count + _clusterLeafTris.Count) / 3;
        lastTriangleCount = (_woodTris.Count + _leafTris.Count + _clusterLeafTris.Count) / 3;
        lastMainBranchCount = _generatedMainBranchCount;
        lastSubBranchCount = _generatedSubBranchCount;
        lastLeafCount = _generatedLeafCount;
        lastClusterLeafCount = _generatedClusterLeafCount;
        lastUpwardLeafRatio = _checkedLeafCount > 0 ? (float)_upwardLeafCount / _checkedLeafCount : 1f;
        LeafSettings leaves = data != null ? data.leaves : null;
        if (leaves == null || !leaves.validateUpwardOrientation)
            lastUpwardLeafCheckPassed = true;
        else
            lastUpwardLeafCheckPassed = lastUpwardLeafRatio >= leaves.minUpwardLeafRatio;
    }

    private void RegisterLeafNormal(Vector3 leafNormal)
    {
        _checkedLeafCount++;
        LeafSettings leaves = data != null ? data.leaves : null;
        float threshold = leaves != null ? leaves.upwardDotThreshold : 0f;
        if (Vector3.Dot(leafNormal.normalized, Vector3.up) >= threshold)
            _upwardLeafCount++;
    }

    private void AlignLeafNormalsToSun(Mesh mesh)
    {
        if (mesh == null || (_leafTris.Count == 0 && _clusterLeafTris.Count == 0)) return;

        Vector3 worldSunFacingDir = Vector3.up;
        if (RenderSettings.sun != null && RenderSettings.sun.type == LightType.Directional)
            worldSunFacingDir = -RenderSettings.sun.transform.forward;

        Vector3 localSunFacingDir = transform.InverseTransformDirection(worldSunFacingDir).normalized;
        if (localSunFacingDir.sqrMagnitude < 1e-6f)
            localSunFacingDir = Vector3.up;

        Vector3[] normals = mesh.normals;
        if (normals == null || normals.Length != mesh.vertexCount)
            normals = new Vector3[mesh.vertexCount];

        var touched = new HashSet<int>();
        for (int i = 0; i < _leafTris.Count; i++)
            touched.Add(_leafTris[i]);
        for (int i = 0; i < _clusterLeafTris.Count; i++)
            touched.Add(_clusterLeafTris[i]);

        // Preserve natural shading curvature: only flip normals that face away from sun.
        foreach (int idx in touched)
        {
            Vector3 n = normals[idx];
            if (n.sqrMagnitude < 1e-8f)
            {
                normals[idx] = localSunFacingDir;
                continue;
            }

            n.Normalize();
            float dot = Vector3.Dot(n, localSunFacingDir);
            if (dot < 0f)
            {
                normals[idx] = -n;
                n = normals[idx];
                dot = Vector3.Dot(n, localSunFacingDir);
            }
            else
                normals[idx] = n;

            // Optional soft correction: lift only the darkest leaf normals toward sun direction.
            float minDot = Mathf.Clamp(leafBakeMinSunDot, 0f, 0.95f);
            if (dot < minDot)
            {
                float t = (minDot - dot) / Mathf.Max(1e-4f, 1f - dot);
                normals[idx] = Vector3.Slerp(normals[idx], localSunFacingDir, Mathf.Clamp01(t)).normalized;
            }
        }

        mesh.normals = normals;
    }

#if UNITY_EDITOR
    private void GenerateLightmapUvForMesh(Mesh mesh)
    {
        if (mesh == null) return;
        UnityEditor.Unwrapping.GenerateSecondaryUVSet(mesh);
        UnityEditor.EditorUtility.SetDirty(mesh);
    }
#endif

    // ── Math helpers ──────────────────────────────────────────────────────────

    /// <summary>Interpolates a node along a tube at normalized arc-length t.</summary>
    private TubeNode InterpolateNode(List<TubeNode> nodes, float t)
    {
        t = Mathf.Clamp01(t);
        if (t <= nodes[0].t)          return nodes[0];
        if (t >= nodes[nodes.Count-1].t) return nodes[nodes.Count - 1];

        for (int i = 0; i < nodes.Count - 1; i++)
        {
            if (nodes[i].t <= t && nodes[i + 1].t >= t)
            {
                float range = nodes[i + 1].t - nodes[i].t;
                float lt    = range < 1e-6f ? 0f : (t - nodes[i].t) / range;
                return new TubeNode
                {
                    position = Vector3.Lerp(nodes[i].position, nodes[i + 1].position, lt),
                    rotation = Quaternion.Slerp(nodes[i].rotation, nodes[i + 1].rotation, lt),
                    radius   = Mathf.Lerp(nodes[i].radius, nodes[i + 1].radius, lt),
                    t        = t
                };
            }
        }

        return nodes[nodes.Count - 1];
    }

    /// <summary>Returns a direction perpendicular to axis, rotated around axis by phiDeg.</summary>
    private Vector3 GetRadialDir(Vector3 axis, float phiDeg)
    {
        // Choose a stable reference perpendicular to axis
        Vector3 perp = Mathf.Abs(axis.y) < 0.9f
            ? Vector3.Cross(axis, Vector3.up).normalized
            : Vector3.Cross(axis, Vector3.right).normalized;

        return Quaternion.AngleAxis(phiDeg, axis) * perp;
    }

    /// <summary>Uniform random direction within a cone of given half-angle around axis.</summary>
    private Vector3 RandomInCone(Vector3 axis, float halfAngleDeg, LeafTarget target)
    {
        float halfRad  = halfAngleDeg * Mathf.Deg2Rad;
        float cosAngle = Mathf.Cos(halfRad);
        float z        = cosAngle + NextLeafFloat(target) * (1f - cosAngle);
        float phi      = NextLeafFloat(target) * Mathf.PI * 2f;
        float r        = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
        Vector3 local  = new Vector3(r * Mathf.Cos(phi), z, r * Mathf.Sin(phi));
        return Quaternion.FromToRotation(Vector3.up, axis) * local;
    }

    private float SampleBranchLength(float trunkAbsT, BranchSettings s)
    {
        float relT     = Mathf.InverseLerp(s.startHeight, s.endHeight, trunkAbsT);
        float hFactor  = s.lengthByHeight.Evaluate(relT);
        return RandRange(s.minLength, s.maxLength) * hFactor;
    }

    /// <summary>
    /// Y = udział max segmentów (0..1) dla znormalizowanej długości gałęzi (0=najkrótsza, 1=najdłuższa w zestawie).
    /// Pusta krzywa → liniowo jak X.
    /// </summary>
    private static float EvaluateSegmentsByBranchLength01(AnimationCurve curve, float length01)
    {
        float x = Mathf.Clamp01(length01);
        if (curve == null || curve.length == 0)
            return x;
        return Mathf.Clamp01(curve.Evaluate(x));
    }

    private float ComputeLeafDistanceNormalization()
    {
        if (data == null) return 1f;

        float refDist = 0f;
        if (data.mainBranches != null && data.mainBranches.enabled)
            refDist += Mathf.Max(0f, data.mainBranches.maxLength);

        if (data.subBranches != null && data.subBranches.enabled && data.mainBranches != null)
            refDist += Mathf.Max(0f, data.mainBranches.maxLength * data.subBranches.lengthRatio);

        if (data.subBranchesLevel2 != null && data.subBranchesLevel2.enabled && data.subBranches != null && data.mainBranches != null)
            refDist += Mathf.Max(0f, data.mainBranches.maxLength * data.subBranches.lengthRatio * data.subBranchesLevel2.lengthRatio);

        if (refDist <= 0f)
            refDist = Mathf.Max(0.1f, data.trunk != null ? data.trunk.maxRadius * 2f : 1f);

        return refDist;
    }

    private int ScaleAlongLeafCountByLength(int baseCount, float length01)
    {
        if (baseCount <= 0) return 0;

        int scaled = Mathf.RoundToInt(baseCount * Mathf.Clamp01(length01));
        if (scaled == 0 && length01 > 0f)
            scaled = 1;
        return Mathf.Clamp(scaled, 0, baseCount);
    }

    private int GetLeafCountPerTip(LeafSettings ls, LeafTarget target)
    {
        return target switch
        {
            LeafTarget.MainBranch => ls.countPerTipMainBranch,
            LeafTarget.SubBranch => ls.countPerTipSubBranch,
            LeafTarget.SubBranchLevel2 => ls.countPerTipSubBranchLevel2,
            _ => ls.countPerTipMainBranch
        };
    }

    private int GetLeafCountPerNode(LeafSettings ls, LeafTarget target)
    {
        return target switch
        {
            LeafTarget.MainBranch => ls.countPerNodeMainBranch,
            LeafTarget.SubBranch => ls.countPerNodeSubBranch,
            LeafTarget.SubBranchLevel2 => ls.countPerNodeSubBranchLevel2,
            _ => ls.countPerNodeMainBranch
        };
    }

    private int GetLeafCountAlong(LeafSettings ls, LeafTarget target)
    {
        return target switch
        {
            LeafTarget.MainBranch => ls.countAlongMainBranch,
            LeafTarget.SubBranch => ls.countAlongSubBranch,
            LeafTarget.SubBranchLevel2 => ls.countAlongSubBranchLevel2,
            _ => ls.countAlongMainBranch
        };
    }

    private AnimationCurve GetLeafAlongDistribution(LeafSettings ls, LeafTarget target)
    {
        return target switch
        {
            LeafTarget.MainBranch => ls.alongDistributionMainBranch,
            LeafTarget.SubBranch => ls.alongDistributionSubBranch,
            LeafTarget.SubBranchLevel2 => ls.alongDistributionSubBranchLevel2,
            _ => ls.alongDistributionMainBranch
        };
    }

    private float GetLeafMinSeparation(LeafSettings ls, LeafTarget target)
    {
        return target switch
        {
            LeafTarget.MainBranch => ls.minSeparationMainBranch,
            LeafTarget.SubBranch => ls.minSeparationSubBranch,
            LeafTarget.SubBranchLevel2 => ls.minSeparationSubBranchLevel2,
            _ => ls.minSeparationMainBranch
        };
    }

    private float EvaluateRadius(AnimationCurve curve, float t, float radiusMultiplier, bool normalizedCurve)
    {
        float value = curve.Evaluate(t);
        if (normalizedCurve)
            value = Mathf.Clamp01(value) * Mathf.Max(0f, radiusMultiplier);
        return Mathf.Max(0.0001f, value);
    }

    private float RandLeafRange(float min, float max, LeafTarget target) => min + NextLeafFloat(target) * (max - min);
    private float RandRange(System.Random rng, float min, float max) => min + NextFloat(rng) * (max - min);
    private float RandRange(float min, float max) => min + NextFloat() * (max - min);
    private float NextFloat(System.Random rng)     => (float)rng.NextDouble();
    private float NextLeafFloat(LeafTarget target)
    {
        return target switch
        {
            LeafTarget.MainBranch => (float)_leafMainRng.NextDouble(),
            LeafTarget.SubBranch => (float)_leafSubRng.NextDouble(),
            LeafTarget.SubBranchLevel2 => (float)_leafSubLevel2Rng.NextDouble(),
            _ => (float)_leafMainRng.NextDouble()
        };
    }

    /// <summary>Losowanie wyłącznie dla krzywych alongDistribution (main / sub / sub2) — osobny seed od reszty liści.</summary>
    private float NextLeafAlongFloat(LeafTarget target)
    {
        return target switch
        {
            LeafTarget.MainBranch => (float)_leafAlongMainRng.NextDouble(),
            LeafTarget.SubBranch => (float)_leafAlongSubRng.NextDouble(),
            LeafTarget.SubBranchLevel2 => (float)_leafAlongSubLevel2Rng.NextDouble(),
            _ => (float)_leafAlongMainRng.NextDouble()
        };
    }
    private float NextFloat()                       => (float)_rng.NextDouble();
}
