using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using FogOfWar.Visibility.Components;
using FogOfWar.Visibility.Core;
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
    /// Uses VisibilitySystemBehaviour.Instance.Runtime for all GPU resources.
    /// </summary>
    [UpdateInGroup(typeof(VisibilitySystemGroup))]
    [UpdateAfter(typeof(VisibilityDataCollectionSystem))]
    [UpdateBefore(typeof(VisibilityReadbackSystem))]
    public partial class VisibilityComputeDispatchSystem : SystemBase
    {
        // [PERF #42] Pre-allocated arrays to avoid per-frame GC allocations
        private int[] _zeroCounts;
        private int[] _zeroCandidates;

        protected override void OnCreate()
        {
            RequireForUpdate<VisionGroupRegistry>();

            // [PERF #42] Allocate once at startup
            _zeroCounts = new int[GPUConstants.MAX_GROUPS];
            _zeroCandidates = new int[GPUConstants.MAX_GROUPS];
        }

        protected override void OnUpdate()
        {
            // Get runtime from MonoBehaviour
            var behaviour = VisibilitySystemBehaviour.Instance;
            if (behaviour == null || !behaviour.IsReady)
                return;

            var runtime = behaviour.Runtime;
            var config = behaviour.Config;

            // Dispatch the visibility pipeline
            DispatchVisibilityPipeline(runtime, config);
        }

        private void DispatchVisibilityPipeline(VisibilitySystemRuntime runtime, Configuration.VisibilitySystemConfig config)
        {
            if (config.VisibilityCheckShader == null || config.RayMarchConfirmShader == null)
                return;

            // [PERF #42] Clear counters using pre-allocated arrays (no per-frame allocation)
            runtime.VisibleCountsBuffer.SetData(_zeroCounts);
            runtime.CandidateCountsBuffer.SetData(_zeroCandidates);

            // Stage 0: Generate player fog volume
            if (config.PlayerFogVolumeShader != null && runtime.PlayerFogKernel >= 0)
            {
                DispatchPlayerFogVolume(runtime, config);
            }

            // [PARALLEL] Choose between parallel and sequential dispatch
            if (config.UseParallelDispatch && runtime.VisibilityCheckPerGroupKernel >= 0)
            {
                DispatchParallelPipeline(runtime, config);
            }
            else
            {
                DispatchSequentialPipeline(runtime, config);
            }
        }

        private void DispatchPlayerFogVolume(VisibilitySystemRuntime runtime, Configuration.VisibilitySystemConfig config)
        {
            var shader = config.PlayerFogVolumeShader;
            int kernel = runtime.PlayerFogKernel;

            // Bind buffers
            shader.SetBuffer(kernel, "_GroupData", runtime.GroupDataBuffer);
            shader.SetBuffer(kernel, "_UnitContributions", runtime.UnitContributionsBuffer);
            shader.SetTexture(kernel, "_FogVolumeOutput", runtime.FogVolumeTexture);

            // Set parameters
            shader.SetVector("_VolumeWorldMin", new Vector4(config.VolumeMin.x, config.VolumeMin.y, config.VolumeMin.z, 0));
            shader.SetVector("_VolumeWorldMax", new Vector4(config.VolumeMax.x, config.VolumeMax.y, config.VolumeMax.z, 0));
            shader.SetVector("_VolumeResolution", new Vector4(config.FogResolution, config.FogResolution, config.FogResolution, 0));
            shader.SetInt("_PlayerGroupId", config.PlayerGroupId);

            // Bind island data for shadow calculation
            shader.SetBuffer(kernel, "_Islands", runtime.IslandsBuffer);
            shader.SetInt("_IslandCount", runtime.IslandCount);
            shader.SetInt("_IslandValidityMask", (int)runtime.IslandValidityMask);

            // Bind island SDF textures
            shader.SetTexture(kernel, "_IslandSDF0", runtime.GetIslandTextureOrDummy(0));
            shader.SetTexture(kernel, "_IslandSDF1", runtime.GetIslandTextureOrDummy(1));
            shader.SetTexture(kernel, "_IslandSDF2", runtime.GetIslandTextureOrDummy(2));
            shader.SetTexture(kernel, "_IslandSDF3", runtime.GetIslandTextureOrDummy(3));
            shader.SetTexture(kernel, "_IslandSDF4", runtime.GetIslandTextureOrDummy(4));
            shader.SetTexture(kernel, "_IslandSDF5", runtime.GetIslandTextureOrDummy(5));
            shader.SetTexture(kernel, "_IslandSDF6", runtime.GetIslandTextureOrDummy(6));
            shader.SetTexture(kernel, "_IslandSDF7", runtime.GetIslandTextureOrDummy(7));

            // Dispatch
            int groups = config.FogResolution / GPUConstants.THREAD_GROUP_3D_X;
            shader.Dispatch(kernel, groups, groups, groups);
        }

        private void DispatchVisibilityCheck(VisibilitySystemRuntime runtime, Configuration.VisibilitySystemConfig config)
        {
            var shader = config.VisibilityCheckShader;
            int kernel = runtime.VisibilityCheckKernel;

            // Bind buffers
            shader.SetBuffer(kernel, "_GroupData", runtime.GroupDataBuffer);
            shader.SetBuffer(kernel, "_UnitContributions", runtime.UnitContributionsBuffer);
            shader.SetBuffer(kernel, "_SeeableEntities", runtime.SeeableEntitiesBuffer);
            shader.SetBuffer(kernel, "_GroupIslandMasks", runtime.GroupIslandMasksBuffer);
            shader.SetBuffer(kernel, "_Candidates", runtime.CandidatesBuffer);
            shader.SetBuffer(kernel, "_CandidateCounts", runtime.CandidateCountsBuffer);
            shader.SetBuffer(kernel, "_CandidateOffsets", runtime.CandidateOffsetsBuffer);
            shader.SetTexture(kernel, "_PlayerFogVolume", runtime.FogVolumeTexture);

            // Set parameters
            shader.SetInt("_GroupCount", GPUConstants.MAX_GROUPS);
            shader.SetInt("_SeeableCount", runtime.SeeableCount);
            shader.SetInt("_PlayerGroupId", config.PlayerGroupId);
            shader.SetInt("_MaxCandidatesPerGroup", config.MaxCandidatesPerGroup);
            shader.SetInt("_ActiveGroupMask", (int)runtime.ActiveGroupMask);
            shader.SetVector("_VolumeWorldMin", new Vector4(config.VolumeMin.x, config.VolumeMin.y, config.VolumeMin.z, 0));
            shader.SetVector("_VolumeWorldMax", new Vector4(config.VolumeMax.x, config.VolumeMax.y, config.VolumeMax.z, 0));

            // Dispatch: 1 thread GROUP per seeable
            int threadGroups = math.max(1, runtime.SeeableCount);
            shader.Dispatch(kernel, threadGroups, 1, 1);
        }

        private void BindPrepareDispatchBuffers(VisibilitySystemRuntime runtime, Configuration.VisibilitySystemConfig config)
        {
            var shader = config.RayMarchConfirmShader;
            int kernel = runtime.PrepareDispatchKernel;

            // [PARALLEL] Bind per-group candidate counts and indirect args
            shader.SetBuffer(kernel, "_CandidateCounts", runtime.CandidateCountsBuffer);
            shader.SetBuffer(kernel, "_IndirectArgs", runtime.IndirectArgsBuffer);
        }

        private void BindRayMarchBuffers(VisibilitySystemRuntime runtime, Configuration.VisibilitySystemConfig config, int kernel)
        {
            var shader = config.RayMarchConfirmShader;

            shader.SetBuffer(kernel, "_GroupData", runtime.GroupDataBuffer);
            shader.SetBuffer(kernel, "_UnitContributions", runtime.UnitContributionsBuffer);
            shader.SetBuffer(kernel, "_SeeableEntities", runtime.SeeableEntitiesBuffer);
            shader.SetBuffer(kernel, "_Candidates", runtime.CandidatesBuffer);
            shader.SetBuffer(kernel, "_CandidateCounts", runtime.CandidateCountsBuffer);
            shader.SetBuffer(kernel, "_CandidateOffsets", runtime.CandidateOffsetsBuffer);
            shader.SetBuffer(kernel, "_GroupIslandMasks", runtime.GroupIslandMasksBuffer);
            shader.SetBuffer(kernel, "_VisibleEntities", runtime.VisibleEntitiesBuffer);
            shader.SetBuffer(kernel, "_VisibleCounts", runtime.VisibleCountsBuffer);
            shader.SetBuffer(kernel, "_VisibleOffsets", runtime.VisibleOffsetsBuffer);
            shader.SetInt("_MaxVisiblePerGroup", config.MaxVisiblePerGroup);
            shader.SetInt("_SeeableCount", runtime.SeeableCount);

            // Island occlusion bindings
            shader.SetBuffer(kernel, "_Islands", runtime.IslandsBuffer);
            shader.SetInt("_IslandCount", runtime.IslandCount);
            shader.SetInt("_IslandValidityMask", (int)runtime.IslandValidityMask);

            // Bind island SDF textures
            shader.SetTexture(kernel, "_IslandSDF0", runtime.GetIslandTextureOrDummy(0));
            shader.SetTexture(kernel, "_IslandSDF1", runtime.GetIslandTextureOrDummy(1));
            shader.SetTexture(kernel, "_IslandSDF2", runtime.GetIslandTextureOrDummy(2));
            shader.SetTexture(kernel, "_IslandSDF3", runtime.GetIslandTextureOrDummy(3));
            shader.SetTexture(kernel, "_IslandSDF4", runtime.GetIslandTextureOrDummy(4));
            shader.SetTexture(kernel, "_IslandSDF5", runtime.GetIslandTextureOrDummy(5));
            shader.SetTexture(kernel, "_IslandSDF6", runtime.GetIslandTextureOrDummy(6));
            shader.SetTexture(kernel, "_IslandSDF7", runtime.GetIslandTextureOrDummy(7));
        }

        // =============================================================================
        // SEQUENTIAL PIPELINE (backward compatible)
        // =============================================================================

        private void DispatchSequentialPipeline(VisibilitySystemRuntime runtime, Configuration.VisibilitySystemConfig config)
        {
            // Stage 1: Visibility Check (generates candidates)
            DispatchVisibilityCheck(runtime, config);

            // Stage 2: Prepare indirect dispatch args
            BindPrepareDispatchBuffers(runtime, config);
            config.RayMarchConfirmShader.Dispatch(runtime.PrepareDispatchKernel, 1, 1, 1);

            // Stage 3: Ray march confirmation (indirect dispatch)
            BindRayMarchBuffers(runtime, config, runtime.RayMarchKernel);
            config.RayMarchConfirmShader.DispatchIndirect(runtime.RayMarchKernel, runtime.IndirectArgsBuffer, 0);
        }

        // =============================================================================
        // [PARALLEL] PARALLEL PIPELINE - 8 parallel dispatches per stage
        // =============================================================================

        private void DispatchParallelPipeline(VisibilitySystemRuntime runtime, Configuration.VisibilitySystemConfig config)
        {
            var visShader = config.VisibilityCheckShader;
            var rayShader = config.RayMarchConfirmShader;
            int perGroupKernel = runtime.VisibilityCheckPerGroupKernel;
            int rayPerGroupKernel = runtime.RayMarchPerGroupKernel;
            uint activeGroupMask = runtime.ActiveGroupMask;

            int threadGroups = math.max(1, runtime.SeeableCount);

            // Stage 1: Dispatch VisibilityCheck_PerGroup for each active group
            // Bind common buffers once
            BindVisibilityCheckBuffers(runtime, config, perGroupKernel);

            for (int g = 0; g < GPUConstants.MAX_GROUPS; g++)
            {
                if ((activeGroupMask & (1u << g)) == 0)
                    continue; // Skip inactive groups

                visShader.SetInt("_TargetGroupId", g);
                visShader.Dispatch(perGroupKernel, threadGroups, 1, 1);
            }

            // Stage 2: PrepareDispatch_PerGroup - computes all 8 indirect args in one dispatch
            int preparePerGroupKernel = runtime.PrepareDispatchPerGroupKernel;
            rayShader.SetBuffer(preparePerGroupKernel, "_CandidateCounts", runtime.CandidateCountsBuffer);
            rayShader.SetBuffer(preparePerGroupKernel, "_IndirectArgs", runtime.IndirectArgsBuffer);
            rayShader.Dispatch(preparePerGroupKernel, 1, 1, 1);

            // Stage 3: Dispatch RayMarch_PerGroup for each active group
            // Bind common buffers once
            BindRayMarchBuffers(runtime, config, rayPerGroupKernel);

            for (int g = 0; g < GPUConstants.MAX_GROUPS; g++)
            {
                if ((activeGroupMask & (1u << g)) == 0)
                    continue; // Skip inactive groups

                rayShader.SetInt("_TargetGroupId", g);

                // Each group has its own indirect args at offset g * 16 bytes (4 uints * 4 bytes)
                uint argsOffset = (uint)(g * 16);
                rayShader.DispatchIndirect(rayPerGroupKernel, runtime.IndirectArgsBuffer, argsOffset);
            }
        }

        private void BindVisibilityCheckBuffers(VisibilitySystemRuntime runtime, Configuration.VisibilitySystemConfig config, int kernel)
        {
            var shader = config.VisibilityCheckShader;

            shader.SetBuffer(kernel, "_GroupData", runtime.GroupDataBuffer);
            shader.SetBuffer(kernel, "_UnitContributions", runtime.UnitContributionsBuffer);
            shader.SetBuffer(kernel, "_SeeableEntities", runtime.SeeableEntitiesBuffer);
            shader.SetBuffer(kernel, "_GroupIslandMasks", runtime.GroupIslandMasksBuffer);
            shader.SetBuffer(kernel, "_Candidates", runtime.CandidatesBuffer);
            shader.SetBuffer(kernel, "_CandidateCounts", runtime.CandidateCountsBuffer);
            shader.SetBuffer(kernel, "_CandidateOffsets", runtime.CandidateOffsetsBuffer);
            shader.SetTexture(kernel, "_PlayerFogVolume", runtime.FogVolumeTexture);

            shader.SetInt("_GroupCount", GPUConstants.MAX_GROUPS);
            shader.SetInt("_SeeableCount", runtime.SeeableCount);
            shader.SetInt("_PlayerGroupId", config.PlayerGroupId);
            shader.SetInt("_MaxCandidatesPerGroup", config.MaxCandidatesPerGroup);
            shader.SetInt("_ActiveGroupMask", (int)runtime.ActiveGroupMask);
            shader.SetVector("_VolumeWorldMin", new Vector4(config.VolumeMin.x, config.VolumeMin.y, config.VolumeMin.z, 0));
            shader.SetVector("_VolumeWorldMax", new Vector4(config.VolumeMax.x, config.VolumeMax.y, config.VolumeMax.z, 0));
        }
    }
}
