using System.Linq;
using UnityEditor;
using UnityEngine;
using FogOfWar.Visibility.Authoring;

namespace FogOfWar.Visibility.Editor.SDFBaking
{
    /// <summary>
    /// Editor window for managing SDF island baking.
    /// </summary>
    public class SDFIslandManagerWindow : EditorWindow
    {
        private Vector2 _scrollPosition;
        private SDFBakeConfig _config;
        private bool _showConfig = true;
        private bool _showIslands = true;
        private bool _showConfigProperties = false;

        // For embedded inspector view of config
        private UnityEditor.Editor _configEditor;
        private SerializedObject _configSerializedObject;

        [MenuItem("Window/FogOfWar/SDF Island Manager")]
        public static void ShowWindow()
        {
            var window = GetWindow<SDFIslandManagerWindow>();
            window.titleContent = new GUIContent("SDF Islands");
            window.minSize = new Vector2(350, 400);
            window.Show();
        }

        private void OnEnable()
        {
            _config = SDFBakeConfig.GetOrCreateDefault();
            RebuildConfigEditor();

            // Subscribe to events for repaint
            SDFBakeQueue.OnBakeStarted += OnBakeEvent;
            SDFBakeQueue.OnBakeFinished += OnBakeFinishedEvent;
            SDFBakeQueue.OnProgressUpdated += OnProgressEvent;
            IslandDirtyTracker.OnDebounceStarted += Repaint;
            IslandDirtyTracker.OnDebounceCompleted += Repaint;
        }

        // Event handlers (stored as methods so unsubscription works)
        private void OnBakeEvent(string _) => Repaint();
        private void OnBakeFinishedEvent(string _, bool __) => Repaint();
        private void OnProgressEvent(float _, string __) => Repaint();

        private void RebuildConfigEditor()
        {
            // Cleanup old editor
            if (_configEditor != null)
            {
                DestroyImmediate(_configEditor);
                _configEditor = null;
            }

            // Create new editor if config exists
            if (_config != null)
            {
                _configEditor = UnityEditor.Editor.CreateEditor(_config);
                _configSerializedObject = new SerializedObject(_config);
            }
        }

        private void OnDisable()
        {
            // Cleanup editor
            if (_configEditor != null)
            {
                DestroyImmediate(_configEditor);
                _configEditor = null;
            }

            // Unsubscribe
            SDFBakeQueue.OnBakeStarted -= OnBakeEvent;
            SDFBakeQueue.OnBakeFinished -= OnBakeFinishedEvent;
            SDFBakeQueue.OnProgressUpdated -= OnProgressEvent;
            IslandDirtyTracker.OnDebounceStarted -= Repaint;
            IslandDirtyTracker.OnDebounceCompleted -= Repaint;
        }

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawHeader();
            DrawStatusBar();
            EditorGUILayout.Space(10);

