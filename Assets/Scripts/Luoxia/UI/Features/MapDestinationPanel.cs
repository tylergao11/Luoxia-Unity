using System.Collections.Generic;
using Luoxia.Contracts;
using Luoxia.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Features
{
    /// <summary>
    /// Map destination float: atmosphere stays visible; 45–60% dim scrim under panel.
    /// Scrim eats all clicks; click scrim or close dismisses. At most one float panel.
    /// ChromeLayers.MapFloat — see LayoutSlots/ChromeLayers registry.
    /// </summary>
    public sealed class MapDestinationPanel : LuoxiaView
    {
        private static readonly Color ScrimColor = new Color(0f, 0f, 0f, 0.52f);

        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image scrimImage;
        [SerializeField] private Button scrimButton;
        [SerializeField] private Transform listRoot;
        [SerializeField] private Button closeButton;
        [SerializeField] private Text emptyHintText;
        [SerializeField] private Button destinationButtonPrefab;

        private IPlayerIntentSink _intents;
        private readonly List<Button> _buttons = new List<Button>();

        public bool IsOpen =>
            canvasGroup != null && canvasGroup.blocksRaycasts;

        public void Configure(IPlayerIntentSink intents)
        {
            _intents = intents;
        }

        protected override void OnBound()
        {
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(Close);
            }

            if (scrimButton != null)
            {
                scrimButton.onClick.AddListener(Close);
            }

            ApplyScrimVisual();
            Close();
        }

        protected override void OnUnbound()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
            }

            if (scrimButton != null)
            {
                scrimButton.onClick.RemoveListener(Close);
            }

            ClearButtons();
        }

        public override void OnSessionView(SessionViewDto view)
        {
            if (!IsOpen)
            {
                return;
            }

            Rebuild(view);
        }

        public void Open()
        {
            Rebuild(LatestView);
            ApplyScrimVisual();
            SetVisible(true);
        }

        public void Close()
        {
            SetVisible(false);
        }

        private void ApplyScrimVisual()
        {
            if (scrimImage != null)
            {
                // Fable: 45–60% dim; keep atmosphere (scene) visible underneath.
                scrimImage.color = ScrimColor;
                scrimImage.raycastTarget = true;
            }
        }

        private void Rebuild(SessionViewDto view)
        {
            ClearButtons();
            if (destinationButtonPrefab == null || listRoot == null)
            {
                Debug.LogError(
                    "[MapDestinationPanel] destinationButtonPrefab missing. Rebuild via Luoxia/UI/Build Main World Screen.");
                if (emptyHintText != null)
                {
                    emptyHintText.gameObject.SetActive(true);
                    emptyHintText.text = "无可前往地点";
                }

                return;
            }

            var locations = LoreQuery.CollectVisibleLocations(view);
            var moveTargets = 0;

            for (var i = 0; i < locations.Count; i++)
            {
                var loc = locations[i];
                if (loc == null || loc.IsCurrent)
                {
                    continue;
                }

                moveTargets++;
                var btn = Instantiate(destinationButtonPrefab, listRoot);
                var labelText = btn.GetComponentInChildren<Text>();
                if (labelText != null)
                {
                    labelText.text = loc.DisplayLabel ?? string.Empty;
                }

                var captured = loc.EntityId;
                btn.onClick.AddListener(() =>
                {
                    if (_intents != null && _intents.TryMapMove(captured))
                    {
                        Close();
                    }
                });
                _buttons.Add(btn);
            }

            if (emptyHintText != null)
            {
                emptyHintText.gameObject.SetActive(moveTargets == 0);
                emptyHintText.text = moveTargets == 0 ? "无可前往地点" : string.Empty;
            }
        }

        private void ClearButtons()
        {
            for (var i = 0; i < _buttons.Count; i++)
            {
                if (_buttons[i] != null)
                {
                    Destroy(_buttons[i].gameObject);
                }
            }

            _buttons.Clear();
        }

        private void SetVisible(bool visible)
        {
            if (canvasGroup == null)
            {
                gameObject.SetActive(visible);
                return;
            }

            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }
    }
}
