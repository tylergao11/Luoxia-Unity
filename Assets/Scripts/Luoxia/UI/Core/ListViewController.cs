using System;
using System.Collections.Generic;
using UnityEngine;

namespace Luoxia.UI.Core
{
    /// <summary>
    /// Simple transform-child pool for list prefabs. Not ScrollRect virtualization (add later if needed).
    /// </summary>
    public sealed class ListViewController<TModel, TItem>
        where TItem : ListItemView<TModel>
    {
        private readonly TItem _prefab;
        private readonly Transform _container;
        private readonly List<TItem> _active = new List<TItem>();
        private readonly Stack<TItem> _pool = new Stack<TItem>();

        public IReadOnlyList<TItem> ActiveItems => _active;

        public ListViewController(TItem prefab, Transform container)
        {
            _prefab = prefab != null ? prefab : throw new ArgumentNullException(nameof(prefab));
            _container = container != null ? container : throw new ArgumentNullException(nameof(container));
            _prefab.gameObject.SetActive(false);
        }

        public void SetItems(IReadOnlyList<TModel> models)
        {
            models ??= Array.Empty<TModel>();

            while (_active.Count > models.Count)
            {
                var last = _active[_active.Count - 1];
                _active.RemoveAt(_active.Count - 1);
                last.Unbind();
                _pool.Push(last);
            }

            for (var i = 0; i < models.Count; i++)
            {
                TItem item;
                if (i < _active.Count)
                {
                    item = _active[i];
                }
                else
                {
                    item = Acquire();
                    _active.Add(item);
                }

                item.Bind(models[i], i);
            }
        }

        public void Clear()
        {
            for (var i = 0; i < _active.Count; i++)
            {
                _active[i].Unbind();
                _pool.Push(_active[i]);
            }

            _active.Clear();
        }

        private TItem Acquire()
        {
            if (_pool.Count > 0)
            {
                return _pool.Pop();
            }

            return UnityEngine.Object.Instantiate(_prefab, _container);
        }
    }
}
