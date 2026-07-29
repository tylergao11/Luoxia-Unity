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
        [JsonProperty("world_time")] public LogicalTimeDto world_time;
        [JsonProperty("render_nodes")] public List<RenderNodeDto> render_nodes = new List<RenderNodeDto>();
        [JsonProperty("goal_plans")] public List<GoalPlanViewDto> goal_plans = new List<GoalPlanViewDto>();
        [JsonProperty("notices")] public List<NoticeDto> notices = new List<NoticeDto>();
        [JsonProperty("day_cycle")] public DayCycleStateDto day_cycle;
        [JsonProperty("event_budget")] public EventBudgetViewDto event_budget;
        [JsonProperty("event_cards")] public List<EventCardViewDto> event_cards = new List<EventCardViewDto>();
        [JsonProperty("dialogues")] public List<DialogueViewDto> dialogues = new List<DialogueViewDto>();
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

        public DialogueStatus StatusEnum =>
            status == "closed" ? DialogueStatus.Closed : DialogueStatus.Active;

        public bool IsActive => StatusEnum == DialogueStatus.Active;
    }

    [Serializable]
    public sealed class DialogueParticipantRefDto
    {
        [JsonProperty("participant_kind")] public string participant_kind;
        [JsonProperty("entity_id")] public string entity_id;

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
