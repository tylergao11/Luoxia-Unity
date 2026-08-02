using System.Collections;
using System.Collections.Generic;
using System.Text;
using Luoxia.Contracts;
using Luoxia.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Immersion
{
    /// <summary>
    /// Character dossier: profile + hearsay for one subject_entity_id.
    /// No lore entries ⇒ panel refuses to open (无条目=无入口).
    /// </summary>
    public sealed class CharacterDossierPanel : LuoxiaView
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Text nameText;
        [SerializeField] private Text profileText;
        [SerializeField] private Text hearsayText;
        [SerializeField] private Button closeButton;
        [SerializeField] private GameObject root;
        [SerializeField] private float fadeSeconds = 0.2f;

        private string _subjectEntityId;
        private Coroutine _fadeRoutine;

        public bool IsOpen =>
            root != null
                ? root.activeSelf
                : canvasGroup != null && canvasGroup.blocksRaycasts;

        protected override void OnBound()
        {
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(Close);
            }

            CloseImmediate();
        }

        protected override void OnUnbound()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
            }
        }

        public override void OnSessionView(SessionViewDto view)
        {
            if (!IsOpen || string.IsNullOrEmpty(_subjectEntityId))
            {
                return;
            }

            if (!LoreQuery.HasDossier(view, _subjectEntityId))
            {
                Close();
                return;
            }

            PaintSubject(view, _subjectEntityId);
        }

        public bool TryOpen(SessionViewDto view, string subjectEntityId)
        {
            if (view == null || string.IsNullOrEmpty(subjectEntityId))
            {
                return false;
            }

            if (!LoreQuery.HasDossier(view, subjectEntityId))
            {
                return false;
            }

            _subjectEntityId = subjectEntityId;
            PaintSubject(view, subjectEntityId);
            SetVisible(true);
            return true;
        }

        public void Close()
        {
            _subjectEntityId = null;
            SetVisible(false);
        }

        private void CloseImmediate()
        {
            _subjectEntityId = null;
            if (root != null)
            {
                root.SetActive(false);
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
        }

        private void PaintSubject(SessionViewDto view, string subjectEntityId)
        {
            if (nameText != null)
            {
                var name = LoreQuery.ResolveSubjectDisplayName(view, subjectEntityId);
                nameText.text = name;
                nameText.gameObject.SetActive(!string.IsNullOrEmpty(name));
            }

            if (profileText != null)
            {
                profileText.text = JoinBodies(LoreQuery.ForSubject(view, subjectEntityId, LoreKind.Profile));
            }

            if (hearsayText != null)
            {
                hearsayText.text = JoinBodies(LoreQuery.ForSubject(view, subjectEntityId, LoreKind.Hearsay));
            }
        }

        private static string JoinBodies(IEnumerable<LoreViewDto> entries)
        {
            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                var title = entry.ResolveTitle();
                var body = entry.ResolveBody();
                if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(body))
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append("\n\n");
                }

                if (!string.IsNullOrEmpty(title))
                {
                    sb.Append(title);
                    if (!string.IsNullOrEmpty(body))
                    {
                        sb.Append('\n');
                    }
                }

                if (!string.IsNullOrEmpty(body))
                {
                    sb.Append(body);
                }
            }

            return sb.ToString();
        }

        private void SetVisible(bool visible)
        {
            if (root != null)
            {
                root.SetActive(true);
            }

            if (canvasGroup == null)
            {
                if (root != null)
                {
                    root.SetActive(visible);
                }

                return;
            }

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
            }

            _fadeRoutine = StartCoroutine(FadeTo(visible ? 1f : 0f, visible));
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
            if (!interactable && root != null)
            {
                root.SetActive(false);
            }

            _fadeRoutine = null;
        }
    }
}
