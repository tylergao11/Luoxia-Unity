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

        [MenuItem("Luoxia/UI/Accept Main World Screen")]
        public static void Accept()
        {
            var report = Run();
            if (report.Failed > 0)
            {
                Debug.LogError(report.ToString());
                throw new InvalidOperationException(
                    $"[Luoxia] MainWorld accept failed: {report.Failed} issue(s). See Console.");
            }

            Debug.Log(report.ToString());
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
            Check(report, "eventPanel wired", so.FindProperty("eventPanel")?.objectReferenceValue != null);
            Check(report, "immersiveShell wired", so.FindProperty("immersiveShell")?.objectReferenceValue != null);
            Check(report, "mapDestinationPanel wired", so.FindProperty("mapDestinationPanel")?.objectReferenceValue != null);
            Check(report, "endDayButton wired", so.FindProperty("endDayButton")?.objectReferenceValue != null);
            Check(report, "featurePagesContent wired", so.FindProperty("featurePagesContent")?.objectReferenceValue != null);

            var canvas = screen.GetComponent<Canvas>();
            var scaler = screen.GetComponent<CanvasScaler>();
            Check(report, "Canvas present", canvas != null);
            Check(report, "CanvasScaler Expand",
                scaler != null &&
                scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize &&
                scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand &&
                Mathf.Approximately(scaler.referenceResolution.x, 1080f) &&
                Mathf.Approximately(scaler.referenceResolution.y, 1920f));

            var swipe = UnityEngine.Object.FindObjectOfType<FeatureSwipeNavigator>(true);
            Check(report, "FeatureSwipeNavigator present", swipe != null);

            Check(report, "SwipeHint removed", GameObject.Find("SwipeHint") == null);

            var pages = GameObject.Find("FeaturePagesContent");
            Check(report, "FeaturePagesContent present", pages != null);
            if (pages != null)
            {
                var pagesRt = pages.GetComponent<RectTransform>();
                Check(report, "FeaturePagesContent width 2160",
                    pagesRt != null && Mathf.Approximately(pagesRt.sizeDelta.x, 2160f));
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

            var confirm = UnityEngine.Object.FindObjectOfType<EventCardConfirmPanel>(true);
            Check(report, "EventCardConfirmPanel present", confirm != null);
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
