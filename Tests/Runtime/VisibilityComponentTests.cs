using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using FogOfWar.Visibility.Components;
using FogOfWar.Visibility.GPU;
using FogOfWar.Visibility.Query;

namespace FogOfWar.Visibility.Tests
{
    /// <summary>
    /// Unit tests for visibility components.
    /// </summary>
    public class VisibilityComponentTests
    {
        [Test]
        public void VisibleToGroups_IsVisibleToGroup_ReturnsCorrectValue()
        {
            var component = new VisibleToGroups { GroupMask = 0b00000101 }; // Groups 0 and 2

            Assert.IsTrue(component.IsVisibleToGroup(0), "Should be visible to group 0");
            Assert.IsFalse(component.IsVisibleToGroup(1), "Should not be visible to group 1");
            Assert.IsTrue(component.IsVisibleToGroup(2), "Should be visible to group 2");
            Assert.IsFalse(component.IsVisibleToGroup(7), "Should not be visible to group 7");
        }

        [Test]
        public void VisibleToGroups_EmptyMask_ReturnsAllFalse()
        {
            var component = new VisibleToGroups { GroupMask = 0 };

            for (int i = 0; i < 8; i++)
            {
                Assert.IsFalse(component.IsVisibleToGroup(i), $"Should not be visible to group {i}");
            }
        }

        [Test]
        public void VisibleToGroups_FullMask_ReturnsAllTrue()
        {
            var component = new VisibleToGroups { GroupMask = 0xFF };

            for (int i = 0; i < 8; i++)
            {
                Assert.IsTrue(component.IsVisibleToGroup(i), $"Should be visible to group {i}");
            }
        }

        [Test]
        public void VisionType_HasExpectedValues()
        {
            Assert.AreEqual(0, (byte)VisionType.Sphere);
            Assert.AreEqual(1, (byte)VisionType.SphereWithCone);
            Assert.AreEqual(2, (byte)VisionType.DualSphere);
        }

        [Test]
        public void IslandMembership_ForIsland_SetsSingleBit()
        {
            var membership = IslandMembership.ForIsland(3);
            Assert.AreEqual(0b00001000, membership.IslandMask);
            Assert.IsTrue(membership.IsInIsland(3));
            Assert.IsFalse(membership.IsInIsland(2));
        }

        [Test]
        public void IslandMembership_AddRemove_ModifiesMaskCorrectly()
        {
            var membership = IslandMembership.ForIsland(0);
            membership.AddToIsland(5);

            Assert.IsTrue(membership.IsInIsland(0));
            Assert.IsTrue(membership.IsInIsland(5));
            Assert.AreEqual(0b00100001, membership.IslandMask);

            membership.RemoveFromIsland(0);
            Assert.IsFalse(membership.IsInIsland(0));
            Assert.IsTrue(membership.IsInIsland(5));
        }

        [Test]
        public void IslandMembership_InvalidIndex_ThrowsException()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => IslandMembership.ForIsland(-1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => IslandMembership.ForIsland(16));
        }

        [Test]
        public void VisionGroupActive_EnableDisable_WorksCorrectly()
        {
            var active = VisionGroupActive.AllActive;
            Assert.AreEqual(8, active.ActiveCount);
            Assert.IsTrue(active.IsGroupActive(0));

            active.DisableGroup(3);
            Assert.IsFalse(active.IsGroupActive(3));
            Assert.AreEqual(7, active.ActiveCount);

            active.EnableGroup(3);
            Assert.IsTrue(active.IsGroupActive(3));
        }

