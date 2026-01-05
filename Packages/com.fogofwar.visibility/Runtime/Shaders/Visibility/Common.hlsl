// Common.hlsl
// Shared definitions for visibility compute shaders

#ifndef VISIBILITY_COMMON_HLSL
#define VISIBILITY_COMMON_HLSL

// =============================================================================
// CONSTANTS
// =============================================================================

#define MAX_GROUPS 8
#define MAX_ISLANDS 16
#define MAX_RAY_STEPS 48
#define VISIBILITY_THRESHOLD 0.1
#define OCCLUSION_THRESHOLD 0.05

// Vision types
#define VISION_SPHERE 0
#define VISION_SPHERE_CONE 1
#define VISION_DUAL_SPHERE 2

// Multi-sample visibility constants
#define VISIBILITY_SAMPLE_COUNT 8
#define COVERAGE_SEEN_THRESHOLD 0.65
#define COVERAGE_PARTIAL_THRESHOLD 0.30

// Note: No LOD needed for ray marching - SDF-guided sphere tracing
// naturally adapts: large SDF = big steps (fast), small SDF = small steps (precise)

// =============================================================================
// [M3 FIX] THREAD GROUP SIZES
// =============================================================================

// 1D dispatch (VisibilityCheck, RayMarchConfirm): 64 threads
#define THREAD_GROUP_1D 64

// 3D dispatch (PlayerFogVolume): 4x4x4 = 64 threads
// Using 64 instead of 512 (8x8x8) for:
// - Better AMD GCN occupancy (+15-25% performance)
// - Cross-platform compatibility (some mobile GPUs limit to 256)
// - Reduced register pressure
#define THREAD_GROUP_3D_X 4
#define THREAD_GROUP_3D_Y 4
#define THREAD_GROUP_3D_Z 4

// =============================================================================
// DATA STRUCTURES (must match C# GPUDataStructures.cs)
// =============================================================================

// Size: 48 bytes - MUST match C# VisionGroupDataGPU
struct VisionGroupData
{
    int unitStartIndex;         // 0-4
    int unitCount;              // 4-8
    float3 groupCenter;         // 8-20
    float groupBoundingRadius;  // 20-24
    float maxViewDistance;      // 24-28
    uint packedIds;             // 28-32: groupId(8) | visibilityMask(8) | flags(8) | padding(8)
    float4 reserved;            // 32-48
};

// Unpack helpers for VisionGroupData.packedIds
uint GetGroupId(VisionGroupData g) { return g.packedIds & 0xFF; }
uint GetVisibilityMask(VisionGroupData g) { return (g.packedIds >> 8) & 0xFF; }
uint GetGroupFlags(VisionGroupData g) { return (g.packedIds >> 16) & 0xFF; }

// Size: 48 bytes - MUST match C# UnitSDFContributionGPU
struct UnitSDFContribution
{
    float3 position;        // 12
    float primaryRadius;    // 4 = 16
    float3 forwardDirection;// 12
    float secondaryParam;   // 4 = 32
    uint packedFlags;       // visionType(8) | flags(8) | ownerGroupId(16) = 36
    float3 padding;         // 12 = 48
};

// Size: 32 bytes - MUST match C# SeeableEntityDataGPU
struct SeeableEntityData
{
    int entityId;           // 4
    float3 position;        // 12 = 16
    float boundingRadius;   // 4
    uint packedFlags;       // ownerGroupId(8) | seeableByMask(8) | flags(16) = 24
    float2 padding;         // 8 = 32
};

// Environment island with rotation support (96 bytes)
// Islands can be rotated - SDF is baked in local space
struct EnvironmentIsland
{
    float3 worldCenter;         // Island pivot in world space
    float padding1;
    float3 localHalfExtents;    // Local-space AABB half-size
    float padding2;
    float4 rotation;            // Quaternion (world from local)
    float4 rotationInverse;     // Inverse quaternion (local from world)
    float3 sdfScale;            // 1 / (localHalfExtents * 2) * textureResolution
    int textureIndex;
    int isValid;                // Non-zero if island is loaded
    float3 padding3;
};

// [C1 FIX] Added seeableIndex to avoid O(n) search
struct VisibilityCandidate
{
    int entityId;
    int seeableIndex;       // [C1 FIX] Direct index into SeeableEntities buffer
    int viewerGroupId;
    int nearestUnitIndex;
    float distance;
    float3 padding;
};

