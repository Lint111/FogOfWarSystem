using Unity.Entities;

namespace FogOfWar.Visibility.Components
{
    /// <summary>
    /// Assigns an entity to a vision group (faction).
    /// Group IDs range from 0 to MAX_GROUPS-1 (currently 0-7).
    /// </summary>
    public struct VisionGroupMembership : IComponentData
    {
        /// <summary>
        /// The vision group this entity belongs to (0-7).
        /// </summary>
        public byte GroupId;
    }
}
