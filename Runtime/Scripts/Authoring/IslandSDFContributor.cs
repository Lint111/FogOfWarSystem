using System;
using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar.Visibility.Authoring
{
    /// <summary>
    /// Marks a GameObject hierarchy as an SDF island contributor.
    /// The system will automatically collect child meshes, detect changes,
    /// and rebake the SDF when dirty.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnvironmentIslandAuthoring))]
    [AddComponentMenu("FogOfWar/Island SDF Contributor")]
    public class IslandSDFContributor : MonoBehaviour
    {
        [Header("Island Identity")]
        [Tooltip("Unique identifier for this island. Auto-generated if empty.")]
        [SerializeField] private string _islandId;

        // Cached reference to paired authoring component
        private EnvironmentIslandAuthoring _cachedAuthoring;

        [Tooltip("Texture slot (0-7). Set to -1 for auto-assignment.")]
        [Range(-1, 7)]
        [SerializeField] private int _textureSlot = -1;

        [Header("Bake Settings")]
        [Tooltip("Override the default resolution for this island. 0 = use config default.")]
        [SerializeField] private int _overrideResolution = 0;

        [Tooltip("Include only these mesh filters. Empty = auto-collect all children.")]
        [SerializeField] private List<MeshFilter> _explicitMeshSources = new List<MeshFilter>();

        [Header("Status (Read-Only)")]
        [SerializeField] private bool _isDirty = true;
        [SerializeField] private string _lastBakeTime;
        [SerializeField] private Texture3D _bakedSDFTexture;

        // Cached hash for dirty detection
        private int _cachedGeometryHash;
        private int _cachedTransformHash;

        /// <summary>
        /// Unique identifier for this island.
        /// </summary>
        public string IslandId
        {
            get
            {
                if (string.IsNullOrEmpty(_islandId))
                {
                    _islandId = GenerateIslandId();
                }
                return _islandId;
            }
            set => _islandId = value;
        }

        /// <summary>
        /// Assigned texture slot (0-7), or -1 if not yet assigned.
        /// </summary>
        public int TextureSlot
        {
            get => _textureSlot;
            set => _textureSlot = Mathf.Clamp(value, -1, 7);
        }

        /// <summary>
        /// Override resolution for this island. 0 = use config default.
        /// </summary>
        public int OverrideResolution
        {
            get => _overrideResolution;
            set => _overrideResolution = Mathf.Clamp(value, 0, 256);
        }

        /// <summary>
        /// Whether this island needs rebaking.
        /// </summary>
        public bool IsDirty
        {
            get => _isDirty;
            set => _isDirty = value;
        }

        /// <summary>
        /// The currently baked SDF texture for this island.
        /// Setting this also updates EnvironmentIslandAuthoring if present.
        /// </summary>
        public Texture3D BakedSDFTexture
        {
            get => _bakedSDFTexture;
            set
            {
                _bakedSDFTexture = value;
                SyncToIslandAuthoring();
            }
        }

        /// <summary>
        /// Time of last successful bake.
        /// </summary>
        public string LastBakeTime
        {
            get => _lastBakeTime;
            set => _lastBakeTime = value;
        }

        /// <summary>
        /// Gets the paired EnvironmentIslandAuthoring component.
        /// </summary>
        public EnvironmentIslandAuthoring Authoring
        {
            get
            {
                CacheAuthoring();
                return _cachedAuthoring;
            }
        }

        /// <summary>
        /// Event fired when the island becomes dirty.
        /// </summary>
        public static event Action<IslandSDFContributor> OnIslandDirty;

        /// <summary>
        /// Event fired when an island is registered.
        /// </summary>
        public static event Action<IslandSDFContributor> OnIslandRegistered;

        /// <summary>
        /// Event fired when an island is unregistered.
        /// </summary>
        public static event Action<IslandSDFContributor> OnIslandUnregistered;

        private void Awake()
        {
            CacheAuthoring();
        }

        private void OnEnable()
        {
            CacheAuthoring();

            // Register with the tracking system
            OnIslandRegistered?.Invoke(this);

            // Calculate initial hash
            RecalculateHashes();
        }

        private void OnDisable()
        {
            OnIslandUnregistered?.Invoke(this);
        }

        private void OnValidate()
        {
            CacheAuthoring();

            if (string.IsNullOrEmpty(_islandId))
            {
                _islandId = GenerateIslandId();
            }
        }

        private void CacheAuthoring()
        {
            if (_cachedAuthoring == null)
            {
                _cachedAuthoring = GetComponent<EnvironmentIslandAuthoring>();
            }
        }

        /// <summary>
        /// Gets all mesh filters that contribute to this island's SDF.
        /// </summary>
        public MeshFilter[] GetMeshSources()
        {
            if (_explicitMeshSources != null && _explicitMeshSources.Count > 0)
            {
                // Filter out null entries
                var valid = new List<MeshFilter>();
                foreach (var mf in _explicitMeshSources)
                {
                    if (mf != null && mf.sharedMesh != null)
                        valid.Add(mf);
                }
                return valid.ToArray();
            }

            // Auto-collect from children
            return GetComponentsInChildren<MeshFilter>(false);
        }

        /// <summary>
        /// Calculates the combined world bounds of all mesh sources.
        /// </summary>
        public Bounds CalculateWorldBounds()
        {
            var meshFilters = GetMeshSources();
            if (meshFilters.Length == 0)
            {
                return new Bounds(transform.position, Vector3.one);
            }

            Bounds bounds = default;
            bool first = true;

            foreach (var mf in meshFilters)
            {
                if (mf == null || mf.sharedMesh == null) continue;

                var renderer = mf.GetComponent<Renderer>();
                if (renderer == null) continue;

                if (first)
                {
                    bounds = renderer.bounds;
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds;
        }

        /// <summary>
        /// Calculates the combined local-space bounds of all mesh sources,
        /// relative to this island's root transform.
        /// Returns bounds CENTERED AT ORIGIN (as required by runtime).
        /// </summary>
        public Bounds CalculateLocalBounds()
        {
            var meshFilters = GetMeshSources();
            if (meshFilters.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.one);
            }

            Matrix4x4 rootWorldToLocal = transform.worldToLocalMatrix;

            // Track max distance from origin in each axis direction
            Vector3 maxExtent = Vector3.zero;

            foreach (var mf in meshFilters)
            {
                if (mf == null || mf.sharedMesh == null) continue;

                // Transform mesh bounds to island's local space
                var mesh = mf.sharedMesh;
                var meshToLocal = rootWorldToLocal * mf.transform.localToWorldMatrix;

                // Transform all 8 corners of the mesh's local bounds
                var meshBounds = mesh.bounds;
                var min = meshBounds.min;
                var max = meshBounds.max;

                Vector3[] corners = new Vector3[8]
                {
                    new Vector3(min.x, min.y, min.z),
                    new Vector3(min.x, min.y, max.z),
                    new Vector3(min.x, max.y, min.z),
                    new Vector3(min.x, max.y, max.z),
                    new Vector3(max.x, min.y, min.z),
                    new Vector3(max.x, min.y, max.z),
                    new Vector3(max.x, max.y, min.z),
                    new Vector3(max.x, max.y, max.z)
                };

                foreach (var corner in corners)
                {
                    Vector3 localCorner = meshToLocal.MultiplyPoint3x4(corner);
                    // Track max distance from origin (not from mesh center)
                    maxExtent.x = Mathf.Max(maxExtent.x, Mathf.Abs(localCorner.x));
                    maxExtent.y = Mathf.Max(maxExtent.y, Mathf.Abs(localCorner.y));
                    maxExtent.z = Mathf.Max(maxExtent.z, Mathf.Abs(localCorner.z));
                }
            }

            // Return bounds centered at origin with extents covering all mesh points
            return new Bounds(Vector3.zero, maxExtent * 2f);
        }

        /// <summary>
        /// Checks if the island has changed since last hash calculation.
        /// </summary>
        public bool CheckForChanges()
        {
            int newGeometryHash = CalculateGeometryHash();
            int newTransformHash = CalculateTransformHash();

            bool changed = (newGeometryHash != _cachedGeometryHash) ||
                          (newTransformHash != _cachedTransformHash);

            if (changed && !_isDirty)
            {
                _isDirty = true;
                OnIslandDirty?.Invoke(this);
            }

            return changed;
        }

        /// <summary>
        /// Recalculates and caches the geometry and transform hashes.
        /// </summary>
        public void RecalculateHashes()
        {
            _cachedGeometryHash = CalculateGeometryHash();
            _cachedTransformHash = CalculateTransformHash();
        }

        /// <summary>
        /// Marks the island as clean (after successful bake).
        /// </summary>
        public void MarkClean()
        {
            _isDirty = false;
            _lastBakeTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            RecalculateHashes();
        }

        /// <summary>
        /// Syncs the baked SDF texture to EnvironmentIslandAuthoring on the same GameObject.
        /// </summary>
        private void SyncToIslandAuthoring()
        {
            CacheAuthoring();

            if (_cachedAuthoring != null && _bakedSDFTexture != null)
            {
                _cachedAuthoring.SDFTexture = _bakedSDFTexture;

                // Sync texture slot if auto-assigned
                if (_textureSlot >= 0)
                {
                    _cachedAuthoring.TextureSlot = _textureSlot;
                }

                // Sync bounds from calculated local bounds
                var bounds = CalculateLocalBounds();
                _cachedAuthoring.LocalHalfExtents = bounds.extents;

                #if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(_cachedAuthoring);
                #endif
            }
        }

        /// <summary>
        /// Forces the island to be marked dirty.
        /// </summary>
        public void MarkDirty()
        {
            if (!_isDirty)
            {
                _isDirty = true;
                OnIslandDirty?.Invoke(this);
            }
        }

        private string GenerateIslandId()
        {
            // Use GameObject name + scene path for uniqueness
            string sceneName = gameObject.scene.IsValid() ? gameObject.scene.name : "NoScene";
            return $"{sceneName}_{gameObject.name}_{GetInstanceID()}";
        }

        private int CalculateGeometryHash()
        {
            unchecked
            {
                int hash = 17;
                var meshFilters = GetMeshSources();

                foreach (var mf in meshFilters)
                {
                    if (mf == null || mf.sharedMesh == null) continue;

                    var mesh = mf.sharedMesh;
                    // Hash vertex count and bounds (fast approximation)
                    hash = hash * 31 + mesh.vertexCount;
                    hash = hash * 31 + mesh.bounds.size.GetHashCode();
                    hash = hash * 31 + mesh.GetInstanceID();
                }

                return hash;
            }
        }

        private int CalculateTransformHash()
        {
            unchecked
            {
                int hash = 17;
                var transforms = GetComponentsInChildren<Transform>(false);

                foreach (var t in transforms)
                {
                    // Combine position, rotation, scale
                    hash = hash * 31 + t.position.GetHashCode();
                    hash = hash * 31 + t.rotation.GetHashCode();
                    hash = hash * 31 + t.lossyScale.GetHashCode();
                }

                return hash;
            }
        }

        private void OnDrawGizmosSelected()
        {
            // Draw bounds
            var bounds = CalculateWorldBounds();

            Gizmos.color = _isDirty ? Color.yellow : Color.green;
            Gizmos.DrawWireCube(bounds.center, bounds.size);

            // Draw label
            #if UNITY_EDITOR
            UnityEditor.Handles.Label(bounds.center + Vector3.up * bounds.extents.y,
                $"{IslandId}\nSlot: {(_textureSlot >= 0 ? _textureSlot.ToString() : "Auto")}\n{(_isDirty ? "DIRTY" : "Clean")}");
            #endif
        }
    }
}
