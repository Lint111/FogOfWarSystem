using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using FogOfWar.Visibility.Components;
using FogOfWar.Visibility.Configuration;
using FogOfWar.Visibility.GPU;

namespace FogOfWar.Visibility.Systems
{
    /// <summary>
    /// Orchestrates the GPU visibility compute pipeline.
    ///
    /// Pipeline stages:
    /// 0. PlayerFogVolume - Generate 3D fog texture for player group
    /// 1. VisibilityCheck - Evaluate SDF visibility for all seeables
    /// 2. PrepareRayMarchDispatch - Compute indirect dispatch args
    /// 3. RayMarchConfirmation - Confirm visibility via ray marching (indirect dispatch)
    ///
    /// [#15] Uses indirect dispatch to avoid CPU readback of candidate count.
    /// </summary>
    [UpdateInGroup(typeof(VisibilitySystemGroup))]
    [UpdateAfter(typeof(VisibilityDataCollectionSystem))]
    [UpdateBefore(typeof(VisibilityReadbackSystem))]
    public partial class VisibilityComputeDispatchSystem : SystemBase
    {
        // Compute shader references
        private ComputeShader _playerFogShader;
        private ComputeShader _visibilityCheckShader;
        private ComputeShader _rayMarchShader;

        // Kernel indices
        private int _playerFogKernel;
        private int _clearFogKernel;
        private int _visibilityCheckKernel;
        private int _prepareDispatchKernel;
        private int _rayMarchKernel;

        // GPU Buffers
        private ComputeBuffer _groupDataBuffer;
        private ComputeBuffer _unitContributionsBuffer;
        private ComputeBuffer _seeableEntitiesBuffer;
        private ComputeBuffer _candidatesBuffer;
        private ComputeBuffer _candidateCountBuffer;
        private ComputeBuffer _visibleEntitiesBuffer;
        private ComputeBuffer _visibleCountsBuffer;
        private ComputeBuffer _visibleOffsetsBuffer;
        private ComputeBuffer _groupIslandMasksBuffer;
        private ComputeBuffer _islandsBuffer;
        private ComputeBuffer _indirectArgsBuffer;

        // Fog volume texture
        private RenderTexture _fogVolumeTexture;
        private const int FOG_RESOLUTION = 128;

        // Volume bounds (world space)
        private float3 _volumeMin = new float3(-50, -10, -50);
        private float3 _volumeMax = new float3(50, 40, 50);

        // Configuration
        private int _maxUnitsPerGroup = 256;
        private int _maxSeeables = 2048;
        private int _maxCandidates = 4096;
        private int _maxVisiblePerGroup = 1024;
        private int _playerGroupId = 0;

        private bool _initialized;
        private VisibilityGPUBuffersRef _buffersRef;

        protected override void OnCreate()
        {
            RequireForUpdate<VisionGroupRegistry>();
        }

        protected override void OnDestroy()
        {
            DisposeBuffers();
        }

        protected override void OnUpdate()
        {
            if (!_initialized)
            {
                if (!InitializeShaders())
                    return;

                InitializeBuffers();
                _initialized = true;
            }

            // Data already uploaded by VisibilityDataCollectionSystem
            DispatchVisibilityPipeline();
        }

