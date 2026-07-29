using UnityEngine;

namespace Luoxia.UI.Core
{
    /// <summary>
    /// Prefab list row base. Bind/Unbind only; pooling owned by ListViewController.
    /// </summary>
    public abstract class ListItemView<TModel> : MonoBehaviour, IListItemView<TModel>
    {
        private TModel _model;
        private int _index = -1;
        private bool _hasModel;

        public TModel Model => _model;
        public int Index => _index;
        public bool HasModel => _hasModel;

        public void Bind(TModel model, int index)
        {
            _model = model;
            _index = index;
            _hasModel = true;
            gameObject.SetActive(true);
            OnBind(model, index);
        }

        public void Unbind()
        {
            if (!_hasModel)
            {
                return;
            }

            OnUnbind();
            _model = default;
            _index = -1;
            _hasModel = false;
            gameObject.SetActive(false);
        }

        protected abstract void OnBind(TModel model, int index);

        protected virtual void OnUnbind()
        {
        }
    }
}