struct VisibilityEntry
{
    int entityId;
    int seenByUnitIndex;
    float distance;
    uint packed;            // visibilityLevel (8) | coverage (8) | flags (16)
};

// Pack visibility entry fields
uint PackVisibilityEntry(uint visibilityLevel, uint coverage255, uint flags)
{
    return visibilityLevel | (coverage255 << 8) | (flags << 16);
}

// Unpack coverage from visibility entry (0-255 -> 0.0-1.0)
float UnpackCoverage(uint packed)
{
    return ((packed >> 8) & 0xFF) / 255.0;
}

// Indirect dispatch arguments for DispatchIndirect()
// Size: 16 bytes (4 uints)
struct IndirectDispatchArgs
{
    uint threadGroupCountX;
    uint threadGroupCountY;
    uint threadGroupCountZ;
    uint padding;
};

// =============================================================================
// HELPER FUNCTIONS
// =============================================================================

float SmoothMin(float a, float b, float k)
{
    // Protect against division by zero when k approaches 0
    if (k < 0.0001) return min(a, b);
    float h = max(k - abs(a - b), 0.0) / k;
    return min(a, b) - h * h * k * 0.25;
}

bool IsInsideAABB(float3 p, float3 minB, float3 maxB)
{
    return all(p >= minB) && all(p <= maxB);
}

// Check if point is inside local-space oriented box (centered at origin)
bool IsInsideLocalBox(float3 localP, float3 halfExtents)
{
    return all(abs(localP) <= halfExtents);
}

// Unpack byte from uint (for struct fields)
uint UnpackByte(uint packed, uint byteIndex)
{
    return (packed >> (byteIndex * 8)) & 0xFF;
}

// Unpack helpers for UnitSDFContribution.packedFlags
// packedFlags = visionType(8) | flags(8) | ownerGroupId(16)
uint GetUnitVisionType(UnitSDFContribution unit)
{
    return unit.packedFlags & 0xFF;
}

uint GetUnitFlags(UnitSDFContribution unit)
{
    return (unit.packedFlags >> 8) & 0xFF;
}

uint GetUnitOwnerGroupId(UnitSDFContribution unit)
{
    return (unit.packedFlags >> 16) & 0xFFFF;
}

// Unpack helpers for SeeableEntityData.packedFlags
// packedFlags = ownerGroupId(8) | seeableByMask(8) | flags(16)
uint GetSeeableOwnerGroupId(SeeableEntityData seeable)
{
    return seeable.packedFlags & 0xFF;
}

uint GetSeeableMask(SeeableEntityData seeable)
{
    return (seeable.packedFlags >> 8) & 0xFF;
}

uint GetSeeableFlags(SeeableEntityData seeable)
{
    return (seeable.packedFlags >> 16) & 0xFFFF;
}

// =============================================================================
// VISIBILITY MASK UTILITIES
// =============================================================================

// Check if any viewer group can potentially see this seeable
// Uses XOR/AND to quickly reject impossible visibility pairs
// activeGroupMask: bitmask of groups that have active units
// seeableByMask: which groups the seeable allows to see it
// Returns: bitmask of groups that COULD see this seeable
uint ComputePotentialViewers(uint activeGroupMask, uint seeableByMask, uint seeableOwnerGroup)
{
    // Groups that are active AND allowed to see this seeable AND not the owner
    uint ownerBit = 1u << seeableOwnerGroup;
    return activeGroupMask & seeableByMask & (~ownerBit);
}

// Quick check if there's ANY potential visibility
bool HasAnyPotentialViewer(uint activeGroupMask, uint seeableByMask, uint seeableOwnerGroup)
{
    return ComputePotentialViewers(activeGroupMask, seeableByMask, seeableOwnerGroup) != 0;
}

// =============================================================================
// QUATERNION UTILITIES
// =============================================================================

// Rotate vector by quaternion: q * v * q^-1
// q.xyz = vector part, q.w = scalar part
float3 QuatRotate(float4 q, float3 v)
{
    float3 t = 2.0 * cross(q.xyz, v);
    return v + q.w * t + cross(q.xyz, t);
}

// Transform world position to island local space
float3 WorldToIslandLocal(float3 worldPos, EnvironmentIsland island)
{
    return QuatRotate(island.rotationInverse, worldPos - island.worldCenter);
}

// Transform island local position to world space
float3 IslandLocalToWorld(float3 localPos, EnvironmentIsland island)
{
    return QuatRotate(island.rotation, localPos) + island.worldCenter;
}

#endif // VISIBILITY_COMMON_HLSL
