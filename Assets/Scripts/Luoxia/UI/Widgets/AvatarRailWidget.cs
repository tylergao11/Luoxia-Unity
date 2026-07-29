using System.Collections.Generic;
using Luoxia.Contracts;
using Luoxia.UI.Core;
using UnityEngine;

namespace Luoxia.UI.Widgets
{
    /// <summary>
    /// Top avatar strip for selectable dialogue targets (NPC / System).
    /// Does not invent characters: derived from active dialogues + portrait render nodes for now.
    /// </summary>
    public sealed class AvatarRailWidget : HudWidget
    {
        [SerializeField] private AvatarRailItemView itemPrefab;
        [SerializeField] private Transform contentRoot;

        private ListViewController<AvatarRailItemModel, AvatarRailItemView> _list;
        private IDialogueSelection _selection;
        private IPlayerIntentSink _intents;

        public void Configure(IDialogueSelection selection, IPlayerIntentSink intents)
        {
            _selection = selection;
            _intents = intents;

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
            }
        }

        private List<AvatarRailItemModel> BuildModels(SessionViewDto view)
        {
            var result = new List<AvatarRailItemModel>();
            var selected = _selection != null ? _selection.Current : null;

            // System is always available as a product premise.
            result.Add(new AvatarRailItemModel
            {
                Target = DialogueTarget.System("渡口风闻"),
                DisplayName = "渡口风闻",
                Selected = selected.HasValue && selected.Value.kind == DialogueParticipantKind.System,
                HasNotification = false
            });

            // Participants from active dialogues (entity side, not player).
            if (view.dialogues == null)
            {
                return result;
            }

            var seen = new HashSet<string>();
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
                    if (part.KindEnum != DialogueParticipantKind.Entity)
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(part.entity_id) || seen.Contains(part.entity_id))
                    {
                        continue;
                    }

                    if (part.entity_id == view.player_entity_id)
                    {
                        continue;
                    }

                    seen.Add(part.entity_id);
                    var isSelected = selected.HasValue &&
                                     selected.Value.kind == DialogueParticipantKind.Entity &&
                                     selected.Value.entityId == part.entity_id;

                    result.Add(new AvatarRailItemModel
                    {
                        Target = DialogueTarget.Entity(part.entity_id, ShortId(part.entity_id)),
                        DisplayName = ShortId(part.entity_id),
                        Selected = isSelected,
                        Portrait = null
                    });
                }
            }

            return result;
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

        private void HandleSelectionChanged(DialogueTarget? _)
        {
            if (LatestView != null)
            {
                Paint(LatestView);
            }
        }

        private static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length <= 8)
            {
                return id;
            }

            return id.Substring(0, 8);
        }
    }
}
