using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace FogOfWar.Visibility.GPU
{
    /// <summary>
    /// GPU data structure constants.
    /// </summary>
    public static class GPUConstants
    {
        public const int MAX_GROUPS = 8;
        public const int MAX_ISLANDS = 16;
        public const int MAX_RAY_STEPS = 48;
        public const float VISIBILITY_THRESHOLD = 0.1f;
        public const float OCCLUSION_THRESHOLD = 0.05f;
    }

    /// <summary>
    /// Per-group metadata. Stored once per active group.
    /// Size: 48 bytes (aligned)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VisionGroupDataGPU
    {
        public int unitStartIndex;          // Index into unit contributions buffer
        public int unitCount;               // Number of units in this group
        public float3 groupCenter;          // Centroid of all units
        public float groupBoundingRadius;   // Radius encapsulating all units
        public float maxViewDistance;       // Furthest any unit can see
        public byte groupId;                // 0-7
        public byte visibilityMask;         // Bitmask: which groups can this see
        public byte flags;                  // IsActive, IsPlayer, etc.
        public byte padding;
        public float4 reserved;             // Padding to 48 bytes
    }

    /// <summary>
    /// Per-unit vision contribution. Each unit that can see adds one entry.
    /// Size: 48 bytes (aligned from 40)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UnitSDFContributionGPU
    {
        public float3 position;             // World position
        public float primaryRadius;         // Main vision sphere radius
        public float3 forwardDirection;     // Normalized, for directional vision
        public float secondaryParam;        // Cone angle (radians) or second sphere radius
        public byte visionType;             // 0=sphere, 1=cone, 2=dual-sphere
        public byte flags;                  // HasDirectionalVision, IsElevated, etc.
        public ushort ownerGroupId;         // Redundant but useful for debugging
        public float4 padding;              // Pad to 48 bytes
    }

    /// <summary>
    /// Entity that can be seen by other groups.
    /// Size: 32 bytes (aligned from 24)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SeeableEntityDataGPU
    {
        public int entityId;                // Entity.Index for lookup
        public float3 position;             // World position
        public float boundingRadius;        // For partial visibility calculation
        public byte ownerGroupId;           // Which group owns this
        public byte seeableByMask;          // Which groups are allowed to see this
        public ushort flags;                // IsUnit, IsStructure, IsCritical, etc.
        public float4 padding;              // Pad to 32 bytes
    }

    /// <summary>
    /// Environment island definition.
    /// Size: 64 bytes
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EnvironmentIslandGPU
    {
        public float3 boundsMin;            // AABB minimum
        public float padding1;
        public float3 boundsMax;            // AABB maximum
        public float padding2;
        public float3 sdfOffset;            // World offset for SDF sampling
        public float padding3;
        public float3 sdfScale;             // Scale factor (bounds size / texture size)
        public int textureIndex;            // Index into island SDF texture array
    }

    /// <summary>
    /// Intermediate candidate for ray march confirmation.
    /// [C1 FIX]: Added seeableIndex to avoid O(n) search in ray march shader.
    /// Size: 32 bytes (aligned from 20)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VisibilityCandidateGPU
    {
        public int entityId;                // Who might be visible
        public int seeableIndex;            // [C1 FIX] Direct index into SeeableEntities buffer
        public int viewerGroupId;           // Which group is looking
        public int nearestUnitIndex;        // Which unit from viewer group is closest
        public float distance;              // Distance from nearest unit
        public float3 padding;              // Pad to 32 bytes
    }

    /// <summary>
    /// Final visibility output entry.
    /// Size: 16 bytes
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VisibilityEntryGPU
    {
        public int entityId;                // Who is visible
        public int seenByUnitIndex;         // Which of our units sees this
        public float distance;              // Distance from that unit
        public byte visibilityLevel;        // 0=edge, 1=partial, 2=full
        public byte flags;
        public ushort padding;
    }
}