        private bool InitializeShaders()
        {
            // Try to get shaders from ScriptableObject config
            var config = VisibilityShaderConfig.Instance;
            if (config != null && config.IsValid)
            {
                _playerFogShader = config.PlayerFogVolumeShader;
                _visibilityCheckShader = config.VisibilityCheckShader;
                _rayMarchShader = config.RayMarchConfirmShader;
            }
            else
            {
                // Fallback: try to find shaders by name in project
                _playerFogShader = FindShaderByName("PlayerFogVolume");
                _visibilityCheckShader = FindShaderByName("VisibilityCheck");
                _rayMarchShader = FindShaderByName("RayMarchConfirm");
            }

            if (_visibilityCheckShader == null || _rayMarchShader == null)
            {
                UnityEngine.Debug.LogWarning("[VisibilityComputeDispatchSystem] Compute shaders not found. " +
                    $"VisibilityCheck: {(_visibilityCheckShader != null ? "OK" : "MISSING")}, " +
                    $"RayMarchConfirm: {(_rayMarchShader != null ? "OK" : "MISSING")}. " +
                    "Create a VisibilityShaderConfig asset and assign the shaders, or ensure shaders are in project.");
                return false;
            }

            // Get kernel indices
            if (_playerFogShader != null)
            {
                _playerFogKernel = _playerFogShader.FindKernel("GeneratePlayerFogVolume");
                _clearFogKernel = _playerFogShader.FindKernel("ClearPlayerFogVolume");
            }
            _visibilityCheckKernel = _visibilityCheckShader.FindKernel("ComputeAllGroupsVisibility");
            _prepareDispatchKernel = _rayMarchShader.FindKernel("PrepareRayMarchDispatch");
            _rayMarchKernel = _rayMarchShader.FindKernel("RayMarchConfirmation");

            return true;
        }

