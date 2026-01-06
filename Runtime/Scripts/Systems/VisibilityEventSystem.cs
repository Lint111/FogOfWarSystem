using Unity.Entities;
using Unity.Collections;
using FogOfWar.Visibility.Components;
using FogOfWar.Visibility.Core;
using FogOfWar.Visibility.GPU;
using FogOfWar.Visibility.Query;

namespace FogOfWar.Visibility.Systems
{
    /// <summary>
    /// Generates visibility events when entities enter or exit vision.
    /// Runs after VisibilityReadbackSystem and compares current vs previous visibility state.
    /// Fires events through VisibilitySystemBehaviour.Instance.Runtime.
    /// </summary>
    [UpdateInGroup(typeof(VisibilitySystemGroup))]
    [UpdateAfter(typeof(VisibilityReadbackSystem))]
    public partial class VisibilityEventSystem : SystemBase
    {
        // Previous frame's visibility per group (entityId -> was visible)
        private NativeParallelHashMap<int, byte>[] _previousVisibility;
        private bool _initialized;
        private int _lastProcessedFrame;

        // [PERF #43] Pre-allocated containers to avoid per-frame allocations
        private NativeParallelHashSet<int>[] _currentlyVisibleSets;
        private NativeList<int>[] _toRemoveLists;
        private NativeList<int>[] _keyLists;  // Reusable key enumeration

        protected override void OnCreate()
        {
            RequireForUpdate<VisibilityQueryData>();
        }

        protected override void OnDestroy()
        {
            if (_initialized)
            {
                for (int g = 0; g < GPUConstants.MAX_GROUPS; g++)
                {
                    if (_previousVisibility[g].IsCreated)
                        _previousVisibility[g].Dispose();

                    // [PERF #43] Dispose pre-allocated containers
                    if (_currentlyVisibleSets[g].IsCreated)
                        _currentlyVisibleSets[g].Dispose();
                    if (_toRemoveLists[g].IsCreated)
                        _toRemoveLists[g].Dispose();
                    if (_keyLists[g].IsCreated)
                        _keyLists[g].Dispose();
                }
            }
        }

        protected override void OnUpdate()
        {
            // Get runtime from MonoBehaviour
            var behaviour = VisibilitySystemBehaviour.Instance;
            if (behaviour == null || !behaviour.IsReady)
                return;

            if (!_initialized)
            {
                Initialize();
                return;
            }

            // Get current visibility data
            var queryData = SystemAPI.GetSingleton<VisibilityQueryData>();
            if (!queryData.IsValid || !queryData.Results.IsCreated)
                return;

            // Skip if we already processed this frame
            if (queryData.FrameComputed == _lastProcessedFrame)
                return;

            _lastProcessedFrame = queryData.FrameComputed;

            // Process each group
            for (int groupId = 0; groupId < GPUConstants.MAX_GROUPS; groupId++)
            {
                ProcessGroupVisibility(queryData, groupId, behaviour.Runtime);
            }
        }

        private void Initialize()
        {
            _previousVisibility = new NativeParallelHashMap<int, byte>[GPUConstants.MAX_GROUPS];

            // [PERF #43] Pre-allocate per-group containers
            _currentlyVisibleSets = new NativeParallelHashSet<int>[GPUConstants.MAX_GROUPS];
            _toRemoveLists = new NativeList<int>[GPUConstants.MAX_GROUPS];
            _keyLists = new NativeList<int>[GPUConstants.MAX_GROUPS];

            for (int g = 0; g < GPUConstants.MAX_GROUPS; g++)
            {
                _previousVisibility[g] = new NativeParallelHashMap<int, byte>(256, Allocator.Persistent);
                _currentlyVisibleSets[g] = new NativeParallelHashSet<int>(256, Allocator.Persistent);
                _toRemoveLists[g] = new NativeList<int>(64, Allocator.Persistent);
                _keyLists[g] = new NativeList<int>(256, Allocator.Persistent);
            }
            _initialized = true;
        }

        private void ProcessGroupVisibility(VisibilityQueryData queryData, int groupId, VisibilitySystemRuntime runtime)
        {
            ref var blob = ref queryData.Results.Value;
            int offset = blob.GroupOffsets[groupId];
            int count = blob.GroupCounts[groupId];

            var prevMap = _previousVisibility[groupId];

            // [PERF #43] Use pre-allocated containers instead of per-frame allocations
            var currentlyVisible = _currentlyVisibleSets[groupId];
            currentlyVisible.Clear();

            // Check for new visibility entries (entered vision)
            for (int i = 0; i < count; i++)
            {
                var entry = blob.Entries[offset + i];
                int entityId = entry.entityId;
                currentlyVisible.Add(entityId);

                // Check if this entity was NOT visible before
                if (!prevMap.ContainsKey(entityId))
                {
                    // Entity just entered vision - fire event
                    var info = new VisibilityChangeInfo
                    {
                        EntityId = entityId,
                        ViewerGroupId = (byte)groupId,
                        Distance = entry.distance,
                        EventType = VisibilityEventType.Entered,
                        FrameNumber = queryData.FrameComputed
                    };

                    runtime.FireVisibilityEvent(info);
                    prevMap.TryAdd(entityId, 1);
                }
            }

            // [PERF #43] Use pre-allocated containers for exit detection
            var toRemove = _toRemoveLists[groupId];
            toRemove.Clear();

            var keyList = _keyLists[groupId];
            keyList.Clear();

            // Copy keys to reusable list (avoids GetKeyArray allocation)
            foreach (var kvp in prevMap)
            {
                keyList.Add(kvp.Key);
            }

            for (int i = 0; i < keyList.Length; i++)
            {
                int entityId = keyList[i];
                if (!currentlyVisible.Contains(entityId))
                {
                    // Entity just left vision - fire event
                    var info = new VisibilityChangeInfo
                    {
                        EntityId = entityId,
                        ViewerGroupId = (byte)groupId,
                        Distance = 0f, // Unknown after exit
                        EventType = VisibilityEventType.Exited,
                        FrameNumber = queryData.FrameComputed
                    };

                    runtime.FireVisibilityEvent(info);
                    toRemove.Add(entityId);
                }
            }

            // Remove exited entities from tracking
            for (int i = 0; i < toRemove.Length; i++)
            {
                prevMap.Remove(toRemove[i]);
            }
        }

        /// <summary>
        /// Clears the visibility tracking state for a group.
        /// Call this when respawning or resetting units.
        /// </summary>
        public void ClearGroupState(int groupId)
        {
            if (_initialized && groupId >= 0 && groupId < GPUConstants.MAX_GROUPS)
            {
                _previousVisibility[groupId].Clear();
            }
        }

        /// <summary>
        /// Clears all visibility tracking state.
        /// </summary>
        public void ClearAllState()
        {
            if (_initialized)
            {
                for (int g = 0; g < GPUConstants.MAX_GROUPS; g++)
                {
                    _previousVisibility[g].Clear();
                }
            }
        }
    }
}
