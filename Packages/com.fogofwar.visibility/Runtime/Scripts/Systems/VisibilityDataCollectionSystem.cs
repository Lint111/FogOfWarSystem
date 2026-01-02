using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Burst;
using FogOfWar.Visibility.Components;
using FogOfWar.Visibility.GPU;

namespace FogOfWar.Visibility.Systems
{
    /// <summary>
    /// Collects ECS visibility data and uploads to GPU buffers.
    /// Runs before VisibilityComputeDispatchSystem.
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

            // Upload to GPU via the dispatch system's buffers
            UploadToGPU();
        }

        private void CollectUnits()
        {
            var memberships = _unitQuery.ToComponentDataArray<VisionGroupMembership>(Allocator.Temp);
            var visions = _unitQuery.ToComponentDataArray<UnitVision>(Allocator.Temp);
            var transforms = _unitQuery.ToComponentDataArray<LocalToWorld>(Allocator.Temp);

            // First pass: count per group
            for (int i = 0; i < memberships.Length; i++)
            {
                byte groupId = memberships[i].GroupId;
                if (groupId < GPUConstants.MAX_GROUPS)
                    _groupUnitCounts[groupId]++;
            }

            // Calculate offsets
            int offset = 0;
            for (int g = 0; g < GPUConstants.MAX_GROUPS; g++)
            {
                _groupUnitOffsets[g] = offset;
                offset += _groupUnitCounts[g];
            }

            // Prepare staging with correct size
            _unitStaging.Resize(offset, NativeArrayOptions.ClearMemory);

            // Track current write position per group
            var writePos = new NativeArray<int>(GPUConstants.MAX_GROUPS, Allocator.Temp);
            for (int g = 0; g < GPUConstants.MAX_GROUPS; g++)
                writePos[g] = _groupUnitOffsets[g];

            // Second pass: fill staging buffer (grouped by faction)
            for (int i = 0; i < memberships.Length; i++)
            {
                byte groupId = memberships[i].GroupId;
                if (groupId >= GPUConstants.MAX_GROUPS)
                    continue;

                var vision = visions[i];
                var ltw = transforms[i];

                var gpu = new UnitSDFContributionGPU
                {
                    position = ltw.Position,
                    primaryRadius = vision.Radius,
                    forwardDirection = math.normalize(ltw.Forward),
                    secondaryParam = vision.Type == VisionType.SphereWithCone
                        ? vision.ConeHalfAngle
                        : vision.SecondaryRadius,
                    visionType = (byte)vision.Type,
                    flags = 0,
                    ownerGroupId = groupId,
                    padding = float4.zero
                };

                _unitStaging[writePos[groupId]++] = gpu;
            }

            writePos.Dispose();
            memberships.Dispose();
            visions.Dispose();
            transforms.Dispose();
        }

        private void CollectSeeables()
        {
            var entities = _seeableQuery.ToEntityArray(Allocator.Temp);
            var seeables = _seeableQuery.ToComponentDataArray<Seeable>(Allocator.Temp);
            var memberships = _seeableQuery.ToComponentDataArray<VisionGroupMembership>(Allocator.Temp);
            var transforms = _seeableQuery.ToComponentDataArray<LocalToWorld>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var seeable = seeables[i];
                var membership = memberships[i];
                var ltw = transforms[i];

                var gpu = new SeeableEntityDataGPU
                {
                    entityId = entities[i].Index,
                    position = ltw.Position + new float3(0, seeable.HeightOffset, 0),
                    boundingRadius = 0.5f, // Default bounding radius
                    ownerGroupId = membership.GroupId,
                    seeableByMask = 0xFF, // Visible to all groups by default
                    flags = 0,
                    padding = float4.zero
                };

                _seeableStaging.Add(gpu);
            }

            entities.Dispose();
            seeables.Dispose();
            memberships.Dispose();
            transforms.Dispose();
        }

        private void CollectIslands()
        {
            var definitions = _islandQuery.ToComponentDataArray<EnvironmentIslandDefinition>(Allocator.Temp);
            var transforms = _islandQuery.ToComponentDataArray<LocalToWorld>(Allocator.Temp);

            for (int i = 0; i < definitions.Length && i < GPUConstants.MAX_ISLANDS; i++)
            {
                var def = definitions[i];
                var ltw = transforms[i];

                if (!def.IsLoaded)
                    continue;

                // Extract rotation from LocalToWorld
                var rotation = new quaternion(ltw.Value);

                var gpu = EnvironmentIslandGPU.Create(
                    ltw.Position,
                    def.LocalHalfExtents,
                    rotation,
                    new float3(def.TextureResolution),
                    def.TextureIndex
                );

                _islandStaging.Add(gpu);
            }

            definitions.Dispose();
            transforms.Dispose();
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

        private void UploadToGPU()
        {
            // Get buffer reference from dispatch system
            if (!SystemAPI.ManagedAPI.TryGetSingleton<VisibilityGPUBuffersRef>(out var buffersRef))
                return;

            // Upload group data
            if (buffersRef.GroupData != null && _groupDataStaging.Length > 0)
            {
                buffersRef.GroupData.SetData(_groupDataStaging.AsArray());
            }

            // Upload unit contributions
            if (buffersRef.UnitContributions != null && _unitStaging.Length > 0)
            {
                buffersRef.UnitContributions.SetData(_unitStaging.AsArray());
            }

            // Upload seeables
            if (buffersRef.SeeableEntities != null && _seeableStaging.Length > 0)
            {
                buffersRef.SeeableEntities.SetData(_seeableStaging.AsArray());
            }

            // Upload islands
            if (buffersRef.Islands != null && _islandStaging.Length > 0)
            {
                buffersRef.Islands.SetData(_islandStaging.AsArray());
            }

            // Update counts for shaders
            buffersRef.SeeableCount = _seeableStaging.Length;
            buffersRef.GroupCount = GPUConstants.MAX_GROUPS;
            buffersRef.IslandCount = _islandStaging.Length;
        }
    }
}
