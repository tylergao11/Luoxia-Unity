using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Luoxia.Contracts
{
    /// <summary>
    /// Server presentation / stage / dialogue.reply message bodies (client-bridge.v1).
    /// </summary>
    [Serializable]
    public sealed class PresentationFrameDto
    {
        [JsonProperty("type")] public string type;
        [JsonProperty("frame_id")] public string frame_id;
        [JsonProperty("view_revision")] public int view_revision;
        [JsonProperty("operations")] public List<PresentationOpDto> operations = new List<PresentationOpDto>();
    }

    [Serializable]
    public sealed class PresentationOpDto
    {
        [JsonProperty("op")] public string op;
        [JsonProperty("effect_id")] public string effect_id;
        [JsonProperty("parameters")] public JObject parameters;
        [JsonProperty("event_card_id")] public string event_card_id;
        [JsonProperty("presentation")] public EventResultPresentationViewDto presentation;
        [JsonProperty("channel_id")] public string channel_id;
        [JsonProperty("asset")] public AssetContentRefDto asset;
        [JsonProperty("slot_id")] public string slot_id;
        [JsonProperty("subject")] public JToken subject;

        public bool IsNarrativeShow => op == "narrative.show";
    }

    [Serializable]
    public sealed class EventResultPresentationViewDto
    {
        [JsonProperty("presentation_id")] public string presentation_id;
        [JsonProperty("segments")] public List<NarrativeSegmentViewDto> segments = new List<NarrativeSegmentViewDto>();
    }

    [Serializable]
    public sealed class NarrativeSegmentViewDto
    {
        [JsonProperty("segment_kind")] public string segment_kind;
        /// <summary>
        /// narration/system/notice: LocalizedText object; dialogue_quote: plain string.
        /// </summary>
        [JsonProperty("text")] public JToken text;
        [JsonProperty("dialogue_id")] public string dialogue_id;
        [JsonProperty("turn_id")] public string turn_id;
        [JsonProperty("speaker")] public DialogueParticipantRefDto speaker;
        [JsonProperty("locale")] public string locale;
        [JsonProperty("emotion_id")] public string emotion_id;

        public bool IsDialogueQuote => segment_kind == "dialogue_quote";

        public string ResolveDisplayText(string preferredLocale = null)
        {
            if (text == null || text.Type == JTokenType.Null)
            {
                return HostDisplayLocale.MissingPlaceholder;
            }

            if (text.Type == JTokenType.String)
            {
                return text.Value<string>() ?? string.Empty;
            }

            if (text.Type == JTokenType.Object)
            {
                var localized = text.ToObject<LocalizedTextDto>();
                return localized != null
                    ? localized.Resolve(preferredLocale)
                    : HostDisplayLocale.MissingPlaceholder;
            }

            return text.ToString();
        }
    }

    [Serializable]
    public sealed class StageOpenDto
    {
        [JsonProperty("type")] public string type;
        [JsonProperty("stage_instance_id")] public string stage_instance_id;
        [JsonProperty("stage_revision")] public int stage_revision;
        [JsonProperty("stage_module_lock")] public JObject stage_module_lock;
        [JsonProperty("scene_id")] public string scene_id;
        [JsonProperty("visible_context")] public JObject visible_context;
        [JsonProperty("allowed_input_types")] public List<string> allowed_input_types = new List<string>();
        [JsonProperty("bindings")] public List<StageAssetBindingDto> bindings = new List<StageAssetBindingDto>();
    }

    [Serializable]
    public sealed class StageUpdateDto
    {
        [JsonProperty("type")] public string type;
        [JsonProperty("stage_instance_id")] public string stage_instance_id;
        [JsonProperty("stage_revision")] public int stage_revision;
        [JsonProperty("visible_state")] public JObject visible_state;
    }

    [Serializable]
    public sealed class StageCloseDto
    {
        [JsonProperty("type")] public string type;
        [JsonProperty("stage_instance_id")] public string stage_instance_id;
        [JsonProperty("stage_revision")] public int stage_revision;
        [JsonProperty("reason_code")] public string reason_code;
    }

    [Serializable]
    public sealed class StageAssetBindingDto
    {
        [JsonProperty("binding_id")] public string binding_id;
        [JsonProperty("subject")] public JToken subject;
        [JsonProperty("slot_id")] public string slot_id;
        [JsonProperty("asset")] public AssetContentRefDto asset;
    }

    [Serializable]
    public sealed class DialogueReplyDto
    {
        [JsonProperty("type")] public string type;
        [JsonProperty("dialogue_id")] public string dialogue_id;
        [JsonProperty("turn")] public DialogueTurnViewDto turn;
    }

    [Serializable]
    public sealed class ProtocolErrorDto
    {
        [JsonProperty("type")] public string type;
        [JsonProperty("code")] public string code;
        [JsonProperty("message")] public string message;
        [JsonProperty("recoverability")] public string recoverability;
        [JsonProperty("details")] public JObject details;
    }
}
