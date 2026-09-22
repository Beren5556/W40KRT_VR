namespace RTMaquetaXR
{
    public static partial class Main
    {
        static string OpenXrRuntimeValue() => OpenXrRuntimePolicy.Name(_cfg.openXrRuntime) +
            (OpenXrRuntime.RestartPending(_cfg.openXrRuntime) ? " · " + ModLocalization.Text("Restart required") : "");
        static void SelectOpenXrRuntime(int value)
        {
            _cfg.openXrRuntime = OpenXrRuntimeSelection.Normalize(value);
            MarkSettingsDirty(); SaveSettings(); // Persist before the requested restart.
        }
        static OverlayOption OpenXrRuntimeOption() => ImageValue("OpenXR runtime",
            "Choose Virtual Desktop (VDXR) or Meta Quest Link / Air Link. Requires restarting the game. Does not change the Windows default runtime, headset refresh or graphics settings. Connect through the selected PC application before launching.",
            OpenXrRuntimeValue, direction => SelectOpenXrRuntime(1 - OpenXrRuntimeSelection.Normalize(_cfg.openXrRuntime)));
    }
}
