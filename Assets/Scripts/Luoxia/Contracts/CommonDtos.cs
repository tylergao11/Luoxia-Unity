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

        /// <summary>
        /// RFC 4647 Lookup against Host preferred locale: exact, then truncate
        /// language-range subtags, then prefix match on available keys.
        /// If no match and the map has exactly one key, use that key.
        /// Otherwise return the missing-localization placeholder — never dictionary order.
        /// </summary>
        public string Resolve(string preferredLocale = null, string fallback = null)
        {
            var miss = fallback ?? HostDisplayLocale.MissingPlaceholder;
            if (Raw == null || Raw.Count == 0)
            {
                return miss;
            }

            var preferred = string.IsNullOrWhiteSpace(preferredLocale)
                ? HostDisplayLocale.Preferred
                : preferredLocale.Trim();

            if (!string.IsNullOrEmpty(preferred))
            {
                if (TryGetString(preferred, out var exact))
                {
                    return exact;
                }

                // RFC 4647 Lookup: progressively truncate the language range.
                var range = preferred;
                while (TryTruncateLanguageRange(range, out range))
                {
                    if (TryGetString(range, out var truncated))
                    {
                        return truncated;
                    }
                }

                // Prefix: available tag starts with preferred + "-" (e.g. zh → zh-CN).
                foreach (var pair in Raw)
                {
                    if (string.IsNullOrEmpty(pair.Key) || pair.Value == null)
                    {
                        continue;
                    }

                    if (pair.Key.StartsWith(preferred + "-", StringComparison.OrdinalIgnoreCase)
                        && TryTokenToString(pair.Value, out var prefixed))
                    {
                        return prefixed;
                    }
                }
            }

            // Exactly one locale key → use it; never guess among many by dictionary order.
            if (Raw.Count == 1)
            {
                foreach (var pair in Raw)
                {
                    if (pair.Value != null && TryTokenToString(pair.Value, out var only))
                    {
                        return only;
                    }
                }
            }

            return miss;
        }

        private bool TryGetString(string locale, out string value)
        {
            value = null;
            if (Raw == null || string.IsNullOrEmpty(locale))
            {
                return false;
            }

            foreach (var pair in Raw)
            {
                if (pair.Key == null || pair.Value == null)
                {
                    continue;
                }

                if (!string.Equals(pair.Key, locale, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return TryTokenToString(pair.Value, out value);
            }

            return false;
        }

        private static bool TryTruncateLanguageRange(string range, out string truncated)
        {
            truncated = null;
            if (string.IsNullOrEmpty(range))
            {
                return false;
            }

            var dash = range.LastIndexOf('-');
            if (dash <= 0)
            {
                return false;
            }

            truncated = range.Substring(0, dash);
            return truncated.Length > 0;
        }

        private static bool TryTokenToString(JToken token, out string value)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                value = null;
                return false;
            }

            value = token.Type == JTokenType.String
                ? token.Value<string>() ?? string.Empty
                : token.ToString();
            return true;
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

    /// <summary>
    /// Lore kinds projected into SessionView.lore. Unknown kinds are ignored by UI.
    /// </summary>
    public enum LoreKind
    {
        Unknown,
        Profile,
        Hearsay,
        Arrival,
        Nightfall
    }
}
