#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using Luoxia.UI.Core;
using Luoxia.UI.Features;
using Luoxia.UI.Immersion;
using Luoxia.UI.Screens;
using Luoxia.UI.Widgets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Luoxia.Editor
{
    /// <summary>
    /// Structural accept gate for the Fable MainWorld shell (no Play Mode / no Mock).
    /// Menu: Luoxia/UI/Accept Main World Screen
    /// </summary>
    public static class MainWorldUiAccept
    {
        private const string ScenePath = "Assets/Scenes/MainWorld.unity";
        private const string MapDestinationPrefab = "Assets/Prefabs/UI/MapDestinationItem.prefab";
        private const string DialogueTurnPrefab = "Assets/Prefabs/UI/DialogueTurnItem.prefab";
        private const string EventCardPrefab = "Assets/Prefabs/UI/EventCardItem.prefab";
        private const string AvatarRailPrefab = "Assets/Prefabs/UI/AvatarRailItem.prefab";
        private const string AcceptRequestFileName = ".luoxia-accept-mainworld-request";
        private const string ArtifactRelativeDir = "Artifacts/ui-accept";
        private const string ReportFileName = "report.txt";

        [InitializeOnLoadMethod]
        private static void ConsumeExternalAcceptRequest()
        {
            EditorApplication.update -= PollExternalAcceptRequest;
            EditorApplication.update += PollExternalAcceptRequest;
            EditorApplication.delayCall += TryConsumeExternalAcceptRequest;
        }

        private static void PollExternalAcceptRequest()
        {
            TryConsumeExternalAcceptRequest();
        }

        private static void TryConsumeExternalAcceptRequest()
        {
            var requestPath = Path.Combine(
                Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty,
                AcceptRequestFileName);
            if (string.IsNullOrEmpty(requestPath) || !File.Exists(requestPath))
            {
                return;
            }

            try
            {
                File.Delete(requestPath);
            }
            catch (IOException)
            {
                return;
            }

            EditorApplication.update -= PollExternalAcceptRequest;
            try
            {
                Accept();
            }
            finally
            {
                EditorApplication.update += PollExternalAcceptRequest;
            }
        }

        [MenuItem("Luoxia/UI/Accept Main World Screen")]
        public static void Accept()
        {
            var report = Run();
            WriteArtifact(report);
            if (report.Failed > 0)
            {
                Debug.LogError(report.ToString());
                throw new InvalidOperationException(
                    $"[Luoxia] MainWorld accept failed: {report.Failed} issue(s). See Console.");
            }

            Debug.Log(report.ToString());
        }

        private static void WriteArtifact(AcceptReport report)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var dir = Path.Combine(projectRoot, ArtifactRelativeDir);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ReportFileName), report.ToString(), Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(dir, "exit-code.txt"),
                report.Failed == 0 ? "0" : "1",
                Encoding.UTF8);
        }

        public static AcceptReport Run()
        {
            var report = new AcceptReport();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var screen = UnityEngine.Object.FindObjectOfType<MainWorldScreen>(true);
            Check(report, "MainWorldScreen present", screen != null);
            if (screen == null)
            {
                return report;
            }

            var so = new SerializedObject(screen);
            Check(report, "fatalOverlay wired", so.FindProperty("fatalOverlay")?.objectReferenceValue != null);
            Check(report, "eventCardConfirmPanel wired", so.FindProperty("eventCardConfirmPanel")?.objectReferenceValue != null);
            Check(report, "endDayConfirmPanel wired", so.FindProperty("endDayConfirmPanel")?.objectReferenceValue != null);
            Check(report, "dialoguePanel wired", so.FindProperty("dialoguePanel")?.objectReferenceValue != null);
            Check(report, "featureDock wired", so.FindProperty("featureDock")?.objectReferenceValue != null);
            Check(report, "featureDockGroup wired", so.FindProperty("featureDockGroup")?.objectReferenceValue != null);
            Check(report, "immersiveShell wired", so.FindProperty("immersiveShell")?.objectReferenceValue != null);
            Check(report, "mapDestinationPanel wired", so.FindProperty("mapDestinationPanel")?.objectReferenceValue != null);
            Check(report, "endDayButton wired", so.FindProperty("endDayButton")?.objectReferenceValue != null);

            var canvas = screen.GetComponent<Canvas>();
            var scaler = screen.GetComponent<CanvasScaler>();
            Check(report, "Canvas present", canvas != null);
            Check(report, "CanvasScaler Expand",
                scaler != null &&
                scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize &&
                scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand &&
                Mathf.Approximately(scaler.referenceResolution.x, 1080f) &&
                Mathf.Approximately(scaler.referenceResolution.y, 1920f));

            Check(report, "FeatureSwipeNavigator script removed",
                AssetDatabase.LoadAssetAtPath<MonoScript>(
                    "Assets/Scripts/Luoxia/UI/Features/FeatureSwipeNavigator.cs") == null);
            Check(report, "EventFeaturePanel script removed",
                AssetDatabase.LoadAssetAtPath<MonoScript>(
                    "Assets/Scripts/Luoxia/UI/Features/EventFeaturePanel.cs") == null);
            Check(report, "DragDirectionRelay script removed",
                AssetDatabase.LoadAssetAtPath<MonoScript>(
                    "Assets/Scripts/Luoxia/UI/Features/DragDirectionRelay.cs") == null);
            Check(report, "EventFeaturePanel node removed",
                GameObject.Find("EventFeaturePanel") == null);
            Check(report, "Tabs removed", GameObject.Find("Tabs") == null);
            Check(report, "DialogueTab removed", GameObject.Find("DialogueTab") == null);
            Check(report, "EventTab removed", GameObject.Find("EventTab") == null);
            Check(report, "TabBaseLine removed", GameObject.Find("TabBaseLine") == null);
            Check(report, "TabActiveMarker removed", GameObject.Find("TabActiveMarker") == null);
            Check(report, "GestureZone removed", GameObject.Find("GestureZone") == null);
            Check(report, "FeaturePages removed", GameObject.Find("FeaturePages") == null);
            Check(report, "FeaturePagesContent removed", GameObject.Find("FeaturePagesContent") == null);
            Check(report, "SwipeHint removed", GameObject.Find("SwipeHint") == null);

            Check(report, "BottomGradient removed from BottomShell",
                GameObject.Find("BottomGradient") == null);
            Check(report, "DialogueMist removed from BottomShell",
                GameObject.Find("DialogueMist") == null);
            Check(report, "LotusWater present", GameObject.Find("LotusWater") != null);
            Check(report, "Sparkle present", GameObject.Find("Sparkle") != null);

            var chassis = GameObject.Find("FeatureChassis");
            Check(report, "FeatureChassis present", chassis != null);
            if (chassis != null)
            {
                var img = chassis.GetComponent<Image>();
                Check(report, "FeatureChassis uses panel_bottom_gradient_9slice",
                    img != null &&
                    img.sprite != null &&
                    img.sprite.name.IndexOf("panel_bottom_gradient", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    img.type == Image.Type.Sliced);
                var artFade = chassis.transform.Find("ArtFade");
                Check(report, "ArtFade under FeatureChassis", artFade != null);
                if (artFade != null)
                {
                    var fadeImg = artFade.GetComponent<Image>();
                    Check(report, "ArtFade uses overlay_event_art_fade",
                        fadeImg != null &&
                        fadeImg.sprite != null &&
                        fadeImg.sprite.name.IndexOf("overlay_event_art_fade", StringComparison.OrdinalIgnoreCase) >= 0);
                    var fadeRt = artFade.GetComponent<RectTransform>();
                    Check(report, "ArtFade anchors (0.02,0.50)-(0.98,1.0)",
                        fadeRt != null &&
                        Approx(fadeRt.anchorMin, 0.02f, 0.50f) &&
                        Approx(fadeRt.anchorMax, 0.98f, 1f));
                }
            }

            Check(report, "PanelMist present", GameObject.Find("PanelMist") != null);

            var dialogueGo = GameObject.Find("DialogueFeaturePanel");
            Check(report, "DialogueFeaturePanel present", dialogueGo != null);
            if (dialogueGo != null)
            {
                var drt = dialogueGo.GetComponent<RectTransform>();
                Check(report, "DialogueFeaturePanel anchors (0.05,0.16)-(0.95,0.96)",
                    drt != null &&
                    Approx(drt.anchorMin, 0.05f, 0.16f) &&
                    Approx(drt.anchorMax, 0.95f, 0.96f));
                Check(report, "DialogueFeaturePanel parent is FeatureDock",
                    dialogueGo.transform.parent != null &&
                    dialogueGo.transform.parent.name == "FeatureDock");
                Check(report, "TurnsGroup present", dialogueGo.transform.Find("TurnScroll/Viewport/Content/TurnsGroup") != null);
                Check(report, "PendingCardsGroup present",
                    dialogueGo.transform.Find("TurnScroll/Viewport/Content/PendingCardsGroup") != null);
            }

            var dock = GameObject.Find("FeatureDock");
            Check(report, "FeatureDock present", dock != null);
            if (dock != null)
            {
                var dockCg = dock.GetComponent<CanvasGroup>();
                Check(report, "FeatureDock CanvasGroup blocksRaycasts false (collapsed default)",
                    dockCg != null && !dockCg.blocksRaycasts);
                var dockRt = dock.GetComponent<RectTransform>();
                Check(report, "FeatureDock collapsed anchoredPosition.y < 0",
                    dockRt != null && dockRt.anchoredPosition.y < -1f);

                // Sibling order: FeatureChassis → PanelMist → DialogueFeaturePanel → InputBar
                Check(report, "FeatureDock sibling order chassis→mist→dialogue→input",
                    ChildNameAt(dock.transform, 0) == "FeatureChassis" &&
                    ChildNameAt(dock.transform, 1) == "PanelMist" &&
                    ChildNameAt(dock.transform, 2) == "DialogueFeaturePanel" &&
                    ChildNameAt(dock.transform, 3) == "InputBar");
            }

            Check(report, "LayoutSlots.Avatar constant", LayoutSlots.Avatar == "avatar");
            Check(report, "LayoutSlots.DialoguePortrait constant",
                LayoutSlots.DialoguePortrait == "dialogue_portrait");

            var mapPanel = UnityEngine.Object.FindObjectOfType<MapDestinationPanel>(true);
            if (mapPanel != null)
            {
                var mso = new SerializedObject(mapPanel);
                Check(report, "map destinationButtonPrefab wired",
                    mso.FindProperty("destinationButtonPrefab")?.objectReferenceValue != null);
                Check(report, "map scrimButton wired",
                    mso.FindProperty("scrimButton")?.objectReferenceValue != null);
                Check(report, "map scrimImage wired",
                    mso.FindProperty("scrimImage")?.objectReferenceValue != null);
            }
            else
            {
                Check(report, "MapDestinationPanel present", false);
            }

            Check(report, "MapDestinationItem.prefab exists",
                AssetDatabase.LoadAssetAtPath<GameObject>(MapDestinationPrefab) != null);

            CheckFontContract(report, "input Text", FindNamedText("InputBar", "Text"), 30, bestFit: false);
            CheckFontContract(report, "input Placeholder", FindNamedText("InputBar", "Placeholder"), 30, bestFit: false);
            CheckFontContract(report, "badge label", FindNamedText("EventBadgeBar", "BadgeText"), 22, bestFit: false);

            CheckPrefabFonts(report);

            Check(report, "EventCardConfirmPanel present",
                UnityEngine.Object.FindObjectOfType<EventCardConfirmPanel>(true) != null);
            Check(report, "EndDayConfirmPanel present",
                UnityEngine.Object.FindObjectOfType<EndDayConfirmPanel>(true) != null);
            Check(report, "ArrivalLoreOverlay present",
                UnityEngine.Object.FindObjectOfType<ArrivalLoreOverlay>(true) != null);
            Check(report, "NightCurtainOverlay present",
                UnityEngine.Object.FindObjectOfType<NightCurtainOverlay>(true) != null);
            Check(report, "ChromeLayers.MapFloat documented", ChromeLayers.MapFloat == 3);

            var fatal = UnityEngine.Object.FindObjectOfType<SessionFatalOverlay>(true);
            Check(report, "SessionFatalOverlay present", fatal != null);

            var scenePortrait = UnityEngine.Object.FindObjectOfType<ScenePortraitLayer>(true);
            if (scenePortrait != null)
            {
                var spo = new SerializedObject(scenePortrait);
                Check(report, "fallbackScene unset (no baked background)",
                    spo.FindProperty("fallbackScene")?.objectReferenceValue == null);
            }
            else
            {
                Check(report, "ScenePortraitLayer present", false);
            }

            var narrative = UnityEngine.Object.FindObjectOfType<NarrativeFramePlayer>(true);
            Check(report, "NarrativeFramePlayer present", narrative != null);

            Check(report, "no dead chrome names",
                GameObject.Find("AddButton") == null &&
                GameObject.Find("SmileButton") == null &&
                GameObject.Find("MoreButton") == null &&
                GameObject.Find("SettingsButton") == null &&
                GameObject.Find("Weather") == null);

            var sceneText = File.ReadAllText(Path.GetFullPath(ScenePath));
            Check(report, "scene has no background.png path",
                sceneText.IndexOf("background.png", StringComparison.OrdinalIgnoreCase) < 0);

            Check(report, "scene opened", scene.IsValid());
            return report;
        }

        private static void CheckPrefabFonts(AcceptReport report)
        {
            var turn = AssetDatabase.LoadAssetAtPath<GameObject>(DialogueTurnPrefab);
            Check(report, "DialogueTurnItem.prefab exists", turn != null);
            if (turn != null)
            {
                var otherBody = turn.transform.Find("OtherRoot/OtherBubble/OtherBody")?.GetComponent<Text>();
                var playerBody = turn.transform.Find("PlayerRoot/PlayerBubble/PlayerBody")?.GetComponent<Text>();
                CheckFontContract(report, "turn OtherBody", otherBody, 26, bestFit: false, overflow: true);
                CheckFontContract(report, "turn PlayerBody", playerBody, 26, bestFit: false, overflow: true);
                var playerAvatar = turn.transform.Find("PlayerRoot/PlayerAvatar")?.GetComponent<RectTransform>();
                Check(report, "player avatar left-aligned",
                    playerAvatar != null && playerAvatar.anchorMin.x < 0.2f);
            }

            var card = AssetDatabase.LoadAssetAtPath<GameObject>(EventCardPrefab);
            Check(report, "EventCardItem.prefab exists", card != null);
            if (card != null)
            {
                CheckFontContract(report, "card Title", card.transform.Find("Title")?.GetComponent<Text>(), 24, bestFit: false);
                CheckFontContract(report, "card Cost", card.transform.Find("Cost")?.GetComponent<Text>(), 18, bestFit: false);
                CheckFontContract(report, "card Summary", card.transform.Find("Summary")?.GetComponent<Text>(), 18, bestFit: false);
            }

            var avatar = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarRailPrefab);
            Check(report, "AvatarRailItem.prefab exists", avatar != null);
            if (avatar != null)
            {
                CheckFontContract(
                    report,
                    "avatar NamePlate",
                    avatar.transform.Find("NamePlate/Name")?.GetComponent<Text>(),
                    20,
                    bestFit: false);
            }

            var mapItem = AssetDatabase.LoadAssetAtPath<GameObject>(MapDestinationPrefab);
            if (mapItem != null)
            {
                CheckFontContract(
                    report,
                    "map destination Label",
                    mapItem.transform.Find("Label")?.GetComponent<Text>(),
                    26,
                    bestFit: false);
            }
        }

        private static Text FindNamedText(string rootName, string childName)
        {
            var root = GameObject.Find(rootName);
            if (root == null)
            {
                return null;
            }

            var t = root.transform.Find(childName);
            if (t != null)
            {
                return t.GetComponent<Text>();
            }

            var texts = root.GetComponentsInChildren<Text>(true);
            for (var i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i].name == childName)
                {
                    return texts[i];
                }
            }

            return null;
        }

        private static void CheckFontContract(
            AcceptReport report,
            string label,
            Text text,
            int size,
            bool bestFit,
            bool overflow = false)
        {
            Check(report, $"{label} present", text != null);
            if (text == null)
            {
                return;
            }

            Check(report, $"{label} fontSize={size}", text.fontSize == size);
            Check(report, $"{label} BestFit={(bestFit ? "on" : "off")}", text.resizeTextForBestFit == bestFit);
            if (overflow)
            {
                Check(report, $"{label} Overflow", text.verticalOverflow == VerticalWrapMode.Overflow);
            }
        }

        private static bool Approx(Vector2 v, float x, float y) =>
            Mathf.Abs(v.x - x) < 0.001f && Mathf.Abs(v.y - y) < 0.001f;

        private static string ChildNameAt(Transform parent, int index)
        {
            if (parent == null || index < 0 || index >= parent.childCount)
            {
                return null;
            }

            return parent.GetChild(index).name;
        }

        private static void Check(AcceptReport report, string label, bool ok)
        {
            report.Add(label, ok);
        }

        public sealed class AcceptReport
        {
            private readonly StringBuilder _sb = new StringBuilder();
            public int Passed { get; private set; }
            public int Failed { get; private set; }

            public void Add(string label, bool ok)
            {
                if (ok)
                {
                    Passed++;
                    _sb.AppendLine($"PASS  {label}");
                }
                else
                {
                    Failed++;
                    _sb.AppendLine($"FAIL  {label}");
                }
            }

            public override string ToString() =>
                $"[Luoxia] MainWorld accept: {Passed} passed, {Failed} failed\n{_sb}";
        }
    }
}
#endif


