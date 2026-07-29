using Luoxia.Contracts;
using Luoxia.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Features
{
    public sealed class EventCardItemModel
    {
        public EventCardViewDto Card;
        public string SourceLabel;
        public Sprite Portrait;
    }

    public sealed class EventCardItemView : ListItemView<EventCardItemModel>
    {
        [SerializeField] private Text titleText;
        [SerializeField] private Text summaryText;
        [SerializeField] private Text sourceText;
        [SerializeField] private Image portraitImage;
        [SerializeField] private Button openButton;

        private System.Action<string> _onOpen;

        public void SetOpenHandler(System.Action<string> onOpen)
        {
            _onOpen = onOpen;
        }

        private void Awake()
        {
            if (openButton != null)
            {
                openButton.onClick.AddListener(HandleOpen);
            }
        }

        private void OnDestroy()
        {
            if (openButton != null)
            {
                openButton.onClick.RemoveListener(HandleOpen);
            }
        }

        protected override void OnBind(EventCardItemModel model, int index)
        {
            var card = model.Card;
            if (titleText != null)
            {
                titleText.text = card?.title != null ? card.title.Resolve() : string.Empty;
            }

            if (summaryText != null)
            {
                summaryText.text = card?.summary != null ? card.summary.Resolve() : string.Empty;
            }

            if (sourceText != null)
            {
                sourceText.text = model.SourceLabel ?? string.Empty;
            }

            if (portraitImage != null)
            {
                portraitImage.sprite = model.Portrait;
                portraitImage.enabled = model.Portrait != null;
            }

            if (openButton != null)
            {
                openButton.interactable = card != null && card.IsAvailable;
            }
        }

        private void HandleOpen()
        {
            if (!HasModel || Model.Card == null)
            {
                return;
            }

            _onOpen?.Invoke(Model.Card.event_card_id);
        }
    }
}