        [Test]
        public void VisionGroupActive_OnlyGroup_SetsSingleBit()
        {
            var active = VisionGroupActive.OnlyGroup(2);
            Assert.IsTrue(active.IsGroupActive(2));
            Assert.IsFalse(active.IsGroupActive(0));
            Assert.AreEqual(1, active.ActiveCount);
        }
    }

    /// <summary>
    /// Tests for the visibility readback double-buffering system (C3 fix).
    /// </summary>
    public class VisibilityReadbackBufferTests
    {
        [Test]
        public void VisibilityReadbackBuffers_Initialize_CreatesTwoValidBlobs()
        {
            var buffers = new VisibilityReadbackBuffers();
            buffers.Initialize(512);

            Assert.IsTrue(buffers.Blobs[0].IsCreated, "First blob should be created");
            Assert.IsTrue(buffers.Blobs[1].IsCreated, "Second blob should be created");
            Assert.AreEqual(512, buffers.MaxVisiblePerGroup);
            Assert.AreEqual(GPUConstants.MAX_GROUPS * 512, buffers.TotalCapacity);

            buffers.Dispose();
        }

        [Test]
        public void VisibilityReadbackBuffers_Dispose_ReleasesBlobs()
        {
            var buffers = new VisibilityReadbackBuffers();
            buffers.Initialize(256);

            // Verify blobs are created before dispose
            Assert.IsTrue(buffers.Blobs[0].IsCreated, "First blob should exist before dispose");
            Assert.IsTrue(buffers.Blobs[1].IsCreated, "Second blob should exist before dispose");

            buffers.Dispose();

            // After dispose, the array references should show as not created
            Assert.IsFalse(buffers.Blobs[0].IsCreated, "First blob should be disposed");
            Assert.IsFalse(buffers.Blobs[1].IsCreated, "Second blob should be disposed");
        }

        [Test]
        public void VisibilityReadbackBuffers_BlobStructure_HasCorrectLayout()
        {
            var buffers = new VisibilityReadbackBuffers();
            buffers.Initialize(128);

            ref var blob = ref buffers.Blobs[0].Value;

            // Verify array sizes
            Assert.AreEqual(GPUConstants.MAX_GROUPS, blob.GroupOffsets.Length);
            Assert.AreEqual(GPUConstants.MAX_GROUPS, blob.GroupCounts.Length);
            Assert.AreEqual(GPUConstants.MAX_GROUPS * 128, blob.Entries.Length);

            // Verify offsets are spaced correctly
            for (int i = 0; i < GPUConstants.MAX_GROUPS; i++)
            {
                Assert.AreEqual(i * 128, blob.GroupOffsets[i], $"Group {i} offset incorrect");
            }

            buffers.Dispose();
        }
    }

    /// <summary>
    /// Tests for the blob-based visibility query methods.
    /// </summary>
    public class VisibilityQueryBlobTests
    {
        private BlobAssetReference<VisibilityResultsBlob> _testBlob;
        private VisibilityQueryData _queryData;

        [SetUp]
        public void SetUp()
        {
            // Create a test blob with known data
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<VisibilityResultsBlob>();

            var offsets = builder.Allocate(ref root.GroupOffsets, GPUConstants.MAX_GROUPS);
            var counts = builder.Allocate(ref root.GroupCounts, GPUConstants.MAX_GROUPS);
            var entries = builder.Allocate(ref root.Entries, GPUConstants.MAX_GROUPS * 16);

            // Set up group 0 with 3 visible entities
            offsets[0] = 0;
            counts[0] = 3;
            var e0 = new VisibilityEntryGPU { entityId = 100, distance = 5.0f };
            e0.VisibilityLevel = 2;
            entries[0] = e0;
            var e1 = new VisibilityEntryGPU { entityId = 101, distance = 10.0f };
            e1.VisibilityLevel = 1;
            entries[1] = e1;
            var e2 = new VisibilityEntryGPU { entityId = 102, distance = 20.0f };
            e2.VisibilityLevel = 0;
            entries[2] = e2;

            // Set up group 1 with 1 visible entity
            offsets[1] = 16;
            counts[1] = 1;
            var e3 = new VisibilityEntryGPU { entityId = 200, distance = 8.0f };
            e3.VisibilityLevel = 2;
            entries[16] = e3;

            // Other groups empty
            for (int i = 2; i < GPUConstants.MAX_GROUPS; i++)
            {
                offsets[i] = i * 16;
                counts[i] = 0;
            }

            _testBlob = builder.CreateBlobAssetReference<VisibilityResultsBlob>(Allocator.Persistent);

            _queryData = new VisibilityQueryData
            {
                Results = _testBlob,
                GroupCount = GPUConstants.MAX_GROUPS,
                FrameComputed = 100,
                IsValid = true
            };
        }

        [TearDown]
        public void TearDown()
        {
            if (_testBlob.IsCreated)
                _testBlob.Dispose();
        }

        [Test]
        public void GetVisibleCount_ReturnsCorrectCount()
        {
            Assert.AreEqual(3, VisibilityQuery.GetVisibleCount(_queryData, 0));
            Assert.AreEqual(1, VisibilityQuery.GetVisibleCount(_queryData, 1));
            Assert.AreEqual(0, VisibilityQuery.GetVisibleCount(_queryData, 2));
        }

        [Test]
        public void GetVisibleCount_InvalidGroup_ReturnsZero()
        {
            Assert.AreEqual(0, VisibilityQuery.GetVisibleCount(_queryData, -1));
            Assert.AreEqual(0, VisibilityQuery.GetVisibleCount(_queryData, 100));
        }

        [Test]
        public void IsEntityIdVisibleToGroup_FindsCorrectEntity()
        {
            Assert.IsTrue(VisibilityQuery.IsEntityIdVisibleToGroup(_queryData, 100, 0));
            Assert.IsTrue(VisibilityQuery.IsEntityIdVisibleToGroup(_queryData, 101, 0));
            Assert.IsFalse(VisibilityQuery.IsEntityIdVisibleToGroup(_queryData, 999, 0));
        }

        [Test]
        public void TryGetClosestVisible_FindsClosest()
        {
            Assert.IsTrue(VisibilityQuery.TryGetClosestVisible(_queryData, 0, out var entry));
            Assert.AreEqual(100, entry.entityId);
            Assert.AreEqual(5.0f, entry.distance, 0.001f);
        }

        [Test]
        public void TryGetVisibilityEntry_ReturnsCorrectEntry()
        {
            Assert.IsTrue(VisibilityQuery.TryGetVisibilityEntry(_queryData, 101, 0, out var entry));
            Assert.AreEqual(101, entry.entityId);
            Assert.AreEqual(10.0f, entry.distance, 0.001f);
            Assert.AreEqual(1, entry.VisibilityLevel);
        }

        [Test]
        public void GetVisibleEntry_ReturnsCorrectEntryByIndex()
        {
            var entry = VisibilityQuery.GetVisibleEntry(_queryData, 0, 2);
            Assert.AreEqual(102, entry.entityId);
            Assert.AreEqual(20.0f, entry.distance, 0.001f);
        }

        [Test]
        public void GetVisibleInRange_FiltersCorrectly()
        {
            var results = new NativeArray<VisibilityEntryGPU>(10, Allocator.Temp);

            int count = VisibilityQuery.GetVisibleInRange(_queryData, 0, 8.0f, 15.0f, results);

            Assert.AreEqual(1, count);
            Assert.AreEqual(101, results[0].entityId);

            results.Dispose();
        }

        [Test]
        public void QueryMethods_InvalidData_ReturnSafeDefaults()
        {
            var invalidQuery = new VisibilityQueryData { IsValid = false };

            Assert.AreEqual(0, VisibilityQuery.GetVisibleCount(invalidQuery, 0));
            Assert.IsFalse(VisibilityQuery.IsEntityIdVisibleToGroup(invalidQuery, 100, 0));
            Assert.IsFalse(VisibilityQuery.TryGetClosestVisible(invalidQuery, 0, out _));
        }
    }
}
