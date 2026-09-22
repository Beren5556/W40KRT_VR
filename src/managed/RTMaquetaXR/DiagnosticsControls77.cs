namespace RTMaquetaXR
{
    public static partial class Main
    {
        static OverlayOption AllDiagnosticsOption77() => ImageToggle("All diagnostics",
            "Enable or disable all mod traces and detailed measurements together, including active OpenXR, NVIDIA and OFXR modules. Off by default; your choice is saved. Enabling traces adds measurement overhead. Runtime faults remain visible in the overlay.",
            () => AllDiagnosticsEnabled, SetAllDiagnosticsEnabled);
    }
}
