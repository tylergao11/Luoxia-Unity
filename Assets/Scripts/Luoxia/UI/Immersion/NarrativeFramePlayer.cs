using System.Collections;
using System.Collections.Generic;
using Luoxia.Contracts;
using Luoxia.Session;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Immersion
{
    /// <summary>
    /// Queues presentation.frame → narrative.show segments as page-turning narrative.
    /// Modal chrome: panel_event_modal + art fade + title + divider; close only when no pages remain.
    /// </summary>
    public sealed class NarrativeFramePlayer : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Text kindText;
        [SerializeField] private Text bodyText;
        [SerializeField] private Button advanceButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Image advanceButtonImage;
        [SerializeField] private Sprite choiceNormalSprite;
        [SerializeField] private Sprite choiceActiveSprite;
        [SerializeField] private float fadeSeconds = 0.2f;

        private readonly Queue<NarrativePage> _pages = new Queue<NarrativePage>();
        private IPresentationBus _bus;
        private readonly HashSet<string> _seenFrameIds = new HashSet<string>();
        private Coroutine _fadeRoutine;
        private bool _pageVisible;

        private struct NarrativePage
        {
            public string KindLabel;
            public string Body;
        }

        public bool IsOpen => canvasGroup != null && canvasGroup.blocksRaycasts;

        private void Awake()
        {
            if (advanceButton != null)
            {
                advanceButton.onClick.AddListener(Advance);
                ApplyChoiceSpriteSwap();
            }

            if (closeButton != null)
            {
                closeButton.onClick.AddListener(HandleClose);
            }

            HideImmediate();
            RefreshCloseInteractable();
        }

        private void OnDestroy()
        {
            Unbind();
            if (advanceButton != null)
            {
                advanceButton.onClick.RemoveListener(Advance);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(HandleClose);
            }
        }

        public void Bind(IPresentationBus bus)
        {
            Unbind();
            _bus = bus;
            if (_bus != null)
            {
                _bus.PresentationFrameReceived += HandleFrame;
            }
        }

        public void Unbind()
        {
            if (_bus != null)
            {
                _bus.PresentationFrameReceived -= HandleFrame;
                _bus = null;
            }
        }

        public void ClearSeen()
        {
            _seenFrameIds.Clear();
        }

        private void HandleFrame(PresentationFrameDto frame)
        {
            if (frame == null || frame.operations == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(frame.frame_id) && !_seenFrameIds.Add(frame.frame_id))
            {
                return;
            }

            for (var i = 0; i < frame.operations.Count; i++)
            {
                var op = frame.operations[i];
                if (op == null)
                {
                    continue;
                }

                if (!op.IsNarrativeShow || op.presentation?.segments == null)
                {
                    Debug.Log($"[NarrativeFrame] skip op={op.op}");
                    continue;
                }

                for (var s = 0; s < op.presentation.segments.Count; s++)
                {
                    var segment = op.presentation.segments[s];
                    if (segment == null)
                    {
                        continue;
                    }

                    // Chat stream owns dialogue quotes; narrative modal must not echo them.
                    if (segment.IsDialogueQuote)
                    {
                        continue;
                    }

                    var body = segment.ResolveDisplayText();
                    if (string.IsNullOrEmpty(body))
                    {
                        continue;
                    }

                    _pages.Enqueue(new NarrativePage
                    {
                        KindLabel = ResolveKindChromeLabel(segment.segment_kind),
                        Body = body
                    });
                }
            }

            if (!IsOpen && _pages.Count > 0)
            {
                ShowNext();
            }
        }

        /// <summary>
        /// Closed presentation chrome for schema segment_kind. Unknown kinds hide type chrome
        /// (never show raw English op/kind strings to players).
        /// </summary>
        private static string ResolveKindChromeLabel(string segmentKind)
        {
            if (string.IsNullOrEmpty(segmentKind))
            {
                return string.Empty;
            }

            switch (segmentKind)
            {
                case "narration":
                    return "旁白";
                case "system":
                    return "系统";
                case "notice":
                    return "提示";
                default:
                    return string.Empty;
            }
        }

        private void Advance()
        {
            if (_pages.Count > 0)
            {
                ShowNext();
                return;
            }

            FadeOut();
        }

        private void HandleClose()
        {
            if (_pages.Count > 0)
            {
                return;
            }

            FadeOut();
        }

        private void ShowNext()
        {
            if (_pages.Count == 0)
            {
                FadeOut();
                return;
            }

            var page = _pages.Dequeue();
            _pageVisible = true;
            if (kindText != null)
            {
                kindText.text = page.KindLabel;
                kindText.gameObject.SetActive(!string.IsNullOrEmpty(page.KindLabel));
            }

            if (bodyText != null)
            {
                bodyText.text = page.Body;
            }

            RefreshCloseInteractable();
            FadeIn();
        }

        private void RefreshCloseInteractable()
        {
            if (closeButton == null)
            {
                return;
            }

            // Close is only clickable when no pages remain after the current one is shown.
            closeButton.interactable = _pageVisible && _pages.Count == 0;
        }

        private void ApplyChoiceSpriteSwap()
        {
            if (advanceButton == null)
            {
                return;
            }

            // Primary continue uses gold active slice + ColorTint.
            if (advanceButtonImage != null && choiceActiveSprite != null)
            {
                advanceButtonImage.sprite = choiceActiveSprite;
            }
            else if (advanceButtonImage != null && choiceNormalSprite != null)
            {
                advanceButtonImage.sprite = choiceNormalSprite;
            }

            advanceButton.transition = Selectable.Transition.ColorTint;
        }

        private void FadeIn()
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

        private void FadeOut()
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

        private IEnumerator FadeTo(float target, bool interactable)
        {
            var duration = Mathf.Clamp(fadeSeconds, 0.15f, 0.25f);
            var start = canvasGroup.alpha;
            if (!interactable)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            var t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(start, target, t / duration);
                yield return null;
            }

            canvasGroup.alpha = target;
            canvasGroup.interactable = interactable;
            canvasGroup.blocksRaycasts = interactable;
            if (!interactable)
            {
                _pageVisible = false;
            }

            _fadeRoutine = null;
            RefreshCloseInteractable();
        }

        private void HideImmediate()
        {
            _pageVisible = false;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            RefreshCloseInteractable();
        }
    }
}
