#if UNITY_EDITOR
using System.IO;
using Luoxia.App;
using Luoxia.UI.Features;
using Luoxia.UI.Immersion;
using Luoxia.UI.Screens;
using Luoxia.UI.Widgets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Luoxia.Editor
{
    /// <summary>
    /// Builds portrait MainWorld UI (1080×1920 Expand) per Fable layout contract.
    /// Menu: Luoxia/UI/Build Main World Screen
    /// Drop an empty file `.luoxia-build-mainworld-request` at the project root to rebuild
    /// while the Editor already has the project open (batchmode cannot share the lock).
    /// </summary>
    public static class MainWorldUiBuilder
    {
        private const string PrefabRoot = "Assets/Prefabs/UI";
        private const string ScenePath = "Assets/Scenes/MainWorld.unity";
        private const string MapArt = "Assets/Art/UI/Map";
        private const string FontPath = "Assets/Art/UI/Fonts/LuoxiaCJKSource.ttf";
        private const string BuildRequestFileName = ".luoxia-build-mainworld-request";
        private const float W = 1080f;
        private const float H = 1920f;

        private static Font s_cjkFont;

        [InitializeOnLoadMethod]
        private static void ConsumeExternalBuildRequest()
        {
            EditorApplication.delayCall += () =>
            {
                var requestPath = Path.Combine(
                    Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty,
                    BuildRequestFileName);
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

                Build();
            };
        }

        [MenuItem("Luoxia/UI/Build Main World Screen")]
        public static void Build()
        {
            EnsureFolders();
            UiMapImportPostprocessor.ReimportAll();
            s_cjkFont = EnsureCjkFont();

            var turnPrefab = BuildDialogueTurnPrefab();
            var eventItemPrefab = BuildEventCardItemPrefab();
            var avatarItemPrefab = BuildAvatarRailItemPrefab();
            var mapDestinationPrefab = BuildMapDestinationItemPrefab();
            var anchorPrefab = BuildAnchorButtonPrefab();

            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EnsureEventSystem();

            var canvasGo = new GameObject("MainWorldCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(W, H);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.anchorMin = Vector2.zero;
            canvasRt.anchorMax = Vector2.one;
            canvasRt.offsetMin = Vector2.zero;
            canvasRt.offsetMax = Vector2.zero;

            var screen = canvasGo.AddComponent<MainWorldScreen>();
            var bootstrap = canvasGo.AddComponent<LuoxiaClientBootstrap>();
            canvasGo.AddComponent<PortraitScreenPolicy>();

            var designRoot = Create("DesignRoot", canvasGo.transform);
            var designRt = designRoot.GetComponent<RectTransform>();
            designRt.anchorMin = new Vector2(0.5f, 0.5f);
            designRt.anchorMax = new Vector2(0.5f, 0.5f);
            designRt.pivot = new Vector2(0.5f, 0.5f);
            designRt.sizeDelta = new Vector2(W, H);
            designRt.anchoredPosition = Vector2.zero;
            var designParent = designRoot.transform;

            // ── 1 SceneLayer ────────────────────────────────────────────────
            var sceneLayer = Create("SceneLayer", designParent);
            Stretch(sceneLayer);
            var sceneMask = Create("SceneMask", sceneLayer.transform);
            Stretch(sceneMask);
            sceneMask.AddComponent<RectMask2D>();

            var sceneImageGo = Create("SceneImage", sceneMask.transform);
            Stretch(sceneImageGo);
            var sceneImg = AddImage(sceneImageGo, null, Image.Type.Simple, Color.white);
            sceneImg.preserveAspect = true;
            sceneImg.raycastTarget = false;
            sceneImg.enabled = false;
            var sceneFitter = sceneImageGo.AddComponent<AspectRatioFitter>();
            sceneFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            sceneFitter.aspectRatio = W / H;

            var portraitGo = Create("Portrait", sceneLayer.transform);
            // Shrunk hot zone (mid character area).
            SetAnchors(portraitGo, 0.30f, 0.42f, 0.70f, 0.78f);
            var portraitImg = AddImage(portraitGo, null, Image.Type.Simple, Color.white);
            portraitImg.preserveAspect = true;
            portraitImg.raycastTarget = false;
            portraitImg.enabled = false;
            var portraitBtn = portraitGo.AddComponent<Button>();
            portraitBtn.targetGraphic = portraitImg;
            portraitBtn.interactable = false;

            var sceneFade = sceneLayer.AddComponent<CanvasGroup>();
            sceneFade.alpha = 1f;

            var scenePortrait = sceneLayer.AddComponent<ScenePortraitLayer>();
            Assign(scenePortrait, "sceneImage", sceneImg);
            Assign(scenePortrait, "sceneAspectFitter", sceneFitter);
            Assign(scenePortrait, "portraitImage", portraitImg);
            Assign(scenePortrait, "portraitButton", portraitBtn);
            Assign(scenePortrait, "layerGroup", sceneFade);
            // background.png disabled as scene / fallbackScene
            var assetErr = CreateUiText("AssetError", sceneLayer.transform, string.Empty, 22, FontStyle.Normal, TextAnchor.MiddleCenter);
            SetAnchors(assetErr.gameObject, 0.1f, 0.48f, 0.9f, 0.56f);
            assetErr.color = new Color(1f, 0.35f, 0.35f, 0.95f);
            assetErr.gameObject.SetActive(false);
            Assign(scenePortrait, "assetErrorText", assetErr);

            // ── 2 BottomShell ───────────────────────────────────────────────
            var bottomShell = Create("BottomShell", designParent);
            SetAnchors(bottomShell, 0f, 0f, 1f, 0.52f);
            bottomShell.AddComponent<RectMask2D>();

            var bottomGrad = Create("BottomGradient", bottomShell.transform);
            Stretch(bottomGrad);
            var gradImg = AddImage(bottomGrad, Map("panel_bottom_gradient_9slice.png"), Image.Type.Sliced, Color.white);
            gradImg.raycastTarget = false;

            var mist = Create("DialogueMist", bottomShell.transform);
            var mistRt = mist.GetComponent<RectTransform>();
            mistRt.anchorMin = new Vector2(0.5f, 0f);
            mistRt.anchorMax = new Vector2(0.5f, 0f);
            mistRt.pivot = new Vector2(0.5f, 0f);
            mistRt.sizeDelta = new Vector2(1080f, 1200f);
            mistRt.anchoredPosition = Vector2.zero;
            var mistImg = AddImage(mist, Map("deco_dialogue_mist.png"), Image.Type.Simple, new Color(1f, 1f, 1f, 0.95f));
            mistImg.raycastTarget = false;
            mistImg.preserveAspect = true;

            var lotus = Create("LotusWater", bottomShell.transform);
            SetAnchors(lotus, 0.05f, 0.0f, 0.95f, 0.22f);
            var lotusImg = AddImage(lotus, Map("deco_bottom_lotus_water.png"), Image.Type.Simple, Color.white);
            lotusImg.preserveAspect = true;
            lotusImg.raycastTarget = false;

            var sparkle = Create("Sparkle", bottomShell.transform);
            SetAnchors(sparkle, 0.42f, 0.14f, 0.58f, 0.24f);
            var sparkleImg = AddImage(sparkle, Map("deco_sparkle_gold.png"), Image.Type.Simple, Color.white);
            sparkleImg.preserveAspect = true;
            sparkleImg.raycastTarget = false;

            // ── 3 FeatureDock ───────────────────────────────────────────────
            var featureDock = Create("FeatureDock", designParent);
            Stretch(featureDock);

            // Gesture zone: full width y≈150–990 (excludes InputBar)
            var gestureZone = Create("GestureZone", featureDock.transform);
            SetRectBL(gestureZone, 0f, 150f, W, 840f);
            var gestureImg = AddImage(gestureZone, null, Image.Type.Simple, new Color(1f, 1f, 1f, 0f));
            gestureImg.raycastTarget = true;
            var swipeNav = gestureZone.AddComponent<FeatureSwipeNavigator>();
            Assign(swipeNav, "screen", screen);

            var tabs = Create("Tabs", featureDock.transform);
            SetRectBL(tabs, 180, 900, 720, 80);

            // Full-width baseline under both tabs.
            var tabBase = Create("TabBaseLine", tabs.transform);
            SetAnchors(tabBase, 0f, 0.05f, 1f, 0.2f);
            var tabBaseImg = AddImage(tabBase, Map("deco_tab_base_line.png"), Image.Type.Sliced, Color.white);
            tabBaseImg.raycastTarget = false;

            var dialogueTab = CreateTab("DialogueTab", tabs.transform, "对话", 0f);
            var eventTab = CreateTab("EventTab", tabs.transform, "事件", 1f);

            var tabActive = Create("TabActiveMarker", dialogueTab.transform);
            SetAnchors(tabActive, 0.15f, 0f, 0.85f, 0.28f);
            var tabActiveImg = AddImage(tabActive, Map("deco_tab_active_marker.png"), Image.Type.Simple, Color.white);
            tabActiveImg.preserveAspect = true;
            tabActiveImg.raycastTarget = false;

            // Horizontal feature pages: dialogue @0, event @1080; content slides 0 ↔ −1080.
            var featurePages = Create("FeaturePages", featureDock.transform);
            SetAnchors(featurePages, 0f, 0.09f, 1f, 0.44f);
            featurePages.AddComponent<RectMask2D>();

            var pagesContent = Create("FeaturePagesContent", featurePages.transform);
            var pagesRt = pagesContent.GetComponent<RectTransform>();
            pagesRt.anchorMin = new Vector2(0f, 0f);
            pagesRt.anchorMax = new Vector2(0f, 1f);
            pagesRt.pivot = new Vector2(0f, 0.5f);
            pagesRt.sizeDelta = new Vector2(W * 2f, 0f);
            pagesRt.anchoredPosition = Vector2.zero;

            // Dialogue feature panel (page 0)
            var dialoguePanelGo = Create("DialogueFeaturePanel", pagesContent.transform);
            var dialoguePageRt = dialoguePanelGo.GetComponent<RectTransform>();
            dialoguePageRt.anchorMin = new Vector2(0f, 0f);
            dialoguePageRt.anchorMax = new Vector2(0f, 1f);
            dialoguePageRt.pivot = new Vector2(0f, 0.5f);
            dialoguePageRt.sizeDelta = new Vector2(W, 0f);
            dialoguePageRt.anchoredPosition = Vector2.zero;
            var dialogueCg = dialoguePanelGo.AddComponent<CanvasGroup>();
            var dialoguePanel = dialoguePanelGo.AddComponent<DialogueFeaturePanel>();
            Assign(dialoguePanel, "featureId", DialogueFeaturePanel.Id);
            Assign(dialoguePanel, "canvasGroup", dialogueCg);
            Assign(dialoguePanel, "activeRoot", dialoguePanelGo);

            var turnScroll = Create("TurnScroll", dialoguePanelGo.transform);
            SetAnchors(turnScroll, 0f, 0.18f, 1f, 1f);
            var scroll = turnScroll.AddComponent<ScrollRect>();
            var viewport = Create("Viewport", turnScroll.transform);
            Stretch(viewport);
            viewport.AddComponent<RectMask2D>();
            var viewportHit = AddImage(viewport, null, Image.Type.Simple, new Color(1f, 1f, 1f, 0f));
            viewportHit.raycastTarget = true;
            var turnRelay = viewport.AddComponent<DragDirectionRelay>();
            Assign(turnRelay, "scrollRect", scroll);
            Assign(turnRelay, "navigator", swipeNav);
            var turnContent = Create("Content", viewport.transform);
            Stretch(turnContent);
            var vlg = turnContent.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 14f;
            vlg.padding = new RectOffset(8, 8, 8, 8);
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            var fitter = turnContent.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = turnContent.GetComponent<RectTransform>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            // Input bar (dialogue tab only interactable)
            var inputBar = Create("InputBar", featureDock.transform);
            SetRectBL(inputBar, 40, 24, 1000, 120);
            var inputBarCg = inputBar.AddComponent<CanvasGroup>();
            var inputBg = AddImage(inputBar, Map("panel_dialogue_input_9slice.png"), Image.Type.Sliced, Color.white);

            var inputGo = Create("InputField", inputBar.transform);
            SetAnchors(inputGo, 0.04f, 0.15f, 0.78f, 0.85f);
            var inputField = inputGo.AddComponent<InputField>();
            var textArea = Create("Text Area", inputGo.transform);
            Stretch(textArea);
            textArea.AddComponent<RectMask2D>();
            var placeholder = CreateUiText("Placeholder", textArea.transform, string.Empty, 26, FontStyle.Italic, TextAnchor.MiddleLeft);
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);
            Stretch(placeholder.gameObject);
            var inputText = CreateUiText("Text", textArea.transform, string.Empty, 26, FontStyle.Normal, TextAnchor.MiddleLeft);
            inputText.supportRichText = false;
            Stretch(inputText.gameObject);
            inputField.textComponent = inputText;
            inputField.placeholder = placeholder;

            var sendGo = Create("SendButton", inputBar.transform);
            SetAnchors(sendGo, 0.80f, 0.12f, 0.96f, 0.88f);
            var sendImg = AddImage(sendGo, Map("button_dialogue_send.png"), Image.Type.Simple, Color.white);
            sendImg.preserveAspect = true;
            var sendBtn = sendGo.AddComponent<Button>();
            sendBtn.targetGraphic = sendImg;

            Assign(dialoguePanel, "turnPrefab", turnPrefab.GetComponent<DialogueTurnItemView>());
            Assign(dialoguePanel, "turnContent", turnContent.transform);
            Assign(dialoguePanel, "inputField", inputField);
            Assign(dialoguePanel, "sendButton", sendBtn);
            Assign(dialoguePanel, "inputPlaceholder", placeholder);
            Assign(dialoguePanel, "scrollRect", scroll);
            Assign(dialoguePanel, "inputBarGroup", inputBarCg);

            // Event feature panel (page 1 at x=1080)
            var eventPanelGo = Create("EventFeaturePanel", pagesContent.transform);
            var eventPageRt = eventPanelGo.GetComponent<RectTransform>();
            eventPageRt.anchorMin = new Vector2(0f, 0f);
            eventPageRt.anchorMax = new Vector2(0f, 1f);
            eventPageRt.pivot = new Vector2(0f, 0.5f);
            eventPageRt.sizeDelta = new Vector2(W, 0f);
            eventPageRt.anchoredPosition = new Vector2(W, 0f);
            var eventCg = eventPanelGo.AddComponent<CanvasGroup>();
            eventCg.alpha = 1f;
            eventCg.interactable = false;
            eventCg.blocksRaycasts = false;
            var eventPanel = eventPanelGo.AddComponent<EventFeaturePanel>();
            Assign(eventPanel, "featureId", EventFeaturePanel.Id);
            Assign(eventPanel, "canvasGroup", eventCg);
            Assign(eventPanel, "activeRoot", eventPanelGo);

            var eventHeaderIcon = Create("EventHeaderIcon", eventPanelGo.transform);
            SetAnchors(eventHeaderIcon, 0.02f, 0.90f, 0.10f, 0.99f);
            var ehi = AddImage(eventHeaderIcon, Map("deco_event_header.png"), Image.Type.Simple, Color.white);
            ehi.preserveAspect = true;
            ehi.raycastTarget = false;

            var eventHeader = CreateUiText("EventHeader", eventPanelGo.transform, "今日事件", 30, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetAnchors(eventHeader.gameObject, 0.12f, 0.90f, 0.55f, 1f);
            var eventCount = CreateUiText("EventCount", eventPanelGo.transform, "待开启 0 件", 24, FontStyle.Normal, TextAnchor.MiddleRight);
            SetAnchors(eventCount.gameObject, 0.55f, 0.90f, 0.98f, 1f);
            eventCount.color = new Color(1f, 0.88f, 0.55f, 0.95f);

            var eventScroll = Create("EventScroll", eventPanelGo.transform);
            SetAnchors(eventScroll, 0f, 0.16f, 1f, 0.88f);
            var eScroll = eventScroll.AddComponent<ScrollRect>();
            var eViewport = Create("Viewport", eventScroll.transform);
            Stretch(eViewport);
            eViewport.AddComponent<RectMask2D>();
            var eViewportHit = AddImage(eViewport, null, Image.Type.Simple, new Color(1f, 1f, 1f, 0f));
            eViewportHit.raycastTarget = true;
            var eventRelay = eViewport.AddComponent<DragDirectionRelay>();
            Assign(eventRelay, "scrollRect", eScroll);
            Assign(eventRelay, "navigator", swipeNav);
            var eContent = Create("Content", eViewport.transform);
            Stretch(eContent);
            var eVlg = eContent.AddComponent<VerticalLayoutGroup>();
            eVlg.spacing = 10f;
            eVlg.padding = new RectOffset(4, 4, 4, 4);
            eVlg.childControlHeight = false;
            eVlg.childForceExpandHeight = false;
            eVlg.childControlWidth = true;
            eVlg.childForceExpandWidth = true;
            var eFitter = eContent.AddComponent<ContentSizeFitter>();
            eFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            eScroll.viewport = eViewport.GetComponent<RectTransform>();
            eScroll.content = eContent.GetComponent<RectTransform>();
            eScroll.horizontal = false;

            var openAll = Create("OpenAllButton", eventPanelGo.transform);
            SetAnchors(openAll, 0.18f, 0.0f, 0.82f, 0.13f);
            var openAllImg = AddImage(openAll, Map("button_open_all_9slice.png"), Image.Type.Sliced, Color.white);
            var openAllBtn = openAll.AddComponent<Button>();
            openAllBtn.targetGraphic = openAllImg;
            var openAllLabel = CreateUiText("Label", openAll.transform, "全部开启", 30, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(openAllLabel.gameObject);

            Assign(eventPanel, "itemPrefab", eventItemPrefab.GetComponent<EventCardItemView>());
            Assign(eventPanel, "contentRoot", eContent.transform);
            Assign(eventPanel, "headerCountText", eventCount);
            Assign(eventPanel, "openAllButton", openAllBtn);

            // Raise pages / tabs above gesture zone so list rows receive clicks first.
            gestureZone.transform.SetAsFirstSibling();
            featurePages.transform.SetAsLastSibling();
            tabs.transform.SetAsLastSibling();
            inputBar.transform.SetAsLastSibling();

            // ── 4 HudTop ────────────────────────────────────────────────────
            var hudTop = Create("HudTop", designParent);
            Stretch(hudTop);

            var location = Create("LocationDay", hudTop.transform);
            SetRectBL(location, 32, 1780, 380, 100);
            var locTitle = CreateUiText("LocationText", location.transform, string.Empty, 34, FontStyle.Bold, TextAnchor.UpperLeft);
            SetAnchors(locTitle.gameObject, 0f, 0.45f, 1f, 1f);
            var dayText = CreateUiText("DayTimeText", location.transform, string.Empty, 26, FontStyle.Normal, TextAnchor.UpperLeft);
            SetAnchors(dayText.gameObject, 0f, 0f, 0.85f, 0.5f);
            dayText.color = new Color(1f, 0.95f, 0.85f, 0.9f);
            var sun = Create("SunIcon", location.transform);
            SetAnchors(sun, 0.82f, 0.05f, 0.98f, 0.55f);
            var sunImg = AddImage(sun, Map("icon_sun.png"), Image.Type.Simple, Color.white);
            sunImg.preserveAspect = true;
            sunImg.raycastTarget = false;
            var locationWidget = location.AddComponent<LocationDayWidget>();
            Assign(locationWidget, "locationText", locTitle);
            Assign(locationWidget, "dayTimeText", dayText);

            var budget = Create("EventBudget", hudTop.transform);
            SetRectBL(budget, 32, 1712, 320, 56);
            // Neutral empty until first SessionView; capacity/costs are pack-owned, never scaffolded.
            var budgetLabel = CreateUiText("BudgetText", budget.transform, "—", 28, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetAnchors(budgetLabel.gameObject, 0f, 0.15f, 1f, 0.95f);
            var budgetWidget = budget.AddComponent<EventBudgetWidget>();
            Assign(budgetWidget, "budgetText", budgetLabel);

            var minimapRoot = Create("Minimap", hudTop.transform);
            SetRectBL(minimapRoot, 28, 1400, 280, 280);
            var mapFace = Create("MapFace", minimapRoot.transform);
            SetAnchors(mapFace, 0.12f, 0.12f, 0.88f, 0.88f);
            // Compact face chrome — full panel_minimap is reserved for MapDestinationPanel modal.
            var mapFaceImg = AddImage(mapFace, Map("frame_minimap_cloud_ring.png"), Image.Type.Simple, Color.white);
            mapFaceImg.preserveAspect = true;
            mapFaceImg.raycastTarget = true;
            var cloudRing = Create("CloudRing", minimapRoot.transform);
            Stretch(cloudRing);
            var ringImg = AddImage(cloudRing, Map("frame_minimap_cloud_ring.png"), Image.Type.Simple, Color.white);
            ringImg.preserveAspect = true;
            ringImg.raycastTarget = false;
            var mapMarker = Create("MapMarker", minimapRoot.transform);
            SetAnchors(mapMarker, 0.42f, 0.42f, 0.58f, 0.58f);
            var markerImg = AddImage(mapMarker, Map("icon_map_marker.png"), Image.Type.Simple, Color.white);
            markerImg.preserveAspect = true;
            markerImg.raycastTarget = false;
            var mapBtn = minimapRoot.AddComponent<Button>();
            mapBtn.targetGraphic = mapFaceImg;

            var endDayGo = Create("EndDayButton", hudTop.transform);
            SetRectBL(endDayGo, 28, 1330, 280, 60);
            var endDayImg = AddImage(endDayGo, null, Image.Type.Sliced, new Color(0.12f, 0.1f, 0.08f, 0.88f));
            var endDayBtn = endDayGo.AddComponent<Button>();
            endDayBtn.targetGraphic = endDayImg;
            var endDayLabel = CreateUiText("Label", endDayGo.transform, "收工", 28, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(endDayLabel.gameObject);

            var badge = Create("EventBadgeBar", hudTop.transform);
            SetRectBL(badge, 28, 1256, 440, 60);
            var badgeBg = AddImage(badge, null, Image.Type.Simple, new Color(0.05f, 0.04f, 0.06f, 0.72f));
            badgeBg.raycastTarget = true;
            var badgeIcon = Create("BadgeIcon", badge.transform);
            SetAnchors(badgeIcon, 0.02f, 0.15f, 0.14f, 0.85f);
            var badgeIconImg = AddImage(badgeIcon, Map("icon_event_badge.png"), Image.Type.Simple, Color.white);
            badgeIconImg.preserveAspect = true;
            badgeIconImg.raycastTarget = false;
            var badgeLabel = CreateUiText("BadgeText", badge.transform, "今日有0件事待处理", 22, FontStyle.Normal, TextAnchor.MiddleLeft);
            SetAnchors(badgeLabel.gameObject, 0.16f, 0.1f, 0.88f, 0.9f);
            var chevron = Create("Chevron", badge.transform);
            SetAnchors(chevron, 0.88f, 0.25f, 0.98f, 0.75f);
            var chevronImg = AddImage(chevron, Map("icon_chevron_right.png"), Image.Type.Simple, Color.white);
            chevronImg.preserveAspect = true;
            chevronImg.raycastTarget = false;
            var badgeBtn = badge.AddComponent<Button>();
            badgeBtn.targetGraphic = badgeBg;
            var badgeWidget = badge.AddComponent<EventBadgeBar>();
            Assign(badgeWidget, "labelText", badgeLabel);
            Assign(badgeWidget, "openEventsButton", badgeBtn);

            var avatarRail = Create("AvatarRail", hudTop.transform);
            SetRectBL(avatarRail, 420, 1720, 620, 150);
            var avatarContent = Create("Content", avatarRail.transform);
            Stretch(avatarContent);
            var hlg = avatarContent.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 18f;
            hlg.padding = new RectOffset(8, 8, 4, 4);
            hlg.childAlignment = TextAnchor.MiddleRight;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            var avatarWidget = avatarRail.AddComponent<AvatarRailWidget>();
            Assign(avatarWidget, "itemPrefab", avatarItemPrefab.GetComponent<AvatarRailItemView>());
            Assign(avatarWidget, "contentRoot", avatarContent.transform);

            // ── 5 CommandFeedback ───────────────────────────────────────────
            var toastGo = Create("CommandFeedback", designParent);
            SetRectBL(toastGo, 120, 1680, 840, 72);
            var toastCg = toastGo.AddComponent<CanvasGroup>();
            toastCg.alpha = 0f;
            toastCg.blocksRaycasts = false;
            var toastBg = AddImage(toastGo, null, Image.Type.Sliced, new Color(0.05f, 0.04f, 0.06f, 0.88f));
            toastBg.raycastTarget = false;
            var toastText = CreateUiText("Status", toastGo.transform, string.Empty, 26, FontStyle.Normal, TextAnchor.MiddleCenter);
            Stretch(toastText.gameObject);
            toastText.color = new Color(1f, 0.85f, 0.7f, 1f);
            var toast = toastGo.AddComponent<CommandFeedbackHud>();
            Assign(toast, "canvasGroup", toastCg);
            Assign(toast, "statusText", toastText);

            // ── 6 MapDestinationPanel (float; 45–60% scrim; atmosphere stays) ─
            var mapPanelGo = Create("MapDestinationPanel", designParent);
            Stretch(mapPanelGo);
            var mapCg = mapPanelGo.AddComponent<CanvasGroup>();
            mapCg.alpha = 0f;
            mapCg.interactable = false;
            mapCg.blocksRaycasts = false;
            var mapDim = Create("Scrim", mapPanelGo.transform);
            Stretch(mapDim);
            // Fable: 45–60% dim; scrim eats clicks; click dismisses.
            var mapDimImg = AddImage(mapDim, null, Image.Type.Simple, new Color(0f, 0f, 0f, 0.52f));
            mapDimImg.raycastTarget = true;
            var mapScrimBtn = mapDim.AddComponent<Button>();
            mapScrimBtn.targetGraphic = mapDimImg;
            mapScrimBtn.transition = Selectable.Transition.None;
            var mapRoot = Create("Root", mapPanelGo.transform);
            SetAnchors(mapRoot, 0.10f, 0.22f, 0.90f, 0.82f);
            var mapRootImg = AddImage(mapRoot, Map("panel_minimap.png"), Image.Type.Simple, Color.white);
            mapRootImg.preserveAspect = true;
            mapRootImg.raycastTarget = true;
            var mapTitle = CreateUiText("Title", mapRoot.transform, "前往", 32, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetAnchors(mapTitle.gameObject, 0.1f, 0.88f, 0.9f, 0.98f);
            var mapList = Create("List", mapRoot.transform);
            SetAnchors(mapList, 0.10f, 0.16f, 0.90f, 0.86f);
            var mapVlg = mapList.AddComponent<VerticalLayoutGroup>();
            mapVlg.spacing = 10f;
            mapVlg.childControlHeight = false;
            mapVlg.childForceExpandHeight = false;
            mapVlg.childControlWidth = true;
            mapVlg.childForceExpandWidth = true;
            var mapEmpty = CreateUiText("Empty", mapRoot.transform, "无可前往地点", 24, FontStyle.Normal, TextAnchor.MiddleCenter);
            SetAnchors(mapEmpty.gameObject, 0.1f, 0.4f, 0.9f, 0.6f);
            var mapClose = Create("Close", mapRoot.transform);
            SetAnchors(mapClose, 0.35f, 0.04f, 0.65f, 0.12f);
            var mapCloseImg = AddImage(mapClose, null, Image.Type.Simple, new Color(0.2f, 0.16f, 0.12f, 0.95f));
            var mapCloseBtn = mapClose.AddComponent<Button>();
            mapCloseBtn.targetGraphic = mapCloseImg;
            var mapCloseLabel = CreateUiText("Label", mapClose.transform, "关闭", 26, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(mapCloseLabel.gameObject);
            var mapPanel = mapPanelGo.AddComponent<MapDestinationPanel>();
            Assign(mapPanel, "canvasGroup", mapCg);
            Assign(mapPanel, "scrimImage", mapDimImg);
            Assign(mapPanel, "scrimButton", mapScrimBtn);
            Assign(mapPanel, "listRoot", mapList.transform);
            Assign(mapPanel, "closeButton", mapCloseBtn);
            Assign(mapPanel, "emptyHintText", mapEmpty);
            Assign(mapPanel, "destinationButtonPrefab", mapDestinationPrefab.GetComponent<Button>());

            // ── 7 EventCardConfirmPanel (local modal; 稍后 = dismiss only) ───
            var confirmPanel = BuildEventCardConfirmPanel(designParent);
            Assign(eventPanel, "confirmPanel", confirmPanel);

            // ── 7b EndDayConfirmPanel (pending EventCards) ───────────────────
            var endDayConfirm = BuildEndDayConfirmPanel(designParent);

            // ── 8 ImmersiveShell ─────────────────────────────────────────────
            var immersion = BuildImmersionOverlays(designParent, sceneLayer, scenePortrait, anchorPrefab);
            Assign(screen, "immersiveShell", immersion.shell);
            Assign(screen, "dossierPanel", immersion.dossier);
            Assign(screen, "chapterOverlay", immersion.chapter);
            Assign(screen, "narrativeFramePlayer", immersion.narrative);
            Assign(screen, "stageShellOverlay", immersion.stage);

            // ── 9 SessionFatalOverlay ────────────────────────────────────────
            var fatal = BuildFatalOverlay(designParent);

            // Wire MainWorldScreen
            Assign(screen, "locationDayWidget", locationWidget);
            Assign(screen, "eventBudgetWidget", budgetWidget);
            Assign(screen, "eventBadgeBar", badgeWidget);
            Assign(screen, "avatarRailWidget", avatarWidget);
            Assign(screen, "scenePortraitLayer", scenePortrait);
            Assign(screen, "mapButton", mapBtn);
            Assign(screen, "endDayButton", endDayBtn);
            Assign(screen, "endDayButtonImage", endDayImg);
            Assign(screen, "endDayButtonLabel", endDayLabel);
            Assign(screen, "commandFeedback", toast);
            Assign(screen, "fatalOverlay", fatal);
            Assign(screen, "dialogueTabButton", dialogueTab);
            Assign(screen, "eventTabButton", eventTab);
            Assign(screen, "tabActiveMarker", tabActive.GetComponent<RectTransform>());
            Assign(screen, "featurePagesContent", pagesRt);
            Assign(screen, "dialoguePanel", dialoguePanel);
            Assign(screen, "eventPanel", eventPanel);
            Assign(screen, "eventCardConfirmPanel", confirmPanel);
            Assign(screen, "endDayConfirmPanel", endDayConfirm);
            Assign(screen, "defaultFeatureId", DialogueFeaturePanel.Id);
            Assign(screen, "mapDestinationPanel", mapPanel);

            Assign(bootstrap, "mainWorldScreen", screen);
            Assign(bootstrap, "mode", LuoxiaClientBootstrap.SessionSourceMode.EngineWithInitialView);
            Assign(bootstrap, "engineBaseUrl", "http://127.0.0.1:8000");

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            TrimTrailingWhitespace(ScenePath);
            TrimTrailingWhitespace($"{PrefabRoot}/DialogueTurnItem.prefab");
            TrimTrailingWhitespace($"{PrefabRoot}/EventCardItem.prefab");
            TrimTrailingWhitespace($"{PrefabRoot}/AvatarRailItem.prefab");
            TrimTrailingWhitespace($"{PrefabRoot}/MapDestinationItem.prefab");
            TrimTrailingWhitespace($"{PrefabRoot}/InteractionAnchorButton.prefab");
            AssetDatabase.Refresh();
            Debug.Log($"[Luoxia] MainWorld rebuilt (Fable layout) → {ScenePath}");
        }

        private struct ImmersionRefs
        {
            public ImmersiveShellController shell;
            public CharacterDossierPanel dossier;
            public LoreChapterOverlay chapter;
            public ArrivalLoreOverlay arrival;
            public NightCurtainOverlay nightCurtain;
            public NarrativeFramePlayer narrative;
            public StageShellOverlay stage;
        }

        private static ImmersionRefs BuildImmersionOverlays(
            Transform designParent,
            GameObject sceneLayer,
            ScenePortraitLayer scenePortrait,
            GameObject anchorPrefab)
        {
            var shellGo = Create("ImmersiveShell", designParent);
            Stretch(shellGo);
            var shell = shellGo.AddComponent<ImmersiveShellController>();

            var anchorRoot = Create("InteractionAnchors", shellGo.transform);
            Stretch(anchorRoot);

            // Arrival toast — non-modal, on portrait/scene band; no full-screen scrim.
            var arrivalGo = Create("ArrivalLoreOverlay", shellGo.transform);
            Stretch(arrivalGo);
            var arrivalCg = arrivalGo.AddComponent<CanvasGroup>();
            arrivalCg.alpha = 0f;
            arrivalCg.interactable = false;
            arrivalCg.blocksRaycasts = false;
            var arrivalToast = Create("Toast", arrivalGo.transform);
            SetAnchors(arrivalToast, 0.08f, 0.52f, 0.92f, 0.72f);
            var arrivalToastImg = AddImage(arrivalToast, null, Image.Type.Sliced, new Color(0.05f, 0.04f, 0.07f, 0.82f));
            arrivalToastImg.raycastTarget = true;
            var arrivalDismissBtn = arrivalToast.AddComponent<Button>();
            arrivalDismissBtn.targetGraphic = arrivalToastImg;
            arrivalDismissBtn.transition = Selectable.Transition.None;
            var arrivalTitle = CreateUiText("Title", arrivalToast.transform, string.Empty, 28, FontStyle.Bold, TextAnchor.UpperCenter);
            SetAnchors(arrivalTitle.gameObject, 0.06f, 0.62f, 0.94f, 0.94f);
            var arrivalBody = CreateUiText("Body", arrivalToast.transform, string.Empty, 24, FontStyle.Normal, TextAnchor.UpperCenter);
            SetAnchors(arrivalBody.gameObject, 0.06f, 0.08f, 0.94f, 0.60f);
            arrivalBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            arrivalBody.verticalOverflow = VerticalWrapMode.Overflow;
            arrivalTitle.raycastTarget = false;
            arrivalBody.raycastTarget = false;
            var arrival = arrivalGo.AddComponent<ArrivalLoreOverlay>();
            Assign(arrival, "canvasGroup", arrivalCg);
            Assign(arrival, "titleText", arrivalTitle);
            Assign(arrival, "bodyText", arrivalBody);
            Assign(arrival, "dismissButton", arrivalDismissBtn);

            var chapterGo = Create("LoreChapterOverlay", shellGo.transform);
            Stretch(chapterGo);
            var chapterCg = chapterGo.AddComponent<CanvasGroup>();
            chapterCg.alpha = 0f;
            chapterCg.interactable = false;
            chapterCg.blocksRaycasts = false;
            var chapterDim = Create("Dimmer", chapterGo.transform);
            Stretch(chapterDim);
            var chapterDimImg = AddImage(chapterDim, null, Image.Type.Simple, new Color(0f, 0f, 0f, 0.72f));
            chapterDimImg.raycastTarget = true;
            var chapterTitle = CreateUiText("Title", chapterGo.transform, string.Empty, 36, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetAnchors(chapterTitle.gameObject, 0.1f, 0.62f, 0.9f, 0.78f);
            var chapterBody = CreateUiText("Body", chapterGo.transform, string.Empty, 28, FontStyle.Normal, TextAnchor.UpperCenter);
            SetAnchors(chapterBody.gameObject, 0.12f, 0.28f, 0.88f, 0.60f);
            chapterBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            chapterBody.verticalOverflow = VerticalWrapMode.Overflow;
            var chapterAdvance = Create("Advance", chapterGo.transform);
            SetAnchors(chapterAdvance, 0.30f, 0.10f, 0.70f, 0.18f);
            var chapterAdvanceImg = AddImage(chapterAdvance, null, Image.Type.Simple, new Color(0.15f, 0.12f, 0.1f, 0.9f));
            var chapterAdvanceBtn = chapterAdvance.AddComponent<Button>();
            chapterAdvanceBtn.targetGraphic = chapterAdvanceImg;
            var chapterAdvanceLabel = CreateUiText("Label", chapterAdvance.transform, "继续", 28, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(chapterAdvanceLabel.gameObject);
            var chapter = chapterGo.AddComponent<LoreChapterOverlay>();
            Assign(chapter, "canvasGroup", chapterCg);
            Assign(chapter, "dimmer", chapterDimImg);
            Assign(chapter, "titleText", chapterTitle);
            Assign(chapter, "bodyText", chapterBody);
            Assign(chapter, "advanceButton", chapterAdvanceBtn);

            var dossierGo = Create("CharacterDossierPanel", shellGo.transform);
            Stretch(dossierGo);
            var dossierCg = dossierGo.AddComponent<CanvasGroup>();
            dossierCg.alpha = 0f;
            dossierCg.interactable = false;
            dossierCg.blocksRaycasts = false;
            var dossierRoot = Create("Root", dossierGo.transform);
            SetAnchors(dossierRoot, 0.08f, 0.22f, 0.92f, 0.82f);
            var dossierBg = AddImage(dossierRoot, null, Image.Type.Simple, new Color(0.06f, 0.05f, 0.08f, 0.94f));
            dossierBg.raycastTarget = true;
            var dossierName = CreateUiText("Name", dossierRoot.transform, string.Empty, 34, FontStyle.Bold, TextAnchor.UpperLeft);
            SetAnchors(dossierName.gameObject, 0.06f, 0.86f, 0.8f, 0.96f);
            var dossierProfile = CreateUiText("Profile", dossierRoot.transform, string.Empty, 24, FontStyle.Normal, TextAnchor.UpperLeft);
            SetAnchors(dossierProfile.gameObject, 0.06f, 0.48f, 0.94f, 0.84f);
            dossierProfile.horizontalOverflow = HorizontalWrapMode.Wrap;
            dossierProfile.verticalOverflow = VerticalWrapMode.Overflow;
            var dossierHearsay = CreateUiText("Hearsay", dossierRoot.transform, string.Empty, 22, FontStyle.Normal, TextAnchor.UpperLeft);
            SetAnchors(dossierHearsay.gameObject, 0.06f, 0.12f, 0.94f, 0.46f);
            dossierHearsay.horizontalOverflow = HorizontalWrapMode.Wrap;
            dossierHearsay.verticalOverflow = VerticalWrapMode.Overflow;
            var dossierClose = Create("Close", dossierRoot.transform);
            SetAnchors(dossierClose, 0.86f, 0.88f, 0.96f, 0.98f);
            var dossierCloseImg = AddImage(dossierClose, null, Image.Type.Simple, new Color(0.3f, 0.2f, 0.15f, 0.95f));
            var dossierCloseBtn = dossierClose.AddComponent<Button>();
            dossierCloseBtn.targetGraphic = dossierCloseImg;
            var dossier = dossierGo.AddComponent<CharacterDossierPanel>();
            Assign(dossier, "canvasGroup", dossierCg);
            Assign(dossier, "root", dossierRoot);
            Assign(dossier, "nameText", dossierName);
            Assign(dossier, "profileText", dossierProfile);
            Assign(dossier, "hearsayText", dossierHearsay);
            Assign(dossier, "closeButton", dossierCloseBtn);

            // Narrative modal kit
            var narrativeGo = Create("NarrativeFramePlayer", shellGo.transform);
            Stretch(narrativeGo);
            var narrativeCg = narrativeGo.AddComponent<CanvasGroup>();
            narrativeCg.alpha = 0f;
            narrativeCg.interactable = false;
            narrativeCg.blocksRaycasts = false;
            var narrativeDim = Create("Dimmer", narrativeGo.transform);
            Stretch(narrativeDim);
            var narrativeDimImg = AddImage(narrativeDim, null, Image.Type.Simple, new Color(0f, 0f, 0f, 0.78f));
            narrativeDimImg.raycastTarget = true;

            var modal = Create("Modal", narrativeGo.transform);
            SetAnchors(modal, 0.06f, 0.18f, 0.94f, 0.88f);
            var modalImg = AddImage(modal, Map("panel_event_modal_9slice.png"), Image.Type.Sliced, Color.white);
            modalImg.raycastTarget = true;

            var artFade = Create("ArtFade", modal.transform);
            SetAnchors(artFade, 0.04f, 0.42f, 0.96f, 0.96f);
            var artFadeImg = AddImage(artFade, Map("overlay_event_art_fade.png"), Image.Type.Simple, Color.white);
            artFadeImg.preserveAspect = true;
            artFadeImg.raycastTarget = false;

            var titleDeco = Create("TitleDeco", modal.transform);
            SetAnchors(titleDeco, 0.20f, 0.86f, 0.80f, 0.96f);
            var titleDecoImg = AddImage(titleDeco, Map("deco_event_title.png"), Image.Type.Simple, Color.white);
            titleDecoImg.preserveAspect = true;
            titleDecoImg.raycastTarget = false;

            var narrativeKind = CreateUiText("Kind", modal.transform, string.Empty, 22, FontStyle.Normal, TextAnchor.MiddleCenter);
            SetAnchors(narrativeKind.gameObject, 0.12f, 0.80f, 0.88f, 0.88f);
            narrativeKind.color = new Color(1f, 0.9f, 0.7f, 0.85f);

            var divider = Create("Divider", modal.transform);
            SetAnchors(divider, 0.12f, 0.76f, 0.88f, 0.79f);
            var dividerImg = AddImage(divider, Map("deco_event_modal_divider.png"), Image.Type.Simple, Color.white);
            dividerImg.preserveAspect = true;
            dividerImg.raycastTarget = false;

            var narrativeBody = CreateUiText("Body", modal.transform, string.Empty, 30, FontStyle.Normal, TextAnchor.UpperCenter);
            SetAnchors(narrativeBody.gameObject, 0.10f, 0.28f, 0.90f, 0.74f);
            narrativeBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            narrativeBody.verticalOverflow = VerticalWrapMode.Overflow;

            var postpone = Create("PostponeDecor", modal.transform);
            SetAnchors(postpone, 0.08f, 0.08f, 0.28f, 0.18f);
            var postponeImg = AddImage(postpone, Map("deco_event_postpone.png"), Image.Type.Simple, Color.white);
            postponeImg.preserveAspect = true;
            postponeImg.raycastTarget = false;

            var narrativeAdvance = Create("Advance", modal.transform);
            SetAnchors(narrativeAdvance, 0.30f, 0.08f, 0.70f, 0.18f);
            var choiceNormal = Map("button_event_choice_normal_9slice.png");
            var choiceActive = Map("button_event_choice_active_9slice.png");
            var narrativeAdvanceImg = AddImage(narrativeAdvance, choiceActive, Image.Type.Sliced, Color.white);
            var narrativeAdvanceBtn = narrativeAdvance.AddComponent<Button>();
            narrativeAdvanceBtn.targetGraphic = narrativeAdvanceImg;
            narrativeAdvanceBtn.transition = Selectable.Transition.ColorTint;
            var narrativeAdvanceLabel = CreateUiText("Label", narrativeAdvance.transform, "继续", 28, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(narrativeAdvanceLabel.gameObject);

            var narrativeClose = Create("Close", modal.transform);
            SetAnchors(narrativeClose, 0.78f, 0.08f, 0.94f, 0.18f);
            var narrativeCloseImg = AddImage(narrativeClose, Map("button_event_close.png"), Image.Type.Simple, Color.white);
            narrativeCloseImg.preserveAspect = true;
            var narrativeCloseBtn = narrativeClose.AddComponent<Button>();
            narrativeCloseBtn.targetGraphic = narrativeCloseImg;

            var narrative = narrativeGo.AddComponent<NarrativeFramePlayer>();
            Assign(narrative, "canvasGroup", narrativeCg);
            Assign(narrative, "kindText", narrativeKind);
            Assign(narrative, "bodyText", narrativeBody);
            Assign(narrative, "advanceButton", narrativeAdvanceBtn);
            Assign(narrative, "closeButton", narrativeCloseBtn);
            Assign(narrative, "advanceButtonImage", narrativeAdvanceImg);
            Assign(narrative, "choiceNormalSprite", choiceNormal);
            Assign(narrative, "choiceActiveSprite", choiceActive);

            // Night curtain — after Narrative so day-end Host beat sits above narrative.show.
            var nightGo = Create("NightCurtainOverlay", shellGo.transform);
            Stretch(nightGo);
            var nightCg = nightGo.AddComponent<CanvasGroup>();
            nightCg.alpha = 0f;
            nightCg.interactable = false;
            nightCg.blocksRaycasts = false;
            var nightCurtainImgGo = Create("Curtain", nightGo.transform);
            Stretch(nightCurtainImgGo);
            var nightCurtainImg = AddImage(nightCurtainImgGo, null, Image.Type.Simple, new Color(0.02f, 0.02f, 0.08f, 0.94f));
            nightCurtainImg.raycastTarget = true;
            var nightTitle = CreateUiText("Title", nightGo.transform, string.Empty, 36, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetAnchors(nightTitle.gameObject, 0.1f, 0.58f, 0.9f, 0.72f);
            nightTitle.color = new Color(0.85f, 0.88f, 1f, 0.95f);
            var nightBody = CreateUiText("Body", nightGo.transform, string.Empty, 28, FontStyle.Normal, TextAnchor.UpperCenter);
            SetAnchors(nightBody.gameObject, 0.12f, 0.28f, 0.88f, 0.56f);
            nightBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            nightBody.verticalOverflow = VerticalWrapMode.Overflow;
            nightBody.color = new Color(0.9f, 0.92f, 1f, 0.9f);
            var nightAdvance = Create("Advance", nightGo.transform);
            SetAnchors(nightAdvance, 0.30f, 0.10f, 0.70f, 0.18f);
            var nightAdvanceImg = AddImage(nightAdvance, null, Image.Type.Simple, new Color(0.12f, 0.14f, 0.22f, 0.92f));
            var nightAdvanceBtn = nightAdvance.AddComponent<Button>();
            nightAdvanceBtn.targetGraphic = nightAdvanceImg;
            var nightAdvanceLabel = CreateUiText("Label", nightAdvance.transform, "入夜", 28, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(nightAdvanceLabel.gameObject);
            var nightCurtain = nightGo.AddComponent<NightCurtainOverlay>();
            Assign(nightCurtain, "canvasGroup", nightCg);
            Assign(nightCurtain, "curtainImage", nightCurtainImg);
            Assign(nightCurtain, "titleText", nightTitle);
            Assign(nightCurtain, "bodyText", nightBody);
            Assign(nightCurtain, "advanceButton", nightAdvanceBtn);

            var stageGo = Create("StageShellOverlay", shellGo.transform);
            Stretch(stageGo);
            var stageCg = stageGo.AddComponent<CanvasGroup>();
            stageCg.alpha = 0f;
            stageCg.interactable = false;
            stageCg.blocksRaycasts = false;
            var stageDim = Create("Dimmer", stageGo.transform);
            Stretch(stageDim);
            AddImage(stageDim, null, Image.Type.Simple, new Color(0.02f, 0.02f, 0.05f, 0.92f)).raycastTarget = true;
            var stageScene = CreateUiText("SceneId", stageGo.transform, string.Empty, 24, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetAnchors(stageScene.gameObject, 0.1f, 0.78f, 0.9f, 0.88f);
            var stageContext = CreateUiText("Context", stageGo.transform, string.Empty, 26, FontStyle.Normal, TextAnchor.UpperCenter);
            SetAnchors(stageContext.gameObject, 0.1f, 0.25f, 0.9f, 0.75f);
            stageContext.horizontalOverflow = HorizontalWrapMode.Wrap;
            stageContext.verticalOverflow = VerticalWrapMode.Overflow;
            var stageDismiss = Create("Dismiss", stageGo.transform);
            SetAnchors(stageDismiss, 0.35f, 0.04f, 0.65f, 0.10f);
            var stageDismissImg = AddImage(stageDismiss, null, Image.Type.Simple, new Color(0.12f, 0.12f, 0.18f, 0.9f));
            var stageDismissBtn = stageDismiss.AddComponent<Button>();
            stageDismissBtn.targetGraphic = stageDismissImg;
            var stageDismissLabel = CreateUiText("Label", stageDismiss.transform, "关闭", 26, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(stageDismissLabel.gameObject);
            var stageOutcomes = Create("OutcomeButtons", stageGo.transform);
            SetAnchors(stageOutcomes, 0.15f, 0.12f, 0.85f, 0.28f);
            var stageOutVlg = stageOutcomes.AddComponent<VerticalLayoutGroup>();
            stageOutVlg.spacing = 10f;
            stageOutVlg.childControlHeight = false;
            stageOutVlg.childForceExpandHeight = false;
            stageOutVlg.childControlWidth = true;
            stageOutVlg.childForceExpandWidth = true;
            var stageOutcomeHint = CreateUiText("OutcomeHint", stageGo.transform, "等待 Stage 结果选项", 22, FontStyle.Normal, TextAnchor.MiddleCenter);
            SetAnchors(stageOutcomeHint.gameObject, 0.15f, 0.14f, 0.85f, 0.22f);
            stageOutcomeHint.color = new Color(1f, 0.9f, 0.75f, 0.75f);
            var stage = stageGo.AddComponent<StageShellOverlay>();
            Assign(stage, "canvasGroup", stageCg);
            Assign(stage, "sceneIdText", stageScene);
            Assign(stage, "contextText", stageContext);
            Assign(stage, "dismissHintButton", stageDismissBtn);
            Assign(stage, "outcomeButtonRoot", stageOutcomes.transform);
            Assign(stage, "outcomeHintText", stageOutcomeHint);

            var sceneFade = sceneLayer.GetComponent<CanvasGroup>();
            Assign(shell, "arrivalOverlay", arrival);
            Assign(shell, "nightCurtain", nightCurtain);
            Assign(shell, "chapterOverlay", chapter);
            Assign(shell, "dossierPanel", dossier);
            Assign(shell, "narrativeFramePlayer", narrative);
            Assign(shell, "stageShellOverlay", stage);
            Assign(shell, "scenePortraitLayer", scenePortrait);
            Assign(shell, "sceneFadeGroup", sceneFade);
            Assign(shell, "anchorRoot", anchorRoot.GetComponent<RectTransform>());
            Assign(shell, "anchorButtonPrefab", anchorPrefab.GetComponent<Button>());

            return new ImmersionRefs
            {
                shell = shell,
                dossier = dossier,
                chapter = chapter,
                arrival = arrival,
                nightCurtain = nightCurtain,
                narrative = narrative,
                stage = stage
            };
        }

        private static EndDayConfirmPanel BuildEndDayConfirmPanel(Transform designParent)
        {
            var go = Create("EndDayConfirmPanel", designParent);
            Stretch(go);
            var cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;

            var dim = Create("Dimmer", go.transform);
            Stretch(dim);
            var dimImg = AddImage(dim, null, Image.Type.Simple, new Color(0f, 0f, 0f, 0.55f));
            dimImg.raycastTarget = true;

            var modal = Create("Modal", go.transform);
            SetAnchors(modal, 0.08f, 0.28f, 0.92f, 0.78f);
            var modalImg = AddImage(modal, Map("panel_event_modal_9slice.png"), Image.Type.Sliced, Color.white);
            modalImg.raycastTarget = true;

            var close = Create("Close", modal.transform);
            SetAnchors(close, 0.88f, 0.86f, 0.98f, 0.98f);
            var closeImg = AddImage(close, Map("button_event_close.png"), Image.Type.Simple, Color.white);
            closeImg.preserveAspect = true;
            var closeBtn = close.AddComponent<Button>();
            closeBtn.targetGraphic = closeImg;

            var message = CreateUiText("Message", modal.transform, string.Empty, 28, FontStyle.Bold, TextAnchor.UpperCenter);
            SetAnchors(message.gameObject, 0.08f, 0.62f, 0.92f, 0.88f);
            message.horizontalOverflow = HorizontalWrapMode.Wrap;
            message.verticalOverflow = VerticalWrapMode.Overflow;

            var titles = CreateUiText("Titles", modal.transform, string.Empty, 24, FontStyle.Normal, TextAnchor.UpperLeft);
            SetAnchors(titles.gameObject, 0.10f, 0.28f, 0.90f, 0.60f);
            titles.horizontalOverflow = HorizontalWrapMode.Wrap;
            titles.verticalOverflow = VerticalWrapMode.Overflow;

            var goLook = Create("GoLook", modal.transform);
            SetAnchors(goLook, 0.08f, 0.08f, 0.48f, 0.20f);
            var goLookImg = AddImage(goLook, Map("button_event_choice_normal_9slice.png"), Image.Type.Sliced, Color.white);
            var goLookBtn = goLook.AddComponent<Button>();
            goLookBtn.targetGraphic = goLookImg;
            var goLookLabel = CreateUiText("Label", goLook.transform, "去看看", 26, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(goLookLabel.gameObject);

            var forceEnd = Create("ForceEnd", modal.transform);
            SetAnchors(forceEnd, 0.52f, 0.08f, 0.92f, 0.20f);
            var forceEndImg = AddImage(forceEnd, Map("button_event_choice_active_9slice.png"), Image.Type.Sliced, Color.white);
            var forceEndBtn = forceEnd.AddComponent<Button>();
            forceEndBtn.targetGraphic = forceEndImg;
            var forceEndLabel = CreateUiText("Label", forceEnd.transform, "仍要收工", 26, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(forceEndLabel.gameObject);

            var panel = go.AddComponent<EndDayConfirmPanel>();
            Assign(panel, "canvasGroup", cg);
            Assign(panel, "messageText", message);
            Assign(panel, "titlesText", titles);
            Assign(panel, "goLookButton", goLookBtn);
            Assign(panel, "forceEndButton", forceEndBtn);
            Assign(panel, "closeButton", closeBtn);
            return panel;
        }

        private static EventCardConfirmPanel BuildEventCardConfirmPanel(Transform designParent)
        {
            var go = Create("EventCardConfirmPanel", designParent);
            Stretch(go);
            var cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;

            var dim = Create("Dimmer", go.transform);
            Stretch(dim);
            var dimImg = AddImage(dim, null, Image.Type.Simple, new Color(0f, 0f, 0f, 0.60f));
            dimImg.raycastTarget = true;

            var modal = Create("Modal", go.transform);
            SetAnchors(modal, 0.08f, 0.28f, 0.92f, 0.78f);
            var modalImg = AddImage(modal, Map("panel_event_modal_9slice.png"), Image.Type.Sliced, Color.white);
            modalImg.raycastTarget = true;

            var close = Create("Close", modal.transform);
            SetAnchors(close, 0.88f, 0.86f, 0.98f, 0.98f);
            var closeImg = AddImage(close, Map("button_event_close.png"), Image.Type.Simple, Color.white);
            closeImg.preserveAspect = true;
            var closeBtn = close.AddComponent<Button>();
            closeBtn.targetGraphic = closeImg;

            var titleDeco = Create("TitleDeco", modal.transform);
            SetAnchors(titleDeco, 0.18f, 0.86f, 0.82f, 0.96f);
            var titleDecoImg = AddImage(titleDeco, Map("deco_event_title.png"), Image.Type.Simple, Color.white);
            titleDecoImg.preserveAspect = true;
            titleDecoImg.raycastTarget = false;

            var ring = Create("PortraitRing", modal.transform);
            SetAnchors(ring, 0.36f, 0.58f, 0.64f, 0.86f);
            var ringImg = AddImage(ring, Map("frame_event_portrait.png"), Image.Type.Simple, Color.white);
            ringImg.preserveAspect = true;
            ringImg.raycastTarget = false;

            var portrait = Create("Portrait", ring.transform);
            SetAnchors(portrait, 0.12f, 0.12f, 0.88f, 0.88f);
            var portraitImg = AddImage(portrait, null, Image.Type.Simple, Color.white);
            portraitImg.preserveAspect = true;
            portraitImg.raycastTarget = false;
            portraitImg.enabled = false;

            var title = CreateUiText("Title", modal.transform, string.Empty, 32, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetAnchors(title.gameObject, 0.10f, 0.48f, 0.90f, 0.58f);

            var summary = CreateUiText("Summary", modal.transform, string.Empty, 24, FontStyle.Normal, TextAnchor.UpperCenter);
            SetAnchors(summary.gameObject, 0.10f, 0.30f, 0.90f, 0.48f);
            summary.horizontalOverflow = HorizontalWrapMode.Wrap;
            summary.verticalOverflow = VerticalWrapMode.Truncate;
            summary.color = new Color(1f, 1f, 1f, 0.88f);

            var cost = CreateUiText("Cost", modal.transform, string.Empty, 22, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetAnchors(cost.gameObject, 0.10f, 0.22f, 0.90f, 0.30f);
            cost.color = new Color(1f, 0.82f, 0.45f, 0.95f);

            var choiceNormal = Map("button_event_choice_normal_9slice.png");
            var choiceActive = Map("button_event_choice_active_9slice.png");

            var later = Create("LaterButton", modal.transform);
            SetAnchors(later, 0.08f, 0.06f, 0.46f, 0.18f);
            var laterImg = AddImage(later, choiceNormal, Image.Type.Sliced, Color.white);
            var laterBtn = later.AddComponent<Button>();
            laterBtn.targetGraphic = laterImg;
            laterBtn.transition = Selectable.Transition.ColorTint;
            var laterLabel = CreateUiText("Label", later.transform, "稍后", 28, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(laterLabel.gameObject);

            var open = Create("OpenButton", modal.transform);
            SetAnchors(open, 0.54f, 0.06f, 0.92f, 0.18f);
            var openImg = AddImage(open, choiceActive, Image.Type.Sliced, Color.white);
            var openBtn = open.AddComponent<Button>();
            openBtn.targetGraphic = openImg;
            openBtn.transition = Selectable.Transition.ColorTint;
            var openLabel = CreateUiText("Label", open.transform, "开启", 28, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(openLabel.gameObject);

            var panel = go.AddComponent<EventCardConfirmPanel>();
            Assign(panel, "canvasGroup", cg);
            Assign(panel, "titleText", title);
            Assign(panel, "summaryText", summary);
            Assign(panel, "costText", cost);
            Assign(panel, "portraitImage", portraitImg);
            Assign(panel, "portraitRingImage", ringImg);
            Assign(panel, "laterButton", laterBtn);
            Assign(panel, "openButton", openBtn);
            Assign(panel, "closeButton", closeBtn);
            Assign(panel, "laterButtonImage", laterImg);
            Assign(panel, "openButtonImage", openImg);
            Assign(panel, "choiceNormalSprite", choiceNormal);
            Assign(panel, "choiceActiveSprite", choiceActive);
            return panel;
        }

        private static SessionFatalOverlay BuildFatalOverlay(Transform designParent)
        {
            var go = Create("SessionFatalOverlay", designParent);
            Stretch(go);
            var bg = AddImage(go, null, Image.Type.Simple, new Color(0.04f, 0.03f, 0.05f, 0.92f));
            bg.raycastTarget = true;
            var cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;

            var title = CreateUiText("Title", go.transform, "会话中断", 40, FontStyle.Bold, TextAnchor.MiddleCenter);
            SetAnchors(title.gameObject, 0.1f, 0.62f, 0.9f, 0.72f);
            title.color = new Color(1f, 0.9f, 0.82f, 1f);

            var detail = CreateUiText("Detail", go.transform, string.Empty, 26, FontStyle.Normal, TextAnchor.UpperCenter);
            SetAnchors(detail.gameObject, 0.12f, 0.38f, 0.88f, 0.6f);
            detail.color = new Color(0.92f, 0.86f, 0.78f, 1f);
            detail.horizontalOverflow = HorizontalWrapMode.Wrap;
            detail.verticalOverflow = VerticalWrapMode.Overflow;

            var retry = Create("FatalRetry", go.transform);
            SetAnchors(retry, 0.28f, 0.18f, 0.48f, 0.26f);
            var retryImg = AddImage(retry, null, Image.Type.Simple, new Color(0.18f, 0.14f, 0.1f, 0.95f));
            var retryBtn = retry.AddComponent<Button>();
            retryBtn.targetGraphic = retryImg;
            var retryLabel = CreateUiText("Label", retry.transform, "重试", 28, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(retryLabel.gameObject);

            var dismiss = Create("FatalDismiss", go.transform);
            SetAnchors(dismiss, 0.52f, 0.18f, 0.72f, 0.26f);
            var dismissImg = AddImage(dismiss, null, Image.Type.Simple, new Color(0.18f, 0.14f, 0.1f, 0.95f));
            var dismissBtn = dismiss.AddComponent<Button>();
            dismissBtn.targetGraphic = dismissImg;
            var dismissLabel = CreateUiText("Label", dismiss.transform, "关闭", 28, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(dismissLabel.gameObject);

            var overlay = go.AddComponent<SessionFatalOverlay>();
            Assign(overlay, "canvasGroup", cg);
            Assign(overlay, "titleText", title);
            Assign(overlay, "detailText", detail);
            Assign(overlay, "retryButton", retryBtn);
            Assign(overlay, "dismissButton", dismissBtn);
            return overlay;
        }

        private static GameObject BuildDialogueTurnPrefab()
        {
            var root = Create("DialogueTurnItem", null);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(980, 150);
            var le = root.AddComponent<LayoutElement>();
            le.minHeight = 130f;
            le.preferredHeight = 150f;

            var other = Create("OtherRoot", root.transform);
            Stretch(other);
            var otherAvatar = Create("OtherAvatar", other.transform);
            SetAnchors(otherAvatar, 0.0f, 0.25f, 0.12f, 0.95f);
            var otherAvImg = AddImage(otherAvatar, Map("frame_event_portrait.png"), Image.Type.Simple, Color.white);
            otherAvImg.preserveAspect = true;
            otherAvImg.raycastTarget = false;
            var otherBubble = Create("OtherBubble", other.transform);
            SetAnchors(otherBubble, 0.13f, 0.05f, 0.92f, 0.95f);
            var otherBubbleBg = AddImage(otherBubble, null, Image.Type.Sliced, new Color(0.08f, 0.07f, 0.1f, 0.82f));
            otherBubbleBg.raycastTarget = false;
            var otherName = CreateUiText("OtherName", otherBubble.transform, string.Empty, 22, FontStyle.Bold, TextAnchor.UpperLeft);
            SetAnchors(otherName.gameObject, 0.04f, 0.62f, 0.96f, 0.95f);
            otherName.color = new Color(1f, 0.9f, 0.65f, 1f);
            var otherBody = CreateUiText("OtherBody", otherBubble.transform, "……", 24, FontStyle.Normal, TextAnchor.UpperLeft);
            SetAnchors(otherBody.gameObject, 0.04f, 0.08f, 0.96f, 0.62f);
            otherBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            otherBody.verticalOverflow = VerticalWrapMode.Truncate;

            var player = Create("PlayerRoot", root.transform);
            Stretch(player);
            player.SetActive(false);
            var playerAvatar = Create("PlayerAvatar", player.transform);
            SetAnchors(playerAvatar, 0.88f, 0.25f, 1f, 0.95f);
            var playerAvImg = AddImage(playerAvatar, Map("frame_event_portrait.png"), Image.Type.Simple, Color.white);
            playerAvImg.preserveAspect = true;
            playerAvImg.raycastTarget = false;
            var playerBubble = Create("PlayerBubble", player.transform);
            SetAnchors(playerBubble, 0.08f, 0.05f, 0.87f, 0.95f);
            var playerBubbleBg = AddImage(playerBubble, null, Image.Type.Sliced, new Color(0.12f, 0.1f, 0.08f, 0.82f));
            playerBubbleBg.raycastTarget = false;
            var playerName = CreateUiText("PlayerName", playerBubble.transform, string.Empty, 22, FontStyle.Bold, TextAnchor.UpperRight);
            SetAnchors(playerName.gameObject, 0.04f, 0.62f, 0.96f, 0.95f);
            playerName.color = new Color(1f, 0.9f, 0.65f, 1f);
            var playerBody = CreateUiText("PlayerBody", playerBubble.transform, "……", 24, FontStyle.Normal, TextAnchor.UpperRight);
            SetAnchors(playerBody.gameObject, 0.04f, 0.08f, 0.96f, 0.62f);
            playerBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            playerBody.verticalOverflow = VerticalWrapMode.Truncate;

            var view = root.AddComponent<DialogueTurnItemView>();
            Assign(view, "playerRoot", player);
            Assign(view, "otherRoot", other);
            Assign(view, "playerNameText", playerName);
            Assign(view, "playerBodyText", playerBody);
            Assign(view, "otherNameText", otherName);
            Assign(view, "otherBodyText", otherBody);
            Assign(view, "playerPortrait", playerAvImg);
            Assign(view, "otherPortrait", otherAvImg);

            return SavePrefab(root, $"{PrefabRoot}/DialogueTurnItem.prefab");
        }

        private static GameObject BuildEventCardItemPrefab()
        {
            var root = Create("EventCardItem", null);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(1000, 140);
            var le = root.AddComponent<LayoutElement>();
            le.minHeight = 130f;
            le.preferredHeight = 140f;

            var bg = AddImage(root, null, Image.Type.Sliced, new Color(0.06f, 0.05f, 0.08f, 0.88f));
            bg.raycastTarget = true;
            var rowBtn = root.AddComponent<Button>();
            rowBtn.targetGraphic = bg;
            rowBtn.transition = Selectable.Transition.ColorTint;

            var sep = Create("Separator", root.transform);
            SetAnchors(sep, 0.02f, 0.0f, 0.98f, 0.04f);
            var sepImg = AddImage(sep, Map("deco_event_separator_9slice.png"), Image.Type.Sliced, Color.white);
            sepImg.raycastTarget = false;

            var portrait = Create("Portrait", root.transform);
            SetAnchors(portrait, 0.02f, 0.12f, 0.14f, 0.88f);
            var portraitImg = AddImage(portrait, Map("frame_event_portrait.png"), Image.Type.Simple, Color.white);
            portraitImg.preserveAspect = true;
            portraitImg.raycastTarget = false;

            var chatBadge = Create("ChatBadge", root.transform);
            SetAnchors(chatBadge, 0.11f, 0.62f, 0.17f, 0.90f);
            var chatBadgeImg = AddImage(chatBadge, Map("icon_event_chat_badge.png"), Image.Type.Simple, Color.white);
            chatBadgeImg.preserveAspect = true;
            chatBadgeImg.raycastTarget = false;

            var title = CreateUiText("Title", root.transform, string.Empty, 28, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetAnchors(title.gameObject, 0.18f, 0.52f, 0.68f, 0.92f);

            // Source row omitted — protocol has no event source label.
            var source = CreateUiText("Source", root.transform, string.Empty, 18, FontStyle.Normal, TextAnchor.MiddleLeft);
            SetAnchors(source.gameObject, 0.18f, 0.32f, 0.50f, 0.52f);
            source.gameObject.SetActive(false);

            var cost = CreateUiText("Cost", root.transform, string.Empty, 20, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetAnchors(cost.gameObject, 0.18f, 0.32f, 0.70f, 0.52f);
            cost.color = new Color(1f, 0.82f, 0.45f, 0.95f);

            var summary = CreateUiText("Summary", root.transform, string.Empty, 20, FontStyle.Normal, TextAnchor.UpperLeft);
            SetAnchors(summary.gameObject, 0.18f, 0.06f, 0.70f, 0.34f);
            summary.horizontalOverflow = HorizontalWrapMode.Wrap;
            summary.verticalOverflow = VerticalWrapMode.Truncate;
            summary.color = new Color(1f, 1f, 1f, 0.8f);

            var choiceNormal = Map("button_event_choice_normal_9slice.png");
            var choiceActive = Map("button_event_choice_active_9slice.png");
            var open = Create("OpenButton", root.transform);
            SetAnchors(open, 0.72f, 0.22f, 0.97f, 0.78f);
            var openImg = AddImage(open, choiceActive, Image.Type.Sliced, Color.white);
            var openBtn = open.AddComponent<Button>();
            openBtn.targetGraphic = openImg;
            openBtn.transition = Selectable.Transition.ColorTint;
            var openLabel = CreateUiText("OpenLabel", open.transform, "开启", 26, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(openLabel.gameObject);

            var view = root.AddComponent<EventCardItemView>();
            Assign(view, "titleText", title);
            Assign(view, "summaryText", summary);
            Assign(view, "sourceText", source);
            Assign(view, "costText", cost);
            Assign(view, "portraitImage", portraitImg);
            Assign(view, "chatBadgeImage", chatBadgeImg);
            Assign(view, "openButton", openBtn);
            Assign(view, "rowButton", rowBtn);
            Assign(view, "openButtonImage", openImg);
            Assign(view, "choiceNormalSprite", choiceNormal);
            Assign(view, "choiceActiveSprite", choiceActive);

            return SavePrefab(root, $"{PrefabRoot}/EventCardItem.prefab");
        }

        private static GameObject BuildAvatarRailItemPrefab()
        {
            var root = Create("AvatarRailItem", null);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(120, 140);

            // Root hit target stays transparent so empty/dark avatar display never blacks the Button tint.
            var hitImg = AddImage(root, null, Image.Type.Simple, new Color(1f, 1f, 1f, 0f));
            hitImg.raycastTarget = true;

            var portrait = Create("Portrait", root.transform);
            SetAnchors(portrait, 0.08f, 0.28f, 0.92f, 0.95f);
            var pImg = AddImage(portrait, null, Image.Type.Simple, new Color(0.22f, 0.2f, 0.24f, 1f));
            pImg.preserveAspect = true;
            pImg.raycastTarget = false;

            var frame = Create("SelectedFrame", root.transform);
            SetAnchors(frame, 0f, 0.2f, 1f, 1f);
            var fImg = AddImage(frame, Map("frame_avatar_selected.png"), Image.Type.Simple, Color.white);
            fImg.preserveAspect = true;
            fImg.enabled = false;
            fImg.raycastTarget = false;

            var namePlate = Create("NamePlate", root.transform);
            SetAnchors(namePlate, 0.05f, 0.0f, 0.95f, 0.28f);
            var plateImg = AddImage(namePlate, Map("panel_avatar_name.png"), Image.Type.Sliced, Color.white);
            plateImg.raycastTarget = false;
            var name = CreateUiText("Name", namePlate.transform, string.Empty, 18, FontStyle.Normal, TextAnchor.MiddleCenter);
            Stretch(name.gameObject);

            var notif = Create("NotificationDot", root.transform);
            SetAnchors(notif, 0.72f, 0.78f, 0.92f, 0.96f);
            var notifImg = AddImage(notif, Map("icon_notification_dot.png"), Image.Type.Simple, Color.white);
            notifImg.preserveAspect = true;
            notifImg.enabled = false;
            notifImg.raycastTarget = false;

            var btn = root.AddComponent<Button>();
            btn.targetGraphic = hitImg;

            var view = root.AddComponent<AvatarRailItemView>();
            Assign(view, "portraitImage", pImg);
            Assign(view, "selectedFrame", fImg);
            Assign(view, "notificationDot", notifImg);
            Assign(view, "nameText", name);
            Assign(view, "selectButton", btn);

            return SavePrefab(root, $"{PrefabRoot}/AvatarRailItem.prefab");
        }

        private static GameObject BuildMapDestinationItemPrefab()
        {
            var root = Create("MapDestinationItem", null);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(720, 72);
            var le = root.AddComponent<LayoutElement>();
            le.minHeight = 72f;
            le.preferredHeight = 72f;

            var img = AddImage(root, null, Image.Type.Sliced, new Color(0.12f, 0.1f, 0.08f, 0.92f));
            img.raycastTarget = true;
            var btn = root.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.ColorTint;

            var label = CreateUiText("Label", root.transform, string.Empty, 28, FontStyle.Normal, TextAnchor.MiddleLeft);
            Stretch(label.gameObject);
            var labelRt = label.GetComponent<RectTransform>();
            labelRt.offsetMin = new Vector2(16f, 4f);
            labelRt.offsetMax = new Vector2(-16f, -4f);
            label.color = new Color(1f, 0.95f, 0.85f, 1f);
            label.raycastTarget = false;

            return SavePrefab(root, $"{PrefabRoot}/MapDestinationItem.prefab");
        }

        private static GameObject BuildAnchorButtonPrefab()
        {
            var root = Create("InteractionAnchorButton", null);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(280, 64);
            var img = AddImage(root, Map("panel_avatar_name.png"), Image.Type.Sliced, Color.white);
            img.raycastTarget = true;
            var btn = root.AddComponent<Button>();
            btn.targetGraphic = img;
            var label = CreateUiText("Label", root.transform, string.Empty, 24, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(label.gameObject);
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.32f, 0.48f);
            rt.anchorMax = new Vector2(0.68f, 0.56f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return SavePrefab(root, $"{PrefabRoot}/InteractionAnchorButton.prefab");
        }

        private static Button CreateTab(string name, Transform parent, string label, float index)
        {
            var go = Create(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(index * 0.5f, 0.25f);
            rt.anchorMax = new Vector2(index * 0.5f + 0.5f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var tmp = CreateUiText("Label", go.transform, label, 32, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(tmp.gameObject);
            // Dialogue (index 0) starts active gold; event starts inactive 55% alpha.
            tmp.color = index < 0.5f
                ? new Color(1f, 0.84f, 0.4f, 1f)
                : new Color(1f, 0.95f, 0.85f, 0.55f);
            // Transparent hit target so tab text can receive clicks via Button.
            var hit = AddImage(go, null, Image.Type.Simple, new Color(1f, 1f, 1f, 0f));
            hit.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = hit;
            return btn;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }

            if (!AssetDatabase.IsValidFolder(PrefabRoot))
            {
                AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
            }

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            if (!AssetDatabase.IsValidFolder("Assets/Art"))
            {
                AssetDatabase.CreateFolder("Assets", "Art");
            }

            if (!AssetDatabase.IsValidFolder("Assets/Art/UI"))
            {
                AssetDatabase.CreateFolder("Assets/Art", "UI");
            }

            if (!AssetDatabase.IsValidFolder("Assets/Art/UI/Fonts"))
            {
                AssetDatabase.CreateFolder("Assets/Art/UI", "Fonts");
            }
        }

        private static Font EnsureCjkFont()
        {
            AssetDatabase.ImportAsset(FontPath, ImportAssetOptions.ForceSynchronousImport);
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null)
            {
                Debug.LogWarning("[Luoxia] Missing LuoxiaCJKSource.ttf; Chinese text may not render.");
            }

            return font;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private static GameObject Create(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            return go;
        }

        private static Image AddImage(GameObject go, Sprite sprite, Image.Type type, Color color)
        {
            var img = go.GetComponent<Image>();
            if (img == null)
            {
                img = go.AddComponent<Image>();
            }

            img.sprite = sprite;
            img.type = type;
            img.color = color;
            if (sprite != null && type == Image.Type.Sliced)
            {
                img.pixelsPerUnitMultiplier = 1f;
            }

            return img;
        }

        private static Text CreateUiText(
            string name,
            Transform parent,
            string text,
            int size,
            FontStyle style,
            TextAnchor align)
        {
            var go = Create(name, parent);
            var ui = go.AddComponent<Text>();
            if (s_cjkFont != null)
            {
                ui.font = s_cjkFont;
            }

            ui.text = text;
            ui.fontSize = size;
            ui.fontStyle = style;
            ui.color = Color.white;
            ui.alignment = align;
            ui.raycastTarget = false;
            ui.horizontalOverflow = HorizontalWrapMode.Wrap;
            ui.verticalOverflow = VerticalWrapMode.Truncate;
            ui.resizeTextForBestFit = false;
            return ui;
        }

        private static void Stretch(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        private static void SetAnchors(GameObject go, float xMin, float yMin, float xMax, float yMax)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        private static void SetRectBL(GameObject go, float x, float y, float w, float h)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private static Sprite Map(string fileName) => Sprite($"{MapArt}/{fileName}");

        private static Sprite Sprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

        private static GameObject SavePrefab(GameObject root, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void TrimTrailingWhitespace(string assetPath)
        {
            var full = Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty, assetPath));
            if (!File.Exists(full))
            {
                return;
            }

            var lines = File.ReadAllLines(full);
            var changed = false;
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimEnd();
                if (trimmed != lines[i])
                {
                    lines[i] = trimmed;
                    changed = true;
                }
            }

            if (changed)
            {
                File.WriteAllLines(full, lines);
            }
        }

        private static void Assign(Object target, string fieldName, object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogWarning($"Missing field {target.GetType().Name}.{fieldName}");
                return;
            }

            switch (prop.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    prop.objectReferenceValue = value as Object;
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = value as string;
                    break;
                case SerializedPropertyType.Enum:
                    if (value is System.Enum)
                    {
                        prop.enumValueIndex = System.Convert.ToInt32(value);
                    }

                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = value is bool b && b;
                    break;
                default:
                    Debug.LogWarning($"Unhandled property type {prop.propertyType} for {fieldName}");
                    break;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
