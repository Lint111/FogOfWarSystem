// SDFEvaluation.hlsl
// SDF primitive and unit vision evaluation functions

#ifndef SDF_EVALUATION_HLSL
#define SDF_EVALUATION_HLSL

#include "Common.hlsl"

// =============================================================================
// [M1 FIX] SHARED MEMORY FOR COOPERATIVE UNIT LOADING
// =============================================================================

// Maximum units that can be loaded into shared memory per batch
// 48 bytes per unit * 128 units = 6KB (well within 16-48KB limit)
#define SHARED_UNIT_BATCH_SIZE 128

// Shared memory for cooperative unit loading
// Declared in compute shader that includes this file
// groupshared UnitSDFContribution g_SharedUnits[SHARED_UNIT_BATCH_SIZE];

// =============================================================================
// PRIMITIVE SDF FUNCTIONS
// =============================================================================

float sdSphere(float3 p, float r)
{
    return length(p) - r;
}

float sdCone(float3 p, float3 dir, float angle, float maxDist)
{
    float along = dot(p, dir);

    // Behind or too far
    if (along <= 0.0 || along > maxDist)
        return 1e10;

    float radius = along * tan(angle);
    float perpDist = length(p - dir * along);

    return perpDist - radius;
}

// =============================================================================
// UNIT VISION EVALUATION
// =============================================================================

float EvaluateUnitVision(float3 targetPos, UnitSDFContribution unit)
{
    float3 toTarget = targetPos - unit.position;
    float dist = length(toTarget);

    // Primary sphere (always present)
    float sdf = dist - unit.primaryRadius;

    uint visionType = GetUnitVisionType(unit);

    // Early out for pure sphere
    if (visionType == VISION_SPHERE)
        return sdf;

    // Cone extension
    if (visionType == VISION_SPHERE_CONE && unit.secondaryParam > 0.0)
    {
        float coneSDF = sdCone(toTarget, unit.forwardDirection,
                               unit.secondaryParam, unit.primaryRadius * 2.0);
        sdf = min(sdf, coneSDF);
    }
    // Dual sphere
    else if (visionType == VISION_DUAL_SPHERE && unit.secondaryParam > 0.0)
    {
        float3 secondPos = unit.position + unit.forwardDirection * unit.primaryRadius;
        float secondSDF = length(targetPos - secondPos) - unit.secondaryParam;
        sdf = SmoothMin(sdf, secondSDF, 0.5);
    }

    return sdf;
}

// =============================================================================
// GROUP VISION EVALUATION
// =============================================================================

// Evaluate combined vision SDF for a group at a target position
// Returns: SDF value, nearest unit index, distance to nearest unit
float EvaluateGroupVision(
    float3 targetPos,
    VisionGroupData group,
    StructuredBuffer<UnitSDFContribution> units,
    out int nearestUnit,
    out float nearestDist)
{
    float bestSDF = 1e10;
    nearestUnit = -1;
    nearestDist = 1e10;

    for (int i = 0; i < group.unitCount; i++)
    {
        int unitIdx = group.unitStartIndex + i;
        UnitSDFContribution unit = units[unitIdx];

        // Quick distance cull
        float dist = length(targetPos - unit.position);
        if (dist > group.maxViewDistance + unit.primaryRadius)
            continue;

        float sdf = EvaluateUnitVision(targetPos, unit);

        if (sdf < bestSDF)
        {
            bestSDF = sdf;
        }

        if (dist < nearestDist)
        {
            nearestDist = dist;
            nearestUnit = unitIdx;
        }
    }

    return bestSDF;
}

// Convert SDF value to visibility (0-1)
float SDFToVisibility(float sdf, float targetRadius)
{
    // Negative SDF = inside vision volume
    // Map [-targetRadius, +targetRadius] to [1, 0]
    return saturate(-sdf / max(targetRadius, 0.1) + 0.5);
}

// =============================================================================
// [M1 FIX] SHARED MEMORY OPTIMIZED GROUP EVALUATION
// =============================================================================

// Cooperative loading helper - call from all threads in group
// Returns number of units loaded into shared memory this batch
uint CooperativeLoadUnits(
    uint threadIdInGroup,
    uint groupThreadCount,
    uint batchStart,
    uint totalUnits,
    uint unitBufferStart,
    StructuredBuffer<UnitSDFContribution> units,
    inout UnitSDFContribution sharedUnits[SHARED_UNIT_BATCH_SIZE])
{
    uint batchSize = min(SHARED_UNIT_BATCH_SIZE, totalUnits - batchStart);

    // Each thread loads ceil(batchSize / groupThreadCount) units
    uint unitsPerThread = (batchSize + groupThreadCount - 1) / groupThreadCount;

    for (uint i = 0; i < unitsPerThread; i++)
    {
        uint localIdx = threadIdInGroup + i * groupThreadCount;
        if (localIdx < batchSize)
        {
            uint globalIdx = unitBufferStart + batchStart + localIdx;
            sharedUnits[localIdx] = units[globalIdx];
        }
    }

    return batchSize;
}

// Evaluate group vision using shared memory batching
// Requires: g_SharedUnits declared as groupshared in compute shader
// Requires: GroupMemoryBarrierWithGroupSync() called after each batch load
float EvaluateGroupVisionShared(
    float3 targetPos,
    VisionGroupData group,
    uint batchSize,
    UnitSDFContribution sharedUnits[SHARED_UNIT_BATCH_SIZE],
    inout int nearestUnit,
    inout float nearestDist,
    uint batchStartIndex)  // Offset to convert shared index to global
{
    float bestSDF = 1e10;

    for (uint i = 0; i < batchSize; i++)
    {
        UnitSDFContribution unit = sharedUnits[i];

        // Quick distance cull
        float dist = length(targetPos - unit.position);
        if (dist > group.maxViewDistance + unit.primaryRadius)
            continue;

        float sdf = EvaluateUnitVision(targetPos, unit);

        if (sdf < bestSDF)
        {
            bestSDF = sdf;
        }

        if (dist < nearestDist)
        {
            nearestDist = dist;
            nearestUnit = (int)(batchStartIndex + i);
        }
    }

    return bestSDF;
}

#endif // SDF_EVALUATION_HLSL
