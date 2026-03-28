using System;
using UnityEngine;

[CreateAssetMenu(fileName = "NewTreeData", menuName = "Tree Generator/Tree Data")]
public class TreeData : ScriptableObject
{
    public int seed = 42;
    public TrunkSettings     trunk        = new TrunkSettings();
    public BranchSettings    mainBranches = new BranchSettings();
    public SubBranchSettings subBranches  = new SubBranchSettings();
    public SubBranchLevel2Settings subBranchesLevel2 = new SubBranchLevel2Settings();
    public LeafSettings      leaves       = new LeafSettings();
}

// ─── Trunk ──────────────────────────────────────────────────────────────────

[Serializable]
public class TrunkSettings
{
    [Min(0.1f)]
    [Tooltip("Total height of the trunk.")]
    public float height = 5f;

    [Range(3, 32)]
    [Tooltip("Number of cross-section rings along the trunk.")]
    public int segments = 8;

    [Range(3, 16)]
    [Tooltip("Number of vertices per ring (smoothness of cross-section).")]
    public int sides = 8;

    [Min(0.001f)]
    [Tooltip("Maximum allowed trunk radius (upper clamp for radius curve).")]
    public float maxRadius = 0.30f;

    [Tooltip("If enabled, the number of trunk sides decreases with each segment, ending at 3 on the last segment.")]
    public bool reduceSidesPerSegmentToThree = false;

    [Tooltip("Radius at each point along the trunk (X=base → tip, Y=radius).")]
    public AnimationCurve radiusCurve = new AnimationCurve(
        new Keyframe(0f, 0.30f, -0.25f, -0.25f),
        new Keyframe(1f, 0.04f, -0.25f, -0.25f));

    [Range(0f, 80f)]
    [Tooltip("How much the trunk leans from vertical (degrees).")]
    public float bendAngle = 8f;

    [Range(0f, 360f)]
    [Tooltip("Compass direction the trunk leans toward (0 = +Z, 90 = +X).")]
    public float bendDirection = 0f;

    [Range(0f, 360f)]
    [Tooltip("How much trunk bend direction rotates along trunk height (degrees total).")]
    public float twist = 15f;
}

// ─── Main Branches ──────────────────────────────────────────────────────────

[Serializable]
public class BranchSettings
{
    public bool enabled = true;

    [Range(0, 60)]
    [Tooltip("Total number of main branches to generate.")]
    public int count = 12;

    [Range(0f, 1f)]
    [Tooltip("Normalized trunk height where branches begin.")]
    public float startHeight = 0.25f;

    [Range(0f, 1f)]
    [Tooltip("Normalized trunk height where branches end.")]
    public float endHeight = 0.88f;

    [Tooltip("Relative probability of a branch at each height (X=bottom→top of range, Y=density).")]
    public AnimationCurve densityCurve = new AnimationCurve(
        new Keyframe(0f, 0.4f), new Keyframe(0.45f, 1f), new Keyframe(1f, 0.3f));

    [Tooltip("Enforces minimal spacing so branches are not generated too close to each other.")]
    public bool enforceMinSeparation = true;

    [Range(0f, 0.5f)]
    [Tooltip("Minimum spacing between branch starts along trunk height (normalized 0..1).")]
    public float minHeightSeparation = 0.04f;

    [Range(0f, 180f)]
    [Tooltip("Minimum angular spacing around trunk (degrees).")]
    public float minAngularSeparation = 20f;

    [Min(0.1f)]
    [Tooltip("Minimum branch length.")]
    public float minLength = 0.8f;

    [Min(0.1f)]
    [Tooltip("Maximum branch length.")]
    public float maxLength = 2.0f;

    [Tooltip("Length multiplier by height within the branch zone (X=bottom→top, Y=multiplier).")]
    public AnimationCurve lengthByHeight = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(1f, 0.35f));

    [Range(5f, 88f)]
    [Tooltip("Minimum angle of branches from the trunk axis (degrees).")]
    public float minAngle = 40f;

    [Range(5f, 88f)]
    [Tooltip("Maximum angle of branches from the trunk axis (degrees).")]
    public float maxAngle = 70f;

    [Range(0f, 75f)]
    [Tooltip("How much the branch droops downward over its length (degrees).")]
    public float bendAmount = 30f;

    [Range(0f, 180f)]
    [Tooltip("How much bend direction rotates along branch length (degrees total).")]
    public float twistAmount = 25f;

    [Range(1, 16)]
    [Tooltip("Maximum number of rings along each branch.")]
    public int segments = 5;

    [Range(3, 8)]
    [Tooltip("Cross-section smoothness of branches.")]
    public int sides = 5;

    [Tooltip("If enabled, shorter branches get fewer segments (shortest = 1, longest = max segments).")]
    public bool scaleSegmentsByLength = true;

    [Tooltip("If enabled, the number of branch sides decreases with each segment, ending at 3 on the last segment.")]
    public bool reduceSidesPerSegmentToThree = false;

    [Min(0.001f)]
    [Tooltip("Maximum possible branch radius. Radius curve value = 1 means this exact value.")]
    public float maxRadius = 0.06f;

    [Tooltip("Multiplier of maxRadius based on branch start height on the trunk (X=trunk height 0..1, Y=multiplier).")]
    public AnimationCurve maxRadiusByStartHeight = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(1f, 0.45f));

    [Tooltip("Normalized branch radius along branch length (X=base → tip, Y=0..1). Value 1 = maxRadius.")]
    public AnimationCurve radiusCurve = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(1f, 0.15f));
}

