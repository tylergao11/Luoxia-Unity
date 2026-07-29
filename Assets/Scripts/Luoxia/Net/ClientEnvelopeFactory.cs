using System;
using Newtonsoft.Json.Linq;

namespace Luoxia.Net
{
    public sealed class ClientEnvelopeFactory : IClientEnvelopeFactory
    {
        private const string Protocol = "client-bridge.v1";
        private int _nextSequence;

        public ClientEnvelopeFactory(int startingSequence = 0)
        {
            _nextSequence = Math.Max(0, startingSequence);
        }

        public int PeekNextSequence => _nextSequence;

        public void SetNextSequence(int sequence) => _nextSequence = Math.Max(0, sequence);

        public string CreateReady(string sessionId, int sequence, string clientBuildDigest)
        {
            return Build(sessionId, sequence, null, new JObject
            {
                ["type"] = "client.ready",
                ["client_build_digest"] = clientBuildDigest,
                ["supported_protocols"] = new JArray { Protocol }
            });
        }

        public string CreateResync(string sessionId, int sequence, string basisToken)
        {
            return Build(sessionId, sequence, null, new JObject
            {
                ["type"] = "session.resync_request",
                ["basis_token"] = basisToken
            });
        }

        public string CreateDialogueStart(
            string sessionId,
            int sequence,
            string commandId,
            string basisToken,
            string recipientEntityIdOrSystem,
            string text)
        {
            JObject recipient;
            if (string.IsNullOrEmpty(recipientEntityIdOrSystem) ||
                string.Equals(recipientEntityIdOrSystem, "system", StringComparison.OrdinalIgnoreCase))
            {
                recipient = new JObject { ["participant_kind"] = "system" };
            }
            else
            {
                recipient = new JObject
                {
                    ["participant_kind"] = "entity",
                    ["entity_id"] = recipientEntityIdOrSystem
                };
            }

            return Build(sessionId, sequence, null, new JObject
            {
                ["type"] = "dialogue.start",
                ["command_id"] = commandId,
                ["basis_token"] = basisToken,
                ["recipient"] = recipient,
                ["locale"] = "zh-CN",
                ["text"] = text
            });
        }

        public string CreateDialogueContinue(
            string sessionId,
            int sequence,
            string commandId,
            string basisToken,
            string dialogueId,
            string text)
        {
            return Build(sessionId, sequence, null, new JObject
            {
                ["type"] = "dialogue.continue",
                ["command_id"] = commandId,
                ["basis_token"] = basisToken,
                ["dialogue_id"] = dialogueId,
                ["locale"] = "zh-CN",
                ["text"] = text
            });
        }

        public string CreateDialogueClose(
            string sessionId,
            int sequence,
            string commandId,
            string basisToken,
            string dialogueId)
        {
            return Build(sessionId, sequence, null, new JObject
            {
                ["type"] = "dialogue.close",
                ["command_id"] = commandId,
                ["basis_token"] = basisToken,
                ["dialogue_id"] = dialogueId
            });
        }

        public string CreateMapMove(
            string sessionId,
            int sequence,
            string commandId,
            string basisToken,
            string worldId,
            string destinationEntityId)
        {
            return Build(sessionId, sequence, null, new JObject
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
            int sequence,
            string commandId,
            string basisToken,
            string eventCardId)
        {
            return Build(sessionId, sequence, null, new JObject
            {
                ["type"] = "event_card.trigger",
                ["command_id"] = commandId,
                ["basis_token"] = basisToken,
                ["event_card_id"] = eventCardId
            });
        }

        public string CreatePlayerDayEnd(
            string sessionId,
            int sequence,
            string commandId,
            string basisToken)
        {
            return Build(sessionId, sequence, null, new JObject
            {
                ["type"] = "player_day.end",
                ["command_id"] = commandId,
                ["basis_token"] = basisToken
            });
        }

        /// <summary>Allocate next client sequence and build envelope (preferred for real sends).</summary>
        public string CreateReadyAuto(string sessionId, string clientBuildDigest) =>
            CreateReady(sessionId, AllocateSequence(), clientBuildDigest);

        public string CreateResyncAuto(string sessionId, string basisToken) =>
            CreateResync(sessionId, AllocateSequence(), basisToken);

        private int AllocateSequence() => _nextSequence++;

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
