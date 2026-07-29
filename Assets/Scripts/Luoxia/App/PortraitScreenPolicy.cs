using UnityEngine;

namespace Luoxia.App
{
    /// <summary>
    /// Enforces portrait presentation at runtime.
    /// Design reference: 1080×1920 (9:16). Standalone uses a portrait window when possible.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PortraitScreenPolicy : MonoBehaviour
    {
        public const int DesignWidth = 1080;
        public const int DesignHeight = 1920;

        [SerializeField] private bool lockPortraitOrientation = true;
        [SerializeField] private bool applyStandaloneWindowSize = true;
        [SerializeField] private int standaloneWidth = DesignWidth;
        [SerializeField] private int standaloneHeight = DesignHeight;
        [SerializeField] private bool preferWindowedStandalone = true;

        private void Awake()
        {
            Apply();
        }

        [ContextMenu("Apply Portrait Policy")]
        public void Apply()
        {
            if (lockPortraitOrientation)
            {
                Screen.autorotateToPortrait = true;
                Screen.autorotateToPortraitUpsideDown = false;
                Screen.autorotateToLandscapeLeft = false;
                Screen.autorotateToLandscapeRight = false;
                Screen.orientation = ScreenOrientation.Portrait;
            }

#if UNITY_STANDALONE || UNITY_EDITOR
            if (applyStandaloneWindowSize && !Application.isMobilePlatform)
            {
                if (preferWindowedStandalone && Screen.fullScreen)
                {
                    Screen.fullScreenMode = FullScreenMode.Windowed;
                }

                var w = Mathf.Max(360, standaloneWidth);
                var h = Mathf.Max(640, standaloneHeight);
                // Ensure portrait (height >= width).
                if (w > h)
                {
                    (w, h) = (h, w);
                }

                Screen.SetResolution(w, h, Screen.fullScreenMode);
            }
#endif
        }
    }
}
