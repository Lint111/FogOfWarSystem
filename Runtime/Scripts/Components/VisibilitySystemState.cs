using Unity.Entities;
using FogOfWar.Visibility.GPU;

namespace FogOfWar.Visibility.Components
{
    /// <summary>
    /// Singleton component tracking visibility system runtime state.
    /// Controls which groups are active and provides frame versioning.
    /// </summary>
    public struct VisibilitySystemState : IComponentData
    {
        /// <summary>
        /// Bitmask of currently active groups.
        /// Bit N set = group N is active and should be processed.
        /// </summary>
        public byte ActiveGroupMask;

        /// <summary>
        /// Bitmask of groups that are player-controlled (for player fog volume).
        /// </summary>
        public byte PlayerGroupMask;

        /// <summary>
        /// Current frame's visibility data version.
        /// Incremented each time visibility is recalculated.
        /// </summary>
        public uint DataVersion;

        /// <summary>
        /// Creates state with all groups active.
        /// </summary>
        public static VisibilitySystemState CreateDefault()
        {
            return new VisibilitySystemState
            {
                ActiveGroupMask = 0xFF, // All 8 groups active
                PlayerGroupMask = 0x01, // Group 0 is player by default
                DataVersion = 0
            };
        }

        /// <summary>
        /// Checks if a group is active.
        /// </summary>
        public bool IsGroupActive(int groupId)
        {
            if (groupId < 0 || groupId >= GPUConstants.MAX_GROUPS) return false;
            return (ActiveGroupMask & (1 << groupId)) != 0;
        }

        /// <summary>
        /// Sets a group's active state.
        /// </summary>
        public void SetGroupActive(int groupId, bool active)
        {
            if (groupId < 0 || groupId >= GPUConstants.MAX_GROUPS) return;
            if (active)
                ActiveGroupMask |= (byte)(1 << groupId);
            else
                ActiveGroupMask &= (byte)~(1 << groupId);
        }

        /// <summary>
        /// Checks if a group is player-controlled.
        /// </summary>
        public bool IsPlayerGroup(int groupId)
        {
            if (groupId < 0 || groupId >= GPUConstants.MAX_GROUPS) return false;
            return (PlayerGroupMask & (1 << groupId)) != 0;
        }
    }
}
