// SDFEvaluation.hlsl
// SDF primitive and unit vision evaluation functions

#ifndef SDF_EVALUATION_HLSL
#define SDF_EVALUATION_HLSL

#include "Common.hlsl"

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

    uint visionType = unit.visionType & 0xFF;

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

#endif // SDF_EVALUATION_HLSL
