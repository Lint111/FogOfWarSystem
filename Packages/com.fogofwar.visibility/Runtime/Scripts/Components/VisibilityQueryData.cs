using Unity.Entities;
using Unity.Collections;
using FogOfWar.Visibility.GPU;

namespace FogOfWar.Visibility.Components
{
    /// <summary>
    /// Blob asset containing per-group visibility results.
    /// This is the data that gets populated from GPU readback.
    /// </summary>
    public struct VisibilityResultsBlob
    {
        /// <summary>
        /// Start offset into entries array for each group.
        /// Size: MAX_GROUPS (8)
        /// </summary>
        public BlobArray<int> GroupOffsets;

        /// <summary>
        /// Number of visible entries for each group.
        /// Size: MAX_GROUPS (8)
        /// </summary>
        public BlobArray<int> GroupCounts;

        /// <summary>
        /// Flat array of all visibility entries.
        /// Entries for group N start at GroupOffsets[N] and have GroupCounts[N] entries.
        /// Size: MAX_GROUPS * MaxVisiblePerGroup
        /// </summary>
        public BlobArray<VisibilityEntryGPU> Entries;
    }

    /// <summary>
    /// Singleton component providing access to visibility query results.
    /// Updated each frame by VisibilityReadbackSystem.
    /// </summary>
    public struct VisibilityQueryData : IComponentData
    {
        /// <summary>
        /// Reference to the blob containing visibility results.
        /// </summary>
        public BlobAssetReference<VisibilityResultsBlob> Results;

        /// <summary>
        /// Number of active vision groups (1-8).
        /// </summary>
        public byte GroupCount;

        /// <summary>
        /// Frame number when this data was computed (GPU side).
        /// There may be 1-2 frames of latency due to async readback.
        /// </summary>
        public int FrameComputed;

        /// <summary>
        /// True if the results are valid and can be queried.
        /// False during initial frames before first readback completes.
        /// </summary>
        public bool IsValid;
    }

    /// <summary>
    /// Blob asset containing vision group definitions.
    /// Set up at game initialization.
    /// </summary>
    public struct VisionGroupRegistryBlob
    {
        public BlobArray<VisionGroupDefinition> Groups;
    }

    /// <summary>
    /// Definition of a single vision group.
    /// </summary>
    public struct VisionGroupDefinition
    {
        /// <summary>
        /// Display name for debugging (e.g., "Player", "EnemyRed").
        /// </summary>
        public FixedString32Bytes Name;

        /// <summary>
        /// Group ID (0-7).
        /// </summary>
        public byte GroupId;

        /// <summary>
        /// Bitmask of which groups this group can see by default.
        /// </summary>
        public byte DefaultVisibilityMask;

        /// <summary>
        /// Default maximum view distance for units in this group.
        /// </summary>
        public float DefaultViewDistance;
    }

    /// <summary>
    /// Singleton component holding the vision group registry.
    /// </summary>
    public struct VisionGroupRegistry : IComponentData
    {
        public BlobAssetReference<VisionGroupRegistryBlob> Blob;
    }

    /// <summary>
    /// Internal singleton for double-buffering readback blobs.
    /// Not meant for direct use by game systems.
    /// [C3 FIX] Implements double-buffered blob pattern to avoid GC pressure.
    /// </summary>
    public struct VisibilityReadbackState : IComponentData
    {
        /// <summary>
        /// Index of the currently active blob (0 or 1).
        /// Game systems read from Blobs[ActiveIndex].
        /// </summary>
        public int ActiveIndex;

        /// <summary>
        /// Frame number of pending readback request.
        /// </summary>
        public int PendingFrame;

        /// <summary>
        /// True if a readback request is in flight.
        /// </summary>
        public bool ReadbackPending;
    }

    /// <summary>
    /// Managed class holding the double-buffered blob references.
    /// [C3 FIX] Two pre-allocated blobs that swap each frame.
    /// </summary>
    public class VisibilityReadbackBuffers : IComponentData
    {
        /// <summary>
        /// Two pre-allocated blob assets for double-buffering.
        /// Index 0 or 1, swapped each frame readback completes.
        /// </summary>
        public BlobAssetReference<VisibilityResultsBlob>[] Blobs;

        /// <summary>
        /// Maximum visible entities per group.
        /// </summary>
        public int MaxVisiblePerGroup;

        /// <summary>
        /// Total capacity: MAX_GROUPS * MaxVisiblePerGroup.
        /// </summary>
        public int TotalCapacity;

        public VisibilityReadbackBuffers()
        {
            Blobs = new BlobAssetReference<VisibilityResultsBlob>[2];
            MaxVisiblePerGroup = 1024;
            TotalCapacity = GPUConstants.MAX_GROUPS * MaxVisiblePerGroup;
        }

        /// <summary>
        /// Allocates both blob buffers. Call once at initialization.
        /// </summary>
        public void Initialize(int maxVisiblePerGroup = 1024)
        {
            MaxVisiblePerGroup = maxVisiblePerGroup;
            TotalCapacity = GPUConstants.MAX_GROUPS * maxVisiblePerGroup;

            // Pre-allocate both blobs
            Blobs[0] = CreateEmptyBlob();
            Blobs[1] = CreateEmptyBlob();
        }

        /// <summary>
        /// Disposes both blob buffers. Call at shutdown.
        /// </summary>
        public void Dispose()
        {
            if (Blobs[0].IsCreated)
            {
                Blobs[0].Dispose();
                Blobs[0] = default;
            }
            if (Blobs[1].IsCreated)
            {
                Blobs[1].Dispose();
                Blobs[1] = default;
            }
        }

        private BlobAssetReference<VisibilityResultsBlob> CreateEmptyBlob()
        {
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<VisibilityResultsBlob>();

            // Allocate arrays
            var offsets = builder.Allocate(ref root.GroupOffsets, GPUConstants.MAX_GROUPS);
            var counts = builder.Allocate(ref root.GroupCounts, GPUConstants.MAX_GROUPS);
            var entries = builder.Allocate(ref root.Entries, TotalCapacity);

            // Initialize offsets (each group gets MaxVisiblePerGroup slots)
            for (int i = 0; i < GPUConstants.MAX_GROUPS; i++)
            {
                offsets[i] = i * MaxVisiblePerGroup;
                counts[i] = 0;
            }

            // Initialize entries to default
            for (int i = 0; i < TotalCapacity; i++)
            {
                entries[i] = default;
            }

            return builder.CreateBlobAssetReference<VisibilityResultsBlob>(Allocator.Persistent);
        }
    }
}
