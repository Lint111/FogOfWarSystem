using UnityEngine;
using Unity.Entities;
using FogOfWar.Visibility.Components;

namespace FogOfWar.Visibility.Authoring
{
    /// <summary>
    /// Authoring component for entities with vision capability.
    /// Adds UnitVision and VisionGroupMembership to the baked entity.
    /// </summary>
    public class UnitVisionAuthoring : MonoBehaviour
    {
        [Header("Group")]
        [Tooltip("Vision group this unit belongs to (0-7)")]
        [Range(0, 7)]
        public int GroupId = 0;

        [Header("Vision Parameters")]
        [Tooltip("Type of vision volume")]
        public VisionType Type = VisionType.Sphere;

        [Tooltip("Primary vision radius in world units")]
        [Min(0.1f)]
        public float Radius = 10f;

        [Tooltip("Secondary radius for dual-sphere, or cone angle (degrees) for cone vision")]
        [Min(0f)]
        public float SecondaryParameter = 5f;

        [Header("Gizmos")]
        public bool ShowGizmos = true;
        public Color GizmoColor = new Color(0f, 1f, 0f, 0.3f);

        private void OnDrawGizmosSelected()
        {
            if (!ShowGizmos) return;

            Gizmos.color = GizmoColor;

            // Draw primary sphere
            Gizmos.DrawWireSphere(transform.position, Radius);

            if (Type == VisionType.SphereWithCone && SecondaryParameter > 0)
            {
                // Draw cone direction
                var forward = transform.forward * Radius * 2f;
                Gizmos.DrawRay(transform.position, forward);

                // Draw cone bounds
                float angleRad = SecondaryParameter * Mathf.Deg2Rad;
                float coneRadius = Radius * 2f * Mathf.Tan(angleRad);
                var endPoint = transform.position + forward;
                Gizmos.DrawWireSphere(endPoint, coneRadius * 0.5f);
            }
            else if (Type == VisionType.DualSphere && SecondaryParameter > 0)
            {
                // Draw second sphere
                var secondPos = transform.position + transform.forward * Radius;
                Gizmos.DrawWireSphere(secondPos, SecondaryParameter);
            }
        }
    }

    public class UnitVisionBaker : Baker<UnitVisionAuthoring>
    {
        public override void Bake(UnitVisionAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new VisionGroupMembership
            {
                GroupId = (byte)authoring.GroupId
            });

            AddComponent(entity, new UnitVision
            {
                Type = authoring.Type,
                Radius = authoring.Radius,
                SecondaryRadius = authoring.Type == VisionType.DualSphere ? authoring.SecondaryParameter : 0f,
                ConeHalfAngle = authoring.Type == VisionType.SphereWithCone
                    ? authoring.SecondaryParameter * Mathf.Deg2Rad
                    : 0f,
                Forward = authoring.transform.forward
            });
        }
    }
}
