using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Luoxia.UI.Features
{
    /// <summary>
    /// On a ScrollRect viewport: vertical drag → ScrollRect; horizontal → FeatureSwipeNavigator.
    /// Must sit on a child that receives the raycast so ScrollRect does not double-handle.
    /// </summary>
    public sealed class DragDirectionRelay : MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IInitializePotentialDragHandler
    {
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private FeatureSwipeNavigator navigator;
        [SerializeField] private float horizontalDominance = 1.5f;
        [SerializeField] private float decideThresholdPx = 8f;

        private enum Axis
        {
            Undecided,
            Horizontal,
            Vertical
        }

        private Axis _axis;
        private Vector2 _startPos;
        private float _startTime;
        private bool _dragging;
        private bool _scrollBegun;

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            scrollRect?.OnInitializePotentialDrag(eventData);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData == null)
            {
                return;
            }

            _dragging = true;
            _scrollBegun = false;
            _axis = Axis.Undecided;
            _startPos = eventData.position;
            _startTime = Time.unscaledTime;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || eventData == null)
            {
                return;
            }

            var delta = eventData.position - _startPos;
            if (_axis == Axis.Undecided)
            {
                if (delta.sqrMagnitude < decideThresholdPx * decideThresholdPx)
                {
                    return;
                }

                var absX = Mathf.Abs(delta.x);
                var absY = Mathf.Abs(delta.y);
                if (absX > absY * horizontalDominance)
                {
                    _axis = Axis.Horizontal;
                }
                else if (absY > absX * horizontalDominance)
                {
                    _axis = Axis.Vertical;
                    if (scrollRect != null)
                    {
                        scrollRect.OnBeginDrag(eventData);
                        _scrollBegun = true;
                    }
                }
                else
                {
                    return;
                }
            }

            if (_axis == Axis.Vertical && scrollRect != null)
            {
                if (!_scrollBegun)
                {
                    scrollRect.OnBeginDrag(eventData);
                    _scrollBegun = true;
                }

                scrollRect.OnDrag(eventData);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            if (eventData == null)
            {
                _axis = Axis.Undecided;
                _scrollBegun = false;
                return;
            }

            if (_axis == Axis.Vertical && scrollRect != null && _scrollBegun)
            {
                scrollRect.OnEndDrag(eventData);
            }
            else if (_axis == Axis.Horizontal && navigator != null)
            {
                navigator.HandleHorizontalEnd(_startPos, eventData.position, Time.unscaledTime - _startTime);
            }

            _axis = Axis.Undecided;
            _scrollBegun = false;
        }
    }
}
