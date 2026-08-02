using System.Collections;
using Luoxia.Contracts;
using Luoxia.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Features
{
    /// <summary>
    /// Local "开启事件" confirm modal. Later/X dismiss without ClientMessage;
    /// Open → TryTriggerEventCard. Dimmer α≈0.60.
    /// </summary>
    public sealed class EventCardConfirmPanel : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Text titleText;
        [SerializeField] private Text summaryText;
        [SerializeField] private Text costText;
        [SerializeField] private Image portraitImage;
        [SerializeField] private Image portraitRingImage;
        [SerializeField] private Button laterButton;
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Image laterButtonImage;
        [SerializeField] private Image openButtonImage;
        [SerializeField] private Sprite choiceNormalSprite;
        [SerializeField] private Sprite choiceActiveSprite;
        [SerializeField] private float fadeSeconds = 0.2f;

        private IPlayerIntentSink _intents;
        private string _pendingCardId;
        private bool _commandLocked;
        private Coroutine _fadeRoutine;

        public bool IsOpen =>
            canvasGroup != null && canvasGroup.blocksRaycasts && canvasGroup.alpha > 0.01f;

        public void Configure(IPlayerIntentSink intents)
        {
            _intents = intents;
        }

        public void SetCommandLocked(bool locked)
        {
            _commandLocked = locked;
            RefreshButtons();
        }

        private void Awake()
        {
            if (laterButton != null)
            {
                laterButton.onClick.AddListener(DismissLocal);
            }

            if (closeButton != null)
            {
                closeButton.onClick.AddListener(DismissLocal);
            }

            if (openButton != null)
            {
                openButton.onClick.AddListener(HandleOpen);
            }

            ApplyChoiceSprites();
            HideImmediate();
        }

        private void OnDestroy()
        {
            if (laterButton != null)
            {
                laterButton.onClick.RemoveListener(DismissLocal);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(DismissLocal);
            }

            if (openButton != null)
            {
                openButton.onClick.RemoveListener(HandleOpen);
            }
        }

        public void TryOpen(SessionViewDto view, string eventCardId)
        {
            if (string.IsNullOrEmpty(eventCardId) || view?.event_cards == null)
            {
                return;
            }

            EventCardViewDto card = null;
            for (var i = 0; i < view.event_cards.Count; i++)
            {
                var c = view.event_cards[i];
                if (c != null && c.event_card_id == eventCardId && c.IsAvailable)
                {
                    card = c;
                    break;
                }
            }

            if (card == null)
            {
                return;
            }

            _pendingCardId = eventCardId;
            if (titleText != null)
            {
                titleText.text = card.title != null ? card.title.Resolve() : string.Empty;
            }

            if (summaryText != null)
            {
                summaryText.text = card.summary != null ? card.summary.Resolve() : string.Empty;
            }

            if (costText != null)
            {
                var amount = card.CostAmount;
                costText.text = amount > 0
                    ? $"耗行动力 {amount}（发卡时已扣）"
                    : string.Empty;
                costText.gameObject.SetActive(amount > 0);
            }

            // EventCard has no portrait asset — keep chrome ring/frame only (never first face in view).
            KeepPortraitChromeOnly();
            Show();
            RefreshButtons();
        }

        /// <summary>
        /// Close if the pending card is no longer available after SessionView update.
        /// </summary>
        public void OnSessionView(SessionViewDto view)
        {
            if (!IsOpen || string.IsNullOrEmpty(_pendingCardId) || view?.event_cards == null)
            {
                return;
            }

            for (var i = 0; i < view.event_cards.Count; i++)
            {
                var c = view.event_cards[i];
                if (c != null && c.event_card_id == _pendingCardId && c.IsAvailable)
                {
                    return;
                }
            }

            DismissLocal();
        }

        private void KeepPortraitChromeOnly()
        {
            if (portraitImage != null)
            {
                // Do not assign null over chrome; leave Builder frame sprite as-is.
                portraitImage.enabled = portraitImage.sprite != null;
                portraitImage.preserveAspect = true;
                portraitImage.raycastTarget = false;
            }

            if (portraitRingImage != null)
            {
                portraitRingImage.enabled = true;
                portraitRingImage.raycastTarget = false;
            }
        }

        private void HandleOpen()
        {
            if (_commandLocked || string.IsNullOrEmpty(_pendingCardId))
            {
                return;
            }

            _intents?.TryTriggerEventCard(_pendingCardId);
        }

        private void DismissLocal()
        {
            _pendingCardId = null;
            Hide();
        }

        private void RefreshButtons()
        {
            var canAct = !_commandLocked && !string.IsNullOrEmpty(_pendingCardId);
            if (openButton != null)
            {
                openButton.interactable = canAct;
            }

            if (laterButton != null)
            {
                laterButton.interactable = !_commandLocked;
            }

            if (closeButton != null)
            {
                closeButton.interactable = !_commandLocked;
            }
        }

        private void ApplyChoiceSprites()
        {
            if (openButtonImage != null && choiceActiveSprite != null)
            {
                openButtonImage.sprite = choiceActiveSprite;
            }

            if (laterButtonImage != null && choiceNormalSprite != null)
            {
                laterButtonImage.sprite = choiceNormalSprite;
            }

            if (openButton != null)
            {
                openButton.transition = Selectable.Transition.ColorTint;
            }

            if (laterButton != null)
            {
                laterButton.transition = Selectable.Transition.ColorTint;
            }
        }

        private void Show()
        {
            if (canvasGroup == null)
            {
                return;
            }

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
            }

            _fadeRoutine = StartCoroutine(FadeTo(1f, true));
        }

        private void Hide()
        {
            if (canvasGroup == null)
            {
                return;
            }

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
            }

            _fadeRoutine = StartCoroutine(FadeTo(0f, false));
        }

        private void HideImmediate()
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        private IEnumerator FadeTo(float target, bool interactable)
        {
            var duration = Mathf.Clamp(fadeSeconds, 0.15f, 0.25f);
            if (interactable)
            {
                canvasGroup.blocksRaycasts = true;
                canvasGroup.interactable = true;
            }
            else
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            var start = canvasGroup.alpha;
            var t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(start, target, t / duration);
                yield return null;
            }

            canvasGroup.alpha = target;
            if (!interactable)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            _fadeRoutine = null;
        }
    }
}
