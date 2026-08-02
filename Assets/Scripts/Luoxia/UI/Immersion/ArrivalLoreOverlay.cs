using System.Collections;
using Luoxia.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Immersion
{
    /// <summary>
    /// Non-modal arrival lore toast on portrait/scene after map.move location change.
    /// Tap dismiss or short auto-fade. Does NOT lock underlying HUD/tabs/input
    /// (root CanvasGroup.blocksRaycasts stays false; only the toast graphic receives taps).
    /// Never routes to NarrativeFramePlayer.
    /// </summary>
    public sealed class ArrivalLoreOverlay : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Text titleText;
        [SerializeField] private Text bodyText;
        [SerializeField] private Button dismissButton;
        [SerializeField] private float fadeSeconds = 0.25f;
        [SerializeField] private float autoFadeSeconds = 4.5f;

        private Coroutine _fadeRoutine;
        private Coroutine _autoFadeRoutine;

        public bool IsVisible =>
            canvasGroup != null && canvasGroup.alpha > 0.05f;

        private void Awake()
        {
            if (dismissButton != null)
            {
                dismissButton.onClick.AddListener(Dismiss);
            }

            HideImmediate();
        }

        private void OnDestroy()
        {
            if (dismissButton != null)
            {
                dismissButton.onClick.RemoveListener(Dismiss);
            }
        }

        public void Show(LoreViewDto entry)
        {
            if (entry == null || canvasGroup == null)
            {
                return;
            }

            var title = entry.ResolveTitle();
            var body = entry.ResolveBody();
            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(body))
            {
                return;
            }

            if (titleText != null)
            {
                titleText.text = title;
            }

            if (bodyText != null)
            {
                bodyText.text = body;
            }

            // No full-screen Graphic: only the toast Image receives taps; underlying HUD stays hittable.
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
            }

            if (_autoFadeRoutine != null)
            {
                StopCoroutine(_autoFadeRoutine);
            }

            _fadeRoutine = StartCoroutine(FadeTo(1f));
            _autoFadeRoutine = StartCoroutine(AutoFade());
        }

        public void Dismiss()
        {
            if (_autoFadeRoutine != null)
            {
                StopCoroutine(_autoFadeRoutine);
                _autoFadeRoutine = null;
            }

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
            }

            _fadeRoutine = StartCoroutine(FadeTo(0f));
        }

        private IEnumerator AutoFade()
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(1.5f, autoFadeSeconds));
            _autoFadeRoutine = null;
            Dismiss();
        }

        private IEnumerator FadeTo(float target)
        {
            if (canvasGroup == null)
            {
                yield break;
            }

            var start = canvasGroup.alpha;
            var duration = Mathf.Clamp(fadeSeconds, 0.15f, 0.35f);
            var t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(start, target, t / duration);
                yield return null;
            }

            canvasGroup.alpha = target;
            if (target <= 0.01f)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            _fadeRoutine = null;
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
    }
}
