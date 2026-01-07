using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using FogOfWar.Visibility.Authoring;

namespace FogOfWar.Visibility.Editor.SDFBaking
{
    /// <summary>
    /// Editor singleton that monitors scene changes and tracks which islands need rebaking.
    /// Implements debounced change detection to prevent thrashing.
    /// </summary>
    [InitializeOnLoad]
    public static class IslandDirtyTracker
    {
        private static readonly Dictionary<string, IslandTrackingData> _trackedIslands = new();
        private static readonly HashSet<string> _pendingBakeQueue = new();
        private static double _lastChangeTime;
        private static bool _isDebouncing;
        private static SDFBakeConfig _config;

        /// <summary>
        /// Event fired when an island is queued for baking (after debounce).
        /// </summary>
        public static event Action<string> OnIslandQueuedForBake;

        /// <summary>
        /// Event fired when debounce timer starts.
        /// </summary>
        public static event Action OnDebounceStarted;

        /// <summary>
        /// Event fired when debounce timer completes and bake queue is processed.
        /// </summary>
        public static event Action OnDebounceCompleted;

        static IslandDirtyTracker()
        {
            // Subscribe to Unity events
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += OnEditorUpdate;

            // Subscribe to island events
            IslandSDFContributor.OnIslandRegistered += OnIslandRegistered;
            IslandSDFContributor.OnIslandUnregistered += OnIslandUnregistered;
            IslandSDFContributor.OnIslandDirty += OnIslandMarkedDirty;

            Debug.Log("[IslandDirtyTracker] Initialized");
        }

        /// <summary>
        /// Gets the current bake config.
        /// </summary>
        public static SDFBakeConfig Config
        {
            get
            {
                if (_config == null)
                {
                    _config = SDFBakeConfig.GetOrCreateDefault();
                }
                return _config;
            }
        }

        /// <summary>
        /// Gets all tracked islands.
        /// </summary>
        public static IReadOnlyDictionary<string, IslandTrackingData> TrackedIslands => _trackedIslands;

        /// <summary>
        /// Gets all islands pending bake.
        /// </summary>
        public static IReadOnlyCollection<string> PendingBakeQueue => _pendingBakeQueue;

        /// <summary>
        /// Whether a debounce is currently active.
        /// </summary>
        public static bool IsDebouncing => _isDebouncing;

        /// <summary>
        /// Time remaining on debounce timer (in seconds).
        /// </summary>
        public static float DebounceTimeRemaining
        {
            get
            {
                if (!_isDebouncing) return 0f;
                float elapsed = (float)(EditorApplication.timeSinceStartup - _lastChangeTime);
                return Mathf.Max(0f, Config.DebounceDuration - elapsed);
            }
        }

        /// <summary>
        /// Gets all dirty islands.
        /// </summary>
        public static List<IslandSDFContributor> GetDirtyIslands()
        {
            var result = new List<IslandSDFContributor>();
            foreach (var kvp in _trackedIslands)
            {
                if (kvp.Value.Contributor != null && kvp.Value.Contributor.IsDirty)
                {
                    result.Add(kvp.Value.Contributor);
                }
            }
            return result;
        }

        /// <summary>
        /// Forces an immediate check for changes on all islands.
        /// </summary>
        public static void ForceCheckAllIslands()
        {
            foreach (var kvp in _trackedIslands)
            {
                if (kvp.Value.Contributor != null)
                {
                    kvp.Value.Contributor.CheckForChanges();
                }
            }
        }

        /// <summary>
        /// Clears the pending bake queue.
        /// </summary>
        public static void ClearPendingQueue()
        {
            _pendingBakeQueue.Clear();
            _isDebouncing = false;
        }

        /// <summary>
        /// Forces all dirty islands to be queued for immediate baking.
        /// </summary>
        public static void QueueAllDirtyIslandsNow()
        {
            foreach (var island in GetDirtyIslands())
            {
                QueueIslandForBake(island.IslandId, immediate: true);
            }
        }

        private static void OnIslandRegistered(IslandSDFContributor contributor)
        {
            if (contributor == null) return;

            string id = contributor.IslandId;
            if (!_trackedIslands.ContainsKey(id))
            {
                _trackedIslands[id] = new IslandTrackingData
                {
                    Contributor = contributor,
                    LastModifiedTime = EditorApplication.timeSinceStartup
                };
                Debug.Log($"[IslandDirtyTracker] Registered island: {id}");
            }
        }

        private static void OnIslandUnregistered(IslandSDFContributor contributor)
        {
            if (contributor == null) return;

            string id = contributor.IslandId;
            if (_trackedIslands.Remove(id))
            {
                _pendingBakeQueue.Remove(id);
                Debug.Log($"[IslandDirtyTracker] Unregistered island: {id}");
            }
        }

