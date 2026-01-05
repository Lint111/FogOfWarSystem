using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;
using Unity.Collections;
using FogOfWar.Visibility.Components;

namespace FogOfWar.Visibility.Debugging
{
    /// <summary>
    /// System that moves RoamingUnit entities toward random targets within bounds.
    /// Avoids island blockers using proper OBB (Oriented Bounding Box) collision.
    /// Used for stress testing the visibility system with moving units.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct RoamingUnitSystem : ISystem
    {
        private const float ARRIVAL_THRESHOLD = 1.0f;
        private const float MIN_WAIT_TIME = 0.5f;
        private const float MAX_WAIT_TIME = 2.0f;
        private const float BLOCKER_MARGIN = 0.3f;

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;

            // Collect island OBBs using SystemAPI query
            int islandCount = 0;
            foreach (var _ in SystemAPI.Query<RefRO<EnvironmentIslandDefinition>>())
                islandCount++;

            var islandOBBs = new NativeArray<OBB>(islandCount, Allocator.Temp);

            int idx = 0;
            foreach (var (island, ltw) in SystemAPI.Query<RefRO<EnvironmentIslandDefinition>, RefRO<LocalToWorld>>())
            {
                // Extract scale from LocalToWorld
                float3 scale = new float3(
                    math.length(ltw.ValueRO.Value.c0.xyz),
                    math.length(ltw.ValueRO.Value.c1.xyz),
                    math.length(ltw.ValueRO.Value.c2.xyz)
                );

                // Extract rotation (normalize to remove scale)
                float3x3 rotMatrix = new float3x3(
                    math.normalizesafe(ltw.ValueRO.Value.c0.xyz),
                    math.normalizesafe(ltw.ValueRO.Value.c1.xyz),
                    math.normalizesafe(ltw.ValueRO.Value.c2.xyz)
                );
                quaternion rotation = new quaternion(rotMatrix);

                islandOBBs[idx++] = new OBB
                {
                    Center = ltw.ValueRO.Position,
                    HalfExtents = island.ValueRO.LocalHalfExtents * scale + BLOCKER_MARGIN,
                    RotationInverse = math.inverse(rotation)
                };
            }

            foreach (var (ltw, roaming) in SystemAPI.Query<RefRW<LocalToWorld>, RefRW<RoamingUnit>>())
            {
                ref var r = ref roaming.ValueRW;

                // Handle wait timer
                if (r.WaitTimer > 0)
                {
                    r.WaitTimer -= dt;
                    continue;
                }

                float3 currentPos = ltw.ValueRO.Position;
                float3 targetPos = r.TargetPosition;
                targetPos.y = currentPos.y;

                float distToTarget = math.distance(currentPos, targetPos);

                // Check if arrived at target or target is inside a blocker
                bool needNewTarget = distToTarget < ARRIVAL_THRESHOLD;

                // Also check if target is inside any island (proper OBB test)
                if (!needNewTarget)
                {
                    for (int i = 0; i < islandCount; i++)
                    {
                        if (IsInsideOBB(targetPos, islandOBBs[i]))
                        {
                            needNewTarget = true;
                            break;
                        }
                    }
                }

                if (needNewTarget)
                {
                    // Pick new random target that's not inside a blocker
                    float3 newTarget = PickRandomTarget(ref r, currentPos, islandOBBs, islandCount);
                    r.TargetPosition = newTarget;

                    // Random wait before moving
                    uint waitSeed = r.RandomSeed;
                    waitSeed = WangHash(waitSeed);
                    r.WaitTimer = math.lerp(MIN_WAIT_TIME, MAX_WAIT_TIME, (waitSeed & 0xFFFFFF) / (float)0xFFFFFF);
                    r.RandomSeed = waitSeed;
                    continue;
                }

                // Calculate potential new position
                float3 direction = math.normalize(targetPos - currentPos);
                float moveAmount = math.min(r.MoveSpeed * dt, distToTarget);
                float3 newPos = currentPos + direction * moveAmount;

                // Check if new position would be inside any island (proper OBB test)
                bool blocked = false;
                for (int i = 0; i < islandCount; i++)
                {
                    if (IsInsideOBB(newPos, islandOBBs[i]))
                    {
                        blocked = true;
                        break;
                    }
                }

                if (blocked)
                {
                    // Pick a new target instead of moving into blocker
                    float3 newTarget = PickRandomTarget(ref r, currentPos, islandOBBs, islandCount);
                    r.TargetPosition = newTarget;
                    r.WaitTimer = 0.1f;
                    continue;
                }

                // Update transform
                ltw.ValueRW = new LocalToWorld
                {
                    Value = float4x4.TRS(newPos, quaternion.LookRotation(direction, math.up()), new float3(1, 1, 1))
                };
            }

            islandOBBs.Dispose();
        }

