using System;
using System.Collections;
using System.Collections.Generic;
using Luoxia.Contracts;
using Luoxia.Session;
using Luoxia.UI.Core;
using Luoxia.UI.Widgets;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.UI.Immersion
{
    /// <summary>
    /// Composes immersive transitions driven only by SessionView + Bridge presentation bus.
    /// Zero plot hardcoding; missing lore/render_nodes ⇒ no UI entry.
    /// Arrival → ArrivalLoreOverlay (non-modal). Day increment → NightCurtainOverlay.
    /// presentation.frame + narrative.show → NarrativeFramePlayer only.
    /// </summary>
    public sealed class ImmersiveShellController : LuoxiaView
    {
        [Header("Overlays")]
        [SerializeField] private ArrivalLoreOverlay arrivalOverlay;
        [SerializeField] private NightCurtainOverlay nightCurtain;
        [SerializeField] private LoreChapterOverlay chapterOverlay;
        [SerializeField] private CharacterDossierPanel dossierPanel;
        [SerializeField] private NarrativeFramePlayer narrativeFramePlayer;
        [SerializeField] private StageShellOverlay stageShellOverlay;
        [SerializeField] private ScenePortraitLayer scenePortraitLayer;

        [Header("Location transition")]
        [SerializeField] private CanvasGroup sceneFadeGroup;
        [SerializeField] private float crossfadeSeconds = 0.45f;

        [Header("Interaction anchors")]
        [SerializeField] private RectTransform anchorRoot;
        [SerializeField] private Button anchorButtonPrefab;

        private IPresentationBus _presentation;
        private IDialogueSelection _selection;
        private readonly SeenLoreTracker _seenLore = new SeenLoreTracker();
        private string _lastLocationEntityId;
        private int _lastDay = -1;
        private bool _bootstrapped;
        private readonly Dictionary<string, AnchorEntry> _anchors = new Dictionary<string, AnchorEntry>(StringComparer.Ordinal);
        private Coroutine _fadeRoutine;

        private sealed class AnchorEntry
        {
            public Button Button;
            public string SubjectEntityId;
        }

        public void Configure(
            ISessionViewSource session,
            IPresentationBus presentation,
            IDialogueSelection selection)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            _presentation = presentation;
            _selection = selection;

            narrativeFramePlayer?.Bind(presentation);
            stageShellOverlay?.Bind(presentation);

            if (_selection != null)
            {
                _selection.Changed -= HandleSelectionChanged;
                _selection.Changed += HandleSelectionChanged;
            }

            scenePortraitLayer?.SetSubjectInspectHandler(HandleInspectSubject);
        }

        public void ConfigureStageIntents(IPlayerIntentSink intents)
        {
            stageShellOverlay?.Configure(intents);
        }

        protected override void OnUnbound()
        {
            if (_selection != null)
            {
                _selection.Changed -= HandleSelectionChanged;
            }

            narrativeFramePlayer?.Unbind();
            stageShellOverlay?.Unbind();
            ClearAllAnchors();
            // Rebind (reconnect / in-Play reprovision) must re-seed lore against the new session.
            _bootstrapped = false;
            _lastLocationEntityId = null;
            _lastDay = 0;
            _seenLore.ResetForSession(string.Empty);
        }

        public override void OnSessionView(SessionViewDto view)
        {
            if (view == null)
            {
                return;
            }

            RebuildAnchors(view);
            UpdateSpeakerPortrait(view);

            var locationId = view.player_location_entity_id;
            var day = view.day_cycle != null ? view.day_cycle.day : 0;

            if (!_bootstrapped)
            {
                _seenLore.SeedFromView(view);
                _lastLocationEntityId = locationId;
                _lastDay = day;
                _bootstrapped = true;
                return;
            }

            _seenLore.ResetForSession(view.session_id);

            if (!string.IsNullOrEmpty(locationId) &&
                !string.Equals(locationId, _lastLocationEntityId, StringComparison.Ordinal))
            {
                _lastLocationEntityId = locationId;
                HandleLocationChanged(view, locationId);
            }
            else if (string.IsNullOrEmpty(locationId))
            {
                _lastLocationEntityId = locationId;
            }

            if (day > 0 && _lastDay > 0 && day > _lastDay)
            {
                _lastDay = day;
                HandleDayAdvanced(view);
            }
            else if (day > 0)
            {
                _lastDay = day;
            }
        }

        private void HandleLocationChanged(SessionViewDto view, string locationEntityId)
        {
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
            }

            _fadeRoutine = StartCoroutine(CrossfadeScene(() =>
            {
                // lore_id diff only — never NarrativeFrame modal for arrival.
                var arrival = LoreQuery.FindUnseenArrival(
                    view,
                    locationEntityId,
                    id => !_seenLore.HasSeen(id));
                if (arrival == null)
                {
                    return;
                }

                if (!_seenLore.TryMarkNew(arrival.lore_id))
                {
                    return;
                }

                arrivalOverlay?.Show(arrival);
            }));
        }

        private void HandleDayAdvanced(SessionViewDto view)
        {
            var nightfall = LoreQuery.FindUnseenNightfall(
                view,
                id => !_seenLore.HasSeen(id));
            if (nightfall == null)
            {
                // Still play a brief empty curtain so day rollover has Host nightfall beat.
                nightCurtain?.Play(null);
                return;
            }

            if (!_seenLore.TryMarkNew(nightfall.lore_id))
            {
                return;
            }

            nightCurtain?.Play(nightfall);
        }

        private IEnumerator CrossfadeScene(Action midPoint)
        {
            var group = sceneFadeGroup;
            if (group == null && scenePortraitLayer != null)
            {
                group = scenePortraitLayer.GetComponent<CanvasGroup>();
            }

            if (group == null)
            {
                midPoint?.Invoke();
                yield break;
            }

            var duration = Mathf.Max(0.01f, crossfadeSeconds);
            var half = duration * 0.5f;
            var t = 0f;
            var start = group.alpha > 0.01f ? group.alpha : 1f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(start, 0f, t / half);
                yield return null;
            }

            group.alpha = 0f;
            midPoint?.Invoke();
            t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(0f, 1f, t / half);
                yield return null;
            }

            group.alpha = 1f;
        }

        private void HandleSelectionChanged(DialogueTarget? _)
        {
            if (LatestView != null)
            {
                UpdateSpeakerPortrait(LatestView);
            }
        }

        private void UpdateSpeakerPortrait(SessionViewDto view)
        {
            var speakerId = ResolveActiveSpeakerEntityId(view);
            scenePortraitLayer?.SetFocusSubject(speakerId);
        }

        private string ResolveActiveSpeakerEntityId(SessionViewDto view)
        {
            var selected = _selection != null ? _selection.Current : null;
            if (selected.HasValue &&
                selected.Value.kind == DialogueParticipantKind.Entity &&
                !string.IsNullOrEmpty(selected.Value.entityId))
            {
                return selected.Value.entityId;
            }

            if (view.dialogues == null)
            {
                return null;
            }

            for (var i = 0; i < view.dialogues.Count; i++)
            {
                var d = view.dialogues[i];
                if (d == null || !d.IsActive || d.turns == null || d.turns.Count == 0)
                {
                    continue;
                }

                var turn = d.turns[d.turns.Count - 1];
                if (turn?.speaker != null &&
                    turn.speaker.KindEnum == DialogueParticipantKind.Entity &&
                    turn.speaker.entity_id != view.player_entity_id)
                {
                    return turn.speaker.entity_id;
                }
            }

            return null;
        }

        private void HandleInspectSubject(string subjectEntityId)
        {
            if (LatestView == null || dossierPanel == null)
            {
                return;
            }

            dossierPanel.TryOpen(LatestView, subjectEntityId);
        }

        private void RebuildAnchors(SessionViewDto view)
        {
            if (anchorRoot == null)
            {
                ClearAllAnchors();
                return;
            }

            var desired = new HashSet<string>(StringComparer.Ordinal);
            if (view.render_nodes != null)
            {
                for (var i = 0; i < view.render_nodes.Count; i++)
                {
                    var node = view.render_nodes[i];
                    if (node == null || node.KindEnum != RenderNodeKind.InteractionAnchor)
                    {
                        continue;
                    }

                    var subjectId = node.subject_entity_id;
                    if (string.IsNullOrEmpty(subjectId) || !LoreQuery.HasDossier(view, subjectId))
                    {
                        continue;
                    }

                    var displayName = LoreQuery.ResolveSubjectDisplayName(view, subjectId);
                    if (string.IsNullOrEmpty(displayName))
                    {
                        // No display name ⇒ do not put an anchor on screen.
                        continue;
                    }

                    var key = !string.IsNullOrEmpty(node.node_id) ? node.node_id : subjectId;
                    desired.Add(key);

                    if (_anchors.TryGetValue(key, out var existing) && existing.Button != null)
                    {
                        existing.SubjectEntityId = subjectId;
                        RefreshAnchorLabel(existing.Button, displayName);
                        RebindAnchorClick(existing.Button, subjectId);
                        continue;
                    }

                    var btn = CreateAnchorButton(node, displayName);
                    var captured = subjectId;
                    btn.onClick.AddListener(() => HandleInspectSubject(captured));
                    _anchors[key] = new AnchorEntry
                    {
                        Button = btn,
                        SubjectEntityId = subjectId
                    };
                }
            }

            var removeKeys = new List<string>();
            foreach (var pair in _anchors)
            {
                if (!desired.Contains(pair.Key))
                {
                    removeKeys.Add(pair.Key);
                }
            }

            for (var i = 0; i < removeKeys.Count; i++)
            {
                var key = removeKeys[i];
                if (_anchors.TryGetValue(key, out var entry) && entry.Button != null)
                {
                    Destroy(entry.Button.gameObject);
                }

                _anchors.Remove(key);
            }
        }

        private Button CreateAnchorButton(RenderNodeDto node, string displayName)
        {
            Button btn;
            if (anchorButtonPrefab != null)
            {
                btn = Instantiate(anchorButtonPrefab, anchorRoot);
                btn.gameObject.SetActive(true);
            }
            else
            {
                // Visible nameplate — never alpha≈0 invisible hit targets.
                var go = new GameObject($"Anchor_{node.node_id}", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(anchorRoot, false);
                var img = go.GetComponent<Image>();
                img.color = new Color(0.08f, 0.06f, 0.1f, 0.88f);
                btn = go.GetComponent<Button>();
                btn.targetGraphic = img;
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.32f, 0.48f);
                rt.anchorMax = new Vector2(0.68f, 0.56f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
                textGo.transform.SetParent(go.transform, false);
                var trt = textGo.GetComponent<RectTransform>();
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(12f, 4f);
                trt.offsetMax = new Vector2(-12f, -4f);
                var text = textGo.GetComponent<Text>();
                text.alignment = TextAnchor.MiddleCenter;
                text.fontSize = 24;
                text.color = new Color(1f, 0.95f, 0.85f, 1f);
                text.raycastTarget = false;
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            RefreshAnchorLabel(btn, displayName);
            return btn;
        }

        private static void RefreshAnchorLabel(Button btn, string displayName)
        {
            var label = btn.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.text = displayName ?? string.Empty;
                label.raycastTarget = false;
            }
        }

        private void RebindAnchorClick(Button btn, string subjectId)
        {
            btn.onClick.RemoveAllListeners();
            var captured = subjectId;
            btn.onClick.AddListener(() => HandleInspectSubject(captured));
        }

        private void ClearAllAnchors()
        {
            foreach (var pair in _anchors)
            {
                if (pair.Value?.Button != null)
                {
                    Destroy(pair.Value.Button.gameObject);
                }
            }

            _anchors.Clear();
        }
    }
}
