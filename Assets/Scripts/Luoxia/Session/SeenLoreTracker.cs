using System;
using System.Collections.Generic;
using Luoxia.Contracts;

namespace Luoxia.Session
{
    /// <summary>
    /// Local Host dedupe of SessionView lore by session_id + lore_id.
    /// Does not invent content; only tracks which projected lore rows were already shown.
    /// </summary>
    public sealed class SeenLoreTracker
    {
        private string _sessionId;
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);

        public void ResetForSession(string sessionId)
        {
            if (string.Equals(_sessionId, sessionId, StringComparison.Ordinal))
            {
                return;
            }

            _sessionId = sessionId;
            _seen.Clear();
        }

        /// <summary>
        /// Mark every current lore_id as seen (boot / first view) so only later diffs surface.
        /// </summary>
        public void SeedFromView(SessionViewDto view)
        {
            if (view == null)
            {
                return;
            }

            ResetForSession(view.session_id);
            if (view.lore == null)
            {
                return;
            }

            for (var i = 0; i < view.lore.Count; i++)
            {
                var entry = view.lore[i];
                if (entry != null && !string.IsNullOrEmpty(entry.lore_id))
                {
                    _seen.Add(entry.lore_id);
                }
            }
        }

        public bool TryMarkNew(string loreId)
        {
            if (string.IsNullOrEmpty(loreId))
            {
                return false;
            }

            return _seen.Add(loreId);
        }

        public bool HasSeen(string loreId)
        {
            return !string.IsNullOrEmpty(loreId) && _seen.Contains(loreId);
        }
    }
}
