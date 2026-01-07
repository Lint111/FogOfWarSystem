using UnityEngine;
using System.IO;

namespace FogOfWar.Visibility.Editor.SDFBaking
{
    /// <summary>
    /// ScriptableObject configuration for automatic SDF island baking.
    /// Create via Assets > Create > FogOfWar > SDF Bake Config.
    /// </summary>
    [CreateAssetMenu(fileName = "SDFBakeConfig", menuName = "FogOfWar/SDF Bake Config")]
    public class SDFBakeConfig : ScriptableObject
    {
        [Header("Bake Triggers")]
        [Tooltip("Automatically bake dirty islands before entering play mode")]
        public bool BakeOnPlayMode = true;

        [Tooltip("Automatically queue bake after changes (with debounce)")]
        public bool BakeOnDirtyDebounced = true;

        [Tooltip("Debounce duration in seconds after last change before auto-bake")]
        [Range(30f, 300f)]
        public float DebounceDuration = 120f;

        [Header("Output Settings")]
        [Tooltip("Default SDF texture resolution (per axis)")]
        public SDFResolution DefaultResolution = SDFResolution.Resolution64;

        [Tooltip("Output folder for baked SDF textures (relative to Assets/)")]
        public string OutputPath = "FogOfWar/BakedSDFs";

        [Tooltip("Naming pattern for output files. {0}=IslandName, {1}=Resolution")]
        public string NamingPattern = "{0}_SDF_{1}";

        [Header("Baking Settings")]
        [Tooltip("Number of sign passes during SDF generation (higher = more accurate, slower)")]
        [Range(1, 4)]
        public int SignPassCount = 1;

        [Tooltip("Distance threshold for SDF generation")]
        [Range(0.1f, 1f)]
        public float Threshold = 0.5f;

        [Tooltip("Padding around mesh bounds (as fraction of size)")]
        [Range(0f, 0.5f)]
        public float BoundsPadding = 0.1f;

        [Header("Performance")]
        [Tooltip("Use background coroutine for baking (recommended)")]
        public bool UseBackgroundBaking = true;

        [Tooltip("Maximum concurrent bake operations")]
        [Range(1, 4)]
        public int MaxConcurrentBakes = 1;

        /// <summary>
        /// Gets the full output path (Assets/...)
        /// </summary>
        public string FullOutputPath => Path.Combine("Assets", OutputPath);

        /// <summary>
        /// Gets the resolution as integer.
        /// </summary>
        public int ResolutionInt => (int)DefaultResolution;

        /// <summary>
        /// Generates the output filename for an island.
        /// </summary>
        public string GetOutputFilename(string islandName, int? overrideResolution = null)
        {
            int res = overrideResolution ?? ResolutionInt;
            return string.Format(NamingPattern, islandName, res) + ".asset";
        }

        /// <summary>
        /// Gets the full output path for an island texture.
        /// </summary>
        public string GetOutputPath(string islandName, int? overrideResolution = null)
        {
            return Path.Combine(FullOutputPath, GetOutputFilename(islandName, overrideResolution));
        }

        /// <summary>
        /// Ensures the output directory exists.
        /// </summary>
        public void EnsureOutputDirectory()
        {
            string fullPath = FullOutputPath;
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Sanitize output path
            OutputPath = OutputPath.TrimStart('/').TrimEnd('/');
            if (string.IsNullOrWhiteSpace(OutputPath))
            {
                OutputPath = "FogOfWar/BakedSDFs";
            }

            // Validate naming pattern has placeholders
            if (!NamingPattern.Contains("{0}"))
            {
                Debug.LogWarning("[SDFBakeConfig] NamingPattern should contain {0} for island name", this);
            }
        }
#endif

        /// <summary>
        /// Gets or creates the default config instance.
        /// </summary>
        public static SDFBakeConfig GetOrCreateDefault()
        {
            // Try to find existing config
            var guids = UnityEditor.AssetDatabase.FindAssets("t:SDFBakeConfig");
            if (guids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                return UnityEditor.AssetDatabase.LoadAssetAtPath<SDFBakeConfig>(path);
            }

            // Create default config
            var config = CreateInstance<SDFBakeConfig>();

            string configPath = "Assets/FogOfWar/SDFBakeConfig.asset";
            string directory = Path.GetDirectoryName(configPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            UnityEditor.AssetDatabase.CreateAsset(config, configPath);
            UnityEditor.AssetDatabase.SaveAssets();

            Debug.Log($"[SDFBakeConfig] Created default config at {configPath}");
            return config;
        }
    }

    /// <summary>
    /// SDF texture resolution options.
    /// </summary>
    public enum SDFResolution
    {
        Resolution32 = 32,
        Resolution64 = 64,
        Resolution128 = 128,
        Resolution256 = 256
    }
}
