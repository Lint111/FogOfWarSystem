using Unity.Collections;
using Unity.Entities;
using FogOfWar.Visibility.Components;

namespace FogOfWar.Visibility.Query
{
    /// <summary>
    /// Provides query methods for visibility data.
    /// </summary>
    public static class VisibilityQuery
    {
        /// <summary>
        /// Gets all entities visible to a specific group.
        /// </summary>
        /// <param name="entityManager">Entity manager for queries</param>
        /// <param name="groupId">Vision group ID (0-7)</param>
        /// <param name="allocator">Allocator for result list</param>
        /// <returns>List of entities visible to the group</returns>
        public static NativeList<Entity> GetVisibleToGroup(
            EntityManager entityManager,
            int groupId,
            Allocator allocator = Allocator.Temp)
        {
            var result = new NativeList<Entity>(allocator);
            var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Seeable>(),
                ComponentType.ReadOnly<VisibleToGroups>());

            var entities = query.ToEntityArray(Allocator.Temp);
            var visibilities = query.ToComponentDataArray<VisibleToGroups>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (visibilities[i].IsVisibleToGroup(groupId))
                {
                    result.Add(entities[i]);
                }
            }

            entities.Dispose();
            visibilities.Dispose();

            return result;
        }

        /// <summary>
        /// Checks if a specific entity is visible to a group.
        /// </summary>
        /// <param name="entityManager">Entity manager for queries</param>
        /// <param name="entity">Entity to check</param>
        /// <param name="groupId">Vision group ID (0-7)</param>
        /// <returns>True if entity is visible to the group</returns>
        public static bool IsEntityVisibleToGroup(
            EntityManager entityManager,
            Entity entity,
            int groupId)
        {
            if (!entityManager.HasComponent<VisibleToGroups>(entity))
                return false;

            var visibility = entityManager.GetComponentData<VisibleToGroups>(entity);
            return visibility.IsVisibleToGroup(groupId);
        }
    }
}