        private static ComputeShader FindShaderByName(string name)
        {
#if UNITY_EDITOR
            // In editor, we can search for shaders by name
            var guids = UnityEditor.AssetDatabase.FindAssets($"t:ComputeShader {name}");
            foreach (var guid in guids)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith($"{name}.compute"))
                {
                    return UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
                }
            }
#endif
            return null;
        }

        private void InitializeBuffers()
        {
            int totalUnits = GPUConstants.MAX_GROUPS * _maxUnitsPerGroup;
            int totalVisible = GPUConstants.MAX_GROUPS * _maxVisiblePerGroup;

            // Create compute buffers
            _groupDataBuffer = new ComputeBuffer(GPUConstants.MAX_GROUPS, 48);
            _unitContributionsBuffer = new ComputeBuffer(totalUnits, 48);
            _seeableEntitiesBuffer = new ComputeBuffer(_maxSeeables, 32);
            _candidatesBuffer = new ComputeBuffer(_maxCandidates, 32);
            _candidateCountBuffer = new ComputeBuffer(1, sizeof(int));
            _visibleEntitiesBuffer = new ComputeBuffer(totalVisible, 16);
            _visibleCountsBuffer = new ComputeBuffer(GPUConstants.MAX_GROUPS, sizeof(int));
            _visibleOffsetsBuffer = new ComputeBuffer(GPUConstants.MAX_GROUPS, sizeof(int));
            _groupIslandMasksBuffer = new ComputeBuffer(GPUConstants.MAX_GROUPS, sizeof(uint));
            _islandsBuffer = new ComputeBuffer(GPUConstants.MAX_ISLANDS, 96);
            _indirectArgsBuffer = new ComputeBuffer(1, 16, ComputeBufferType.IndirectArguments);

            // Create 3D fog volume texture
            _fogVolumeTexture = new RenderTexture(FOG_RESOLUTION, FOG_RESOLUTION, 0, RenderTextureFormat.RFloat);
            _fogVolumeTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
            _fogVolumeTexture.volumeDepth = FOG_RESOLUTION;
            _fogVolumeTexture.enableRandomWrite = true;
            _fogVolumeTexture.filterMode = FilterMode.Trilinear;
            _fogVolumeTexture.wrapMode = TextureWrapMode.Clamp;
            _fogVolumeTexture.Create();

            // Initialize offsets (fixed layout)
            int[] offsets = new int[GPUConstants.MAX_GROUPS];
            for (int i = 0; i < GPUConstants.MAX_GROUPS; i++)
                offsets[i] = i * _maxVisiblePerGroup;
            _visibleOffsetsBuffer.SetData(offsets);

            // Initialize indirect args
            uint[] initialArgs = { 1, 1, 1, 0 };
            _indirectArgsBuffer.SetData(initialArgs);

            // Register all buffers with singleton
            _buffersRef = new VisibilityGPUBuffersRef
            {
                // Input buffers
                GroupData = _groupDataBuffer,
                UnitContributions = _unitContributionsBuffer,
                SeeableEntities = _seeableEntitiesBuffer,
                Islands = _islandsBuffer,
                GroupIslandMasks = _groupIslandMasksBuffer,

                // Output buffers
                VisibleCounts = _visibleCountsBuffer,
                VisibleEntities = _visibleEntitiesBuffer,
                VisibleOffsets = _visibleOffsetsBuffer,

                // Intermediate buffers
                Candidates = _candidatesBuffer,
                CandidateCount = _candidateCountBuffer,
                IndirectArgs = _indirectArgsBuffer,

                // Textures
                FogVolume = _fogVolumeTexture,
                VolumeBounds = new Bounds(
                    (_volumeMin + _volumeMax) * 0.5f,
                    _volumeMax - _volumeMin),

                // Counts
                GroupCount = GPUConstants.MAX_GROUPS,
                PlayerGroupId = _playerGroupId,
                MaxCandidates = _maxCandidates,
                MaxVisiblePerGroup = _maxVisiblePerGroup
            };

            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, _buffersRef);
        }

        private void DispatchVisibilityPipeline()
        {
            if (_visibilityCheckShader == null || _rayMarchShader == null)
                return;

            // Get current counts from data collection
            if (!SystemAPI.ManagedAPI.TryGetSingleton<VisibilityGPUBuffersRef>(out var buffers))
                return;

            // Clear counters
            int[] zeroCounts = new int[GPUConstants.MAX_GROUPS];
            _visibleCountsBuffer.SetData(zeroCounts);

            int[] zeroCandidate = { 0 };
            _candidateCountBuffer.SetData(zeroCandidate);

            // Stage 0: Generate player fog volume
            if (_playerFogShader != null)
            {
                DispatchPlayerFogVolume(buffers);
            }

            // Stage 1: Visibility Check (generates candidates)
            DispatchVisibilityCheck(buffers);

            // Stage 2: Prepare indirect dispatch args
            BindPrepareDispatchBuffers();
            _rayMarchShader.Dispatch(_prepareDispatchKernel, 1, 1, 1);

            // Stage 3: Ray march confirmation (indirect dispatch)
            BindRayMarchBuffers(buffers);
            _rayMarchShader.DispatchIndirect(_rayMarchKernel, _indirectArgsBuffer, 0);
        }

        private void DispatchPlayerFogVolume(VisibilityGPUBuffersRef buffers)
        {
            // Bind buffers
            _playerFogShader.SetBuffer(_playerFogKernel, "_GroupData", _groupDataBuffer);
            _playerFogShader.SetBuffer(_playerFogKernel, "_UnitContributions", _unitContributionsBuffer);
            _playerFogShader.SetTexture(_playerFogKernel, "_FogVolumeOutput", _fogVolumeTexture);

            // Set parameters
            _playerFogShader.SetVector("_VolumeWorldMin", new Vector4(_volumeMin.x, _volumeMin.y, _volumeMin.z, 0));
            _playerFogShader.SetVector("_VolumeWorldMax", new Vector4(_volumeMax.x, _volumeMax.y, _volumeMax.z, 0));
            _playerFogShader.SetVector("_VolumeResolution", new Vector4(FOG_RESOLUTION, FOG_RESOLUTION, FOG_RESOLUTION, 0));
            _playerFogShader.SetInt("_PlayerGroupId", buffers.PlayerGroupId);

            // Dispatch (thread groups: 128/4 = 32 per dimension)
            int groups = FOG_RESOLUTION / GPUConstants.THREAD_GROUP_3D_X;
            _playerFogShader.Dispatch(_playerFogKernel, groups, groups, groups);
        }

        private void DispatchVisibilityCheck(VisibilityGPUBuffersRef buffers)
        {
            // Bind buffers
            _visibilityCheckShader.SetBuffer(_visibilityCheckKernel, "_GroupData", _groupDataBuffer);
            _visibilityCheckShader.SetBuffer(_visibilityCheckKernel, "_UnitContributions", _unitContributionsBuffer);
            _visibilityCheckShader.SetBuffer(_visibilityCheckKernel, "_SeeableEntities", _seeableEntitiesBuffer);
            _visibilityCheckShader.SetBuffer(_visibilityCheckKernel, "_GroupIslandMasks", _groupIslandMasksBuffer);
            _visibilityCheckShader.SetBuffer(_visibilityCheckKernel, "_Candidates", _candidatesBuffer);
            _visibilityCheckShader.SetBuffer(_visibilityCheckKernel, "_CandidateCount", _candidateCountBuffer);
            _visibilityCheckShader.SetTexture(_visibilityCheckKernel, "_PlayerFogVolume", _fogVolumeTexture);

            // Set parameters
            _visibilityCheckShader.SetInt("_GroupCount", buffers.GroupCount);
            _visibilityCheckShader.SetInt("_SeeableCount", buffers.SeeableCount);
            _visibilityCheckShader.SetInt("_PlayerGroupId", buffers.PlayerGroupId);
            _visibilityCheckShader.SetInt("_MaxCandidates", buffers.MaxCandidates);
            _visibilityCheckShader.SetVector("_VolumeWorldMin", new Vector4(_volumeMin.x, _volumeMin.y, _volumeMin.z, 0));
            _visibilityCheckShader.SetVector("_VolumeWorldMax", new Vector4(_volumeMax.x, _volumeMax.y, _volumeMax.z, 0));

            // Dispatch (1 thread per seeable)
            int seeableCount = math.max(1, buffers.SeeableCount);
            int threadGroups = (seeableCount + GPUConstants.THREAD_GROUP_1D - 1) / GPUConstants.THREAD_GROUP_1D;
            _visibilityCheckShader.Dispatch(_visibilityCheckKernel, threadGroups, 1, 1);
        }

        private void BindPrepareDispatchBuffers()
        {
            _rayMarchShader.SetBuffer(_prepareDispatchKernel, "_CandidateCount", _candidateCountBuffer);
            _rayMarchShader.SetBuffer(_prepareDispatchKernel, "_IndirectArgs", _indirectArgsBuffer);
        }

        private void BindRayMarchBuffers(VisibilityGPUBuffersRef buffers)
        {
            _rayMarchShader.SetBuffer(_rayMarchKernel, "_GroupData", _groupDataBuffer);
            _rayMarchShader.SetBuffer(_rayMarchKernel, "_UnitContributions", _unitContributionsBuffer);
            _rayMarchShader.SetBuffer(_rayMarchKernel, "_SeeableEntities", _seeableEntitiesBuffer);
            _rayMarchShader.SetBuffer(_rayMarchKernel, "_Candidates", _candidatesBuffer);
            _rayMarchShader.SetBuffer(_rayMarchKernel, "_CandidateCount", _candidateCountBuffer);
            _rayMarchShader.SetBuffer(_rayMarchKernel, "_GroupIslandMasks", _groupIslandMasksBuffer);
            _rayMarchShader.SetBuffer(_rayMarchKernel, "_VisibleEntities", _visibleEntitiesBuffer);
            _rayMarchShader.SetBuffer(_rayMarchKernel, "_VisibleCounts", _visibleCountsBuffer);
            _rayMarchShader.SetBuffer(_rayMarchKernel, "_VisibleOffsets", _visibleOffsetsBuffer);
            _rayMarchShader.SetInt("_MaxVisiblePerGroup", buffers.MaxVisiblePerGroup);
            _rayMarchShader.SetInt("_SeeableCount", buffers.SeeableCount);
        }

        private void DisposeBuffers()
        {
            _groupDataBuffer?.Dispose();
            _unitContributionsBuffer?.Dispose();
            _seeableEntitiesBuffer?.Dispose();
            _candidatesBuffer?.Dispose();
            _candidateCountBuffer?.Dispose();
            _visibleEntitiesBuffer?.Dispose();
            _visibleCountsBuffer?.Dispose();
            _visibleOffsetsBuffer?.Dispose();
            _groupIslandMasksBuffer?.Dispose();
            _islandsBuffer?.Dispose();
            _indirectArgsBuffer?.Dispose();

            if (_fogVolumeTexture != null)
            {
                _fogVolumeTexture.Release();
                Object.Destroy(_fogVolumeTexture);
            }
        }
    }
}
