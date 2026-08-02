using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Luoxia.Contracts;
using Luoxia.UI.Core;
using Luoxia.UI.Features;
using Luoxia.UI.Immersion;
using Luoxia.UI.Screens;
using Luoxia.UI.Widgets;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Luoxia.App
{
    /// <summary>
    /// Play Mode accept driver (runtime assembly). Triggered by marker file from Editor Accept.
    /// </summary>
    public sealed class PlayAcceptRuntimeDriver : MonoBehaviour
    {
        public const string MarkerFileName = ".luoxia-play-accept-run";
        public const string ArtifactDir = "Artifacts/play-accept";
        private const float DialogueWaitSec = 240f;
        private const int CaptureWidth = 1080;
        private const int CaptureHeight = 1920;
        /// <summary>Historical blank Camera.main clear-color PNG size (~solid dark blue).</summary>
        private const long KnownBlankCameraClearPngBytes = 31664L;

        private static PropertyInfo s_latestViewProp;
        private readonly StringBuilder _report = new StringBuilder();
        private readonly StringBuilder _notes = new StringBuilder();
        private int _passed;
        private int _failed;
        private int _captureAttempts;
        private int _blankCaptures;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            var marker = MarkerPath();
            if (!File.Exists(marker))
            {
                return;
            }

            if (FindObjectOfType<PlayAcceptRuntimeDriver>() != null)
            {
                return;
            }

            var go = new GameObject("PlayAcceptRuntimeDriver");
            DontDestroyOnLoad(go);
            go.AddComponent<PlayAcceptRuntimeDriver>();
            Debug.Log("[Luoxia] PlayAcceptRuntimeDriver auto-started from marker.");
        }

        private IEnumerator Start()
        {
            yield return Run();
        }

        private IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(1.2f);

            yield return Capture("01-dialogue-boot.png");
            CheckBootChrome();

            ClickFirstNamedAvatar();
            yield return new WaitForSecondsRealtime(0.7f);
            yield return Capture("02-after-avatar-select.png");
            CheckDialogueInputAndPortrait();
            CheckAvatarTint();
            yield return VerifyInputClickFocus();

            var view0 = GetLatestView();
            var baseRemain = view0?.event_budget != null ? view0.event_budget.remaining : -1;
            var baseCards = CountAvailableCards(view0);
            var baseTurns = CountDialogueTurns(view0);

            SendDialogueLine();
            // Optimistic send: player echo + thinking ghost should appear before SessionView lands.
            CheckOptimisticSendChrome();
            var deadline = Time.realtimeSinceStartup + DialogueWaitSec;
            var cardOrBudget = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                var v = GetLatestView();
                var remain = v?.event_budget != null ? v.event_budget.remaining : -1;
                // Turns alone are not enough — product rule: dialogue spends budget and publishes a card.
                if ((baseRemain >= 0 && remain >= 0 && remain < baseRemain) ||
                    CountAvailableCards(v) > baseCards)
                {
                    cardOrBudget = true;
                    break;
                }

                yield return new WaitForSecondsRealtime(1f);
            }

            // Allow gate Complete → ClearPending to land before capture.
            yield return new WaitForSecondsRealtime(0.35f);
            yield return Capture("03-after-dialogue.png");
            Check("SessionView present after dialogue", GetLatestView() != null);
            Check("FatalOverlay still clear after dialogue",
                !IsOverlayBlocking(FindObjectOfType<SessionFatalOverlay>(true)));
            Check("CommandFeedback not pending after dialogue", !IsCommandFeedbackPending());
            var after = GetLatestView();
            var afterRemain = after?.event_budget != null ? after.event_budget.remaining : -1;
            var afterCards = CountAvailableCards(after);
            var afterTurns = CountDialogueTurns(after);
            Check(
                "dialogue spent EventBudget or published EventCard",
                cardOrBudget ||
                (baseRemain >= 0 && afterRemain >= 0 && afterRemain < baseRemain) ||
                afterCards > baseCards);
            Check("dialogue produced turns", afterTurns > baseTurns);
            if (after?.event_budget != null)
            {
                Note(
                    $"budget remaining={after.event_budget.remaining}/{after.event_budget.capacity} cards={afterCards} turns={afterTurns}");
            }

            // Day1 drain: content-neutral probes until remaining==0, then hard UX assert.
            // Coherent flow: drain → event/card → map → 收工 end-day → day2 probe (one end-day).
            yield return DrainBudgetUntilExhausted();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Capture("03b-budget-exhausted.png");
            AssertBudgetExhaustedUx();

            FindObjectOfType<MainWorldScreen>()?.RevealPendingCards();
            yield return new WaitForSecondsRealtime(0.55f);
            yield return Capture("04-pending-cards.png");
            CheckPendingCardsReveal();

            if (CountAvailableCards(GetLatestView()) > 0 && TryOpenFirstEventConfirm())
            {
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Capture("05-confirm-open.png");
                var confirm = FindObjectOfType<EventCardConfirmPanel>(true);
                Check("confirm modal IsOpen before later", confirm != null && confirm.IsOpen);
                GetSerialized<Button>(confirm, "laterButton")?.onClick.Invoke();
                yield return new WaitForSecondsRealtime(0.35f);
                confirm = FindObjectOfType<EventCardConfirmPanel>(true);
                Check("later dismisses confirm locally", confirm != null && !confirm.IsOpen);
                Check("available EventCards remain after later", CountAvailableCards(GetLatestView()) > 0);

                if (TryOpenFirstEventConfirm())
                {
                    yield return new WaitForSecondsRealtime(0.4f);
                    confirm = FindObjectOfType<EventCardConfirmPanel>(true);
                    Check("confirm modal IsOpen before open", confirm != null && confirm.IsOpen);
                    GetSerialized<Button>(confirm, "openButton")?.onClick.Invoke();
                    Note("clicked confirm open → TryTriggerEventCard");
                    var openDeadline = Time.realtimeSinceStartup + 12f;
                    while (Time.realtimeSinceStartup < openDeadline &&
                           CountAvailableCards(GetLatestView()) > 0 &&
                           !IsNarrativeOpen())
                    {
                        yield return new WaitForSecondsRealtime(0.5f);
                    }

                    yield return new WaitForSecondsRealtime(0.35f);
                    yield return Capture("06-after-event-open.png");
                    Check("event open consumed card or opened narrative",
                        CountAvailableCards(GetLatestView()) == 0 || IsNarrativeOpen());
                    Check("CommandFeedback not pending after event open", !IsCommandFeedbackPending());
                    if (IsNarrativeOpen())
                    {
                        Check("narrative kind chrome is Chinese (not raw English)",
                            IsNarrativeKindChromeChineseOrHidden());
                    }
                }
                else
                {
                    Check("event confirm reopened for open", false);
                }
            }
            else
            {
                Check("event confirm flow reachable (available card)", false);
                Note("skipped confirm open path — no available EventCard");
            }

            // Dismiss narrative fully before map — never capture map under narration modal.
            yield return DismissNarrativeUntilClosed();
            Check("narrative closed before map", !IsNarrativeOpen());
            FindObjectOfType<MapDestinationPanel>(true)?.Open();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Capture("07-map-modal.png");
            Check("narrative still closed on map capture", !IsNarrativeOpen());
            var mapPanel = FindObjectOfType<MapDestinationPanel>(true);
            Check("MapDestinationPanel open", mapPanel != null && mapPanel.IsOpen);
            var scrimImg = GetSerialized<Image>(mapPanel, "scrimImage");
            if (scrimImg != null)
            {
                var a = scrimImg.color.a;
                Check("map scrim alpha in 45–60%", a >= 0.45f && a <= 0.60f);
            }
            else
            {
                Check("map scrim Image wired", false);
            }

            CheckMapLabels();

            // Map navigation must move location without spending EventBudget/AP.
            yield return PerformMapMoveAndVerify();

            // Arrival lore may enqueue after scene crossfade — wait briefly.
            // Arrival is NON-modal (ArrivalLoreOverlay); must not open NarrativeFrame / LoreChapter.
            var loreWaitUntil = Time.realtimeSinceStartup + 3.5f;
            while (Time.realtimeSinceStartup < loreWaitUntil &&
                   !IsNarrativeOpen() &&
                   !IsLoreChapterOpen() &&
                   !IsArrivalOverlayVisible())
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }

            Check("arrival did not open NarrativeFrame modal", !IsNarrativeOpen());
            Check("arrival did not open LoreChapter modal", !IsLoreChapterOpen());
            if (IsArrivalOverlayVisible())
            {
                Note("ArrivalLoreOverlay visible (non-modal) — dismissing via tap");
                FindObjectOfType<ArrivalLoreOverlay>(true)?.Dismiss();
                yield return new WaitForSecondsRealtime(0.5f);
            }

            yield return DismissBlockingOverlays();
            Check("arrival lore/narrative not blocking after map.move",
                !IsNarrativeOpen() && !IsLoreChapterOpen() && !IsNightCurtainOpen());

            // player_day.end / 收工 — with remaining==0 this is the natural day-end path.
            yield return PerformEndDayAndVerify();

            // Day 2: content-agnostic second loop (same neutral probe) or health floor.
            yield return PerformDay2LoopAndVerify();

            // Overlay PNG: require non-blank when graphics exist; -nographics batch soft-skips.
            if (HasUsableGraphics())
            {
                Check(
                    "capture not blank camera clear",
                    _captureAttempts > 0 && _blankCaptures < _captureAttempts);
            }
            else
            {
                Note("capture check soft-skipped: GraphicsDeviceType.Null (-nographics)");
            }

            WriteReportAndClearMarker();
        }

        private IEnumerator PerformMapMoveAndVerify()
        {
            var before = GetLatestView();
            var beforeLocId = before?.player_location_entity_id ?? string.Empty;
            var beforeLocLabel = LoreQuery.ResolveLocationLabel(before);
            var beforeBudget = before?.event_budget != null ? before.event_budget.remaining : -1;
            Note(
                $"map before location_label={beforeLocLabel} location_id={beforeLocId} " +
                $"budget_remaining={beforeBudget}");

            var clicked = TryClickFirstNonCurrentMapDestination();
            Check("clicked first non-current map destination", clicked);
            if (!clicked)
            {
                Check("map.move changed player location", false);
                Check("EventBudget.remaining unchanged by map.move", false);
                Check("CommandFeedback not pending after map.move", !IsCommandFeedbackPending());
                var mapFail = FindObjectOfType<MapDestinationPanel>(true);
                Check("MapDestinationPanel closed after map.move", mapFail != null && !mapFail.IsOpen);
                yield return Capture("08-after-map-move.png");
                yield break;
            }

            // Structural wait: only player_location_entity_id (labels may be logged in Notes).
            var moved = false;
            var deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var v = GetLatestView();
                var locId = v?.player_location_entity_id ?? string.Empty;
                if (!string.IsNullOrEmpty(beforeLocId) &&
                    !string.IsNullOrEmpty(locId) &&
                    locId != beforeLocId)
                {
                    moved = true;
                    break;
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }

            // Allow gate Complete → ClearPending and panel Close to settle.
            yield return new WaitForSecondsRealtime(0.35f);

            var after = GetLatestView();
            var afterLocId = after?.player_location_entity_id ?? string.Empty;
            var afterLocLabel = LoreQuery.ResolveLocationLabel(after);
            var afterBudget = after?.event_budget != null ? after.event_budget.remaining : -1;
            Note(
                $"map after location_label={afterLocLabel} location_id={afterLocId} " +
                $"budget_remaining={afterBudget}");

            Check(
                "map.move changed player location",
                moved ||
                (!string.IsNullOrEmpty(beforeLocId) &&
                 !string.IsNullOrEmpty(afterLocId) &&
                 afterLocId != beforeLocId));
            Check(
                "EventBudget.remaining unchanged by map.move",
                beforeBudget >= 0 && afterBudget == beforeBudget);
            Check("CommandFeedback not pending after map.move", !IsCommandFeedbackPending());
            var map = FindObjectOfType<MapDestinationPanel>(true);
            Check("MapDestinationPanel closed after map.move", map != null && !map.IsOpen);
            CheckSceneFollowsLocation("after map.move");

            yield return Capture("08-after-map-move.png");
        }

        private IEnumerator PerformEndDayAndVerify()
        {
            yield return DismissBlockingOverlays();

            var before = GetLatestView();
            var beforeDay = before?.day_cycle != null ? before.day_cycle.day : -1;
            var beforePhase = before?.day_cycle != null ? before.day_cycle.phase ?? string.Empty : string.Empty;
            var beforeRev = before != null ? before.view_revision : -1;
            var beforePhaseRev = before?.day_cycle != null ? before.day_cycle.phase_revision : -1;
            Note(
                $"end-day before day={beforeDay} phase={beforePhase} " +
                $"phase_revision={beforePhaseRev} view_revision={beforeRev}");

            var screen = FindObjectOfType<MainWorldScreen>(true);
            var endDay = GetSerialized<Button>(screen, "endDayButton");
            Check("EndDayButton present", endDay != null);
            if (endDay == null)
            {
                Check("player_day.end advanced day/phase or accepted view update", false);
                Check("CommandFeedback not pending after end-day", !IsCommandFeedbackPending());
                Check("FatalOverlay still clear after end-day",
                    !IsOverlayBlocking(FindObjectOfType<SessionFatalOverlay>(true)));
                yield return Capture("09-after-end-day.png");
                yield break;
            }

            var playerPhase = before?.day_cycle == null ||
                              before.day_cycle.PhaseEnum == DayPhase.Player;
            Check("day_cycle is player phase (end-day allowed)", playerPhase);
            Check("EndDayButton interactable", endDay.interactable);

            if (!endDay.interactable)
            {
                Check("player_day.end advanced day/phase or accepted view update", false);
                Check("CommandFeedback not pending after end-day", !IsCommandFeedbackPending());
                Check("FatalOverlay still clear after end-day",
                    !IsOverlayBlocking(FindObjectOfType<SessionFatalOverlay>(true)));
                yield return Capture("09-after-end-day.png");
                yield break;
            }

            endDay.onClick.Invoke();
            Note("clicked EndDayButton → player_day.end (or EndDayConfirm if cards remain)");

            // If available EventCards remain, confirm modal opens — force end for Accept path.
            yield return new WaitForSecondsRealtime(0.35f);
            var endConfirm = FindObjectOfType<EndDayConfirmPanel>(true);
            if (endConfirm != null && endConfirm.IsOpen)
            {
                Note("EndDayConfirm open — clicking 仍要收工");
                var force = GetSerialized<Button>(endConfirm, "forceEndButton");
                Check("EndDayConfirm forceEndButton present", force != null);
                force?.onClick.Invoke();
                yield return new WaitForSecondsRealtime(0.35f);
            }

            var advanced = false;
            var deadline = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (IsCommandFeedbackPending())
                {
                    yield return new WaitForSecondsRealtime(0.5f);
                    continue;
                }

                var v = GetLatestView();
                if (v == null)
                {
                    yield return new WaitForSecondsRealtime(0.5f);
                    continue;
                }

                var day = v.day_cycle != null ? v.day_cycle.day : -1;
                var phase = v.day_cycle != null ? v.day_cycle.phase ?? string.Empty : string.Empty;
                var phaseRev = v.day_cycle != null ? v.day_cycle.phase_revision : -1;
                var rev = v.view_revision;
                if ((beforeDay >= 0 && day > beforeDay) ||
                    (beforePhaseRev >= 0 && phaseRev > beforePhaseRev) ||
                    (!string.IsNullOrEmpty(beforePhase) &&
                     !string.IsNullOrEmpty(phase) &&
                     phase != beforePhase) ||
                    (beforeRev >= 0 && rev > beforeRev))
                {
                    advanced = true;
                    break;
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }

            yield return new WaitForSecondsRealtime(0.35f);
            // Nightfall / phase lore must not stick.
            yield return DismissBlockingOverlays();

            var after = GetLatestView();
            var afterDay = after?.day_cycle != null ? after.day_cycle.day : -1;
            var afterPhase = after?.day_cycle != null ? after.day_cycle.phase ?? string.Empty : string.Empty;
            var afterPhaseRev = after?.day_cycle != null ? after.day_cycle.phase_revision : -1;
            var afterRev = after != null ? after.view_revision : -1;
            Note(
                $"end-day after day={afterDay} phase={afterPhase} " +
                $"phase_revision={afterPhaseRev} view_revision={afterRev}");

            Check(
                "player_day.end advanced day/phase or accepted view update",
                advanced ||
                (beforeDay >= 0 && afterDay > beforeDay) ||
                (beforePhaseRev >= 0 && afterPhaseRev > beforePhaseRev) ||
                (!string.IsNullOrEmpty(beforePhase) &&
                 !string.IsNullOrEmpty(afterPhase) &&
                 afterPhase != beforePhase) ||
                (beforeRev >= 0 && afterRev > beforeRev));
            Check("CommandFeedback not pending after end-day", !IsCommandFeedbackPending());
            Check("FatalOverlay still clear after end-day",
                !IsOverlayBlocking(FindObjectOfType<SessionFatalOverlay>(true)));
            Check("lore/narrative not blocking after end-day",
                !IsNarrativeOpen() && !IsLoreChapterOpen() && !IsNightCurtainOpen());

            yield return Capture("09-after-end-day.png");
        }

        /// <summary>
        /// After end-day → day 2: re-select avatar, same neutral probe, wait for budget/card
        /// within DialogueWaitSec. If director blocks or times out, assert SessionView health floor.
        /// </summary>
        private IEnumerator PerformDay2LoopAndVerify()
        {
            yield return DismissBlockingOverlays();

            var day2 = GetLatestView();
            var dayNum = day2?.day_cycle != null ? day2.day_cycle.day : -1;
            var locLabel = LoreQuery.ResolveLocationLabel(day2);
            var locId = day2?.player_location_entity_id ?? string.Empty;
            Note(
                $"day2 enter day={dayNum} location_label={locLabel} location_id={locId} " +
                $"budget={(day2?.event_budget != null ? day2.event_budget.remaining + "/" + day2.event_budget.capacity : "n/a")}");

            Check("day2 SessionView present", day2 != null);
            Check("day2 FatalOverlay clear",
                !IsOverlayBlocking(FindObjectOfType<SessionFatalOverlay>(true)));
            Check("day2 location label from view only (non-empty)",
                !string.IsNullOrWhiteSpace(locLabel));
            CheckLocationHudMatchesView(day2);

            FindObjectOfType<MainWorldScreen>()?.ActivateFeature(DialogueFeaturePanel.Id);
            yield return new WaitForSecondsRealtime(0.35f);

            var dialogue = FindObjectOfType<DialogueFeaturePanel>(true);
            var inputBar = GetSerialized<CanvasGroup>(dialogue, "inputBarGroup");
            var input = GetSerialized<InputField>(dialogue, "inputField");
            Check("day2 dialogue InputBar visible (alpha≈1)",
                inputBar != null && inputBar.alpha > 0.9f);
            Check("day2 InputField present", input != null);

            // If remaining already 0 after day rollover, skip probe and rely on exhausted UX check.
            var remainBefore = day2?.event_budget != null ? day2.event_budget.remaining : -1;
            if (remainBefore == 0)
            {
                Note("day2 budget remaining=0 — skipping second dialogue probe; UX covered by exhausted check");
                yield return Capture("10-day2-health.png");
                yield break;
            }

            ClickFirstNamedAvatar();
            yield return new WaitForSecondsRealtime(0.7f);
            yield return Capture("10-day2-after-avatar.png");

            var baseRemain = GetLatestView()?.event_budget != null
                ? GetLatestView().event_budget.remaining
                : -1;
            var baseCards = CountAvailableCards(GetLatestView());
            var baseTurns = CountDialogueTurns(GetLatestView());

            if (input != null && !input.interactable)
            {
                Note("day2 InputField not interactable after avatar — documenting health floor only");
                Check("day2 SessionView still healthy when input blocked", GetLatestView() != null);
                Check("day2 FatalOverlay clear when input blocked",
                    !IsOverlayBlocking(FindObjectOfType<SessionFatalOverlay>(true)));
                CheckLocationHudMatchesView(GetLatestView());
                yield return Capture("11-day2-input-blocked.png");
                yield break;
            }

            SendDialogueLine();
            var cardOrBudget = false;
            var deadline = Time.realtimeSinceStartup + DialogueWaitSec;
            while (Time.realtimeSinceStartup < deadline)
            {
                var v = GetLatestView();
                var remain = v?.event_budget != null ? v.event_budget.remaining : -1;
                if ((baseRemain >= 0 && remain >= 0 && remain < baseRemain) ||
                    CountAvailableCards(v) > baseCards)
                {
                    cardOrBudget = true;
                    break;
                }

                if (IsCommandFeedbackPending())
                {
                    yield return new WaitForSecondsRealtime(0.5f);
                    continue;
                }

                yield return new WaitForSecondsRealtime(1f);
            }

            yield return new WaitForSecondsRealtime(0.35f);
            yield return Capture("11-day2-after-dialogue.png");

            var after = GetLatestView();
            var afterRemain = after?.event_budget != null ? after.event_budget.remaining : -1;
            var afterCards = CountAvailableCards(after);
            var afterTurns = CountDialogueTurns(after);
            Note(
                $"day2 dialogue result cardOrBudget={cardOrBudget} " +
                $"budget {baseRemain}→{afterRemain} cards {baseCards}→{afterCards} " +
                $"turns {baseTurns}→{afterTurns}");

            Check("day2 SessionView present after probe", after != null);
            Check("day2 FatalOverlay still clear",
                !IsOverlayBlocking(FindObjectOfType<SessionFatalOverlay>(true)));
            Check("day2 CommandFeedback not pending", !IsCommandFeedbackPending());
            CheckLocationHudMatchesView(after);

            if (cardOrBudget ||
                (baseRemain >= 0 && afterRemain >= 0 && afterRemain < baseRemain) ||
                afterCards > baseCards)
            {
                Check("day2 dialogue spent EventBudget or published EventCard", true);
                Check("day2 dialogue produced turns", afterTurns > baseTurns);
            }
            else
            {
                Note(
                    "day2 dialogue did not spend budget/card within " + DialogueWaitSec +
                    "s — director may block or Engine rejected; asserting SessionView health floor");
                // Soft floor: do not fail Accept solely on day2 model/director latency.
                Check("day2 SessionView health floor after failed probe", after != null);
                Check("day2 health floor: InputBar still present",
                    FindObjectOfType<DialogueFeaturePanel>(true) != null &&
                    GetSerialized<CanvasGroup>(
                        FindObjectOfType<DialogueFeaturePanel>(true),
                        "inputBarGroup") != null);
                Check("day2 health floor: location label still from view",
                    !string.IsNullOrWhiteSpace(LoreQuery.ResolveLocationLabel(after)));
            }
        }

        private void CheckLocationHudMatchesView(SessionViewDto view)
        {
            var expected = LoreQuery.ResolveLocationLabel(view);
            var widget = FindObjectOfType<LocationDayWidget>(true);
            var hud = GetSerialized<Text>(widget, "locationText");
            if (hud == null)
            {
                Check("day2 LocationDayWidget locationText present", false);
                return;
            }

            var shown = hud.text ?? string.Empty;
            Check(
                "day2 LocationDayWidget matches SessionView location label",
                !string.IsNullOrWhiteSpace(expected) &&
                string.Equals(shown, expected, StringComparison.Ordinal));
            Note($"location hud={shown} view={expected}");
        }

        /// <summary>
        /// After a successful dialogue with remaining&gt;0, loop content-neutral probes
        /// (same greeting) while rotating named avatars until remaining==0 or capacity+2 attempts.
        /// Capacity and spend amounts come from SessionView.event_budget (pack-owned);
        /// accept only asserts relational outcomes (remaining decreases / hits 0), never literal 4/1.
        /// </summary>
        private IEnumerator DrainBudgetUntilExhausted()
        {
            yield return DismissBlockingOverlays();
            FindObjectOfType<MainWorldScreen>()?.ActivateFeature(DialogueFeaturePanel.Id);
            yield return new WaitForSecondsRealtime(0.25f);

            var view = GetLatestView();
            var remain = view?.event_budget != null ? view.event_budget.remaining : -1;
            // Pack-owned capacity from view — never hardcode Guyandu/Riverside daily_capacity.
            var capacity = view?.event_budget != null ? view.event_budget.capacity : -1;
            if (remain < 0 || capacity < 0)
            {
                Check("drained event_budget to remaining=0", false);
                Note("budget drain aborted: event_budget missing");
                yield break;
            }

            if (remain == 0)
            {
                Note("budget already remaining=0 after first dialogue — drain skipped");
                Check("drained event_budget to remaining=0", true);
                yield break;
            }

            var maxAttempts = capacity + 2;
            var attempts = 0;
            var avatarCursor = 0;
            Note(
                $"budget drain start remaining={remain}/{capacity} maxAttempts={maxAttempts}");

            while (remain > 0 && attempts < maxAttempts)
            {
                attempts++;
                yield return DismissBlockingOverlays();
                FindObjectOfType<MainWorldScreen>()?.ActivateFeature(DialogueFeaturePanel.Id);
                yield return new WaitForSecondsRealtime(0.2f);

                if (!TryClickNamedAvatarAt(ref avatarCursor))
                {
                    Note($"drain attempt {attempts}: no named avatar — aborting drain");
                    break;
                }

                yield return new WaitForSecondsRealtime(0.55f);

                var dialogue = FindObjectOfType<DialogueFeaturePanel>(true);
                var input = GetSerialized<InputField>(dialogue, "inputField");
                var send = GetSerialized<Button>(dialogue, "sendButton");
                if (input == null || send == null || !input.interactable || !send.interactable)
                {
                    Note(
                        $"drain attempt {attempts}: send controls blocked " +
                        $"(input={(input != null && input.interactable)} " +
                        $"send={(send != null && send.interactable)}) — rotate avatar");
                    continue;
                }

                var baseRemain = remain;
                var baseCards = CountAvailableCards(GetLatestView());
                SendDialogueLineQuiet();

                var spent = false;
                var deadline = Time.realtimeSinceStartup + DialogueWaitSec;
                while (Time.realtimeSinceStartup < deadline)
                {
                    if (IsCommandFeedbackPending())
                    {
                        yield return new WaitForSecondsRealtime(0.5f);
                        continue;
                    }

                    var v = GetLatestView();
                    var r = v?.event_budget != null ? v.event_budget.remaining : -1;
                    if (r >= 0 && r < baseRemain)
                    {
                        spent = true;
                        remain = r;
                        break;
                    }

                    // Card publish without budget drop is not progress for drain.
                    if (CountAvailableCards(v) > baseCards)
                    {
                        Note(
                            $"drain attempt {attempts}: card published without budget drop " +
                            $"(remain still {r})");
                    }

                    yield return new WaitForSecondsRealtime(1f);
                }

                remain = GetLatestView()?.event_budget != null
                    ? GetLatestView().event_budget.remaining
                    : remain;
                Note(
                    $"drain attempt {attempts}/{maxAttempts} spent={spent} " +
                    $"remaining={remain}/{capacity}");

                if (!spent && remain > 0)
                {
                    Note($"drain attempt {attempts}: no spend within {DialogueWaitSec}s — next avatar");
                }

                yield return new WaitForSecondsRealtime(0.25f);
            }

            Check("drained event_budget to remaining=0", remain == 0);
            Note(
                $"budget drain done probes={attempts} remaining={remain}/{capacity} " +
                $"(cap={maxAttempts})");
        }

        /// <summary>
        /// Hard assert after drain: 收工 placeholder, blocked input/send, EndDay still usable.
        /// </summary>
        private void AssertBudgetExhaustedUx()
        {
            FindObjectOfType<MainWorldScreen>()?.ActivateFeature(DialogueFeaturePanel.Id);
            var view = GetLatestView();
            var remain = view?.event_budget != null ? view.event_budget.remaining : -1;
            Check("budget-exhausted remaining==0", remain == 0);

            var dialogue = FindObjectOfType<DialogueFeaturePanel>(true);
            var placeholder = GetSerialized<Text>(dialogue, "inputPlaceholder");
            var input = GetSerialized<InputField>(dialogue, "inputField");
            var send = GetSerialized<Button>(dialogue, "sendButton");
            var ph = placeholder != null ? placeholder.text ?? string.Empty : string.Empty;
            Check(
                "budget-exhausted placeholder shows 收工 guidance",
                ph.Contains("收工") || ph.Contains("行动力已尽"));
            Check(
                "budget-exhausted InputField not interactable OR send blocked",
                (input != null && !input.interactable) || (send != null && !send.interactable));
            Check("FatalOverlay clear at budget exhausted",
                !IsOverlayBlocking(FindObjectOfType<SessionFatalOverlay>(true)));
            Check("CommandFeedback not pending at budget exhausted", !IsCommandFeedbackPending());

            var screen = FindObjectOfType<MainWorldScreen>(true);
            var endDay = GetSerialized<Button>(screen, "endDayButton");
            Check("EndDayButton present at budget exhausted", endDay != null);
            Check("EndDayButton interactable at budget exhausted (可收工)",
                endDay != null && endDay.interactable);
            var endDayImg = GetSerialized<Image>(screen, "endDayButtonImage");
            if (endDayImg != null)
            {
                Check(
                    "EndDay primary chrome when remaining=0",
                    endDayImg.color.r > 0.5f && endDayImg.color.g > 0.3f);
            }

            var mapBtn = GetSerialized<Button>(screen, "mapButton");
            Check("map still enabled at budget exhausted", mapBtn != null && mapBtn.interactable);
            Note("budget-exhausted UX at remaining=0 placeholder=" + ph);
        }

        private bool TryClickFirstNonCurrentMapDestination()
        {
            var map = FindObjectOfType<MapDestinationPanel>(true);
            if (map == null || !map.IsOpen)
            {
                Note("map panel not open — cannot click destination");
                return false;
            }

            var listRoot = GetSerialized<Transform>(map, "listRoot");
            if (listRoot == null)
            {
                Note("map listRoot missing — cannot click destination");
                return false;
            }

            // MapDestinationPanel only instantiates non-current destinations into listRoot.
            for (var i = 0; i < listRoot.childCount; i++)
            {
                var child = listRoot.GetChild(i);
                if (child == null || !child.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var btn = child.GetComponent<Button>();
                if (btn == null)
                {
                    btn = child.GetComponentInChildren<Button>(true);
                }

                if (btn == null || !btn.interactable)
                {
                    continue;
                }

                var label = btn.GetComponentInChildren<Text>(true);
                Note("clicking map destination label=" + (label != null ? label.text : child.name));
                btn.onClick.Invoke();
                return true;
            }

            Note("no non-current destination button in map listRoot");
            return false;
        }

        private IEnumerator Capture(string fileName)
        {
            // Avoid WaitForEndOfFrame in batchmode (can hang with no presenter).
            yield return null;

            if (!HasUsableGraphics())
            {
                Note($"capture {fileName}: soft-skip (null graphics / -nographics)");
                yield break;
            }

            var absolutePath = Path.Combine(ArtifactRoot(), fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? ".");
            if (File.Exists(absolutePath))
            {
                try
                {
                    File.Delete(absolutePath);
                }
                catch (IOException)
                {
                }
            }

            var wrote = TryCaptureOverlayUiPng(absolutePath);
            if (wrote && IsBlankPngFile(absolutePath))
            {
                Note($"overlay capture {fileName}: blank — falling back to ScreenCapture");
                try
                {
                    File.Delete(absolutePath);
                }
                catch (IOException)
                {
                }

                wrote = false;
            }

            if (!wrote)
            {
                ScreenCapture.CaptureScreenshot(absolutePath);
                var waitUntil = Time.realtimeSinceStartup + 2.5f;
                while (Time.realtimeSinceStartup < waitUntil && !File.Exists(absolutePath))
                {
                    yield return null;
                }
            }

            _captureAttempts++;
            if (File.Exists(absolutePath))
            {
                var len = new FileInfo(absolutePath).Length;
                var blank = IsBlankPngFile(absolutePath);
                if (blank)
                {
                    _blankCaptures++;
                    File.WriteAllText(
                        absolutePath + ".stamp.txt",
                        $"bytes={len}\nblank=1\n",
                        Encoding.UTF8);
                    Note($"capture {fileName}: blank/camera-clear soft-fail bytes={len}");
                }
                else
                {
                    File.WriteAllText(
                        absolutePath + ".stamp.txt",
                        $"bytes={len}\nblank=0\n",
                        Encoding.UTF8);
                    Note($"captured {fileName} bytes={len}");
                }
            }
            else
            {
                _blankCaptures++;
                File.WriteAllText(
                    absolutePath + ".stamp.txt",
                    "missing png (continuing)\nblank=1\n",
                    Encoding.UTF8);
                Note($"capture {fileName}: missing png (continuing)");
            }
        }

        private static bool TryCaptureOverlayUiPng(string absolutePath)
        {
            var canvases = CollectCaptureRootCanvases();
            if (canvases.Count == 0)
            {
                return false;
            }

            GameObject camGo = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            var restored = new List<CanvasCaptureState>(canvases.Count);
            try
            {
                camGo = new GameObject("PlayAcceptOverlayCaptureCam");
                camGo.hideFlags = HideFlags.HideAndDontSave;
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.orthographicSize = CaptureHeight * 0.5f * 0.01f;
                cam.aspect = (float)CaptureWidth / CaptureHeight;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 1000f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.02f, 0.04f, 0.08f, 1f);
                cam.cullingMask = ~0;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.transform.position = new Vector3(0f, 0f, -10f);
                cam.transform.rotation = Quaternion.identity;

                rt = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
                rt.Create();
                cam.targetTexture = rt;

                for (var i = 0; i < canvases.Count; i++)
                {
                    var canvas = canvases[i];
                    if (canvas == null || !canvas.isActiveAndEnabled)
                    {
                        continue;
                    }

                    if (canvas.renderMode != RenderMode.ScreenSpaceOverlay &&
                        canvas.renderMode != RenderMode.ScreenSpaceCamera)
                    {
                        continue;
                    }

                    restored.Add(new CanvasCaptureState
                    {
                        Canvas = canvas,
                        RenderMode = canvas.renderMode,
                        WorldCamera = canvas.worldCamera,
                        PlaneDistance = canvas.planeDistance
                    });
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = 1f;
                }

                if (restored.Count == 0)
                {
                    return false;
                }

                Canvas.ForceUpdateCanvases();
                cam.Render();

                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
                tex.Apply(false, false);
                RenderTexture.active = prevActive;

                if (IsNearlyUniformTexture(tex))
                {
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? ".");
                var png = tex.EncodeToPNG();
                if (png == null || png.Length == 0)
                {
                    return false;
                }

                File.WriteAllBytes(absolutePath, png);
                return File.Exists(absolutePath) && new FileInfo(absolutePath).Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                for (var i = restored.Count - 1; i >= 0; i--)
                {
                    var state = restored[i];
                    if (state.Canvas == null)
                    {
                        continue;
                    }

                    state.Canvas.renderMode = state.RenderMode;
                    state.Canvas.worldCamera = state.WorldCamera;
                    state.Canvas.planeDistance = state.PlaneDistance;
                }

                if (tex != null)
                {
                    Destroy(tex);
                }

                if (rt != null)
                {
                    if (camGo != null)
                    {
                        var cam = camGo.GetComponent<Camera>();
                        if (cam != null)
                        {
                            cam.targetTexture = null;
                        }
                    }

                    rt.Release();
                    Destroy(rt);
                }

                if (camGo != null)
                {
                    Destroy(camGo);
                }
            }
        }

        private static List<Canvas> CollectCaptureRootCanvases()
        {
            var result = new List<Canvas>();
            var seen = new HashSet<int>();

            void Consider(Canvas canvas)
            {
                if (canvas == null)
                {
                    return;
                }

                var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
                if (root == null || !root.isActiveAndEnabled || !root.isRootCanvas)
                {
                    return;
                }

                if (root.renderMode != RenderMode.ScreenSpaceOverlay &&
                    root.renderMode != RenderMode.ScreenSpaceCamera)
                {
                    return;
                }

                if (!seen.Add(root.GetInstanceID()))
                {
                    return;
                }

                result.Add(root);
            }

            var screen = FindObjectOfType<MainWorldScreen>(true);
            if (screen != null)
            {
                Consider(screen.GetComponent<Canvas>());
                Consider(screen.GetComponentInParent<Canvas>());
                var nested = screen.GetComponentsInChildren<Canvas>(true);
                for (var i = 0; i < nested.Length; i++)
                {
                    Consider(nested[i]);
                }
            }

            var shell = FindObjectOfType<ImmersiveShellController>(true);
            if (shell != null)
            {
                Consider(shell.GetComponentInParent<Canvas>());
                var nested = shell.GetComponentsInChildren<Canvas>(true);
                for (var i = 0; i < nested.Length; i++)
                {
                    Consider(nested[i]);
                }
            }

            if (result.Count == 0)
            {
                var all = FindObjectsOfType<Canvas>();
                for (var i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].isRootCanvas)
                    {
                        Consider(all[i]);
                    }
                }
            }

            return result;
        }

        private static bool IsBlankPngFile(string absolutePath)
        {
            if (!File.Exists(absolutePath))
            {
                return true;
            }

            var len = new FileInfo(absolutePath).Length;
            if (len <= 0)
            {
                return true;
            }

            // Historical blank Camera.main clear-color PNG (~solid dark blue).
            if (len == KnownBlankCameraClearPngBytes)
            {
                return true;
            }

            Texture2D tex = null;
            try
            {
                var bytes = File.ReadAllBytes(absolutePath);
                tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!tex.LoadImage(bytes, false))
                {
                    return true;
                }

                return IsNearlyUniformTexture(tex);
            }
            catch (Exception)
            {
                return true;
            }
            finally
            {
                if (tex != null)
                {
                    Destroy(tex);
                }
            }
        }

        private static bool IsNearlyUniformTexture(Texture2D tex)
        {
            if (tex == null || tex.width <= 0 || tex.height <= 0)
            {
                return true;
            }

            Color32[] pixels;
            try
            {
                pixels = tex.GetPixels32();
            }
            catch (Exception)
            {
                return true;
            }

            if (pixels == null || pixels.Length == 0)
            {
                return true;
            }

            var first = pixels[0];
            var checkedCount = 0;
            var sameCount = 0;
            const int stride = 32;
            for (var i = 0; i < pixels.Length; i += stride)
            {
                checkedCount++;
                var p = pixels[i];
                if (p.r == first.r && p.g == first.g && p.b == first.b)
                {
                    sameCount++;
                }
            }

            return checkedCount > 0 && sameCount >= checkedCount * 0.995f;
        }

        private struct CanvasCaptureState
        {
            public Canvas Canvas;
            public RenderMode RenderMode;
            public Camera WorldCamera;
            public float PlaneDistance;
        }

        private static bool HasUsableGraphics()
        {
            return SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
        }

        private void WriteReportAndClearMarker()
        {
            var root = ArtifactRoot();
            Directory.CreateDirectory(root);
            var pngIndex = new StringBuilder();
            foreach (var path in Directory.GetFiles(root, "*.png"))
            {
                pngIndex.AppendLine($"{Path.GetFileName(path)}\t{new FileInfo(path).Length}");
            }

            File.WriteAllText(Path.Combine(root, "png-index.txt"), pngIndex.ToString(), Encoding.UTF8);
            Note(
                $"capture blank summary: blank={_blankCaptures}/{_captureAttempts}\npng files:\n" +
                pngIndex);

            var text =
                $"[Luoxia] Play Accept: {_passed} passed, {_failed} failed\n{_report}\nNotes:\n{_notes}";
            File.WriteAllText(Path.Combine(root, "report.txt"), text, Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(root, "exit-code.txt"),
                _failed == 0 ? "0" : "1",
                Encoding.UTF8);

            try
            {
                File.Delete(MarkerPath());
            }
            catch (IOException)
            {
            }

            if (_failed == 0)
            {
                Debug.Log(text);
            }
            else
            {
                Debug.LogError(text);
            }
        }

        private void CheckBootChrome()
        {
            Check("SwipeHint absent at runtime", GameObject.Find("SwipeHint") == null);
            var mapChrome = GameObject.Find("MapChrome");
            var mapChromeImg = mapChrome != null ? mapChrome.GetComponent<Image>() : null;
            Check(
                "MapChrome uses panel_minimap (tall chassis)",
                mapChromeImg != null
                && mapChromeImg.sprite != null
                && mapChromeImg.sprite.name.IndexOf("panel_minimap", StringComparison.OrdinalIgnoreCase) >= 0);
            var cloudRing = GameObject.Find("CloudRing");
            Check("CloudRing present (HUD map ring)", cloudRing != null);
            var mapFace = GameObject.Find("MapFace");
            var mapFaceRt = mapFace != null ? mapFace.GetComponent<RectTransform>() : null;
            var minimapRt = GameObject.Find("Minimap") != null
                ? GameObject.Find("Minimap").GetComponent<RectTransform>()
                : null;
            // Face must fill the ring aperture — reject tall preserveAspect strips.
            var faceFillsRing = mapFaceRt != null
                && minimapRt != null
                && minimapRt.rect.width > 1f
                && mapFaceRt.rect.width / minimapRt.rect.width >= 0.65f
                && Mathf.Abs(mapFaceRt.rect.width - mapFaceRt.rect.height) < 2f;
            Check("MapFace fills circular ring aperture", faceFillsRing);
            Check("FeatureChassis present (dialogue panel)", GameObject.Find("FeatureChassis") != null);
            Check("FeaturePagesContent removed", GameObject.Find("FeaturePagesContent") == null);
            Check("EventFeaturePanel removed", GameObject.Find("EventFeaturePanel") == null);
            Check("PanelMist present", GameObject.Find("PanelMist") != null);
            // PendingCardsGroup starts inactive (no available cards) — Find misses inactive nodes.
            var dialogueBoot = FindObjectOfType<DialogueFeaturePanel>(true);
            var pendingBoot = dialogueBoot != null
                ? dialogueBoot.transform.Find("TurnScroll/Viewport/Content/PendingCardsGroup")
                : null;
            Check("PendingCardsGroup present", pendingBoot != null);
            Check("EventCardConfirmPanel present", FindObjectOfType<EventCardConfirmPanel>(true) != null);
            Check("SessionFatalOverlay not blocking boot",
                !IsOverlayBlocking(FindObjectOfType<SessionFatalOverlay>(true)));
            var dock = GameObject.Find("FeatureDock");
            var dockCg = dock != null ? dock.GetComponent<CanvasGroup>() : null;
            var dockRt = dock != null ? dock.GetComponent<RectTransform>() : null;
            Check("FeatureDock collapsed at boot (blocksRaycasts=false)",
                dockCg != null && !dockCg.blocksRaycasts);
            Check("FeatureDock collapsed at boot (anchoredPosition.y < 0)",
                dockRt != null && dockRt.anchoredPosition.y < -1f);
            var dialogue = FindObjectOfType<DialogueFeaturePanel>(true);
            var inputBar = GetSerialized<CanvasGroup>(dialogue, "inputBarGroup");
            Check("dialogue InputBar group present (alpha≈1)", inputBar != null && inputBar.alpha > 0.9f);
            CheckAvatarRailHasNoBlackPlaceholders();
            CheckSceneFollowsLocation("boot");
        }

        /// <summary>
        /// Active AvatarRail chips must never paint solid black placeholder squares.
        /// Missing art leaves the circular mask empty; the chip itself still has a name.
        /// </summary>
        private void CheckAvatarRailHasNoBlackPlaceholders()
        {
            var items = FindObjectsOfType<AvatarRailItemView>(true);
            var active = 0;
            var blackish = 0;
            for (var i = 0; i < items.Length; i++)
            {
                if (!items[i].gameObject.activeInHierarchy)
                {
                    continue;
                }

                active++;
                var portrait = GetSerialized<Image>(items[i], "portraitImage");
                if (portrait == null || !portrait.enabled || portrait.sprite != null)
                {
                    continue;
                }

                // Enabled Image with null sprite + dark tint = the old empty-slot bug.
                if (portrait.color.a > 0.5f &&
                    portrait.color.r < 0.35f &&
                    portrait.color.g < 0.35f &&
                    portrait.color.b < 0.35f)
                {
                    blackish++;
                }
            }

            Check("AvatarRail has no black placeholder chips", blackish == 0);
            Note($"avatar rail active={active} blackish={blackish}");
        }

        /// <summary>
        /// Scene RenderNode must be the current location's scene (subject match).
        /// Structural only — never asserts location names or art content.
        /// </summary>
        private void CheckSceneFollowsLocation(string stage)
        {
            var view = GetLatestView();
            var locId = view?.player_location_entity_id ?? string.Empty;
            var sceneNode = LoreQuery.FindSceneNode(view);
            Check(
                $"{stage}: scene render node follows player location",
                !string.IsNullOrEmpty(locId) &&
                sceneNode != null &&
                sceneNode.subject_entity_id == locId);
            Note(
                $"{stage} scene subject={(sceneNode != null ? sceneNode.subject_entity_id ?? "world" : "none")} " +
                $"player_location={locId}");
        }

        private void ClickFirstNamedAvatar()
        {
            var cursor = 0;
            var clicked = TryClickNamedAvatarAt(ref cursor);
            Check("clicked a named AvatarRail item", clicked);
        }

        /// <summary>
        /// Click a named AvatarRail item by rotating cursor (wrap). Advances cursor on success.
        /// Content-agnostic — does not assert display names.
        /// </summary>
        private bool TryClickNamedAvatarAt(ref int cursor)
        {
            var items = FindObjectsOfType<AvatarRailItemView>(true);
            if (items == null || items.Length == 0)
            {
                return false;
            }

            var named = new List<Button>();
            for (var i = 0; i < items.Length; i++)
            {
                // Pooled list rows survive deactivated with stale names — never click those.
                if (!items[i].gameObject.activeInHierarchy)
                {
                    continue;
                }

                var name = GetSerialized<Text>(items[i], "nameText");
                var btn = GetSerialized<Button>(items[i], "selectButton");
                if (btn == null || name == null || string.IsNullOrWhiteSpace(name.text))
                {
                    continue;
                }

                named.Add(btn);
            }

            if (named.Count == 0)
            {
                return false;
            }

            if (cursor < 0)
            {
                cursor = 0;
            }

            var index = cursor % named.Count;
            var picked = named[index];
            var label = picked.GetComponentInChildren<Text>(true);
            picked.onClick.Invoke();
            cursor = index + 1;
            Note(
                $"selected named avatar index={index}/{named.Count} " +
                $"label={(label != null ? label.text : "?")}");
            return true;
        }

        private void CheckDialogueInputAndPortrait()
        {
            var dock = GameObject.Find("FeatureDock");
            var dockCg = dock != null ? dock.GetComponent<CanvasGroup>() : null;
            var dockRt = dock != null ? dock.GetComponent<RectTransform>() : null;
            Check("FeatureDock expanded after avatar select",
                dockCg != null && dockCg.blocksRaycasts &&
                dockRt != null && Mathf.Abs(dockRt.anchoredPosition.y) < 8f);

            var dialogue = FindObjectOfType<DialogueFeaturePanel>(true);
            var input = GetSerialized<InputField>(dialogue, "inputField");
            var placeholder = GetSerialized<Text>(dialogue, "inputPlaceholder");
            Check("dialogue InputField present", input != null);
            if (input != null)
            {
                Check("inputField.interactable after avatar select", input.interactable);
                Check("inputField fontSize=30",
                    input.textComponent != null &&
                    input.textComponent.fontSize == 30 &&
                    !input.textComponent.resizeTextForBestFit);
            }

            if (placeholder != null)
            {
                var text = placeholder.text ?? string.Empty;
                Check("input placeholder is guidance (not empty dead bar)",
                    !string.IsNullOrWhiteSpace(text) &&
                    (text.Contains("说") || text.Contains("选择交谈")));
                Note("placeholder=" + text);
            }

            var scenePortrait = FindObjectOfType<ScenePortraitLayer>(true);
            var portraitImage = GetSerialized<Image>(scenePortrait, "portraitImage");
            Check("ScenePortraitLayer portrait Image present", portraitImage != null);
            if (portraitImage != null && portraitImage.sprite != null)
            {
                var rect = portraitImage.sprite.rect;
                Check("central portrait is full-body class (h>=512)", rect.height >= 512f);
                Note($"portrait sprite={portraitImage.sprite.name} size={rect.width}x{rect.height}");
            }
            else
            {
                Check("central portrait sprite bound after select", false);
            }
        }

        /// <summary>
        /// Click-to-focus regression guard: the InputField must own a raycastable
        /// Graphic (child texts are raycastTarget=false, so without one taps fall
        /// through), and a delivered pointer click must focus the field.
        /// Screen-point raycasts are unreliable against editor Game view scaling,
        /// so the hit surface is asserted structurally.
        /// </summary>
        private IEnumerator VerifyInputClickFocus()
        {
            var dialogue = FindObjectOfType<DialogueFeaturePanel>(true);
            var input = GetSerialized<InputField>(dialogue, "inputField");
            var eventSystem = EventSystem.current;
            if (input == null || eventSystem == null)
            {
                Check("InputField owns raycastable hit Graphic", false);
                yield break;
            }

            var hitGraphic = input.GetComponent<Graphic>();
            Check(
                "InputField owns raycastable hit Graphic",
                hitGraphic != null && hitGraphic.raycastTarget);

            var pointer = new PointerEventData(eventSystem);
            ExecuteEvents.Execute(
                input.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            // ActivateInputField applies on the following update tick.
            yield return null;
            yield return null;
            Check("InputField focused after click", input.isFocused);
            input.DeactivateInputField();
            yield return null;
        }

        private void CheckAvatarTint()
        {
            var items = FindObjectsOfType<AvatarRailItemView>(true);
            var withSprite = 0;
            var bright = 0;
            for (var i = 0; i < items.Length; i++)
            {
                var portrait = GetSerialized<Image>(items[i], "portraitImage");
                if (portrait == null || portrait.sprite == null)
                {
                    continue;
                }

                withSprite++;
                if (portrait.color.r >= 0.95f && portrait.color.g >= 0.95f && portrait.color.b >= 0.95f)
                {
                    bright++;
                }
            }

            Check("AvatarRail has at least one bound sprite", withSprite > 0);
            Check("bound AvatarRail portraits use white tint (not blackened)",
                withSprite > 0 && bright == withSprite);
            Note($"avatar sprites={withSprite} bright={bright}");
        }

        private void CheckOptimisticSendChrome()
        {
            var dialogue = FindObjectOfType<DialogueFeaturePanel>(true);
            var turnContent = GetSerialized<Transform>(dialogue, "turnContent");
            if (turnContent == null)
            {
                Check("optimistic send shows thinking placeholder", false);
                return;
            }

            var foundThinking = false;
            var texts = turnContent.GetComponentsInChildren<Text>(true);
            for (var i = 0; i < texts.Length; i++)
            {
                var body = texts[i] != null ? texts[i].text ?? string.Empty : string.Empty;
                if (body.Contains("正在思考中"))
                {
                    foundThinking = true;
                    break;
                }
            }

            Check("optimistic send shows thinking placeholder", foundThinking);
        }

        private void SendDialogueLine()
        {
            FindObjectOfType<MainWorldScreen>()?.ActivateFeature(DialogueFeaturePanel.Id);
            var dialogue = FindObjectOfType<DialogueFeaturePanel>(true);
            var input = GetSerialized<InputField>(dialogue, "inputField");
            var send = GetSerialized<Button>(dialogue, "sendButton");
            Check("send controls present", input != null && send != null);
            if (input == null || send == null)
            {
                return;
            }

            // Content-neutral greeting — any NPC can answer; no pack-specific probes.
            input.text = "在下初来乍到，此地可有什么要紧事？";
            Check("inputField interactable before send", input.interactable);
            send.onClick.Invoke();
            Note("sent content-neutral dialogue probe via InputField+SendButton");
        }

        /// <summary>Drain-loop send: same neutral greeting, no Check spam.</summary>
        private void SendDialogueLineQuiet()
        {
            FindObjectOfType<MainWorldScreen>()?.ActivateFeature(DialogueFeaturePanel.Id);
            var dialogue = FindObjectOfType<DialogueFeaturePanel>(true);
            var input = GetSerialized<InputField>(dialogue, "inputField");
            var send = GetSerialized<Button>(dialogue, "sendButton");
            if (input == null || send == null)
            {
                Note("drain send aborted: controls missing");
                return;
            }

            input.text = "在下初来乍到，此地可有什么要紧事？";
            send.onClick.Invoke();
            Note("drain: sent content-neutral dialogue probe");
        }

        private void CheckPendingCardsReveal()
        {
            var dock = GameObject.Find("FeatureDock");
            var dockCg = dock != null ? dock.GetComponent<CanvasGroup>() : null;
            var dockRt = dock != null ? dock.GetComponent<RectTransform>() : null;
            Check("FeatureDock expanded after RevealPendingCards",
                dockCg != null && dockCg.blocksRaycasts &&
                dockRt != null && Mathf.Abs(dockRt.anchoredPosition.y) < 8f);
            var pending = GameObject.Find("PendingCardsGroup");
            Check("PendingCardsGroup active when cards available",
                CountAvailableCards(GetLatestView()) == 0 ||
                (pending != null && pending.activeInHierarchy));
            var dialogue = FindObjectOfType<DialogueFeaturePanel>(true);
            var inputBar = GetSerialized<CanvasGroup>(dialogue, "inputBarGroup");
            Check("InputBar remains visible with pending cards",
                inputBar != null && inputBar.alpha > 0.9f);
            var screen = FindObjectOfType<MainWorldScreen>();
            Check("ActiveFeatureId == dialogue after merge",
                screen != null && screen.ActiveFeatureId == DialogueFeaturePanel.Id);
        }

        private bool TryOpenFirstEventConfirm()
        {
            var items = FindObjectsOfType<EventCardItemView>(true);
            for (var i = 0; i < items.Length; i++)
            {
                if (!items[i].gameObject.activeInHierarchy)
                {
                    continue;
                }

                var open = GetSerialized<Button>(items[i], "openButton");
                if (open == null || !open.interactable)
                {
                    continue;
                }

                open.onClick.Invoke();
                return true;
            }

            return false;
        }

        private bool IsNarrativeOpen()
        {
            var narrative = FindObjectOfType<NarrativeFramePlayer>(true);
            var cg = GetSerialized<CanvasGroup>(narrative, "canvasGroup");
            return cg != null && cg.alpha > 0.2f && cg.blocksRaycasts;
        }

        private IEnumerator DismissNarrativeUntilClosed()
        {
            if (!IsNarrativeOpen())
            {
                yield break;
            }

            var deadline = Time.realtimeSinceStartup + 45f;
            var clicks = 0;
            while (Time.realtimeSinceStartup < deadline && IsNarrativeOpen())
            {
                var narrative = FindObjectOfType<NarrativeFramePlayer>(true);
                var advance = GetSerialized<Button>(narrative, "advanceButton");
                if (advance == null)
                {
                    Note("narrative advance button missing — cannot dismiss");
                    yield break;
                }

                advance.onClick.Invoke();
                clicks++;
                // Fade/page turn uses unscaled fadeSeconds (~0.2); wait without WaitForEndOfFrame.
                yield return new WaitForSecondsRealtime(0.45f);
            }

            Note($"narrative dismiss clicks={clicks} open={IsNarrativeOpen()}");
        }

        private IEnumerator DismissLoreChapterUntilClosed()
        {
            if (!IsLoreChapterOpen())
            {
                yield break;
            }

            var deadline = Time.realtimeSinceStartup + 45f;
            var clicks = 0;
            while (Time.realtimeSinceStartup < deadline && IsLoreChapterOpen())
            {
                var lore = FindObjectOfType<LoreChapterOverlay>(true);
                var advance = GetSerialized<Button>(lore, "advanceButton");
                if (advance == null)
                {
                    Note("lore chapter advance button missing — cannot dismiss");
                    yield break;
                }

                advance.onClick.Invoke();
                clicks++;
                yield return new WaitForSecondsRealtime(0.45f);
            }

            Note($"lore chapter dismiss clicks={clicks} open={IsLoreChapterOpen()}");
        }

        private IEnumerator DismissBlockingOverlays()
        {
            yield return DismissNarrativeUntilClosed();
            yield return DismissLoreChapterUntilClosed();
            yield return DismissNightCurtainUntilClosed();
        }

        private IEnumerator DismissNightCurtainUntilClosed()
        {
            if (!IsNightCurtainOpen())
            {
                yield break;
            }

            var deadline = Time.realtimeSinceStartup + 20f;
            var clicks = 0;
            while (Time.realtimeSinceStartup < deadline && IsNightCurtainOpen())
            {
                var night = FindObjectOfType<NightCurtainOverlay>(true);
                var advance = GetSerialized<Button>(night, "advanceButton");
                if (advance != null)
                {
                    advance.onClick.Invoke();
                    clicks++;
                }
                else
                {
                    night?.ClearAndClose();
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }

            Note($"night curtain dismiss clicks={clicks} open={IsNightCurtainOpen()}");
        }

        private bool IsLoreChapterOpen()
        {
            var lore = FindObjectOfType<LoreChapterOverlay>(true);
            return lore != null && lore.IsOpen;
        }

        private bool IsArrivalOverlayVisible()
        {
            var arrival = FindObjectOfType<ArrivalLoreOverlay>(true);
            return arrival != null && arrival.IsVisible;
        }

        private bool IsNightCurtainOpen()
        {
            var night = FindObjectOfType<NightCurtainOverlay>(true);
            return night != null && night.IsOpen;
        }

        private bool IsCommandFeedbackPending()
        {
            var hud = FindObjectOfType<CommandFeedbackHud>(true);
            if (hud == null)
            {
                return false;
            }

            var cg = GetSerialized<CanvasGroup>(hud, "canvasGroup");
            var text = GetSerialized<Text>(hud, "statusText");
            if (cg == null || cg.alpha < 0.05f)
            {
                return false;
            }

            var body = text != null ? text.text ?? string.Empty : string.Empty;
            return body.Contains("命令发送中");
        }

        private bool IsNarrativeKindChromeChineseOrHidden()
        {
            var narrative = FindObjectOfType<NarrativeFramePlayer>(true);
            var kind = GetSerialized<Text>(narrative, "kindText");
            if (kind == null || !kind.gameObject.activeInHierarchy)
            {
                return true;
            }

            var label = kind.text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(label))
            {
                return true;
            }

            // Closed Chinese chrome only — reject raw schema English kinds.
            // dialogue_quote must never appear (chat stream owns it).
            if (label == "narration" || label == "system" || label == "notice" ||
                label == "dialogue_quote" || label == "对话" || label == "narrative.show")
            {
                return false;
            }

            return label == "旁白" || label == "系统" || label == "提示";
        }

        private static bool IsOverlayBlocking(SessionFatalOverlay overlay)
        {
            if (overlay == null)
            {
                return false;
            }

            var cg = GetSerialized<CanvasGroup>(overlay, "canvasGroup");
            return cg != null && cg.blocksRaycasts && cg.alpha > 0.01f;
        }

        private void CheckMapLabels()
        {
            var map = FindObjectOfType<MapDestinationPanel>(true);
            Check("MapDestinationPanel present", map != null);
            var listRoot = GetSerialized<Transform>(map, "listRoot");
            Check("map listRoot present", listRoot != null);
            if (listRoot == null)
            {
                return;
            }

            var labels = listRoot.GetComponentsInChildren<Text>(true);
            var readable = 0;
            var uuidLike = 0;
            var uuid = new Regex(
                @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$");
            for (var i = 0; i < labels.Length; i++)
            {
                var t = labels[i].text?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(t))
                {
                    continue;
                }

                if (uuid.IsMatch(t))
                {
                    uuidLike++;
                }
                else
                {
                    readable++;
                }
            }

            Check("map destination labels are display names (no UUID rows)",
                readable > 0 && uuidLike == 0);
            Note($"map labels readable={readable} uuidLike={uuidLike}");
        }

        private void Check(string label, bool ok)
        {
            if (ok)
            {
                _passed++;
                _report.AppendLine("PASS  " + label);
            }
            else
            {
                _failed++;
                _report.AppendLine("FAIL  " + label);
            }

            Debug.Log($"[Luoxia][PlayAccept] {(ok ? "PASS" : "FAIL")} {label}");
        }

        private void Note(string note)
        {
            _notes.AppendLine(note);
            Debug.Log("[Luoxia][PlayAccept] " + note);
        }

        private static string MarkerPath() =>
            Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath, MarkerFileName);

        private static string ArtifactRoot() =>
            Path.GetFullPath(Path.Combine(
                Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath,
                ArtifactDir));

        private static SessionViewDto GetLatestView()
        {
            var screen = FindObjectOfType<MainWorldScreen>();
            if (screen == null)
            {
                return null;
            }

            s_latestViewProp ??= typeof(LuoxiaView).GetProperty(
                "LatestView",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return s_latestViewProp?.GetValue(screen) as SessionViewDto;
        }

        private static int CountAvailableCards(SessionViewDto view)
        {
            if (view?.event_cards == null)
            {
                return 0;
            }

            var n = 0;
            for (var i = 0; i < view.event_cards.Count; i++)
            {
                if (view.event_cards[i] != null && view.event_cards[i].IsAvailable)
                {
                    n++;
                }
            }

            return n;
        }

        private static int CountDialogueTurns(SessionViewDto view)
        {
            if (view?.dialogues == null)
            {
                return 0;
            }

            var n = 0;
            for (var i = 0; i < view.dialogues.Count; i++)
            {
                var d = view.dialogues[i];
                if (d?.turns != null)
                {
                    n += d.turns.Count;
                }
            }

            return n;
        }

        private static T GetSerialized<T>(UnityEngine.Object target, string field) where T : class
        {
            if (target == null)
            {
                return null;
            }

            // Runtime cannot use SerializedObject; use reflection on private fields.
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var type = target.GetType();
            while (type != null)
            {
                var f = type.GetField(field, flags);
                if (f != null)
                {
                    return f.GetValue(target) as T;
                }

                type = type.BaseType;
            }

            return null;
        }
    }
}
