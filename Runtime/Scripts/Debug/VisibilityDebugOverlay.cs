using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using FogOfWar.Visibility.Components;
using FogOfWar.Visibility.Query;
using FogOfWar.Visibility.GPU;

namespace FogOfWar.Visibility.Debugging
{
    /// <summary>
    /// Debug overlay showing visibility relationships between units and seeables.
    /// Draws gizmos showing line-of-sight and visibility status.
    /// </summary>
    [ExecuteAlways]
    public class VisibilityDebugOverlay : MonoBehaviour
    {
        [Header("Display Settings")]
        [Tooltip("Which vision group to visualize (0 = player)")]
        [Range(0, 7)]
        public int GroupToVisualize = 0;

        [Tooltip("Show the debug visualization")]
        public bool IsVisible = true;

        [Tooltip("Draw visibility lines from units to visible targets")]
        public bool ShowVisibilityLines = true;

        [Tooltip("Draw spheres at visible seeable positions")]
        public bool ShowVisibleMarkers = true;

        [Tooltip("Draw spheres at unit positions")]
        public bool ShowUnitMarkers = true;

        [Header("Colors")]
        [Tooltip("Color for units providing vision")]
        public Color UnitColor = new Color(0.2f, 0.8f, 0.2f, 0.8f);

        [Tooltip("Color for visible seeables")]
        public Color VisibleSeeableColor = new Color(1f, 0.9f, 0.2f, 0.8f);

        [Tooltip("Color for non-visible seeables (within range but occluded)")]
        public Color OccludedSeeableColor = new Color(0.8f, 0.2f, 0.2f, 0.5f);

        [Tooltip("Color for visibility lines")]
        public Color VisibilityLineColor = new Color(0.2f, 1f, 0.2f, 0.6f);

        [Header("Marker Sizes")]
        public float UnitMarkerSize = 0.5f;
        public float SeeableMarkerSize = 0.4f;

        [Header("Debug Info")]
        [Tooltip("Log visibility info to console")]
        public bool LogVisibilityInfo = false;

        private VisibilityQueryData _queryData;
        private bool _hasValidData;
        private int _lastVisibleCount;
        private float _lastLogTime;

        // Cached entity data for gizmo drawing
        private NativeArray<float3> _unitPositions;
        private NativeArray<float3> _seeablePositions;
        private NativeArray<int> _seeableEntityIds;
        private NativeArray<byte> _seeableGroups;
        private bool _arraysAllocated;

        private void OnEnable()
        {
            AllocateArrays();
        }

        private void OnDisable()
        {
            DisposeArrays();
        }

        private void OnDestroy()
        {
            DisposeArrays();
        }

        private void AllocateArrays()
        {
            // After domain reload, arrays may be invalid even if _arraysAllocated was true
            // Check IsCreated to ensure we have valid arrays
            if (_arraysAllocated && _unitPositions.IsCreated) return;

            // Dispose any stale arrays first
            try
            {
                if (_unitPositions.IsCreated) _unitPositions.Dispose();
                if (_seeablePositions.IsCreated) _seeablePositions.Dispose();
                if (_seeableEntityIds.IsCreated) _seeableEntityIds.Dispose();
                if (_seeableGroups.IsCreated) _seeableGroups.Dispose();
            }
            catch (System.Exception) { }

            _unitPositions = new NativeArray<float3>(256, Allocator.Persistent);
            _seeablePositions = new NativeArray<float3>(256, Allocator.Persistent);
            _seeableEntityIds = new NativeArray<int>(256, Allocator.Persistent);
            _seeableGroups = new NativeArray<byte>(256, Allocator.Persistent);
            _arraysAllocated = true;
        }

