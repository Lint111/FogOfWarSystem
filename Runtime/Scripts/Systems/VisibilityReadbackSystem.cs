using Unity.Entities;
using Unity.Collections;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using FogOfWar.Visibility.Components;
using FogOfWar.Visibility.Core;
using FogOfWar.Visibility.GPU;

namespace FogOfWar.Visibility.Systems
{
    /// <summary>
    /// System update ordering group for all visibility systems.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial class VisibilitySystemGroup : ComponentSystemGroup { }

    /// <summary>
    /// Handles async GPU readback of visibility results.
    /// Uses double-buffered blob assets to eliminate per-frame allocations.
    /// Gets GPU buffers from VisibilitySystemBehaviour.Instance.Runtime.
    /// </summary>
    [UpdateInGroup(typeof(VisibilitySystemGroup))]
    public partial class VisibilityReadbackSystem : SystemBase
    {
        private AsyncGPUReadbackRequest _countsRequest;
        private AsyncGPUReadbackRequest _entriesRequest;
        private bool _requestsPending;
        private int _pendingFrameNumber;

        // Staging arrays for readback data (reused each frame)
        private NativeArray<int> _stagingCounts;
        private NativeArray<VisibilityEntryGPU> _stagingEntries;
        private bool _stagingAllocated;
        private int _maxVisiblePerGroup;

        protected override void OnCreate()
        {
            // Don't require any ECS singleton - we get buffers from MonoBehaviour
        }

        protected override void OnDestroy()
        {
            // Dispose staging arrays
            if (_stagingAllocated)
            {
                if (_stagingCounts.IsCreated)
                    _stagingCounts.Dispose();
                if (_stagingEntries.IsCreated)
                    _stagingEntries.Dispose();
            }

            // Dispose double-buffer blobs
            if (SystemAPI.ManagedAPI.TryGetSingleton<VisibilityReadbackBuffers>(out var buffers))
            {
                buffers.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            // Get runtime from MonoBehaviour
            var behaviour = VisibilitySystemBehaviour.Instance;
            if (behaviour == null || !behaviour.IsReady)
                return;

            var runtime = behaviour.Runtime;
            var config = behaviour.Config;

            // Get or create managed buffer reference
            if (!SystemAPI.ManagedAPI.TryGetSingleton<VisibilityReadbackBuffers>(out var buffers))
            {
                InitializeDoubleBuffers(config.MaxVisiblePerGroup);
                return;
            }

            // Check if config changed
            if (_maxVisiblePerGroup != config.MaxVisiblePerGroup)
            {
                // Reinitialize with new size
                _stagingAllocated = false;
                _maxVisiblePerGroup = config.MaxVisiblePerGroup;
            }

            // Ensure staging arrays are allocated
            EnsureStagingAllocated(buffers);

            // Check if previous request completed
            if (_requestsPending)
            {
                if (_countsRequest.done && _entriesRequest.done)
                {
                    _requestsPending = false;

                    if (!_countsRequest.hasError && !_entriesRequest.hasError)
                    {
                        // Copy from readback to staging
                        _countsRequest.GetData<int>().CopyTo(_stagingCounts);
                        _entriesRequest.GetData<VisibilityEntryGPU>().CopyTo(_stagingEntries);

                        // Write to pending blob and swap
                        WriteAndSwapBlobs(buffers, _pendingFrameNumber);
                    }
                    else
                    {
                        // Log error but continue (previous data still valid)
                        Debug.LogWarning("[VisibilityReadback] GPU readback failed, using stale data");
                    }
                }
            }

            // Request new readback if none pending
            if (!_requestsPending)
            {
                _countsRequest = AsyncGPUReadback.Request(runtime.VisibleCountsBuffer);
                _entriesRequest = AsyncGPUReadback.Request(runtime.VisibleEntitiesBuffer);
                _requestsPending = true;
                _pendingFrameNumber = UnityEngine.Time.frameCount;
            }
        }

        private void InitializeDoubleBuffers(int maxVisiblePerGroup)
        {
            _maxVisiblePerGroup = maxVisiblePerGroup;

            // Create the managed singleton for double-buffering
            var buffers = new VisibilityReadbackBuffers();
            buffers.Initialize(maxVisiblePerGroup);

            // Create entity for the managed component
            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, buffers);

            // Create the readback state singleton
            var stateEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(stateEntity, new VisibilityReadbackState
            {
                ActiveIndex = 0,
                PendingFrame = 0,
                ReadbackPending = false
            });

            // Create the query data singleton (initially invalid)
            var queryEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(queryEntity, new VisibilityQueryData
            {
                Results = buffers.Blobs[0],
                GroupCount = GPUConstants.MAX_GROUPS,
                FrameComputed = -1,
                IsValid = false
            });
        }