        private static void OnIslandMarkedDirty(IslandSDFContributor contributor)
        {
            if (contributor == null) return;

            string id = contributor.IslandId;
            if (_trackedIslands.TryGetValue(id, out var data))
            {
                data.LastModifiedTime = EditorApplication.timeSinceStartup;
            }

            // Start debounce timer
            StartDebounce(id);
        }

        private static void OnHierarchyChanged()
        {
            // Check all tracked islands for changes
            foreach (var kvp in _trackedIslands)
            {
                // This will fire OnIslandDirty if changes detected
                kvp.Value.Contributor?.CheckForChanges();
            }
        }

        private static void OnEditorUpdate()
        {
            if (!_isDebouncing || _pendingBakeQueue.Count == 0) return;

            // Check if debounce time has elapsed
            float elapsed = (float)(EditorApplication.timeSinceStartup - _lastChangeTime);
            if (elapsed >= Config.DebounceDuration)
            {
                ProcessDebouncedQueue();
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                // Block play mode entry if we have dirty islands and config says to bake first
                if (Config.BakeOnPlayMode)
                {
                    var dirtyIslands = GetDirtyIslands();
                    if (dirtyIslands.Count > 0)
                    {
                        // Cancel the pending play mode entry
                        EditorApplication.isPlaying = false;

                        Debug.Log($"[IslandDirtyTracker] Blocking play mode entry - {dirtyIslands.Count} dirty island(s) need baking");

                        // Queue all dirty islands for immediate bake
                        foreach (var island in dirtyIslands)
                        {
                            QueueIslandForBake(island.IslandId, immediate: true);
                        }

                        // Signal that we're about to bake
                        SDFBakeQueue.OnBakingComplete += ResumePlayMode;
                        SDFBakeQueue.StartProcessing();
                    }
                }
            }
        }

        private static void ResumePlayMode()
        {
            SDFBakeQueue.OnBakingComplete -= ResumePlayMode;

            // Check if all islands are now clean
            var stillDirty = GetDirtyIslands();
            if (stillDirty.Count == 0)
            {
                Debug.Log("[IslandDirtyTracker] All islands baked, entering play mode");
                EditorApplication.isPlaying = true;
            }
            else
            {
                Debug.LogWarning($"[IslandDirtyTracker] {stillDirty.Count} island(s) still dirty after bake. Play mode entry cancelled.");
            }
        }

        private static void StartDebounce(string islandId)
        {
            _pendingBakeQueue.Add(islandId);
            _lastChangeTime = EditorApplication.timeSinceStartup;

            if (!_isDebouncing)
            {
                _isDebouncing = true;
                OnDebounceStarted?.Invoke();
                Debug.Log($"[IslandDirtyTracker] Debounce started ({Config.DebounceDuration}s)");
            }
        }

        private static void ProcessDebouncedQueue()
        {
            _isDebouncing = false;
            OnDebounceCompleted?.Invoke();

            if (_pendingBakeQueue.Count == 0) return;

            Debug.Log($"[IslandDirtyTracker] Debounce complete, queueing {_pendingBakeQueue.Count} island(s) for bake");

            foreach (var islandId in _pendingBakeQueue)
            {
                OnIslandQueuedForBake?.Invoke(islandId);

                // Add to SDFBakeQueue
                if (_trackedIslands.TryGetValue(islandId, out var data) && data.Contributor != null)
                {
                    SDFBakeQueue.Enqueue(data.Contributor);
                }
            }

            _pendingBakeQueue.Clear();

            // Auto-start processing if configured
            if (Config.BakeOnDirtyDebounced)
            {
                SDFBakeQueue.StartProcessing();
            }
        }

        private static void QueueIslandForBake(string islandId, bool immediate = false)
        {
            if (!_trackedIslands.TryGetValue(islandId, out var data) || data.Contributor == null)
            {
                Debug.LogWarning($"[IslandDirtyTracker] Cannot queue unknown island: {islandId}");
                return;
            }

            if (immediate)
            {
                _pendingBakeQueue.Remove(islandId);
                SDFBakeQueue.Enqueue(data.Contributor);
                OnIslandQueuedForBake?.Invoke(islandId);
            }
            else
            {
                StartDebounce(islandId);
            }
        }

        /// <summary>
        /// Tracking data for a single island.
        /// </summary>
        public class IslandTrackingData
        {
            public IslandSDFContributor Contributor;
            public double LastModifiedTime;
        }
    }

    /// <summary>
    /// Asset modification processor for detecting mesh/material changes.
    /// </summary>
    public class IslandAssetModificationProcessor : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            // Check if any saved assets are meshes or materials used by tracked islands
            foreach (var path in paths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);

                if (asset is Mesh || asset is Material)
                {
                    // Force a hierarchy check which will detect if any islands use this asset
                    IslandDirtyTracker.ForceCheckAllIslands();
                    break;
                }
            }

            return paths;
        }
    }
}
