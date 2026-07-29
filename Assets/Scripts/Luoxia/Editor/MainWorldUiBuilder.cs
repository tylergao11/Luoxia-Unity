#if UNITY_EDITOR
using Luoxia.App;
using Luoxia.UI.Features;
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
    /// Builds portrait MainWorld UI (1080×1920) with correct map-slice wiring.
    /// Menu: Luoxia/UI/Build Main World Screen
    /// </summary>
    public static class MainWorldUiBuilder
    {
        private const string PrefabRoot = "Assets/Prefabs/UI";
        private const string ScenePath = "Assets/Scenes/MainWorld.unity";
        private const string MapArt = "Assets/Art/UI/Map";
        private const string FontPath = "Assets/Art/UI/Fonts/LuoxiaCJKSource.ttf";
        private const float W = 1080f;
        private const float H = 1920f;

        private static Font s_cjkFont;

        [MenuItem("Luoxia/UI/Build Main World Screen")]
        public static void Build()
        {
            EnsureFolders();
            UiMapImportPostprocessor.ReimportAll();
            s_cjkFont = EnsureCjkFont();

            var turnPrefab = BuildDialogueTurnPrefab();
            var eventItemPrefab = BuildEventCardItemPrefab();
            var avatarItemPrefab = BuildAvatarRailItemPrefab();

            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EnsureEventSystem();

            var canvasGo = new GameObject("MainWorldCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(W, H);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;

            var canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.anchorMin = Vector2.zero;
            canvasRt.anchorMax = Vector2.one;
            canvasRt.offsetMin = Vector2.zero;
            canvasRt.offsetMax = Vector2.zero;

            var screen = canvasGo.AddComponent<MainWorldScreen>();
            var bootstrap = canvasGo.AddComponent<LuoxiaClientBootstrap>();
            canvasGo.AddComponent<PortraitScreenPolicy>();

            // Fixed design root in reference pixels so layout is independent of Game View size.
            var designRoot = Create("DesignRoot", canvasGo.transform);
            var designRt = designRoot.GetComponent<RectTransform>();
            designRt.anchorMin = new Vector2(0.5f, 0.5f);
            designRt.anchorMax = new Vector2(0.5f, 0.5f);
            designRt.pivot = new Vector2(0.5f, 0.5f);
            designRt.sizeDelta = new Vector2(W, H);
            designRt.anchoredPosition = Vector2.zero;
            var designParent = designRoot.transform;

            // ── Full-screen scene / portrait layer ──────────────────────────
            var sceneLayer = Create("ScenePortraitLayer", designParent);
            Stretch(sceneLayer);
            var sceneImg = AddImage(sceneLayer, Sprite("Assets/Art/UI/background.png"), Image.Type.Simple, Color.white);
            sceneImg.preserveAspect = false; // fill portrait frame
            sceneImg.raycastTarget = false;

            var portraitGo = Create("Portrait", sceneLayer.transform);
            // Mid-upper character area (design: large half-body)
            SetAnchors(portraitGo, 0.08f, 0.30f, 0.92f, 0.90f);
            var portraitImg = AddImage(portraitGo, null, Image.Type.Simple, new Color(1f, 1f, 1f, 0.02f));
            portraitImg.raycastTarget = false;

            var scenePortrait = sceneLayer.AddComponent<ScenePortraitLayer>();
            Assign(scenePortrait, "sceneImage", sceneImg);
            Assign(scenePortrait, "portraitImage", portraitImg);
            Assign(scenePortrait, "fallbackScene", Sprite("Assets/Art/UI/background.png"));

            // ── Top-left location / day ──────────────────────────────────────
            var location = Create("LocationDay", designParent);
            // bottom-left y: near top of 1920 canvas
            SetRectBL(location, 32, 1780, 460, 100);
            var locTitle = CreateUiText("LocationText", location.transform, "烟水渡", 34, FontStyle.Bold, TextAnchor.UpperLeft);
            SetAnchors(locTitle.gameObject, 0f, 0.45f, 1f, 1f);
            var dayText = CreateUiText("DayTimeText", location.transform, "第一日·清晨", 26, FontStyle.Normal, TextAnchor.UpperLeft);
            SetAnchors(dayText.gameObject, 0f, 0f, 0.85f, 0.5f);
            dayText.color = new Color(1f, 0.95f, 0.85f, 0.9f);

            var sun = Create("SunIcon", location.transform);
            SetAnchors(sun, 0.78f, 0.05f, 0.92f, 0.55f);
            var sunImg = AddImage(sun, Map("icon_sun.png"), Image.Type.Simple, Color.white);
            sunImg.preserveAspect = true;
            sunImg.raycastTarget = false;

            var weather = Create("WeatherIcon", location.transform);
            SetAnchors(weather, 0.90f, 0.05f, 1.04f, 0.55f);
            var weatherImg = AddImage(weather, Map("icon_weather.png"), Image.Type.Simple, Color.white);
            weatherImg.preserveAspect = true;
            weatherImg.raycastTarget = false;

            var locationWidget = location.AddComponent<LocationDayWidget>();
            Assign(locationWidget, "locationText", locTitle);
            Assign(locationWidget, "dayTimeText", dayText);

            // ── Minimap: cloud ring + map face (NOT full 1037x1517 as tiny rect) ─
            // panel_minimap is a large art panel; we use cloud ring as chrome and
            // a cropped face of panel_minimap (Simple, preserveAspect) inside.
            var minimapRoot = Create("Minimap", designParent);
            SetRectBL(minimapRoot, 28, 1480, 300, 300);

            var mapFace = Create("MapFace", minimapRoot.transform);
            SetAnchors(mapFace, 0.12f, 0.12f, 0.88f, 0.88f);
            var mapFaceImg = AddImage(mapFace, Map("panel_minimap.png"), Image.Type.Simple, Color.white);
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

            var compass = Create("Compass", designParent);
            SetRectBL(compass, 300, 1840, 48, 48);
            var compassImg = AddImage(compass, Map("icon_compass_target.png"), Image.Type.Simple, Color.white);
            compassImg.preserveAspect = true;
            compassImg.raycastTarget = false;

            // ── AP budget ───────────────────────────────────────────────────
            var budget = Create("EventBudget", designParent);
            SetRectBL(budget, 40, 1400, 320, 56);
            var budgetLabel = CreateUiText("BudgetText", budget.transform, "AP 3/3", 28, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetAnchors(budgetLabel.gameObject, 0f, 0.15f, 0.72f, 0.95f);
            var addBtnGo = Create("AddButton", budget.transform);
            SetAnchors(addBtnGo, 0.78f, 0.1f, 1f, 0.9f);
            var addImg = AddImage(addBtnGo, Map("button_add.png"), Image.Type.Simple, Color.white);
            addImg.preserveAspect = true;
            addBtnGo.AddComponent<Button>().targetGraphic = addImg;
            var budgetWidget = budget.AddComponent<EventBudgetWidget>();
            Assign(budgetWidget, "budgetText", budgetLabel);

            // ── Event badge bar ──────────────────────────────────────────────
            var badge = Create("EventBadgeBar", designParent);
            SetRectBL(badge, 32, 1320, 400, 56);
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

            // ── Avatar rail (top right) ──────────────────────────────────────
            var avatarRail = Create("AvatarRail", designParent);
            SetRectBL(avatarRail, 380, 1720, 600, 150);
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

            // More / settings (top-right)
            var more = Create("MoreButton", designParent);
            SetRectBL(more, W - 88, 1830, 64, 64);
            var moreImg = AddImage(more, Map("button_more.png"), Image.Type.Simple, Color.white);
            moreImg.preserveAspect = true;
            more.AddComponent<Button>().targetGraphic = moreImg;

            var settings = Create("SettingsButton", designParent);
            SetRectBL(settings, W - 88, 1750, 64, 64);
            var settingsImg = AddImage(settings, Map("button_settings.png"), Image.Type.Simple, Color.white);
            settingsImg.preserveAspect = true;
            settings.AddComponent<Button>().targetGraphic = settingsImg;

            // ── Bottom shell: gradient + mist + lotus ────────────────────────
            var bottomShell = Create("BottomShell", designParent);
            SetAnchors(bottomShell, 0f, 0f, 1f, 0.52f);

            var bottomGrad = Create("BottomGradient", bottomShell.transform);
            Stretch(bottomGrad);
            var gradImg = AddImage(bottomGrad, Map("panel_bottom_gradient_9slice.png"), Image.Type.Sliced, Color.white);
            gradImg.raycastTarget = false;

            var mist = Create("DialogueMist", bottomShell.transform);
            Stretch(mist);
            var mistImg = AddImage(mist, Map("deco_dialogue_mist.png"), Image.Type.Simple, new Color(1f, 1f, 1f, 0.95f));
            mistImg.raycastTarget = false;
            mistImg.preserveAspect = false;

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

            // ── Tabs 对话 | 事件 ─────────────────────────────────────────────
            var tabs = Create("Tabs", designParent);
            SetRectBL(tabs, 180, 900, 720, 80);

            var tabBase = Create("TabBaseLine", tabs.transform);
            SetAnchors(tabBase, 0.1f, 0.05f, 0.9f, 0.2f);
            var tabBaseImg = AddImage(tabBase, Map("deco_tab_base_line.png"), Image.Type.Sliced, Color.white);
            tabBaseImg.raycastTarget = false;

            var dialogueTab = CreateTab("DialogueTab", tabs.transform, "对话", 0f);
            var eventTab = CreateTab("EventTab", tabs.transform, "事件", 1f);

            var tabActive = Create("TabActiveMarker", tabs.transform);
            SetAnchors(tabActive, 0.12f, 0f, 0.38f, 0.35f);
            var tabActiveImg = AddImage(tabActive, Map("deco_tab_active_marker.png"), Image.Type.Simple, Color.white);
            tabActiveImg.preserveAspect = true;
            tabActiveImg.raycastTarget = false;

            var swipeHint = Create("SwipeHint", designParent);
            SetRectBL(swipeHint, 300, 850, 480, 36);
            var swipeMarkL = Create("SwipeMarkL", swipeHint.transform);
            SetAnchors(swipeMarkL, 0.05f, 0.15f, 0.14f, 0.85f);
            var swipeMarkLImg = AddImage(swipeMarkL, Map("deco_swipe_hint_mark.png"), Image.Type.Simple, new Color(1f, 0.9f, 0.7f, 0.85f));
            swipeMarkLImg.preserveAspect = true;
            swipeMarkLImg.raycastTarget = false;
            var swipeIcon = Create("SwipeIcon", swipeHint.transform);
            SetAnchors(swipeIcon, 0.15f, 0.1f, 0.28f, 0.9f);
            var swipeIconImg = AddImage(swipeIcon, Map("icon_swipe_horizontal.png"), Image.Type.Simple, new Color(1f, 0.9f, 0.7f, 0.85f));
            swipeIconImg.preserveAspect = true;
            swipeIconImg.raycastTarget = false;
            var swipeText = CreateUiText("SwipeText", swipeHint.transform, "左右滑动可切换", 20, FontStyle.Normal, TextAnchor.MiddleCenter);
            SetAnchors(swipeText.gameObject, 0.28f, 0f, 0.85f, 1f);
            swipeText.color = new Color(1f, 0.9f, 0.75f, 0.75f);
            var swipeMarkR = Create("SwipeMarkR", swipeHint.transform);
            SetAnchors(swipeMarkR, 0.86f, 0.15f, 0.95f, 0.85f);
            var swipeMarkRImg = AddImage(swipeMarkR, Map("deco_swipe_hint_mark.png"), Image.Type.Simple, new Color(1f, 0.9f, 0.7f, 0.85f));
            swipeMarkRImg.preserveAspect = true;
            swipeMarkRImg.raycastTarget = false;

            // ── Dialogue feature panel ───────────────────────────────────────
            var dialoguePanelGo = Create("DialogueFeaturePanel", designParent);
            SetAnchors(dialoguePanelGo, 0.04f, 0.09f, 0.96f, 0.44f);
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

            // Input bar
            var inputBar = Create("InputBar", designParent);
            SetRectBL(inputBar, 40, 24, 1000, 120);
            var inputBg = AddImage(inputBar, Map("panel_dialogue_input_9slice.png"), Image.Type.Sliced, Color.white);

            var inputGo = Create("InputField", inputBar.transform);
            SetAnchors(inputGo, 0.04f, 0.15f, 0.68f, 0.85f);
            var inputField = inputGo.AddComponent<InputField>();
            var textArea = Create("Text Area", inputGo.transform);
            Stretch(textArea);
            textArea.AddComponent<RectMask2D>();
            var placeholder = CreateUiText("Placeholder", textArea.transform, "你想说什么……", 26, FontStyle.Italic, TextAnchor.MiddleLeft);
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);
            Stretch(placeholder.gameObject);
            var inputText = CreateUiText("Text", textArea.transform, string.Empty, 26, FontStyle.Normal, TextAnchor.MiddleLeft);
            inputText.supportRichText = false;
            Stretch(inputText.gameObject);
            inputField.textComponent = inputText;
            inputField.placeholder = placeholder;

            var sendGo = Create("SendButton", inputBar.transform);
            SetAnchors(sendGo, 0.70f, 0.12f, 0.84f, 0.88f);
            var sendImg = AddImage(sendGo, Map("button_dialogue_send.png"), Image.Type.Simple, Color.white);
            sendImg.preserveAspect = true;
            var sendBtn = sendGo.AddComponent<Button>();
            sendBtn.targetGraphic = sendImg;

            var smileGo = Create("SmileButton", inputBar.transform);
            SetAnchors(smileGo, 0.85f, 0.12f, 0.98f, 0.88f);
            var smileImg = AddImage(smileGo, Map("button_smile.png"), Image.Type.Simple, Color.white);
            smileImg.preserveAspect = true;
            smileGo.AddComponent<Button>().targetGraphic = smileImg;

            Assign(dialoguePanel, "turnPrefab", turnPrefab.GetComponent<DialogueTurnItemView>());
            Assign(dialoguePanel, "turnContent", turnContent.transform);
            Assign(dialoguePanel, "inputField", inputField);
            Assign(dialoguePanel, "sendButton", sendBtn);
            Assign(dialoguePanel, "inputPlaceholder", placeholder);
            Assign(dialoguePanel, "scrollRect", scroll);

            // ── Event feature panel ──────────────────────────────────────────
            var eventPanelGo = Create("EventFeaturePanel", designParent);
            SetAnchors(eventPanelGo, 0.04f, 0.09f, 0.96f, 0.44f);
            var eventCg = eventPanelGo.AddComponent<CanvasGroup>();
            eventCg.alpha = 0f;
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

            // ── Wire MainWorldScreen ─────────────────────────────────────────
            Assign(screen, "locationDayWidget", locationWidget);
            Assign(screen, "eventBudgetWidget", budgetWidget);
            Assign(screen, "eventBadgeBar", badgeWidget);
            Assign(screen, "avatarRailWidget", avatarWidget);
            Assign(screen, "scenePortraitLayer", scenePortrait);
            Assign(screen, "mapButton", mapBtn);
            Assign(screen, "dialogueTabButton", dialogueTab);
            Assign(screen, "eventTabButton", eventTab);
            Assign(screen, "dialoguePanel", dialoguePanel);
            Assign(screen, "eventPanel", eventPanel);
            Assign(screen, "defaultFeatureId", DialogueFeaturePanel.Id);

            Assign(bootstrap, "mainWorldScreen", screen);
            Assign(bootstrap, "mode", LuoxiaClientBootstrap.SessionSourceMode.MockOnly);
            Assign(bootstrap, "engineBaseUrl", "http://127.0.0.1:8000");

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Luoxia] MainWorld rebuilt with full map-slice wiring → {ScenePath}");
        }

        // ── Prefabs ─────────────────────────────────────────────────────────

        private static GameObject BuildDialogueTurnPrefab()
        {
            var root = Create("DialogueTurnItem", null);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(980, 150);
            var le = root.AddComponent<LayoutElement>();
            le.minHeight = 130f;
            le.preferredHeight = 150f;

            // Other (NPC) — left aligned
            var other = Create("OtherRoot", root.transform);
            Stretch(other);
            var otherAvatar = Create("OtherAvatar", other.transform);
            SetAnchors(otherAvatar, 0.0f, 0.25f, 0.12f, 0.95f);
            var otherAvImg = AddImage(otherAvatar, Map("frame_event_portrait.png"), Image.Type.Simple, Color.white);
            otherAvImg.preserveAspect = true;
            var otherBubble = Create("OtherBubble", other.transform);
            SetAnchors(otherBubble, 0.13f, 0.05f, 0.92f, 0.95f);
            var otherBubbleBg = AddImage(otherBubble, null, Image.Type.Sliced, new Color(0.08f, 0.07f, 0.1f, 0.82f));
            otherBubbleBg.raycastTarget = false;
            var otherName = CreateUiText("OtherName", otherBubble.transform, "对方", 22, FontStyle.Bold, TextAnchor.UpperLeft);
            SetAnchors(otherName.gameObject, 0.04f, 0.62f, 0.96f, 0.95f);
            otherName.color = new Color(1f, 0.9f, 0.65f, 1f);
            var otherBody = CreateUiText("OtherBody", otherBubble.transform, "……", 24, FontStyle.Normal, TextAnchor.UpperLeft);
            SetAnchors(otherBody.gameObject, 0.04f, 0.08f, 0.96f, 0.62f);
            otherBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            otherBody.verticalOverflow = VerticalWrapMode.Truncate;

            // Player — right aligned
            var player = Create("PlayerRoot", root.transform);
            Stretch(player);
            player.SetActive(false);
            var playerAvatar = Create("PlayerAvatar", player.transform);
            SetAnchors(playerAvatar, 0.88f, 0.25f, 1f, 0.95f);
            var playerAvImg = AddImage(playerAvatar, Map("frame_event_portrait.png"), Image.Type.Simple, Color.white);
            playerAvImg.preserveAspect = true;
            var playerBubble = Create("PlayerBubble", player.transform);
            SetAnchors(playerBubble, 0.08f, 0.05f, 0.87f, 0.95f);
            var playerBubbleBg = AddImage(playerBubble, null, Image.Type.Sliced, new Color(0.12f, 0.1f, 0.08f, 0.82f));
            playerBubbleBg.raycastTarget = false;
            var playerName = CreateUiText("PlayerName", playerBubble.transform, "你", 22, FontStyle.Bold, TextAnchor.UpperRight);
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
            // Row card — NOT the full 968×1548 modal art.
            var root = Create("EventCardItem", null);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(1000, 140);
            var le = root.AddComponent<LayoutElement>();
            le.minHeight = 130f;
            le.preferredHeight = 140f;

            var bg = AddImage(root, null, Image.Type.Sliced, new Color(0.06f, 0.05f, 0.08f, 0.88f));
            bg.raycastTarget = true;

            // Thin gold separator strip at bottom of row using 9-slice separator
            var sep = Create("Separator", root.transform);
            SetAnchors(sep, 0.02f, 0.0f, 0.98f, 0.04f);
            var sepImg = AddImage(sep, Map("deco_event_separator_9slice.png"), Image.Type.Sliced, Color.white);
            sepImg.raycastTarget = false;

            var portrait = Create("Portrait", root.transform);
            SetAnchors(portrait, 0.02f, 0.12f, 0.14f, 0.88f);
            var portraitImg = AddImage(portrait, Map("frame_event_portrait.png"), Image.Type.Simple, Color.white);
            portraitImg.preserveAspect = true;
            portraitImg.raycastTarget = false;

            var title = CreateUiText("Title", root.transform, "事件标题", 28, FontStyle.Bold, TextAnchor.MiddleLeft);
            SetAnchors(title.gameObject, 0.16f, 0.52f, 0.70f, 0.92f);

            var source = CreateUiText("Source", root.transform, "来源: 世界", 18, FontStyle.Normal, TextAnchor.MiddleLeft);
            SetAnchors(source.gameObject, 0.16f, 0.32f, 0.55f, 0.52f);
            source.color = new Color(1f, 0.85f, 0.5f, 0.85f);

            var summary = CreateUiText("Summary", root.transform, "摘要", 20, FontStyle.Normal, TextAnchor.UpperLeft);
            SetAnchors(summary.gameObject, 0.16f, 0.06f, 0.70f, 0.34f);
            summary.horizontalOverflow = HorizontalWrapMode.Wrap;
            summary.verticalOverflow = VerticalWrapMode.Truncate;
            summary.color = new Color(1f, 1f, 1f, 0.8f);

            var open = Create("OpenButton", root.transform);
            SetAnchors(open, 0.72f, 0.22f, 0.97f, 0.78f);
            // Use compact choice button art, not full modal
            var openImg = AddImage(open, Map("button_event_choice_normal_9slice.png"), Image.Type.Sliced, Color.white);
            var openBtn = open.AddComponent<Button>();
            openBtn.targetGraphic = openImg;
            var openLabel = CreateUiText("OpenLabel", open.transform, "开启", 26, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(openLabel.gameObject);

            var view = root.AddComponent<EventCardItemView>();
            Assign(view, "titleText", title);
            Assign(view, "summaryText", summary);
            Assign(view, "sourceText", source);
            Assign(view, "portraitImage", portraitImg);
            Assign(view, "openButton", openBtn);

            return SavePrefab(root, $"{PrefabRoot}/EventCardItem.prefab");
        }

        private static GameObject BuildAvatarRailItemPrefab()
        {
            var root = Create("AvatarRailItem", null);
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(120, 140);

            var portrait = Create("Portrait", root.transform);
            SetAnchors(portrait, 0.08f, 0.28f, 0.92f, 0.95f);
            var pImg = AddImage(portrait, null, Image.Type.Simple, new Color(0.35f, 0.32f, 0.38f, 1f));

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
            var name = CreateUiText("Name", namePlate.transform, "角色", 18, FontStyle.Normal, TextAnchor.MiddleCenter);
            Stretch(name.gameObject);

            var notif = Create("NotificationDot", root.transform);
            SetAnchors(notif, 0.72f, 0.78f, 0.92f, 0.96f);
            var notifImg = AddImage(notif, Map("icon_notification_dot.png"), Image.Type.Simple, Color.white);
            notifImg.preserveAspect = true;
            notifImg.enabled = false;
            notifImg.raycastTarget = false;

            var btn = root.AddComponent<Button>();
            btn.targetGraphic = pImg;

            var view = root.AddComponent<AvatarRailItemView>();
            Assign(view, "portraitImage", pImg);
            Assign(view, "selectedFrame", fImg);
            Assign(view, "notificationDot", notifImg);
            Assign(view, "nameText", name);
            Assign(view, "selectButton", btn);

            return SavePrefab(root, $"{PrefabRoot}/AvatarRailItem.prefab");
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static Button CreateTab(string name, Transform parent, string label, float index)
        {
            var go = Create(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(index * 0.5f, 0.25f);
            rt.anchorMax = new Vector2(index * 0.5f + 0.5f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var tmp = CreateUiText("Label", go.transform, label, 36, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(tmp.gameObject);
            tmp.color = new Color(1f, 0.95f, 0.85f, 0.95f);
            return go.AddComponent<Button>();
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

        /// <summary>
        /// Fixed pixel rect on design canvas. (x,y) = bottom-left corner, size (w,h).
        /// Parent must be full-screen stretch (MainWorldCanvas).
        /// </summary>
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
