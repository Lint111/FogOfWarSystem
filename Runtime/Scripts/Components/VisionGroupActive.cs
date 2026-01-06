using Unity.Entities;
using FogOfWar.Visibility.GPU;

namespace FogOfWar.Visibility.Components
{
    /// <summary>
    /// Singleton component controlling which vision groups are active.
    /// Use to enable/disable visibility processing for specific factions.
    /// </summary>
    public struct VisionGroupActive : IComponentData
    {
        /// <summary>
        /// Bitmask of active groups. Bit N = group N is active.
        /// Default: all groups active (0xFF for 8 groups).
        /// </summary>
        public byte ActiveMask;

        /// <summary>
        /// Creates with all groups active.
        /// </summary>
        public static VisionGroupActive AllActive => new VisionGroupActive { ActiveMask = 0xFF };

        /// <summary>
        /// Creates with no groups active.
        /// </summary>
        public static VisionGroupActive NoneActive => new VisionGroupActive { ActiveMask = 0x00 };

        /// <summary>
        /// Creates with only the specified group active.
        /// </summary>
        public static VisionGroupActive OnlyGroup(int groupId)
        {
            ValidateGroupId(groupId);
            return new VisionGroupActive { ActiveMask = (byte)(1 << groupId) };
        }

        /// <summary>
        /// Check if a group is active.
        /// </summary>
        public readonly bool IsGroupActive(int groupId)
        {
            if (groupId < 0 || groupId >= GPUConstants.MAX_GROUPS)
                return false;
            return (ActiveMask & (1 << groupId)) != 0;
        }

        /// <summary>
        /// Enable a specific group.
        /// </summary>
        public void EnableGroup(int groupId)
        {
            ValidateGroupId(groupId);
            ActiveMask |= (byte)(1 << groupId);
        }

        /// <summary>
        /// Disable a specific group.
        /// </summary>
        public void DisableGroup(int groupId)
        {
            ValidateGroupId(groupId);
            ActiveMask &= (byte)~(1 << groupId);
        }

        /// <summary>
        /// Set a group's active state.
        /// </summary>
        public void SetGroupActive(int groupId, bool active)
        {
            if (active)
                EnableGroup(groupId);
            else
                DisableGroup(groupId);
        }

        /// <summary>
        /// Count of currently active groups.
        /// </summary>
        public readonly int ActiveCount
        {
            get
            {
                int count = 0;
                byte mask = ActiveMask;
                while (mask != 0)
                {
                    count += mask & 1;
                    mask >>= 1;
                }
                return count;
            }
        }

        private static void ValidateGroupId(int groupId)
        {
            if (groupId < 0 || groupId >= GPUConstants.MAX_GROUPS)
                throw new System.ArgumentOutOfRangeException(nameof(groupId),
                    $"Group ID must be between 0 and {GPUConstants.MAX_GROUPS - 1}, got {groupId}");
        }
    }
}
