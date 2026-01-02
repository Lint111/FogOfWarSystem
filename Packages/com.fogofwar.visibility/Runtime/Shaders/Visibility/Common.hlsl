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

struct VisionGroupData
{
    int unitStartIndex;
    int unitCount;
    float3 groupCenter;
    float groupBoundingRadius;
    float maxViewDistance;
    uint groupId;           // byte packed as uint
    uint visibilityMask;    // byte packed as uint
    uint flags;             // byte packed as uint
    uint padding;
    float4 reserved;
};

struct UnitSDFContribution
{
    float3 position;
    float primaryRadius;
    float3 forwardDirection;
    float secondaryParam;
    uint visionType;        // byte packed as uint
    uint flags;             // byte packed as uint
    uint ownerGroupId;      // ushort packed as uint
    uint padding2;
    float4 padding;
};

struct SeeableEntityData
{
    int entityId;
    float3 position;
    float boundingRadius;
    uint ownerGroupId;      // byte packed as uint
    uint seeableByMask;     // byte packed as uint
    uint flags;             // ushort packed as uint
    uint padding;
    float4 padding2;
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
    uint packed;            // visibilityLevel (8) | flags (8) | padding (16)
};

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
