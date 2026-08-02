using UnityEngine;
using UnityEngine.EventSystems;
using Luoxia.UI.Screens;

namespace Luoxia.UI.Features
{
    /// <summary>
    /// Horizontal swipe on FeatureDock gesture zone → ActivateFeature (left=event, right=dialogue).
    /// Same ActivateFeature entry as tab buttons (shared pager slide).
    /// </summary>
    public sealed class FeatureSwipeNavigator : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private MainWorldScreen screen;
        [SerializeField] private float minDistancePx = 120f;
        [SerializeField] private float minVelocityPxPerSec = 900f;
        [SerializeField] private float horizontalDominance = 1.5f;

        private Vector2 _startPos;
        private float _startTime;
        private bool _tracking;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData == null)
            {
                return;
            }

            _tracking = true;
            _startPos = eventData.position;
            _startTime = Time.unscaledTime;
        }

        public void OnDrag(PointerEventData eventData)
        {
            // Threshold evaluated on end; keep interface for EventSystem completeness.
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_tracking || eventData == null)
            {
                _tracking = false;
                return;
            }

            _tracking = false;
            HandleHorizontalEnd(_startPos, eventData.position, Time.unscaledTime - _startTime);
        }

        /// <summary>
        /// Called by DragDirectionRelay after a horizontal gesture is resolved on a ScrollRect.
        /// </summary>
        public void HandleHorizontalEnd(Vector2 start, Vector2 end, float durationSec)
        {
            var delta = end - start;
            var absX = Mathf.Abs(delta.x);
            var absY = Mathf.Abs(delta.y);
            if (absX <= absY * horizontalDominance)
            {
                return;
            }

            var duration = Mathf.Max(0.0001f, durationSec);
            var velocity = absX / duration;
            if (absX < minDistancePx && velocity < minVelocityPxPerSec)
            {
                return;
            }

            if (screen == null)
            {
                return;
            }

            // Left swipe → event tab; right swipe → dialogue tab.
            screen.ActivateFeature(delta.x < 0f ? EventFeaturePanel.Id : DialogueFeaturePanel.Id);
        }
    }
}
