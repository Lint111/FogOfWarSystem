using UnityEngine;
using Unity.Entities;
using Unity.Profiling;
using FogOfWar.Visibility.Core;
using FogOfWar.Visibility.Components;
using FogOfWar.Visibility.GPU;

namespace FogOfWar.Visibility.Debugging
{
    /// <summary>
    /// Performance monitoring overlay for the visibility system.
    /// Displays FPS, entity counts, and visibility statistics.
    /// </summary>
    public class VisibilityPerformanceMonitor : MonoBehaviour
    {
        [Header("Display Settings")]
        [Tooltip("Toggle this in Inspector to show/hide overlay")]
        public bool ShowOverlay = true;

        [Header("Position")]
        public TextAnchor Anchor = TextAnchor.UpperLeft;
        public Vector2 Offset = new Vector2(10, 10);

        [Header("Styling")]
        public int FontSize = 14;
        public Color TextColor = Color.white;
        public Color BackgroundColor = new Color(0, 0, 0, 0.7f);

        // Performance tracking
        private float _fps;
        private float _fpsUpdateTimer;
        private int _frameCount;
        private float _msPerFrame;

        // Entity counts
        private int _unitCount;
        private int _seeableCount;
        private int _islandCount;

        // Visibility stats
        private int _candidateCount;
        private int _visibleCount;
        private int[] _perGroupVisible = new int[GPUConstants.MAX_GROUPS];
        private uint _activeGroupMask;
        private uint _islandMask;

        // System state
        private bool _systemReady;
        private string _systemStatus = "Not Ready";

        // Profiler recorders
        private ProfilerRecorder _mainThreadTimeRecorder;

        // GUI
        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private Texture2D _backgroundTexture;

        private void OnEnable()
        {
            _mainThreadTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);

            // Create background texture
            _backgroundTexture = new Texture2D(1, 1);
            _backgroundTexture.SetPixel(0, 0, BackgroundColor);
            _backgroundTexture.Apply();
        }

        private void OnDisable()
        {
            _mainThreadTimeRecorder.Dispose();
            if (_backgroundTexture != null)
                Destroy(_backgroundTexture);
        }

        private void Update()
        {
            // Update FPS every frame for smoother display
            _frameCount++;
            _fpsUpdateTimer += Time.unscaledDeltaTime;
            if (_fpsUpdateTimer >= 0.25f)  // Update 4x per second
            {
                _fps = _frameCount / _fpsUpdateTimer;
                _msPerFrame = 1000f / _fps;
                _frameCount = 0;
                _fpsUpdateTimer = 0;

                // Update all stats
                UpdateSystemStatus();
                UpdateEntityCounts();
                UpdateVisibilityStats();
            }
        }

        private void UpdateSystemStatus()
        {
            var behaviour = VisibilitySystemBehaviour.Instance;
            if (behaviour == null)
            {
                _systemReady = false;
                _systemStatus = "No VisibilitySystemBehaviour";
                return;
            }

            if (!behaviour.IsReady)
            {
                _systemReady = false;
                _systemStatus = "Runtime not initialized";
                return;
            }

            _systemReady = true;
            _systemStatus = "Running";
        }

