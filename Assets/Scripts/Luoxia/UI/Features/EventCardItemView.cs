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
        [SerializeField] private Text costText;
        [SerializeField] private Image portraitImage;
        [SerializeField] private Image chatBadgeImage;
        [SerializeField] private Button openButton;
        [SerializeField] private Button rowButton;
        [SerializeField] private Image openButtonImage;
        [SerializeField] private Sprite choiceNormalSprite;
        [SerializeField] private Sprite choiceActiveSprite;

        private System.Action<string> _onOpen;

        public void SetOpenHandler(System.Action<string> onOpen)
        {
            _onOpen = onOpen;
            RefreshInteractable();
        }

        private void Awake()
        {
            if (openButton != null)
            {
                openButton.onClick.AddListener(HandleOpen);
            }

            if (rowButton != null)
            {
                rowButton.onClick.AddListener(HandleOpen);
            }

            ApplyPrimaryChoiceStyle();
        }

        private void OnDestroy()
        {
            if (openButton != null)
            {
                openButton.onClick.RemoveListener(HandleOpen);
            }

            if (rowButton != null)
            {
                rowButton.onClick.RemoveListener(HandleOpen);
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

            // Protocol has no event "来源"; never invent one on screen.
            if (sourceText != null)
            {
                sourceText.text = string.Empty;
                sourceText.gameObject.SetActive(false);
            }

            if (costText != null)
            {
                var amount = card != null ? card.CostAmount : 0;
                costText.text = amount > 0 ? $"耗行动力 {amount}（发卡时已扣）" : string.Empty;
                costText.gameObject.SetActive(amount > 0);
            }

            if (portraitImage != null)
            {
                if (model.Portrait != null)
                {
                    portraitImage.sprite = model.Portrait;
                    portraitImage.color = Color.white;
                    portraitImage.enabled = true;
                }
                // No portrait: keep chrome frame already on the Image — do not assign null.
                portraitImage.raycastTarget = false;
            }

            if (chatBadgeImage != null)
            {
                chatBadgeImage.enabled = true;
                chatBadgeImage.raycastTarget = false;
            }

            ApplyPrimaryChoiceStyle();
            RefreshInteractable();
        }

        private void RefreshInteractable()
        {
            var available = HasModel && Model.Card != null && Model.Card.IsAvailable && _onOpen != null;
            if (openButton != null)
            {
                openButton.interactable = available;
            }

            if (rowButton != null)
            {
                rowButton.interactable = available;
            }
        }

        private void ApplyPrimaryChoiceStyle()
        {
            if (openButton == null)
            {
                return;
            }

            // Primary "开启" uses gold active slice + ColorTint (not SpriteSwap).
            if (openButtonImage != null && choiceActiveSprite != null)
            {
                openButtonImage.sprite = choiceActiveSprite;
            }

            openButton.transition = Selectable.Transition.ColorTint;
        }

        private void HandleOpen()
        {
            if (!HasModel || Model.Card == null || _onOpen == null)
            {
                return;
            }

            _onOpen.Invoke(Model.Card.event_card_id);
        }
    }
}
