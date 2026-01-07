using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX.SDF;
using FogOfWar.Visibility.Authoring;

namespace FogOfWar.Visibility.Editor.SDFBaking
{
    /// <summary>
    /// Background baking pipeline manager for SDF island textures.
    /// Uses Unity's MeshToSDFBaker from VFX Graph package.
    /// </summary>
    public static class SDFBakeQueue
    {
        private static readonly Queue<BakeRequest> Queue = new();
        private static BakeRequest _currentRequest;
        private static bool _isProcessing;
        private static SDFBakeConfig _config;
        private static MeshToSDFBaker _activeBaker;

        /// <summary>
        /// Event fired when baking starts for an island.
        /// </summary>
        public static event Action<string> OnBakeStarted;

        /// <summary>
        /// Event fired when baking completes for an island.
        /// </summary>
        public static event Action<string, bool> OnBakeFinished;

        /// <summary>
        /// Event fired when all queued bakes are complete.
        /// </summary>
        public static event Action OnBakingComplete;

        /// <summary>
        /// Event fired with progress updates (0-1).
        /// </summary>
        public static event Action<float, string> OnProgressUpdated;

        /// <summary>
        /// Whether the queue is currently processing.
        /// </summary>
        public static bool IsProcessing => _isProcessing;

        /// <summary>
        /// Current queue count.
        /// </summary>
        public static int QueueCount => Queue.Count;

        /// <summary>
        /// Current baking island name (or null if not baking).
        /// </summary>
        public static string CurrentBakingIsland => _currentRequest?.IslandId;

        private static SDFBakeConfig Config
        {
            get
            {
                if (_config == null)
                {
                    _config = SDFBakeConfig.GetOrCreateDefault();
                }
                return _config;
            }
        }

        /// <summary>
        /// Enqueues an island for baking.
        /// </summary>
        public static void Enqueue(IslandSDFContributor contributor)
        {
            if (contributor == null)
            {
                Debug.LogWarning("[SDFBakeQueue] Cannot enqueue null contributor");
                return;
            }

            // Check if already in queue
            foreach (var existing in Queue)
            {
                if (existing.IslandId == contributor.IslandId)
                {
                    Debug.Log($"[SDFBakeQueue] Island {contributor.IslandId} already in queue");
                    return;
                }
            }

            // Don't queue if currently baking this island
            if (_currentRequest != null && _currentRequest.IslandId == contributor.IslandId)
            {
                Debug.Log($"[SDFBakeQueue] Island {contributor.IslandId} is currently baking");
                return;
            }

            var request = new BakeRequest
            {
                Contributor = contributor,
                IslandId = contributor.IslandId,
                Resolution = contributor.OverrideResolution > 0
                    ? contributor.OverrideResolution
                    : Config.ResolutionInt
            };

            Queue.Enqueue(request);
            Debug.Log($"[SDFBakeQueue] Enqueued island {contributor.IslandId} (resolution: {request.Resolution})");
        }

        /// <summary>
        /// Starts processing the queue.
        /// </summary>
        public static void StartProcessing()
        {
            if (_isProcessing)
            {
                Debug.Log("[SDFBakeQueue] Already processing");
                return;
            }

            if (Queue.Count == 0)
            {
                Debug.Log("[SDFBakeQueue] Queue is empty");
                OnBakingComplete?.Invoke();
                return;
            }

            _isProcessing = true;
            EditorApplication.update += ProcessQueue;
            Debug.Log($"[SDFBakeQueue] Starting processing ({Queue.Count} islands)");
        }

        /// <summary>
        /// Stops processing and clears the queue.
        /// </summary>
        public static void StopProcessing()
        {
            _isProcessing = false;
            EditorApplication.update -= ProcessQueue;
            Queue.Clear();
            DisposeBaker();
            _currentRequest = null;
            Debug.Log("[SDFBakeQueue] Processing stopped");
        }

        private static void DisposeBaker()
        {
            _activeBaker?.Dispose();
            _activeBaker = null;
        }

