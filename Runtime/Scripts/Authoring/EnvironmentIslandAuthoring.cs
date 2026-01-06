using System.Collections;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using FogOfWar.Visibility.Components;
using FogOfWar.Visibility.Core;

namespace FogOfWar.Visibility.Authoring
{
    /// <summary>
    /// Authoring component for environment islands with SDF occlusion.
    /// The SDF texture is baked in local space - the island can be rotated/translated at runtime.
    /// Automatically registers the SDF texture with the visibility system at runtime.
    /// Creates an ECS entity at runtime for the visibility system to query.
    /// </summary>
    public class EnvironmentIslandAuthoring : MonoBehaviour
    {
        [Header("SDF Texture")]
        [Tooltip("3D SDF texture baked from VFX Graph SDF Bake Tool")]
        public Texture3D SDFTexture;

        [Header("Volume Settings")]
        [Tooltip("Local-space half-extents of the SDF volume (size/2). Should match the bake bounds.")]
        public Vector3 LocalHalfExtents = new Vector3(5f, 5f, 5f);

        [Tooltip("Island slot index (0-7). Each island needs a unique slot.")]
        [Range(0, 7)]
        public int TextureSlot = 0;

        [Header("Gizmos")]
        public bool ShowGizmos = true;
        public Color GizmoColor = new Color(0.5f, 0.5f, 1f, 0.3f);

        [Header("Debug")]
        [SerializeField] private bool _isRegistered;

        private Coroutine _registrationCoroutine;
        private Entity _runtimeEntity = Entity.Null;

        private void OnValidate()
        {
            // Auto-detect half-extents from mesh bounds if available
            if (LocalHalfExtents == Vector3.zero)
            {
                var meshFilter = GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    LocalHalfExtents = meshFilter.sharedMesh.bounds.extents;
                }
            }
        }

        private void Start()
        {
            TryRegister();
        }

        private void OnEnable()
        {
            if (!_isRegistered)
                TryRegister();
        }

        private void OnDisable()
        {
            if (_registrationCoroutine != null)
            {
                StopCoroutine(_registrationCoroutine);
                _registrationCoroutine = null;
            }
            Unregister();
        }

        private void LateUpdate()
        {
            // Keep runtime entity transform in sync with GameObject
            if (_runtimeEntity == Entity.Null)
                return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;

            var em = world.EntityManager;
            if (!em.Exists(_runtimeEntity))
            {
                _runtimeEntity = Entity.Null;
                return;
            }

            // Update LocalToWorld to match current transform
            em.SetComponentData(_runtimeEntity, new LocalToWorld
            {
                Value = transform.localToWorldMatrix
            });
        }

        private void TryRegister()
        {
            if (_isRegistered)
                return;

            if (SDFTexture == null)
            {
                Debug.LogWarning($"[EnvironmentIslandAuthoring] No SDF texture assigned on {gameObject.name}");
                return;
            }

            var system = VisibilitySystemBehaviour.Instance;
            if (system != null && system.IsReady)
            {
                // Register SDF texture with runtime
                system.RegisterIslandSDF(TextureSlot, SDFTexture);

                // Create ECS entity for the island (if not using subscene baking)
                CreateRuntimeEntity();

                _isRegistered = true;
                Debug.Log($"[EnvironmentIslandAuthoring] Registered SDF texture '{SDFTexture.name}' for slot {TextureSlot} on {gameObject.name}");
            }
            else
            {
                // Start coroutine to wait for system
                if (_registrationCoroutine == null)
                {
                    _registrationCoroutine = StartCoroutine(WaitForSystemReady());
                }
            }
        }

