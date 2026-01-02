using Unity.Entities;

namespace FogOfWar.Visibility.Components
{
    /// <summary>
    /// Enableable tag marking an entity for visibility recalculation.
    /// When enabled, the visibility system will re-evaluate this entity.
    /// Automatically disabled after processing.
    /// </summary>
    public struct VisibilityDirty : IComponentData, IEnableableComponent
    {
    }
}
