using System.Collections.Generic;
using Luoxia.Contracts;
using Luoxia.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Features
{
    /// <summary>
    /// Event tab: available EventCards. Row/开启 opens local confirm modal;
    /// 全部开启 still triggers through IPlayerIntentSink only.
    /// </summary>
    public sealed class EventFeaturePanel : FeaturePanel
    {
        public const string Id = "event";

        protected override string ResolveDefaultFeatureId() => Id;

        [SerializeField] private EventCardItemView itemPrefab;
        [SerializeField] private Transform contentRoot;
        [SerializeField] private Text headerCountText;
        [SerializeField] private Button openAllButton;
        [SerializeField] private EventCardConfirmPanel confirmPanel;
        [SerializeField] private string countFormat = "待开启 {0} 件";

        private ListViewController<EventCardItemModel, EventCardItemView> _list;
        private IPlayerIntentSink _intents;
        private bool _commandInteractable = true;
        private int _availableCount;

        public void Configure(IPlayerIntentSink intents, EventCardConfirmPanel confirm = null)
        {
            _intents = intents;
            if (confirm != null)
            {
                confirmPanel = confirm;
            }
        }

        public void SetCommandInteractable(bool interactable)
        {
            _commandInteractable = interactable;
            if (openAllButton != null)
            {
                openAllButton.interactable = _commandInteractable && _availableCount > 0;
            }

            ApplyOpenHandlers();
        }

        protected override void Awake()
        {
            base.Awake();
            if (itemPrefab != null && contentRoot != null)
            {
                _list = new ListViewController<EventCardItemModel, EventCardItemView>(itemPrefab, contentRoot);
            }
        }

        protected override void OnBound()
        {
            if (openAllButton != null)
            {
                openAllButton.onClick.AddListener(HandleOpenAll);
            }
        }

        protected override void OnUnbound()
        {
            if (openAllButton != null)
            {
                openAllButton.onClick.RemoveListener(HandleOpenAll);
            }

            _list?.Clear();
        }

        public override void OnSessionView(SessionViewDto view)
        {
            if (view == null || _list == null)
            {
                return;
            }

            var models = new List<EventCardItemModel>();
            if (view.event_cards != null)
            {
                for (var i = 0; i < view.event_cards.Count; i++)
                {
                    var card = view.event_cards[i];
                    if (card == null || !card.IsAvailable)
                    {
                        continue;
                    }

                    models.Add(new EventCardItemModel
                    {
                        Card = card,
                        SourceLabel = string.Empty,
                        Portrait = null
                    });
                }
            }

            _availableCount = models.Count;
            _list.SetItems(models);
            ApplyOpenHandlers();
            confirmPanel?.OnSessionView(view);

            if (headerCountText != null)
            {
                headerCountText.text = string.Format(countFormat, models.Count);
            }

            if (openAllButton != null)
            {
                openAllButton.interactable = _commandInteractable && models.Count > 0;
            }
        }

        private void ApplyOpenHandlers()
        {
            if (_list == null)
            {
                return;
            }

            for (var i = 0; i < _list.ActiveItems.Count; i++)
            {
                _list.ActiveItems[i].SetOpenHandler(_commandInteractable ? HandleOpenOne : null);
            }
        }

        private void HandleOpenOne(string eventCardId)
        {
            if (!_commandInteractable)
            {
                return;
            }

            if (confirmPanel == null)
            {
                Debug.LogError(
                    "[EventFeaturePanel] EventCardConfirmPanel missing. Rebuild via Luoxia/UI/Build Main World Screen.");
                return;
            }

            var view = LatestView;
            if (view == null)
            {
                return;
            }

            confirmPanel.TryOpen(view, eventCardId);
        }

        private void HandleOpenAll()
        {
            if (!_commandInteractable)
            {
                return;
            }

            _intents?.TryTriggerAllAvailableEventCards();
        }
    }
}
