using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Widgets
{
    /// <summary>
    /// Full-screen blocker for SessionReplica Fatal / severe protocol errors.
    /// Not a toast — Play cannot continue until retry / leave.
    /// terminalToProvision (recoverability=fatal): session retry hidden; button「重新开局」
    /// triggers in-Play POST /provision/new-play (no Exit Play → Editor menu).
    /// </summary>
    public sealed class SessionFatalOverlay : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Text titleText;
        [SerializeField] private Text detailText;
        [SerializeField] private Button retryButton;
        [SerializeField] private Text retryButtonLabel;
        [SerializeField] private Button dismissButton;
        [SerializeField] private float fadeSeconds = 0.2f;

        private System.Action _onRetry;
        private Coroutine _fadeRoutine;
        private bool _allowSessionRetry = true;

        public bool IsVisible =>
            canvasGroup != null && canvasGroup.alpha > 0.01f && canvasGroup.blocksRaycasts;

        public void Configure(System.Action onRetry)
        {
            _onRetry = onRetry;
        }

        private void Awake()
        {
            if (retryButton != null)
            {
                retryButton.onClick.AddListener(HandleRetry);
            }

            if (dismissButton != null)
            {
                dismissButton.onClick.AddListener(Hide);
            }

            HideImmediate();
        }

        private void OnDestroy()
        {
            if (retryButton != null)
            {
                retryButton.onClick.RemoveListener(HandleRetry);
            }

            if (dismissButton != null)
            {
                dismissButton.onClick.RemoveListener(Hide);
            }
        }

        public void Show(string title, string detail, bool allowSessionRetry = true)
        {
            _allowSessionRetry = allowSessionRetry;

            if (titleText != null)
            {
                titleText.text = string.IsNullOrEmpty(title) ? "会话中断" : title;
            }

            if (detailText != null)
            {
                detailText.text = detail ?? string.Empty;
            }

            if (retryButton != null)
            {
                retryButton.gameObject.SetActive(true);
            }

            if (retryButtonLabel != null)
            {
                retryButtonLabel.text = allowSessionRetry ? "重试" : "重新开局";
            }

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
            }

            _fadeRoutine = StartCoroutine(FadeTo(1f, true));
        }

        public void Hide()
        {
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

        private void HandleRetry()
        {
            // Always invoke: Bootstrap maps terminal → provision-only copy; session retry → resync/ready.
            _onRetry?.Invoke();
        }

        private IEnumerator FadeTo(float target, bool interactable)
        {
            if (canvasGroup == null)
            {
                yield break;
            }

            if (interactable)
            {
                canvasGroup.blocksRaycasts = true;
                canvasGroup.interactable = true;
            }

            var start = canvasGroup.alpha;
            var duration = Mathf.Max(0.01f, fadeSeconds);
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
