using System;
using Luoxia.Contracts;
using Newtonsoft.Json.Linq;

namespace Luoxia.Net
{
    /// <summary>
    /// Builds contract-valid ClientEnvelope payloads. Owns the sole client sequence counter
    /// shared by BridgeSessionClient and PlayerIntentRouter.
    /// </summary>
    public sealed class ClientEnvelopeFactory : IClientEnvelopeFactory
    {
        private const string Protocol = "client-bridge.v1";
        public const string DefaultInteractionKind = "dialogue";

        private int _nextSequence;

        public ClientEnvelopeFactory(int startingSequence = 0)
        {
            _nextSequence = Math.Max(0, startingSequence);
        }

        public int PeekNextSequence => _nextSequence;

        public void SetNextSequence(int sequence) => _nextSequence = Math.Max(0, sequence);

        /// <summary>Single source of client sequence for Bridge + IntentRouter.</summary>
        public int AllocateSequence() => _nextSequence++;

        public string CreateReady(string sessionId, string clientBuildDigest) =>
            Build(sessionId, AllocateSequence(), null, new JObject
            {
                ["type"] = "client.ready",
                ["client_build_digest"] = clientBuildDigest,
                ["supported_protocols"] = new JArray { Protocol }
            });

        public string CreateResync(string sessionId, string basisToken) =>
            Build(sessionId, AllocateSequence(), null, new JObject
            {
                ["type"] = "session.resync_request",
                ["basis_token"] = basisToken
            });

        public string CreateDialogueStart(
            string sessionId,
            string commandId,
            string basisToken,
            string worldId,
            string recipientEntityIdOrSystem,
            string text,
            string interactionKind = DefaultInteractionKind)
        {
            JObject recipient;
            if (string.IsNullOrEmpty(recipientEntityIdOrSystem) ||
                string.Equals(recipientEntityIdOrSystem, "system", StringComparison.OrdinalIgnoreCase))
            {
                recipient = new JObject { ["participant_kind"] = "system" };
            }
            else
            {
                if (string.IsNullOrEmpty(worldId))
                {
                    throw new ArgumentException("worldId required for entity recipient", nameof(worldId));
                }

                // DialogueParticipantRef entity branch: { participant_kind, entity: EntityRef }
                recipient = new JObject
                {
                    ["participant_kind"] = "entity",
                    ["entity"] = new JObject
                    {
                        ["world_id"] = worldId,
                        ["entity_id"] = recipientEntityIdOrSystem
                    }
                };
            }

            return Build(sessionId, AllocateSequence(), null, new JObject
            {
                ["type"] = "dialogue.start",
                ["command_id"] = commandId,
                ["basis_token"] = basisToken,
                ["recipient"] = recipient,
                ["interaction_kind"] = NormalizeInteractionKind(interactionKind),
                ["locale"] = RequireHostLocale(),
                ["text"] = text
            });
        }

        public string CreateDialogueContinue(
            string sessionId,
            string commandId,
            string basisToken,
            string dialogueId,
            string text,
            string interactionKind = DefaultInteractionKind)
        {
            return Build(sessionId, AllocateSequence(), null, new JObject
            {
                ["type"] = "dialogue.continue",
                ["command_id"] = commandId,
                ["basis_token"] = basisToken,
                ["dialogue_id"] = dialogueId,
                ["interaction_kind"] = NormalizeInteractionKind(interactionKind),
                ["locale"] = RequireHostLocale(),
                ["text"] = text
            });
        }

        public string CreateDialogueClose(
            string sessionId,
            string commandId,
            string basisToken,
            string dialogueId)
        {
            return Build(sessionId, AllocateSequence(), null, new JObject
            {
                ["type"] = "dialogue.close",
                ["command_id"] = commandId,
                ["basis_token"] = basisToken,
                ["dialogue_id"] = dialogueId
            });
        }

        public string CreateMapMove(
            string sessionId,
            string commandId,
            string basisToken,
            string worldId,
            string destinationEntityId)
        {
            return Build(sessionId, AllocateSequence(), null, new JObject
            {
                ["type"] = "map.move",
                ["command_id"] = commandId,
                ["basis_token"] = basisToken,
                ["destination"] = new JObject
                {
                    ["world_id"] = worldId,
                    ["entity_id"] = destinationEntityId
                }
            });
        }

        public string CreateEventCardTrigger(
            string sessionId,
            string commandId,
            string basisToken,
            string eventCardId)
        {
            return Build(sessionId, AllocateSequence(), null, new JObject
            {
                ["type"] = "event_card.trigger",
                ["command_id"] = commandId,
                ["basis_token"] = basisToken,
                ["event_card_id"] = eventCardId
            });
        }

        public string CreatePlayerDayEnd(
            string sessionId,
            string commandId,
            string basisToken)
        {
            return Build(sessionId, AllocateSequence(), null, new JObject
            {
                ["type"] = "player_day.end",
                ["command_id"] = commandId,
                ["basis_token"] = basisToken
            });
        }

        public string CreateStageOutcomeProposal(
            string sessionId,
            string commandId,
            string basisToken,
            string stageInstanceId,
            int stageRevision,
            string outcomeType,
            JObject outcome,
            string evidenceDigest)
        {
            if (string.IsNullOrEmpty(outcomeType))
            {
                throw new ArgumentException("outcomeType required", nameof(outcomeType));
            }

            if (string.IsNullOrEmpty(evidenceDigest) || evidenceDigest.Length != 64)
            {
                throw new ArgumentException("evidenceDigest must be 64-char sha256 hex", nameof(evidenceDigest));
            }

            return Build(sessionId, AllocateSequence(), null, new JObject
            {
                ["type"] = "stage.outcome_proposal",
                ["command_id"] = commandId,
                ["basis_token"] = basisToken,
                ["stage_instance_id"] = stageInstanceId,
                ["stage_revision"] = stageRevision,
                ["outcome_type"] = outcomeType,
                ["outcome"] = outcome ?? new JObject(),
                ["evidence_digest"] = evidenceDigest
            });
        }

        private static string RequireHostLocale()
        {
            var locale = HostDisplayLocale.Preferred;
            if (string.IsNullOrWhiteSpace(locale))
            {
                throw new InvalidOperationException(
                    "HostDisplayLocale must be set from Bootstrap/provision before dialogue commands");
            }

            return locale;
        }

        private static string NormalizeInteractionKind(string interactionKind)
        {
            if (string.IsNullOrWhiteSpace(interactionKind))
            {
                return DefaultInteractionKind;
            }

            switch (interactionKind)
            {
                case "dialogue":
                case "goal_plan":
                case "definition_draft":
                    return interactionKind;
                default:
                    throw new ArgumentException(
                        $"interaction_kind must be dialogue|goal_plan|definition_draft, got '{interactionKind}'",
                        nameof(interactionKind));
            }
        }

        private static string Build(string sessionId, int sequence, string correlationId, JObject message)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                throw new ArgumentException("sessionId required", nameof(sessionId));
            }

            var envelope = new JObject
            {
                ["protocol_version"] = Protocol,
                ["envelope_type"] = "client",
                ["message_id"] = Guid.NewGuid().ToString(),
                ["session_id"] = sessionId,
                ["sequence"] = sequence,
                ["message"] = message
            };

            if (!string.IsNullOrEmpty(correlationId))
            {
                envelope["correlation_id"] = correlationId;
            }

            return envelope.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string NewCommandId() => Guid.NewGuid().ToString();

        public static string PlaceholderBuildDigest()
        {
            // 64-char hex placeholder until CI injects real client build digest.
            return new string('a', 64);
        }
    }
}
