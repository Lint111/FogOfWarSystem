using System;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;
using FogOfWar.Visibility.Configuration;
using FogOfWar.Visibility.Components;
using FogOfWar.Visibility.GPU;

namespace FogOfWar.Visibility.Core
{
    /// <summary>
    /// Central runtime manager for the visibility system.
    /// Owns all GPU resources and provides the public API.
    /// Created and disposed by VisibilitySystemBehaviour.
    /// </summary>
    public class VisibilitySystemRuntime : IDisposable
    {
        public VisibilitySystemConfig Config { get; }
        public bool IsInitialized { get; private set; }
        public bool IsDisposed { get; private set; }

        // ===== GPU Buffers =====
        public ComputeBuffer GroupDataBuffer { get; private set; }
        public ComputeBuffer UnitContributionsBuffer { get; private set; }
        public ComputeBuffer SeeableEntitiesBuffer { get; private set; }
        public ComputeBuffer IslandsBuffer { get; private set; }
        public ComputeBuffer GroupIslandMasksBuffer { get; private set; }
        public ComputeBuffer VisibleCountsBuffer { get; private set; }
        public ComputeBuffer VisibleEntitiesBuffer { get; private set; }
        public ComputeBuffer VisibleOffsetsBuffer { get; private set; }
        public ComputeBuffer CandidatesBuffer { get; private set; }
        public ComputeBuffer CandidateCountsBuffer { get; private set; }    // [PARALLEL] Now 8 per-group counters
        public ComputeBuffer CandidateOffsetsBuffer { get; private set; }   // [PARALLEL] Pre-computed per-group offsets
        public ComputeBuffer IndirectArgsBuffer { get; private set; }       // [PARALLEL] Now 8 sets of indirect args

        // ===== Textures =====
        public RenderTexture FogVolumeTexture { get; private set; }
        public Texture3D DummyIslandSDF { get; private set; }

        // ===== Island SDF Registry =====
        public Texture3D[] IslandTextures { get; } = new Texture3D[GPUConstants.MAX_ISLANDS];
        public uint IslandValidityMask { get; private set; }
        public int LastIslandModifiedFrame { get; private set; }

        // ===== Compute Kernels =====
        public int PlayerFogKernel { get; private set; } = -1;
        public int ClearFogKernel { get; private set; } = -1;
        public int VisibilityCheckKernel { get; private set; } = -1;
        public int VisibilityCheckPerGroupKernel { get; private set; } = -1;  // [PARALLEL] Single-group kernel
        public int PrepareDispatchKernel { get; private set; } = -1;
        public int PrepareDispatchPerGroupKernel { get; private set; } = -1; // [PARALLEL] Compute 8 indirect args
        public int RayMarchKernel { get; private set; } = -1;
        public int RayMarchPerGroupKernel { get; private set; } = -1;        // [PARALLEL] Single-group ray march

        // ===== Runtime State =====
        public int SeeableCount { get; set; }
        public int IslandCount { get; set; }
        public uint ActiveGroupMask { get; set; }  // Bitmask of groups with active units (for early rejection)

        // ===== Events (replaces static events in VisibilityEventSystem) =====
        public event Action<VisibilityChangeInfo> OnVisibilityChanged;
        public event Action<VisibilityChangeInfo> OnEntitySpotted;
        public event Action<VisibilityChangeInfo> OnEntityLost;

