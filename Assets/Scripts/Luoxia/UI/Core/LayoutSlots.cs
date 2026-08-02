namespace Luoxia.UI.Core
{
    /// <summary>
    /// Closed layout slot_id constants for RenderNode portrait / scene bindings.
    /// Exact match only — never invent or cross-slot fallback.
    /// </summary>
    public static class LayoutSlots
    {
        public const string Avatar = "avatar";
        public const string DialoguePortrait = "dialogue_portrait";
        public const string LocationScene = "location_scene";
        public const string MapAnchor = "map_anchor";
        public const string StageBackdrop = "stage_backdrop";
        public const string WorldHeader = "world_header";
        public const string WorldDefaultScene = "world_default_scene";
    }

    /// <summary>
    /// Host chrome stacking (sibling order under DesignRoot / ImmersiveShell).
    /// Not a runtime service — documentation registry for Builder + overlays.
    /// Bottom → top:
    /// 0 ScenePortrait / atmosphere (always visible under float chrome)
    /// 1 HUD + feature tabs/pages (dialogue/event input)
    /// 2 ArrivalLoreOverlay (non-modal toast on portrait/scene; does not lock input)
    /// 3 MapDestinationPanel float (scrim 45–60% + panel; at most one float; scrim eats clicks)
    /// 4 EventCardConfirmPanel / EndDayConfirmPanel local modals
    /// 5 NarrativeFramePlayer (presentation.frame + narrative.show only)
    /// 6 NightCurtainOverlay (day-increment Host choreography)
    /// 7 StageShellOverlay
    /// 8 SessionFatalOverlay
    /// </summary>
    public static class ChromeLayers
    {
        public const int SceneAtmosphere = 0;
        public const int HudAndFeatures = 1;
        public const int ArrivalToast = 2;
        public const int MapFloat = 3;
        public const int LocalConfirmModal = 4;
        public const int NarrativeModal = 5;
        public const int NightCurtain = 6;
        public const int StageShell = 7;
        public const int Fatal = 8;
    }
}
