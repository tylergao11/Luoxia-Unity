using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Luoxia.Contracts
{
    [Serializable]
    public sealed class SessionViewDto
    {
        [JsonProperty("contract_version")] public string contract_version;
        [JsonProperty("record_type")] public string record_type;
        [JsonProperty("session_id")] public string session_id;
        [JsonProperty("view_revision")] public int view_revision;
        [JsonProperty("basis_token")] public string basis_token;
        [JsonProperty("player_entity_id")] public string player_entity_id;
        /// <summary>
        /// Runtime UUID of the player's current location entity.
        /// Optional until Engine SessionViewProjector ships the field; null-safe UI.
        /// </summary>
        [JsonProperty("player_location_entity_id")] public string player_location_entity_id;
        [JsonProperty("world_time")] public LogicalTimeDto world_time;
        [JsonProperty("render_nodes")] public List<RenderNodeDto> render_nodes = new List<RenderNodeDto>();
        /// <summary>
        /// Visible lore chapters (profile / hearsay / arrival / nightfall).
        /// Optional until Engine projection exists; empty list = no lore UI entry.
        /// </summary>
        [JsonProperty("lore")] public List<LoreViewDto> lore = new List<LoreViewDto>();
        [JsonProperty("goal_plans")] public List<GoalPlanViewDto> goal_plans = new List<GoalPlanViewDto>();
        [JsonProperty("notices")] public List<NoticeDto> notices = new List<NoticeDto>();
        [JsonProperty("day_cycle")] public DayCycleStateDto day_cycle;
        [JsonProperty("event_budget")] public EventBudgetViewDto event_budget;
        [JsonProperty("event_cards")] public List<EventCardViewDto> event_cards = new List<EventCardViewDto>();
        [JsonProperty("dialogues")] public List<DialogueViewDto> dialogues = new List<DialogueViewDto>();
    }

    /// <summary>
    /// Client-forward Lore projection. Engine schema pending; fields are empty-safe.
    /// </summary>
    [Serializable]
    public sealed class LoreViewDto
    {
        [JsonProperty("lore_id")] public string lore_id;
        [JsonProperty("lore_kind")] public string lore_kind;
        /// <summary>Engine SubjectRef; prefer <see cref="subject_entity_id"/>.</summary>
        [JsonProperty("subject")] public JToken subject;
        [JsonProperty("title")] public LocalizedTextDto title;
        [JsonProperty("body")] public LocalizedTextDto body;
        [JsonProperty("ordinal")] public int ordinal;

        public string subject_entity_id
        {
            get
            {
                if (subject == null || subject.Type != JTokenType.Object)
                {
                    return null;
                }

                // SubjectRef entity: { kind:"entity", entity:{ world_id, entity_id } }
                var nested = subject["entity"];
                if (nested != null && nested.Type == JTokenType.Object)
                {
                    return nested["entity_id"]?.ToString();
                }

                return subject["entity_id"]?.ToString();
            }
        }

        public LoreKind KindEnum => lore_kind switch
        {
            "profile" => LoreKind.Profile,
            "hearsay" => LoreKind.Hearsay,
            "arrival" => LoreKind.Arrival,
            "nightfall" => LoreKind.Nightfall,
            "opening" => LoreKind.Arrival,
            _ => LoreKind.Unknown
        };

        public string ResolveTitle(string preferredLocale = null) =>
            title != null ? title.Resolve(preferredLocale) : HostDisplayLocale.MissingPlaceholder;

        public string ResolveBody(string preferredLocale = null) =>
            body != null ? body.Resolve(preferredLocale) : HostDisplayLocale.MissingPlaceholder;
    }

    [Serializable]
    public sealed class DayCycleStateDto
    {
        [JsonProperty("day")] public int day;
        [JsonProperty("phase")] public string phase;
        [JsonProperty("phase_revision")] public int phase_revision;

        public DayPhase PhaseEnum => phase switch
        {
            "player" => DayPhase.Player,
            "director_settlement" => DayPhase.DirectorSettlement,
            _ => DayPhase.Autonomous
        };
    }

    [Serializable]
    public sealed class EventBudgetViewDto
    {
        [JsonProperty("day")] public int day;
        [JsonProperty("capacity")] public int capacity;
        [JsonProperty("spent")] public int spent;
        [JsonProperty("remaining")] public int remaining;
    }

    [Serializable]
    public sealed class EventCardViewDto
    {
        [JsonProperty("event_card_id")] public string event_card_id;
        [JsonProperty("day")] public int day;
        [JsonProperty("title")] public LocalizedTextDto title;
        [JsonProperty("summary")] public LocalizedTextDto summary;
        [JsonProperty("event_cost")] public EventCostDto event_cost;
        [JsonProperty("status")] public string status;

        public int CostAmount => event_cost != null ? event_cost.amount : 0;

        public EventCardStatus StatusEnum => status switch
        {
            "triggered" => EventCardStatus.Triggered,
            "expired" => EventCardStatus.Expired,
            "invalidated" => EventCardStatus.Invalidated,
            _ => EventCardStatus.Available
        };

        public bool IsAvailable => StatusEnum == EventCardStatus.Available;
    }

    [Serializable]
    public sealed class DialogueViewDto
    {
        [JsonProperty("dialogue_id")] public string dialogue_id;
        [JsonProperty("day")] public int day;
        [JsonProperty("participants")] public List<DialogueParticipantRefDto> participants = new List<DialogueParticipantRefDto>();
        [JsonProperty("turns")] public List<DialogueTurnViewDto> turns = new List<DialogueTurnViewDto>();
        [JsonProperty("status")] public string status;

        /// <summary>
        /// Only status===active is continue-eligible. Unknown / closed / other → not active.
        /// Host never compares dialogue.day vs day_cycle.day.
        /// </summary>
        public DialogueStatus StatusEnum =>
            status == "active" ? DialogueStatus.Active : DialogueStatus.Closed;

        public bool IsActive => StatusEnum == DialogueStatus.Active;
    }

    [Serializable]
    public sealed class DialogueParticipantRefDto
    {
        [JsonProperty("participant_kind")] public string participant_kind;
        /// <summary>Engine EntityRef when participant_kind=entity.</summary>
        [JsonProperty("entity")] public JToken entity;

        [JsonIgnore]
        public string entity_id
        {
            get
            {
                if (entity == null || entity.Type != JTokenType.Object)
                {
                    return null;
                }

                return entity["entity_id"]?.ToString();
            }
        }

        public DialogueParticipantKind KindEnum => participant_kind switch
        {
            "system" => DialogueParticipantKind.System,
            "human" => DialogueParticipantKind.Human,
            _ => DialogueParticipantKind.Entity
        };
    }

    [Serializable]
    public sealed class DialogueTurnViewDto
    {
        [JsonProperty("turn_id")] public string turn_id;
        [JsonProperty("speaker")] public DialogueParticipantRefDto speaker;
        [JsonProperty("locale")] public string locale;
        [JsonProperty("text")] public string text;
        [JsonProperty("emotion_id")] public string emotion_id;
        [JsonProperty("occurred_at")] public LogicalTimeDto occurred_at;
    }

    [Serializable]
    public sealed class RenderNodeDto
    {
        [JsonProperty("node_id")] public string node_id;
        [JsonProperty("node_kind")] public string node_kind;
        [JsonProperty("slot_id")] public string slot_id;
        [JsonProperty("subject")] public JToken subject;
        [JsonProperty("asset")] public AssetContentRefDto asset;
        [JsonProperty("text")] public LocalizedTextDto text;
        [JsonProperty("parameters")] public JObject parameters;

        public string subject_entity_id
        {
            get
            {
                if (subject == null || subject.Type != JTokenType.Object)
                {
                    return null;
                }

                // Engine EntityRef: { kind:"entity", entity:{ world_id, entity_id, ... } }
                var nested = subject["entity"];
                if (nested != null && nested.Type == JTokenType.Object)
                {
                    var nestedId = nested["entity_id"]?.ToString();
                    if (!string.IsNullOrEmpty(nestedId))
                    {
                        return nestedId;
                    }
                }

                // Flat mock / legacy: { entity_id }
                return subject["entity_id"]?.ToString();
            }
        }

        public string parameters_json => parameters != null ? parameters.ToString(Formatting.None) : null;

        public RenderNodeKind KindEnum => node_kind switch
        {
            "portrait" => RenderNodeKind.Portrait,
            "cg" => RenderNodeKind.Cg,
            "overlay" => RenderNodeKind.Overlay,
            "text" => RenderNodeKind.Text,
            "interaction_anchor" => RenderNodeKind.InteractionAnchor,
            _ => RenderNodeKind.Scene
        };
    }

    [Serializable]
    public sealed class GoalPlanViewDto
    {
        [JsonProperty("plan_id")] public string plan_id;
        [JsonProperty("goal")] public LocalizedTextDto goal;
        [JsonProperty("status")] public string status;
        [JsonProperty("current_steps")] public List<LocalizedTextDto> current_steps = new List<LocalizedTextDto>();
    }

    [Serializable]
    public sealed class NoticeDto
    {
        [JsonProperty("notice_id")] public string notice_id;
        [JsonProperty("severity")] public string severity;
        [JsonProperty("message")] public LocalizedTextDto message;
    }
}
