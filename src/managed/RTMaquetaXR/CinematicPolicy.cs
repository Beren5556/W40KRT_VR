namespace RTMaquetaXR
{
    internal static class CinematicPolicy
    {
        internal static bool ScriptedMode(string mode) => mode == "Cutscene" || mode == "CutsceneGlobalMap" || mode == "Dialog";
        internal static bool StereoMode(string mode) => mode == "Default" || mode == "SpaceCombat" || ScriptedMode(mode);
        // Tutorial visibility is updated separately from the game-mode stack.
        // Both its opening frame and its closing frame can be Pause with no VM.
        // Retain the last real scene throughout that pause, including dialogues.
        internal static string SceneMode(string mode, string pausedScene, bool tutorial) =>
            mode == "Pause" && StereoMode(pausedScene) ? pausedScene : mode;
        internal static bool PresentationStereo(string mode, string pausedScene, bool tutorial, bool action,
            bool cinemaVr, bool tutorialVr, bool dialogVr)
        {
            string scene = SceneMode(mode, pausedScene, tutorial);
            if (!StereoMode(scene)) return false;
            if (tutorial) return tutorialVr;
            if (scene == "Dialog") return dialogVr;
            if (ScriptedMode(scene) || action) return cinemaVr;
            return scene == "Default" || scene == "SpaceCombat";
        }
    }
}

