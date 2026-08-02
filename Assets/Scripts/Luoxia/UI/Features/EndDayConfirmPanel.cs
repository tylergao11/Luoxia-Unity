using System.Collections;
using System.Collections.Generic;
using System.Text;
using Luoxia.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Features
{
    /// <summary>
    /// Local confirm when EndDay is pressed while available EventCards remain for the current day.
    /// 「去看看」→ dismiss + open Event tab. 「仍要收工」→ invoke continue callback (player_day.end).
    /// </summary>
    public sealed class EndDayConfirmPanel : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Text messageText;
        [SerializeField] private Text titlesText;
        [SerializeField] private Button goLookButton;
        [SerializeField] private Button forceEndButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private float fadeSeconds = 0.2f;

        private System.Action _onForceEnd;
        private System.Action _onGoLook;
        private Coroutine _fadeRoutine;

        public bool IsOpen =>
            canvasGroup != null && canvasGroup.blocksRaycasts && canvasGroup.alpha > 0.01f;

        private void Awake()
        {
            if (goLookButton != null)
            {
                goLookButton.onClick.AddListener(HandleGoLook);
            }

            if (forceEndButton != null)
            {
                forceEndButton.onClick.AddListener(HandleForceEnd);
            }

            if (closeButton != null)
            {
                closeButton.onClick.AddListener(Dismiss);
            }

            HideImmediate();
        }

        private void OnDestroy()
        {
            if (goLookButton != null)
            {
                goLookButton.onClick.RemoveListener(HandleGoLook);
            }

            if (forceEndButton != null)
            {
                forceEndButton.onClick.RemoveListener(HandleForceEnd);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Dismiss);
            }
        }

        public void TryOpen(
            IReadOnlyList<EventCardViewDto> availableCards,
            System.Action onForceEnd,
            System.Action onGoLook)
        {
            if (availableCards == null || availableCards.Count == 0)
            {
                onForceEnd?.Invoke();
                return;
            }

            _onForceEnd = onForceEnd;
            _onGoLook = onGoLook;

            if (messageText != null)
            {
                messageText.text =
                    $"还有 {availableCards.Count} 张事件卡未开启，收工后将过期作废";
            }

            if (titlesText != null)
            {
                var sb = new StringBuilder();
                for (var i = 0; i < availableCards.Count; i++)
                {
                    var card = availableCards[i];
                    var title = card?.title != null ? card.title.Resolve() : string.Empty;
                    if (string.IsNullOrEmpty(title))
                    {
                        continue;
                    }

                    if (sb.Length > 0)
                    {
                        sb.Append('\n');
                    }

                    sb.Append('·').Append(title);
                }

                titlesText.text = sb.ToString();
            }

            Show();
        }

        public void Dismiss()
        {
            _onForceEnd = null;
            _onGoLook = null;
            Hide();
        }

        private void HandleGoLook()
        {
            var go = _onGoLook;
            Dismiss();
            go?.Invoke();
        }

        private void HandleForceEnd()
        {
            var end = _onForceEnd;
            Dismiss();
            end?.Invoke();
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
