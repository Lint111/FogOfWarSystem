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
    /// Layout matches HLSL: uint packed = visibilityLevel (8) | flags (8) | padding (16)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VisibilityEntryGPU
    {
        public int entityId;                // Who is visible
        public int seenByUnitIndex;         // Which of our units sees this
        public float distance;              // Distance from that unit
        private uint packed;                // visibilityLevel (8) | flags (8) | padding (16)

        /// <summary>Visibility level: 0=edge, 1=partial, 2=full.</summary>
        public byte visibilityLevel
        {
            get => (byte)(packed & 0xFF);
            set => packed = (packed & 0xFFFFFF00) | value;
        }

        /// <summary>Additional flags.</summary>
        public byte flags
        {
            get => (byte)((packed >> 8) & 0xFF);
            set => packed = (packed & 0xFFFF00FF) | ((uint)value << 8);
        }

        /// <summary>Sets the packed field directly (for GPU readback).</summary>
        public void SetPacked(uint value) => packed = value;

        /// <summary>Gets the raw packed field.</summary>
        public uint GetPacked() => packed;
    }
}
