using UnityEngine;
using Unity.Entities;

namespace FogOfWar.Visibility.Debugging
{
    /// <summary>
    /// Debug visualizer for the 3D fog volume.
    /// Displays a slice of the fog volume with visibility coloring.
    /// Press F1 to toggle, PageUp/PageDown to change slice height.
    /// </summary>
    [ExecuteAlways]
    public class FogVolumeVisualizer : MonoBehaviour
    {
        [Header("Display Settings")]
        [Tooltip("Material for rendering the fog slice")]
        public Material VisualizationMaterial;

        [Tooltip("Size of the debug quad in world units")]
        public float QuadSize = 50f;

        [Tooltip("Height of the slice in world space")]
        public float SliceHeight = 0f;

        [Tooltip("Show the visualization")]
        public bool IsVisible = true;

        [Header("Colors")]
        public Color VisibleColor = new Color(0.2f, 0.8f, 0.2f, 0.4f);
        public Color HiddenColor = new Color(0.3f, 0.3f, 0.3f, 0.4f);

        private RenderTexture _fogVolume;
        private Bounds _volumeBounds;
        private Mesh _quadMesh;
        private MaterialPropertyBlock _propertyBlock;
        private bool _hasValidData;

        private void OnEnable()
        {
            CreateQuadMesh();
            _propertyBlock = new MaterialPropertyBlock();
        }

        private void OnDisable()
        {
            if (_quadMesh != null)
            {
                DestroyImmediate(_quadMesh);
                _quadMesh = null;
            }
        }

        private void Update()
        {
            // Toggle with F1
            if (Input.GetKeyDown(KeyCode.F1))
            {
                IsVisible = !IsVisible;
            }

            // Adjust slice height with PageUp/PageDown
            if (Input.GetKey(KeyCode.PageUp))
            {
                SliceHeight += Time.deltaTime * 10f;
            }
            if (Input.GetKey(KeyCode.PageDown))
            {
                SliceHeight -= Time.deltaTime * 10f;
            }

            // Try to get fog volume from ECS
            TryGetFogVolume();
        }

        private void TryGetFogVolume()
        {
            if (World.DefaultGameObjectInjectionWorld == null)
            {
                _hasValidData = false;
                return;
            }

            var em = World.DefaultGameObjectInjectionWorld.EntityManager;

            // Query for the GPU buffers singleton
            var query = em.CreateEntityQuery(typeof(Systems.VisibilityGPUBuffersRef));
            if (query.IsEmpty)
            {
                _hasValidData = false;
                return;
            }

            var entity = query.GetSingletonEntity();
            var buffers = em.GetComponentData<Systems.VisibilityGPUBuffersRef>(entity);

            if (buffers?.FogVolume != null)
            {
                _fogVolume = buffers.FogVolume;
                _volumeBounds = buffers.VolumeBounds;
                _hasValidData = true;
            }
            else
            {
                _hasValidData = false;
            }
        }

        private void OnRenderObject()
        {
            if (!IsVisible || !_hasValidData || _fogVolume == null || VisualizationMaterial == null)
                return;

            // Calculate slice position
            float normalizedHeight = (_volumeBounds.size.y > 0)
                ? (SliceHeight - _volumeBounds.min.y) / _volumeBounds.size.y
                : 0.5f;
            normalizedHeight = Mathf.Clamp01(normalizedHeight);

            // Update material properties
            _propertyBlock.SetTexture("_FogVolume", _fogVolume);
            _propertyBlock.SetFloat("_SliceHeight", normalizedHeight);
            _propertyBlock.SetVector("_VolumeMin", _volumeBounds.min);
            _propertyBlock.SetVector("_VolumeMax", _volumeBounds.max);
            _propertyBlock.SetColor("_VisibleColor", VisibleColor);
            _propertyBlock.SetColor("_HiddenColor", HiddenColor);

            // Position quad at slice height
            var pos = new Vector3(
                _volumeBounds.center.x,
                SliceHeight,
                _volumeBounds.center.z
            );

            var matrix = Matrix4x4.TRS(
                pos,
                Quaternion.Euler(90, 0, 0),
                new Vector3(_volumeBounds.size.x, _volumeBounds.size.z, 1)
            );

            // Draw
            VisualizationMaterial.SetPass(0);
            Graphics.DrawMesh(_quadMesh, matrix, VisualizationMaterial, 0, null, 0, _propertyBlock);
        }

        private void CreateQuadMesh()
        {
            _quadMesh = new Mesh();
            _quadMesh.name = "FogVisualizerQuad";

            _quadMesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0),
                new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0)
            };

            _quadMesh.uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };

            _quadMesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            _quadMesh.RecalculateNormals();
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
