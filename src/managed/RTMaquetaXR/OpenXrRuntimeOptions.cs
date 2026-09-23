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
            "Choose Virtual Desktop (VDXR), Meta Quest Link / Air Link, or Pimax OpenXR. PICO uses VDXR. Restart the game after changing runtime. Connect the headset through its PC application first. Windows runtime and graphics settings stay unchanged.",
            OpenXrRuntimeValue, direction => SelectOpenXrRuntime(CycleImageValue(OpenXrRuntimeSelection.Normalize(_cfg.openXrRuntime),direction,3)));
    }
}