        private void EnsureStagingAllocated(VisibilityReadbackBuffers buffers)
        {
            if (_stagingAllocated)
                return;

            _stagingCounts = new NativeArray<int>(GPUConstants.MAX_GROUPS, Allocator.Persistent);
            _stagingEntries = new NativeArray<VisibilityEntryGPU>(buffers.TotalCapacity, Allocator.Persistent);
            _stagingAllocated = true;
        }

        /// <summary>
        /// Core double-buffering logic.
        /// Writes readback data to the pending blob, then swaps active/pending.
        /// </summary>
        private void WriteAndSwapBlobs(VisibilityReadbackBuffers buffers, int frameNumber)
        {
            // Get current state
            var state = SystemAPI.GetSingleton<VisibilityReadbackState>();
            int pendingIndex = 1 - state.ActiveIndex;

            // Rebuild the pending blob
            if (buffers.Blobs[pendingIndex].IsCreated)
                buffers.Blobs[pendingIndex].Dispose();

            buffers.Blobs[pendingIndex] = CreateBlobFromStagingData(buffers);

            // Swap active index
            state.ActiveIndex = pendingIndex;
            state.PendingFrame = frameNumber;
            state.ReadbackPending = false;
            SystemAPI.SetSingleton(state);

            // Update query data singleton
            var queryData = SystemAPI.GetSingleton<VisibilityQueryData>();
            queryData.Results = buffers.Blobs[pendingIndex];
            queryData.FrameComputed = frameNumber;
            queryData.IsValid = true;
            SystemAPI.SetSingleton(queryData);
        }

        private BlobAssetReference<VisibilityResultsBlob> CreateBlobFromStagingData(
            VisibilityReadbackBuffers buffers)
        {
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<VisibilityResultsBlob>();

            // Allocate arrays
            var offsets = builder.Allocate(ref root.GroupOffsets, GPUConstants.MAX_GROUPS);
            var counts = builder.Allocate(ref root.GroupCounts, GPUConstants.MAX_GROUPS);
            var entries = builder.Allocate(ref root.Entries, buffers.TotalCapacity);

            // Copy counts and compute offsets
            for (int g = 0; g < GPUConstants.MAX_GROUPS; g++)
            {
                offsets[g] = g * buffers.MaxVisiblePerGroup;
                counts[g] = _stagingCounts[g];
            }

            // Copy entries
            for (int i = 0; i < buffers.TotalCapacity; i++)
            {
                entries[i] = _stagingEntries[i];
            }

            return builder.CreateBlobAssetReference<VisibilityResultsBlob>(Allocator.Persistent);
        }
    }

    /// <summary>
    /// Double-buffer container for visibility readback results.
    /// </summary>
    public class VisibilityReadbackBuffers : IComponentData
    {
        public BlobAssetReference<VisibilityResultsBlob>[] Blobs { get; private set; }
        public int MaxVisiblePerGroup { get; private set; }
        public int TotalCapacity => GPUConstants.MAX_GROUPS * MaxVisiblePerGroup;

        public void Initialize(int maxVisiblePerGroup)
        {
            MaxVisiblePerGroup = maxVisiblePerGroup;
            Blobs = new BlobAssetReference<VisibilityResultsBlob>[2];

            // Create initial empty blobs
            for (int i = 0; i < 2; i++)
            {
                using var builder = new BlobBuilder(Allocator.Temp);
                ref var root = ref builder.ConstructRoot<VisibilityResultsBlob>();
                builder.Allocate(ref root.GroupOffsets, GPUConstants.MAX_GROUPS);
                builder.Allocate(ref root.GroupCounts, GPUConstants.MAX_GROUPS);
                builder.Allocate(ref root.Entries, TotalCapacity);
                Blobs[i] = builder.CreateBlobAssetReference<VisibilityResultsBlob>(Allocator.Persistent);
            }
        }

        public void Dispose()
        {
            if (Blobs != null)
            {
                for (int i = 0; i < Blobs.Length; i++)
                {
                    if (Blobs[i].IsCreated)
                    {
                        Blobs[i].Dispose();
                        Blobs[i] = default;
                    }
                }
            }
        }
    }
}
