using UnityEngine;
using Unity.Entities;
using FogOfWar.Visibility.Components;

namespace FogOfWar.Visibility.Authoring
{
    /// <summary>
    /// Authoring component for entities that can be seen by other groups.
    /// Adds Seeable, VisionGroupMembership, and VisibleToGroups to the baked entity.
    /// </summary>
    public class SeeableAuthoring : MonoBehaviour
    {
        [Header("Group")]
        [Tooltip("Vision group this entity belongs to (0-7)")]
        [Range(0, 7)]
        public int GroupId = 1;

        [Header("Visibility Settings")]
        [Tooltip("Height offset for visibility check point (e.g., torso height)")]
        public float HeightOffset = 1f;

        [Tooltip("Bounding radius for partial visibility calculations")]
        [Min(0.1f)]
        public float BoundingRadius = 0.5f;

        [Header("Gizmos")]
        public bool ShowGizmos = true;
        public Color GizmoColor = new Color(1f, 0.5f, 0f, 0.5f);

        private void OnDrawGizmosSelected()
        {
            if (!ShowGizmos) return;

            Gizmos.color = GizmoColor;

            // Draw bounding sphere at height offset
            var checkPoint = transform.position + Vector3.up * HeightOffset;
            Gizmos.DrawWireSphere(checkPoint, BoundingRadius);

            // Draw line from ground to check point
            Gizmos.DrawLine(transform.position, checkPoint);
        }
    }

    public class SeeableBaker : Baker<SeeableAuthoring>
    {
        public override void Bake(SeeableAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new VisionGroupMembership
            {
                GroupId = (byte)authoring.GroupId
            });

            AddComponent(entity, new Seeable
            {
                HeightOffset = authoring.HeightOffset
            });

            // Add output component (will be updated by visibility system)
            AddComponent(entity, new VisibleToGroups
            {
                GroupMask = 0
            });
        }
    }
}
