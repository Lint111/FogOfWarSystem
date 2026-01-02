// IslandSampling.hlsl
// Environment island SDF sampling and ray marching

#ifndef ISLAND_SAMPLING_HLSL
#define ISLAND_SAMPLING_HLSL

#include "Common.hlsl"

// =============================================================================
// ISLAND SDF TEXTURES
// =============================================================================

// Individual island SDF textures (bound separately)
Texture3D<float> _IslandSDF0;
Texture3D<float> _IslandSDF1;
Texture3D<float> _IslandSDF2;
Texture3D<float> _IslandSDF3;
Texture3D<float> _IslandSDF4;
Texture3D<float> _IslandSDF5;
Texture3D<float> _IslandSDF6;
Texture3D<float> _IslandSDF7;

SamplerState sampler_IslandSDF;

StructuredBuffer<EnvironmentIsland> _Islands;
uint _IslandCount;

// [C4 FIX] Island validity flags (bit per island)
uint _IslandValidityMask;

// =============================================================================
// ISLAND SDF SAMPLING
// =============================================================================

// Sample a specific island's SDF at world position
// [C4 FIX] Returns large positive if island texture is invalid
float SampleIslandSDF(int islandIndex, float3 worldPos)
{
    // [C4 FIX] Check validity first
    if ((_IslandValidityMask & (1u << islandIndex)) == 0)
        return 1.0; // Invalid island = no occlusion

    EnvironmentIsland island = _Islands[islandIndex];

    // Check if inside AABB
    if (!IsInsideAABB(worldPos, island.boundsMin, island.boundsMax))
        return 1.0; // Outside = no occlusion

    // Convert world pos to UVW
    float3 uvw = (worldPos - island.sdfOffset) * island.sdfScale;

    // Clamp UVW to valid range
    uvw = saturate(uvw);

    // Sample appropriate texture
    // (Manual dispatch because HLSL doesn't support texture arrays well)
    switch (islandIndex)
    {
        case 0: return _IslandSDF0.SampleLevel(sampler_IslandSDF, uvw, 0);
        case 1: return _IslandSDF1.SampleLevel(sampler_IslandSDF, uvw, 0);
        case 2: return _IslandSDF2.SampleLevel(sampler_IslandSDF, uvw, 0);
        case 3: return _IslandSDF3.SampleLevel(sampler_IslandSDF, uvw, 0);
        case 4: return _IslandSDF4.SampleLevel(sampler_IslandSDF, uvw, 0);
        case 5: return _IslandSDF5.SampleLevel(sampler_IslandSDF, uvw, 0);
        case 6: return _IslandSDF6.SampleLevel(sampler_IslandSDF, uvw, 0);
        case 7: return _IslandSDF7.SampleLevel(sampler_IslandSDF, uvw, 0);
        default: return 1.0;
    }
}

// Find which island contains a point (or -1 if none)
int FindContainingIsland(float3 worldPos)
{
    for (uint i = 0; i < _IslandCount; i++)
    {
        // [C4 FIX] Skip invalid islands
        if ((_IslandValidityMask & (1u << i)) == 0)
            continue;

        EnvironmentIsland island = _Islands[i];
        if (IsInsideAABB(worldPos, island.boundsMin, island.boundsMax))
            return (int)i;
    }
    return -1;
}

// Sample environment SDF at world position, checking all relevant islands
// Returns minimum distance to any obstacle
float SampleEnvironmentSDF(float3 worldPos, uint islandMask)
{
    float minDist = 1e10;

    // [C4 FIX] Apply validity mask
    islandMask &= _IslandValidityMask;

    for (uint i = 0; i < _IslandCount; i++)
    {
        if ((islandMask & (1u << i)) == 0)
            continue;

        float d = SampleIslandSDF((int)i, worldPos);
        minDist = min(minDist, d);
    }

    // If no islands sampled, return large positive (no occlusion)
    return (minDist < 1e9) ? minDist : 1.0;
}

// =============================================================================
// RAY MARCHING THROUGH ISLANDS
// =============================================================================

// Ray march from origin to target, checking for occlusion
// Returns true if path is clear
bool RayMarchThroughIslands(
    float3 origin,
    float3 target,
    uint relevantIslandMask,
    float targetRadius)
{
    // [C4 FIX] Apply validity mask
    relevantIslandMask &= _IslandValidityMask;

    // Early out if no valid islands
    if (relevantIslandMask == 0)
        return true;

    float3 dir = normalize(target - origin);
    float maxDist = length(target - origin) - targetRadius;

    float t = 0.5; // Start slightly away from origin

    for (int step = 0; step < MAX_RAY_STEPS && t < maxDist; step++)
    {
        float3 p = origin + dir * t;

        // Sample environment SDF at this point
        float envDist = SampleEnvironmentSDF(p, relevantIslandMask);

        // Hit obstacle
        if (envDist < OCCLUSION_THRESHOLD)
            return false;

        // Sphere trace step
        t += max(envDist * 0.8, 0.2);
    }

    return true; // Reached target
}

// Compute which islands a ray might pass through
uint ComputeRayIslandMask(float3 origin, float3 target)
{
    uint mask = 0;
    float3 dir = target - origin;
    float rayLength = length(dir);

    if (rayLength < 0.001)
        return 0;

    dir /= rayLength;

    for (uint i = 0; i < _IslandCount; i++)
    {
        // [C4 FIX] Skip invalid islands
        if ((_IslandValidityMask & (1u << i)) == 0)
            continue;

        EnvironmentIsland island = _Islands[i];

        // Ray-AABB intersection test
        float3 invDir = 1.0 / dir;
        float3 t1 = (island.boundsMin - origin) * invDir;
        float3 t2 = (island.boundsMax - origin) * invDir;

        float3 tMin = min(t1, t2);
        float3 tMax = max(t1, t2);

        float tEnter = max(max(tMin.x, tMin.y), tMin.z);
        float tExit = min(min(tMax.x, tMax.y), tMax.z);

        // Ray intersects AABB
        if (tEnter <= tExit && tExit >= 0.0 && tEnter <= rayLength)
        {
            mask |= (1u << i);
        }
    }

    return mask;
}

#endif // ISLAND_SAMPLING_HLSL