        private static float3 PickRandomTarget(ref RoamingUnit r, float3 currentPos,
            NativeArray<OBB> islandOBBs, int islandCount)
        {
            // Try up to 10 times to find a valid target
            for (int attempt = 0; attempt < 10; attempt++)
            {
                uint seed = r.RandomSeed;
                seed = WangHash(seed);
                uint sx = WangHash(seed);
                uint sy = WangHash(sx);
                uint sz = WangHash(sy);
                r.RandomSeed = sz;

                float3 newTarget = new float3(
                    math.lerp(r.BoundsMin.x, r.BoundsMax.x, (sx & 0xFFFFFF) / (float)0xFFFFFF),
                    currentPos.y,
                    math.lerp(r.BoundsMin.z, r.BoundsMax.z, (sz & 0xFFFFFF) / (float)0xFFFFFF)
                );

                // Check if target is valid (not inside any island OBB)
                bool valid = true;
                for (int i = 0; i < islandCount; i++)
                {
                    if (IsInsideOBB(newTarget, islandOBBs[i]))
                    {
                        valid = false;
                        break;
                    }
                }

                if (valid)
                    return newTarget;
            }

            // Fallback: if stuck, move away from nearest blocker
            if (islandCount > 0)
            {
                float3 escapeDir = FindEscapeDirection(currentPos, islandOBBs, islandCount);
                float3 escapeTarget = currentPos + escapeDir * 5.0f;

                // Clamp to bounds
                escapeTarget.x = math.clamp(escapeTarget.x, r.BoundsMin.x, r.BoundsMax.x);
                escapeTarget.z = math.clamp(escapeTarget.z, r.BoundsMin.z, r.BoundsMax.z);
                return escapeTarget;
            }

            return currentPos;
        }

        private static float3 FindEscapeDirection(float3 pos, NativeArray<OBB> islandOBBs, int islandCount)
        {
            // Find direction away from nearest blocker
            float nearestDist = float.MaxValue;
            float3 nearestCenter = pos;

            for (int i = 0; i < islandCount; i++)
            {
                float dist = math.distance(pos, islandOBBs[i].Center);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestCenter = islandOBBs[i].Center;
                }
            }

            float3 awayDir = pos - nearestCenter;
            awayDir.y = 0;
            float len = math.length(awayDir);
            if (len < 0.01f)
            {
                // Directly on center, pick random direction
                return new float3(1, 0, 0);
            }
            return awayDir / len;
        }

        /// <summary>
        /// Test if a point is inside an Oriented Bounding Box.
        /// Transforms point to local space, then tests against axis-aligned box.
        /// </summary>
        private static bool IsInsideOBB(float3 worldPoint, OBB obb)
        {
            // Transform point to OBB local space
            float3 localPoint = math.mul(obb.RotationInverse, worldPoint - obb.Center);

            // Test against axis-aligned box at origin
            return math.all(math.abs(localPoint) <= obb.HalfExtents);
        }

        private static uint WangHash(uint seed)
        {
            seed = (seed ^ 61) ^ (seed >> 16);
            seed *= 9;
            seed = seed ^ (seed >> 4);
            seed *= 0x27d4eb2d;
            seed = seed ^ (seed >> 15);
            return seed;
        }

        /// <summary>
        /// Oriented Bounding Box for precise collision detection with rotated islands.
        /// </summary>
        private struct OBB
        {
            public float3 Center;
            public float3 HalfExtents;
            public quaternion RotationInverse; // To transform world -> local
        }
    }
}
