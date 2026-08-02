using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Widgets
{
    /// <summary>
    /// Simple visible command feedback (not Log-only). Shows pending + failure text.
    /// </summary>
    public sealed class CommandFeedbackHud : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Text statusText;
        [SerializeField] private float visibleSeconds = 3.2f;
        [SerializeField] private float fadeSeconds = 0.2f;

        private Coroutine _hideRoutine;
        private bool _locked;

        public void ShowPending(string commandHint = null)
        {
            _locked = true;
            SetText(string.IsNullOrEmpty(commandHint) ? "命令发送中…" : commandHint);
            ShowImmediate();
            if (_hideRoutine != null)
            {
                StopCoroutine(_hideRoutine);
                _hideRoutine = null;
            }
        }

        public void ClearPending()
        {
            // Only clear pending chrome — never wipe an in-flight ShowError / ShowInfo.
            if (!_locked)
            {
                return;
            }

            _locked = false;
            if (_hideRoutine != null)
            {
                StopCoroutine(_hideRoutine);
                _hideRoutine = null;
            }

            // Drop pending chrome immediately — never leave "命令发送中…" after success.
            SetText(string.Empty);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
            }
        }

        public void ShowError(string message)
        {
            _locked = false;
            SetText(string.IsNullOrEmpty(message) ? "命令失败" : message);
            ShowImmediate();
            if (_hideRoutine != null)
            {
                StopCoroutine(_hideRoutine);
            }

            _hideRoutine = StartCoroutine(HideAfter(visibleSeconds));
        }

        public void ShowInfo(string message)
        {
            if (_locked)
            {
                return;
            }

            SetText(message ?? string.Empty);
            ShowImmediate();
            if (_hideRoutine != null)
            {
                StopCoroutine(_hideRoutine);
            }

            _hideRoutine = StartCoroutine(HideAfter(visibleSeconds));
        }

        private void SetText(string text)
        {
            if (statusText != null)
            {
                statusText.text = text ?? string.Empty;
            }
        }

        private void ShowImmediate()
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        private IEnumerator HideAfter(float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }

            var duration = Mathf.Max(0.01f, fadeSeconds);
            var t = 0f;
            var start = canvasGroup != null ? canvasGroup.alpha : 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = Mathf.Lerp(start, 0f, t / duration);
                }

                yield return null;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }

            _hideRoutine = null;
        }
    }
}
