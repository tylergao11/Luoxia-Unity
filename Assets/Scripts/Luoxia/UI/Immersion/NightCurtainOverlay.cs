using System;
using System.Collections;
using Luoxia.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Immersion
{
    /// <summary>
    /// Fullscreen Host night-curtain choreography when day_cycle.day increments after
    /// player_day.end. Shows nightfall lore text from SessionView lore_id diff.
    /// Pure Host — no new Server message. Locks input until curtain completes.
    /// </summary>
    public sealed class NightCurtainOverlay : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image curtainImage;
        [SerializeField] private Text titleText;
        [SerializeField] private Text bodyText;
        [SerializeField] private Button advanceButton;
        [SerializeField] private float fadeInSeconds = 0.55f;
        [SerializeField] private float holdSeconds = 1.2f;
        [SerializeField] private float fadeOutSeconds = 0.65f;

        private Coroutine _routine;
        private Action _onClosed;

        public bool IsOpen =>
            canvasGroup != null && canvasGroup.blocksRaycasts && canvasGroup.alpha > 0.01f;

        private void Awake()
        {
            if (advanceButton != null)
            {
                advanceButton.onClick.AddListener(SkipToClose);
            }

            HideImmediate();
        }

        private void OnDestroy()
        {
            if (advanceButton != null)
            {
                advanceButton.onClick.RemoveListener(SkipToClose);
            }
        }

        public void Play(LoreViewDto nightfall, Action onClosed = null)
        {
            if (canvasGroup == null)
            {
                onClosed?.Invoke();
                return;
            }

            _onClosed = onClosed;
            if (titleText != null)
            {
                titleText.text = nightfall != null ? nightfall.ResolveTitle() : string.Empty;
            }

            if (bodyText != null)
            {
                bodyText.text = nightfall != null ? nightfall.ResolveBody() : string.Empty;
            }

            if (_routine != null)
            {
                StopCoroutine(_routine);
            }

            _routine = StartCoroutine(RunCurtain());
        }

        public void ClearAndClose()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            var done = _onClosed;
            _onClosed = null;
            HideImmediate();
            done?.Invoke();
        }

        private void SkipToClose()
        {
            if (!IsOpen)
            {
                return;
            }

            if (_routine != null)
            {
                StopCoroutine(_routine);
            }

            _routine = StartCoroutine(FadeOutAndFinish());
        }

        private IEnumerator RunCurtain()
        {
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;

            yield return FadeAlpha(0f, 1f, fadeInSeconds);
            yield return new WaitForSecondsRealtime(Mathf.Max(0.4f, holdSeconds));
            yield return FadeOutAndFinish();
        }

        private IEnumerator FadeOutAndFinish()
        {
            yield return FadeAlpha(canvasGroup.alpha, 0f, fadeOutSeconds);
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.alpha = 0f;
            _routine = null;
            var done = _onClosed;
            _onClosed = null;
            done?.Invoke();
        }

        private IEnumerator FadeAlpha(float from, float to, float duration)
        {
            var dur = Mathf.Max(0.05f, duration);
            var t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(from, to, t / dur);
                yield return null;
            }

            canvasGroup.alpha = to;
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
