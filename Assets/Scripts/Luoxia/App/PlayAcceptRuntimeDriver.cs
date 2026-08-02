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

            var view0 = GetLatestView();
            var baseRemain = view0?.event_budget != null ? view0.event_budget.remaining : -1;
            var baseCards = CountAvailableCards(view0);
            var baseTurns = CountDialogueTurns(view0);

            SendDialogueLine();
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

            FindObjectOfType<MainWorldScreen>()?.ActivateFeature(EventFeaturePanel.Id);
            yield return new WaitForSecondsRealtime(0.55f);
            yield return Capture("04-event-page.png");
            CheckEventPage();

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
            CheckMapLabels();

            // Map navigation must move location without spending EventBudget/AP.
            yield return PerformMapMoveAndVerify();

            Check(
                "capture not blank camera clear",
                _captureAttempts > 0 && _blankCaptures < _captureAttempts);
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

            var moved = false;
            var deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var v = GetLatestView();
                var locId = v?.player_location_entity_id ?? string.Empty;
                var locLabel = LoreQuery.ResolveLocationLabel(v);
                if ((!string.IsNullOrEmpty(beforeLocId) &&
                     !string.IsNullOrEmpty(locId) &&
                     locId != beforeLocId) ||
                    (!string.IsNullOrEmpty(beforeLocLabel) &&
                     !string.IsNullOrEmpty(locLabel) &&
                     locLabel != beforeLocLabel))
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

            yield return Capture("08-after-map-move.png");
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
            Check("FeaturePagesContent present", GameObject.Find("FeaturePagesContent") != null);
            Check("EventCardConfirmPanel present", FindObjectOfType<EventCardConfirmPanel>(true) != null);
            Check("SessionFatalOverlay not blocking boot",
                !IsOverlayBlocking(FindObjectOfType<SessionFatalOverlay>(true)));
            var dialogue = FindObjectOfType<DialogueFeaturePanel>(true);
            var inputBar = GetSerialized<CanvasGroup>(dialogue, "inputBarGroup");
            Check("dialogue InputBar visible (alpha≈1)", inputBar != null && inputBar.alpha > 0.9f);
        }

        private void ClickFirstNamedAvatar()
        {
            var items = FindObjectsOfType<AvatarRailItemView>(true);
            Button fallback = null;
            Button clicked = null;
            for (var i = 0; i < items.Length; i++)
            {
                var name = GetSerialized<Text>(items[i], "nameText");
                var portrait = GetSerialized<Image>(items[i], "portraitImage");
                var btn = GetSerialized<Button>(items[i], "selectButton");
                if (btn == null || name == null || string.IsNullOrWhiteSpace(name.text))
                {
                    continue;
                }

                fallback ??= btn;
                if (portrait != null && portrait.sprite != null && portrait.color.r > 0.85f)
                {
                    btn.onClick.Invoke();
                    clicked = btn;
                    break;
                }
            }

            if (clicked == null && fallback != null)
            {
                fallback.onClick.Invoke();
                clicked = fallback;
            }

            Check("clicked a named AvatarRail item", clicked != null);
        }

        private void CheckDialogueInputAndPortrait()
        {
            var dialogue = FindObjectOfType<DialogueFeaturePanel>(true);
            var input = GetSerialized<InputField>(dialogue, "inputField");
            var placeholder = GetSerialized<Text>(dialogue, "inputPlaceholder");
            Check("dialogue InputField present", input != null);
            if (input != null)
            {
                Check("inputField.interactable after avatar select", input.interactable);
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

            input.text = "今日可有盐镖消息？";
            Check("inputField interactable before send", input.interactable);
            send.onClick.Invoke();
            Note("sent dialogue line via InputField+SendButton");
        }

        private void CheckEventPage()
        {
            var pages = GameObject.Find("FeaturePagesContent");
            var rt = pages != null ? pages.GetComponent<RectTransform>() : null;
            Check("FeaturePagesContent slid to event (x≈-1080)",
                rt != null && Mathf.Abs(rt.anchoredPosition.x + 1080f) < 8f);
            var dialogue = FindObjectOfType<DialogueFeaturePanel>(true);
            var inputBar = GetSerialized<CanvasGroup>(dialogue, "inputBarGroup");
            Check("InputBar hidden on event page",
                inputBar != null && inputBar.alpha < 0.05f && !inputBar.blocksRaycasts);
            var screen = FindObjectOfType<MainWorldScreen>();
            Check("ActiveFeatureId == event",
                screen != null && screen.ActiveFeatureId == EventFeaturePanel.Id);
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
            if (label == "narration" || label == "system" || label == "notice" ||
                label == "dialogue_quote" || label == "narrative.show")
            {
                return false;
            }

            return label == "旁白" || label == "系统" || label == "提示" || label == "对话";
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
