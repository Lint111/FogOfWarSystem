using NUnit.Framework;
using FogOfWar.Visibility.Components;

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
    }
}
