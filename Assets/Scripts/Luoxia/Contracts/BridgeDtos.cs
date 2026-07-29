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
        [JsonProperty("ok")] public bool ok;
        [JsonProperty("code")] public string code;
        [JsonProperty("message")] public string message;
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
    }
}
