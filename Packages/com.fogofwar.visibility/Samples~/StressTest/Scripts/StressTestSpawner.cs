using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using FogOfWar.Visibility.Components;
using FogOfWar.Visibility.Core;

namespace FogOfWar.Visibility.Debugging
{
    /// <summary>
    /// Spawns multiple groups of units for stress testing the visibility system.
    /// Uses hybrid approach: ECS entities for visibility + GameObjects for rendering.
    /// </summary>
    public class StressTestSpawner : MonoBehaviour
    {
        [Header("Spawn Settings")]
        [Tooltip("Number of units per group")]
        [Range(1, 1024)]
        public int UnitsPerGroup = 50;

        [Tooltip("Number of groups (max 4 for stress test)")]
        [Range(1, 4)]
        public int GroupCount = 4;

        [Header("Bounds")]
        public Vector3 SpawnAreaMin = new Vector3(-40, 0, -40);
        public Vector3 SpawnAreaMax = new Vector3(40, 0, 40);

        [Header("Unit Settings")]
        public float VisionRadius = 15f;
        public float MoveSpeed = 3f;
        public float UnitHeight = 1.5f;
        public float UnitScale = 0.5f;

        [Header("Seeable Settings")]
        [Tooltip("Fraction of units that are also seeable (0-1)")]
        [Range(0f, 1f)]
        public float SeeableFraction = 0.5f;

        [Header("Visuals")]
        [Tooltip("If null, uses primitive capsules")]
        public Mesh UnitMesh;

        [Header("Debug Visualization")]
        public bool ShowSpawnArea = true;
        public bool ShowVisibilityLines = true;
        [Range(0f, 1f)]
        public float VisibilityLineAlpha = 0.5f;

        // Group colors: Red, Blue, Green, Yellow
        private static readonly Color[] GroupColors = {
            new Color(0.9f, 0.2f, 0.2f),
            new Color(0.2f, 0.4f, 0.9f),
            new Color(0.2f, 0.8f, 0.2f),
            new Color(0.9f, 0.9f, 0.2f)
        };

        private EntityManager _entityManager;
        private List<UnitVisual> _unitVisuals = new List<UnitVisual>();
        private Transform _unitsContainer;
        private Material[] _groupMaterials;
        private Material[] _seenMaterials;

        // Visibility debug data
        private List<VisibilityConnection> _visibilityConnections = new List<VisibilityConnection>();
        private Dictionary<int, Vector3> _entityPositions = new Dictionary<int, Vector3>();

        private struct VisibilityConnection
        {
            public Vector3 ViewerPos;
            public Vector3 SeenPos;
            public byte ViewerGroupId;
        }

        private class UnitVisual
        {
            public Entity Entity;
            public GameObject GameObject;
            public MeshRenderer Renderer;
            public byte GroupId;
            public bool IsSeeable;
            public int EntityId;
        }

        private void Start()
        {
            CreateMaterials();
            SpawnAllGroups();
        }

        private void CreateMaterials()
        {
            _groupMaterials = new Material[4];
            _seenMaterials = new Material[4];

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            for (int i = 0; i < 4; i++)
            {
                _groupMaterials[i] = new Material(shader);
                _groupMaterials[i].color = GroupColors[i];
                _groupMaterials[i].name = $"Group{i}_Normal";

                _seenMaterials[i] = new Material(shader);
                _seenMaterials[i].color = GroupColors[i] * 1.5f;
                _seenMaterials[i].EnableKeyword("_EMISSION");
                _seenMaterials[i].SetColor("_EmissionColor", GroupColors[i] * 0.5f);
                _seenMaterials[i].name = $"Group{i}_Seen";
            }
        }

        [ContextMenu("Spawn All Groups")]
        public void SpawnAllGroups()
        {
            DestroyAllUnits();

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogError("[StressTest] No default world available");
                return;
            }

            _entityManager = world.EntityManager;

            _unitsContainer = new GameObject("SpawnedUnits").transform;
            _unitsContainer.SetParent(transform);

