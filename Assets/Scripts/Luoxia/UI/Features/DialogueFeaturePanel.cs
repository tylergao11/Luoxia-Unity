using System.Collections.Generic;
using Luoxia.Contracts;
using Luoxia.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Features
{
    /// <summary>
    /// Dialogue tab: turn list + input. Free text only for currently selected NPC/System.
    /// </summary>
    public sealed class DialogueFeaturePanel : FeaturePanel
    {
        public const string Id = "dialogue";

        protected override string ResolveDefaultFeatureId() => Id;

        [SerializeField] private DialogueTurnItemView turnPrefab;
        [SerializeField] private Transform turnContent;
        [SerializeField] private InputField inputField;
        [SerializeField] private Button sendButton;
        [SerializeField] private Text inputPlaceholder;
        [SerializeField] private ScrollRect scrollRect;

        private ListViewController<DialogueTurnItemModel, DialogueTurnItemView> _list;
        private IPlayerIntentSink _intents;
        private IDialogueSelection _selection;
        private string _playerEntityId;

        public void Configure(IPlayerIntentSink intents, IDialogueSelection selection)
        {
            _intents = intents;
            _selection = selection;
            if (_selection != null)
            {
                _selection.Changed -= HandleSelectionChanged;
                _selection.Changed += HandleSelectionChanged;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            if (turnPrefab != null && turnContent != null)
            {
                _list = new ListViewController<DialogueTurnItemModel, DialogueTurnItemView>(turnPrefab, turnContent);
            }
        }

        protected override void OnBound()
        {
            if (sendButton != null)
            {
                sendButton.onClick.AddListener(HandleSend);
            }
        }

        protected override void OnUnbound()
        {
            if (sendButton != null)
            {
                sendButton.onClick.RemoveListener(HandleSend);
            }

            if (_selection != null)
            {
                _selection.Changed -= HandleSelectionChanged;
            }

            _list?.Clear();
        }

        public override void OnSessionView(SessionViewDto view)
        {
            if (view == null)
            {
                return;
            }

            _playerEntityId = view.player_entity_id;
            RebuildTurns(view);
            RefreshPlaceholder();
        }

        private void RebuildTurns(SessionViewDto view)
        {
            if (_list == null)
            {
                return;
            }

            var models = new List<DialogueTurnItemModel>();
            var dialogue = FindFocusedDialogue(view);
            if (dialogue?.turns != null)
            {
                for (var i = 0; i < dialogue.turns.Count; i++)
                {
                    var turn = dialogue.turns[i];
                    var isPlayer = IsPlayerSpeaker(turn, view.player_entity_id);
                    models.Add(new DialogueTurnItemModel
                    {
                        Turn = turn,
                        IsPlayer = isPlayer,
                        SpeakerName = isPlayer ? "你" : ResolveSpeakerName(turn)
                    });
                }
            }

            _list.SetItems(models);
            if (scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }

        private DialogueViewDto FindFocusedDialogue(SessionViewDto view)
        {
            if (view.dialogues == null || view.dialogues.Count == 0)
            {
                return null;
            }

            var selected = _selection != null ? _selection.Current : null;
            for (var i = 0; i < view.dialogues.Count; i++)
            {
                var d = view.dialogues[i];
                if (d == null || !d.IsActive)
                {
                    continue;
                }

                if (!selected.HasValue)
                {
                    return d;
                }

                if (MatchesSelection(d, selected.Value, view.player_entity_id))
                {
                    return d;
                }
            }

            return null;
        }

        private static bool MatchesSelection(DialogueViewDto dialogue, DialogueTarget target, string playerId)
        {
            if (dialogue.participants == null)
            {
                return false;
            }

            for (var i = 0; i < dialogue.participants.Count; i++)
            {
                var p = dialogue.participants[i];
                if (target.kind == DialogueParticipantKind.System &&
                    p.KindEnum == DialogueParticipantKind.System)
                {
                    return true;
                }

                if (target.kind == DialogueParticipantKind.Entity &&
                    p.KindEnum == DialogueParticipantKind.Entity &&
                    p.entity_id == target.entityId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPlayerSpeaker(DialogueTurnViewDto turn, string playerEntityId)
        {
            if (turn?.speaker == null)
            {
                return false;
            }

            if (turn.speaker.KindEnum == DialogueParticipantKind.Human)
            {
                return true;
            }

            return turn.speaker.KindEnum == DialogueParticipantKind.Entity &&
                   turn.speaker.entity_id == playerEntityId;
        }

        private static string ResolveSpeakerName(DialogueTurnViewDto turn)
        {
            if (turn?.speaker == null)
            {
                return string.Empty;
            }

            if (turn.speaker.KindEnum == DialogueParticipantKind.System)
            {
                return "渡口风闻";
            }

            var id = turn.speaker.entity_id;
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }

            return id.Length > 8 ? id.Substring(0, 8) : id;
        }

        private void HandleSend()
        {
            if (_intents == null || inputField == null)
            {
                return;
            }

            var text = inputField.text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (_intents.TrySendDialogueText(text.Trim()))
            {
                inputField.text = string.Empty;
            }
        }

        private void HandleSelectionChanged(DialogueTarget? target)
        {
            RefreshPlaceholder();
            if (LatestView != null)
            {
                RebuildTurns(LatestView);
            }
        }

        private void RefreshPlaceholder()
        {
            if (inputPlaceholder == null)
            {
                return;
            }

            var selected = _selection != null ? _selection.Current : null;
            if (!selected.HasValue)
            {
                inputPlaceholder.text = "请先选择交谈对象……";
                if (inputField != null)
                {
                    inputField.interactable = false;
                }

                if (sendButton != null)
                {
                    sendButton.interactable = false;
                }

                return;
            }

            var name = selected.Value.displayName ?? "对方";
            inputPlaceholder.text = $"你想对{name}说什么……";
            if (inputField != null)
            {
                inputField.interactable = true;
            }

            if (sendButton != null)
            {
                sendButton.interactable = true;
            }
        }
    }
}
