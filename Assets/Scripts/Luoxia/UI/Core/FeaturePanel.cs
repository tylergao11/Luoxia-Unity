using UnityEngine;

namespace Luoxia.UI.Core
{
    /// <summary>
    /// Bottom feature tab panel base (Dialogue / Event / ...).
    /// Active feature is visual + input enabled; inactive still receives SessionView updates.
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

            if (activeRoot != null)
            {
                activeRoot.SetActive(active);
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = active ? 1f : 0f;
                canvasGroup.interactable = active;
                canvasGroup.blocksRaycasts = active;
            }
            else if (activeRoot == null)
            {
                // fallback: whole object
                if (active)
                {
                    Show();
                }
                else
                {
                    Hide();
                }
            }

            OnActiveFeatureChanged(active);
        }

        protected virtual void OnActiveFeatureChanged(bool active)
        {
        }
    }
}
