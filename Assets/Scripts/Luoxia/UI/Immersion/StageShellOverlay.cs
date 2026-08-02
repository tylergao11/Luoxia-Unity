using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Luoxia.Contracts;
using Luoxia.Session;
using Luoxia.UI.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Immersion
{
    /// <summary>
    /// Minimal Stage shell: fullscreen overlay + visible_context / visible_state text
    /// + outcome choice buttons → stage.outcome_proposal.
    /// </summary>
    public sealed class StageShellOverlay : MonoBehaviour
    {
        private static readonly Regex NamespacedId = new Regex(
            @"^[a-z][a-z0-9_-]*(?:\.[a-z][a-z0-9_-]*)+$",
            RegexOptions.Compiled);

        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Text sceneIdText;
        [SerializeField] private Text contextText;
        [SerializeField] private Button dismissHintButton;
        [SerializeField] private Transform outcomeButtonRoot;
        [SerializeField] private Button outcomeButtonPrefab;
        [SerializeField] private Text outcomeHintText;

        private IPresentationBus _bus;
        private IPlayerIntentSink _intents;
        private string _activeStageId;
        private int _activeStageRevision;
        private readonly List<Button> _outcomeButtons = new List<Button>();

        public bool IsOpen =>
            !string.IsNullOrEmpty(_activeStageId) &&
            canvasGroup != null &&
            canvasGroup.blocksRaycasts;

        private void Awake()
        {
            if (dismissHintButton != null)
            {
                dismissHintButton.onClick.AddListener(HandleLocalDismiss);
            }

            HideImmediate();
        }

        private void OnDestroy()
        {
            Unbind();
            if (dismissHintButton != null)
            {
                dismissHintButton.onClick.RemoveListener(HandleLocalDismiss);
            }

            ClearOutcomeButtons();
        }

        public void Configure(IPlayerIntentSink intents)
        {
            _intents = intents;
        }

        public void Bind(IPresentationBus bus)
        {
            Unbind();
            _bus = bus;
            if (_bus == null)
            {
                return;
            }

            _bus.StageOpened += HandleOpen;
            _bus.StageUpdated += HandleUpdate;
            _bus.StageClosed += HandleClose;
        }

        public void Unbind()
        {
            if (_bus == null)
            {
                return;
            }

            _bus.StageOpened -= HandleOpen;
            _bus.StageUpdated -= HandleUpdate;
            _bus.StageClosed -= HandleClose;
            _bus = null;
        }

        private void HandleOpen(StageOpenDto open)
        {
            if (open == null || string.IsNullOrEmpty(open.stage_instance_id))
            {
                return;
            }

            _activeStageId = open.stage_instance_id;
            _activeStageRevision = open.stage_revision;
            if (sceneIdText != null)
            {
                sceneIdText.text = open.scene_id ?? string.Empty;
            }

            if (contextText != null)
            {
                contextText.text = FormatJsonObject(open.visible_context);
            }

            RebuildOutcomeButtons(open.visible_context);
            Show();
        }

        private void HandleUpdate(StageUpdateDto update)
        {
            if (update == null || update.stage_instance_id != _activeStageId)
            {
                return;
            }

            _activeStageRevision = update.stage_revision;
            if (contextText != null)
            {
                contextText.text = FormatJsonObject(update.visible_state);
            }

            RebuildOutcomeButtons(update.visible_state);
        }

        private void HandleClose(StageCloseDto close)
        {
            if (close == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(_activeStageId) &&
                close.stage_instance_id != _activeStageId)
            {
                return;
            }

            _activeStageId = null;
            ClearOutcomeButtons();
            HideImmediate();
        }

        private void HandleLocalDismiss()
        {
            // Only clears UI if Server already closed; never invents stage.close.
            if (string.IsNullOrEmpty(_activeStageId))
            {
                HideImmediate();
            }
        }

        private void RebuildOutcomeButtons(JObject context)
        {
            ClearOutcomeButtons();
            var choices = ExtractOutcomeChoices(context);
            if (outcomeHintText != null)
            {
                outcomeHintText.gameObject.SetActive(choices.Count == 0);
                outcomeHintText.text = choices.Count == 0 ? "等待 Stage 结果选项" : string.Empty;
            }

            for (var i = 0; i < choices.Count; i++)
            {
                var choice = choices[i];
                var btn = CreateOutcomeButton(choice.Label);
                var type = choice.OutcomeType;
                var outcome = choice.Outcome;
                btn.onClick.AddListener(() => SubmitOutcome(type, outcome));
                _outcomeButtons.Add(btn);
            }
        }

        private void SubmitOutcome(string outcomeType, JObject outcome)
        {
            if (string.IsNullOrEmpty(_activeStageId) || _intents == null)
            {
                return;
            }

            _intents.TrySubmitStageOutcome(_activeStageId, _activeStageRevision, outcomeType, outcome);
        }

        private Button CreateOutcomeButton(string label)
        {
            Button btn;
            var parent = outcomeButtonRoot != null ? outcomeButtonRoot : transform;
            if (outcomeButtonPrefab != null)
            {
                btn = Instantiate(outcomeButtonPrefab, parent);
            }
            else
            {
                var go = new GameObject("Outcome", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
                go.transform.SetParent(parent, false);
                var img = go.GetComponent<Image>();
                img.color = new Color(0.14f, 0.12f, 0.2f, 0.95f);
                btn = go.GetComponent<Button>();
                btn.targetGraphic = img;
                var le = go.GetComponent<LayoutElement>();
                le.minHeight = 64f;
                le.preferredHeight = 64f;
                var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
                textGo.transform.SetParent(go.transform, false);
                var rt = textGo.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(12f, 4f);
                rt.offsetMax = new Vector2(-12f, -4f);
                var text = textGo.GetComponent<Text>();
                text.alignment = TextAnchor.MiddleCenter;
                text.fontSize = 26;
                text.color = Color.white;
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.raycastTarget = false;
            }

            var labelText = btn.GetComponentInChildren<Text>();
            if (labelText != null)
            {
                labelText.text = label ?? string.Empty;
            }

            return btn;
        }

        private void ClearOutcomeButtons()
        {
            for (var i = 0; i < _outcomeButtons.Count; i++)
            {
                if (_outcomeButtons[i] != null)
                {
                    Destroy(_outcomeButtons[i].gameObject);
                }
            }

            _outcomeButtons.Clear();
        }

        private static List<OutcomeChoice> ExtractOutcomeChoices(JObject context)
        {
            var list = new List<OutcomeChoice>();
            if (context == null || !context.HasValues)
            {
                return list;
            }

            // Preferred: outcome_options: [{ outcome_type, label?, outcome? }, ...]
            var optionsToken = context["outcome_options"];
            if (optionsToken != null && optionsToken.Type == JTokenType.Array)
            {
                var options = (JArray)optionsToken;
                for (var i = 0; i < options.Count; i++)
                {
                    var item = options[i] as JObject;
                    if (item == null)
                    {
                        continue;
                    }

                    var type = item["outcome_type"]?.ToString();
                    if (!IsNamespaced(type))
                    {
                        continue;
                    }

                    var label = item["label"]?.ToString();
                    if (string.IsNullOrEmpty(label))
                    {
                        label = type;
                    }

                    list.Add(new OutcomeChoice
                    {
                        OutcomeType = type,
                        Label = label,
                        Outcome = item["outcome"] as JObject ?? new JObject()
                    });
                }

                return list;
            }

            // Alternate: outcomes: { "ns.type": { ... } | "label" }
            var outcomesToken = context["outcomes"];
            if (outcomesToken != null && outcomesToken.Type == JTokenType.Object)
            {
                var outcomes = (JObject)outcomesToken;
                foreach (var prop in outcomes.Properties())
                {
                    if (!IsNamespaced(prop.Name))
                    {
                        continue;
                    }

                    var label = prop.Name;
                    var outcome = new JObject();
                    var obj = prop.Value as JObject;
                    if (obj != null)
                    {
                        label = obj["label"]?.ToString() ?? prop.Name;
                        outcome = obj["outcome"] as JObject ?? obj;
                    }
                    else if (prop.Value != null && prop.Value.Type == JTokenType.String)
                    {
                        label = prop.Value.Value<string>();
                    }

                    list.Add(new OutcomeChoice
                    {
                        OutcomeType = prop.Name,
                        Label = label,
                        Outcome = outcome
                    });
                }
            }

            return list;
        }

        private static bool IsNamespaced(string value) =>
            !string.IsNullOrEmpty(value) && NamespacedId.IsMatch(value);

        private void Show()
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        private void HideImmediate()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
        }

        private static string FormatJsonObject(JObject obj)
        {
            if (obj == null || !obj.HasValues)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (var prop in obj.Properties())
            {
                if (prop.Name == "outcome_options" || prop.Name == "outcomes")
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }

                var value = prop.Value;
                string rendered;
                if (value == null || value.Type == JTokenType.Null)
                {
                    rendered = string.Empty;
                }
                else if (value.Type == JTokenType.String)
                {
                    rendered = value.Value<string>();
                }
                else if (value.Type == JTokenType.Object)
                {
                    var localized = value.ToObject<LocalizedTextDto>();
                    rendered = localized != null ? localized.Resolve() : value.ToString(Newtonsoft.Json.Formatting.None);
                }
                else
                {
                    rendered = value.ToString(Newtonsoft.Json.Formatting.None);
                }

                sb.Append(prop.Name);
                if (!string.IsNullOrEmpty(rendered))
                {
                    sb.Append(": ");
                    sb.Append(rendered);
                }
            }

            return sb.ToString();
        }

        private struct OutcomeChoice
        {
            public string OutcomeType;
            public string Label;
            public JObject Outcome;
        }
    }
}