        public VisibilitySystemRuntime(VisibilitySystemConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Initializes all GPU resources. Call after construction.
        /// </summary>
        public void Initialize()
        {
            if (IsInitialized)
            {
                Debug.LogWarning("[VisibilitySystemRuntime] Already initialized");
                return;
            }

            if (!Config.IsValid)
            {
                Debug.LogError("[VisibilitySystemRuntime] Config is invalid - missing shaders");
                return;
            }

            InitializeKernels();
            InitializeBuffers();
            InitializeTextures();

            IsInitialized = true;
            Debug.Log("[VisibilitySystemRuntime] Initialized successfully");
        }

        private void InitializeKernels()
        {
            // Player fog shader (optional)
            if (Config.PlayerFogVolumeShader != null)
            {
                try
                {
                    PlayerFogKernel = Config.PlayerFogVolumeShader.FindKernel("GeneratePlayerFogVolume");
                    ClearFogKernel = Config.PlayerFogVolumeShader.FindKernel("ClearPlayerFogVolume");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[VisibilitySystemRuntime] PlayerFogVolume shader kernel error: {e.Message}");
                }
            }

            // Visibility check shader (required)
            try
            {
                VisibilityCheckKernel = Config.VisibilityCheckShader.FindKernel("ComputeAllGroupsVisibility");
                VisibilityCheckPerGroupKernel = Config.VisibilityCheckShader.FindKernel("VisibilityCheck_PerGroup");
            }
            catch (Exception e)
            {
                Debug.LogError($"[VisibilitySystemRuntime] VisibilityCheck kernel error: {e.Message}");
            }

            // Ray march shader (required)
            try
            {
                PrepareDispatchKernel = Config.RayMarchConfirmShader.FindKernel("PrepareRayMarchDispatch");
                PrepareDispatchPerGroupKernel = Config.RayMarchConfirmShader.FindKernel("PrepareDispatch_PerGroup");
                RayMarchKernel = Config.RayMarchConfirmShader.FindKernel("RayMarchConfirmation");
                RayMarchPerGroupKernel = Config.RayMarchConfirmShader.FindKernel("RayMarch_PerGroup");
            }
            catch (Exception e)
            {
                Debug.LogError($"[VisibilitySystemRuntime] RayMarch kernel error: {e.Message}");
            }
        }

        private void InitializeBuffers()
        {
            int totalUnits = GPUConstants.MAX_GROUPS * Config.MaxUnitsPerGroup;
            int totalVisible = GPUConstants.MAX_GROUPS * Config.MaxVisiblePerGroup;

            // Input buffers
            GroupDataBuffer = new ComputeBuffer(GPUConstants.MAX_GROUPS, 48);
            UnitContributionsBuffer = new ComputeBuffer(totalUnits, 48);
            SeeableEntitiesBuffer = new ComputeBuffer(Config.MaxSeeables, 32);
            IslandsBuffer = new ComputeBuffer(GPUConstants.MAX_ISLANDS, 96);
            GroupIslandMasksBuffer = new ComputeBuffer(GPUConstants.MAX_GROUPS, sizeof(uint));

            // Output buffers
            VisibleCountsBuffer = new ComputeBuffer(GPUConstants.MAX_GROUPS, sizeof(int));
            VisibleEntitiesBuffer = new ComputeBuffer(totalVisible, 16);
            VisibleOffsetsBuffer = new ComputeBuffer(GPUConstants.MAX_GROUPS, sizeof(int));

            // Intermediate buffers - [PARALLEL] expanded for per-group processing
            // Each group gets its own region: group g uses [g * MaxCandidatesPerGroup, (g+1) * MaxCandidatesPerGroup)
            CandidatesBuffer = new ComputeBuffer(Config.TotalCandidates, 32);
            CandidateCountsBuffer = new ComputeBuffer(GPUConstants.MAX_GROUPS, sizeof(int));
            CandidateOffsetsBuffer = new ComputeBuffer(GPUConstants.MAX_GROUPS, sizeof(int));
            IndirectArgsBuffer = new ComputeBuffer(GPUConstants.MAX_GROUPS * 4, sizeof(uint), ComputeBufferType.IndirectArguments);

            // Initialize visible entity offsets (per-group partitions)
            int[] visibleOffsets = new int[GPUConstants.MAX_GROUPS];
            for (int i = 0; i < GPUConstants.MAX_GROUPS; i++)
                visibleOffsets[i] = i * Config.MaxVisiblePerGroup;
            VisibleOffsetsBuffer.SetData(visibleOffsets);

            // Initialize candidate offsets (per-group partitions)
            int[] candidateOffsets = new int[GPUConstants.MAX_GROUPS];
            for (int i = 0; i < GPUConstants.MAX_GROUPS; i++)
                candidateOffsets[i] = i * Config.MaxCandidatesPerGroup;
            CandidateOffsetsBuffer.SetData(candidateOffsets);

            // Initialize indirect args for all 8 groups (each group gets 4 uints: x, y, z, padding)
            uint[] initialArgs = new uint[GPUConstants.MAX_GROUPS * 4];
            for (int i = 0; i < GPUConstants.MAX_GROUPS; i++)
            {
                int baseIdx = i * 4;
                initialArgs[baseIdx + 0] = 1;  // threadGroupCountX
                initialArgs[baseIdx + 1] = 1;  // threadGroupCountY
                initialArgs[baseIdx + 2] = 1;  // threadGroupCountZ
                initialArgs[baseIdx + 3] = 0;  // padding
            }
            IndirectArgsBuffer.SetData(initialArgs);
        }

        private void InitializeTextures()
        {
            // Create 3D fog volume
            FogVolumeTexture = new RenderTexture(Config.FogResolution, Config.FogResolution, 0, RenderTextureFormat.RFloat);
            FogVolumeTexture.dimension = TextureDimension.Tex3D;
            FogVolumeTexture.volumeDepth = Config.FogResolution;
            FogVolumeTexture.enableRandomWrite = true;
            FogVolumeTexture.filterMode = FilterMode.Trilinear;
            FogVolumeTexture.wrapMode = TextureWrapMode.Clamp;
            FogVolumeTexture.Create();

            // Create dummy 1x1x1 texture for unbound island SDFs
            DummyIslandSDF = new Texture3D(1, 1, 1, TextureFormat.RFloat, false);
            DummyIslandSDF.SetPixelData(new float[] { 1.0f }, 0);
            DummyIslandSDF.Apply();
        }

        // ===== Island SDF Management =====

        /// <summary>
        /// Registers an island SDF texture at the specified slot.
        /// </summary>
        public void SetIslandTexture(int slot, Texture3D texture)
        {
            if (slot < 0 || slot >= GPUConstants.MAX_ISLANDS)
            {
                Debug.LogWarning($"[VisibilitySystemRuntime] Invalid island slot {slot}, must be 0-{GPUConstants.MAX_ISLANDS - 1}");
                return;
            }

            IslandTextures[slot] = texture;

            if (texture != null)
                IslandValidityMask |= (1u << slot);
            else
                IslandValidityMask &= ~(1u << slot);

            LastIslandModifiedFrame = Time.frameCount;
            Debug.Log($"[VisibilitySystemRuntime] Island slot {slot} {(texture != null ? "set" : "cleared")}, mask=0x{IslandValidityMask:X}");
        }

        /// <summary>
        /// Clears an island SDF texture slot.
        /// </summary>
        public void ClearIslandTexture(int slot)
        {
            SetIslandTexture(slot, null);
        }

        /// <summary>
        /// Gets an island SDF texture, or null if not set.
        /// </summary>
        public Texture3D GetIslandTexture(int slot)
        {
            if (slot < 0 || slot >= GPUConstants.MAX_ISLANDS)
                return null;
            return IslandTextures[slot];
        }

        /// <summary>
        /// Gets the texture for a slot, or the dummy texture if not set.
        /// Use this when binding to compute shaders.
        /// </summary>
        public Texture3D GetIslandTextureOrDummy(int slot)
        {
            var tex = GetIslandTexture(slot);
            return tex != null ? tex : DummyIslandSDF;
        }

        // ===== Event Firing =====

        /// <summary>
        /// Fires visibility change events. Called by VisibilityEventSystem.
        /// </summary>
        public void FireVisibilityEvent(VisibilityChangeInfo info)
        {
            try
            {
                OnVisibilityChanged?.Invoke(info);

                if (info.EventType == VisibilityEventType.Entered)
                    OnEntitySpotted?.Invoke(info);
                else
                    OnEntityLost?.Invoke(info);
            }
            catch (Exception e)
            {
                Debug.LogError($"[VisibilitySystemRuntime] Event handler error: {e.Message}");
            }
        }

        // ===== Disposal =====

        public void Dispose()
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            IsInitialized = false;

            // Clear events
            OnVisibilityChanged = null;
            OnEntitySpotted = null;
            OnEntityLost = null;

            // Dispose buffers
            GroupDataBuffer?.Dispose();
            UnitContributionsBuffer?.Dispose();
            SeeableEntitiesBuffer?.Dispose();
            IslandsBuffer?.Dispose();
            GroupIslandMasksBuffer?.Dispose();
            VisibleCountsBuffer?.Dispose();
            VisibleEntitiesBuffer?.Dispose();
            VisibleOffsetsBuffer?.Dispose();
            CandidatesBuffer?.Dispose();
            CandidateCountsBuffer?.Dispose();
            CandidateOffsetsBuffer?.Dispose();
            IndirectArgsBuffer?.Dispose();

            // Dispose textures
            if (FogVolumeTexture != null)
            {
                FogVolumeTexture.Release();
                UnityEngine.Object.Destroy(FogVolumeTexture);
                FogVolumeTexture = null;
            }

            if (DummyIslandSDF != null)
            {
                UnityEngine.Object.Destroy(DummyIslandSDF);
                DummyIslandSDF = null;
            }

            // Clear island references
            for (int i = 0; i < GPUConstants.MAX_ISLANDS; i++)
                IslandTextures[i] = null;
            IslandValidityMask = 0;

            Debug.Log("[VisibilitySystemRuntime] Disposed");
        }
    }

    /// <summary>
    /// Information about a visibility state change.
    /// </summary>
    public struct VisibilityChangeInfo
    {
        public int EntityId;
        public byte ViewerGroupId;
        public float Distance;
        public VisibilityEventType EventType;
        public int FrameNumber;
    }
}
