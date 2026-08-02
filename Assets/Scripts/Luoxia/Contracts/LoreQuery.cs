using System;
using System.Collections.Generic;

namespace Luoxia.Contracts
{
    /// <summary>
    /// Pure helpers over SessionView.lore / render_nodes. No content inventing.
    /// </summary>
    public static class LoreQuery
    {
        public static IEnumerable<LoreViewDto> ForSubject(SessionViewDto view, string subjectEntityId, LoreKind kind)
        {
            if (view?.lore == null || string.IsNullOrEmpty(subjectEntityId))
            {
                yield break;
            }

            for (var i = 0; i < view.lore.Count; i++)
            {
                var entry = view.lore[i];
                if (entry == null || entry.KindEnum != kind)
                {
                    continue;
                }

                if (entry.subject_entity_id == subjectEntityId)
                {
                    yield return entry;
                }
            }
        }

        public static bool HasDossier(SessionViewDto view, string subjectEntityId)
        {
            if (view?.lore == null || string.IsNullOrEmpty(subjectEntityId))
            {
                return false;
            }

            for (var i = 0; i < view.lore.Count; i++)
            {
                var entry = view.lore[i];
                if (entry == null || entry.subject_entity_id != subjectEntityId)
                {
                    continue;
                }

                if (entry.KindEnum == LoreKind.Profile || entry.KindEnum == LoreKind.Hearsay)
                {
                    return true;
                }
            }

            return false;
        }

        public static LoreViewDto FindArrival(SessionViewDto view, string locationEntityId)
        {
            if (view?.lore == null || string.IsNullOrEmpty(locationEntityId))
            {
                return null;
            }

            for (var i = 0; i < view.lore.Count; i++)
            {
                var entry = view.lore[i];
                if (entry != null &&
                    entry.KindEnum == LoreKind.Arrival &&
                    entry.subject_entity_id == locationEntityId)
                {
                    return entry;
                }
            }

            return null;
        }

        public static LoreViewDto FindNightfall(SessionViewDto view, int day)
        {
            if (view?.lore == null)
            {
                return null;
            }

            LoreViewDto fallback = null;
            for (var i = 0; i < view.lore.Count; i++)
            {
                var entry = view.lore[i];
                if (entry == null || entry.KindEnum != LoreKind.Nightfall)
                {
                    continue;
                }

                // Prefer body/title presence; no Engine day field on LoreView — Host uses lore_id diff.
                if (!string.IsNullOrEmpty(entry.ResolveBody()) || !string.IsNullOrEmpty(entry.ResolveTitle()))
                {
                    fallback = entry;
                }
            }

            return fallback;
        }

        /// <summary>
        /// New arrival lore for a location that has not been marked seen (lore_id diff).
        /// </summary>
        public static LoreViewDto FindUnseenArrival(
            SessionViewDto view,
            string locationEntityId,
            System.Func<string, bool> isUnseen)
        {
            if (view?.lore == null || string.IsNullOrEmpty(locationEntityId) || isUnseen == null)
            {
                return null;
            }

            for (var i = 0; i < view.lore.Count; i++)
            {
                var entry = view.lore[i];
                if (entry == null ||
                    entry.KindEnum != LoreKind.Arrival ||
                    entry.subject_entity_id != locationEntityId)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(entry.lore_id) || !isUnseen(entry.lore_id))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(entry.ResolveBody()) || !string.IsNullOrEmpty(entry.ResolveTitle()))
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>
        /// First unseen nightfall lore row (lore_id diff after day increment).
        /// </summary>
        public static LoreViewDto FindUnseenNightfall(
            SessionViewDto view,
            System.Func<string, bool> isUnseen)
        {
            if (view?.lore == null || isUnseen == null)
            {
                return null;
            }

            LoreViewDto best = null;
            var bestOrdinal = int.MaxValue;
            for (var i = 0; i < view.lore.Count; i++)
            {
                var entry = view.lore[i];
                if (entry == null || entry.KindEnum != LoreKind.Nightfall)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(entry.lore_id) || !isUnseen(entry.lore_id))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(entry.ResolveBody()) && string.IsNullOrEmpty(entry.ResolveTitle()))
                {
                    continue;
                }

                if (entry.ordinal < bestOrdinal)
                {
                    bestOrdinal = entry.ordinal;
                    best = entry;
                }
            }

