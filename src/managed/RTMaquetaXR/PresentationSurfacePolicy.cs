namespace RTMaquetaXR
{
    internal enum PresentationSurface { Scene, Panel, Transition }

    internal static class PresentationSurfacePolicy
    {
        internal static PresentationSurface Resolve(bool manualPanel, bool mainMenu, bool loading,
            bool video, bool stereoScene, bool previousScene, string mode, bool nativeUiOnly = false)
        {
            if (manualPanel || video || (mainMenu && !loading)) return PresentationSurface.Panel;
            if (loading) return PresentationSurface.Transition;
            // Colony/service dialogues can retain the Dialog game mode while
            // the native stack intentionally removes the world camera. They
            // are real interface content, not a missing stereo frame.
            if (nativeUiOnly) return PresentationSurface.Panel;
            if (stereoScene) return PresentationSurface.Scene;
            // None/Unknown are not instructions to show the world's monitor
            // image. Wait in the same XR session until a real scene is ready.
            if (previousScene && (mode == "None" || mode == "Unknown")) return PresentationSurface.Transition;
            return PresentationSurface.Panel;
        }
        internal static bool CapturePanel(PresentationSurface surface, bool stereoRendered, bool poseValid) =>
            surface == PresentationSurface.Panel && !stereoRendered && poseValid;
    }
}
