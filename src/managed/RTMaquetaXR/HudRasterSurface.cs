using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _hudRasterRequestPending;
        internal static string HudRasterDescription => HudRasterPolicy.Width(_cfg.uiRasterMode) + " × " +
            HudRasterPolicy.Height(_cfg.uiRasterMode) + (_hudRasterRequestPending ? ModLocalization.Text(" · pending") :
            ModLocalization.Text(" · actual ") + Screen.width + " × " + Screen.height) + "\n" + HudCaptureDescription;

        internal static void CycleHudRaster(int direction)
        {
            _cfg.uiRasterMode = HudRasterPolicy.Next(_cfg.uiRasterMode, direction);
            _hudRasterRequestPending = _active;
            MarkSettingsDirty();
        }

        static void SetHudRasterSurface()
        {
            _hudRasterRequestPending = false;
            Screen.SetResolution(HudRasterPolicy.Width(_cfg.uiRasterMode),
                HudRasterPolicy.Height(_cfg.uiRasterMode), FullScreenMode.Windowed);
            _startupStableSince = -1;
        }

        static void TickHudRasterSurface()
        {
            if (!_active || !_displayPrepared || !_hudRasterRequestPending) return;
            // A surface change may rebuild the game's CanvasScaler geometry.
            // Cancel held actions and restore native canvases before it happens.
            CancelTouchGameGestures();
            if (_attached) SuspendForTransition("UI raster resolution change");
            SetHudRasterSurface();
            _lastTransitionTime = Time.unscaledTime;
            _log.Log("[ui/raster] Requested real window " + HudRasterPolicy.Width(_cfg.uiRasterMode) + "x" +
                HudRasterPolicy.Height(_cfg.uiRasterMode) + "; screenshot follows actual Screen size, no capture upsampling.");
        }
    }
}