        private static void ProcessQueue()
        {
            // Check if current request is still processing
            if (_currentRequest != null)
            {
                if (_currentRequest.IsComplete)
                {
                    FinalizeCurrentBake();
                }
                return;
            }

            // Get next from queue
            if (Queue.Count == 0)
            {
                _isProcessing = false;
                EditorApplication.update -= ProcessQueue;
                Debug.Log("[SDFBakeQueue] All bakes complete");
                OnBakingComplete?.Invoke();
                return;
            }

            _currentRequest = Queue.Dequeue();
            StartBake(_currentRequest);
        }

        private static void StartBake(BakeRequest request)
        {
            Debug.Log($"[SDFBakeQueue] Starting bake for {request.IslandId}");
            OnBakeStarted?.Invoke(request.IslandId);
            OnProgressUpdated?.Invoke(0f, $"Baking {request.IslandId}...");

            if (request.Contributor == null)
            {
                Debug.LogWarning($"[SDFBakeQueue] Contributor for {request.IslandId} was destroyed");
                request.IsComplete = true;
                request.Success = false;
                return;
            }

            try
            {
                // Get mesh sources and bounds
                var meshFilters = request.Contributor.GetMeshSources();
                if (meshFilters.Length == 0)
                {
                    Debug.LogWarning($"[SDFBakeQueue] No meshes found for island {request.IslandId}");
                    request.IsComplete = true;
                    request.Success = false;
                    return;
                }

                // Calculate local-space bounds (relative to island root)
                var bounds = request.Contributor.CalculateLocalBounds();

                // Add padding
                float padding = bounds.size.magnitude * Config.BoundsPadding;
                bounds.Expand(padding * 2f);

                request.Bounds = bounds;

                // Combine meshes in local space (relative to island root)
                var combinedMesh = CombineMeshes(meshFilters, request.Contributor.transform);
                if (combinedMesh == null)
                {
                    Debug.LogWarning($"[SDFBakeQueue] Failed to combine meshes for island {request.IslandId}");
                    request.IsComplete = true;
                    request.Success = false;
                    return;
                }

                request.CombinedMesh = combinedMesh;

                // Use Unity's MeshToSDFBaker
                BakeWithUnityAPI(request);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SDFBakeQueue] Bake failed for {request.IslandId}: {ex.Message}\n{ex.StackTrace}");
                request.IsComplete = true;
                request.Success = false;
            }
        }

        private static void BakeWithUnityAPI(BakeRequest request)
        {
            OnProgressUpdated?.Invoke(0.3f, $"GPU baking {request.IslandId}...");

            try
            {
                // Create the baker using Unity's VFX SDF API
                _activeBaker = new MeshToSDFBaker(
                    request.Bounds.size,
                    request.Bounds.center,
                    request.Resolution,
                    request.CombinedMesh,
                    Config.SignPassCount,
                    Config.Threshold
                );

                // Perform the bake (GPU-accelerated)
                _activeBaker.BakeSDF();

                OnProgressUpdated?.Invoke(0.7f, $"Extracting SDF {request.IslandId}...");

                // Get the result texture
                var sdfTexture = _activeBaker.SdfTexture;
                if (sdfTexture != null)
                {
                    // Create a copy as Texture3D asset (SdfTexture is a RenderTexture)
                    request.ResultTexture = ConvertToTexture3D(sdfTexture, request.Resolution);
                    request.Success = request.ResultTexture != null;
                }
                else
                {
                    Debug.LogWarning($"[SDFBakeQueue] SDF texture is null for {request.IslandId}");
                    request.Success = false;
                }

                // Cleanup baker
                DisposeBaker();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SDFBakeQueue] Unity SDF baking failed: {ex.Message}");
                request.Success = false;
                DisposeBaker();
            }

