using System.Collections.Generic;
using Luoxia.Assets;
using Luoxia.Contracts;
using Luoxia.UI.Core;
using UnityEngine;


namespace Luoxia.UI.Widgets
{
    /// <summary>
    /// Top avatar strip for selectable dialogue targets.
    /// Sources: co-located portrait/interaction_anchor subjects + active dialogue participants.
    /// Portraits resolve via content_hash; miss leaves empty sprite (no fake art).
    /// </summary>
    public sealed class AvatarRailWidget : HudWidget
    {
        [SerializeField] private AvatarRailItemView itemPrefab;
        [SerializeField] private Transform contentRoot;

        private ListViewController<AvatarRailItemModel, AvatarRailItemView> _list;
        private IDialogueSelection _selection;
        private IPlayerIntentSink _intents;
        private System.Action<string> _onInspectSubject;

        public void Configure(
            IDialogueSelection selection,
            IPlayerIntentSink intents,
            System.Action<string> onInspectSubject = null)
        {
            _selection = selection;
            _intents = intents;
            _onInspectSubject = onInspectSubject;

            if (_selection != null)
            {
                _selection.Changed -= HandleSelectionChanged;
                _selection.Changed += HandleSelectionChanged;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            if (itemPrefab != null && contentRoot != null)
            {
                _list = new ListViewController<AvatarRailItemModel, AvatarRailItemView>(itemPrefab, contentRoot);
            }
        }

        protected override void OnUnbound()
        {
            if (_selection != null)
            {
                _selection.Changed -= HandleSelectionChanged;
            }

            _list?.Clear();
        }

        protected override void Paint(SessionViewDto view)
        {
            if (_list == null)
            {
                return;
            }

            var models = BuildModels(view);
            _list.SetItems(models);

            for (var i = 0; i < _list.ActiveItems.Count; i++)
            {
                _list.ActiveItems[i].SetSelectHandler(HandleSelect);
                _list.ActiveItems[i].SetInspectHandler(HandleInspect);
            }
        }

        private List<AvatarRailItemModel> BuildModels(SessionViewDto view)
        {
            var result = new List<AvatarRailItemModel>();
            var selected = _selection != null ? _selection.Current : null;
            var seen = new HashSet<string>();
            var hasSystem = false;

            void AddEntity(string entityId)
            {
                if (string.IsNullOrEmpty(entityId) ||
                    entityId == view.player_entity_id ||
                    !seen.Add(entityId))
                {
                    return;
                }

                var display = LoreQuery.ResolveSubjectDisplayName(view, entityId);
                var isSelected = selected.HasValue &&
                                 selected.Value.kind == DialogueParticipantKind.Entity &&
                                 selected.Value.entityId == entityId;

                result.Add(new AvatarRailItemModel
                {
                    Target = DialogueTarget.Entity(entityId, display),
                    DisplayName = display,
                    Selected = isSelected,
                    Portrait = ResolvePortrait(view, entityId),
                    CanInspect = LoreQuery.HasDossier(view, entityId),
                    SubjectEntityId = entityId
                });
            }

            var colocated = LoreQuery.CollectCoLocatedEntityIds(view);
            for (var i = 0; i < colocated.Count; i++)
            {
                AddEntity(colocated[i]);
            }

            if (view.dialogues == null)
            {
                return result;
            }

            for (var d = 0; d < view.dialogues.Count; d++)
            {
                var dialogue = view.dialogues[d];
                if (dialogue == null || !dialogue.IsActive || dialogue.participants == null)
                {
                    continue;
                }

                for (var p = 0; p < dialogue.participants.Count; p++)
                {
                    var part = dialogue.participants[p];
                    if (part.KindEnum == DialogueParticipantKind.System)
                    {
                        if (hasSystem)
                        {
                            continue;
                        }

                        hasSystem = true;
                        result.Add(new AvatarRailItemModel
                        {
                            Target = DialogueTarget.System(string.Empty),
                            DisplayName = string.Empty,
                            Selected = selected.HasValue && selected.Value.kind == DialogueParticipantKind.System,
                            HasNotification = false,
                            CanInspect = false
                        });
                        continue;
                    }

                    if (part.KindEnum == DialogueParticipantKind.Entity)
                    {
                        AddEntity(part.entity_id);
                    }
                }
            }

            return result;
        }

        private static Sprite ResolvePortrait(SessionViewDto view, string entityId)
        {
            var node = LoreQuery.FindPortraitNode(view, entityId, LayoutSlots.Avatar);
            var hash = node?.asset?.content_hash;
            if (string.IsNullOrEmpty(hash))
            {
                return null;
            }

            var resolver = ContentHashSpriteResolverLocator.Shared;
            if (resolver.TryResolve(hash, out var sprite, out var error))
            {
                return sprite;
            }

            Debug.LogWarning($"[AvatarRail] portrait miss entity={entityId}: {error}");
            return null;
        }

        private void HandleSelect(DialogueTarget target)
        {
            _selection?.Select(target);
            _intents?.TrySelectDialogueTarget(target);
            if (LatestView != null)
            {
                Paint(LatestView);
            }
        }

        private void HandleInspect(string subjectEntityId)
        {
            _onInspectSubject?.Invoke(subjectEntityId);
        }

        private void HandleSelectionChanged(DialogueTarget? _)
        {
            if (LatestView != null)
            {
                Paint(LatestView);
            }
        }
    }
}