            for (int g = 0; g < GroupCount; g++)
            {
                SpawnGroup((byte)g);
            }

            Debug.Log($"[StressTest] Spawned {_unitVisuals.Count} units across {GroupCount} groups");
        }

        private void SpawnGroup(byte groupId)
        {
            Vector3 quadrantCenter = GetQuadrantCenter(groupId);
            float quadrantRadius = (SpawnAreaMax.x - SpawnAreaMin.x) / 4f;

            var groupContainer = new GameObject($"Group_{groupId}").transform;
            groupContainer.SetParent(_unitsContainer);

            for (int i = 0; i < UnitsPerGroup; i++)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float dist = UnityEngine.Random.Range(0f, quadrantRadius);
                Vector3 pos = quadrantCenter + new Vector3(
                    Mathf.Cos(angle) * dist,
                    UnitHeight,
                    Mathf.Sin(angle) * dist
                );

                pos.x = Mathf.Clamp(pos.x, SpawnAreaMin.x, SpawnAreaMax.x);
                pos.z = Mathf.Clamp(pos.z, SpawnAreaMin.z, SpawnAreaMax.z);

                bool isSeeable = UnityEngine.Random.value < SeeableFraction;
                CreateUnit(pos, groupId, isSeeable, groupContainer);
            }
        }

        private void CreateUnit(Vector3 position, byte groupId, bool isSeeable, Transform parent)
        {
            GameObject go;
            if (UnitMesh != null)
            {
                go = new GameObject($"Unit_G{groupId}_{_unitVisuals.Count}");
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = UnitMesh;
                go.AddComponent<MeshRenderer>();
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = $"Unit_G{groupId}_{_unitVisuals.Count}";
                var col = go.GetComponent<Collider>();
                if (col != null) DestroyImmediate(col);
            }

            go.transform.SetParent(parent);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * UnitScale;

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _groupMaterials[groupId % 4];
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var entity = _entityManager.CreateEntity();
            int entityId = entity.Index;
            _entityManager.SetName(entity, go.name);

            _entityManager.AddComponentData(entity, new LocalToWorld
            {
                Value = float4x4.TRS(position, quaternion.identity, new float3(1, 1, 1))
            });

            _entityManager.AddComponentData(entity, new VisionGroupMembership
            {
                GroupId = groupId
            });

            _entityManager.AddComponentData(entity, new UnitVision
            {
                Radius = VisionRadius,
                Type = VisionType.Sphere,
                ConeHalfAngle = 0,
                SecondaryRadius = 0
            });

            _entityManager.AddComponentData(entity, new RoamingUnit
            {
                TargetPosition = position,
                MoveSpeed = MoveSpeed,
                WaitTimer = 0,
                BoundsMin = (float3)SpawnAreaMin,
                BoundsMax = (float3)SpawnAreaMax,
                RandomSeed = (uint)(UnityEngine.Random.Range(1, int.MaxValue))
            });

            if (isSeeable)
            {
                _entityManager.AddComponentData(entity, new Seeable
                {
                    HeightOffset = 0.5f
                });
            }

            _unitVisuals.Add(new UnitVisual
            {
                Entity = entity,
                GameObject = go,
                Renderer = renderer,
                GroupId = groupId,
                IsSeeable = isSeeable,
                EntityId = entityId
            });
        }

        private Vector3 GetQuadrantCenter(int groupId)
        {
            float cx = (SpawnAreaMin.x + SpawnAreaMax.x) / 2f;
            float cz = (SpawnAreaMin.z + SpawnAreaMax.z) / 2f;
            float qx = (SpawnAreaMax.x - SpawnAreaMin.x) / 4f;
            float qz = (SpawnAreaMax.z - SpawnAreaMin.z) / 4f;

            switch (groupId % 4)
            {
                case 0: return new Vector3(cx - qx, 0, cz - qz);
                case 1: return new Vector3(cx + qx, 0, cz - qz);
                case 2: return new Vector3(cx - qx, 0, cz + qz);
                case 3: return new Vector3(cx + qx, 0, cz + qz);
                default: return new Vector3(cx, 0, cz);
            }
        }

        private void LateUpdate()
        {
            if (_unitVisuals.Count == 0) return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;
            var behaviour = VisibilitySystemBehaviour.Instance;
            bool hasVisibilityData = behaviour != null && behaviour.IsReady;

            // Build entity position lookup and sync transforms
            _entityPositions.Clear();
            HashSet<int> visibleEntityIds = null;

            foreach (var unit in _unitVisuals)
            {
                if (!em.Exists(unit.Entity))
                    continue;

                var ltw = em.GetComponentData<LocalToWorld>(unit.Entity);
                Vector3 pos = ltw.Position;
                unit.GameObject.transform.position = pos;
                _entityPositions[unit.EntityId] = pos;
            }

            // Get visibility data and build connections
            _visibilityConnections.Clear();
            if (hasVisibilityData)
            {
                BuildVisibilityConnections(behaviour.Runtime, em);
                visibleEntityIds = GetVisibleEntityIds(behaviour.Runtime);
            }

            // Update materials based on visibility
            foreach (var unit in _unitVisuals)
            {
                if (!unit.IsSeeable || visibleEntityIds == null)
                    continue;

                bool isSeen = visibleEntityIds.Contains(unit.EntityId);
                var targetMat = isSeen
                    ? _seenMaterials[unit.GroupId % 4]
                    : _groupMaterials[unit.GroupId % 4];

                if (unit.Renderer.sharedMaterial != targetMat)
                    unit.Renderer.sharedMaterial = targetMat;
            }
        }

        private void BuildVisibilityConnections(VisibilitySystemRuntime runtime, EntityManager em)
        {
            if (runtime.VisibleEntitiesBuffer == null || runtime.VisibleCountsBuffer == null)
                return;
            if (runtime.UnitContributionsBuffer == null)
                return;

            int[] counts = new int[GPU.GPUConstants.MAX_GROUPS];
            runtime.VisibleCountsBuffer.GetData(counts);

            // Read unit positions for viewer lookup
            int maxUnits = GPU.GPUConstants.MAX_GROUPS * 256;
            var units = new GPU.UnitSDFContributionGPU[maxUnits];
            runtime.UnitContributionsBuffer.GetData(units);

            // Read group data to get unit start indices
            var groups = new GPU.VisionGroupDataGPU[GPU.GPUConstants.MAX_GROUPS];
            runtime.GroupDataBuffer.GetData(groups);

            // Get MaxVisiblePerGroup from config
            int maxVisiblePerGroup = 1024; // Default
            if (VisibilitySystemBehaviour.Instance?.Config != null)
                maxVisiblePerGroup = VisibilitySystemBehaviour.Instance.Config.MaxVisiblePerGroup;

            // Read each group's section separately (they're at fixed offsets)
            for (int g = 0; g < GPU.GPUConstants.MAX_GROUPS; g++)
            {
                int count = counts[g];
                if (count <= 0) continue;

                int offset = g * maxVisiblePerGroup;
                var groupEntries = new GPU.VisibilityEntryGPU[count];
                runtime.VisibleEntitiesBuffer.GetData(groupEntries, 0, offset, count);

                foreach (var entry in groupEntries)
                {
                    if (entry.entityId <= 0) continue;
                    if (!_entityPositions.TryGetValue(entry.entityId, out Vector3 seenPos))
                        continue;

                    int viewerIdx = entry.seenByUnitIndex;
                    if (viewerIdx < 0 || viewerIdx >= maxUnits) continue;

                    Vector3 viewerPos = units[viewerIdx].position;

                    _visibilityConnections.Add(new VisibilityConnection
                    {
                        ViewerPos = viewerPos,
                        SeenPos = seenPos,
                        ViewerGroupId = (byte)g
                    });
                }
            }
        }

        private HashSet<int> GetVisibleEntityIds(VisibilitySystemRuntime runtime)
        {
            var result = new HashSet<int>();

            if (runtime.VisibleEntitiesBuffer == null || runtime.VisibleCountsBuffer == null)
                return result;

            int[] counts = new int[GPU.GPUConstants.MAX_GROUPS];
            runtime.VisibleCountsBuffer.GetData(counts);

            // Get MaxVisiblePerGroup from config
            int maxVisiblePerGroup = 1024;
            if (VisibilitySystemBehaviour.Instance?.Config != null)
                maxVisiblePerGroup = VisibilitySystemBehaviour.Instance.Config.MaxVisiblePerGroup;

            // Read each group's section separately (fixed offsets per group)
            for (int g = 0; g < GPU.GPUConstants.MAX_GROUPS; g++)
            {
                int count = counts[g];
                if (count <= 0) continue;

                int offset = g * maxVisiblePerGroup;
                var groupEntries = new GPU.VisibilityEntryGPU[count];
                runtime.VisibleEntitiesBuffer.GetData(groupEntries, 0, offset, count);

                foreach (var entry in groupEntries)
                {
                    if (entry.entityId > 0)
                        result.Add(entry.entityId);
                }
            }

            return result;
        }

        [ContextMenu("Destroy All Units")]
        public void DestroyAllUnits()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                var em = world.EntityManager;
                foreach (var unit in _unitVisuals)
                {
                    if (em.Exists(unit.Entity))
                        em.DestroyEntity(unit.Entity);
                }
            }

            foreach (var unit in _unitVisuals)
            {
                if (unit.GameObject != null)
                    DestroyImmediate(unit.GameObject);
            }
            _unitVisuals.Clear();
            _visibilityConnections.Clear();
            _entityPositions.Clear();

            if (_unitsContainer != null)
            {
                DestroyImmediate(_unitsContainer.gameObject);
                _unitsContainer = null;
            }

            Debug.Log("[StressTest] Destroyed all units");
        }

        private void OnDestroy()
        {
            DestroyAllUnits();

            if (_groupMaterials != null)
            {
                foreach (var mat in _groupMaterials)
                    if (mat != null) DestroyImmediate(mat);
            }
            if (_seenMaterials != null)
            {
                foreach (var mat in _seenMaterials)
                    if (mat != null) DestroyImmediate(mat);
            }
        }

        private void OnDrawGizmos()
        {
            // Draw spawn area
            if (ShowSpawnArea)
            {
                Gizmos.color = new Color(1, 1, 0, 0.3f);
                Vector3 center = (SpawnAreaMin + SpawnAreaMax) / 2f;
                Vector3 size = SpawnAreaMax - SpawnAreaMin;
                size.y = 0.1f;
                Gizmos.DrawCube(center, size);

                Gizmos.color = new Color(1, 1, 1, 0.5f);
                Gizmos.DrawWireCube(center, size);

                for (int i = 0; i < 4; i++)
                {
                    Gizmos.color = GroupColors[i];
                    Vector3 qc = GetQuadrantCenter(i);
                    qc.y = 0.5f;
                    Gizmos.DrawWireSphere(qc, 2f);
                }
            }

            // Draw visibility lines
            if (ShowVisibilityLines && Application.isPlaying)
            {
                foreach (var conn in _visibilityConnections)
                {
                    Color lineColor = GroupColors[conn.ViewerGroupId % 4];
                    lineColor.a = VisibilityLineAlpha;
                    Gizmos.color = lineColor;
                    Gizmos.DrawLine(conn.ViewerPos, conn.SeenPos);

                    // Draw small sphere at seen entity
                    Gizmos.DrawWireSphere(conn.SeenPos, 0.3f);
                }
            }
        }
    }

    /// <summary>
    /// Component for units that roam randomly within bounds.
    /// </summary>
    public struct RoamingUnit : IComponentData
    {
        public float3 TargetPosition;
        public float MoveSpeed;
        public float WaitTimer;
        public float3 BoundsMin;
        public float3 BoundsMax;
        public uint RandomSeed;
    }
}