        private void DisposeArrays()
        {
            if (!_arraysAllocated) return;

            try
            {
                if (_unitPositions.IsCreated) _unitPositions.Dispose();
                if (_seeablePositions.IsCreated) _seeablePositions.Dispose();
                if (_seeableEntityIds.IsCreated) _seeableEntityIds.Dispose();
                if (_seeableGroups.IsCreated) _seeableGroups.Dispose();
            }
            catch (System.Exception)
            {
                // Ignore disposal errors during domain reload
            }
            _arraysAllocated = false;
        }

        private void Update()
        {
            if (!IsVisible) return;

            TryGetVisibilityData();

            // Log visibility info periodically
            if (LogVisibilityInfo && Time.time - _lastLogTime > 1f)
            {
                _lastLogTime = Time.time;
                if (_hasValidData)
                {
                    int visibleCount = VisibilityQuery.GetVisibleCount(_queryData, GroupToVisualize);
                    Debug.Log($"[VisibilityDebug] Group {GroupToVisualize}: {visibleCount} visible, {_cachedUnitCount} units, {_cachedSeeableCount} seeables");
                    _lastVisibleCount = visibleCount;
                }
                else
                {
                    Debug.Log($"[VisibilityDebug] No valid data yet. QueryData exists: {_queryData.Results.IsCreated}, IsValid: {_queryData.IsValid}");
                }
            }
        }

        private void TryGetVisibilityData()
        {
            _hasValidData = false;

            try
            {
                var world = World.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated)
                    return;

                var em = world.EntityManager;

                // Get visibility query data
                var queryDataQuery = em.CreateEntityQuery(ComponentType.ReadOnly<VisibilityQueryData>());
                if (!queryDataQuery.IsEmptyIgnoreFilter)
                {
                    using var entities = queryDataQuery.ToEntityArray(Allocator.Temp);
                    if (entities.Length == 1)
                    {
                        _queryData = em.GetComponentData<VisibilityQueryData>(entities[0]);
                        _hasValidData = _queryData.IsValid && _queryData.Results.IsCreated;
                    }
                }
                queryDataQuery.Dispose();

                // Cache entity positions for gizmo drawing
                if (_hasValidData && Application.isPlaying)
                {
                    CacheEntityPositions(em);
                }
            }
            catch (System.Exception)
            {
                _hasValidData = false;
            }
        }

        private int _cachedUnitCount;
        private int _cachedSeeableCount;

        private void CacheEntityPositions(EntityManager em)
        {
            // Cache unit positions for the visualized group
            var unitQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitVision>(),
                ComponentType.ReadOnly<VisionGroupMembership>(),
                ComponentType.ReadOnly<Unity.Transforms.LocalToWorld>()
            );

            _cachedUnitCount = 0;
            using (var memberships = unitQuery.ToComponentDataArray<VisionGroupMembership>(Allocator.Temp))
            using (var transforms = unitQuery.ToComponentDataArray<Unity.Transforms.LocalToWorld>(Allocator.Temp))
            {
                for (int i = 0; i < memberships.Length && _cachedUnitCount < _unitPositions.Length; i++)
                {
                    if (memberships[i].GroupId == GroupToVisualize)
                    {
                        _unitPositions[_cachedUnitCount++] = transforms[i].Position;
                    }
                }
            }
            unitQuery.Dispose();