            request.IsComplete = true;
        }

        private static Texture3D ConvertToTexture3D(Texture sdfTexture, int resolution)
        {
            // The SdfTexture from MeshToSDFBaker is a Texture3D, we can copy it
            if (sdfTexture is Texture3D tex3D)
            {
                // Create a new Texture3D that we can save as an asset
                var copy = new Texture3D(resolution, resolution, resolution, TextureFormat.RHalf, false);
                Graphics.CopyTexture(tex3D, copy);
                copy.Apply(false, true);
                return copy;
            }

            // If it's a RenderTexture, need async readback
            if (sdfTexture is RenderTexture rt)
            {
                var texture = new Texture3D(resolution, resolution, resolution, TextureFormat.RHalf, false);

                // Synchronous read for editor baking
                var prevRT = RenderTexture.active;
                RenderTexture.active = rt;

                // For 3D textures, we need to read slice by slice
                var tempRT2D = RenderTexture.GetTemporary(resolution, resolution, 0, rt.graphicsFormat);
                var colors = new Color[resolution * resolution * resolution];

                for (int z = 0; z < resolution; z++)
                {
                    Graphics.CopyTexture(rt, z, 0, tempRT2D, 0, 0);
                    RenderTexture.active = tempRT2D;

                    var tex2D = new Texture2D(resolution, resolution, TextureFormat.RHalf, false);
                    tex2D.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                    tex2D.Apply();

                    var sliceColors = tex2D.GetPixels();
                    for (int i = 0; i < sliceColors.Length; i++)
                    {
                        colors[z * resolution * resolution + i] = sliceColors[i];
                    }

                    UnityEngine.Object.DestroyImmediate(tex2D);
                }

                RenderTexture.ReleaseTemporary(tempRT2D);
                RenderTexture.active = prevRT;

                texture.SetPixels(colors);
                texture.Apply(false, true);
                return texture;
            }

            Debug.LogWarning($"[SDFBakeQueue] Unknown texture type: {sdfTexture.GetType()}");
            return null;
        }

        private static Mesh CombineMeshes(MeshFilter[] meshFilters, Transform islandRoot)
        {
            var combineInstances = new List<CombineInstance>();

            // Get inverse of island root to transform to local space
            Matrix4x4 rootWorldToLocal = islandRoot.worldToLocalMatrix;

            foreach (var mf in meshFilters)
            {
                if (mf == null || mf.sharedMesh == null) continue;

                // Transform mesh to island's local space (not world space)
                // localSpace = rootWorldToLocal * childWorldMatrix
                Matrix4x4 localTransform = rootWorldToLocal * mf.transform.localToWorldMatrix;

                combineInstances.Add(new CombineInstance
                {
                    mesh = mf.sharedMesh,
                    transform = localTransform
                });
            }

            if (combineInstances.Count == 0) return null;

            var combinedMesh = new Mesh
            {
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            combinedMesh.CombineMeshes(combineInstances.ToArray(), true, true);
            combinedMesh.RecalculateBounds();

            return combinedMesh;
        }

        private static void FinalizeCurrentBake()
        {
            if (_currentRequest == null) return;

            string islandId = _currentRequest.IslandId;
            bool success = _currentRequest.Success;

            if (success && _currentRequest.ResultTexture != null)
            {
                // Save texture to disk
                Config.EnsureOutputDirectory();
                string outputPath = Config.GetOutputPath(
                    SanitizeFilename(islandId),
                    _currentRequest.Resolution
                );

                AssetDatabase.CreateAsset(_currentRequest.ResultTexture, outputPath);
                AssetDatabase.SaveAssets();

                // Update contributor
                if (_currentRequest.Contributor != null)
                {
                    _currentRequest.Contributor.BakedSDFTexture = _currentRequest.ResultTexture;
                    _currentRequest.Contributor.MarkClean();
                    EditorUtility.SetDirty(_currentRequest.Contributor);
                }

                Debug.Log($"[SDFBakeQueue] Bake complete for {islandId}: {outputPath}");
            }
            else
            {
                Debug.LogWarning($"[SDFBakeQueue] Bake failed for {islandId}");
            }

            // Cleanup combined mesh
            if (_currentRequest.CombinedMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_currentRequest.CombinedMesh);
            }

            OnBakeFinished?.Invoke(islandId, success);
            OnProgressUpdated?.Invoke(1f, $"Completed {islandId}");

            _currentRequest = null;
        }

        private static string SanitizeFilename(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        private class BakeRequest
        {
            public IslandSDFContributor Contributor;
            public string IslandId;
            public int Resolution;
            public Bounds Bounds;
            public Mesh CombinedMesh;
            public Texture3D ResultTexture;
            public bool IsComplete;
            public bool Success;
        }
    }
}