// ─── Sub-Branches ───────────────────────────────────────────────────────────

[Serializable]
public class SubBranchSettings
{
    public bool enabled = true;

    [Range(0, 50)]
    [Tooltip("Maximum number of sub-branches per main branch (before distribution scaling).")]
    public int countPerBranch = 3;

    [Tooltip("Multiplier of max sub-branch count by trunk height of the parent branch start (X=0..1, Y=multiplier).")]
    public AnimationCurve countByTrunkHeight = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(1f, 0.35f));

    [Tooltip("Multiplier of max sub-branch count by parent branch length (X=0 shortest, 1 longest, Y=multiplier).")]
    public AnimationCurve countByParentLength = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(1f, 0.55f));

    [Range(0f, 1f)]
    [Tooltip("Normalized branch position where sub-branches begin.")]
    public float startPosition = 0.30f;

    [Range(0f, 1f)]
    [Tooltip("Normalized branch position where sub-branches end.")]
    public float endPosition = 0.88f;

    [Range(0.1f, 1f)]
    [Tooltip("Sub-branch length as a fraction of the parent branch length.")]
    public float lengthRatio = 0.45f;

    [Range(5f, 88f)]
    [Tooltip("Minimum angle from the parent branch axis (degrees).")]
    public float minAngle = 30f;

    [Range(5f, 88f)]
    [Tooltip("Maximum angle from the parent branch axis (degrees).")]
    public float maxAngle = 60f;

    [Range(0f, 60f)]
    [Tooltip("How much sub-branches droop downward over their length (degrees).")]
    public float bendAmount = 15f;

    [Range(1, 10)]
    [Tooltip("Maximum number of rings along each sub-branch (scaled by sub-branch length).")]
    public int segments = 3;

    [Range(3, 6)]
    [Tooltip("Cross-section smoothness of sub-branches.")]
    public int sides = 4;

    [Tooltip("Sub-branch radius at each point (X=base → tip, Y=radius).")]
    public AnimationCurve radiusCurve = new AnimationCurve(
        new Keyframe(0f, 0.025f), new Keyframe(1f, 0.003f));
}

// ─── Sub-Branches Level 2 ────────────────────────────────────────────────────

[Serializable]
public class SubBranchLevel2Settings
{
    public bool enabled = false;

    [Range(0, 30)]
    [Tooltip("Maximum number of level-2 sub-branches per level-1 sub-branch.")]
    public int countPerBranch = 2;

    [Tooltip("Multiplier of max level-2 count by parent sub-branch length (X=0 shortest, 1 longest, Y=multiplier).")]
    public AnimationCurve countByParentLength = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(1f, 0.55f));

    [Range(0f, 1f)]
    [Tooltip("Normalized level-1 sub-branch position where level-2 sub-branches begin.")]
    public float startPosition = 0.35f;

    [Range(0f, 1f)]
    [Tooltip("Normalized level-1 sub-branch position where level-2 sub-branches end.")]
    public float endPosition = 0.9f;

    [Range(0.1f, 1f)]
    [Tooltip("Level-2 sub-branch length as a fraction of the parent level-1 sub-branch length.")]
    public float lengthRatio = 0.5f;

    [Range(5f, 88f)]
    [Tooltip("Minimum angle from the parent level-1 sub-branch axis (degrees).")]
    public float minAngle = 25f;

    [Range(5f, 88f)]
    [Tooltip("Maximum angle from the parent level-1 sub-branch axis (degrees).")]
    public float maxAngle = 55f;

    [Range(0f, 60f)]
    [Tooltip("How much level-2 sub-branches droop downward over their length (degrees).")]
    public float bendAmount = 12f;

    [Range(1, 8)]
    [Tooltip("Maximum number of rings along each level-2 sub-branch (scaled by length).")]
    public int segments = 2;

    [Range(3, 6)]
    [Tooltip("Cross-section smoothness of level-2 sub-branches.")]
    public int sides = 4;

    [Tooltip("Level-2 sub-branch radius at each point (X=base → tip, Y=radius).")]
    public AnimationCurve radiusCurve = new AnimationCurve(
        new Keyframe(0f, 0.012f), new Keyframe(1f, 0.002f));
}

// ─── Leaves ─────────────────────────────────────────────────────────────────

[Serializable]
public class LeafSettings
{
    public bool enabled = true;

    [Range(0, 100)]
    [Tooltip("Leaves placed at the tip of each main branch.")]
    public int countPerTipMainBranch = 8;

