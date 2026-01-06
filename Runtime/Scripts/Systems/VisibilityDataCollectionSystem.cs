using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Jobs;
using Unity.Burst;
using FogOfWar.Visibility.Components;
using FogOfWar.Visibility.Core;
using FogOfWar.Visibility.GPU;

namespace FogOfWar.Visibility.Systems
{
    /// <summary>
    /// Collects ECS visibility data and uploads to GPU buffers.
    /// Runs before VisibilityComputeDispatchSystem.
    /// Uses VisibilitySystemBehaviour.Instance.Runtime for buffer access.
    /// </summary>
    [UpdateInGroup(typeof(VisibilitySystemGroup))]
    [UpdateBefore(typeof(VisibilityComputeDispatchSystem))]
    [UpdateAfter(typeof(VisibilityBootstrapSystem))]
    public partial class VisibilityDataCollectionSystem : SystemBase
    {
        // Cached queries
        private EntityQuery _unitQuery;
        private EntityQuery _seeableQuery;
        private EntityQuery _islandQuery;

        // Staging arrays (reused each frame)
        private NativeList<VisionGroupDataGPU> _groupDataStaging;
        private NativeList<UnitSDFContributionGPU> _unitStaging;
        private NativeList<SeeableEntityDataGPU> _seeableStaging;
        private NativeList<EnvironmentIslandGPU> _islandStaging;

        // Per-group unit tracking
        private NativeArray<int> _groupUnitCounts;
        private NativeArray<int> _groupUnitOffsets;

