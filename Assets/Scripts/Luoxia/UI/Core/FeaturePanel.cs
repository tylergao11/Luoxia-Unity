using UnityEngine;

namespace Luoxia.UI.Core
{
    /// <summary>
    /// Bottom feature tab panel base (Dialogue / Event / ...).
    /// Visual page slide is owned by MainWorldScreen FeaturePagesContent;
    /// this panel only toggles input (immediate cut on deactivate).
    /// </summary>
    public abstract class FeaturePanel : LuoxiaView, IFeaturePanel
    {
        [SerializeField] private string featureId;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private GameObject activeRoot;

        private bool _isActiveFeature;

        public string FeatureId => featureId;
        public bool IsActiveFeature => _isActiveFeature;

        protected override void Awake()
        {
            base.Awake();
            if (string.IsNullOrEmpty(featureId))
            {
                featureId = ResolveDefaultFeatureId();
            }
        }

        /// <summary>Override for stable feature ids used by the screen shell.</summary>
        protected virtual string ResolveDefaultFeatureId() => GetType().Name;

        public virtual void SetActiveFeature(bool active)
        {
            _isActiveFeature = active;

            if (activeRoot != null && activeRoot != gameObject)
            {
                activeRoot.SetActive(true);
            }

            if (canvasGroup != null)
            {
                // Pages stay opaque; pager clips. Deactivate immediately cuts input.
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = active;
                canvasGroup.blocksRaycasts = active;
            }
            else if (activeRoot == null)
            {
                if (active)
                {
                    Show();
                }
                else
                {
                    Hide();
                }
            }
            else
            {
                activeRoot.SetActive(active);
            }

            OnActiveFeatureChanged(active);
        }

        protected virtual void OnActiveFeatureChanged(bool active)
        {
        }
    }
}
