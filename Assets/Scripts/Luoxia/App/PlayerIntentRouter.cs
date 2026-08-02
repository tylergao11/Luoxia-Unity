using System;
using Luoxia.Contracts;
using Luoxia.Net;
using Luoxia.Session;
using Luoxia.UI.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Luoxia.App
{
    /// <summary>
    /// Maps UI intents to ClientEnvelope commands via BridgeSessionClient.
    /// Client sequence comes only from the shared ClientEnvelopeFactory.
    /// </summary>
    public sealed class PlayerIntentRouter : IPlayerIntentSink
    {
        private readonly ISessionViewSource _session;
        private readonly ICommandGate _gate;
        private readonly IDialogueSelection _selection;
        private readonly BridgeSessionClient _bridge;
        private readonly ClientEnvelopeFactory _factory;
        private readonly MonoBehaviour _runner;
        private readonly string _worldId;

        public PlayerIntentRouter(
            ISessionViewSource session,
            ICommandGate gate,
            IDialogueSelection selection,
            BridgeSessionClient bridge,
            ClientEnvelopeFactory factory,
            MonoBehaviour runner,
            string worldId = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _selection = selection ?? throw new ArgumentNullException(nameof(selection));
            _bridge = bridge;
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _runner = runner;
            _worldId = worldId;
        }

        public bool TrySelectDialogueTarget(DialogueTarget target)
        {
            _selection.Select(target);
            return true;
        }

        public bool TrySendDialogueText(string text, string interactionKind = ClientEnvelopeFactory.DefaultInteractionKind)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (!EnsureCanMutate(out var view))
            {
                return false;
            }

            // Same effective target as DialogueFeaturePanel display: selection ?? focused non-player.
            if (!DialogueTargetResolver.TryResolveEffective(view, _selection.Current, out var target))
            {
                Debug.LogWarning("[Intent] no dialogue target");
                return false;
            }

            if (view.event_budget != null && view.event_budget.remaining <= 0)
            {
                Debug.LogWarning("[Intent] dialogue blocked: event_budget.remaining=0; use player_day.end");
                return false;
            }

            var commandId = ClientEnvelopeFactory.NewCommandId();
            var kind = string.IsNullOrWhiteSpace(interactionKind)
                ? ClientEnvelopeFactory.DefaultInteractionKind
                : interactionKind;

            string envelope;
            var dialogue = FindMatchingDialogue(view, target);
            if (dialogue != null)
            {
                envelope = _factory.CreateDialogueContinue(
                    view.session_id, commandId, view.basis_token, dialogue.dialogue_id, text.Trim(), kind);
            }
            else
            {
                if (target.kind != DialogueParticipantKind.System && string.IsNullOrEmpty(_worldId))
                {
                    Debug.LogWarning("[Intent] dialogue.start needs world_id from session bootstrap");
                    return false;
                }

                var recipient = target.kind == DialogueParticipantKind.System
                    ? "system"
                    : target.entityId;
                envelope = _factory.CreateDialogueStart(
                    view.session_id, commandId, view.basis_token, _worldId, recipient, text.Trim(), kind);
            }

            return Dispatch(commandId, envelope);
        }

        public bool TrySendDialogueText(string text) =>
            TrySendDialogueText(text, ClientEnvelopeFactory.DefaultInteractionKind);

        public bool TryCloseActiveDialogue()
        {
            if (!EnsureCanMutate(out var view))
            {
                return false;
            }

            var dialogue = FindActiveDialogue(view);
            if (dialogue == null)
            {
                return false;
            }

            var commandId = ClientEnvelopeFactory.NewCommandId();
            var envelope = _factory.CreateDialogueClose(
                view.session_id, commandId, view.basis_token, dialogue.dialogue_id);
            return Dispatch(commandId, envelope);
        }

        public bool TryTriggerEventCard(string eventCardId)
        {
            if (string.IsNullOrEmpty(eventCardId) || !EnsureCanMutate(out var view))
            {
                return false;
            }

            if (!HasAvailableCard(view, eventCardId))
            {
                Debug.LogWarning($"[Intent] card not available: {eventCardId}");
                return false;
            }

            var commandId = ClientEnvelopeFactory.NewCommandId();
            var envelope = _factory.CreateEventCardTrigger(
                view.session_id, commandId, view.basis_token, eventCardId);
            return Dispatch(commandId, envelope);
        }

        public bool TryTriggerAllAvailableEventCards()
        {
            if (!EnsureCanMutate(out var view) || view.event_cards == null)
            {
                return false;
            }

            // Single-flight gate: only start the first available card; rest after completion.
            for (var i = 0; i < view.event_cards.Count; i++)
            {
                var card = view.event_cards[i];
                if (card != null && card.IsAvailable)
                {
                    return TryTriggerEventCard(card.event_card_id);
                }
            }

            return false;
        }

        public bool TryMapMove(string destinationEntityId)
        {
            if (string.IsNullOrEmpty(destinationEntityId) || !EnsureCanMutate(out var view))
            {
                return false;
            }

            if (string.IsNullOrEmpty(_worldId))
            {
                Debug.LogWarning("[Intent] map.move needs world_id from session bootstrap");
                return false;
            }

            var commandId = ClientEnvelopeFactory.NewCommandId();
            var envelope = _factory.CreateMapMove(
                view.session_id, commandId, view.basis_token, _worldId, destinationEntityId);
            return Dispatch(commandId, envelope);
        }

        public bool TryEndPlayerDay()
        {
            if (!EnsureCanMutate(out var view))
            {
                return false;
            }

            if (view.day_cycle == null || view.day_cycle.PhaseEnum != DayPhase.Player)
            {
                Debug.LogWarning("[Intent] player_day.end rejected: not player phase");
                return false;
            }

            var commandId = ClientEnvelopeFactory.NewCommandId();
            var envelope = _factory.CreatePlayerDayEnd(
                view.session_id, commandId, view.basis_token);
            return Dispatch(commandId, envelope);
        }

        public bool TrySubmitStageOutcome(
            string stageInstanceId,
            int stageRevision,
            string outcomeType,
            JObject outcome = null)
        {
            if (string.IsNullOrEmpty(stageInstanceId) ||
                string.IsNullOrEmpty(outcomeType) ||
                !EnsureCanMutate(out var view))
            {
                return false;
            }

            var outcomeObj = outcome ?? new JObject();
            var digest = BridgeSessionClient.EvidenceDigestForOutcome(
                stageInstanceId, stageRevision, outcomeType, outcomeObj);
            var commandId = ClientEnvelopeFactory.NewCommandId();
            var envelope = _factory.CreateStageOutcomeProposal(
                view.session_id,
                commandId,
                view.basis_token,
                stageInstanceId,
                stageRevision,
                outcomeType,
                outcomeObj,
                digest);
            return Dispatch(commandId, envelope);
        }

        public bool TryOpenMap()
        {
            // Local UI only — MapDestinationPanel listens via MainWorldScreen.
            return true;
        }

        private bool Dispatch(string commandId, string envelopeJson)
        {
            if (_bridge == null || _runner == null)
            {
                Debug.LogWarning("[Intent] bridge not wired; envelope prepared only");
                Debug.Log(envelopeJson);
                return false;
            }

            _runner.StartCoroutine(DispatchCoroutine(commandId, envelopeJson));
            return true;
        }

        private System.Collections.IEnumerator DispatchCoroutine(string commandId, string envelopeJson)
        {
            var task = _bridge.SendMutatingAsync(commandId, envelopeJson);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                Debug.LogError(task.Exception);
            }
        }

        private bool EnsureCanMutate(out SessionViewDto view)
        {
            view = _session.CurrentView;
            if (view == null)
            {
                Debug.LogWarning("[Intent] no SessionView");
                return false;
            }

            if (_gate.HasPending)
            {
                Debug.LogWarning("[Intent] command already pending");
                return false;
            }

            return true;
        }

        private static bool HasAvailableCard(SessionViewDto view, string eventCardId)
        {
            if (view.event_cards == null)
            {
                return false;
            }

            for (var i = 0; i < view.event_cards.Count; i++)
            {
                var c = view.event_cards[i];
                if (c != null && c.event_card_id == eventCardId && c.IsAvailable)
                {
                    return true;
                }
            }

            return false;
        }

        private static DialogueViewDto FindActiveDialogue(SessionViewDto view)
        {
            if (view.dialogues == null)
            {
                return null;
            }

            for (var i = 0; i < view.dialogues.Count; i++)
            {
                if (view.dialogues[i] != null && view.dialogues[i].IsActive)
                {
                    return view.dialogues[i];
                }
            }

            return null;
        }

        private static DialogueViewDto FindMatchingDialogue(SessionViewDto view, DialogueTarget target)
        {
            if (view.dialogues == null)
            {
                return null;
            }

            // Engine closes prior-day threads (reason_code day_ended). Host continues only
            // status===active dialogues — never compare dialogue.day vs day_cycle.day.
            for (var i = 0; i < view.dialogues.Count; i++)
            {
                var d = view.dialogues[i];
                if (d == null || !d.IsActive || d.participants == null)
                {
                    continue;
                }

                for (var p = 0; p < d.participants.Count; p++)
                {
                    var part = d.participants[p];
                    if (target.kind == DialogueParticipantKind.System &&
                        part.KindEnum == DialogueParticipantKind.System)
                    {
                        return d;
                    }

                    if (target.kind == DialogueParticipantKind.Entity &&
                        part.KindEnum == DialogueParticipantKind.Entity &&
                        part.entity_id == target.entityId)
                    {
                        return d;
                    }
                }
            }

            return null;
        }
    }
}
