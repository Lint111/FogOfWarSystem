using System;
using UnityEngine;
using FogOfWar.Visibility.Configuration;

namespace FogOfWar.Visibility.Core
{
    /// <summary>
    /// MonoBehaviour that manages the visibility system lifecycle.
    /// Add this to a GameObject in your scene and assign the config asset.
    ///
    /// This is the main entry point for the visibility system.
    /// ECS systems and other components access the Runtime through Instance.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [AddComponentMenu("FogOfWar/Visibility System")]
    public class VisibilitySystemBehaviour : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField]
        [Tooltip("Reference to the VisibilitySystemConfig asset")]
        private VisibilitySystemConfig _config;

        /// <summary>
        /// The active instance of the visibility system.
        /// Scene-local: recreates when scene reloads.
        /// </summary>
        public static VisibilitySystemBehaviour Instance { get; private set; }

        /// <summary>
        /// The runtime state and GPU resources.
        /// Null if not initialized or disposed.
        /// </summary>
        public VisibilitySystemRuntime Runtime { get; private set; }

        /// <summary>
        /// True if the system is ready to use.
        /// </summary>
        public bool IsReady => Runtime?.IsInitialized ?? false;

        /// <summary>
        /// The configuration asset.
        /// </summary>
        public VisibilitySystemConfig Config => _config;

        // ===== Lifecycle =====

        private void OnEnable()
        {
            // Scene-local: allow replacement when scene reloads
            Instance = this;

            if (_config == null)
            {
                Debug.LogError("[VisibilitySystemBehaviour] No config assigned! Please assign a VisibilitySystemConfig asset.");
                return;
            }

            if (!_config.IsValid)
            {
                Debug.LogError("[VisibilitySystemBehaviour] Config is invalid - some shaders are missing.");
                return;
            }

            Runtime = new VisibilitySystemRuntime(_config);
            Runtime.Initialize();

            Debug.Log("[VisibilitySystemBehaviour] Enabled and initialized");
        }

        private void OnDisable()
        {
            Runtime?.Dispose();
            Runtime = null;

            if (Instance == this)
                Instance = null;

            Debug.Log("[VisibilitySystemBehaviour] Disabled and disposed");
        }

        // ===== Public API =====

        /// <summary>
        /// Registers an island SDF texture at the specified slot.
        /// Call this from EnvironmentIslandAuthoring or similar components.
        /// </summary>
        /// <param name="slot">Texture slot index (0-7)</param>
        /// <param name="texture">The SDF texture, or null to clear</param>
        public void RegisterIslandSDF(int slot, Texture3D texture)
        {
            if (Runtime == null)
            {
                Debug.LogWarning("[VisibilitySystemBehaviour] Cannot register island - runtime not initialized");
                return;
            }

            Runtime.SetIslandTexture(slot, texture);
        }

        /// <summary>
        /// Unregisters an island SDF texture.
        /// </summary>
        /// <param name="slot">Texture slot index (0-7)</param>
        public void UnregisterIslandSDF(int slot)
        {
            Runtime?.ClearIslandTexture(slot);
        }

        /// <summary>
        /// Subscribe to visibility change events.
        /// </summary>
        public void SubscribeToVisibilityEvents(
            Action<VisibilityChangeInfo> onChanged = null,
            Action<VisibilityChangeInfo> onSpotted = null,
            Action<VisibilityChangeInfo> onLost = null)
        {
            if (Runtime == null)
            {
                Debug.LogWarning("[VisibilitySystemBehaviour] Cannot subscribe - runtime not initialized");
                return;
            }

            if (onChanged != null)
                Runtime.OnVisibilityChanged += onChanged;
            if (onSpotted != null)
                Runtime.OnEntitySpotted += onSpotted;
            if (onLost != null)
                Runtime.OnEntityLost += onLost;
        }

        /// <summary>
        /// Unsubscribe from visibility change events.
        /// </summary>
        public void UnsubscribeFromVisibilityEvents(
            Action<VisibilityChangeInfo> onChanged = null,
            Action<VisibilityChangeInfo> onSpotted = null,
            Action<VisibilityChangeInfo> onLost = null)
        {
            if (Runtime == null)
                return;

            if (onChanged != null)
                Runtime.OnVisibilityChanged -= onChanged;
            if (onSpotted != null)
                Runtime.OnEntitySpotted -= onSpotted;
            if (onLost != null)
                Runtime.OnEntityLost -= onLost;
        }

        // ===== Editor =====

        private void OnValidate()
        {
            if (_config != null && !_config.IsValid)
            {
                Debug.LogWarning("[VisibilitySystemBehaviour] Config has missing shaders - check the asset");
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (_config == null)
                return;

            // Draw volume bounds
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            var bounds = _config.VolumeBounds;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