        private void UpdateEntityCounts()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                _unitCount = 0;
                _seeableCount = 0;
                _islandCount = 0;
                return;
            }

            var em = world.EntityManager;

            // Count units (entities with UnitVision)
            using (var unitQuery = em.CreateEntityQuery(typeof(UnitVision)))
            {
                _unitCount = unitQuery.CalculateEntityCount();
            }

            // Count seeables
            using (var seeableQuery = em.CreateEntityQuery(typeof(Seeable)))
            {
                _seeableCount = seeableQuery.CalculateEntityCount();
            }

            // Count islands
            using (var islandQuery = em.CreateEntityQuery(typeof(EnvironmentIslandDefinition)))
            {
                _islandCount = islandQuery.CalculateEntityCount();
            }
        }

        private void UpdateVisibilityStats()
        {
            if (!_systemReady) return;

            var behaviour = VisibilitySystemBehaviour.Instance;
            if (!behaviour || !behaviour.IsReady) return;

            var runtime = behaviour.Runtime;

            // Get active group mask and island mask
            _activeGroupMask = runtime.ActiveGroupMask;
            _islandMask = runtime.IslandValidityMask;

            // [PARALLEL] Read per-group candidate counts and sum
            if (runtime.CandidateCountsBuffer != null && runtime.CandidateCountsBuffer.IsValid())
            {
                int[] perGroupCandidates = new int[GPUConstants.MAX_GROUPS];
                runtime.CandidateCountsBuffer.GetData(perGroupCandidates);
                _candidateCount = 0;
                foreach (int c in perGroupCandidates)
                    _candidateCount += c;
            }
            else
            {
                _candidateCount = 0;
            }

            // Count total visible across all groups
            if (runtime.VisibleCountsBuffer != null && runtime.VisibleCountsBuffer.IsValid())
            {
                runtime.VisibleCountsBuffer.GetData(_perGroupVisible);
                _visibleCount = 0;
                foreach (int c in _perGroupVisible)
                    _visibleCount += c;
            }
            else
            {
                _visibleCount = 0;
            }

            // Override island count from runtime if available
            if (runtime.IslandCount > 0)
                _islandCount = runtime.IslandCount;
        }

        private void OnGUI()
        {
            if (!ShowOverlay) return;

            // Initialize styles
            if (_boxStyle == null)
            {
                _boxStyle = new GUIStyle(GUI.skin.box);
                _boxStyle.normal.background = _backgroundTexture;
            }

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.fontSize = FontSize;
                _labelStyle.normal.textColor = TextColor;
            }

            // Calculate position
            float width = 300;
            float height = 320;
            float x = Offset.x;
            float y = Offset.y;

            if (Anchor == TextAnchor.UpperRight || Anchor == TextAnchor.MiddleRight || Anchor == TextAnchor.LowerRight)
                x = Screen.width - width - Offset.x;
            if (Anchor == TextAnchor.LowerLeft || Anchor == TextAnchor.LowerCenter || Anchor == TextAnchor.LowerRight)
                y = Screen.height - height - Offset.y;

            // Draw background
            GUI.Box(new Rect(x, y, width, height), "", _boxStyle);

            // Draw content
            float lineHeight = FontSize + 4;
            float currentY = y + 5;

            void DrawLine(string text, Color color)
            {
                var oldColor = _labelStyle.normal.textColor;
                _labelStyle.normal.textColor = color;
                GUI.Label(new Rect(x + 10, currentY, width - 20, lineHeight), text, _labelStyle);
                _labelStyle.normal.textColor = oldColor;
                currentY += lineHeight;
            }

            // Header
            DrawLine("=== Visibility Performance ===", Color.cyan);

            // System status
            Color statusColor = _systemReady ? Color.green : Color.red;
            DrawLine($"Status: {_systemStatus}", statusColor);

            // FPS
            Color fpsColor = _fps >= 60 ? Color.green : (_fps >= 30 ? Color.yellow : Color.red);
            DrawLine($"FPS: {_fps:F1} ({_msPerFrame:F2}ms)", fpsColor);

            // Main thread time
            if (_mainThreadTimeRecorder.Valid)
            {
                double msMain = _mainThreadTimeRecorder.LastValue / 1000000.0;
                DrawLine($"Main Thread: {msMain:F2}ms", TextColor);
            }

            DrawLine("", TextColor); // Spacer

            // Entity counts
            DrawLine("--- Entities ---", Color.cyan);
            DrawLine($"Units: {_unitCount}", TextColor);
            DrawLine($"Seeables: {_seeableCount}", TextColor);
            DrawLine($"Islands: {_islandCount} (mask=0x{_islandMask:X})", TextColor);

            DrawLine("", TextColor); // Spacer

            // Group info
            DrawLine("--- Groups ---", Color.cyan);
            DrawLine($"Active Mask: 0x{_activeGroupMask:X2}", TextColor);

            // Per-group visible counts
            string perGroup = "Visible: ";
            for (int g = 0; g < 4; g++)
            {
                if (g > 0) perGroup += " | ";
                perGroup += $"G{g}:{_perGroupVisible[g]}";
            }
            DrawLine(perGroup, TextColor);

            DrawLine("", TextColor); // Spacer

            // Visibility stats
            DrawLine("--- Visibility ---", Color.cyan);
            DrawLine($"Candidates: {_candidateCount}", TextColor);
            DrawLine($"Total Visible: {_visibleCount}", TextColor);

            // Efficiency ratio
            if (_seeableCount > 0)
            {
                float ratio = (float)_candidateCount / _seeableCount;
                Color ratioColor = ratio > 0 ? Color.green : Color.gray;
                DrawLine($"Candidate Ratio: {ratio:P1}", ratioColor);
            }

            // Controls hint
            DrawLine("", TextColor);
            DrawLine("[Inspector] Toggle overlay", Color.gray);
        }
    }
}
