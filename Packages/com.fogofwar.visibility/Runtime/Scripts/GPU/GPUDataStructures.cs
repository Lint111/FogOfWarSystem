using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace FogOfWar.Visibility.GPU
{
    /// <summary>
    /// GPU data structure constants.
    /// IMPORTANT: These values MUST match the #defines in Common.hlsl.
    /// Run "GPU Constants Validation" test to verify synchronization.
    /// </summary>
    public static class GPUConstants
    {
        // Core limits (must match Common.hlsl)
        public const int MAX_GROUPS = 8;
        public const int MAX_ISLANDS = 16;
        public const int MAX_RAY_STEPS = 48;
        public const float VISIBILITY_THRESHOLD = 0.1f;
        public const float OCCLUSION_THRESHOLD = 0.05f;

        // Thread group sizes (must match Common.hlsl)
        public const int THREAD_GROUP_1D = 64;
        public const int THREAD_GROUP_3D_X = 4;
        public const int THREAD_GROUP_3D_Y = 4;
        public const int THREAD_GROUP_3D_Z = 4;

        // Vision types (must match Common.hlsl)
        public const int VISION_SPHERE = 0;
        public const int VISION_SPHERE_CONE = 1;
        public const int VISION_DUAL_SPHERE = 2;
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
    /// Size: 48 bytes - MUST match HLSL UnitSDFContribution
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UnitSDFContributionGPU
    {
        public float3 position;             // 12 bytes - World position
        public float primaryRadius;         // 4 bytes = 16 - Main vision sphere radius
        public float3 forwardDirection;     // 12 bytes - Normalized, for directional vision
        public float secondaryParam;        // 4 bytes = 32 - Cone angle (radians) or second sphere radius
        public uint packedFlags;            // 4 bytes = 36 - visionType(8) | flags(8) | ownerGroupId(16)
        public float3 padding;              // 12 bytes = 48

        /// <summary>Pack fields for GPU upload.</summary>
        public static uint PackFlags(byte visionType, byte flags, ushort ownerGroupId)
        {
            return (uint)visionType | ((uint)flags << 8) | ((uint)ownerGroupId << 16);
        }
    }

    /// <summary>
    /// Entity that can be seen by other groups.
    /// Size: 32 bytes - MUST match HLSL SeeableEntityData
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SeeableEntityDataGPU
    {
        public int entityId;                // 4 bytes - Entity.Index for lookup
        public float3 position;             // 12 bytes = 16 - World position
        public float boundingRadius;        // 4 bytes - For partial visibility calculation
        public uint packedFlags;            // 4 bytes = 24 - ownerGroupId(8) | seeableByMask(8) | flags(16)
        public float2 padding;              // 8 bytes = 32

        /// <summary>Pack fields for GPU upload.</summary>
        public static uint PackFlags(byte ownerGroupId, byte seeableByMask, ushort flags)
        {
            return (uint)ownerGroupId | ((uint)seeableByMask << 8) | ((uint)flags << 16);
        }
    }

    /// <summary>
    /// Environment island definition with rotation support.
    /// Islands can be rotated arbitrarily - SDF is baked in local space,
    /// transform converts world→local for sampling.
    /// Size: 96 bytes (aligned)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct EnvironmentIslandGPU
    {
        public float3 worldCenter;          // Island pivot in world space
        public float padding1;
        public float3 localHalfExtents;     // Local-space AABB half-size (from center)
        public float padding2;
        public float4 rotation;             // Quaternion (world from local)
        public float4 rotationInverse;      // Inverse quaternion (local from world)
        public float3 sdfScale;             // localHalfExtents * 2 / textureResolution
        public int textureIndex;            // Index into island SDF texture array
        public int isValid;                 // Non-zero if island is loaded
        public float3 padding3;             // Pad to 96 bytes

        /// <summary>
        /// Creates an island GPU struct from transform data.
        /// </summary>
        public static EnvironmentIslandGPU Create(
            float3 worldCenter,
            float3 localHalfExtents,
            quaternion rotation,
            float3 textureResolution,
            int textureIndex)
        {
            var rotInverse = math.inverse(rotation);
            return new EnvironmentIslandGPU
            {
                worldCenter = worldCenter,
                localHalfExtents = localHalfExtents,
                rotation = rotation.value,
                rotationInverse = rotInverse.value,
                sdfScale = 1.0f / (localHalfExtents * 2.0f) * textureResolution,
                textureIndex = textureIndex,
                isValid = 1,
                padding1 = 0, padding2 = 0, padding3 = float3.zero
            };
        }
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
    /// Indirect dispatch arguments for ComputeShader.DispatchIndirect().
    /// Size: 16 bytes (4 uints)
    /// Must be created with ComputeBufferType.IndirectArguments.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct IndirectDispatchArgsGPU
    {
        public uint threadGroupCountX;
        public uint threadGroupCountY;
        public uint threadGroupCountZ;
        public uint padding;

        public static IndirectDispatchArgsGPU Create(uint x, uint y = 1, uint z = 1)
        {
            return new IndirectDispatchArgsGPU
            {
                threadGroupCountX = x,
                threadGroupCountY = y,
                threadGroupCountZ = z,
                padding = 0
            };
        }

        /// <summary>
        /// Calculates thread groups needed for a 1D dispatch.
        /// </summary>
        public static IndirectDispatchArgsGPU For1D(uint itemCount, uint threadGroupSize = 64)
        {
            uint groups = (itemCount + threadGroupSize - 1) / threadGroupSize;
            return Create(groups > 0 ? groups : 1);
        }
    }

    /// <summary>
    /// Final visibility output entry.
    /// Size: 16 bytes
    /// Layout matches HLSL: uint packed = visibilityLevel (8) | coverage (8) | flags (16)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VisibilityEntryGPU
    {
        public int entityId;                // Who is visible
        public int seenByUnitIndex;         // Which of our units sees this
        public float distance;              // Distance from that unit
        private uint packed;                // visibilityLevel (8) | coverage (8) | flags (16)

        /// <summary>Visibility level: 0=edge, 1=partial, 2=full (based on distance).</summary>
        public byte VisibilityLevel
        {
            get => (byte)(packed & 0xFF);
            set => packed = (packed & 0xFFFFFF00) | value;
        }

        /// <summary>Coverage percentage: 0-255 maps to 0%-100%. Used for smooth visual transitions.</summary>
        public byte Coverage
        {
            get => (byte)((packed >> 8) & 0xFF);
            set => packed = (packed & 0xFFFF00FF) | ((uint)value << 8);
        }

        /// <summary>Coverage as normalized float (0.0 - 1.0).</summary>
        public float CoverageNormalized => Coverage / 255f;

        /// <summary>Additional flags.</summary>
        public ushort Flags
        {
            get => (ushort)((packed >> 16) & 0xFFFF);
            set => packed = (packed & 0x0000FFFF) | ((uint)value << 16);
        }

        /// <summary>Sets the packed field directly (for GPU readback).</summary>
        public void SetPacked(uint value) => packed = value;

        /// <summary>Gets the raw packed field.</summary>
        public uint GetPacked() => packed;

        /// <summary>Pack visibility level and coverage into packed field.</summary>
        public static uint Pack(byte visibilityLevel, byte coverage, ushort flags = 0)
        {
            return (uint)visibilityLevel | ((uint)coverage << 8) | ((uint)flags << 16);
        }
    }

    /// <summary>
    /// Constants for visibility coverage thresholds.
    /// </summary>
    public static class VisibilityCoverage
    {
        /// <summary>Coverage threshold for gameplay "seen" state (65%).</summary>
        public const float SEEN_THRESHOLD = 0.65f;

        /// <summary>Coverage threshold for partial visibility/silhouette (30%).</summary>
        public const float PARTIAL_THRESHOLD = 0.30f;

        /// <summary>Number of sample points for multi-sample visibility check.</summary>
        public const int SAMPLE_COUNT = 8;
    }
}
