using System.Collections.Generic;
using Luoxia.Assets;
using Luoxia.Contracts;
using Luoxia.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Features
{
    /// <summary>
    /// Dialogue tab: turn list + input. Free text for effective target
    /// (_selection.Current ?? focused dialogue non-player Entity).
    /// InputBar alpha=1 while dialogue feature active; hidden while event feature active.
    /// </summary>
    public sealed class DialogueFeaturePanel : FeaturePanel
    {
        public const string Id = "dialogue";

        private const string BudgetExhaustedPlaceholder = "今日行动力已尽，请收工";
        private const string NoTargetPlaceholder = "点击上方头像选择交谈对象";

        protected override string ResolveDefaultFeatureId() => Id;

        [SerializeField] private DialogueTurnItemView turnPrefab;
        [SerializeField] private Transform turnContent;
        [SerializeField] private InputField inputField;
        [SerializeField] private Button sendButton;
        [SerializeField] private Text inputPlaceholder;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private CanvasGroup inputBarGroup;

        private ListViewController<DialogueTurnItemModel, DialogueTurnItemView> _list;
        private IPlayerIntentSink _intents;
        private IDialogueSelection _selection;
        private bool _commandInteractable = true;
        private bool _budgetExhausted;

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

        public void SetCommandInteractable(bool interactable)
        {
            _commandInteractable = interactable;
            RefreshPlaceholder();
        }

        public void SetBudgetExhausted(bool exhausted)
        {
            _budgetExhausted = exhausted;
            RefreshPlaceholder();
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

        protected override void OnActiveFeatureChanged(bool active)
        {
            if (inputBarGroup != null)
            {
                inputBarGroup.alpha = active ? 1f : 0f;
                inputBarGroup.blocksRaycasts = active;
                inputBarGroup.interactable = active;
            }

            RefreshPlaceholder();
        }

        public override void OnSessionView(SessionViewDto view)
        {
            if (view == null)
            {
                return;
            }

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
            var dialogue = DialogueTargetResolver.FindFocusedDialogue(
                view, _selection != null ? _selection.Current : null);
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
                        SpeakerName = isPlayer
                            ? string.Empty
                            : ResolveSpeakerName(view, turn),
                        Portrait = ResolveTurnPortrait(view, turn, isPlayer)
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

        private static Sprite ResolveTurnPortrait(SessionViewDto view, DialogueTurnViewDto turn, bool isPlayer)
        {
            string subjectId = null;
            if (isPlayer)
            {
                subjectId = view.player_entity_id;
            }
            else if (turn?.speaker != null &&
                     turn.speaker.KindEnum == DialogueParticipantKind.Entity)
            {
                subjectId = turn.speaker.entity_id;
            }

            if (string.IsNullOrEmpty(subjectId))
            {
                return null;
            }

            var node = LoreQuery.FindPortraitNode(view, subjectId, LayoutSlots.Avatar);
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

            Debug.LogWarning($"[DialogueTurn] portrait miss subject={subjectId}: {error}");
            return null;
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

        private static string ResolveSpeakerName(SessionViewDto view, DialogueTurnViewDto turn)
        {
            if (turn?.speaker == null)
            {
                return string.Empty;
            }

            if (turn.speaker.KindEnum == DialogueParticipantKind.System)
            {
                return string.Empty;
            }

            var id = turn.speaker.entity_id;
            if (string.IsNullOrEmpty(id))
            {
                return string.Empty;
            }

            return LoreQuery.ResolveSubjectDisplayName(view, id);
        }

        private void HandleSend()
        {
            if (_intents == null || inputField == null)
            {
                return;
            }

            if (!IsActiveFeature || _budgetExhausted || !_commandInteractable)
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
            var hasTarget = DialogueTargetResolver.TryResolveEffective(
                LatestView,
                _selection != null ? _selection.Current : null,
                out var effective);
            var inputAllowed = IsActiveFeature && _commandInteractable && !_budgetExhausted && hasTarget;

            if (inputPlaceholder != null)
            {
                if (_budgetExhausted)
                {
                    inputPlaceholder.text = BudgetExhaustedPlaceholder;
                }
                else if (!hasTarget)
                {
                    inputPlaceholder.text = NoTargetPlaceholder;
                }
                else
                {
                    var name = effective.displayName;
                    if (string.IsNullOrEmpty(name) &&
                        effective.kind == DialogueParticipantKind.Entity &&
                        !string.IsNullOrEmpty(effective.entityId))
                    {
                        name = LoreQuery.ResolveSubjectDisplayName(LatestView, effective.entityId);
                    }

                    inputPlaceholder.text = string.IsNullOrEmpty(name)
                        ? "说…"
                        : $"对{name}说…";
                }
            }

            if (inputField != null)
            {
                inputField.interactable = inputAllowed;
                if (_budgetExhausted)
                {
                    inputField.interactable = false;
                }
            }

            if (sendButton != null)
            {
                sendButton.interactable = inputAllowed;
                if (_budgetExhausted)
                {
                    sendButton.interactable = false;
                }
            }

            if (inputBarGroup != null)
            {
                var dialogueActive = IsActiveFeature;
                inputBarGroup.alpha = dialogueActive ? 1f : 0f;
                inputBarGroup.blocksRaycasts = dialogueActive;
                inputBarGroup.interactable = dialogueActive && !_budgetExhausted;
            }
        }
    }
}
