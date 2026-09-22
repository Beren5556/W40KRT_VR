using System;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static PresentationContracts _presentation;
        static bool _presentationBindingsAttempted, _tutorialShowing;
        static bool _tutorialBlocksInput;
        static object _tutorialWindowIdentity;
        internal static bool NativeTutorialInputBlocked => _tutorialBlocksInput;
        static float _presentationRetryAt;
        static bool _presentationContextWarning;
        static string _presentationSceneMode;
        static void EnsurePresentationBindings()
        {
            if (_presentationBindingsAttempted) return;
            _presentationBindingsAttempted = true;
            try { _presentation = PresentationContracts.Create(AccessTools.TypeByName); InstallNativeTutorialMenuGuard(); }
            catch (Exception error) { _log.Error("[presentation] Exact game contracts unavailable: " + error); }
        }
        static void UpdatePresentationContext(string mode)
        {
            if (CinematicPolicy.StereoMode(mode)) _presentationSceneMode = mode;
            else if (mode != "Pause" && mode != "None" && mode != "Unknown") _presentationSceneMode = null;
            EnsurePresentationBindings();
            _tutorialShowing = false;
            _tutorialBlocksInput = false; _tutorialWindowIdentity = null;
            if (_presentation == null || Time.unscaledTime < _presentationRetryAt) return;
            try
            {
                var game = _presentation.Game();
                var root = game == null ? null : _presentation.RootUi(game);
                if (root == null || _presentation.IngameMenu(root)) return;
                var common = _presentation.Common(root);
                var tutorial = common == null ? null : _presentation.Tutorial(common);
                if (tutorial != null)
                {
                    // A small native tutorial is an instructional hint while
                    // the player performs an action, not a blocking dialog.
                    bool modal = _presentation.BigTutorial(tutorial), hint = _presentation.SmallTutorial(tutorial);
                    _tutorialBlocksInput = NativeTutorialInputPolicy.BlocksInput(modal, hint);
                    _tutorialShowing = modal || hint;
                    _tutorialWindowIdentity = _tutorialBlocksInput ? _presentation.BigTutorialModel(tutorial) :
                        _tutorialShowing ? _presentation.SmallTutorialModel(tutorial) : null;
                }
            }
            catch (Exception error)
            {
                // A root being disposed during loading is temporary. Keep the
                // independently valid camera bindings, and bound failed probes.
                _presentationRetryAt = Time.unscaledTime + 1f;
                if (!_presentationContextWarning)
                {
                    _presentationContextWarning = true;
                    _log.Error("[presentation] UI context temporarily unavailable; retrying at most once/second: " + error.Message);
                }
            }
            ObserveCombatFocus();
        }
        static bool PresentationStereo(string mode) => !InNavigationMap && (InSpaceCombat || CinematicPolicy.PresentationStereo(mode, _presentationSceneMode,
            _tutorialShowing, _actionCameraActive, _cfg.cinematicVr, _cfg.tutorialVr, _cfg.dialogVr));
        static string PresentationSceneMode(string mode) => CinematicPolicy.SceneMode(mode, _presentationSceneMode, _tutorialShowing);
    }
}
