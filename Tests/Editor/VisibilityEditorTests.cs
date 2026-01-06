using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using FogOfWar.Visibility.GPU;

namespace FogOfWar.Visibility.Editor.Tests
{
    /// <summary>
    /// Editor-only unit tests for visibility system.
    /// </summary>
    public class VisibilityEditorTests
    {
        /// <summary>
        /// Path to Common.hlsl relative to package root.
        /// </summary>
        private const string CommonHlslPath = "Packages/com.fogofwar.visibility/Runtime/Shaders/Visibility/Common.hlsl";

        [Test]
        public void GPUConstants_MatchHLSLDefines()
        {
            // Read Common.hlsl
            string hlslPath = Path.GetFullPath(CommonHlslPath);
            Assert.IsTrue(File.Exists(hlslPath), $"Common.hlsl not found at: {hlslPath}");

            string hlslContent = File.ReadAllText(hlslPath);
            var hlslDefines = ParseHLSLDefines(hlslContent);

            // Validate each C# constant matches HLSL
            AssertConstantMatch(hlslDefines, "MAX_GROUPS", GPUConstants.MAX_GROUPS);
            AssertConstantMatch(hlslDefines, "MAX_ISLANDS", GPUConstants.MAX_ISLANDS);
            AssertConstantMatch(hlslDefines, "MAX_RAY_STEPS", GPUConstants.MAX_RAY_STEPS);
            AssertConstantMatchFloat(hlslDefines, "VISIBILITY_THRESHOLD", GPUConstants.VISIBILITY_THRESHOLD);
            AssertConstantMatchFloat(hlslDefines, "OCCLUSION_THRESHOLD", GPUConstants.OCCLUSION_THRESHOLD);

            // Thread group sizes
            AssertConstantMatch(hlslDefines, "THREAD_GROUP_1D", GPUConstants.THREAD_GROUP_1D);
            AssertConstantMatch(hlslDefines, "THREAD_GROUP_3D_X", GPUConstants.THREAD_GROUP_3D_X);
            AssertConstantMatch(hlslDefines, "THREAD_GROUP_3D_Y", GPUConstants.THREAD_GROUP_3D_Y);
            AssertConstantMatch(hlslDefines, "THREAD_GROUP_3D_Z", GPUConstants.THREAD_GROUP_3D_Z);

            // Vision types
            AssertConstantMatch(hlslDefines, "VISION_SPHERE", GPUConstants.VISION_SPHERE);
            AssertConstantMatch(hlslDefines, "VISION_SPHERE_CONE", GPUConstants.VISION_SPHERE_CONE);
            AssertConstantMatch(hlslDefines, "VISION_DUAL_SPHERE", GPUConstants.VISION_DUAL_SPHERE);
        }

        [Test]
        public void GPUConstants_AllHLSLDefinesHaveCSharpEquivalent()
        {
            string hlslPath = Path.GetFullPath(CommonHlslPath);
            Assert.IsTrue(File.Exists(hlslPath), $"Common.hlsl not found at: {hlslPath}");

            string hlslContent = File.ReadAllText(hlslPath);
            var hlslDefines = ParseHLSLDefines(hlslContent);

            // List of defines that should have C# equivalents
            var requiredDefines = new[]
            {
                "MAX_GROUPS", "MAX_ISLANDS", "MAX_RAY_STEPS",
                "VISIBILITY_THRESHOLD", "OCCLUSION_THRESHOLD",
                "THREAD_GROUP_1D", "THREAD_GROUP_3D_X", "THREAD_GROUP_3D_Y", "THREAD_GROUP_3D_Z",
                "VISION_SPHERE", "VISION_SPHERE_CONE", "VISION_DUAL_SPHERE"
            };

            foreach (var define in requiredDefines)
            {
                Assert.IsTrue(hlslDefines.ContainsKey(define),
                    $"HLSL Common.hlsl missing required #define: {define}");
            }
        }

        private Dictionary<string, string> ParseHLSLDefines(string hlslContent)
        {
            var defines = new Dictionary<string, string>();
            // Match: #define NAME VALUE (with optional comment)
            var regex = new Regex(@"#define\s+(\w+)\s+([\d.]+)", RegexOptions.Multiline);

            foreach (Match match in regex.Matches(hlslContent))
            {
                string name = match.Groups[1].Value;
                string value = match.Groups[2].Value;
                defines[name] = value;
            }

            return defines;
        }

        private void AssertConstantMatch(Dictionary<string, string> hlslDefines, string name, int expected)
        {
            Assert.IsTrue(hlslDefines.ContainsKey(name), $"HLSL missing #define {name}");
            Assert.IsTrue(int.TryParse(hlslDefines[name], out int actual),
                $"HLSL #define {name} = '{hlslDefines[name]}' is not a valid integer");
            Assert.AreEqual(expected, actual,
                $"Constant mismatch: C# GPUConstants.{name} = {expected}, HLSL #define {name} = {actual}");
        }

        private void AssertConstantMatchFloat(Dictionary<string, string> hlslDefines, string name, float expected)
        {
            Assert.IsTrue(hlslDefines.ContainsKey(name), $"HLSL missing #define {name}");
            Assert.IsTrue(float.TryParse(hlslDefines[name], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float actual),
                $"HLSL #define {name} = '{hlslDefines[name]}' is not a valid float");
            Assert.AreEqual(expected, actual, 0.0001f,
                $"Constant mismatch: C# GPUConstants.{name} = {expected}, HLSL #define {name} = {actual}");
        }
    }
}
