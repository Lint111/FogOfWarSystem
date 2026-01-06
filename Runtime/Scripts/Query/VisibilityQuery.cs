using Unity.Collections;
using Unity.Entities;
using FogOfWar.Visibility.Components;
using FogOfWar.Visibility.GPU;

namespace FogOfWar.Visibility.Query
{
    /// <summary>
    /// Provides query methods for visibility data.
    /// Use the blob-based methods (prefixed with "Query") for high-performance
    /// queries from the GPU readback results.
    /// </summary>
    public static class VisibilityQuery
    {
        // =====================================================================
        // BLOB-BASED QUERIES (High Performance - use these in gameplay systems)
        // =====================================================================

        /// <summary>
        /// Gets the number of entities visible to a group (Burst-compatible).
        /// </summary>
        /// <param name="queryData">The visibility query singleton</param>
        /// <param name="groupId">Vision group ID (0-7)</param>
        /// <returns>Number of visible entities, or 0 if invalid</returns>
        public static int GetVisibleCount(in VisibilityQueryData queryData, int groupId)
        {
            if (!queryData.IsValid || !queryData.Results.IsCreated || groupId < 0 || groupId >= GPUConstants.MAX_GROUPS)
                return 0;

            ref var blob = ref queryData.Results.Value;
            return blob.GroupCounts[groupId];
        }

        /// <summary>
        /// Checks if a specific entity ID is visible to a group (Burst-compatible).
        /// O(n) where n is visible count for that group.
        /// </summary>
        /// <param name="queryData">The visibility query singleton</param>
        /// <param name="entityId">Entity.Index to check</param>
        /// <param name="groupId">Vision group ID (0-7)</param>
        /// <returns>True if entity is visible to the group</returns>
        public static bool IsEntityIdVisibleToGroup(
            in VisibilityQueryData queryData,
            int entityId,
            int groupId)
        {
            if (!queryData.IsValid || !queryData.Results.IsCreated || groupId < 0 || groupId >= GPUConstants.MAX_GROUPS)
                return false;

            ref var blob = ref queryData.Results.Value;
            int offset = blob.GroupOffsets[groupId];
            int count = blob.GroupCounts[groupId];

            for (int i = 0; i < count; i++)
            {
                if (blob.Entries[offset + i].entityId == entityId)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Gets visibility entry for a specific entity if visible (Burst-compatible).
        /// </summary>
        /// <param name="queryData">The visibility query singleton</param>
        /// <param name="entityId">Entity.Index to find</param>
        /// <param name="groupId">Vision group ID (0-7)</param>
        /// <param name="entry">Output: visibility entry if found</param>
        /// <returns>True if entity was found</returns>
        public static bool TryGetVisibilityEntry(
            in VisibilityQueryData queryData,
            int entityId,
            int groupId,
            out VisibilityEntryGPU entry)
        {
            entry = default;

            if (!queryData.IsValid || !queryData.Results.IsCreated || groupId < 0 || groupId >= GPUConstants.MAX_GROUPS)
                return false;

            ref var blob = ref queryData.Results.Value;
            int offset = blob.GroupOffsets[groupId];
            int count = blob.GroupCounts[groupId];

            for (int i = 0; i < count; i++)
            {
                if (blob.Entries[offset + i].entityId == entityId)
                {
                    entry = blob.Entries[offset + i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the closest visible entity to a group (Burst-compatible).
        /// </summary>
        /// <param name="queryData">The visibility query singleton</param>
        /// <param name="groupId">Vision group ID (0-7)</param>
        /// <param name="entry">Output: visibility entry of closest entity</param>
        /// <returns>True if any entity is visible</returns>
        public static bool TryGetClosestVisible(
            in VisibilityQueryData queryData,
            int groupId,
            out VisibilityEntryGPU entry)
        {
            entry = default;

            if (!queryData.IsValid || !queryData.Results.IsCreated || groupId < 0 || groupId >= GPUConstants.MAX_GROUPS)
                return false;

            ref var blob = ref queryData.Results.Value;
            int offset = blob.GroupOffsets[groupId];
            int count = blob.GroupCounts[groupId];

            if (count == 0)
                return false;

            float closestDist = float.MaxValue;
            int closestIdx = -1;

            for (int i = 0; i < count; i++)
            {
                ref var e = ref blob.Entries[offset + i];
                if (e.distance < closestDist)
                {
                    closestDist = e.distance;
                    closestIdx = offset + i;
                }
            }

            if (closestIdx >= 0)
            {
                entry = blob.Entries[closestIdx];
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets all visible entries for a group within a distance range (Burst-compatible).
        /// </summary>
        /// <param name="queryData">The visibility query singleton</param>
        /// <param name="groupId">Vision group ID (0-7)</param>
        /// <param name="minDistance">Minimum distance (inclusive)</param>
        /// <param name="maxDistance">Maximum distance (inclusive)</param>
        /// <param name="results">Output: array to fill with results (must be pre-allocated)</param>
        /// <returns>Number of entries written to results</returns>
        public static int GetVisibleInRange(
            in VisibilityQueryData queryData,
            int groupId,
            float minDistance,
            float maxDistance,
            NativeArray<VisibilityEntryGPU> results)
        {
            if (!queryData.IsValid || !queryData.Results.IsCreated || groupId < 0 || groupId >= GPUConstants.MAX_GROUPS)
                return 0;

            ref var blob = ref queryData.Results.Value;
            int offset = blob.GroupOffsets[groupId];
            int count = blob.GroupCounts[groupId];

            int written = 0;
            for (int i = 0; i < count && written < results.Length; i++)
            {
                ref var e = ref blob.Entries[offset + i];
                if (e.distance >= minDistance && e.distance <= maxDistance)
                {
                    results[written++] = e;
                }
            }

            return written;
        }

        /// <summary>
        /// Iterates over all visible entries for a group without allocation.
        /// Use in a for loop: for (int i = 0; i &lt; count; i++) { var entry = GetVisibleEntry(..., i); }
        /// </summary>
        /// <param name="queryData">The visibility query singleton</param>
        /// <param name="groupId">Vision group ID (0-7)</param>
        /// <param name="localIndex">Index within the group's visible list (0 to count-1)</param>
        /// <returns>The visibility entry at that index</returns>
        public static VisibilityEntryGPU GetVisibleEntry(
            in VisibilityQueryData queryData,
            int groupId,
            int localIndex)
        {
            if (!queryData.IsValid || !queryData.Results.IsCreated ||
                groupId < 0 || groupId >= GPUConstants.MAX_GROUPS)
                return default;

            ref var blob = ref queryData.Results.Value;
            int offset = blob.GroupOffsets[groupId];
            int count = blob.GroupCounts[groupId];

            if (localIndex < 0 || localIndex >= count)
                return default;

            return blob.Entries[offset + localIndex];
        }

        // =====================================================================
        // COMPONENT-BASED QUERIES (Convenience - for systems that need Entity refs)
        // =====================================================================

        /// <summary>
        /// Gets all entities visible to a specific group.
        /// Note: This queries VisibleToGroups components, which are updated
        /// by VisibleToGroupsUpdateSystem based on GPU readback results.
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
