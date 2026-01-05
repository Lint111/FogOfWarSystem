using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
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
                    packedFlags = UnitSDFContributionGPU.PackFlags((byte)vision.Type, 0, groupId),
                    padding = float3.zero
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
                    packedFlags = SeeableEntityDataGPU.PackFlags(membership.GroupId, 0xFF, 0),
                    padding = float2.zero
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

            // Get runtime validity mask (source of truth for which islands have textures)
            var behaviour = VisibilitySystemBehaviour.Instance;
            uint runtimeValidityMask = behaviour?.Runtime?.IslandValidityMask ?? 0;

            for (int i = 0; i < definitions.Length && i < GPUConstants.MAX_ISLANDS; i++)
            {
                var def = definitions[i];
                var ltw = transforms[i];

                // Use runtime validity mask instead of baked IsLoaded flag
                bool isValid = (runtimeValidityMask & (1u << def.TextureIndex)) != 0;
                if (!isValid)
                    continue;

                // Extract rotation and scale from LocalToWorld matrix
                // Scale is the length of each column vector
                float3 scale = new float3(
                    math.length(ltw.Value.c0.xyz),
                    math.length(ltw.Value.c1.xyz),
                    math.length(ltw.Value.c2.xyz)
                );

                // Apply scale to half extents
                float3 scaledHalfExtents = def.LocalHalfExtents * scale;

                // Extract rotation by normalizing columns to remove scale
                // IMPORTANT: Must normalize before quaternion extraction!
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
    }
}
