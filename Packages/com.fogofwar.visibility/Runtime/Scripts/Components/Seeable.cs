using Unity.Entities;

namespace FogOfWar.Visibility.Components
{
    /// <summary>
    /// Tag component marking an entity as potentially visible to other vision groups.
    /// Entities with this component will be evaluated by the visibility system.
    /// </summary>
    public struct Seeable : IComponentData
    {
        /// <summary>
        /// Vertical offset from entity position for visibility check point.
        /// Useful for ground-based entities that should be visible at torso height.
        /// </summary>
        public float HeightOffset;
    }
}
