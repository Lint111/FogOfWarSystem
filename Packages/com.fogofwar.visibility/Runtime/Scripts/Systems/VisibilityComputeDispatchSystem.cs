using Unity.Entities;
using Unity.Collections;
using UnityEngine;
using FogOfWar.Visibility.Components;
using FogOfWar.Visibility.GPU;

namespace FogOfWar.Visibility.Systems
{
    /// <summary>
    /// Orchestrates the GPU visibility compute pipeline.
    ///
    /// Pipeline stages:
    /// 1. VisibilityCheck - Evaluate SDF visibility for all seeables
    /// 2. PrepareRayMarchDispatch - Compute indirect dispatch args
    /// 3. RayMarchConfirmation - Confirm visibility via ray marching (indirect dispatch)
    ///
    /// [#15] Uses indirect dispatch to avoid CPU readback of candidate count.
    /// </summary>
    [UpdateInGroup(typeof(VisibilitySystemGroup))]
    [UpdateBefore(typeof(VisibilityReadbackSystem))]
    public partial class VisibilityComputeDispatchSystem : SystemBase
    {
        // Compute shader references
        private ComputeShader _visibilityCheckShader;
        private ComputeShader _rayMarchShader;

        // Kernel indices
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

        // [#15] Indirect dispatch args buffer
        private ComputeBuffer _indirectArgsBuffer;

        // Configuration
        private int _maxUnitsPerGroup = 256;
        private int _maxSeeables = 2048;
        private int _maxCandidates = 4096;
        private int _maxVisiblePerGroup = 1024;

        private bool _initialized;

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

            // TODO: Collect ECS data into GPU buffers (future system)
            // For now, the buffers are empty shells

            DispatchVisibilityPipeline();
        }

        private bool InitializeShaders()
        {
            _visibilityCheckShader = Resources.Load<ComputeShader>("Shaders/VisibilityCheck");
            _rayMarchShader = Resources.Load<ComputeShader>("Shaders/RayMarchConfirm");

            if (_visibilityCheckShader == null || _rayMarchShader == null)
            {
                Debug.LogWarning("[VisibilityComputeDispatchSystem] Failed to load compute shaders from Resources. " +
                    $"VisibilityCheck: {(_visibilityCheckShader != null ? "OK" : "MISSING")}, " +
                    $"RayMarchConfirm: {(_rayMarchShader != null ? "OK" : "MISSING")}. " +
                    "Ensure shaders are in Resources/Shaders/ or configure via ScriptableObject.");
                return false;
            }

            _visibilityCheckKernel = _visibilityCheckShader.FindKernel("VisibilityCheck");
            _prepareDispatchKernel = _rayMarchShader.FindKernel("PrepareRayMarchDispatch");
            _rayMarchKernel = _rayMarchShader.FindKernel("RayMarchConfirmation");

            return true;
        }

        private void InitializeBuffers()
        {
            int totalUnits = GPUConstants.MAX_GROUPS * _maxUnitsPerGroup;
            int totalVisible = GPUConstants.MAX_GROUPS * _maxVisiblePerGroup;

            // Create compute buffers
            _groupDataBuffer = new ComputeBuffer(GPUConstants.MAX_GROUPS, 48); // VisionGroupDataGPU
            _unitContributionsBuffer = new ComputeBuffer(totalUnits, 48); // UnitSDFContributionGPU
            _seeableEntitiesBuffer = new ComputeBuffer(_maxSeeables, 32); // SeeableEntityDataGPU
            _candidatesBuffer = new ComputeBuffer(_maxCandidates, 32); // VisibilityCandidateGPU
            _candidateCountBuffer = new ComputeBuffer(1, sizeof(int));
            _visibleEntitiesBuffer = new ComputeBuffer(totalVisible, 16); // VisibilityEntryGPU
            _visibleCountsBuffer = new ComputeBuffer(GPUConstants.MAX_GROUPS, sizeof(int));
            _visibleOffsetsBuffer = new ComputeBuffer(GPUConstants.MAX_GROUPS, sizeof(int));
            _groupIslandMasksBuffer = new ComputeBuffer(GPUConstants.MAX_GROUPS, sizeof(uint));

            // [#15] Create indirect args buffer (must use IndirectArguments type)
            _indirectArgsBuffer = new ComputeBuffer(1, 16, ComputeBufferType.IndirectArguments);

            // Initialize offsets (fixed layout: group N starts at N * maxVisiblePerGroup)
            int[] offsets = new int[GPUConstants.MAX_GROUPS];
            for (int i = 0; i < GPUConstants.MAX_GROUPS; i++)
                offsets[i] = i * _maxVisiblePerGroup;
            _visibleOffsetsBuffer.SetData(offsets);

            // Initialize indirect args to minimum valid dispatch (1,1,1)
            uint[] initialArgs = { 1, 1, 1, 0 };
            _indirectArgsBuffer.SetData(initialArgs);

            // Register buffers with readback system
            var gpuBuffersRef = new VisibilityGPUBuffersRef
            {
                VisibleCounts = _visibleCountsBuffer,
                VisibleEntities = _visibleEntitiesBuffer,
                VisibleOffsets = _visibleOffsetsBuffer
            };

            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, gpuBuffersRef);
        }

        /// <summary>
        /// [#15] Dispatches the full visibility pipeline using indirect dispatch.
        /// </summary>
        private void DispatchVisibilityPipeline()
        {
            if (_rayMarchShader == null)
                return;

            // Clear counters
            int[] zeroCounts = new int[GPUConstants.MAX_GROUPS];
            _visibleCountsBuffer.SetData(zeroCounts);

            int[] zeroCandidate = { 0 };
            _candidateCountBuffer.SetData(zeroCandidate);

            // Stage 1: Visibility Check (generates candidates)
            // TODO: Implement when VisibilityCheck shader is ready
            // DispatchVisibilityCheck();

            // Stage 2: Prepare indirect dispatch args
            BindPrepareDispatchBuffers();
            _rayMarchShader.Dispatch(_prepareDispatchKernel, 1, 1, 1);

            // Stage 3: Ray march confirmation (indirect dispatch)
            BindRayMarchBuffers();
            _rayMarchShader.DispatchIndirect(_rayMarchKernel, _indirectArgsBuffer, 0);
        }

        private void BindPrepareDispatchBuffers()
        {
            _rayMarchShader.SetBuffer(_prepareDispatchKernel, "_CandidateCount", _candidateCountBuffer);
            _rayMarchShader.SetBuffer(_prepareDispatchKernel, "_IndirectArgs", _indirectArgsBuffer);
        }

        private void BindRayMarchBuffers()
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
            _rayMarchShader.SetInt("_MaxVisiblePerGroup", _maxVisiblePerGroup);
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
            _indirectArgsBuffer?.Dispose();
        }
    }
}
