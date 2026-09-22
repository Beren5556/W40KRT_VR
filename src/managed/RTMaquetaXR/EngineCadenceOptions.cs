namespace RTMaquetaXR
{
    public static partial class Main
    {
        // Keep this optional experiment inside Performance so the shared root
        // controls and the advanced language insertion retain their indices.
        static OverlayOption EngineCadenceMenu() => ImageGroup("Engine cadence · experimental",
            "Off by default. Only selected compatible visual updates run at a lower rate. Head tracking, Touch input and VR rendering keep the runtime rate. Game simulation is unchanged; target frame rates are not guaranteed.",
            ImageValue("Enable engine cadence",
                "Save whether to use reduced cadence on the next game launch. Enabling or disabling it requires restarting the game. Off uses the original update path.",
                () => ModLocalization.Text(_cfg.engineCadenceEnabled ? "On" : "Off"),
                direction => SetEngineCadenceEnabled(!_cfg.engineCadenceEnabled)),
            ImageValue("VR / visual updates",
                "Save 72/36, 90/45 or 120/60 Hz for the next game launch. Changing the pair requires a restart. Match the VR rate in Virtual Desktop; this selector does not change headset refresh. Different runtime frame timing suspends the feature.",
                EngineCadencePairText,
                direction => SetEngineCadenceMode(CycleImageValue(_cfg.engineCadenceMode, direction, 3)),
                enabled: () => _cfg.engineCadenceEnabled),
            new OverlayOption {
                Label = "Cadence status",
                Description = "Shows the cadence currently applied and any restart needed for saved changes. Only selected visual updates use the lower cadence. Requested rates do not guarantee measured FPS.",
                Value = EngineCadenceStatusText
            });

        static string EngineCadencePairText()
        {
            int vrRate = _cfg.engineCadenceMode == 1 ? 90 : _cfg.engineCadenceMode == 2 ? 120 : 72;
            return ModLocalization.Format("{0} Hz VR / {1} Hz visual updates", vrRate, vrRate / 2);
        }
    }
}
