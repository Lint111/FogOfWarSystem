using Unity.Entities;
using Unity.Mathematics;

namespace FogOfWar.Visibility.Components
{
    /// <summary>
    /// Vision type determining SDF evaluation method.
    /// </summary>
    public enum VisionType : byte
    {
        /// <summary>Radius-based omnidirectional vision.</summary>
        Sphere = 0,
        /// <summary>Sphere + forward-facing cone.</summary>
        SphereWithCone = 1,
        /// <summary>Two smooth-unioned spheres.</summary>
        DualSphere = 2
    }

    /// <summary>
    /// Defines a unit's vision contribution to its group's visibility.
    /// </summary>
    public struct UnitVision : IComponentData
    {
        /// <summary>The type of vision volume.</summary>
        public VisionType Type;

        /// <summary>Primary vision radius in world units.</summary>
        public float Radius;

        /// <summary>Secondary radius for DualSphere type.</summary>
        public float SecondaryRadius;

        /// <summary>Cone half-angle in radians for SphereWithCone type.</summary>
        public float ConeHalfAngle;

        /// <summary>Forward direction for cone vision.</summary>
        public float3 Forward;
    }
}
