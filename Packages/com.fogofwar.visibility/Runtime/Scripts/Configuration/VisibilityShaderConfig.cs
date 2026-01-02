using UnityEngine;

namespace FogOfWar.Visibility.Configuration
{
    /// <summary>
    /// ScriptableObject holding references to visibility compute shaders.
    /// Create an instance and assign the shaders in the editor.
    /// Reference this asset in your scene or via a bootstrap component.
    /// </summary>
    [CreateAssetMenu(fileName = "VisibilityShaderConfig", menuName = "FogOfWar/Visibility Shader Config")]
    public class VisibilityShaderConfig : ScriptableObject
    {
        [Header("Compute Shaders")]
        [Tooltip("PlayerFogVolume.compute - Generates player fog volume")]
        public ComputeShader PlayerFogVolumeShader;

        [Tooltip("VisibilityCheck.compute - Evaluates SDF visibility")]
        public ComputeShader VisibilityCheckShader;

        [Tooltip("RayMarchConfirm.compute - Ray marches to confirm visibility")]
        public ComputeShader RayMarchConfirmShader;

        [Header("Debug Shaders")]
        [Tooltip("Optional: Debug visualization material")]
        public Material DebugVisualizationMaterial;

        /// <summary>
        /// Validates that all required shaders are assigned.
        /// </summary>
        public bool IsValid =>
            PlayerFogVolumeShader != null &&
            VisibilityCheckShader != null &&
            RayMarchConfirmShader != null;

        private static VisibilityShaderConfig _instance;

        /// <summary>
        /// Gets the global shader config instance.
        /// Must be registered via RegisterGlobal() before use.
        /// </summary>
        public static VisibilityShaderConfig Instance => _instance;

        /// <summary>
        /// Registers this config as the global instance.
        /// Call this from a bootstrap component or during scene initialization.
        /// </summary>
        public void RegisterGlobal()
        {
            _instance = this;
        }
    }
}