            // Cache seeable positions
            var seeableQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<Seeable>(),
                ComponentType.ReadOnly<VisionGroupMembership>(),
                ComponentType.ReadOnly<Unity.Transforms.LocalToWorld>()
            );

            _cachedSeeableCount = 0;
            using (var entities = seeableQuery.ToEntityArray(Allocator.Temp))
            using (var seeables = seeableQuery.ToComponentDataArray<Seeable>(Allocator.Temp))
            using (var memberships = seeableQuery.ToComponentDataArray<VisionGroupMembership>(Allocator.Temp))
            using (var transforms = seeableQuery.ToComponentDataArray<Unity.Transforms.LocalToWorld>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length && _cachedSeeableCount < _seeablePositions.Length; i++)
                {
                    _seeablePositions[_cachedSeeableCount] = transforms[i].Position + new float3(0, seeables[i].HeightOffset, 0);
                    _seeableEntityIds[_cachedSeeableCount] = entities[i].Index;
                    _seeableGroups[_cachedSeeableCount] = memberships[i].GroupId;
                    _cachedSeeableCount++;
                }
            }
            seeableQuery.Dispose();
        }

        private void OnDrawGizmos()
        {
            if (!IsVisible || !_hasValidData)
                return;

            DrawUnitMarkers();
            DrawSeeableMarkers();
            DrawVisibilityLines();
        }

        private void DrawUnitMarkers()
        {
            if (!ShowUnitMarkers) return;

            Gizmos.color = UnitColor;
            for (int i = 0; i < _cachedUnitCount; i++)
            {
                Vector3 pos = _unitPositions[i];
                Gizmos.DrawWireSphere(pos, UnitMarkerSize);

                // Draw a small solid sphere at center
                Gizmos.color = new Color(UnitColor.r, UnitColor.g, UnitColor.b, 0.3f);
                Gizmos.DrawSphere(pos, UnitMarkerSize * 0.3f);
                Gizmos.color = UnitColor;
            }
        }

        private void DrawSeeableMarkers()
        {
            if (!ShowVisibleMarkers) return;

            for (int i = 0; i < _cachedSeeableCount; i++)
            {
                // Skip seeables from our own group (can't see ourselves)
                if (_seeableGroups[i] == GroupToVisualize)
                    continue;

                Vector3 pos = _seeablePositions[i];
                int entityId = _seeableEntityIds[i];

                // Check if this entity is visible to our group
                bool isVisible = VisibilityQuery.IsEntityIdVisibleToGroup(_queryData, entityId, GroupToVisualize);

                if (isVisible)
                {
                    // Yellow for visible
                    Gizmos.color = VisibleSeeableColor;
                    Gizmos.DrawSphere(pos, SeeableMarkerSize);
                    Gizmos.DrawWireSphere(pos, SeeableMarkerSize * 1.5f);
                }
                else
                {
                    // Red for occluded/not visible
                    Gizmos.color = OccludedSeeableColor;
                    Gizmos.DrawWireSphere(pos, SeeableMarkerSize);
                }
            }
        }

        private void DrawVisibilityLines()
        {
            if (!ShowVisibilityLines || _cachedUnitCount == 0) return;

            Gizmos.color = VisibilityLineColor;

            // Get visible entries for this group
            int visibleCount = VisibilityQuery.GetVisibleCount(_queryData, GroupToVisualize);
            if (visibleCount == 0) return;

            // Find the closest unit position to draw lines from
            // (In a real implementation, you'd draw from the specific unit that saw the target)
            Vector3 avgUnitPos = Vector3.zero;
            for (int i = 0; i < _cachedUnitCount; i++)
            {
                avgUnitPos += (Vector3)_unitPositions[i];
            }
            avgUnitPos /= _cachedUnitCount;

            // Draw lines to all visible seeables
            for (int i = 0; i < _cachedSeeableCount; i++)
            {
                if (_seeableGroups[i] == GroupToVisualize)
                    continue;

                int entityId = _seeableEntityIds[i];
                if (VisibilityQuery.IsEntityIdVisibleToGroup(_queryData, entityId, GroupToVisualize))
                {
                    Vector3 targetPos = _seeablePositions[i];

                    // Find nearest unit to this target
                    Vector3 nearestUnit = avgUnitPos;
                    float nearestDist = float.MaxValue;
                    for (int u = 0; u < _cachedUnitCount; u++)
                    {
                        float dist = math.distance(_unitPositions[u], (float3)targetPos);
                        if (dist < nearestDist)
                        {
                            nearestDist = dist;
                            nearestUnit = _unitPositions[u];
                        }
                    }

                    Gizmos.DrawLine(nearestUnit, targetPos);
                }
            }
        }
    }
}
