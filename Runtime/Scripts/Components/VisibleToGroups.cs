using Unity.Entities;

namespace FogOfWar.Visibility.Components
{
    /// <summary>
    /// Output component updated by the visibility system.
    /// Contains a bitmask indicating which groups can currently see this entity.
    /// </summary>
    public struct VisibleToGroups : IComponentData
    {
        /// <summary>
        /// Bitmask of groups that can see this entity.
        /// Bit N is set if group N can see this entity.
        /// </summary>
        public byte GroupMask;

        /// <summary>
        /// Check if a specific group can see this entity.
        /// </summary>
        /// <param name="groupId">Group ID (0-7)</param>
        /// <returns>True if the group can see this entity</returns>
        public readonly bool IsVisibleToGroup(int groupId)
        {
            return (GroupMask & (1 << groupId)) != 0;
        }
    }
}