    [Range(0, 100)]
    [Tooltip("Leaves placed at the tip of each sub-branch.")]
    public int countPerTipSubBranch = 8;

    [Range(0, 100)]
    [Tooltip("Leaves placed at the tip of each level-2 sub-branch.")]
    public int countPerTipSubBranchLevel2 = 6;

    [Range(0f, 1f)]
    [Tooltip("Normalized position along the branch from which intermediate leaves start (1 = tip only).")]
    public float alongBranchStart = 0.55f;

    [Range(0, 100)]
    [Tooltip("Additional leaves distributed continuously along each main branch (independent from node/tip counts).")]
    public int countAlongMainBranch = 12;

    [Range(0, 100)]
    [Tooltip("Additional leaves distributed continuously along each sub-branch (independent from node/tip counts).")]
    public int countAlongSubBranch = 8;

    [Range(0, 100)]
    [Tooltip("Additional leaves distributed continuously along each level-2 sub-branch (independent from node/tip counts).")]
    public int countAlongSubBranchLevel2 = 6;

    [Tooltip("Distribution of additional leaves along main branch length (X=alongBranchStart..tip, Y=density).")]
    public AnimationCurve alongDistributionMainBranch = new AnimationCurve(
        new Keyframe(0f, 0.2f), new Keyframe(0.55f, 1f), new Keyframe(1f, 0.45f));

    [Tooltip("Distribution of additional leaves along sub-branch length (X=alongBranchStart..tip, Y=density).")]
    public AnimationCurve alongDistributionSubBranch = new AnimationCurve(
        new Keyframe(0f, 0.35f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0.6f));

    [Tooltip("Distribution of additional leaves along level-2 sub-branch length (X=alongBranchStart..tip, Y=density).")]
    public AnimationCurve alongDistributionSubBranchLevel2 = new AnimationCurve(
        new Keyframe(0f, 0.45f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0.7f));

    [Tooltip("Enforces minimal spacing between generated leaves.")]
    public bool enforceMinSeparation = true;

    [Min(0f)]
    [Tooltip("Minimal 3D distance between leaves on main branches.")]
    public float minSeparationMainBranch = 0.08f;

    [Min(0f)]
    [Tooltip("Minimal 3D distance between leaves on sub-branches.")]
    public float minSeparationSubBranch = 0.06f;

    [Min(0f)]
    [Tooltip("Minimal 3D distance between leaves on level-2 sub-branches.")]
    public float minSeparationSubBranchLevel2 = 0.05f;

    [Range(1, 30)]
    [Tooltip("How many placement attempts are made per leaf when minimal spacing is enabled.")]
    public int placementAttemptsPerLeaf = 8;

    [Range(0, 100)]
    [Tooltip("Additional leaves placed at each intermediate node on main branches past alongBranchStart.")]
    public int countPerNodeMainBranch = 2;

    [Range(0, 100)]
    [Tooltip("Additional leaves placed at each intermediate node on sub-branches past alongBranchStart.")]
    public int countPerNodeSubBranch = 2;

    [Range(0, 100)]
    [Tooltip("Additional leaves placed at each intermediate node on level-2 sub-branches past alongBranchStart.")]
    public int countPerNodeSubBranchLevel2 = 1;

    [Min(0.05f)]
    [Tooltip("Minimum leaf quad size.")]
    public float minSize = 0.15f;

    [Min(0.05f)]
    [Tooltip("Maximum leaf quad size.")]
    public float maxSize = 0.40f;

    [Tooltip("Leaf size multiplier by normalized tree height (X=0 bottom of trunk, X=1 top of trunk).")]
    public AnimationCurve sizeByTreeHeight = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(1f, 1f));

    [Tooltip("Leaf size multiplier by normalized distance from trunk surface (X=0 near trunk, X=1 far from trunk).")]
    public AnimationCurve sizeByDistanceFromTrunk = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(1f, 1f));

    [Range(0f, 180f)]
    [Tooltip("Half-angle of the cone within which leaves are randomly oriented.")]
    public float spreadAngle = 75f;

    [Range(0f, 1f)]
    [Tooltip("How strongly leaf direction aligns with trunk circumference outward direction (0 = random, 1 = fully outward from trunk).")]
    public float outwardDirectionCorrelation = 0.4f;

    [Range(0f, 1f)]
    [Tooltip("How much leaves sag downward (0 = no sag, 1 = fully hanging).")]
    public float droop = 0.25f;

    [Tooltip("Checks whether leaves are generally oriented upward after generation.")]
    public bool validateUpwardOrientation = true;

    [Range(-1f, 1f)]
    [Tooltip("A leaf is treated as upward when dot(leafDirection, worldUp) is above this threshold.")]
    public float upwardDotThreshold = 0f;

    [Range(0f, 1f)]
    [Tooltip("Minimum fraction of upward-oriented leaves required to pass the check.")]
    public float minUpwardLeafRatio = 0.6f;

    [Tooltip("Vertex color applied to leaf quads (use with a vertex-color shader).")]
    public Color color = new Color(0.18f, 0.55f, 0.12f, 1f);
}
