using UnityEngine;
using Unity.Mathematics;

namespace FogOfWar.Visibility.Configuration
{
    /// <summary>
    /// ScriptableObject holding visibility system configuration.
    /// Create an instance via Assets > Create > FogOfWar > Visibility System Config.
    /// Assign to VisibilitySystemBehaviour in your scene.
    /// </summary>
    [CreateAssetMenu(fileName = "VisibilitySystemConfig", menuName = "FogOfWar/Visibility System Config")]
    public class VisibilitySystemConfig : ScriptableObject
    {
        [Header("Compute Shaders")]
        [Tooltip("PlayerFogVolume.compute - Generates player fog volume")]
        public ComputeShader PlayerFogVolumeShader;

        [Tooltip("VisibilityCheck.compute - Evaluates SDF visibility")]
        public ComputeShader VisibilityCheckShader;

        [Tooltip("RayMarchConfirm.compute - Ray marches to confirm visibility")]
        public ComputeShader RayMarchConfirmShader;

        [Header("Volume Settings")]
        [Tooltip("Minimum corner of the fog volume in world space")]
        public Vector3 VolumeMin = new Vector3(-50, -10, -50);

        [Tooltip("Maximum corner of the fog volume in world space")]
        public Vector3 VolumeMax = new Vector3(50, 40, 50);

        [Tooltip("Resolution of the 3D fog volume texture (per axis)")]
        [Range(32, 256)]
        public int FogResolution = 128;

        [Header("Capacity Settings - Primary Parameters")]
        [Tooltip("Number of active vision groups (1-8)")]
        [Range(1, 8)]
        public int NumberOfGroups = 4;

        [Tooltip("Number of entities per group (units + seeables combined)")]
        public int EntitiesPerGroup = 512;

        [Header("Capacity Settings - Derived (Auto-calculated)")]
        [Tooltip("Maximum units per vision group (auto: ceil(EntitiesPerGroup / 2))")]
        public int MaxUnitsPerGroup => (EntitiesPerGroup + 1) / 2;

        [Tooltip("Maximum seeable entities in the scene (auto: EntitiesPerGroup * NumberOfGroups)")]
        public int MaxSeeables => EntitiesPerGroup * NumberOfGroups;

        [Tooltip("Maximum visibility candidates per group per frame (auto: EntitiesPerGroup * 2)")]
        public int MaxCandidatesPerGroup => EntitiesPerGroup * 2;

        [Tooltip("Maximum visible entities per group per frame (auto: EntitiesPerGroup)")]
        public int MaxVisiblePerGroup => EntitiesPerGroup;

        [Header("Player Settings")]
        [Tooltip("Vision group ID for the player (0-7)")]
        [Range(0, 7)]
        public int PlayerGroupId = 0;

        [Header("Performance")]
        [Tooltip("Use parallel per-group dispatch (8 parallel kernels per stage)")]
        public bool UseParallelDispatch = true;

        [Header("Temporal Smoothing")]
        [Tooltip("Visibility blend rate between frames (0.1-1.0). Higher = faster updates, lower = smoother transitions")]
        [Range(0.1f, 1.0f)]
        public float VisibilityBlendRate = 0.7f;

        [Tooltip("Passive dissipation rate for unseen areas (0.8-1.0). Higher = slower fade, lower = faster fade")]
        [Range(0.8f, 1.0f)]
        public float PassiveDissipationRate = 0.95f;

        [Header("Debug")]
        [Tooltip("Optional: Debug visualization material")]
        public Material DebugVisualizationMaterial;

        /// <summary>
        /// Validates that all required shaders are assigned.
        /// </summary>
        public bool IsValid =>
            PlayerFogVolumeShader != null &&
            VisibilityCheckShader != null &&
            RayMarchConfirmShader != null;

        /// <summary>
        /// Gets the volume bounds as a Unity Bounds struct.
        /// </summary>
        public Bounds VolumeBounds => new Bounds(
            (VolumeMin + VolumeMax) * 0.5f,
            VolumeMax - VolumeMin);

        /// <summary>
        /// Gets the volume min as float3.
        /// </summary>
        public float3 VolumeMinFloat3 => new float3(VolumeMin.x, VolumeMin.y, VolumeMin.z);

        /// <summary>
        /// Gets the volume max as float3.
        /// </summary>
        public float3 VolumeMaxFloat3 => new float3(VolumeMax.x, VolumeMax.y, VolumeMax.z);

        /// <summary>
        /// Total candidates buffer size (8 groups * MaxCandidatesPerGroup).
        /// </summary>
        public int TotalCandidates => FogOfWar.Visibility.GPU.GPUConstants.MAX_GROUPS * MaxCandidatesPerGroup;

        /// <summary>
        /// Validates that capacity settings can handle the configured load.
        /// Returns true if configuration is valid, false otherwise.
        /// </summary>
        public bool ValidateCapacity(out string errorMessage)
        {
            errorMessage = null;

            // Validate group count
            if (NumberOfGroups < 1 || NumberOfGroups > FogOfWar.Visibility.GPU.GPUConstants.MAX_GROUPS)
            {
                errorMessage = $"NumberOfGroups must be between 1 and {FogOfWar.Visibility.GPU.GPUConstants.MAX_GROUPS}";
                return false;
            }

            // Validate entities per group
            if (EntitiesPerGroup < 1)
            {
                errorMessage = "EntitiesPerGroup must be at least 1";
                return false;
            }

            // Check if player group ID is within active groups
            if (PlayerGroupId >= NumberOfGroups)
            {
                errorMessage = $"PlayerGroupId ({PlayerGroupId}) must be less than NumberOfGroups ({NumberOfGroups})";
                return false;
            }

            // Warn about performance implications
            int totalEntities = MaxSeeables;
            int totalUnits = MaxUnitsPerGroup * NumberOfGroups;

            if (totalEntities > 4096)
            {
                errorMessage = $"Warning: MaxSeeables ({totalEntities}) is very high. Performance may suffer.";
                return true; // Warning, not error
            }

            if (totalUnits > 2048)
            {
                errorMessage = $"Warning: Total units ({totalUnits}) is very high. Consider enabling UseParallelDispatch.";
                return true; // Warning, not error
            }

            return true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Validate capacity settings
            if (ValidateCapacity(out string error))
            {
                if (error != null && error.StartsWith("Warning:"))
                {
                    Debug.LogWarning($"[VisibilitySystemConfig] {error}", this);
                }
            }
            else
            {
                Debug.LogError($"[VisibilitySystemConfig] {error}", this);
            }

            // Ensure reasonable minimums
            if (EntitiesPerGroup < 32)
            {
                Debug.LogWarning($"[VisibilitySystemConfig] EntitiesPerGroup ({EntitiesPerGroup}) is very low. Minimum recommended: 32", this);
            }
        }
#endif
    }
}
