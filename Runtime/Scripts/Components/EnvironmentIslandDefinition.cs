using Unity.Entities;
using Unity.Mathematics;
using FogOfWar.Visibility.GPU;

namespace FogOfWar.Visibility.Components
{
    /// <summary>
    /// Defines an environment island with its transform and SDF reference.
    /// Islands can be rotated - SDF is baked in local space.
    /// Attach to entity with LocalToWorld for dynamic island positioning.
    /// </summary>
    public struct EnvironmentIslandDefinition : IComponentData
    {
        /// <summary>
        /// Local-space half-extents (size/2) of the island's bounding box.
        /// The SDF texture covers this volume centered at local origin.
        /// </summary>
        public float3 LocalHalfExtents;

        /// <summary>
        /// Resolution of the SDF texture (for scale calculation).
        /// </summary>
        public int3 TextureResolution;

        /// <summary>
        /// Index into the island SDF texture array (0-7).
        /// </summary>
        public int TextureIndex;

        /// <summary>
        /// Whether this island's SDF texture is loaded and valid.
        /// </summary>
        public bool IsLoaded;

        /// <summary>
        /// Creates a GPU-ready struct from this definition and a world transform.
        /// </summary>
        public EnvironmentIslandGPU ToGPU(float3 worldPosition, quaternion worldRotation)
        {
            return EnvironmentIslandGPU.Create(
                worldPosition,
                LocalHalfExtents,
                worldRotation,
                new float3(TextureResolution),
                TextureIndex);
        }
    }

    /// <summary>
    /// Singleton registry tracking all active environment islands.
    /// Updated by EnvironmentIslandCollectionSystem.
    /// </summary>
    public struct EnvironmentIslandRegistry : IComponentData
    {
        /// <summary>
        /// Number of active islands (max 16).
        /// </summary>
        public int ActiveCount;

        /// <summary>
        /// Bitmask of which islands are valid (bit N = island N valid).
        /// </summary>
        public uint ValidityMask;
    }

    /// <summary>
    /// Tag component for entities that represent environment islands.
    /// Used for query filtering.
    /// </summary>
    public struct EnvironmentIslandTag : IComponentData { }
}
