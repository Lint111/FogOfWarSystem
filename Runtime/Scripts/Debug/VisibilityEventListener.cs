using UnityEngine;
using UnityEngine.Events;
using FogOfWar.Visibility.Core;

namespace FogOfWar.Visibility.Debugging
{
    /// <summary>
    /// MonoBehaviour that listens for visibility events and fires UnityEvents.
    /// Attach to any GameObject to react to visibility changes.
    /// Uses VisibilitySystemBehaviour.Instance.Runtime for events.
    /// </summary>
    public class VisibilityEventListener : MonoBehaviour
    {
        [Header("Filter")]
        [Tooltip("Only listen for events for this vision group (-1 = all groups)")]
        [Range(-1, 7)]
        public int FilterGroupId = 0;

        [Tooltip("Only listen for events involving this entity ID (-1 = all entities)")]
        public int FilterEntityId = -1;

        [Header("Events")]
        [Tooltip("Fired when any entity becomes visible")]
        public UnityEvent<VisibilityChangeInfo> OnEntitySpotted;

        [Tooltip("Fired when any entity is lost from vision")]
        public UnityEvent<VisibilityChangeInfo> OnEntityLost;

        [Tooltip("Fired for any visibility change")]
        public UnityEvent<VisibilityChangeInfo> OnVisibilityChanged;

        [Header("Debug")]
        [Tooltip("Log events to console")]
        public bool LogEvents = true;

        [Tooltip("Show last event info in inspector")]
        public string LastEvent = "";

        private bool _subscribed;

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            // Try to subscribe each frame until successful
            if (!_subscribed)
            {
                TrySubscribe();
            }
        }

        private void TrySubscribe()
        {
            if (_subscribed)
                return;

            var behaviour = VisibilitySystemBehaviour.Instance;
            if (behaviour == null || !behaviour.IsReady)
                return;

            behaviour.Runtime.OnEntitySpotted += HandleEntitySpotted;
            behaviour.Runtime.OnEntityLost += HandleEntityLost;
            behaviour.Runtime.OnVisibilityChanged += HandleVisibilityChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
                return;

            var behaviour = VisibilitySystemBehaviour.Instance;
            if (behaviour?.Runtime != null)
            {
                behaviour.Runtime.OnEntitySpotted -= HandleEntitySpotted;
                behaviour.Runtime.OnEntityLost -= HandleEntityLost;
                behaviour.Runtime.OnVisibilityChanged -= HandleVisibilityChanged;
            }
            _subscribed = false;
        }

        private bool PassesFilter(VisibilityChangeInfo info)
        {
            if (FilterGroupId >= 0 && info.ViewerGroupId != FilterGroupId)
                return false;
            if (FilterEntityId >= 0 && info.EntityId != FilterEntityId)
                return false;
            return true;
        }

        private void HandleEntitySpotted(VisibilityChangeInfo info)
        {
            if (!PassesFilter(info)) return;

            if (LogEvents)
            {
                LastEvent = $"SPOTTED: Entity {info.EntityId} by Group {info.ViewerGroupId} @ {info.Distance:F1}m";
                Debug.Log($"[VisibilityListener] {LastEvent}");
            }

            OnEntitySpotted?.Invoke(info);
        }

        private void HandleEntityLost(VisibilityChangeInfo info)
        {
            if (!PassesFilter(info)) return;

            if (LogEvents)
            {
                LastEvent = $"LOST: Entity {info.EntityId} by Group {info.ViewerGroupId}";
                Debug.Log($"[VisibilityListener] {LastEvent}");
            }

            OnEntityLost?.Invoke(info);
        }

        private void HandleVisibilityChanged(VisibilityChangeInfo info)
        {
            if (!PassesFilter(info)) return;

            OnVisibilityChanged?.Invoke(info);
        }
    }
}
