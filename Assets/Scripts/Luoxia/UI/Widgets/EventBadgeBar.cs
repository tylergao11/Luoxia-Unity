using Luoxia.Contracts;
using Luoxia.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Widgets
{
    /// <summary>
    /// "今日有 N 件事待处理" — counts available event cards only.
    /// </summary>
    public sealed class EventBadgeBar : HudWidget
    {
        [SerializeField] private Text labelText;
        [SerializeField] private Button openEventsButton;
        [SerializeField] private string format = "今日有{0}件事待处理";
        [SerializeField] private string emptyFormat = "今日暂无待处理事件";

        private IPlayerIntentSink _intents;
        private System.Action _onOpenEvents;

        public void Configure(IPlayerIntentSink intents, System.Action onOpenEvents)
        {
            _intents = intents;
            _onOpenEvents = onOpenEvents;
        }

        protected override void OnBound()
        {
            if (openEventsButton != null)
            {
                openEventsButton.onClick.AddListener(HandleClick);
            }
        }

        protected override void OnUnbound()
        {
            if (openEventsButton != null)
            {
                openEventsButton.onClick.RemoveListener(HandleClick);
            }
        }

        protected override void Paint(SessionViewDto view)
        {
            var count = 0;
            if (view.event_cards != null)
            {
                for (var i = 0; i < view.event_cards.Count; i++)
                {
                    if (view.event_cards[i].IsAvailable)
                    {
                        count++;
                    }
                }
            }

            if (labelText != null)
            {
                labelText.text = count > 0
                    ? string.Format(format, count)
                    : emptyFormat;
            }

            if (openEventsButton != null)
            {
                openEventsButton.interactable = count > 0;
            }
        }

        private void HandleClick()
        {
            _onOpenEvents?.Invoke();
        }
    }
}
