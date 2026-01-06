using System;
using Unity.Entities;
using FogOfWar.Visibility.GPU;

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
        /// <param name="islandIndex">Island index (0 to MAX_ISLANDS-1).</param>
        /// <exception cref="ArgumentOutOfRangeException">If islandIndex is out of valid range.</exception>
        public static IslandMembership ForIsland(int islandIndex)
        {
            ValidateIslandIndex(islandIndex);
            return new IslandMembership { IslandMask = (ushort)(1 << islandIndex) };
        }

        /// <summary>
        /// Checks if entity is in specified island.
        /// </summary>
        /// <param name="islandIndex">Island index (0 to MAX_ISLANDS-1).</param>
        /// <exception cref="ArgumentOutOfRangeException">If islandIndex is out of valid range.</exception>
        public bool IsInIsland(int islandIndex)
        {
            ValidateIslandIndex(islandIndex);
            return (IslandMask & (1 << islandIndex)) != 0;
        }

        /// <summary>
        /// Adds entity to an island.
        /// </summary>
        /// <param name="islandIndex">Island index (0 to MAX_ISLANDS-1).</param>
        /// <exception cref="ArgumentOutOfRangeException">If islandIndex is out of valid range.</exception>
        public void AddToIsland(int islandIndex)
        {
            ValidateIslandIndex(islandIndex);
            IslandMask |= (ushort)(1 << islandIndex);
        }

        /// <summary>
        /// Removes entity from an island.
        /// </summary>
        /// <param name="islandIndex">Island index (0 to MAX_ISLANDS-1).</param>
        /// <exception cref="ArgumentOutOfRangeException">If islandIndex is out of valid range.</exception>
        public void RemoveFromIsland(int islandIndex)
        {
            ValidateIslandIndex(islandIndex);
            IslandMask &= (ushort)~(1 << islandIndex);
        }

        private static void ValidateIslandIndex(int islandIndex)
        {
            if (islandIndex < 0 || islandIndex >= GPUConstants.MAX_ISLANDS)
                throw new ArgumentOutOfRangeException(nameof(islandIndex),
                    $"Island index must be between 0 and {GPUConstants.MAX_ISLANDS - 1}, got {islandIndex}");
        }
    }
}
