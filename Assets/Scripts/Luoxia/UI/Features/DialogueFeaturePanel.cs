using System.Collections.Generic;
using Luoxia.Assets;
using Luoxia.Contracts;
using Luoxia.Net;
using Luoxia.UI.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Features
{
    /// <summary>
    /// Dialogue dock content: turn list + pending EventCards (after turns) + input.
    /// Optimistic send shows player echo + thinking placeholder until SessionView / gate settles.
    /// </summary>
    public sealed class DialogueFeaturePanel : FeaturePanel
    {
        public const string Id = "dialogue";

        private const string BudgetExhaustedPlaceholder = "今日行动力已尽，请收工";
        private const string NoTargetPlaceholder = "点击上方头像选择交谈对象";
        private const string ThinkingPlaceholder = "正在思考中…";

        private enum SendState
        {
            Idle,
            Optimistic
        }

        protected override string ResolveDefaultFeatureId() => Id;

        [SerializeField] private DialogueTurnItemView turnPrefab;
        [SerializeField] private Transform turnContent;
        [SerializeField] private EventCardItemView cardItemPrefab;
        [SerializeField] private Transform pendingCardsRoot;
        [SerializeField] private GameObject pendingCardsGroup;
        [SerializeField] private Text pendingCardsHeaderText;
        [SerializeField] private Button openAllButton;
        [SerializeField] private EventCardConfirmPanel confirmPanel;
        [SerializeField] private InputField inputField;
        [SerializeField] private Button sendButton;
        [SerializeField] private Text inputPlaceholder;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private CanvasGroup inputBarGroup;
        [SerializeField] private string pendingCountFormat = "今日事件 · 待开启 {0} 件";

        private ListViewController<DialogueTurnItemModel, DialogueTurnItemView> _turnList;
        private ListViewController<EventCardItemModel, EventCardItemView> _cardList;
        private IPlayerIntentSink _intents;
        private IDialogueSelection _selection;
        private ICommandGate _gate;
        private bool _commandInteractable = true;
        private bool _budgetExhausted;
        private SendState _sendState = SendState.Idle;
        private string _optimisticText;
        private bool _scrollToPendingRequested;

        public void Configure(
            IPlayerIntentSink intents,
            IDialogueSelection selection,
            EventCardConfirmPanel confirm = null,
            ICommandGate gate = null)
        {
            _intents = intents;
            _selection = selection;
            if (confirm != null)
            {
                confirmPanel = confirm;
            }

            BindGate(gate);
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
            ApplyCardOpenHandlers();
            if (openAllButton != null)
            {
                openAllButton.interactable = _commandInteractable && CountPendingCards(LatestView) > 0;
            }
        }

        public void SetBudgetExhausted(bool exhausted)
        {
            _budgetExhausted = exhausted;
            RefreshPlaceholder();
        }

        /// <summary>
        /// Expand path from badge / EndDay「去看看」: keep PendingCards in view after next rebuild.
        /// </summary>
        public void ScrollToPendingCards()
        {
            _scrollToPendingRequested = true;
            if (pendingCardsGroup != null && pendingCardsGroup.activeInHierarchy && scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                ScrollPendingIntoView();
            }
        }

        protected override void Awake()
        {
            base.Awake();
            if (turnPrefab != null && turnContent != null)
            {
                _turnList = new ListViewController<DialogueTurnItemModel, DialogueTurnItemView>(
                    turnPrefab, turnContent);
            }

            if (cardItemPrefab != null && pendingCardsRoot != null)
            {
                _cardList = new ListViewController<EventCardItemModel, EventCardItemView>(
                    cardItemPrefab, pendingCardsRoot);
            }
        }

        protected override void OnBound()
        {
            if (sendButton != null)
            {
                sendButton.onClick.AddListener(HandleSend);
            }

            if (openAllButton != null)
            {
                openAllButton.onClick.AddListener(HandleOpenAll);
            }
        }

        protected override void OnUnbound()
        {
            if (sendButton != null)
            {
                sendButton.onClick.RemoveListener(HandleSend);
            }

            if (openAllButton != null)
            {
                openAllButton.onClick.RemoveListener(HandleOpenAll);
            }

            if (_selection != null)
            {
                _selection.Changed -= HandleSelectionChanged;
            }

            UnbindGate();
            ClearOptimistic();
            _turnList?.Clear();
            _cardList?.Clear();
        }

        protected override void OnActiveFeatureChanged(bool active)
        {
            if (inputBarGroup != null)
            {
                inputBarGroup.alpha = 1f;
                inputBarGroup.blocksRaycasts = true;
                inputBarGroup.interactable = !_budgetExhausted;
            }

            RefreshPlaceholder();
        }

        public override void OnSessionView(SessionViewDto view)
        {
            if (view == null)
            {
                return;
            }

            ResolveOptimisticAgainstView(view);
            RebuildTurns(view);
            RebuildPendingCards(view);
            confirmPanel?.OnSessionView(view);
            RefreshPlaceholder();

            if (_scrollToPendingRequested)
            {
                _scrollToPendingRequested = false;
                Canvas.ForceUpdateCanvases();
                ScrollPendingIntoView();
            }
        }

        private void BindGate(ICommandGate gate)
        {
            UnbindGate();
            _gate = gate;
            if (_gate == null)
            {
                return;
            }

            _gate.CommandFailed += HandleGateFailed;
            _gate.CommandCompleted += HandleGateCompleted;
        }

        private void UnbindGate()
        {
            if (_gate == null)
            {
                return;
            }

            _gate.CommandFailed -= HandleGateFailed;
            _gate.CommandCompleted -= HandleGateCompleted;
            _gate = null;
        }

        private void HandleGateFailed(string commandId, string reason)
        {
            if (_sendState != SendState.Optimistic)
            {
                return;
            }

            var rollback = _optimisticText;
            ClearOptimistic();
            if (inputField != null && !string.IsNullOrEmpty(rollback))
            {
                inputField.text = rollback;
            }

            RebuildTurns(LatestView);
            RefreshPlaceholder();
        }

        private void HandleGateCompleted(string commandId)
        {
            if (_sendState != SendState.Optimistic)
            {
                return;
            }

            // Drop thinking ghost; echo stays until SessionView matches.
            RebuildTurns(LatestView);
        }

        private void ResolveOptimisticAgainstView(SessionViewDto view)
        {
            if (_sendState != SendState.Optimistic || string.IsNullOrEmpty(_optimisticText))
            {
                return;
            }

            var dialogue = DialogueTargetResolver.FindFocusedDialogue(
                view, _selection != null ? _selection.Current : null);
            if (dialogue?.turns == null)
            {
                return;
            }

            for (var i = 0; i < dialogue.turns.Count; i++)
            {
                var turn = dialogue.turns[i];
                if (turn == null || !IsPlayerSpeaker(turn, view.player_entity_id))
                {
                    continue;
                }

                if (turn.text == _optimisticText)
                {
                    ClearOptimistic();
                    return;
                }
            }
        }

        private void ClearOptimistic()
        {
            _sendState = SendState.Idle;
            _optimisticText = null;
        }

        private void RebuildTurns(SessionViewDto view)
        {
            if (_turnList == null)
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

            if (_sendState == SendState.Optimistic && !string.IsNullOrEmpty(_optimisticText))
            {
                var echoAlready = false;
                for (var i = 0; i < models.Count; i++)
                {
                    if (models[i].IsPlayer && models[i].Turn != null &&
                        models[i].Turn.text == _optimisticText)
                    {
                        echoAlready = true;
                        break;
                    }
                }

                if (!echoAlready)
                {
                    models.Add(new DialogueTurnItemModel
                    {
                        Turn = new DialogueTurnViewDto { text = _optimisticText },
                        IsPlayer = true,
                        SpeakerName = string.Empty,
                        Portrait = ResolveTurnPortrait(view, null, true)
                    });
                }

                // Thinking ghost only while command still pending.
                if (_gate != null && _gate.HasPending)
                {
                    models.Add(new DialogueTurnItemModel
                    {
                        Turn = new DialogueTurnViewDto { text = ThinkingPlaceholder },
                        IsPlayer = false,
                        SpeakerName = string.Empty,
                        Portrait = null
                    });
                }
            }

            _turnList.SetItems(models);
            if (scrollRect != null && !_scrollToPendingRequested)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }

        private void RebuildPendingCards(SessionViewDto view)
        {
            if (_cardList == null)
            {
                return;
            }

            var models = new List<EventCardItemModel>();
            if (view.event_cards != null)
            {
                for (var i = 0; i < view.event_cards.Count; i++)
                {
                    var card = view.event_cards[i];
                    if (card == null || !card.IsAvailable)
                    {
                        continue;
                    }

                    models.Add(new EventCardItemModel
                    {
                        Card = card,
                        SourceLabel = string.Empty,
                        Portrait = null
                    });
                }
            }

            if (pendingCardsGroup != null)
            {
                pendingCardsGroup.SetActive(models.Count > 0);
            }

            _cardList.SetItems(models);
            ApplyCardOpenHandlers();

            if (pendingCardsHeaderText != null)
            {
                pendingCardsHeaderText.text = string.Format(pendingCountFormat, models.Count);
            }

            if (openAllButton != null)
            {
                openAllButton.interactable = _commandInteractable && models.Count > 0;
            }
        }

        private void ApplyCardOpenHandlers()
        {
            if (_cardList == null)
            {
                return;
            }

            for (var i = 0; i < _cardList.ActiveItems.Count; i++)
            {
                _cardList.ActiveItems[i].SetOpenHandler(_commandInteractable ? HandleOpenOne : null);
            }
        }

        private void HandleOpenOne(string eventCardId)
        {
            if (!_commandInteractable)
            {
                return;
            }

            if (confirmPanel == null)
            {
                Debug.LogError(
                    "[DialogueFeaturePanel] EventCardConfirmPanel missing. Rebuild via Luoxia/UI/Build Main World Screen.");
                return;
            }

            var view = LatestView;
            if (view == null)
            {
                return;
            }

            confirmPanel.TryOpen(view, eventCardId);
        }

        private void HandleOpenAll()
        {
            if (!_commandInteractable)
            {
                return;
            }

            _intents?.TryTriggerAllAvailableEventCards();
        }

        private void ScrollPendingIntoView()
        {
            if (scrollRect == null || pendingCardsGroup == null || scrollRect.content == null)
            {
                return;
            }

            var content = scrollRect.content;
            var target = pendingCardsGroup.GetComponent<RectTransform>();
            if (target == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            var contentH = content.rect.height;
            var viewportH = scrollRect.viewport != null ? scrollRect.viewport.rect.height : 0f;
            if (contentH <= viewportH + 1f)
            {
                scrollRect.verticalNormalizedPosition = 0f;
                return;
            }

            // Normalize so pending block sits near the bottom of the viewport.
            var y = Mathf.Abs(target.anchoredPosition.y);
            var max = contentH - viewportH;
            var norm = 1f - Mathf.Clamp01(y / max);
            scrollRect.verticalNormalizedPosition = Mathf.Clamp01(norm);
        }

        private static int CountPendingCards(SessionViewDto view)
        {
            if (view?.event_cards == null)
            {
                return 0;
            }

            var n = 0;
            for (var i = 0; i < view.event_cards.Count; i++)
            {
                if (view.event_cards[i] != null && view.event_cards[i].IsAvailable)
                {
                    n++;
                }
            }

            return n;
        }

        private static Sprite ResolveTurnPortrait(SessionViewDto view, DialogueTurnViewDto turn, bool isPlayer)
        {
            string subjectId = null;
            if (isPlayer)
            {
                subjectId = view?.player_entity_id;
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

            if (_budgetExhausted || !_commandInteractable || _sendState == SendState.Optimistic)
            {
                return;
            }

            var text = inputField.text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var trimmed = text.Trim();
            if (!_intents.TrySendDialogueText(trimmed))
            {
                return;
            }

            _sendState = SendState.Optimistic;
            _optimisticText = trimmed;
            inputField.text = string.Empty;
            RebuildTurns(LatestView);
            RefreshPlaceholder();
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
            var inputAllowed = _commandInteractable && !_budgetExhausted && hasTarget &&
                               _sendState == SendState.Idle;

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
                inputBarGroup.alpha = 1f;
                inputBarGroup.blocksRaycasts = true;
                inputBarGroup.interactable = !_budgetExhausted;
            }
        }
    }
}
