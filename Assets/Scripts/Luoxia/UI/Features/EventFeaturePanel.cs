using System.Collections.Generic;
using Luoxia.Contracts;
using Luoxia.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Features
{
    /// <summary>
    /// Event tab: available EventCards. Trigger goes through IPlayerIntentSink only.
    /// </summary>
    public sealed class EventFeaturePanel : FeaturePanel
    {
        public const string Id = "event";

        protected override string ResolveDefaultFeatureId() => Id;

        [SerializeField] private EventCardItemView itemPrefab;
        [SerializeField] private Transform contentRoot;
        [SerializeField] private Text headerCountText;
        [SerializeField] private Button openAllButton;
        [SerializeField] private string countFormat = "待开启 {0} 件";

        private ListViewController<EventCardItemModel, EventCardItemView> _list;
        private IPlayerIntentSink _intents;

        public void Configure(IPlayerIntentSink intents)
        {
            _intents = intents;
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
                        SourceLabel = "来源: 世界"
                    });
                }
            }

            _list.SetItems(models);
            for (var i = 0; i < _list.ActiveItems.Count; i++)
            {
                _list.ActiveItems[i].SetOpenHandler(HandleOpenOne);
            }

            if (headerCountText != null)
            {
                headerCountText.text = string.Format(countFormat, models.Count);
            }

            if (openAllButton != null)
            {
                openAllButton.interactable = models.Count > 0;
            }
        }

        private void HandleOpenOne(string eventCardId)
        {
            _intents?.TryTriggerEventCard(eventCardId);
        }

        private void HandleOpenAll()
        {
            _intents?.TryTriggerAllAvailableEventCards();
        }
    }
}
