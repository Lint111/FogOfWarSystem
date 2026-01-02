using Unity.Entities;

namespace FogOfWar.Visibility.Components
{
    /// <summary>
    /// Tracks which environment islands an entity is associated with.
    /// Used for efficient island-based culling in visibility checks.
    /// </summary>
    public struct IslandMembership : IComponentData
    {
        /// <summary>
        /// Bitmask of islands this entity belongs to.
        /// Bit N set = entity is in island N (supports up to 16 islands).
        /// </summary>
        public ushort IslandMask;

        /// <summary>
        /// Creates membership for a single island.
        /// </summary>
        public static IslandMembership ForIsland(int islandIndex)
        {
            return new IslandMembership { IslandMask = (ushort)(1 << islandIndex) };
        }

        /// <summary>
        /// Checks if entity is in specified island.
        /// </summary>
        public bool IsInIsland(int islandIndex) => (IslandMask & (1 << islandIndex)) != 0;

        /// <summary>
        /// Adds entity to an island.
        /// </summary>
        public void AddToIsland(int islandIndex) => IslandMask |= (ushort)(1 << islandIndex);

        /// <summary>
        /// Removes entity from an island.
        /// </summary>
        public void RemoveFromIsland(int islandIndex) => IslandMask &= (ushort)~(1 << islandIndex);
    }
}
