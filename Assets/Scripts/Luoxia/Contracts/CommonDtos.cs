using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Luoxia.Contracts
{
    /// <summary>
    /// Client-side contract shapes. Field names match Engine JSON (snake_case).
    /// Engine owns truth; these are deserialize targets only.
    /// </summary>
    [Serializable]
    public sealed class LocalizedTextDto
    {
        /// <summary>Raw locale map from JSON, e.g. { "zh-CN": "..." }.</summary>
        [JsonExtensionData]
        public IDictionary<string, JToken> Raw { get; set; }

        public string Resolve(string preferredLocale = "zh-CN", string fallback = "")
        {
            if (Raw == null || Raw.Count == 0)
            {
                return fallback;
            }

            if (Raw.TryGetValue(preferredLocale, out var preferred) && preferred != null)
            {
                return preferred.Type == JTokenType.String ? preferred.Value<string>() : preferred.ToString();
            }

            foreach (var pair in Raw)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                return pair.Value.Type == JTokenType.String
                    ? pair.Value.Value<string>()
                    : pair.Value.ToString();
            }

            return fallback;
        }

        public static LocalizedTextDto FromZh(string text)
        {
            return new LocalizedTextDto
            {
                Raw = new Dictionary<string, JToken>
                {
                    ["zh-CN"] = text ?? string.Empty
                }
            };
        }
    }

    [Serializable]
    public sealed class AssetContentRefDto
    {
        [JsonProperty("content_hash")] public string content_hash;
        [JsonProperty("media_type")] public string media_type;
    }

    [Serializable]
    public sealed class LogicalTimeDto
    {
        [JsonProperty("clock_id")] public string clock_id;
        [JsonProperty("tick")] public long tick;
        [JsonProperty("calendar_label")] public string calendar_label;
    }

    [Serializable]
    public sealed class EventCostDto
    {
        [JsonProperty("amount")] public int amount;
    }

    public enum DayPhase
    {
        Autonomous,
        DirectorSettlement,
        Player
    }

    public enum EventCardStatus
    {
        Available,
        Triggered,
        Expired,
        Invalidated
    }

    public enum DialogueStatus
    {
        Active,
        Closed
    }

    public enum RenderNodeKind
    {
        Scene,
        Portrait,
        Cg,
        Overlay,
        Text,
        InteractionAnchor
    }

    public enum DialogueParticipantKind
    {
        Entity,
        System,
        Human
    }
}
