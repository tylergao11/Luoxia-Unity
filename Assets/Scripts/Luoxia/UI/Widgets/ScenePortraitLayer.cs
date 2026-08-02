using System;
using Luoxia.Assets;
using Luoxia.Contracts;
using Luoxia.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Widgets
{
    /// <summary>
    /// Mid-screen narrative layer driven by RenderNode scene/portrait.
    /// Scene cover uses AspectRatioFitter EnvelopeParent inside RectMask2D — never preserveAspect=false squash.
    /// Portrait uses exact dialogue_portrait slot; no cross-slot fallback.
    /// </summary>
    public sealed class ScenePortraitLayer : HudWidget
    {
        [SerializeField] private Image sceneImage;
        [SerializeField] private AspectRatioFitter sceneAspectFitter;
        [SerializeField] private Image portraitImage;
        [SerializeField] private Button portraitButton;
        [SerializeField] private Sprite fallbackScene;
        [SerializeField] private Sprite fallbackPortrait;
        [SerializeField] private CanvasGroup layerGroup;
        [SerializeField] private Text assetErrorText;

        private string _focusSubjectEntityId;
        private Action<string> _onInspectSubject;
        private IContentHashSpriteResolver _resolver;
        private string _lastError;

        public CanvasGroup LayerGroup => layerGroup;

        public void SetSpriteResolver(IContentHashSpriteResolver resolver)
        {
            _resolver = resolver;
            if (LatestView != null)
            {
                Paint(LatestView);
            }
        }

        public void SetSubjectInspectHandler(Action<string> handler)
        {
            _onInspectSubject = handler;
        }

        /// <summary>
        /// Generic speaker / focus subject. Portrait node matched by subject + dialogue_portrait slot.
        /// </summary>
        public void SetFocusSubject(string subjectEntityId)
        {
            _focusSubjectEntityId = subjectEntityId;
            if (LatestView != null)
            {
                Paint(LatestView);
            }
        }

        protected override void OnBound()
        {
            if (portraitButton != null)
            {
                portraitButton.onClick.AddListener(HandlePortraitClick);
            }
        }

        protected override void OnUnbound()
        {
            if (portraitButton != null)
            {
                portraitButton.onClick.RemoveListener(HandlePortraitClick);
            }
        }

        protected override void Paint(SessionViewDto view)
        {
            _lastError = null;
            var sceneNode = LoreQuery.FindSceneNode(view);
            var portraitNode = !string.IsNullOrEmpty(_focusSubjectEntityId)
                ? LoreQuery.FindPortraitNode(view, _focusSubjectEntityId, LayoutSlots.DialoguePortrait)
                : null;

            if (sceneImage != null)
            {
                ApplyScene(sceneImage, sceneNode?.asset);
            }

            if (portraitImage != null)
            {
                var subject = portraitNode != null
                    ? (portraitNode.subject_entity_id ?? _focusSubjectEntityId)
                    : null;
                var canShow = portraitNode != null &&
                              !string.IsNullOrEmpty(subject) &&
                              LoreQuery.HasDossier(view, subject);

                if (canShow)
                {
                    ApplyAssetOrChrome(portraitImage, portraitNode.asset, fallbackPortrait, "portrait");
                    portraitImage.enabled = true;
                    portraitImage.raycastTarget = true;
                    if (portraitButton != null)
                    {
                        portraitButton.interactable = true;
                    }
                }
                else
                {
                    portraitImage.enabled = false;
                    portraitImage.raycastTarget = false;
                    if (portraitButton != null)
                    {
                        portraitButton.interactable = false;
                    }
                }
            }

            if (assetErrorText != null)
            {
                assetErrorText.gameObject.SetActive(!string.IsNullOrEmpty(_lastError));
                assetErrorText.text = _lastError ?? string.Empty;
            }
        }

        private void ApplyScene(Image image, AssetContentRefDto asset)
        {
            if (image == null)
            {
                return;
            }

            image.preserveAspect = true;

            if (asset != null && !string.IsNullOrEmpty(asset.content_hash))
            {
                var resolver = _resolver ?? ContentHashSpriteResolverLocator.Shared;
                if (resolver.TryResolve(asset.content_hash, out var sprite, out var error))
                {
                    image.sprite = sprite;
                    image.color = Color.white;
                    image.enabled = true;
                    ApplySceneAspect(sprite);
                    return;
                }

                _lastError = $"[scene] {error}";
                image.sprite = null;
                image.color = new Color(0.85f, 0.15f, 0.2f, 0.55f);
                image.enabled = true;
                ApplySceneAspect(null);
                return;
            }

            // No background.png fallback — empty cover until SessionView supplies a scene asset.
            if (fallbackScene != null)
            {
                image.sprite = fallbackScene;
                image.color = Color.white;
                image.enabled = true;
                ApplySceneAspect(fallbackScene);
                return;
            }

            image.sprite = null;
            image.enabled = false;
            ApplySceneAspect(null);
        }

        private void ApplySceneAspect(Sprite sprite)
        {
            if (sceneAspectFitter == null)
            {
                return;
            }

            if (sprite != null && sprite.rect.height > 0.01f)
            {
                sceneAspectFitter.aspectRatio = sprite.rect.width / sprite.rect.height;
            }
            else
            {
                // Design portrait frame until a real cover arrives.
                sceneAspectFitter.aspectRatio = 1080f / 1920f;
            }
        }

        private void ApplyAssetOrChrome(Image image, AssetContentRefDto asset, Sprite chromeFallback, string slot)
        {
            if (image == null)
            {
                return;
            }

            image.preserveAspect = true;

            if (asset != null && !string.IsNullOrEmpty(asset.content_hash))
            {
                var resolver = _resolver ?? ContentHashSpriteResolverLocator.Shared;
                if (resolver.TryResolve(asset.content_hash, out var sprite, out var error))
                {
                    image.sprite = sprite;
                    image.color = Color.white;
                    image.enabled = true;
                    return;
                }

                _lastError = $"[{slot}] {error}";
                image.sprite = null;
                image.color = new Color(0.85f, 0.15f, 0.2f, 0.55f);
                image.enabled = true;
                return;
            }

            if (image.sprite == null && chromeFallback != null)
            {
                image.sprite = chromeFallback;
                image.color = Color.white;
            }

            image.enabled = image.sprite != null;
        }

        private void HandlePortraitClick()
        {
            var view = LatestView;
            if (view == null || string.IsNullOrEmpty(_focusSubjectEntityId))
            {
                return;
            }

            var subject = _focusSubjectEntityId;
            if (!LoreQuery.HasDossier(view, subject))
            {
                return;
            }

            // Require exact dialogue_portrait binding for this subject.
            if (LoreQuery.FindPortraitNode(view, subject, LayoutSlots.DialoguePortrait) == null)
            {
                return;
            }

            _onInspectSubject?.Invoke(subject);
        }
    }
}