        private void CreateRuntimeEntity()
        {
            // Skip if we already have an entity or there's no world
            if (_runtimeEntity != Entity.Null)
                return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;

            var em = world.EntityManager;

            // Check if an entity already exists for this island slot (from subscene baking)
            // If so, don't create a duplicate
            var query = em.CreateEntityQuery(typeof(EnvironmentIslandDefinition));
            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            foreach (var entity in entities)
            {
                var def = em.GetComponentData<EnvironmentIslandDefinition>(entity);
                if (def.TextureIndex == TextureSlot)
                {
                    Debug.Log($"[EnvironmentIslandAuthoring] Found existing ECS entity for slot {TextureSlot}, skipping runtime creation");
                    entities.Dispose();
                    return;
                }
            }
            entities.Dispose();

            // Create new entity with island components
            _runtimeEntity = em.CreateEntity();
            em.SetName(_runtimeEntity, $"Island_{TextureSlot}_Runtime");

            // Get texture resolution
            int3 resolution = new int3(64, 64, 64);
            if (SDFTexture != null)
            {
                resolution = new int3(SDFTexture.width, SDFTexture.height, SDFTexture.depth);
            }

            // Add island definition
            em.AddComponentData(_runtimeEntity, new EnvironmentIslandDefinition
            {
                LocalHalfExtents = LocalHalfExtents,
                TextureResolution = resolution,
                TextureIndex = TextureSlot,
                IsLoaded = true // Runtime-created entities have textures loaded
            });

            em.AddComponent<EnvironmentIslandTag>(_runtimeEntity);

            // Add transform components
            em.AddComponentData(_runtimeEntity, new LocalToWorld
            {
                Value = transform.localToWorldMatrix
            });

            Debug.Log($"[EnvironmentIslandAuthoring] Created runtime ECS entity for island slot {TextureSlot}");
        }

        private void DestroyRuntimeEntity()
        {
            if (_runtimeEntity == Entity.Null)
                return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated && world.EntityManager.Exists(_runtimeEntity))
            {
                world.EntityManager.DestroyEntity(_runtimeEntity);
                Debug.Log($"[EnvironmentIslandAuthoring] Destroyed runtime ECS entity for island slot {TextureSlot}");
            }

            _runtimeEntity = Entity.Null;
        }

        private IEnumerator WaitForSystemReady()
        {
            // Wait until the visibility system is ready
            yield return new WaitUntil(() =>
                VisibilitySystemBehaviour.Instance?.IsReady ?? false);

            if (SDFTexture != null && !_isRegistered)
            {
                VisibilitySystemBehaviour.Instance.RegisterIslandSDF(TextureSlot, SDFTexture);

                // Create ECS entity for the island
                CreateRuntimeEntity();

                _isRegistered = true;
                Debug.Log($"[EnvironmentIslandAuthoring] Registered SDF texture '{SDFTexture.name}' for slot {TextureSlot} on {gameObject.name} (deferred)");
            }

            _registrationCoroutine = null;
        }

        private void Unregister()
        {
            if (!_isRegistered)
                return;

            // Destroy runtime ECS entity first
            DestroyRuntimeEntity();

            var system = VisibilitySystemBehaviour.Instance;
            if (system != null && system.IsReady)
            {
                system.UnregisterIslandSDF(TextureSlot);
            }

            _isRegistered = false;
        }

        private void OnDrawGizmos()
        {
            if (!ShowGizmos) return;

            Gizmos.color = GizmoColor;
            Gizmos.matrix = transform.localToWorldMatrix;

            // Draw local-space bounding box
            Gizmos.DrawWireCube(Vector3.zero, LocalHalfExtents * 2f);

            Gizmos.matrix = Matrix4x4.identity;
        }

        private void OnDrawGizmosSelected()
        {
            if (!ShowGizmos) return;

            // Draw solid with less alpha when selected
            Gizmos.color = new Color(GizmoColor.r, GizmoColor.g, GizmoColor.b, 0.1f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(Vector3.zero, LocalHalfExtents * 2f);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }

    public class EnvironmentIslandBaker : Baker<EnvironmentIslandAuthoring>
    {
        public override void Bake(EnvironmentIslandAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            // Get texture resolution (or use default)
            int3 resolution = new int3(64, 64, 64);
            if (authoring.SDFTexture != null)
            {
                resolution = new int3(
                    authoring.SDFTexture.width,
                    authoring.SDFTexture.height,
                    authoring.SDFTexture.depth);
            }

            AddComponent(entity, new EnvironmentIslandDefinition
            {
                LocalHalfExtents = authoring.LocalHalfExtents,
                TextureResolution = resolution,
                TextureIndex = authoring.TextureSlot,
                IsLoaded = authoring.SDFTexture != null
            });

            AddComponent(entity, new EnvironmentIslandTag());
        }
    }
}
