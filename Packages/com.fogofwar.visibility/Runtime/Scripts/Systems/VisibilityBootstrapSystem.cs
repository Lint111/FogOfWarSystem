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

        protected override void OnUpdate()
        {
            if (_initialized)
            {
                Enabled = false;
                return;
            }

            CreateVisionGroupRegistry();
            CreateVisibilitySystemState();

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

            var blob = builder.CreateBlobAssetReference<VisionGroupRegistryBlob>(Allocator.Persistent);

            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, new VisionGroupRegistry { Blob = blob });
        }

        private void CreateVisibilitySystemState()
        {
            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, VisibilitySystemState.CreateDefault());
        }
    }
}
