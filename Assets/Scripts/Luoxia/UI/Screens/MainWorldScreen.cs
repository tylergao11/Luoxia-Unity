using Luoxia.Assets;
using Luoxia.Contracts;
using Luoxia.Net;
using Luoxia.Session;
using Luoxia.UI.Core;
using Luoxia.UI.Features;
using Luoxia.UI.Immersion;
using Luoxia.UI.Widgets;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Screens
{
    /// <summary>
    /// Main 2D world shell: shared HUD + portrait layer + collapsible FeatureDock + immersive overlays.
    /// Composition only — no world rules / no plot hardcoding.
    /// Scene wiring is owned by MainWorldUiBuilder; this screen does not FindOrCreate chrome at Play time.
    /// Dock expands on avatar selection or RevealPendingCards (badge / EndDay「去看看」).
    /// </summary>
    public sealed class MainWorldScreen : LuoxiaView
    {
        private static readonly Color EndDayIdleLabel = new Color(1f, 0.95f, 0.85f, 0.85f);
        private static readonly Color EndDayPrimaryLabel = new Color(1f, 0.98f, 0.9f, 1f);

        [Header("Shared HUD")]
        [SerializeField] private LocationDayWidget locationDayWidget;
        [SerializeField] private EventBudgetWidget eventBudgetWidget;
        [SerializeField] private EventBadgeBar eventBadgeBar;
        [SerializeField] private AvatarRailWidget avatarRailWidget;
        [SerializeField] private ScenePortraitLayer scenePortraitLayer;
        [SerializeField] private Button mapButton;
        [SerializeField] private Button endDayButton;
        [SerializeField] private Image endDayButtonImage;
        [SerializeField] private Text endDayButtonLabel;
        [SerializeField] private Sprite endDayIdleSprite;
        [SerializeField] private Sprite endDayPrimarySprite;
        [SerializeField] private CommandFeedbackHud commandFeedback;
        [SerializeField] private SessionFatalOverlay fatalOverlay;

        [Header("Feature dock")]
        [SerializeField] private RectTransform featureDock;
        [SerializeField] private CanvasGroup featureDockGroup;
        [SerializeField] private DialogueFeaturePanel dialoguePanel;
        [SerializeField] private EventCardConfirmPanel eventCardConfirmPanel;
        [SerializeField] private EndDayConfirmPanel endDayConfirmPanel;

        [Header("Map")]
        [SerializeField] private MapDestinationPanel mapDestinationPanel;

        [Header("Immersion")]
        [SerializeField] private ImmersiveShellController immersiveShell;
        [SerializeField] private CharacterDossierPanel dossierPanel;
        [SerializeField] private LoreChapterOverlay chapterOverlay;
        [SerializeField] private NarrativeFramePlayer narrativeFramePlayer;
        [SerializeField] private StageShellOverlay stageShellOverlay;

        private IPlayerIntentSink _intents;
        private IDialogueSelection _selection;
        private IPresentationBus _presentation;
        private ICommandGate _gate;
        private ISessionReplica _replica;
        private System.Action _fatalRetry;
        private bool _commandLocked;
        private bool _playerPhase = true;
        private bool _hasDialogueBudget = true;
        private bool _cardsViewRequested;
        private int _trackedDay = int.MinValue;
        private bool _dockExpanded;

        /// <summary>Always dialogue after event-page merge; kept for Play Accept probes.</summary>
        public string ActiveFeatureId => DialogueFeaturePanel.Id;

        /// <summary>
        /// Wire pure C# services from app composition root (not Unity singletons).
        /// </summary>
        public void Configure(
            ISessionViewSource session,
            IPlayerIntentSink intents,
            IDialogueSelection selection,
            IPresentationBus presentation = null,
            ICommandGate gate = null,
            IContentHashSpriteResolver spriteResolver = null,
            ISessionReplica replica = null,
            System.Action fatalRetry = null)
        {
            _intents = intents;
            _selection = selection ?? new DialogueSelection();
            _presentation = presentation;
            _replica = replica;
            _fatalRetry = fatalRetry;
            BindGate(gate);
            BindReplica(replica);
            BindSelection(_selection);

            if (spriteResolver != null)
            {
                ContentHashSpriteResolverLocator.SetShared(spriteResolver);
                scenePortraitLayer?.SetSpriteResolver(spriteResolver);
            }

            dialoguePanel?.Configure(_intents, _selection, eventCardConfirmPanel, gate);
            eventCardConfirmPanel?.Configure(_intents);
            mapDestinationPanel?.Configure(_intents);
            avatarRailWidget?.Configure(_selection, _intents, HandleInspectSubject);
            eventBadgeBar?.Configure(_intents, RevealPendingCards);
            fatalOverlay?.Configure(HandleFatalRetry);

            if (immersiveShell != null)
            {
                immersiveShell.Configure(session, _presentation, _selection);
                immersiveShell.ConfigureStageIntents(_intents);
            }
            else
            {
                narrativeFramePlayer?.Bind(_presentation);
                stageShellOverlay?.Bind(_presentation);
                stageShellOverlay?.Configure(_intents);
                dossierPanel?.BindSession(session);
                scenePortraitLayer?.SetSubjectInspectHandler(HandleInspectSubject);
            }

            BindSession(session);
            BindChildren(session);

            dialoguePanel?.SetActiveFeature(true);
            ApplyDockExpanded(ComputeDockExpanded());
            RefreshCommandLockUi();
        }

        public void ShowUserError(string message)
        {
            commandFeedback?.ShowError(message);
        }

        public void ShowFatal(string title, string detail, bool terminalToProvision = false)
        {
            if (fatalOverlay == null)
            {
                Debug.LogError(
                    "[MainWorldScreen] SessionFatalOverlay missing. Rebuild via Luoxia/UI/Build Main World Screen.");
                return;
            }

            fatalOverlay.Show(title, detail, allowSessionRetry: !terminalToProvision);
        }

        /// <summary>
        /// Hide/reset SessionFatalOverlay after a successful reconnect or in-Play reprovision
        /// has synchronized a playable session.
        /// </summary>
        public void HideFatal()
        {
            fatalOverlay?.Hide();
        }

        /// <summary>
        /// Badge / EndDay「去看看」: request cards view, expand dock, scroll to pending block.
        /// </summary>
        public void RevealPendingCards()
        {
            _cardsViewRequested = true;
            ApplyDockExpanded(ComputeDockExpanded());
            dialoguePanel?.ScrollToPendingCards();
        }

        /// <summary>
        /// Compatibility entry used by Play Accept. Event id maps to RevealPendingCards;
        /// dialogue id refreshes dock from selection.
        /// </summary>
        public void ActivateFeature(string featureId)
        {
            if (featureId == "event")
            {
                RevealPendingCards();
                return;
            }

            dialoguePanel?.SetActiveFeature(true);
            ApplyDockExpanded(ComputeDockExpanded());
        }

        protected override void OnBound()
        {
            if (mapButton != null)
            {
                mapButton.onClick.AddListener(HandleMap);
            }

            if (endDayButton != null)
            {
                endDayButton.onClick.AddListener(HandleEndDay);
            }
        }

        protected override void OnUnbound()
        {
            if (mapButton != null)
            {
                mapButton.onClick.RemoveAllListeners();
            }

            if (endDayButton != null)
            {
                endDayButton.onClick.RemoveAllListeners();
            }

            UnbindGate();
            UnbindReplica();
            UnbindSelection();
            narrativeFramePlayer?.Unbind();
            stageShellOverlay?.Unbind();
        }

        public override void OnSessionView(SessionViewDto view)
        {
            _playerPhase = view?.day_cycle == null || view.day_cycle.PhaseEnum == DayPhase.Player;
            // Dialogue pairs with EventCard spend; remaining===0 → only day-end for input.
            // Opening EventCards stays unlocked (开卡不锁).
            _hasDialogueBudget = view?.event_budget == null || view.event_budget.remaining > 0;
            InvalidateStaleDialogueSelection(view);
            SyncCardsViewRequest(view);
            // View replace clears pending toast only (_locked); does not wipe ShowError.
            commandFeedback?.ClearPending();
            eventCardConfirmPanel?.OnSessionView(view);
            ApplyDockExpanded(ComputeDockExpanded());
            RefreshCommandLockUi();
        }

        private void SyncCardsViewRequest(SessionViewDto view)
        {
            var day = view?.day_cycle != null ? view.day_cycle.day : -1;
            if (_trackedDay == int.MinValue)
            {
                _trackedDay = day;
            }
            else if (day != _trackedDay)
            {
                _trackedDay = day;
                _cardsViewRequested = false;
            }

            if (CountAvailableCards(view) == 0)
            {
                _cardsViewRequested = false;
            }
        }

        private bool ComputeDockExpanded()
        {
            var available = CountAvailableCards(LatestView);
            return (_selection != null && _selection.Current != null) ||
                   (_cardsViewRequested && available > 0);
        }

        private void ApplyDockExpanded(bool expanded)
        {
            _dockExpanded = expanded;
            if (featureDock == null || featureDockGroup == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            var h = featureDock.rect.height;
            if (h < 1f)
            {
                h = 1920f * 0.48f;
            }

            var x = featureDock.anchoredPosition.x;
            featureDock.anchoredPosition = new Vector2(x, expanded ? 0f : -h);
            featureDockGroup.blocksRaycasts = expanded;
            featureDockGroup.interactable = expanded;
        }

        /// <summary>
        /// A selected dialogue target must remain offerable: co-located subject or
        /// participant of an active dialogue. After map.move / day rollover the stale
        /// selection is cleared so input cannot address an absent character.
        /// </summary>
        private void InvalidateStaleDialogueSelection(SessionViewDto view)
        {
            if (_selection?.Current == null || view == null)
            {
                return;
            }

            var current = _selection.Current.Value;
            if (current.kind == DialogueParticipantKind.Entity)
            {
                if (string.IsNullOrEmpty(current.entityId) ||
                    !IsOfferableEntity(view, current.entityId))
                {
                    _selection.Clear();
                }

                return;
            }

            if (current.kind == DialogueParticipantKind.System &&
                !HasActiveSystemDialogue(view))
            {
                _selection.Clear();
            }
        }

        private static bool IsOfferableEntity(SessionViewDto view, string entityId)
        {
            var colocated = LoreQuery.CollectCoLocatedEntityIds(view);
            for (var i = 0; i < colocated.Count; i++)
            {
                if (colocated[i] == entityId)
                {
                    return true;
                }
            }

            if (view.dialogues == null)
            {
                return false;
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
                    if (part != null &&
                        part.KindEnum == DialogueParticipantKind.Entity &&
                        part.entity_id == entityId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasActiveSystemDialogue(SessionViewDto view)
        {
            if (view.dialogues == null)
            {
                return false;
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
                    if (part != null && part.KindEnum == DialogueParticipantKind.System)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void BindSelection(IDialogueSelection selection)
        {
            UnbindSelection();
            _selection = selection;
            if (_selection == null)
            {
                return;
            }

            _selection.Changed += HandleSelectionChanged;
        }

        private void UnbindSelection()
        {
            if (_selection == null)
            {
                return;
            }

            _selection.Changed -= HandleSelectionChanged;
        }

        private void HandleSelectionChanged(DialogueTarget? target)
        {
            ApplyDockExpanded(ComputeDockExpanded());
        }

        private void BindGate(ICommandGate gate)
        {
            UnbindGate();
            _gate = gate;
            if (_gate == null)
            {
                return;
            }

            _gate.PendingChanged += HandleGatePendingChanged;
            _gate.CommandFailed += HandleGateFailed;
            _gate.CommandCompleted += HandleGateCompleted;
            _commandLocked = _gate.HasPending;
        }

        private void UnbindGate()
        {
            if (_gate == null)
            {
                return;
            }

            _gate.PendingChanged -= HandleGatePendingChanged;
            _gate.CommandFailed -= HandleGateFailed;
            _gate.CommandCompleted -= HandleGateCompleted;
            _gate = null;
        }

        private void BindReplica(ISessionReplica replica)
        {
            UnbindReplica();
            _replica = replica;
            if (_replica == null)
            {
                return;
            }

            _replica.StateChanged += HandleReplicaStateChanged;
            if (_replica.State == SessionReplicaState.Fatal)
            {
                ShowFatal("会话中断", _replica.FatalReason);
            }
        }

        private void UnbindReplica()
        {
            if (_replica == null)
            {
                return;
            }

            _replica.StateChanged -= HandleReplicaStateChanged;
            _replica = null;
        }

        private void HandleReplicaStateChanged(SessionReplicaState previous, SessionReplicaState next)
        {
            if (next == SessionReplicaState.Fatal)
            {
                ShowFatal("会话中断", _replica != null ? _replica.FatalReason : "unknown");
                return;
            }

            if (previous == SessionReplicaState.Fatal &&
                (next == SessionReplicaState.Synchronized || next == SessionReplicaState.Resynchronizing))
            {
                fatalOverlay?.Hide();
            }
        }

        private void HandleFatalRetry()
        {
            _fatalRetry?.Invoke();
        }

        private void HandleGatePendingChanged()
        {
            _commandLocked = _gate != null && _gate.HasPending;
            if (_commandLocked)
            {
                commandFeedback?.ShowPending("命令发送中…");
            }
            else
            {
                commandFeedback?.ClearPending();
            }

            RefreshCommandLockUi();
        }

        private void HandleGateFailed(string commandId, string reason)
        {
            // Fail already cleared HasPending via PendingChanged; replace with explicit error chrome.
            commandFeedback?.ShowError(reason);
            RefreshCommandLockUi();
        }

        private void HandleGateCompleted(string commandId)
        {
            commandFeedback?.ClearPending();
            RefreshCommandLockUi();
        }

        private void RefreshCommandLockUi()
        {
            var canMutate = !_commandLocked;
            // Budget exhaustion locks dialogue input only; EventCard open stays available.
            dialoguePanel?.SetBudgetExhausted(!_hasDialogueBudget);
            dialoguePanel?.SetCommandInteractable(canMutate);

            eventCardConfirmPanel?.SetCommandLocked(_commandLocked);

            if (endDayButton != null)
            {
                endDayButton.interactable = canMutate && _playerPhase;
            }

            // Map stays enabled at remaining===0 (navigation does not spend AP).
            if (mapButton != null)
            {
                mapButton.interactable = canMutate;
            }

            ApplyEndDayPrimaryChrome(!_hasDialogueBudget && _playerPhase && canMutate);
        }

        private void ApplyEndDayPrimaryChrome(bool primary)
        {
            if (endDayButtonImage != null)
            {
                // Builder wires normal/active 9-slice chrome; primary swaps art, not tint.
                endDayButtonImage.sprite = primary ? endDayPrimarySprite : endDayIdleSprite;
                endDayButtonImage.color = Color.white;
            }

            if (endDayButtonLabel != null)
            {
                // Emphasis via color only — faux-bold doubles CJK strokes into mud.
                endDayButtonLabel.color = primary ? EndDayPrimaryLabel : EndDayIdleLabel;
            }
        }

        private void HandleInspectSubject(string subjectEntityId)
        {
            if (dossierPanel == null || LatestView == null)
            {
                return;
            }

            dossierPanel.TryOpen(LatestView, subjectEntityId);
        }

        private void BindChildren(ISessionViewSource session)
        {
            BindChild(locationDayWidget, session);
            BindChild(eventBudgetWidget, session);
            BindChild(eventBadgeBar, session);
            BindChild(avatarRailWidget, session);
            BindChild(scenePortraitLayer, session);
            BindChild(dialoguePanel, session);
            BindChild(dossierPanel, session);
            BindChild(immersiveShell, session);
            BindChild(mapDestinationPanel, session);
        }

        private static void BindChild(ISessionViewBinder binder, ISessionViewSource session)
        {
            binder?.BindSession(session);
        }

        private void HandleMap()
        {
            if (_commandLocked)
            {
                return;
            }

            // At most one float panel — close EndDay confirm if open.
            endDayConfirmPanel?.Dismiss();
            _intents?.TryOpenMap();
            mapDestinationPanel?.Open();
        }

        private void HandleEndDay()
        {
            if (_commandLocked || !_playerPhase)
            {
                return;
            }

            var pending = CollectAvailableCardsForCurrentDay(LatestView);
            if (pending.Count == 0)
            {
                _intents?.TryEndPlayerDay();
                return;
            }

            if (endDayConfirmPanel == null)
            {
                Debug.LogError(
                    "[MainWorldScreen] EndDayConfirmPanel missing. Rebuild via Luoxia/UI/Build Main World Screen.");
                _intents?.TryEndPlayerDay();
                return;
            }

            mapDestinationPanel?.Close();
            endDayConfirmPanel.TryOpen(
                pending,
                onForceEnd: () => _intents?.TryEndPlayerDay(),
                onGoLook: RevealPendingCards);
        }

        private static int CountAvailableCards(SessionViewDto view)
        {
            if (view?.event_cards == null)
            {
                return 0;
            }

            var n = 0;
            for (var i = 0; i < view.event_cards.Count; i++)
            {
                var card = view.event_cards[i];
                if (card != null && card.IsAvailable)
                {
                    n++;
                }
            }

            return n;
        }

        private static System.Collections.Generic.List<EventCardViewDto> CollectAvailableCardsForCurrentDay(
            SessionViewDto view)
        {
            var result = new System.Collections.Generic.List<EventCardViewDto>();
            if (view?.event_cards == null)
            {
                return result;
            }

            var day = view.day_cycle != null ? view.day_cycle.day : -1;
            for (var i = 0; i < view.event_cards.Count; i++)
            {
                var card = view.event_cards[i];
                if (card == null || !card.IsAvailable)
                {
                    continue;
                }

                // Match current day when projected; unknown day (−1) still counts as pending.
                if (day >= 0 && card.day >= 0 && card.day != day)
                {
                    continue;
                }

                result.Add(card);
            }

            return result;
        }
    }
}
