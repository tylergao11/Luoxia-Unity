using System;
using System.Collections;
using System.Collections.Generic;
using Luoxia.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Immersion
{
    /// <summary>
    /// Generic short-chapter modal for free lore pages (e.g. dossier drill-in).
    /// Arrival uses ArrivalLoreOverlay; nightfall uses NightCurtainOverlay;
    /// presentation.frame narrative.show uses NarrativeFramePlayer.
    /// No content inventing: empty body = stay hidden.
    /// </summary>
    public sealed class LoreChapterOverlay : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image dimmer;
        [SerializeField] private Text titleText;
        [SerializeField] private Text bodyText;
        [SerializeField] private Button advanceButton;
        [SerializeField] private float fadeSeconds = 0.35f;

        private readonly Queue<LoreViewDto> _queue = new Queue<LoreViewDto>();
        private Coroutine _fadeRoutine;
        private Action _onClosed;

        public bool IsOpen => canvasGroup != null && canvasGroup.alpha > 0.01f && canvasGroup.blocksRaycasts;

        private void Awake()
        {
            if (advanceButton != null)
            {
                advanceButton.onClick.AddListener(Advance);
            }

            HideImmediate();
        }

        private void OnDestroy()
        {
            if (advanceButton != null)
            {
                advanceButton.onClick.RemoveListener(Advance);
            }
        }

        public void Enqueue(LoreViewDto entry, Action onClosed = null)
        {
            if (entry == null)
            {
                return;
            }

            var body = entry.ResolveBody();
            var title = entry.ResolveTitle();
            if (string.IsNullOrEmpty(body) && string.IsNullOrEmpty(title))
            {
                return;
            }

            _onClosed = onClosed ?? _onClosed;
            _queue.Enqueue(entry);
            if (!IsOpen)
            {
                ShowNext();
            }
        }

        public void EnqueueMany(IEnumerable<LoreViewDto> entries, Action onClosed = null)
        {
            if (entries == null)
            {
                return;
            }

            _onClosed = onClosed ?? _onClosed;
            foreach (var entry in entries)
            {
                Enqueue(entry);
            }
        }

        public void ClearAndClose()
        {
            _queue.Clear();
            _onClosed = null;
            HideImmediate();
        }

        private void Advance()
        {
            if (_queue.Count > 0)
            {
                ShowNext();
                return;
            }

            var done = _onClosed;
            _onClosed = null;
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
            }

            _fadeRoutine = StartCoroutine(FadeTo(0f, () =>
            {
                if (canvasGroup != null)
                {
                    canvasGroup.interactable = false;
                    canvasGroup.blocksRaycasts = false;
                }

                done?.Invoke();
            }));
        }

        private void ShowNext()
        {
            if (_queue.Count == 0)
            {
                Advance();
                return;
            }

            var entry = _queue.Dequeue();
            if (titleText != null)
            {
                titleText.text = entry.ResolveTitle();
            }

            if (bodyText != null)
            {
                bodyText.text = entry.ResolveBody();
            }

            if (canvasGroup != null)
            {
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
            }

            _fadeRoutine = StartCoroutine(FadeTo(1f));
        }

        private void HideImmediate()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
        }

        private IEnumerator FadeTo(float target, Action after = null)
        {
            if (canvasGroup == null)
            {
                after?.Invoke();
                yield break;
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
            after?.Invoke();
        }
    }
}