            _showConfig = EditorGUILayout.BeginFoldoutHeaderGroup(_showConfig, "Configuration");
            if (_showConfig)
            {
                DrawConfiguration();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(5);

            _showIslands = EditorGUILayout.BeginFoldoutHeaderGroup(_showIslands, "Islands");
            if (_showIslands)
            {
                DrawIslandList();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(10);
            DrawActions();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("SDF Island Manager", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (SDFBakeQueue.IsProcessing)
            {
                GUI.color = Color.yellow;
                GUILayout.Label("Baking...", EditorStyles.miniLabel);
                GUI.color = Color.white;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatusBar()
        {
            var trackedCount = IslandDirtyTracker.TrackedIslands.Count;
            var dirtyCount = IslandDirtyTracker.GetDirtyIslands().Count;
            var queueCount = SDFBakeQueue.QueueCount;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            GUILayout.Label($"Islands: {trackedCount}", EditorStyles.miniLabel);
            GUILayout.Label("|", EditorStyles.miniLabel);

            if (dirtyCount > 0)
            {
                GUI.color = Color.yellow;
                GUILayout.Label($"Dirty: {dirtyCount}", EditorStyles.miniLabel);
                GUI.color = Color.white;
            }
            else
            {
                GUILayout.Label("All clean", EditorStyles.miniLabel);
            }

            GUILayout.Label("|", EditorStyles.miniLabel);
            GUILayout.Label($"Queue: {queueCount}", EditorStyles.miniLabel);

            if (IslandDirtyTracker.IsDebouncing)
            {
                GUILayout.Label("|", EditorStyles.miniLabel);
                GUI.color = Color.cyan;
                GUILayout.Label($"Debounce: {IslandDirtyTracker.DebounceTimeRemaining:F0}s", EditorStyles.miniLabel);
                GUI.color = Color.white;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawConfiguration()
        {
            EditorGUI.indentLevel++;

            // Config asset field with drag-drop and search picker
            EditorGUI.BeginChangeCheck();
            var newConfig = (SDFBakeConfig)EditorGUILayout.ObjectField(
                "Config Asset",
                _config,
                typeof(SDFBakeConfig),
                false
            );
            if (EditorGUI.EndChangeCheck())
            {
                if (newConfig != _config)
                {
                    _config = newConfig;
                    RebuildConfigEditor();
                }
            }

            if (_config == null)
            {
                EditorGUILayout.HelpBox("No config found. Drag a config asset above or click 'Create Config'.", MessageType.Warning);
                if (GUILayout.Button("Create Config"))
                {
                    _config = SDFBakeConfig.GetOrCreateDefault();
                    RebuildConfigEditor();
                }
            }
            else
            {
                // Collapsible properties section
                EditorGUILayout.Space(3);
                _showConfigProperties = EditorGUILayout.Foldout(_showConfigProperties, "Properties", true, EditorStyles.foldoutHeader);

                if (_showConfigProperties)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    if (_configEditor != null)
                    {
                        // Draw the embedded inspector
                        _configEditor.OnInspectorGUI();
                    }

                    EditorGUILayout.EndVertical();
                }
            }

            EditorGUI.indentLevel--;
        }

        private void DrawIslandList()
        {
            EditorGUI.indentLevel++;

            var islands = IslandDirtyTracker.TrackedIslands;

            if (islands.Count == 0)
            {
                EditorGUILayout.HelpBox("No islands registered. Add IslandSDFContributor components to island GameObjects.", MessageType.Info);
            }
            else
            {
                foreach (var kvp in islands.OrderBy(x => x.Key))
                {
                    DrawIslandEntry(kvp.Key, kvp.Value);
                }
            }

            EditorGUI.indentLevel--;
        }

        private void DrawIslandEntry(string islandId, IslandDirtyTracker.IslandTrackingData data)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            // Status indicator
            var contributor = data.Contributor;
            if (contributor == null)
            {
                GUI.color = Color.red;
                GUILayout.Label("X", GUILayout.Width(16));
            }
            else if (contributor.IsDirty)
            {
                GUI.color = Color.yellow;
                GUILayout.Label("*", GUILayout.Width(16));
            }
            else
            {
                GUI.color = Color.green;
                GUILayout.Label("O", GUILayout.Width(16));
            }
            GUI.color = Color.white;

            // Island name (clickable)
            if (GUILayout.Button(islandId, EditorStyles.linkLabel))
            {
                if (contributor != null)
                {
                    Selection.activeGameObject = contributor.gameObject;
                    EditorGUIUtility.PingObject(contributor.gameObject);
                }
            }

            GUILayout.FlexibleSpace();

            // Slot
            if (contributor != null)
            {
                string slotText = contributor.TextureSlot >= 0 ? $"Slot {contributor.TextureSlot}" : "Auto";
                GUILayout.Label(slotText, EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();

            // Details row
            if (contributor != null)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(20);

                // Last bake time
                if (!string.IsNullOrEmpty(contributor.LastBakeTime))
                {
                    GUILayout.Label($"Last: {contributor.LastBakeTime}", EditorStyles.miniLabel);
                }
                else
                {
                    GUILayout.Label("Never baked", EditorStyles.miniLabel);
                }

                GUILayout.FlexibleSpace();

                // Bake button
                GUI.enabled = !SDFBakeQueue.IsProcessing || SDFBakeQueue.CurrentBakingIsland != islandId;
                if (GUILayout.Button("Bake", EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    SDFBakeQueue.Enqueue(contributor);
                    SDFBakeQueue.StartProcessing();
                }
                GUI.enabled = true;

                EditorGUILayout.EndHorizontal();

                // Show baked texture if exists
                if (contributor.BakedSDFTexture != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(20);
                    EditorGUILayout.ObjectField(contributor.BakedSDFTexture, typeof(Texture3D), false);
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawActions()
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = IslandDirtyTracker.GetDirtyIslands().Count > 0 && !SDFBakeQueue.IsProcessing;
            if (GUILayout.Button("Bake All Dirty"))
            {
                IslandDirtyTracker.QueueAllDirtyIslandsNow();
                SDFBakeQueue.StartProcessing();
            }
            GUI.enabled = true;

            GUI.enabled = SDFBakeQueue.IsProcessing;
            if (GUILayout.Button("Stop Baking"))
            {
                SDFBakeQueue.StopProcessing();
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Refresh Islands"))
            {
                // Find all IslandSDFContributor in scene
                var contributors = Object.FindObjectsByType<IslandSDFContributor>(FindObjectsSortMode.None);
                foreach (var c in contributors)
                {
                    // Triggering OnEnable will register them
                    c.enabled = false;
                    c.enabled = true;
                }
                Repaint();
            }

            if (GUILayout.Button("Mark All Dirty"))
            {
                foreach (var kvp in IslandDirtyTracker.TrackedIslands)
                {
                    kvp.Value.Contributor?.MarkDirty();
                }
                Repaint();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // Danger zone
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button("Clear All Baked Files"))
            {
                if (EditorUtility.DisplayDialog("Clear Baked SDFs",
                    "This will delete all baked SDF files. Are you sure?",
                    "Delete", "Cancel"))
                {
                    ClearAllBakedFiles();
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        private void ClearAllBakedFiles()
        {
            if (_config == null) return;

            string folder = _config.FullOutputPath;
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.Log("[SDFIslandManager] No baked files folder found");
                return;
            }

            var guids = AssetDatabase.FindAssets("t:Texture3D", new[] { folder });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.Refresh();

            // Mark all islands dirty
            foreach (var kvp in IslandDirtyTracker.TrackedIslands)
            {
                if (kvp.Value.Contributor != null)
                {
                    kvp.Value.Contributor.BakedSDFTexture = null;
                    kvp.Value.Contributor.MarkDirty();
                }
            }

            Debug.Log($"[SDFIslandManager] Cleared {guids.Length} baked SDF files");
        }

        private void Update()
        {
            // Force repaint during baking for progress updates
            if (SDFBakeQueue.IsProcessing || IslandDirtyTracker.IsDebouncing)
            {
                Repaint();
            }
        }
    }
}
