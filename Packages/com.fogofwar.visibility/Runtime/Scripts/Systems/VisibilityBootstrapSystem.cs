using Unity.Entities;
using Unity.Collections;
using FogOfWar.Visibility.Components;
using FogOfWar.Visibility.GPU;

namespace FogOfWar.Visibility.Systems
{
    /// <summary>
    /// One-time bootstrap system that creates required singletons.
    /// Creates VisionGroupRegistry with default group definitions.
    /// Runs before other visibility systems.
    /// </summary>
    [UpdateInGroup(typeof(VisibilitySystemGroup), OrderFirst = true)]
    public partial class VisibilityBootstrapSystem : SystemBase
    {
        private bool _initialized;
        private Entity _registryEntity;
        private BlobAssetReference<VisionGroupRegistryBlob> _registryBlob;

        protected override void OnDestroy()
        {
            // Dispose blob asset to prevent memory leaks
            if (_registryBlob.IsCreated)
            {
                _registryBlob.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            if (_initialized)
            {
                Enabled = false;
                return;
            }

            UnityEngine.Debug.Log("[VisibilityBootstrap] Creating singletons...");

            try
            {
                CreateVisionGroupRegistry();
                CreateVisibilitySystemState();
                UnityEngine.Debug.Log("[VisibilityBootstrap] Singletons created successfully");
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"[VisibilityBootstrap] Failed to create singletons: {e}");
            }

            _initialized = true;
            Enabled = false;
        }

        private void CreateVisionGroupRegistry()
        {
            // Create blob with default group definitions
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<VisionGroupRegistryBlob>();

            var groups = builder.Allocate(ref root.Groups, GPUConstants.MAX_GROUPS);

            // Group 0: Player (default)
            groups[0] = new VisionGroupDefinition
            {
                Name = "Player",
                GroupId = 0,
                DefaultVisibilityMask = 0xFE, // Can see all except self
                DefaultViewDistance = 50f
            };

            // Group 1: Enemy faction
            groups[1] = new VisionGroupDefinition
            {
                Name = "Enemy",
                GroupId = 1,
                DefaultVisibilityMask = 0xFD, // Can see all except self
                DefaultViewDistance = 30f
            };

            // Groups 2-7: Reserve slots with defaults
            for (int i = 2; i < GPUConstants.MAX_GROUPS; i++)
            {
                groups[i] = new VisionGroupDefinition
                {
                    Name = $"Group{i}",
                    GroupId = (byte)i,
                    DefaultVisibilityMask = (byte)(0xFF & ~(1 << i)),
                    DefaultViewDistance = 20f
                };
            }

            _registryBlob = builder.CreateBlobAssetReference<VisionGroupRegistryBlob>(Allocator.Persistent);

            _registryEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(_registryEntity, new VisionGroupRegistry { Blob = _registryBlob });
        }

        private void CreateVisibilitySystemState()
        {
            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, VisibilitySystemState.CreateDefault());
        }
    }
}
