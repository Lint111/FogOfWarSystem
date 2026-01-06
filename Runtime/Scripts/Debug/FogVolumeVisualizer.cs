using UnityEngine;
using FogOfWar.Visibility.Core;

namespace FogOfWar.Visibility.Debugging
{
    /// <summary>
    /// Debug visualizer for the 3D fog volume.
    /// Uses a child MeshRenderer for URP compatibility.
    /// Gets fog texture from VisibilitySystemBehaviour.Instance.Runtime.
    /// </summary>
    [ExecuteAlways]
    public class FogVolumeVisualizer : MonoBehaviour
    {
        [Header("Display Settings")]
        [Tooltip("Material for rendering the fog slice")]
        public Material VisualizationMaterial;

        [Tooltip("Height of the slice in world space")]
        public float SliceHeight = 0f;

        [Tooltip("Show the visualization")]
        public bool IsVisible = true;

        [Header("Colors")]
        public Color VisibleColor = new Color(0.2f, 0.8f, 0.2f, 0.4f);
        public Color HiddenColor = new Color(0.3f, 0.3f, 0.3f, 0.4f);

        [Header("Debug")]
        [Tooltip("Log status info every second")]
        public bool LogDebugInfo = false;

        private RenderTexture _fogVolume;
        private float _lastLogTime;
        private Bounds _volumeBounds;
        private bool _hasValidData;

        // Child object with MeshRenderer for URP-compatible rendering
        private GameObject _sliceQuad;
        private MeshRenderer _meshRenderer;
        private MeshFilter _meshFilter;
        private MaterialPropertyBlock _propertyBlock;

        private void OnEnable()
        {
            // Clean up any orphaned quads from previous sessions
            CleanupOrphanedQuads();
            CreateSliceQuad();
            _propertyBlock = new MaterialPropertyBlock();
        }

        private void OnDisable()
        {
            DestroySliceQuad();
        }

        private void OnDestroy()
        {
            DestroySliceQuad();
        }

        private void CleanupOrphanedQuads()
        {
            // Find and destroy any orphaned FogSliceQuad children
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name == "FogSliceQuad")
                {
                    if (Application.isPlaying)
                        Destroy(child.gameObject);
                    else
                        DestroyImmediate(child.gameObject);
                }
            }
        }

        private void Update()
        {
            // Try to get fog volume from Runtime
            TryGetFogVolume();

            // Update the quad
            UpdateSliceQuad();

            // Debug logging
            if (LogDebugInfo && Time.time - _lastLogTime > 1f)
            {
                _lastLogTime = Time.time;
            }
        }

        private void TryGetFogVolume()
        {
            _hasValidData = false;
            _fogVolume = null;

            var behaviour = VisibilitySystemBehaviour.Instance;
            if (behaviour == null || !behaviour.IsReady)
                return;

            var runtime = behaviour.Runtime;
            if (runtime.FogVolumeTexture != null && runtime.FogVolumeTexture.IsCreated())
            {
                _fogVolume = runtime.FogVolumeTexture;
                _volumeBounds = behaviour.Config.VolumeBounds;
                _hasValidData = true;
            }
        }

        private void CreateSliceQuad()
        {
            if (_sliceQuad != null)
                return;

            _sliceQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _sliceQuad.name = "FogSliceQuad";
            _sliceQuad.transform.SetParent(transform, false);
            _sliceQuad.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;

            // Remove collider
            var collider = _sliceQuad.GetComponent<Collider>();
            if (collider != null)
                DestroyImmediate(collider);

            _meshRenderer = _sliceQuad.GetComponent<MeshRenderer>();
            _meshFilter = _sliceQuad.GetComponent<MeshFilter>();

            // Disable shadows
            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
        }

        private void DestroySliceQuad()
        {
            if (_sliceQuad == null) return;
            if (Application.isPlaying)
                Destroy(_sliceQuad);
            else
                DestroyImmediate(_sliceQuad);

            _sliceQuad = null;
            _meshRenderer = null;
            _meshFilter = null;
        }

        private void UpdateSliceQuad()
        {
            if (_sliceQuad == null)
                return;

            // Toggle visibility
            bool shouldShow = IsVisible && _hasValidData && _fogVolume != null && VisualizationMaterial != null;
            if (_sliceQuad.activeSelf != shouldShow)
                _sliceQuad.SetActive(shouldShow);

            if (!shouldShow)
                return;

            // Assign material
            if (_meshRenderer.sharedMaterial != VisualizationMaterial)
                _meshRenderer.sharedMaterial = VisualizationMaterial;

            // Calculate normalized height for shader
            float normalizedHeight = (_volumeBounds.size.y > 0)
                ? (SliceHeight - _volumeBounds.min.y) / _volumeBounds.size.y
                : 0.5f;
            normalizedHeight = Mathf.Clamp01(normalizedHeight);

            // Update material properties via property block
            _propertyBlock.SetTexture("_FogVolume", _fogVolume);
            _propertyBlock.SetFloat("_SliceHeight", normalizedHeight);
            _propertyBlock.SetVector("_VolumeMin", _volumeBounds.min);
            _propertyBlock.SetVector("_VolumeMax", _volumeBounds.max);
            _propertyBlock.SetColor("_VisibleColor", VisibleColor);
            _propertyBlock.SetColor("_HiddenColor", HiddenColor);
            _meshRenderer.SetPropertyBlock(_propertyBlock);

            // Position and scale quad
            _sliceQuad.transform.position = new Vector3(
                _volumeBounds.center.x,
                SliceHeight + 0.02f, // Small offset to avoid z-fighting
                _volumeBounds.center.z
            );
            _sliceQuad.transform.rotation = Quaternion.Euler(90, 0, 0);
            _sliceQuad.transform.localScale = new Vector3(_volumeBounds.size.x, _volumeBounds.size.z, 1);
        }

        private void OnDrawGizmos()
        {
            if (!IsVisible || !_hasValidData)
                return;

            // Draw volume bounds
            Gizmos.color = new Color(1, 1, 0, 0.3f);
            Gizmos.DrawWireCube(_volumeBounds.center, _volumeBounds.size);

            // Draw slice plane
            Gizmos.color = new Color(0, 1, 1, 0.5f);
            var sliceCenter = new Vector3(_volumeBounds.center.x, SliceHeight, _volumeBounds.center.z);
            Gizmos.DrawWireCube(sliceCenter, new Vector3(_volumeBounds.size.x, 0.1f, _volumeBounds.size.z));
        }
    }
}
