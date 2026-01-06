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

// Sampler for island SDF textures (trilinear filtering, clamp addressing)
// Unity will auto-bind this when using sampler_<TextureName> convention
SamplerState sampler_IslandSDF0;

StructuredBuffer<EnvironmentIsland> _Islands;
uint _IslandCount;

// [C4 FIX] Island validity flags (bit per island)
uint _IslandValidityMask;

// [DYN] Island dynamic flags - bit set = island uses dynamic RenderTexture
// Can be used for specialized filtering or debugging
uint _IslandDynamicMask;

// =============================================================================
// ISLAND SDF TEXTURE SAMPLING (internal)
// =============================================================================

// Sample island texture by index (manual dispatch - HLSL limitation)
float SampleIslandTexture(int islandIndex, float3 uvw)
{
    switch (islandIndex)
    {
        case 0: return _IslandSDF0.SampleLevel(sampler_IslandSDF0, uvw, 0);
        case 1: return _IslandSDF1.SampleLevel(sampler_IslandSDF0, uvw, 0);
        case 2: return _IslandSDF2.SampleLevel(sampler_IslandSDF0, uvw, 0);
        case 3: return _IslandSDF3.SampleLevel(sampler_IslandSDF0, uvw, 0);
        case 4: return _IslandSDF4.SampleLevel(sampler_IslandSDF0, uvw, 0);
        case 5: return _IslandSDF5.SampleLevel(sampler_IslandSDF0, uvw, 0);
        case 6: return _IslandSDF6.SampleLevel(sampler_IslandSDF0, uvw, 0);
        case 7: return _IslandSDF7.SampleLevel(sampler_IslandSDF0, uvw, 0);
        default: return 1.0;
    }
}

// =============================================================================
// ISLAND SDF SAMPLING (with rotation support)
// =============================================================================

// Sample a specific island's SDF at world position
// Transforms world->local, checks local bounds, samples local-space SDF
// Returns large positive if outside or invalid
float SampleIslandSDF(int islandIndex, float3 worldPos)
{
    // [C4 FIX] Check validity first
    if ((_IslandValidityMask & (1u << islandIndex)) == 0)
        return 1.0; // Invalid island = no occlusion

    EnvironmentIsland island = _Islands[islandIndex];

    // Additional validity check from struct
    if (island.isValid == 0)
        return 1.0;

    // Transform world position to island local space
    float3 localPos = WorldToIslandLocal(worldPos, island);

    // Check if inside local-space bounding box (centered at origin)
    if (!IsInsideLocalBox(localPos, island.localHalfExtents))
        return 1.0; // Outside = no occlusion

    // Convert local position to UVW (local box maps to [0,1]^3)
    // localPos in [-halfExtents, +halfExtents] -> uvw in [0, 1]
    float3 uvw = (localPos / island.localHalfExtents) * 0.5 + 0.5;

    // Clamp UVW to valid range (safety)
    uvw = saturate(uvw);

    return SampleIslandTexture(islandIndex, uvw);
}

// Find which island contains a point (or -1 if none)
// Uses rotated OBB test
int FindContainingIsland(float3 worldPos)
{
    for (uint i = 0; i < _IslandCount; i++)
    {
        // [C4 FIX] Skip invalid islands
        if ((_IslandValidityMask & (1u << i)) == 0)
            continue;

        EnvironmentIsland island = _Islands[i];
        if (island.isValid == 0)
            continue;

        // Transform to local space and check bounds
        float3 localPos = WorldToIslandLocal(worldPos, island);
        if (IsInsideLocalBox(localPos, island.localHalfExtents))
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
// Optimizations:
// - Early exit on exponential step growth (clearly in open space)
// - Early hit on step size below threshold
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

    // Early out for very short rays (target is very close)
    if (maxDist < 0.5)
        return true;

    float t = 0.5; // Start slightly away from origin
    float prevStepSize = 0.0;
    int largeStepCount = 0;

    for (int step = 0; step < MAX_RAY_STEPS && t < maxDist; step++)
    {
        float3 p = origin + dir * t;

        // Sample environment SDF at this point
        float envDist = SampleEnvironmentSDF(p, relevantIslandMask);

        // Hit obstacle - early termination
        if (envDist < OCCLUSION_THRESHOLD)
            return false;

        // Calculate step size
        float stepSize = max(envDist * 0.8, 0.2);

        // Early exit: exponential growth detection
        // If we've taken 3+ consecutive large steps (>5m), we're in open space
        if (stepSize > 5.0)
        {
            largeStepCount++;
            if (largeStepCount >= 3)
                return true; // Clearly in open space, path is clear
        }
        else
        {
            largeStepCount = 0;
        }

        // Early exit: if single step covers remaining distance
        if (t + stepSize >= maxDist)
            return true;

        t += stepSize;
        prevStepSize = stepSize;
    }

    return true; // Reached target (or step limit)
}

// Ray-OBB intersection test (oriented bounding box)
// Transforms ray to island local space, tests against axis-aligned box
bool RayIntersectsIsland(float3 rayOrigin, float3 rayDir, float rayLength, EnvironmentIsland island,
                         out float tEnter, out float tExit)
{
    tEnter = 0.0;
    tExit = rayLength;

    // Transform ray to island local space
    float3 localOrigin = WorldToIslandLocal(rayOrigin, island);
    float3 localDir = QuatRotate(island.rotationInverse, rayDir);

    // Avoid division by zero - sign(0) returns 0, so use explicit epsilon
    float3 safeDir;
    safeDir.x = abs(localDir.x) > 0.0001 ? localDir.x : 0.0001;
    safeDir.y = abs(localDir.y) > 0.0001 ? localDir.y : 0.0001;
    safeDir.z = abs(localDir.z) > 0.0001 ? localDir.z : 0.0001;
    float3 invDir = 1.0 / safeDir;

    // Ray-AABB intersection in local space (box centered at origin)
    float3 t1 = (-island.localHalfExtents - localOrigin) * invDir;
    float3 t2 = ( island.localHalfExtents - localOrigin) * invDir;

    float3 tMinV = min(t1, t2);
    float3 tMaxV = max(t1, t2);

    tEnter = max(max(tMinV.x, tMinV.y), tMinV.z);
    tExit = min(min(tMaxV.x, tMaxV.y), tMaxV.z);

    return tEnter <= tExit && tExit >= 0.0 && tEnter <= rayLength;
}

// Compute which islands a ray might pass through
// Uses oriented box intersection for rotated islands
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
        if (island.isValid == 0)
            continue;

        float tEnter, tExit;
        if (RayIntersectsIsland(origin, dir, rayLength, island, tEnter, tExit))
        {
            mask |= (1u << i);
        }
    }

    return mask;
}

#endif // ISLAND_SAMPLING_HLSL
