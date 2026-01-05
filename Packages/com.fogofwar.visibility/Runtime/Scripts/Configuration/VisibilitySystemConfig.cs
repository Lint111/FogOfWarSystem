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

        [Header("Capacity Settings")]
        [Tooltip("Maximum units per vision group")]
        public int MaxUnitsPerGroup = 256;

        [Tooltip("Maximum seeable entities in the scene")]
        public int MaxSeeables = 2048;

        [Tooltip("Maximum visibility candidates per group per frame")]
        public int MaxCandidatesPerGroup = 4096;

        [Tooltip("Maximum visible entities per group per frame")]
        public int MaxVisiblePerGroup = 1024;

        [Header("Player Settings")]
        [Tooltip("Vision group ID for the player (0-7)")]
        [Range(0, 7)]
        public int PlayerGroupId = 0;

        [Header("Performance")]
        [Tooltip("Use parallel per-group dispatch (8 parallel kernels per stage)")]
        public bool UseParallelDispatch = true;

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
    }
}
