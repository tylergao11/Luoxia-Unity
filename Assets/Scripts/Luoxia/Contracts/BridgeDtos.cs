using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Luoxia.Contracts
{
    /// <summary>
    /// Client/Server Envelope shells from client-bridge.v1.
    /// </summary>
    public sealed class ServerEnvelopeDto
    {
        [JsonProperty("protocol_version")] public string protocol_version;
        [JsonProperty("envelope_type")] public string envelope_type;
        [JsonProperty("message_id")] public string message_id;
        [JsonProperty("session_id")] public string session_id;
        [JsonProperty("sequence")] public int sequence;
        [JsonProperty("correlation_id")] public string correlation_id;
        [JsonProperty("message")] public JObject message;
    }

    public sealed class CommandResultDto
    {
        [JsonProperty("type")] public string type;
        [JsonProperty("command_id")] public string command_id;
        [JsonProperty("status")] public string status;
        [JsonProperty("view_revision")] public int view_revision;
        [JsonProperty("code")] public string code;
        [JsonProperty("message")] public JToken message;

        public bool IsAccepted => status == "accepted";
        public bool IsRejected => status == "rejected";

        public string ResolveMessage(string preferredLocale = null)
        {
            if (message == null || message.Type == JTokenType.Null)
            {
                return HostDisplayLocale.MissingPlaceholder;
            }

            if (message.Type == JTokenType.String)
            {
                return message.Value<string>() ?? string.Empty;
            }

            if (message.Type == JTokenType.Object)
            {
                var localized = message.ToObject<LocalizedTextDto>();
                return localized != null
                    ? localized.Resolve(preferredLocale)
                    : HostDisplayLocale.MissingPlaceholder;
            }

            return message.ToString(Formatting.None);
        }
    }

    public static class BridgeJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            DateParseHandling = DateParseHandling.None
        };

        public static T Deserialize<T>(string json) =>
            JsonConvert.DeserializeObject<T>(json, Settings);

        public static string Serialize(object value) =>
            JsonConvert.SerializeObject(value, Settings);

        public static List<ServerEnvelopeDto> DeserializeServerBatch(string json)
        {
            var token = JToken.Parse(json);
            if (token.Type == JTokenType.Array)
            {
                return token.ToObject<List<ServerEnvelopeDto>>(JsonSerializer.Create(Settings))
                       ?? new List<ServerEnvelopeDto>();
            }

            // Single envelope fallback
            var one = token.ToObject<ServerEnvelopeDto>(JsonSerializer.Create(Settings));
            return one != null
                ? new List<ServerEnvelopeDto> { one }
                : new List<ServerEnvelopeDto>();
        }

        public static string MessageType(ServerEnvelopeDto envelope)
        {
            return envelope?.message?["type"]?.ToString();
        }

        public static SessionViewDto TryExtractSessionView(ServerEnvelopeDto envelope)
        {
            if (envelope?.message == null)
            {
                return null;
            }

            if (envelope.message["type"]?.ToString() != "session.view")
            {
                return null;
            }

            var viewToken = envelope.message["view"];
            return viewToken == null
                ? null
                : viewToken.ToObject<SessionViewDto>(JsonSerializer.Create(Settings));
        }

        public static CommandResultDto TryExtractCommandResult(ServerEnvelopeDto envelope)
        {
            if (envelope?.message == null)
            {
                return null;
            }

            if (envelope.message["type"]?.ToString() != "command.result")
            {
                return null;
            }

            return envelope.message.ToObject<CommandResultDto>(JsonSerializer.Create(Settings));
        }

        public static PresentationFrameDto TryExtractPresentationFrame(ServerEnvelopeDto envelope)
        {
            return TryExtractMessage<PresentationFrameDto>(envelope, "presentation.frame");
        }

        public static StageOpenDto TryExtractStageOpen(ServerEnvelopeDto envelope)
        {
            return TryExtractMessage<StageOpenDto>(envelope, "stage.open");
        }

        public static StageUpdateDto TryExtractStageUpdate(ServerEnvelopeDto envelope)
        {
            return TryExtractMessage<StageUpdateDto>(envelope, "stage.update");
        }

        public static StageCloseDto TryExtractStageClose(ServerEnvelopeDto envelope)
        {
            return TryExtractMessage<StageCloseDto>(envelope, "stage.close");
        }

        public static DialogueReplyDto TryExtractDialogueReply(ServerEnvelopeDto envelope)
        {
            return TryExtractMessage<DialogueReplyDto>(envelope, "dialogue.reply");
        }

        public static ProtocolErrorDto TryExtractProtocolError(ServerEnvelopeDto envelope)
        {
            return TryExtractMessage<ProtocolErrorDto>(envelope, "protocol.error");
        }

        private static T TryExtractMessage<T>(ServerEnvelopeDto envelope, string expectedType)
            where T : class
        {
            if (envelope?.message == null)
            {
                return null;
            }

            if (envelope.message["type"]?.ToString() != expectedType)
            {
                return null;
            }

            return envelope.message.ToObject<T>(JsonSerializer.Create(Settings));
        }
    }
}
