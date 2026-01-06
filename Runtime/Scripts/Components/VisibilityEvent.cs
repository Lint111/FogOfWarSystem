using Unity.Entities;

namespace FogOfWar.Visibility.Components
{
    /// <summary>
    /// Type of visibility state change.
    /// </summary>
    public enum VisibilityEventType : byte
    {
        /// <summary>Entity just became visible to the group.</summary>
        Entered = 0,
        /// <summary>Entity just left the group's vision.</summary>
        Exited = 1
    }

    /// <summary>
    /// Visibility change event. Stored in a dynamic buffer on entities
    /// that need to react to visibility changes.
    /// Cleared each frame after processing by game systems.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct VisibilityEvent : IBufferElementData
    {
        /// <summary>
        /// Entity.Index of the entity that entered/exited vision.
        /// </summary>
        public int OtherEntityId;

        /// <summary>
        /// Distance to the entity when the event occurred.
        /// </summary>
        public float Distance;

        /// <summary>
        /// Which group's vision triggered this event.
        /// </summary>
        public byte ViewerGroupId;

        /// <summary>
        /// Type of visibility change (entered or exited).
        /// </summary>
        public VisibilityEventType EventType;

        /// <summary>
        /// Creates an "entered vision" event.
        /// </summary>
        public static VisibilityEvent Entered(int entityId, byte groupId, float distance)
        {
            return new VisibilityEvent
            {
                OtherEntityId = entityId,
                ViewerGroupId = groupId,
                Distance = distance,
                EventType = VisibilityEventType.Entered
            };
        }

        /// <summary>
        /// Creates an "exited vision" event.
        /// </summary>
        public static VisibilityEvent Exited(int entityId, byte groupId, float distance)
        {
            return new VisibilityEvent
            {
                OtherEntityId = entityId,
                ViewerGroupId = groupId,
                Distance = distance,
                EventType = VisibilityEventType.Exited
            };
        }
    }
}