            return best;
        }

        public static string ResolveSubjectDisplayName(SessionViewDto view, string subjectEntityId)
        {
            if (view?.lore == null || string.IsNullOrEmpty(subjectEntityId))
            {
                return string.Empty;
            }

            for (var i = 0; i < view.lore.Count; i++)
            {
                var entry = view.lore[i];
                if (entry == null || entry.subject_entity_id != subjectEntityId)
                {
                    continue;
                }

                if (entry.KindEnum == LoreKind.Profile)
                {
                    var title = entry.ResolveTitle();
                    if (!string.IsNullOrEmpty(title))
                    {
                        return title;
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Portrait node for subject + exact slot_id. No cross-slot fallback.
        /// </summary>
        public static RenderNodeDto FindPortraitNode(SessionViewDto view, string subjectEntityId, string slotId)
        {
            if (view?.render_nodes == null ||
                string.IsNullOrEmpty(subjectEntityId) ||
                string.IsNullOrEmpty(slotId))
            {
                return null;
            }

            for (var i = 0; i < view.render_nodes.Count; i++)
            {
                var node = view.render_nodes[i];
                if (node != null &&
                    node.KindEnum == RenderNodeKind.Portrait &&
                    node.subject_entity_id == subjectEntityId &&
                    node.slot_id == slotId)
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>
        /// Visible dialogue targets at the current location: portrait / interaction_anchor
        /// subjects (excluding the player). Drawn only from SessionView facts.
        /// </summary>
        public static List<string> CollectCoLocatedEntityIds(SessionViewDto view)
        {
            var result = new List<string>();
            if (view?.render_nodes == null)
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < view.render_nodes.Count; i++)
            {
                var node = view.render_nodes[i];
                if (node == null)
                {
                    continue;
                }

                if (node.KindEnum != RenderNodeKind.Portrait &&
                    node.KindEnum != RenderNodeKind.InteractionAnchor)
                {
                    continue;
                }

                var entityId = node.subject_entity_id;
                if (string.IsNullOrEmpty(entityId) ||
                    entityId == view.player_entity_id ||
                    !seen.Add(entityId))
                {
                    continue;
                }

                result.Add(entityId);
            }

            return result;
        }

        public static RenderNodeDto FindSceneNode(SessionViewDto view)
        {
            if (view?.render_nodes == null)
            {
                return null;
            }

            for (var i = 0; i < view.render_nodes.Count; i++)
            {
                var node = view.render_nodes[i];
                if (node != null && node.KindEnum == RenderNodeKind.Scene)
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>
        /// Location label priority: ① subject-matched render text ② arrival title ③ profile title.
        /// Never prefer UUID / entity_id as display.
        /// </summary>
        public static string ResolveLocationLabel(SessionViewDto view)
        {
            if (view == null || string.IsNullOrEmpty(view.player_location_entity_id))
            {
                return string.Empty;
            }

            return ResolveLocationLabelForEntity(view, view.player_location_entity_id);
        }

        public static string ResolveLocationLabelForEntity(SessionViewDto view, string locationEntityId)
        {
            if (view == null || string.IsNullOrEmpty(locationEntityId))
            {
                return string.Empty;
            }

            // ① subject-matched render node text (location_scene / map_anchor / text, any kind)
            if (view.render_nodes != null)
            {
                string fallbackText = null;
                for (var i = 0; i < view.render_nodes.Count; i++)
                {
                    var node = view.render_nodes[i];
                    if (node == null ||
                        node.subject_entity_id != locationEntityId ||
                        node.text == null)
                    {
                        continue;
                    }

                    var text = node.text.Resolve();
                    if (string.IsNullOrEmpty(text))
                    {
                        continue;
                    }

                    // Prefer map / location slots when present.
                    if (node.slot_id == "location_scene" || node.slot_id == "map_anchor")
                    {
                        return text;
                    }

                    fallbackText ??= text;
                }

                if (!string.IsNullOrEmpty(fallbackText))
                {
                    return fallbackText;
                }
            }

            // ② arrival title
            var arrival = FindArrival(view, locationEntityId);
            if (arrival != null)
            {
                var title = arrival.ResolveTitle();
                if (!string.IsNullOrEmpty(title))
                {
                    return title;
                }
            }

            // ③ profile title
            return ResolveSubjectDisplayName(view, locationEntityId);
        }

        /// <summary>
        /// Visible map destinations derived only from SessionView facts
        /// (arrival lore subjects + current player location). No invented places.
        /// Display labels never fall back to UUID.
        /// </summary>
        public static List<VisibleLocationEntry> CollectVisibleLocations(SessionViewDto view)
        {
            var result = new List<VisibleLocationEntry>();
            if (view == null)
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);

            void TryAdd(string entityId)
            {
                if (string.IsNullOrEmpty(entityId) || !seen.Add(entityId))
                {
                    return;
                }

                result.Add(new VisibleLocationEntry
                {
                    EntityId = entityId,
                    DisplayLabel = ResolveLocationLabelForEntity(view, entityId),
                    IsCurrent = entityId == view.player_location_entity_id
                });
            }

            if (view.lore != null)
            {
                for (var i = 0; i < view.lore.Count; i++)
                {
                    var entry = view.lore[i];
                    if (entry == null || entry.KindEnum != LoreKind.Arrival)
                    {
                        continue;
                    }

                    TryAdd(entry.subject_entity_id);
                }
            }

            // Map destinations projected as map_anchor / location_scene render nodes.
            if (view.render_nodes != null)
            {
                for (var i = 0; i < view.render_nodes.Count; i++)
                {
                    var node = view.render_nodes[i];
                    if (node == null || string.IsNullOrEmpty(node.subject_entity_id))
                    {
                        continue;
                    }

                    if (node.slot_id == "map_anchor" || node.slot_id == "location_scene")
                    {
                        TryAdd(node.subject_entity_id);
                    }
                }
            }

            if (!string.IsNullOrEmpty(view.player_location_entity_id))
            {
                TryAdd(view.player_location_entity_id);
            }

            return result;
        }
    }

    public sealed class VisibleLocationEntry
    {
        public string EntityId;
        public string DisplayLabel;
        public bool IsCurrent;
    }
}