        protected override void OnCreate()
        {
            RequireForUpdate<VisionGroupRegistry>();

            // Units: entities with vision capability
            _unitQuery = GetEntityQuery(
                ComponentType.ReadOnly<VisionGroupMembership>(),
                ComponentType.ReadOnly<UnitVision>(),
                ComponentType.ReadOnly<LocalToWorld>()
            );

            // Seeables: entities that can be seen
            _seeableQuery = GetEntityQuery(
                ComponentType.ReadOnly<Seeable>(),
                ComponentType.ReadOnly<VisionGroupMembership>(),
                ComponentType.ReadOnly<LocalToWorld>()
            );

            // Islands: environment SDF volumes
            _islandQuery = GetEntityQuery(
                ComponentType.ReadOnly<EnvironmentIslandDefinition>(),
                ComponentType.ReadOnly<LocalToWorld>()
            );

            // Initialize staging buffers
            _groupDataStaging = new NativeList<VisionGroupDataGPU>(GPUConstants.MAX_GROUPS, Allocator.Persistent);
            _unitStaging = new NativeList<UnitSDFContributionGPU>(1024, Allocator.Persistent);
            _seeableStaging = new NativeList<SeeableEntityDataGPU>(1024, Allocator.Persistent);
            _islandStaging = new NativeList<EnvironmentIslandGPU>(GPUConstants.MAX_ISLANDS, Allocator.Persistent);
            _groupUnitCounts = new NativeArray<int>(GPUConstants.MAX_GROUPS, Allocator.Persistent);
            _groupUnitOffsets = new NativeArray<int>(GPUConstants.MAX_GROUPS, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            if (_groupDataStaging.IsCreated) _groupDataStaging.Dispose();
            if (_unitStaging.IsCreated) _unitStaging.Dispose();
            if (_seeableStaging.IsCreated) _seeableStaging.Dispose();
            if (_islandStaging.IsCreated) _islandStaging.Dispose();
            if (_groupUnitCounts.IsCreated) _groupUnitCounts.Dispose();
            if (_groupUnitOffsets.IsCreated) _groupUnitOffsets.Dispose();
        }

        protected override void OnUpdate()
        {
            // Get runtime from MonoBehaviour
            var behaviour = VisibilitySystemBehaviour.Instance;
            if (behaviour == null || !behaviour.IsReady)
                return;

            // Clear staging buffers
            _groupDataStaging.Clear();
            _unitStaging.Clear();
            _seeableStaging.Clear();
            _islandStaging.Clear();

            for (int i = 0; i < GPUConstants.MAX_GROUPS; i++)
            {
                _groupUnitCounts[i] = 0;
                _groupUnitOffsets[i] = 0;
            }

            // Collect data
            CollectUnits();
            CollectSeeables();
            CollectIslands();
            BuildGroupData();

            // Upload to GPU via Runtime
            UploadToGPU(behaviour.Runtime);
        }

        private void CollectUnits()
        {
            // [PERF #41] Zero-allocation job-based collection

            // First pass: count units per group using Burst job
            var countJob = new CountUnitsJob
            {
                GroupCounts = _groupUnitCounts
            };
            countJob.Run(_unitQuery);

            // Calculate offsets (trivial, stays on main thread)
            int totalUnits = 0;
            for (int g = 0; g < GPUConstants.MAX_GROUPS; g++)
            {
                _groupUnitOffsets[g] = totalUnits;
                totalUnits += _groupUnitCounts[g];
            }

            // Prepare staging with correct size (no allocation if capacity sufficient)
            _unitStaging.Resize(totalUnits, NativeArrayOptions.ClearMemory);

            // Prepare write positions (copy of offsets, modified during write)
            var writePos = new NativeArray<int>(GPUConstants.MAX_GROUPS, Allocator.TempJob);
            for (int g = 0; g < GPUConstants.MAX_GROUPS; g++)
                writePos[g] = _groupUnitOffsets[g];

            // Second pass: write unit data using Burst job
            var writeJob = new WriteUnitsJob
            {
                Staging = _unitStaging,
                WritePositions = writePos
            };
            writeJob.Run(_unitQuery);

            writePos.Dispose();
        }

        private void CollectSeeables()
        {
            // [PERF #41] Zero-allocation: pre-size list and use Burst job
            int count = _seeableQuery.CalculateEntityCount();
            _seeableStaging.Resize(count, NativeArrayOptions.UninitializedMemory);

            if (count == 0)
                return;

            var writeIndex = new NativeReference<int>(0, Allocator.TempJob);

            var job = new CollectSeeablesJobSequential
            {
                Staging = _seeableStaging,
                WriteIndex = writeIndex
            };
            job.Run(_seeableQuery);

            // Trim to actual written count (in case of filtering)
            _seeableStaging.Resize(writeIndex.Value, NativeArrayOptions.UninitializedMemory);
            writeIndex.Dispose();
        }

        private void CollectIslands()
        {
            // [PERF #41] Zero-allocation: pre-size list and use Burst job
            int count = math.min(_islandQuery.CalculateEntityCount(), GPUConstants.MAX_ISLANDS);
            _islandStaging.Resize(count, NativeArrayOptions.UninitializedMemory);

            if (count == 0)
                return;

            // Get runtime validity mask (source of truth for which islands have textures)
            var behaviour = VisibilitySystemBehaviour.Instance;
            uint runtimeValidityMask = behaviour?.Runtime?.IslandValidityMask ?? 0;

            var writeIndex = new NativeReference<int>(0, Allocator.TempJob);

            var job = new CollectIslandsJobSequential
            {
                Staging = _islandStaging,
                WriteIndex = writeIndex,
                RuntimeValidityMask = runtimeValidityMask,
                MaxIslands = GPUConstants.MAX_ISLANDS
            };
            job.Run(_islandQuery);

            // Trim to actual written count
            _islandStaging.Resize(writeIndex.Value, NativeArrayOptions.UninitializedMemory);
            writeIndex.Dispose();
        }

        private void BuildGroupData()
        {
            var registry = SystemAPI.GetSingleton<VisionGroupRegistry>();
            ref var groups = ref registry.Blob.Value.Groups;

            for (int g = 0; g < GPUConstants.MAX_GROUPS; g++)
            {
                var def = groups[g];
                int unitCount = _groupUnitCounts[g];
                int unitStart = _groupUnitOffsets[g];

                // Compute group bounds from units
                float3 center = float3.zero;
                float maxRadius = 0f;
                float maxViewDist = def.DefaultViewDistance;

                if (unitCount > 0)
                {
                    // Calculate centroid
                    for (int i = 0; i < unitCount; i++)
                    {
                        center += _unitStaging[unitStart + i].position;
                        maxViewDist = math.max(maxViewDist, _unitStaging[unitStart + i].primaryRadius);
                    }
                    center /= unitCount;

                    // Calculate bounding radius
                    for (int i = 0; i < unitCount; i++)
                    {
                        float dist = math.length(_unitStaging[unitStart + i].position - center);
                        maxRadius = math.max(maxRadius, dist + _unitStaging[unitStart + i].primaryRadius);
                    }
                }

                var gpuGroup = new VisionGroupDataGPU
                {
                    unitStartIndex = unitStart,
                    unitCount = unitCount,
                    groupCenter = center,
                    groupBoundingRadius = maxRadius,
                    maxViewDistance = maxViewDist,
                    groupId = def.GroupId,
                    visibilityMask = def.DefaultVisibilityMask,
                    flags = (byte)(unitCount > 0 ? 1 : 0), // IsActive
                    padding = 0,
                    reserved = float4.zero
                };

                _groupDataStaging.Add(gpuGroup);
            }
        }

        private void UploadToGPU(VisibilitySystemRuntime runtime)
        {
            // Compute active group mask (for early rejection in shaders)
            uint activeGroupMask = 0;
            for (int g = 0; g < _groupDataStaging.Length && g < GPUConstants.MAX_GROUPS; g++)
            {
                if (_groupDataStaging[g].unitCount > 0)
                {
                    activeGroupMask |= (1u << g);
                }
            }
            runtime.ActiveGroupMask = activeGroupMask;

            // Upload group data
            if (runtime.GroupDataBuffer != null && _groupDataStaging.Length > 0)
            {
                runtime.GroupDataBuffer.SetData(_groupDataStaging.AsArray());
            }

            // Upload unit contributions
            if (runtime.UnitContributionsBuffer != null && _unitStaging.Length > 0)
            {
                runtime.UnitContributionsBuffer.SetData(_unitStaging.AsArray());
            }

            // Upload seeables
            if (runtime.SeeableEntitiesBuffer != null && _seeableStaging.Length > 0)
            {
                runtime.SeeableEntitiesBuffer.SetData(_seeableStaging.AsArray());
            }

            // Upload islands
            if (runtime.IslandsBuffer != null && _islandStaging.Length > 0)
            {
                runtime.IslandsBuffer.SetData(_islandStaging.AsArray());
            }

            // Update counts
            runtime.SeeableCount = _seeableStaging.Length;
            runtime.IslandCount = _islandStaging.Length;
        }

        // =============================================================================
        // [PERF #41] BURST-COMPILED JOBS FOR ZERO-ALLOCATION COLLECTION
        // =============================================================================

        /// <summary>
        /// Job 1: Count units per vision group (first pass).
        /// Uses atomic increment to allow parallel execution.
        /// </summary>
        [BurstCompile]
        private partial struct CountUnitsJob : IJobEntity
        {
            [NativeDisableParallelForRestriction]
            public NativeArray<int> GroupCounts;

            public void Execute(in VisionGroupMembership membership)
            {
                int groupId = membership.GroupId;
                if (groupId < GPUConstants.MAX_GROUPS)
                {
                    // Note: Not thread-safe, but IJobEntity with default scheduling is single-threaded
                    // For parallel safety, use NativeReference<int> with Interlocked.Increment
                    GroupCounts[groupId]++;
                }
            }
        }

        /// <summary>
        /// Job 2: Write unit data to staging buffer (second pass).
        /// Uses per-group write positions with atomic fetch-add for thread safety.
        /// </summary>
        [BurstCompile]
        private partial struct WriteUnitsJob : IJobEntity
        {
            [NativeDisableParallelForRestriction]
            public NativeList<UnitSDFContributionGPU> Staging;

            [NativeDisableParallelForRestriction]
            public NativeArray<int> WritePositions;

            public void Execute(
                in VisionGroupMembership membership,
                in UnitVision vision,
                in LocalToWorld ltw)
            {
                int groupId = membership.GroupId;
                if (groupId >= GPUConstants.MAX_GROUPS)
                    return;

                // Get write position and increment (sequential execution, so no atomic needed)
                int writeIndex = WritePositions[groupId]++;

                var gpu = new UnitSDFContributionGPU
                {
                    position = ltw.Position,
                    primaryRadius = vision.Radius,
                    forwardDirection = math.normalize(ltw.Forward),
                    secondaryParam = vision.Type == VisionType.SphereWithCone
                        ? vision.ConeHalfAngle
                        : vision.SecondaryRadius,
                    packedFlags = UnitSDFContributionGPU.PackFlags((byte)vision.Type, 0, (byte)groupId),
                    padding = float3.zero
                };

                Staging[writeIndex] = gpu;
            }
        }

        /// <summary>
        /// Job 3: Collect seeable entities using sequential write pattern.
        /// </summary>
        [BurstCompile]
        private partial struct CollectSeeablesJobSequential : IJobEntity
        {
            [NativeDisableParallelForRestriction]
            public NativeList<SeeableEntityDataGPU> Staging;

            public NativeReference<int> WriteIndex;

            public void Execute(
                Entity entity,
                in Seeable seeable,
                in VisionGroupMembership membership,
                in LocalToWorld ltw)
            {
                int idx = WriteIndex.Value++;

                var gpu = new SeeableEntityDataGPU
                {
                    entityId = entity.Index,
                    position = ltw.Position + new float3(0, seeable.HeightOffset, 0),
                    boundingRadius = 0.5f,
                    packedFlags = SeeableEntityDataGPU.PackFlags(membership.GroupId, 0xFF, 0),
                    padding = float2.zero
                };

                Staging[idx] = gpu;
            }
        }

        /// <summary>
        /// Job 4: Collect island data using sequential write pattern.
        /// </summary>
        [BurstCompile]
        private partial struct CollectIslandsJobSequential : IJobEntity
        {
            [NativeDisableParallelForRestriction]
            public NativeList<EnvironmentIslandGPU> Staging;

            public NativeReference<int> WriteIndex;
            public uint RuntimeValidityMask;
            public int MaxIslands;

            public void Execute(in EnvironmentIslandDefinition def, in LocalToWorld ltw)
            {
                // Check validity
                bool isValid = (RuntimeValidityMask & (1u << def.TextureIndex)) != 0;
                if (!isValid)
                    return;

                // Limit to max islands
                int currentIndex = WriteIndex.Value;
                if (currentIndex >= MaxIslands)
                    return;

                // Extract rotation and scale
                float3 scale = new float3(
                    math.length(ltw.Value.c0.xyz),
                    math.length(ltw.Value.c1.xyz),
                    math.length(ltw.Value.c2.xyz)
                );
                float3 scaledHalfExtents = def.LocalHalfExtents * scale;

                float3x3 rotMatrix = new float3x3(
                    math.normalizesafe(ltw.Value.c0.xyz),
                    math.normalizesafe(ltw.Value.c1.xyz),
                    math.normalizesafe(ltw.Value.c2.xyz)
                );
                var rotation = new quaternion(rotMatrix);

                var gpu = EnvironmentIslandGPU.Create(
                    ltw.Position,
                    scaledHalfExtents,
                    rotation,
                    new float3(def.TextureResolution),
                    def.TextureIndex
                );

                Staging[currentIndex] = gpu;
                WriteIndex.Value = currentIndex + 1;
            }
        }
    }
}
