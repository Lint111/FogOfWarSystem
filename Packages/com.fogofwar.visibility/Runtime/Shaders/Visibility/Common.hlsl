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

struct EnvironmentIsland
{
    float3 boundsMin;
    float padding1;
    float3 boundsMax;
    float padding2;
    float3 sdfOffset;
    float padding3;
    float3 sdfScale;
    int textureIndex;
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

// Unpack byte from uint (for struct fields)
uint UnpackByte(uint packed, uint byteIndex)
{
    return (packed >> (byteIndex * 8)) & 0xFF;
}

#endif // VISIBILITY_COMMON_HLSL
