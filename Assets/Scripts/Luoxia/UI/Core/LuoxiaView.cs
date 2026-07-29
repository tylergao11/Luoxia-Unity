using Luoxia.Contracts;
using Luoxia.Session;
using UnityEngine;

namespace Luoxia.UI.Core
{
    /// <summary>
    /// Base MonoBehaviour view. Handles show/hide and optional SessionView binding lifecycle.
    /// Subclasses implement OnSessionView; never write world state.
    /// </summary>
    public abstract class LuoxiaView : MonoBehaviour, ISessionViewBinder
    {
        [SerializeField] private bool startVisible = true;

        private ISessionViewSource _source;
        private bool _bound;

        public bool IsVisible => gameObject.activeSelf;

        protected ISessionViewSource SessionSource => _source;
        protected SessionViewDto LatestView => _source != null ? _source.CurrentView : null;

        protected virtual void Awake()
        {
            if (!startVisible)
            {
                Hide();
            }
        }

        protected virtual void OnDestroy()
        {
            UnbindSession();
        }

        public virtual void Show()
        {
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            OnShown();
        }

        public virtual void Hide()
        {
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }

            OnHidden();
        }

        public void BindSession(ISessionViewSource source)
        {
            if (source == null)
            {
                throw new System.ArgumentNullException(nameof(source));
            }

            if (_bound)
            {
                UnbindSession();
            }

            _source = source;
            _source.ViewChanged += HandleViewChanged;
            _bound = true;
            OnBound();

            if (_source.HasView)
            {
                OnSessionView(_source.CurrentView);
            }
        }

        public void UnbindSession()
        {
            if (!_bound || _source == null)
            {
                return;
            }

            _source.ViewChanged -= HandleViewChanged;
            _source = null;
            _bound = false;
            OnUnbound();
        }

        public abstract void OnSessionView(SessionViewDto view);

        private void HandleViewChanged(SessionViewDto view)
        {
            if (view == null)
            {
                return;
            }

            OnSessionView(view);
        }

        protected virtual void OnBound()
        {
        }

        protected virtual void OnUnbound()
        {
        }

        protected virtual void OnShown()
        {
        }

        protected virtual void OnHidden()
        {
        }
    }
}
