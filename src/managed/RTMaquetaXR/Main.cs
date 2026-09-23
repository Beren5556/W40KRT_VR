// Original game/UI integration: Copyright (c) 2026 SolemnScribe, MIT.
// Modified for direct OpenXR/VDXR, full 6DoF and autonomous tests, September 2026.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityModManagerNet;
using HarmonyLib;
using Owlcat.Runtime.Visual.Waaagh;


namespace RTMaquetaXR
{
    // Legacy public API retained for integrations; all mod surfaces now use the
    // same ES/EN catalogue and saved preference, including the UMM settings panel.
    public static class Localization
    {
        public static string ActiveCode = "en";
        public static string Get(string english) => ModLocalization.Text(english);
        public static void Init(UnityModManager.ModEntry modEntry) { }
    }

    // Game-specific UI and Waaagh compatibility derived from RTVR (MIT).
    public static partial class Main
    {
        // Hardcoded in the source, so it travels with the compiled DLL. The load line logs this next to
        // Info.json's Version; if the two disagree in the log, the running DLL is stale (rebuild needed).
        const string BuildTag = "0.9.80";
        static ModDiagnosticLog _log;
        static string _settingsPath;
        static readonly string[] _presetPaths = new string[3];                       // one snapshot file per slot
        // M7: localise a user-facing panel string (returns the English unchanged when no translation is active).
        internal static string L(string s) => Localization.Get(s);
        static readonly string[] PresetNames  = { "VR View 1", "VR View 2", "VR View 3" };
        static bool   _settingsDirty;       // a GUI slider changed a value; flush to disk after a short idle
        static float  _lastSettingsTouch;   // unscaled time of the last GUI change (for debouncing the save)
        static RenderTexture _rtL, _rtR, _uiRT;
        static GameObject _runnerGo;
        static bool _active;
        static bool _recenterPending;
        // Prepare the smaller desktop window before sharing the D3D11 device.
        // The eye targets keep their own resolution; restore the user's original
        // desktop resolution and mode when the VR session stops.
        static FullScreenMode _savedFullScreenMode = FullScreenMode.ExclusiveFullScreen;
        static bool _displayPrepared;
        static int _savedScreenWidth, _savedScreenHeight;

        // --- OpenXR session vs. scene attachment ---
        // The OpenXR session (Init, eye RTs, the Runner, the submit loop) stays up for the whole game so the
        // headset never drops. The "scene attachment" - the eye cameras plus the world-space canvas conversion -
        // is the only part that touches scene objects, so it is the only part we tear down across a scene load and
        // rebuild afterwards. A native use-after-free fires if our eye cameras or converted canvas are still live
        // while the game unloads a scene, so we relinquish on the FIRST sign of a load and resume once the new
        // scene's camera is stable.
        static Runner _runner;            // the live Runner (eye cameras + submit loop)
        static bool _attached;            // scene attachment is live (eye cameras built, canvases converted)
        static Camera _attachedCam;       // the Camera.main we attached to (safety-net identity check)
        static volatile bool _suspendRequested;   // a scene-load hook/event asked us to suspend; honoured next frame
        static float _lastTransitionTime; // unscaled time of the last scene op/event; gates how soon we may resume
        static Camera _resumeCandidate;   // the camera we're waiting to see stabilise before re-attaching
        static int _resumeStable;         // consecutive frames _resumeCandidate has been the stable Camera.main
        const int   ResumeStableFrames = 8;     // Camera.main must hold this many frames before we re-attach
        const float ResumeMinDelay     = 0.75f; // ...and at least this long since the last scene op (let loading settle)

        // Eye targets persist through scene transitions. The native bridge copies
        // into runtime-owned swapchains and retains submitted textures until the
        // corresponding Unity render event completes.
        static int _eyeW, _eyeH;          // Live output dimensions committed together between stereo pairs.

        // Resume gate: a single area load fires a storm of sub-scene loads with quiet gaps, so a time delay alone
        // re-attaches mid-load. The game's LoadingCanvas carries active drawables only while a load is on screen,
        // so we hold the frozen frame until it clears and attach exactly once. A hard timeout is the fail-safe in
        // case that probe never reads clear.
        static Canvas _loadingCanvas;
        static readonly List<CanvasRenderer> _loadingProbe = new List<CanvasRenderer>();
        static bool _heldForLoadingLogged;
        static float _suspendStart;
        const float ResumeHardTimeout = 30f;   // longer than any observed load; resume regardless if we somehow stall

        // The area-transition selector (TransitionPCView) carries a diegetic "pantograph" servo-arm whose little
        // screen shows the hovered destination. Its arm pose is driven through Camera.main, so in VR the readout
        // text is correct but the screen mis-aims - off the bottom of the panel in tall scenes, stuck at the list
        // bottom in others. Re-posing a skeletal prop the game animates isn't worth it for a cosmetic readout, so
        // while VR is attached we just hide the arm (the list, the glowing map crest and the light beam still show
        // the selection). Throttled because the selector only exists while the menu is open; restored on VR-off.
        static GameObject _hiddenPantograph;
        static float _pantographScanAt;
        const float PantographScanInterval = 0.25f;

        internal static bool SessionActive => _active;
        internal static bool Attached => _attached;
        internal static Camera AttachedCam => _attachedCam;
        internal static bool ConsumeSuspendRequest() { if (!_suspendRequested) return false; _suspendRequested = false; return true; }
        static Settings _cfg = new Settings();

        const float RenderScaleMin = 0.25f, RenderScaleMax = 2.0f;   // 0.6.97: slider range for _cfg.renderScale (1.0 = HMD-recommended)
        const float WorldScaleStep = 1.2f;    // multiplicative (per press)
        const float WorldScaleMin = ComfortCameraOptions.ScaleMin, WorldScaleMax = ComfortCameraOptions.ScaleMax;
        const float IpdStep        = 0.1f;    // additive
        const float IpdMin         = 0.1f,  IpdMax  = 5f;
        const float UiDistStep     = 0.25f, UiDistMin  = 0.5f, UiDistMax  = 10f;
        const float UiWidthStep    = 0.05f, UiWidthMin = 0.5f, UiWidthMax = 1.8f;   // independent angular size relative to centered binocular fit
        const float MarkerBoostMin = 1.0f, MarkerBoostMax = 4.0f;   // 0.6.85: map-marker size cap range (1.0 = markers stay at the registered fit)
        const float UiPosMin = -0.5f, UiPosMax = 1.5f;   // element placement: 0..1 spans the panel; beyond that is off-panel (visible in VR, but out of mouse reach)

        public class Settings
        {
            public ComfortCameraSettings comfort = new ComfortCameraSettings();
            public int openXrRuntime = 0; // 0 VDXR, 1 Meta Quest Link; applied at process startup.
            public string openXrRuntimeManifest80 = "";
            public int modLanguage = -1; // Follow game: esES -> Spanish; every other locale -> English.
            public float worldScale  = ApprovedUserDefaults.WorldScale;
            public float ipdScale    = 1.0f;
            public bool  includeRoll = true;
            public bool  uiEnabled   = true;
            public bool touchGestureHelp = false;
            public bool touchHandsVisible = true;
            public bool touchThirdPersonFollow = true;
            public bool touchFollowRecenter = false;
            public float modMenuOffsetX = 0, modMenuOffsetY = 0;
            public bool touchContextHints = false;
            public float touchHintX = 0, touchHintY = 0, touchHintSize = 1f;
            public int controlsLayoutRevision = 0;
            public bool limitExplorationZoom = true, spaceHudVisible = false;
            public bool customSpaceBackground = true;
            public int qualityPreset = 3;
            public float mapGlobalZoom = .5f, mapSystemZoom = .5f;
            public bool touchCombatGroundDoubleClickHead = true;
            public float uiDistance  = ApprovedUserDefaults.Distance;
            public float uiWidth     = ApprovedUserDefaults.Width;
            public float uiElementScale = 1f;
            public bool pcHudMinimal = true;
            public float uiMenuScale = ApprovedUserDefaults.MenuText;
            public float uiMenuWidth = ApprovedUserDefaults.MenuWidth;
            public float uiMenuDistance = ApprovedUserDefaults.MenuDistance, uiMenuAspect = ApprovedUserDefaults.MenuAspect, uiMenuOffsetX = ApprovedUserDefaults.MenuX, uiMenuOffsetY = ApprovedUserDefaults.MenuY;
            public float mapGlobalWidth=.82f, mapGlobalDistance=.5f, mapGlobalAspect=0f, mapGlobalOffsetX=0f, mapGlobalOffsetY=0f;
            public float mapSystemWidth=.82f, mapSystemDistance=.5f, mapSystemAspect=0f, mapSystemOffsetX=0f, mapSystemOffsetY=0f;
            public int engineEffectProfile = ApprovedUserDefaults.EngineEffects;
            public bool combatMinimumEffects = false;
            public bool engineCadenceEnabled = false;
            public bool ofxrEnabled77 = false;
            public int combatPresentationMask74 = 15; // Independent presentation blocks; applied at restart.
            public bool engineOptimizationsEnabled = true;
            public int engineOptimizationMask = 36; // Approved selection: blocks 3 and 6 only.
            public bool allDiagnosticsEnabled = false;
            public int diagnosticsRevision77 = 77;
            public int engineCadenceMode = 0; // 72/36, 90/45, 120/60; captured at game startup.
            public float uiAspect = ApprovedUserDefaults.Aspect, uiOffsetX = ApprovedUserDefaults.OffsetX, uiOffsetY = ApprovedUserDefaults.OffsetY;
            public int uiRasterMode = ApprovedUserDefaults.Raster;
            public bool uiFullResolution = true;
            public bool cinematicVr = true, tutorialVr = true, dialogVr = true;
            public bool coalesceHudLayout = true;
            public bool familiarEffectsOnY76 = true;
            public bool skipFullyFadedScenery = true;
            public bool stableHudCapture = true;
            public bool isolateOvertipBatches = false;
            public float combatSurfaceIntensity = 1f, combatUnitFxIntensity = 1f;
            public bool preparationAuraEnabled = ApprovedUserDefaults.PreparationAura;
            public int combatOutlineMode = ApprovedUserDefaults.OutlineMode;
            public float combatOutlineIntensity = ApprovedUserDefaults.OutlineIntensity;
            public bool  lockedCrosshair = true;   // show the faint aim crosshair while the cursor is locked to centre
            public bool  autoStartVr = true;  // Connect through Virtual Desktop and launch the game normally.
            public bool  worldOvertips = true;   // 0.6.78: world-space entity overtips on by default (Ctrl+Alt+W toggles; persisted)
            public float markerBoostCap = 2f;    // User's validated HUD preset; 1.0 disables glyph boost.
            public float renderScale = 1.0f;     // Output scale; live changes commit only after both targets succeed.
            public bool temporalAA = true;      // Legacy preference, retained when loading old configurations.
            public bool disableAA = false;     // Off mode; otherwise temporalAA selects TAA or SMAA.
            public int neuralMode = 0;         // Off / shadow diagnostics / DLAA / DLSS.
            public float neuralScale = .67f;
            public int neuralPreset = 0;       // Auto / explicit K.
            public float neuralSharpness = .2f;
            public float taaSharpness = 0.35f;   // Native TAA sharpening, independent of FSR/render scale.
            public bool allowGameFsr = true;
            public bool skipDesktopWorld = true;
            public bool visibleRegionCulling = true;
            public bool indirectVisibleRegionCulling = true;
            public bool detailedProfiling = false;
            public bool syncForcedVisibility = true;
            public int drawDistanceMode = ApprovedUserDefaults.DrawMode; // Original, 100, 60, custom; only eye cameras.
            public float drawDistanceCustom = ApprovedUserDefaults.DrawDistance;
            public bool touchDrawDistanceShortcut = false; // Opt in: resting a thumb must not change rendering cost.
            public bool adaptiveDistance = ApprovedUserDefaults.AdaptiveDistance; // Ground-plane estimate; approved manual ceiling stays authoritative.
            public bool useGamePointerCache = false; // Experimental until UI changes between input and LateUpdate are fully accounted for.
            public bool  desktopMirror = true;   // 0.6.104 (M5): auto-show the desktop mirror in the headset while a full-screen menu suspends VR
            public bool  mirrorCursor  = true;   // 0.6.119: stamp a synthetic cursor into the mirror (DDA excludes the hardware pointer)
            // Per-canvas placement overrides (panel-fraction centre, 0..1, y up), keyed by canvas name.
            // Empty = use the registered defaults. Edited via the settings selector; persisted as pos.<name> lines.
            public Dictionary<string, Vector2> uiPos = new Dictionary<string, Vector2>();
            public Settings() { ApprovedLayout80.CopyTo(this, true); }
        }

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr LoadLibrary(string lpFileName);

        internal static RenderTexture EyeL => _rtL;
        internal static RenderTexture EyeR => _rtR;
        internal static RenderTexture UiRT => _uiRT;
        internal static float WorldScale => EffectiveTouchWorldScale;
        internal static float IpdScale => _cfg.ipdScale;
        internal static bool IncludeRoll => _cfg.includeRoll;
        internal static ModDiagnosticLog Log => _log;

        internal static bool TakeRecenter()
        {
            if (!_recenterPending) return false;
            _recenterPending = false;
            return true;
        }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            // Decide before settings migration, callbacks, patches or OpenXR setup.
            if (!_launcherVrRequested) return true;
            _log = new ModDiagnosticLog(modEntry.Logger.Log, modEntry.Logger.Error);
            SettingsFiles76.Migrate(modEntry.Path, _presetPaths.Length);
            DiagnosticsDefaults77.Migrate(modEntry.Path, _presetPaths.Length);
            _settingsPath = SettingsFiles76.PathFor(modEntry.Path, "settings");
            for (int i = 0; i < _presetPaths.Length; i++) _presetPaths[i] = SettingsFiles76.PathFor(modEntry.Path, "view" + (i + 1));
            LoadSettings();
            OpenXrRuntime.CaptureStartup(_cfg.openXrRuntime,_cfg.openXrRuntimeManifest80);
            ApplyAllDiagnosticsSetting();
            CaptureEngineCadenceStartup();
            CaptureEngineOptimizations58();
            _worldOvertips = _cfg.worldOvertips;   // 0.6.78: apply persisted overtips state
            UpdateModLanguage(true);

            modEntry.OnUpdate = OnUpdate;
            modEntry.OnToggle = OnToggle;
            modEntry.OnUnload = OnUnload;
            modEntry.OnGUI    = OnGUI;

            _modFolder = modEntry.Path;
            CaptureOfxrStartup77(_modFolder);
            Application.quitting += OnAppQuitting;   // 0.6.118: exit-teardown guard (see OnUnload)
            TryHookCursor(modEntry);      // capture the game's hardware cursor so the reticle can wear it
            TryHookHintPlacement(modEntry);   // 0.6.136: panel-correct hint placement while attached
            TryHookSceneLoads(modEntry);  // suspend the scene attachment across loads (avoids the transition crash)
            TryPatchPbdCameraCulling(modEntry);   // 0.6.88: PBD camera-array growth (fixes the SimulationPass error flood)
            ArmOpenXR();
            InstallStartupVrGate();
            InstallTouchPcStartup();
            InstallPcHudAlphaSuppression();
            InstallLoadingStateHooks();
            InstallMainMenuBranding();

            _log.Log("RTMaquetaXR " + modEntry.Info.Version + " loaded (code build " + BuildTag + "). OpenXR automatic start; Ctrl+Alt+V to toggle.");
            return true;
        }

        static void OnAppQuitting() { _appQuitting = true; }

        static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            if (_active) StopVr();
            StopTouchPcStartup();
            StopStartupVrGate();
            StopLoadingStateHooks();
            StopMainMenuBranding();
            // 0.6.118: let the process die patched. Unpatching at application quit frees Harmony
            // trampolines (JITted code) while other threads may still be executing them - the exact
            // class of the parked exit-teardown EXEC-at-heap crash (8 specimens; handoff section 12.10
            // first-order item 1). Runtime mod-disable still unpatches as before; only quit skips.
            if (!_appQuitting)
            {
                try { _harmony?.UnpatchAll(_harmony.Id); } catch { }
            }
            else _log.Log("[quit] application quitting - leaving Harmony patches in place (exit-teardown guard).");
            _harmony = null;
            return true;
        }

        // UMM settings panel. Sliders write _cfg live (the eye cameras and PositionWorldSpaceUi read it every
        // frame, so changes show in the headset immediately) and persist to RTMAQUETAXR_settings.cfg, debounced.
        static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label(ModLocalization.Text("Tabletop VR Â· OpenXR Â· experimental ") + BuildTag);
            if (GUILayout.Button(ModLocalization.Text("OpenXR runtime") + ": " + OpenXrRuntimeValue())) SelectOpenXrRuntime((OpenXrRuntimeSelection.Normalize(_cfg.openXrRuntime) + 1) % 3);
            GUILayout.Label(ModLocalization.Format("Current runtime: {0}. Changes require restarting the game.", OpenXrRuntime.Name));
            GUILayout.Label(ModLocalization.DiagnosticText(OpenXR.Status));
            if (GUILayout.Button(_active ? ModLocalization.Text("Stop VR") : ModLocalization.Text("Start VR")))
            { if (_active) { _autoStartArmed = false; StopVr(); } else { _autoStartArmed = true; StartVr(); } }
            ControlIconDrawing.LayoutLabel(TouchControlAssignments.Find("overlay").Description);
            if (GUILayout.Button(ModLocalization.Text("Recenter position and orientation"))) _recenterPending = true;
            if (GUILayout.Button(ModLocalization.Text("Toggle flat screen"))) ToggleDesktopMirror();
            if (InNavigationMap) GUILayout.Label(ModLocalization.Text("Navigation panels Â· use their own placement settings"));
            else if (InSpaceCombat) GUILayout.Label(ModLocalization.Text("Space table Â· scale ") + TouchWorldScale.ToString("0.00", ModLocalization.Culture) + ModLocalization.Text(" Â· ship-relative limits"));
            else GUILayout.Label(ModLocalization.Text("Bounded Touch camera Â· scale ") + TouchWorldScale.ToString("0.00", ModLocalization.Culture) + ModLocalization.Text(" Â· fixed limits 6â€“14"));
            float touchMove = Slider(ModLocalization.Text("Touch movement speed"), TouchMoveSpeed, .25f, 2f, "0.00");
            if (!Mathf.Approximately(touchMove, TouchMoveSpeed)) SetTouchMoveSpeed(touchMove);
            float touchTurn = Slider(ModLocalization.Text("Touch rotation speed"), TouchTurnSpeed, .25f, 2f, "0.00");
            if (!Mathf.Approximately(touchTurn, TouchTurnSpeed)) SetTouchTurnSpeed(touchTurn);
            float touchZoom = Slider(ModLocalization.Text("Touch zoom speed"), TouchZoomSpeed, .25f, 2f, "0.00");
            if (!Mathf.Approximately(touchZoom, TouchZoomSpeed)) SetTouchZoomSpeed(touchZoom);
            bool gestureHelp = GUILayout.Toggle(_cfg.touchGestureHelp, ModLocalization.Text("Servo-skull gesture help Â· upper right"));
            if (gestureHelp != _cfg.touchGestureHelp) { _cfg.touchGestureHelp = gestureHelp; MarkSettingsDirty(); }
            bool handsVisible = GUILayout.Toggle(_cfg.touchHandsVisible, ModLocalization.Text("3D servo-skulls"));
            if (handsVisible != _cfg.touchHandsVisible) { _cfg.touchHandsVisible = handsVisible; MarkSettingsDirty(); }
            float distance = Slider(ModLocalization.Text("Panel distance"), _cfg.uiDistance, 0.5f, 4f, "0.00");
            if (!Mathf.Approximately(distance, _cfg.uiDistance)) { _cfg.uiDistance = distance; MarkSettingsDirty(); }
            float resolution = Slider(ModLocalization.Text("Per-eye resolution"), RequestedOutputScale, 0.25f, 1.5f, "0.00");
            if (!Mathf.Approximately(resolution, RequestedOutputScale)) RequestOutputScale(resolution);
            GUILayout.Label(ModLocalization.Text("Resolution 1.00 = full OpenXR runtime recommendation. Live change; ") + OutputStatusText);
            if (_active) GUILayout.Label(ModLocalization.Text("Per eye: ") + OpenXR.Width + " Ã— " + OpenXR.Height +
                ModLocalization.Text("; runtime recommends ") + OpenXR.RecommendedWidth + " Ã— " + OpenXR.RecommendedHeight);
            GUILayout.Label(ModLocalization.Text("A larger tabletop scale makes the world smaller."));
            DrawAaSelector();
            DrawNeuralSelector();
            DrawDrawDistanceSelector();
            float sharpness = Slider(ModLocalization.Text("TAA sharpness"), _cfg.taaSharpness, 0f, 1f, "0.00");
            if (sharpness != _cfg.taaSharpness) { _cfg.taaSharpness = sharpness; _renderTargets.Clear(); MarkSettingsDirty(); }
            bool fsr = GUILayout.Toggle(_cfg.allowGameFsr, ModLocalization.Text("Apply the game FSR setting to VR too"));
            if (fsr != _cfg.allowGameFsr) { _cfg.allowGameFsr = fsr; MarkSettingsDirty(); }
            bool skipWorld = GUILayout.Toggle(_cfg.skipDesktopWorld, ModLocalization.Text("Skip the third world view; show one eye on the monitor"));
            if (skipWorld != _cfg.skipDesktopWorld) { _cfg.skipDesktopWorld = skipWorld; MarkSettingsDirty(); }
            bool visibleCull = GUILayout.Toggle(_cfg.visibleRegionCulling, ModLocalization.Text("Cull objects outside the headset view"));
            if (visibleCull != _cfg.visibleRegionCulling) { _cfg.visibleRegionCulling = visibleCull; MarkSettingsDirty(); }
            bool indirectCull = GUILayout.Toggle(_cfg.indirectVisibleRegionCulling, ModLocalization.Text("Cull instances outside the headset view"));
            if (indirectCull != _cfg.indirectVisibleRegionCulling) { _cfg.indirectVisibleRegionCulling = indirectCull; MarkSettingsDirty(); }
            bool forcedVisibility = GUILayout.Toggle(_cfg.syncForcedVisibility, ModLocalization.Text("Update effect visibility for both eyes without drawing another view"));
            if (forcedVisibility != _cfg.syncForcedVisibility) { _cfg.syncForcedVisibility = forcedVisibility; MarkSettingsDirty(); }
            bool allDiagnostics = GUILayout.Toggle(AllDiagnosticsEnabled, ModLocalization.Text("All diagnostics"));
            if (allDiagnostics != AllDiagnosticsEnabled) SetAllDiagnosticsEnabled(allDiagnostics);
            bool auto = GUILayout.Toggle(_cfg.autoStartVr, ModLocalization.Text("Start automatically when the headset connects through the selected runtime"));
            if (auto != _cfg.autoStartVr) { _cfg.autoStartVr = auto; _autoStartArmed = auto; SaveSettings(); }
            bool flip = GUILayout.Toggle(OpenXR.FlipEyes, ModLocalization.Text("Flip eye images vertically (Ctrl+Alt+Y)"));
            OpenXR.FlipEyes = flip;
            OpenXR.FlipFlat = GUILayout.Toggle(OpenXR.FlipFlat, ModLocalization.Text("Flip the flat screen vertically"));
            GUILayout.Label(ModLocalization.Text("Logs: openxr-native.log, diagnostico-openxr.json and Unity Mod Manager log."));
        }

        // One labelled horizontal slider row; returns the (possibly changed) value.
        static float Slider(string label, float val, float min, float max, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(220));
            float v = GUILayout.HorizontalSlider(val, min, max, GUILayout.Width(240));
            GUILayout.Label(v.ToString(fmt, CultureInfo.InvariantCulture), GUILayout.Width(56));
            GUILayout.EndHorizontal();
            return v;
        }

        static void MarkSettingsDirty() { _settingsDirty = true; _lastSettingsTouch = Time.unscaledTime; }

        // 0.6.136: the centre-screen tooltip fix. Kingmaker's HintView.UpdateHintPosition computes
        // the hint's local position via RectTransformUtility.ScreenPointToLocalPointInRectangle(
        // parent, CursorController.CursorPosition, UICamera.Instance, out local) and DISCARDS the
        // bool - on a converted world-space canvas the UICamera ray cannot reach the panel rect,
        // the conversion fails, local stays (0,0), and UIUtility.LimitPositionRectInRect centres
        // the hint (the observed defect; IL-verified against Code.dll, 2026-07-12). While attached
        // the correct camera for panel geometry is the pick camera - that mapping is its entire
        // purpose - so the prefix redoes the identical computation with it and skips the original.
        // Every non-ours case (flat mode, foreign canvases, missing reflection targets, even a
        // failed pick ray) falls through to the game's own code, so flat play is untouched.
        static FieldInfo    _hintParentField, _hintRectField;
        static PropertyInfo _hintCursorPosProp;
        static MethodInfo   _hintLimitRectMethod;

        static void TryHookHintPlacement(UnityModManager.ModEntry modEntry)
        {
            try
            {
                var hv = AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.View.Tooltip.PC.HintView");
                if (hv == null) { _log.Log("Hint hook: HintView type not found; centre-screen hints stay vanilla."); return; }
                var target = AccessTools.Method(hv, "UpdateHintPosition");
                if (target == null) { _log.Log("Hint hook: UpdateHintPosition not found; centre-screen hints stay vanilla."); return; }
                _hintParentField = AccessTools.Field(hv, "m_ParentRectTransform");
                _hintRectField   = AccessTools.Field(hv, "m_RectTransform");
                if (_hintParentField == null || _hintRectField == null)
                { _log.Log("Hint hook: HintView rect fields not found; centre-screen hints stay vanilla."); return; }
                var cc = AccessTools.TypeByName("Kingmaker.UI.Pointer.CursorController");
                _hintCursorPosProp = cc != null ? AccessTools.Property(cc, "CursorPosition") : null;   // optional; falls back to Input.mousePosition
                var uu = AccessTools.TypeByName("Kingmaker.UI.Common.UIUtility");
                _hintLimitRectMethod = uu != null ? AccessTools.Method(uu, "LimitPositionRectInRect") : null;   // optional; unclamped placement without it
                if (_harmony == null) _harmony = new Harmony(modEntry.Info.Id);
                var prefix = new HarmonyMethod(typeof(Main).GetMethod(nameof(HintPositionPrefix), BindingFlags.Static | BindingFlags.NonPublic));
                _harmony.Patch(target, prefix: prefix);
                _log.Log("Hooked HintView.UpdateHintPosition for panel-correct hint placement.");
            }
            catch (Exception e) { _log.Log("Hint hook failed (" + e.Message + "); centre-screen hints stay vanilla."); }
        }

        static bool HintPositionPrefix(object __instance)
        {
            try
            {
                if (!_attached || _pickCam == null) return true;                    // flat mode: vanilla
                var parent = _hintParentField.GetValue(__instance) as RectTransform;
                var rect   = _hintRectField.GetValue(__instance)   as RectTransform;
                if (parent == null || rect == null) return true;
                var canvas = parent.GetComponentInParent<Canvas>();
                if (canvas == null) return true;
                var root = canvas.rootCanvas;
                if (root == null || root.renderMode != RenderMode.WorldSpace || root.worldCamera != _pickCam)
                    return true;                                                    // not our geometry: vanilla
                Vector2 cursor = _hintCursorPosProp != null
                    ? (Vector2)_hintCursorPosProp.GetValue(null, null)
                    : (Vector2)Input.mousePosition;
                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, cursor, _pickCam, out local))
                    return true;                                                    // even the pick ray missed: vanilla
                if (_hintLimitRectMethod != null)
                    local = (Vector2)_hintLimitRectMethod.Invoke(null, new object[] { local, parent, rect });
                rect.localPosition = local;
                return false;                                                       // placed; skip the original
            }
            catch { return true; }                                                  // any surprise: vanilla
        }

        // Hook Cursor.SetCursor so we learn which texture the game is showing. The texture is a real game
        // asset, so wearing it on the reticle gives the genuine pointer plus its context icons (talk, attack,
        // â€¦). Failure here is non-fatal: the reticle simply stays on the built-in crosshair.
        static void TryHookCursor(UnityModManager.ModEntry modEntry)
        {
            try
            {
                _harmony = new Harmony(modEntry.Info.Id);
                var target = typeof(Cursor).GetMethod("SetCursor",
                    new[] { typeof(Texture2D), typeof(Vector2), typeof(CursorMode) });
                if (target == null) { _log.Log("Cursor.SetCursor not found; reticle stays on the crosshair."); return; }
                var postfix = typeof(Main).GetMethod(nameof(OnSetCursor), BindingFlags.Static | BindingFlags.NonPublic);
                _harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                _log.Log("Hooked Cursor.SetCursor for the reticle.");
            }
            catch (Exception e) { _log.Log("Cursor hook failed (" + e.Message + "); reticle stays on the crosshair."); }
        }

        // Hook Unity's scene-load entry points so we can relinquish the scene attachment the instant the game
        // requests a load - before it frees the menu/area objects our eye cameras and converted canvas point at.
        // These are engine methods, so they fire at request time and don't move between game patches; Addressables
        // routes through them too. If the game uses a loader these don't see, the Camera.main safety net catches it.
        static void TryHookSceneLoads(UnityModManager.ModEntry modEntry)
        {
            try
            {
                if (_harmony == null) _harmony = new Harmony(modEntry.Info.Id);
                var prefix = new HarmonyMethod(typeof(Main).GetMethod(nameof(OnSceneOp), BindingFlags.Static | BindingFlags.NonPublic));
                int n = 0;
                foreach (var m in typeof(SceneManager).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name == "LoadSceneAsync" || m.Name == "UnloadSceneAsync")
                    {
                        try { _harmony.Patch(m, prefix: prefix); n++; }
                        catch (Exception e) { _log.Log("  scene-load patch skipped: " + m.Name + " (" + e.Message + ")"); }
                    }
                }
                _log.Log("Hooked SceneManager scene-load (" + n + " method(s)) so VR suspends across loads.");
            }
            catch (Exception e) { _log.Log("Scene-load hook failed (" + e.Message + "); relying on the Camera.main safety net."); }
        }

        // 0.6.88 (M4/PBD): grow SimulationPass's camera array so Camera.GetAllCameras stops throwing.
        // The game sizes the array to kMaxCamerasCount = 4; RTVR adds TWO enabled cameras (the eyes;
        // the UI panel reuses the game's own UICamera via per-frame retargeting), which
        // push Camera.allCamerasCount past it wherever the scene runs an extra camera of its own (the
        // bridge), and GetAllCameras throws BEFORE filling anything -- aborting the PBD simulation on
        // every tick. That one throw is the 'SimulationPass' render-graph error flood, the bridge fps
        // collapse, and (predicted) the holo-table eye freeze. The rest of the method is capacity-safe
        // by IL inspection: at most four view-proj matrices are consumed and the shader receives a
        // dynamic _CamerasCount, so growing the array is semantically transparent -- the cull kernel
        // unions frustums, so extra cameras can only cull LESS, never wrongly hide content.
        static FieldInfo _pbdPassCamerasField;   // SimulationPass.m_Cameras (the persistent source)
        static FieldInfo _pbdDataCamerasField;   // SimulationPassData.Cameras (what GetAllCameras receives)

        static void TryPatchPbdCameraCulling(UnityModManager.ModEntry modEntry)
        {
            try
            {
                if (_harmony == null) _harmony = new Harmony(modEntry.Info.Id);
                var passType = Type.GetType("Owlcat.Runtime.Visual.Waaagh.RendererFeatures.PositionBasedDynamics.Passes.SimulationPass, Owlcat.Runtime.Visual");
                var dataType = Type.GetType("Owlcat.Runtime.Visual.Waaagh.RendererFeatures.PositionBasedDynamics.Passes.SimulationPassData, Owlcat.Runtime.Visual");
                if (passType == null || dataType == null)
                {
                    _log.Log("[pbd] SimulationPass types not found; camera-array patch skipped.");
                    return;
                }
                var target = passType.GetMethod("CameraCulling", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                _pbdPassCamerasField = passType.GetField("m_Cameras", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                _pbdDataCamerasField = dataType.GetField("Cameras", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (target == null || _pbdPassCamerasField == null || _pbdDataCamerasField == null)
                {
                    _log.Log("[pbd] CameraCulling or its camera fields not found; patch skipped.");
                    return;
                }
                var prefix = new HarmonyMethod(typeof(Main).GetMethod(nameof(PbdCameraCulling_Prefix), BindingFlags.Static | BindingFlags.NonPublic));
                _harmony.Patch(target, prefix: prefix);
                _log.Log("[pbd] SimulationPass.CameraCulling camera-array growth patch applied.");
            }
            catch (Exception e) { _log.Log("[pbd] patch failed (" + e.Message + "); PBD scenes may flood the log while VR is up."); }
        }

        static void PbdCameraCulling_Prefix(object __instance, object __0)
        {
            if (!_active) return;
            try
            {
                int need = Camera.allCamerasCount;
                var arr = _pbdPassCamerasField.GetValue(__instance) as Camera[];
                if (arr == null || arr.Length < need)
                {
                    arr = new Camera[need + 4];
                    _pbdPassCamerasField.SetValue(__instance, arr);
                    _log.Log("[pbd] camera array grown to " + arr.Length + " (allCamerasCount=" + need + ").");
                }
                var darr = _pbdDataCamerasField.GetValue(__0) as Camera[];
                if (darr == null || darr.Length < need)
                    _pbdDataCamerasField.SetValue(__0, arr);
            }
            catch { }
        }

        // Harmony prefix on the scene-load methods: just flag a suspend (performed on the next frame, on the main
        // thread, to avoid re-entering Unity from inside its own load call) and note the time so we don't resume
        // until loading settles.
        static void OnSceneOp(MethodBase __originalMethod)
        {
            if (!_active) return;
            _lastTransitionTime = Time.unscaledTime;
            if (!_suspendRequested)   // many sub-scene Load/Unload calls fire together; log once per batch
            {
                _suspendRequested = true;
                _log.Log("[transition] scene op '" + __originalMethod.Name + "' -> suspend requested.");
            }
        }

        static void SubscribeSceneEvents()
        {
            SceneManager.sceneLoaded        += OnSceneLoadedEvt;
            SceneManager.sceneUnloaded      += OnSceneUnloadedEvt;
            SceneManager.activeSceneChanged += OnActiveSceneEvt;
        }
        static void UnsubscribeSceneEvents()
        {
            SceneManager.sceneLoaded        -= OnSceneLoadedEvt;
            SceneManager.sceneUnloaded      -= OnSceneUnloadedEvt;
            SceneManager.activeSceneChanged -= OnActiveSceneEvt;
        }
        static void OnSceneLoadedEvt(Scene s, LoadSceneMode m) => NoteSceneEvent("loaded '" + s.name + "'");
        static void OnSceneUnloadedEvt(Scene s)                => NoteSceneEvent("unloaded '" + s.name + "'");
        static void OnActiveSceneEvt(Scene a, Scene b)
        {
            string bn = b.IsValid() ? b.name : "?";
            NoteSceneEvent("active -> '" + bn + "'");
            // 0.6.117 (doll-panel fix, part 1): restore the HUD synchronously when the service-window
            // scene activates, ahead of the runner's detach. The inventory binds while RTVR is still
            // attached; restoring here shortens the window in which game UI can pose itself against our
            // rotated chain, and runs RestoreHudCanvases (which carries the swing preservation) at the
            // earliest safe moment. The runner's detach re-runs it on an emptied, idempotent state, and
            // the per-frame placement loop no-ops over the cleared list in between.
            if (_active && bn == "UI_Surface_Scene"
                && _attached && (_savedCanvases.Count > 0 || _redirectedCanvases.Count > 0 || _uiRoot != null))
            {
                _log.Log("[restore] early restore on UI-scene activation (pre-bind).");
                RestoreHudCanvases();
            }
        }
        // A scene event also forces a suspend (belt-and-braces) and pushes back the resume timer, so we wait for
        // the whole multi-scene area load to finish before rebuilding.
        static void NoteSceneEvent(string what)
        {
            if (!_active) return;
            _suspendRequested = true;
            _lastTransitionTime = Time.unscaledTime;
            _log.Log("[transition] " + what);
        }

        // Harmony postfix â€” runs after the game sets its hardware cursor. Just records it (cheap); the reticle
        // is rebuilt on the next frame inside the VR loop. Runs whether or not VR is active.
        static void OnSetCursor(Texture2D texture, Vector2 hotspot, CursorMode cursorMode)
        {
            _gameCursorTex = texture;
            _gameCursorHotspot = hotspot;
            _cursorDirty = true;
        }

        // Parse cfg key=value lines into a settings object. uiPos is rebuilt from scratch so the file fully
        // defines placement (a slot with no pos. lines means every element is at its default). Scalars absent
        // from the file keep their current value; all are clamped to range at the end.
        static void ParseInto(Settings cfg, string[] lines)
        {
            cfg.uiPos.Clear();
            bool loadedTemporalAa = false, loadedDisableAa = false;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                if      (key == "worldScale"  && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ws)) cfg.worldScale  = ws;
                else if (key == "ipdScale"    && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ip)) cfg.ipdScale    = ip;
                else if (key == "includeRoll" && bool.TryParse(val, out var rl)) cfg.includeRoll = rl;
                else if (key == "uiEnabled"   && bool.TryParse(val, out var ue)) cfg.uiEnabled   = ue;
                else if (key == "touchGestureHelp" && bool.TryParse(val, out var tgh)) cfg.touchGestureHelp = tgh;
                else if (key == "touchHandsVisible" && bool.TryParse(val, out var thv)) cfg.touchHandsVisible = thv;
                else if (key == "touchContextHints" && bool.TryParse(val,out var tch)) cfg.touchContextHints=tch;
                else if (key == "touchCombatGroundDoubleClickHead" && bool.TryParse(val,out var tcdh)) cfg.touchCombatGroundDoubleClickHead=tcdh;
                else if (key == "uiDistance"  && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ud)) cfg.uiDistance  = ud;
                else if (key == "uiWidth"     && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var uw)) cfg.uiWidth     = uw;
                else if (key == "uiElementScale" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ues)) cfg.uiElementScale = ues;
                else if (key == "pcHudMinimal" && bool.TryParse(val, out var phm)) cfg.pcHudMinimal = phm;
                else if (key == "uiMenuScale" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ums)) cfg.uiMenuScale = ums;
                else if (key == "uiMenuWidth" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var umw)) cfg.uiMenuWidth = umw;
                else if (key == "uiMenuDistance" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var umd)) cfg.uiMenuDistance = umd;
                else if (key == "uiMenuAspect" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var uma)) cfg.uiMenuAspect = uma;
                else if (key == "uiMenuOffsetX" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var umx)) cfg.uiMenuOffsetX = umx;
                else if (key == "uiMenuOffsetY" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var umy)) cfg.uiMenuOffsetY = umy;
                else if (key == "engineEffectProfile" && int.TryParse(val, out var eep)) cfg.engineEffectProfile = eep;
                else if (key == "combatMinimumEffects" && bool.TryParse(val, out var cme)) cfg.combatMinimumEffects = cme;
                else if (key == "uiAspect" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ua)) cfg.uiAspect = ua;
                else if (key == "uiOffsetX" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ux)) cfg.uiOffsetX = ux;
                else if (key == "uiOffsetY" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var uy)) cfg.uiOffsetY = uy;
                else if (key == "uiRasterMode" && int.TryParse(val, out var urm)) cfg.uiRasterMode = urm;
                else if (key == "uiFullResolution" && bool.TryParse(val, out var ufr)) cfg.uiFullResolution = ufr;
                else if (key == "cinematicVr" && bool.TryParse(val, out var cvr)) cfg.cinematicVr = cvr;
                else if (key == "tutorialVr" && bool.TryParse(val, out var tvr)) cfg.tutorialVr = tvr;
                else if (key == "dialogVr" && bool.TryParse(val, out var dvr)) cfg.dialogVr = dvr;
                else if (key == "familiarEffectsOnY76" && bool.TryParse(val,out var fey76)) cfg.familiarEffectsOnY76=fey76;
                else if (key == "coalesceHudLayout" && bool.TryParse(val, out var chl)) cfg.coalesceHudLayout = chl;
                else if (key == "skipFullyFadedScenery" && bool.TryParse(val, out var sffs)) cfg.skipFullyFadedScenery = sffs;
                else if (key == "stableHudCapture" && bool.TryParse(val, out var shc)) cfg.stableHudCapture = shc;
                else if (key == "isolateOvertipBatches" && bool.TryParse(val, out var iob)) cfg.isolateOvertipBatches = iob;
                else if (key == "combatSurfaceIntensity" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var csi) && !float.IsNaN(csi)) cfg.combatSurfaceIntensity = Mathf.Clamp(csi,.25f,1f);
                else if (key == "combatUnitFxIntensity" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cui) && !float.IsNaN(cui)) cfg.combatUnitFxIntensity = Mathf.Clamp(cui,.25f,1f);
                else if (key == "combatOutlineMode" && int.TryParse(val, out var com)) cfg.combatOutlineMode = Mathf.Clamp(com,0,3);
                else if (key == "preparationAuraEnabled" && bool.TryParse(val, out var pae)) cfg.preparationAuraEnabled = pae;
                else if (key == "combatOutlineIntensity" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var coi) && !float.IsNaN(coi)) cfg.combatOutlineIntensity = Mathf.Clamp(coi,.1f,1f);
                else if (key == "lockedCrosshair" && bool.TryParse(val, out var lc)) cfg.lockedCrosshair = lc;
                else if (key == "autoStartVr" && bool.TryParse(val, out var av)) cfg.autoStartVr = av;
                else if (key == "worldOvertips" && bool.TryParse(val, out var wo)) cfg.worldOvertips = wo;
                else if (key == "markerBoostCap" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var mb)) cfg.markerBoostCap = mb;
                else if (key == "renderScale" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var rsc)) cfg.renderScale = rsc;
                else if (key == "temporalAA" && bool.TryParse(val, out var taa)) { cfg.temporalAA = taa; loadedTemporalAa = true; }
                else if (key == "disableAA" && bool.TryParse(val, out var noaa)) { cfg.disableAA = noaa; loadedDisableAa = true; }
                else if (key == "neuralMode" && int.TryParse(val, out var neural)) cfg.neuralMode = Mathf.Clamp(neural, 0, 3);
                else if (key == "neuralScale" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ns) && !float.IsNaN(ns)) cfg.neuralScale = Mathf.Clamp(ns, .5f, 1f);
                else if (key == "neuralPreset" && int.TryParse(val, out var np)) cfg.neuralPreset = Mathf.Clamp(np, 0, 1);
                else if (key == "neuralSharpness" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var nsh) && !float.IsNaN(nsh)) cfg.neuralSharpness = Mathf.Clamp01(nsh);
                else if (key == "taaSharpness" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var ts) && !float.IsNaN(ts)) cfg.taaSharpness = Mathf.Clamp01(ts);
                else if (key == "allowGameFsr" && bool.TryParse(val, out var fsr)) cfg.allowGameFsr = fsr;
                else if (key == "skipDesktopWorld" && bool.TryParse(val, out var skip)) cfg.skipDesktopWorld = skip;
                else if (key == "visibleRegionCulling" && bool.TryParse(val, out var vc)) cfg.visibleRegionCulling = vc;
                else if (key == "indirectVisibleRegionCulling" && bool.TryParse(val, out var ivc)) cfg.indirectVisibleRegionCulling = ivc;
                else if (key == "detailedProfiling" && bool.TryParse(val, out var dp)) cfg.detailedProfiling = dp;
                else if (key == "syncForcedVisibility" && bool.TryParse(val, out var fv)) cfg.syncForcedVisibility = fv;
                else if (key == "drawDistanceMode" && int.TryParse(val, out var dd)) cfg.drawDistanceMode = DrawDistanceOptions.Normalize(dd);
                else if (key == "drawDistanceCustom" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var dc)) cfg.drawDistanceCustom = DrawDistanceOptions.SanitizeCustom(dc);
                else if (key == "touchDrawDistanceShortcut" && bool.TryParse(val, out var tds)) cfg.touchDrawDistanceShortcut = tds;
                else if (key == "touchThirdPersonFollow" && bool.TryParse(val, out var tpf)) cfg.touchThirdPersonFollow = tpf;
                else if (key == "touchFollowRecenter" && bool.TryParse(val, out var tr80)) cfg.touchFollowRecenter = tr80;
                else if (key == "openXrRuntimeManifest80") cfg.openXrRuntimeManifest80 = val;
                else if (key == "modMenuOffsetX" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var mx80)) cfg.modMenuOffsetX = mx80;
                else if (key == "modMenuOffsetY" && float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var my80)) cfg.modMenuOffsetY = my80;
                else if (key == "openXrRuntime" && int.TryParse(val, out var oxr)) cfg.openXrRuntime = OpenXrRuntimeSelection.Normalize(oxr);
                else if (key == "modLanguage" && int.TryParse(val, out var ml)) cfg.modLanguage = Math.Max(-1, Math.Min(1, ml));
                else if (key == "adaptiveDistance" && bool.TryParse(val, out var ad)) cfg.adaptiveDistance = ad;
                else if (key == "useGamePointerCache" && bool.TryParse(val, out var pc)) cfg.useGamePointerCache = pc;
                else if (key == "desktopMirror" && bool.TryParse(val, out var dmr)) cfg.desktopMirror = dmr;
                else if (key == "mirrorCursor" && bool.TryParse(val, out var mcr)) cfg.mirrorCursor = mcr;
                else if (Release48Settings.TryParse(cfg, key, val)) { }
                else if (key == "engineCadenceEnabled" && bool.TryParse(val, out var ece)) cfg.engineCadenceEnabled = ece;
                else if (key == "ofxrEnabled77" && bool.TryParse(val, out var ofxr77)) cfg.ofxrEnabled77 = ofxr77;
                else if (key == "combatPresentationMask74" && int.TryParse(val, out var cpm74)) cfg.combatPresentationMask74 = cpm74 & 15;
                else if (key == "engineOptimizationsEnabled" && bool.TryParse(val, out var eoe)) cfg.engineOptimizationsEnabled = eoe;
                else if (key == "engineOptimizationMask" && int.TryParse(val, out var eom)) cfg.engineOptimizationMask = eom & EngineOptimizationPolicy58.All;
                else if (key == "allDiagnosticsEnabled" && bool.TryParse(val, out var ade)) cfg.allDiagnosticsEnabled = ade;
                else if (key == "diagnosticsRevision77" && int.TryParse(val, out var dr77)) cfg.diagnosticsRevision77 = dr77;
                else if (key == "engineCadenceMode" && int.TryParse(val, out var ecm)) cfg.engineCadenceMode = EngineCadenceSchedule.NormalizeMode(ecm);
                else if (SpatialPanelSettings.TryParse(cfg, key, val)) { }
                else if (ComfortCameraOptions.TryParse(cfg.comfort, key, val)) { }
                else if (key.StartsWith("pos.") && key.Length > 4)
                {
                    var p = val.Split(',');
                    if (p.Length == 2
                        && float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var fx)
                        && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var fy))
                        cfg.uiPos[key.Substring(4)] = new Vector2(Mathf.Clamp(fx, UiPosMin, UiPosMax), Mathf.Clamp(fy, UiPosMin, UiPosMax));
                }
            }
            // Loading an older preset must restore its TAA/SMAA choice even
            // when the current session selected Off before loading it.
            if (loadedTemporalAa && !loadedDisableAa) cfg.disableAA = false;
            cfg.worldScale = Release48Settings.WorldScale(cfg.worldScale, cfg.limitExplorationZoom);
            cfg.ipdScale   = Mathf.Clamp(cfg.ipdScale, IpdMin, IpdMax);
            cfg.uiDistance = TouchPointerMath.Finite(cfg.uiDistance) ? Mathf.Clamp(cfg.uiDistance, UiDistMin, UiDistMax) : ApprovedUserDefaults.Distance;
            cfg.uiWidth    = TouchPointerMath.Finite(cfg.uiWidth) ? Mathf.Clamp(cfg.uiWidth, UiWidthMin, UiWidthMax) : ApprovedUserDefaults.Width;
            cfg.uiElementScale = HudPanelLayout.ClampElementScale(cfg.uiElementScale);
            cfg.uiMenuScale = TouchPointerMath.Finite(cfg.uiMenuScale) ? Mathf.Clamp(cfg.uiMenuScale, .65f, 1.5f) : ApprovedUserDefaults.MenuText;
            cfg.uiMenuWidth = TouchPointerMath.Finite(cfg.uiMenuWidth) ? Mathf.Clamp(cfg.uiMenuWidth, .45f, 1f) : ApprovedUserDefaults.MenuWidth;
            cfg.uiMenuDistance = TouchPointerMath.Finite(cfg.uiMenuDistance) ? Mathf.Clamp(cfg.uiMenuDistance, .5f, 3f) : ApprovedUserDefaults.MenuDistance;
            cfg.uiMenuAspect = !TouchPointerMath.Finite(cfg.uiMenuAspect) ? ApprovedUserDefaults.MenuAspect : cfg.uiMenuAspect > 0 ? Mathf.Clamp(cfg.uiMenuAspect, .8f, 2.4f) : 0;
            cfg.uiMenuOffsetX = TouchPointerMath.Finite(cfg.uiMenuOffsetX) ? Mathf.Clamp(cfg.uiMenuOffsetX, -.65f, .65f) : ApprovedUserDefaults.MenuX;
            cfg.uiMenuOffsetY = TouchPointerMath.Finite(cfg.uiMenuOffsetY) ? Mathf.Clamp(cfg.uiMenuOffsetY, -.65f, .65f) : ApprovedUserDefaults.MenuY;
            SpatialPanelSettings.Normalize(cfg);
            cfg.modMenuOffsetX = PanelFit80.Offset(cfg.modMenuOffsetX);
            cfg.modMenuOffsetY = PanelFit80.Offset(cfg.modMenuOffsetY);
            cfg.engineEffectProfile = EngineEffectPolicy.Normalize(cfg.engineEffectProfile);
            cfg.uiAspect = !TouchPointerMath.Finite(cfg.uiAspect) ? ApprovedUserDefaults.Aspect : cfg.uiAspect <= 0 ? 0 : HudPanelLayout.ClampAspect(cfg.uiAspect);
            cfg.uiOffsetX = TouchPointerMath.Finite(cfg.uiOffsetX) ? HudPanelLayout.ClampOffset(cfg.uiOffsetX) : ApprovedUserDefaults.OffsetX;
            cfg.uiOffsetY = TouchPointerMath.Finite(cfg.uiOffsetY) ? HudPanelLayout.ClampOffset(cfg.uiOffsetY) : ApprovedUserDefaults.OffsetY;
            cfg.uiRasterMode = HudRasterPolicy.Normalize(cfg.uiRasterMode);
            cfg.markerBoostCap = Mathf.Clamp(cfg.markerBoostCap, MarkerBoostMin, MarkerBoostMax);
            if (ReferenceEquals(cfg, _cfg)) ResetComfortCameraReference();
        }

        // Serialize a settings object to the cfg key=value block (used for both the active file and presets).
        static string Serialize(Settings cfg)
        {
            var c = CultureInfo.InvariantCulture;
            string s = string.Format(c,
                "worldScale={0:0.0000}\nipdScale={1:0.0000}\nincludeRoll={2}\nuiEnabled={3}\nuiDistance={4:0.0000}\nuiWidth={5:0.0000}\nlockedCrosshair={6}\nautoStartVr={7}\nworldOvertips={8}\nmarkerBoostCap={9:0.0000}\nrenderScale={10:0.0000}\ndesktopMirror={11}\nmirrorCursor={12}\n",
                cfg.worldScale, cfg.ipdScale, cfg.includeRoll, cfg.uiEnabled, cfg.uiDistance, cfg.uiWidth, cfg.lockedCrosshair, cfg.autoStartVr, cfg.worldOvertips, cfg.markerBoostCap, cfg.renderScale, cfg.desktopMirror, cfg.mirrorCursor);
            s += "openXrRuntime=" + cfg.openXrRuntime + "\n";
            s += "modLanguage=" + cfg.modLanguage + "\n";
            s += "touchContextHints=" + cfg.touchContextHints + "\ntouchCombatGroundDoubleClickHead=" + cfg.touchCombatGroundDoubleClickHead + "\n";
            s += "temporalAA=" + cfg.temporalAA + "\nallowGameFsr=" + cfg.allowGameFsr + "\nskipDesktopWorld=" + cfg.skipDesktopWorld + "\n";
            s += "disableAA=" + cfg.disableAA + "\n";
            s += "touchGestureHelp=" + cfg.touchGestureHelp + "\ntouchHandsVisible=" + cfg.touchHandsVisible + "\n";
            s += ComfortCameraOptions.Serialize(cfg.comfort);
            s += "neuralMode=" + cfg.neuralMode + "\n";
            s += "neuralScale=" + cfg.neuralScale.ToString("R", c) + "\nneuralPreset=" + cfg.neuralPreset + "\nneuralSharpness=" + cfg.neuralSharpness.ToString("R", c) + "\n";
            s += "taaSharpness=" + cfg.taaSharpness.ToString("R", CultureInfo.InvariantCulture) + "\n";
            s += "visibleRegionCulling=" + cfg.visibleRegionCulling + "\n";
            s += "indirectVisibleRegionCulling=" + cfg.indirectVisibleRegionCulling + "\n";
            s += "detailedProfiling=" + cfg.detailedProfiling + "\n";
            s += "syncForcedVisibility=" + cfg.syncForcedVisibility + "\nuseGamePointerCache=" + cfg.useGamePointerCache + "\n";
            s += "drawDistanceMode=" + cfg.drawDistanceMode + "\n";
            s += "drawDistanceCustom=" + cfg.drawDistanceCustom.ToString("R", CultureInfo.InvariantCulture) + "\n";
            s += "touchDrawDistanceShortcut=" + cfg.touchDrawDistanceShortcut + "\n";
            s += "touchThirdPersonFollow=" + cfg.touchThirdPersonFollow + "\n";
            s += "touchFollowRecenter=" + cfg.touchFollowRecenter + "\n";
            s += "openXrRuntimeManifest80=" + (cfg.openXrRuntimeManifest80 ?? "").Replace("\r", "").Replace("\n", "") + "\n";
            s += "modMenuOffsetX=" + cfg.modMenuOffsetX.ToString("R", CultureInfo.InvariantCulture) + "\n";
            s += "modMenuOffsetY=" + cfg.modMenuOffsetY.ToString("R", CultureInfo.InvariantCulture) + "\n";
            s += "adaptiveDistance=" + cfg.adaptiveDistance + "\n";
            s += "uiAspect=" + cfg.uiAspect.ToString("R", CultureInfo.InvariantCulture) + "\n";
            s += "uiElementScale=" + cfg.uiElementScale.ToString("R", CultureInfo.InvariantCulture) + "\n";
            s += "pcHudMinimal=" + cfg.pcHudMinimal + "\n";
            s += "uiMenuScale=" + cfg.uiMenuScale.ToString("R", CultureInfo.InvariantCulture) + "\n";
            s += "uiMenuWidth=" + cfg.uiMenuWidth.ToString("R", CultureInfo.InvariantCulture) + "\n";
            s += "uiMenuDistance=" + cfg.uiMenuDistance.ToString("R", CultureInfo.InvariantCulture) + "\n";
            s += "uiMenuAspect=" + cfg.uiMenuAspect.ToString("R", CultureInfo.InvariantCulture) + "\n";
            s += "uiMenuOffsetX=" + cfg.uiMenuOffsetX.ToString("R", CultureInfo.InvariantCulture) + "\n";
            s += "uiMenuOffsetY=" + cfg.uiMenuOffsetY.ToString("R", CultureInfo.InvariantCulture) + "\n";
            s += SpatialPanelSettings.Serialize(cfg);
            s += Release48Settings.Serialize(cfg);
            s += "engineEffectProfile=" + cfg.engineEffectProfile + "\n";
            s += "combatMinimumEffects=" + cfg.combatMinimumEffects + "\n";
            s += "engineCadenceEnabled=" + cfg.engineCadenceEnabled + "\nengineCadenceMode=" + cfg.engineCadenceMode + "\n";
            s += "combatPresentationMask74=" + cfg.combatPresentationMask74 + "\n";
            s += "engineOptimizationsEnabled=" + cfg.engineOptimizationsEnabled + "\nengineOptimizationMask=" + cfg.engineOptimizationMask + "\n";
            s += "allDiagnosticsEnabled=" + cfg.allDiagnosticsEnabled + "\n";
            s += "ofxrEnabled77=" + cfg.ofxrEnabled77 + "\n";
            s += "diagnosticsRevision77=" + cfg.diagnosticsRevision77 + "\n";
            s += "uiOffsetX=" + cfg.uiOffsetX.ToString("R", CultureInfo.InvariantCulture) + "\n";
            s += "uiOffsetY=" + cfg.uiOffsetY.ToString("R", CultureInfo.InvariantCulture) + "\n";
            s += "uiRasterMode=" + cfg.uiRasterMode + "\nuiFullResolution=" + cfg.uiFullResolution + "\n";
            s += "cinematicVr=" + cfg.cinematicVr + "\ntutorialVr=" + cfg.tutorialVr + "\ndialogVr=" + cfg.dialogVr + "\n";
            s += "familiarEffectsOnY76=" + cfg.familiarEffectsOnY76 + "\n";
            s += "coalesceHudLayout=" + cfg.coalesceHudLayout + "\n";
            s += "skipFullyFadedScenery=" + cfg.skipFullyFadedScenery + "\n";
            s += "stableHudCapture=" + cfg.stableHudCapture + "\n";
            s += "isolateOvertipBatches=" + cfg.isolateOvertipBatches + "\n";
            s += "combatSurfaceIntensity=" + cfg.combatSurfaceIntensity.ToString("R",c) + "\ncombatUnitFxIntensity=" + cfg.combatUnitFxIntensity.ToString("R",c) + "\n";
            s += "combatOutlineMode=" + cfg.combatOutlineMode + "\ncombatOutlineIntensity=" + cfg.combatOutlineIntensity.ToString("R",c) + "\n";
            s += "preparationAuraEnabled=" + cfg.preparationAuraEnabled + "\n";
            foreach (var kv in cfg.uiPos)
                s += string.Format(c, "pos.{0}={1:0.000},{2:0.000}\n", kv.Key, kv.Value.x, kv.Value.y);
            return s;
        }

        static void LoadSettings()
        {
            try { if (File.Exists(_settingsPath)) ParseInto(_cfg, File.ReadAllLines(_settingsPath));
                else QualityPresetPolicy.Apply(_cfg, 1, NvidiaQuality); }
            catch (Exception e) { _log.Log("Settings load failed (" + e.Message + "); using defaults."); }
            if (_cfg.controlsLayoutRevision < 52)
            {
                // Explicit new defaults requested for the reminders only. All other
                // personal graphics, panels, controllers and presets remain untouched.
                _cfg.touchGestureHelp=false; _cfg.touchHintX=-.38f; _cfg.touchHintY=-.50f;
                _cfg.touchContextHints=false; _cfg.touchHintX=0; _cfg.touchHintY=0; _cfg.touchHintSize=1;
                _cfg.controlsLayoutRevision=52; MarkSettingsDirty();
            }
            EnsureCustomSettings();
        }

        static void SaveSettings()
        {
            try { SettingsFiles76.Save(_settingsPath, Serialize(PersistentSettings())); }
            catch (Exception e) { _log.Log("Settings save failed: " + e.Message); }
        }

        // Presets: each slot is a snapshot file in the same format. Save writes the current settings into a slot;
        // Load recalls a slot and makes it the active config (then persists it as the active file).
        static void SavePreset(int i)
        {
            try { SettingsFiles76.Save(_presetPaths[i], Serialize(_cfg)); _log.Log("Saved " + PresetNames[i] + "."); }
            catch (Exception e) { _log.Log("Preset save failed: " + e.Message); }
        }

        static void LoadPreset(int i)
        {
            try
            {
                if (!File.Exists(_presetPaths[i])) { _log.Log(PresetNames[i] + " is empty - save it first."); return; }
                int chosenRuntime = _cfg.openXrRuntime;
                string chosenManifest80 = _cfg.openXrRuntimeManifest80;
                bool chosenDiagnostics = AllDiagnosticsEnabled;
                bool chosenOfxr77 = _cfg.ofxrEnabled77;
                ParseInto(_cfg, File.ReadAllLines(_presetPaths[i]));
                _cfg.openXrRuntime = chosenRuntime; // View presets do not change the next launch's connection.
                _cfg.openXrRuntimeManifest80 = chosenManifest80;
                _cfg.allDiagnosticsEnabled = _cfg.detailedProfiling = chosenDiagnostics;
                _cfg.ofxrEnabled77 = chosenOfxr77;
                _cfg.diagnosticsRevision77 = 77;
                ApplyAllDiagnosticsSetting();
                _reticleState = -1;   // re-apply the reticle look (crosshair on/off) on the next frame
                SaveSettings();       // the loaded preset is now the active config
                _log.Log("Loaded " + PresetNames[i] + ".");
            }
            catch (Exception e) { _log.Log("Preset load failed: " + e.Message); }
        }

        static bool Combo(KeyCode k)
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt  = Input.GetKey(KeyCode.LeftAlt)     || Input.GetKey(KeyCode.RightAlt);
            return ctrl && alt && Input.GetKeyDown(k);
        }

        static bool _autoStartArmed;
        static float _nextAutoStart = 5;
        static float _startupStableSince = -1;
        static int _startupWidth, _startupHeight;
        static FullScreenMode _startupDisplayMode;
        static readonly bool _launcherVrRequested = Array.IndexOf(Environment.GetCommandLineArgs(), "--rtmaquetaxr-vr") >= 0;
        static void ArmOpenXR() { _autoStartArmed = _launcherVrRequested; }

        static void OnUpdate(UnityModManager.ModEntry modEntry, float dt)
        {
            UpdateModLanguage();
            OpenXR.PumpShutdown();
            NeuralTemporalHook.PumpMaintenance();
            if (ConsumeTouchCameraStopRequest(out string cameraFailure))
            {
                _autoStartArmed = false;
                StopVr();
                _log.Error("[touch/camera] VR stopped after camera failure: " + cameraFailure);
                return;
            }
            TickLiveOverlayInput();
            TickHudRasterSurface();
            CheckRestart();
            HeartbeatTick();   // 0.6.89: 1 Hz heartbeat report + right-eye probe enforcement
            if (_restoreCheckDue > 0f && Time.realtimeSinceStartup >= _restoreCheckDue)
            {
                _restoreCheckDue = 0f;
                VerifyRestoredCanvases("+2s");
                _restoreChecks.Clear();
            }
            long uiWatchStarted = DiagnosticTimestamp();
            LateConvertWatcher();   // mid-attach spawns and combat-entry reversion self-heal
            if (_active) RecordModStage("UiWatcher", uiWatchStarted);
            if (_settingsDirty && Time.unscaledTime - _lastSettingsTouch > 0.4f) { _settingsDirty = false; SaveSettings(); }

            if (Combo(KeyCode.V)) { if (!_active) { _autoStartArmed = true; StartVr(); } else { _autoStartArmed = false; StopVr(); } return; }
            if (Combo(KeyCode.Y)) OpenXR.FlipEyes = !OpenXR.FlipEyes;
            if (Combo(KeyCode.W)) { ToggleWorldOvertips(); return; }   // toggle: float the entity overtips into the world over their units (vs flat on the HUD panel)
            if (Combo(KeyCode.X)) { ToggleDesktopMirror(); return; }   // M5: manual desktop-mirror toggle (auto-shows in menus regardless, if enabled)

            if (_autoStartArmed && !_active && Time.realtimeSinceStartup >= _nextAutoStart)
            {
                StartVr();
            }

            if (Combo(KeyCode.P))            { DumpUiState(); }   // diagnostic: canvases + raycast under cursor (0.6.129: moved above the VR gate - works with VR off, e.g. for the chrome deactivate-vs-hide dump)

            if (!_active) return;

            // The closed Touch camera cannot be bypassed by legacy tuning keys.
            if (TouchInputOwned) return;
            if (Combo(KeyCode.A)) SetEyeAa(EyeAaOptions.Next(RequestedEyeAa));
            UpdateDrawDistanceControl();
            if (Combo(KeyCode.RightBracket)) { _cfg.worldScale = Mathf.Clamp(_cfg.worldScale * WorldScaleStep, WorldScaleMin, WorldScaleMax); AfterTune("worldScale"); }
            if (Combo(KeyCode.LeftBracket))  { _cfg.worldScale = Mathf.Clamp(_cfg.worldScale / WorldScaleStep, WorldScaleMin, WorldScaleMax); AfterTune("worldScale"); }
            if (Combo(KeyCode.Equals))       { _cfg.ipdScale   = Mathf.Clamp(_cfg.ipdScale + IpdStep, IpdMin, IpdMax); AfterTune("ipdScale"); }
            if (Combo(KeyCode.Minus))        { _cfg.ipdScale   = Mathf.Clamp(_cfg.ipdScale - IpdStep, IpdMin, IpdMax); AfterTune("ipdScale"); }
            if (Combo(KeyCode.Alpha0))       { _cfg.worldScale = 1f; _cfg.ipdScale = 1f; AfterTune("reset"); }
            if (Combo(KeyCode.C))            { _recenterPending = true; _log.Log("Head-look: recentre requested."); }
            if (Combo(KeyCode.R))            { _cfg.includeRoll = !_cfg.includeRoll; _log.Log("Head roll: " + _cfg.includeRoll); SaveSettings(); }

            if (Combo(KeyCode.U))            { SetUiEnabled(!_cfg.uiEnabled); }
            if (Combo(KeyCode.Period))       { SetUiDistance(_cfg.uiDistance + UiDistStep); }
            if (Combo(KeyCode.Comma))        { SetUiDistance(_cfg.uiDistance - UiDistStep); }
            if (Combo(KeyCode.Quote))        { SetUiWidth(_cfg.uiWidth + UiWidthStep); }
            if (Combo(KeyCode.Semicolon))    { SetUiWidth(_cfg.uiWidth - UiWidthStep); }

        }

        static void AfterTune(string what)
        {
            _log.Log(string.Format("Tuning ({0}): worldScale={1:0.000}  ipdScale={2:0.00}", what, _cfg.worldScale, _cfg.ipdScale));
            SaveSettings();
        }

        static void SetUiEnabled(bool on)
        {
            _cfg.uiEnabled = on;

            _log.Log("UI overlay: " + on);
            SaveSettings();
        }

        static void SetUiDistance(float d)
        {
            _cfg.uiDistance = Mathf.Clamp(d, UiDistMin, UiDistMax);

            _log.Log(string.Format("UI distance={0:0.00} m", _cfg.uiDistance));
            SaveSettings();
        }

        static void SetUiWidth(float w)
        {
            _cfg.uiWidth = Mathf.Clamp(w, UiWidthMin, UiWidthMax);

            _log.Log(string.Format("UI fill={0:0.00} of camera view", _cfg.uiWidth));
            SaveSettings();
        }

        static void StartVr()
        {
            if (!_launcherVrRequested) return;
            if (_active) return;
            try
            {
                // Unity applies display changes at a later frame boundary. Wait
                // for the menu/game and a stable surface BEFORE sharing its device.
                _autoStartArmed = true;
                if (!StartupSurfaceReady()) return;
                if (!_displayPrepared)
                {
                    _savedFullScreenMode = Screen.fullScreenMode;
                    _savedScreenWidth = Screen.width; _savedScreenHeight = Screen.height;
                    _displayPrepared = true;
                    SetHudRasterSurface();
                    _startupStableSince = -1;
                    _nextAutoStart = Time.realtimeSinceStartup + 0.25f;
                    OpenXR.Status = "Preparing window: " + HudRasterPolicy.Width(_cfg.uiRasterMode) + "x" + HudRasterPolicy.Height(_cfg.uiRasterMode);
                    LogStartupStatus(); return;
                }
                if (Screen.fullScreenMode != FullScreenMode.Windowed) return;
                _nextAutoStart = Time.realtimeSinceStartup + 10;
                _log.Log("[OpenXR] Superficie estable: " + Screen.width + "x" + Screen.height +
                    "; pantalla=" + Screen.fullScreenMode + "; modo=" + ReadGameMode());
                if (!OpenXR.Initialize(_modFolder, _cfg.renderScale)) { LogStartupStatus(); return; }
                OpenXR.SetDiagnosticsRecording(DiagnosticsRecording);
                _eyeW = OpenXR.Width; _eyeH = OpenXR.Height;
                EnsureEyeRts();
                _recenterPending = true;
                HbHook();
                _runnerGo = new GameObject("RTMaquetaXR_Runner");
                UnityEngine.Object.DontDestroyOnLoad(_runnerGo);
                _runner = _runnerGo.AddComponent<Runner>();
                _active = true; _attached = false;
                ResetQualityDiagnostics();
                _lastTransitionTime = Time.unscaledTime;
                _suspendStart = Time.unscaledTime;
                SubscribeSceneEvents();
                InstallComfortHook();
                InstallInputAndQualityHooks();
                InstallComfortCameraControls();
                InstallPerformanceHooks();
                InstallTouchGameInput();
                InstallCombatVisuals();
                StartLiveOverlay(BuildImageMenu(), LiveImageStatus);
                InstallTouchGuide();
                MaintainVrPacing();
                _log.Log("[OpenXR] SesiÃ³n " + OpenXrRuntime.Name + " iniciada. Ojos=" + _eyeW + "x" + _eyeH + "; escala=" + _cfg.worldScale + "; seguimiento 6DoF.");
            }
            catch (Exception exception)
            {
                _nextAutoStart = Time.realtimeSinceStartup + 10;
                _log.Error("[OpenXR] Inicio: " + exception);
                if (_active) StopVr(); else OpenXR.Shutdown();
            }
        }

        static bool StartupSurfaceReady()
        {
            float now = Time.realtimeSinceStartup;
            _nextAutoStart = now + 0.25f;
            bool ready = Camera.allCamerasCount > 0 && Screen.width >= 64 && Screen.height >= 64 &&
                (ResumingIntoMainMenu() || GameplayMode(ReadGameMode())) && !LoadingScreenShowing();
            if (!ready)
            {
                _startupStableSince = -1;
                OpenXR.Status = "Waiting for menu or gameplay to start OpenXR";
                LogStartupStatus(); return false;
            }
            if (_startupStableSince < 0 || _startupWidth != Screen.width || _startupHeight != Screen.height ||
                _startupDisplayMode != Screen.fullScreenMode)
            {
                _startupStableSince = now;
                _startupWidth = Screen.width; _startupHeight = Screen.height;
                _startupDisplayMode = Screen.fullScreenMode;
            }
            return now - _startupStableSince >= 2;
        }

        static RenderTexture NewRt(int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            try
            {
                if (!rt.Create()) throw new InvalidOperationException("No se pudo crear la textura del ojo");
                OpenXR.PrepareTexture(rt);
                return rt;
            }
            catch { ReleaseOutput(rt); throw; }
        }

        static void EnsureEyeRts()
        {
            if (_rtL == null) _rtL = NewRt(_eyeW, _eyeH);
            if (_rtR == null) _rtR = NewRt(_eyeW, _eyeH);
        }

        static void ReleaseEyeRts()
        {
            if (_rtL != null) { OpenXR.ForgetTexture(_rtL); _rtL.Release(); _rtL = null; }
            if (_rtR != null) { OpenXR.ForgetTexture(_rtR); _rtR.Release(); _rtR = null; }
        }

        static void StopVr()
        {
            ReleaseManagementBackdrop66();
            ReleaseNativeWorldUi66();
            RestoreNativeHintPlacement();
            RestoreSpaceBackdrop();
            RestoreFamiliarVisuals76();
            ReleaseSpaceBackgroundTexture();
            RestoreSpaceQualityProfile(true);
            StopOvertipBatchIsolation();
            RestoreCombatVisuals();
            StopEngineLoopProfiling();
            ResetCinematicSession();
            StopTouchGameInput();
            StopDiagnosticWriter();
            StopComfortCameraControls();
            StopLiveOverlay();
            StopLiveResolution();
            _startupStableSince = -1;
            RestoreFramePacing();
            if (!_active)
            {
                RestoreDisplayMode();
                return;
            }
            UnsubscribeSceneEvents();
            if (_attached) DetachFromScene();   // restores canvases + cursor and destroys the eye cameras
            UnhookCameraCallbacks();
            StopPerformanceHooks();
            // This backend never retargets UICamera; preserve its original flags.
            _geomLedger.Clear();          // 0.6.139: geometry truth is per-VR-run; the detach above already repaired game state
            RestoreOvertips();            // 0.6.132: undo the per-widget scale/rotation overrides too (includes the
                                          // 0.6.46 ZTest restore). VR stop with world-overtips ON left every widget
                                          // stranded until a game load (12.07, twice) - the canvas layer verified
                                          // perfect at the stop, isolating the widget layer. Mirrors manual toggle-off.
            if (!OpenXR.Shutdown() && !_appQuitting) NeuralRetirementPump.EnsureRunning();
            ReleaseFlatFrame();
            ReleaseLoadingStereo();
            if (_runnerGo != null) UnityEngine.Object.Destroy(_runnerGo);
            _runnerGo = null; _runner = null;
            ReleaseEyeRts();   // the ONLY place the eye RTs are freed; they persist across suspends (Family A)
            if (_uiRT != null) { _uiRT.Release(); _uiRT = null; }

            _active = false;
            _suspendRequested = false; _resumeCandidate = null; _resumeStable = 0;
            if (_hiddenPantograph != null) { _hiddenPantograph.SetActive(true); _hiddenPantograph = null; }
            RestoreDisplayMode();
            RestoreAllIsolation();   // 0.6.87 (M4): session-only skips never outlive VR
            _suspendRightEye = false;   // 0.6.89: the right-eye probe never outlives VR
            HbUnhook();   // 0.6.90: the heartbeat subscription never outlives VR
            _log.Log("VR STOP.");
        }

        static void RestoreDisplayMode()
        {
            if (!_displayPrepared) return;
            Screen.SetResolution(_savedScreenWidth, _savedScreenHeight, _savedFullScreenMode);
            _displayPrepared = false;
            _log.Log("Restored display to " + _savedScreenWidth + "x" + _savedScreenHeight + " / " + _savedFullScreenMode + " on VR stop.");
        }

        // --- 0.6.86 (M4): give the eye cameras the main camera's pipeline configuration. ---
        // The Waaagh pipeline reads per-camera settings from a WaaaghAdditionalCameraData
        // component; a camera without one is served a shared DEFAULT instance
        // (s_DefaultAdditionalCameraData) -- Owlcat's hard defaults, not the game's
        // configuration. That is why the graphics settings never applied to the eyes.
        // We add the component to each eye and copy the serialised m_* fields from the
        // live camera's instance, so the eyes render with whatever the game configured.
        static Type _camDataType;
        static FieldInfo[] _camDataFields;
        static bool _camDataProbed;
        static bool _camDataLoggedOnce;
        static int _camDataNextRefresh;
        static Camera _camDataSource;
        static long _camDataContext = -1;
        static PropertyInfo _camDataDefault;

        internal static void MaybeApplyEyeCameraData(Camera eyeL, Camera eyeR, Camera src)
        {
            if (src == null) return;
            if (!_camDataProbed)
            {
                _camDataProbed = true;
                _camDataType = Type.GetType("Owlcat.Runtime.Visual.Waaagh.WaaaghAdditionalCameraData, Owlcat.Runtime.Visual");
                if (_camDataType == null)
                {
                    _log.Log("[camdata] WaaaghAdditionalCameraData not found; eyes stay on pipeline defaults.");
                    return;
                }
                var keep = new List<FieldInfo>();
                var flds = _camDataType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < flds.Length; i++)
                {
                    var f = flds[i];
                    if (!f.Name.StartsWith("m_")) continue;                       // serialised state only
                    if (f.Name == "m_Camera" || f.Name == "m_Cameras") continue;  // identity / overlay stack -- never share
                    if (f.Name == "m_CameraType") continue;                       // 0.6.93 (12.9): eyes are ALWAYS Base -- bridge MainCamera is an Overlay stacked under BackgroundCamera; copying that flipped the eyes off the render path
                    if (f.Name == "m_VolumeStack") continue;                      // runtime cache; the pipeline rebuilds it
                    if (f.Name == "m_TargetDepthTexture") continue;               // never point the eyes at the main camera's target
                    keep.Add(f);
                }
                _camDataFields = keep.ToArray();
                _camDataDefault = _camDataType.GetProperty("DefaultAdditionalCameraData", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                _log.Log("[camdata] type ok; " + _camDataFields.Length + " copyable fields.");
            }
            if (_camDataType == null || _camDataFields == null) return;
            long context = SpatialGameContextRevision;
            if (src == _camDataSource && context == _camDataContext && Time.frameCount < _camDataNextRefresh) return;
            _camDataNextRefresh = Time.frameCount + 300;   // throttled refresh; covers mid-scene config changes
            var srcData = src.GetComponent(_camDataType);
            // A new area/camera can have no additional component. In that case
            // Waaagh uses its default; keeping the previous area's eye settings
            // would retain unrelated lighting, renderer and volume configuration.
            if (srcData == null)
            {
                try { srcData = _camDataDefault?.GetValue(null, null) as Component; }
                catch (Exception error) { _log.Log("[camdata] Native default unavailable: " + error.Message); }
                if (!_camDataLoggedOnce)
                {
                    _camDataLoggedOnce = true;
                    _log.Log("[camdata] Live camera has no additional data; using the native pipeline default when available.");
                }
                if (srcData == null) return;
            }
            CopyCamData(eyeL, srcData);
            CopyCamData(eyeR, srcData);
            _camDataSource = src; _camDataContext = context;
            LogDefaultCamData();   // 0.6.87 (M4): one-shot record of what the eyes ran pre-0.6.86
            if (!_camDataLoggedOnce)
            {
                _camDataLoggedOnce = true;
                string dump = "[camdata] eyes now inherit the live camera's config:";
                for (int i = 0; i < _camDataFields.Length; i++)
                {
                    object v = null;
                    try { v = _camDataFields[i].GetValue(srcData); } catch { }
                    dump += " " + _camDataFields[i].Name.Substring(2) + "=" + (v == null ? "null" : v.ToString()) + ";";
                }
                _log.Log(dump);
            }
        }

        static void CopyCamData(Camera eye, Component srcData)
        {
            if (eye == null) return;
            var data = eye.GetComponent(_camDataType);
            if (data == null) data = eye.gameObject.AddComponent(_camDataType);
            if (data == null) return;
            for (int i = 0; i < _camDataFields.Length; i++)
            {
                try { _camDataFields[i].SetValue(data, _camDataFields[i].GetValue(srcData)); }
                catch { }
            }
        }

        // --- 0.6.87 (M4): renderer-feature isolation rig (session only). ---
        // Owlcat's ScriptableRendererFeature base carries SetActive(bool)/isActive.
        // We enumerate every loaded feature instance (the base extends ScriptableObject,
        // so Resources.FindObjectsOfTypeAll sees them) and expose a panel checkbox per
        // feature TYPE; ticking disables that feature EVERYWHERE (desktop included) so
        // the eye artefact can be attributed in-headset without rebuilds. Nothing is
        // persisted; all skips restore on VR stop.
        static Type _featureBaseType;
        static MethodInfo _featureSetActive;
        static PropertyInfo _featureIsActive;
        static bool _featuresProbed;
        static bool _featuresLoggedOnce;
        static bool _camDataDefaultLogged;
        static List<string> _isoNames = new List<string>();
        static Dictionary<string, List<UnityEngine.Object>> _isoInstances = new Dictionary<string, List<UnityEngine.Object>>();
        static Dictionary<string, bool> _isoSkip = new Dictionary<string, bool>();

        static void RefreshIsolationRig()
        {
            if (!_featuresProbed)
            {
                _featuresProbed = true;
                _featureBaseType = Type.GetType("Owlcat.Runtime.Visual.Waaagh.RendererFeatures.ScriptableRendererFeature, Owlcat.Runtime.Visual");
                if (_featureBaseType != null)
                {
                    _featureSetActive = _featureBaseType.GetMethod("SetActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    _featureIsActive  = _featureBaseType.GetProperty("isActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                if (_featureBaseType == null || _featureSetActive == null)
                    _log.Log("[features] isolation rig unavailable (feature base type or SetActive not found).");
            }
            if (_featureBaseType == null || _featureSetActive == null) return;
            _isoInstances.Clear();
            _isoNames.Clear();
            var all = Resources.FindObjectsOfTypeAll(_featureBaseType);
            for (int i = 0; i < all.Length; i++)
            {
                var o = all[i];
                if (o == null) continue;
                string tn = o.GetType().Name;
                List<UnityEngine.Object> list;
                if (!_isoInstances.TryGetValue(tn, out list))
                {
                    list = new List<UnityEngine.Object>();
                    _isoInstances[tn] = list;
                    _isoNames.Add(tn);
                    if (!_isoSkip.ContainsKey(tn)) _isoSkip[tn] = false;
                }
                list.Add(o);
            }
            _isoNames.Sort();
            if (!_featuresLoggedOnce && _isoNames.Count > 0)
            {
                _featuresLoggedOnce = true;
                string msg = "[features]";
                for (int i = 0; i < _isoNames.Count; i++)
                {
                    var tn = _isoNames[i];
                    var list = _isoInstances[tn];
                    bool act = true;
                    try { if (_featureIsActive != null) act = (bool)_featureIsActive.GetValue(list[0], null); } catch { }
                    msg += " " + tn + " x" + list.Count + (act ? "" : " (inactive)") + ";";
                }
                _log.Log(msg);
            }
            for (int i = 0; i < _isoNames.Count; i++)   // re-assert ticks onto freshly discovered instances
                if (_isoSkip[_isoNames[i]]) ApplyIsolation(_isoNames[i], true);
        }

        static void ApplyIsolation(string typeName, bool skip)
        {
            List<UnityEngine.Object> list;
            if (!_isoInstances.TryGetValue(typeName, out list)) return;
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                if (o == null) continue;
                try { _featureSetActive.Invoke(o, new object[] { !skip }); n++; } catch { }
            }
            _log.Log("[features] " + typeName + (skip ? " DISABLED" : " restored") + " (" + n + " instance" + (n == 1 ? "" : "s") + ").");
        }

        static void RestoreAllIsolation()
        {
            bool any = false;
            for (int i = 0; i < _isoNames.Count; i++)
            {
                var tn = _isoNames[i];
                if (_isoSkip[tn]) { _isoSkip[tn] = false; ApplyIsolation(tn, false); any = true; }
            }
            if (any) _log.Log("[features] all isolation skips restored on VR stop.");
        }

        static void LogDefaultCamData()
        {
            if (_camDataDefaultLogged || _camDataType == null || _camDataFields == null) return;
            try
            {
                var fld = _camDataType.GetField("s_DefaultAdditionalCameraData", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object inst = fld != null ? fld.GetValue(null) : null;
                if (inst == null) return;   // not created yet; retried on a later refresh tick
                _camDataDefaultLogged = true;
                string dump = "[camdata-default] the pipeline's default instance (what the eyes ran pre-0.6.86):";
                for (int i = 0; i < _camDataFields.Length; i++)
                {
                    object v = null;
                    try { v = _camDataFields[i].GetValue(inst); } catch { }
                    dump += " " + _camDataFields[i].Name.Substring(2) + "=" + (v == null ? "null" : v.ToString()) + ";";
                }
                _log.Log(dump);
            }
            catch { _camDataDefaultLogged = true; }
        }

        // --- 0.6.89 (M4): per-camera render heartbeat + right-eye suspend probe. ---
        // The bridge freeze: both eye RTs stop updating while Submit succeeds (status None
        // throughout) and the desktop lives -- silently. The pipeline fires the public
        // begin/endCameraRendering events. 0.6.89 rode the dead Strategy-B handlers;
        // 0.6.90 subscribed properly and DISCOVERED THE BLIND SPOT: Waaagh fires those
        // public events only for screen-targeted cameras (allCameras=5, events for 3;
        // the RT-targeted eyes render but never fire them). 0.6.91 therefore taps
        // WaaaghPipeline.RenderSingleCamera via Harmony prefix/postfix instead -- the
        // method every camera passes through (seen in the meltdown stack traces). We
        // count begins/ends per camera and report at 1 Hz ONLY when the rendered roster
        // changes or a camera shows begins without ends. Three signatures, three next
        // steps: the eyes lose begins (the pipeline excludes them upstream) / begins
        // without ends (a silently swallowed abort inside RenderSingleCamera) / normal
        // begins+ends with a frozen headset (output never reaches the RT).
        static Dictionary<int, int> _hbBegins = new Dictionary<int, int>();
        static Dictionary<int, int> _hbEnds = new Dictionary<int, int>();
        static Dictionary<int, string> _hbNames = new Dictionary<int, string>();
        static HashSet<int> _hbLastRoster = new HashSet<int>();
        static float _hbNextReport;
        static bool _suspendRightEye;   // session-only probe: drops allCamerasCount by one

        static bool _hbHooked;
        static bool _hbPatched;

        static void HbHook()
        {
            if (_hbHooked) return;
            _hbHooked = true;
            _hbBegins.Clear(); _hbEnds.Clear(); _hbNames.Clear(); _hbLastRoster.Clear();
            _hbNextReport = Time.unscaledTime + 10f;
            if (_hbPatched) { _log.Log("[cams] heartbeat counters reset (tap already in place)."); return; }
            try
            {
                var pipeType = Type.GetType("Owlcat.Runtime.Visual.Waaagh.WaaaghPipeline, Owlcat.Runtime.Visual");
                if (pipeType == null) { _log.Log("[cams] WaaaghPipeline not found; heartbeat unavailable."); return; }
                if (_harmony == null) { _log.Log("[cams] no Harmony instance; heartbeat unavailable."); return; }
                var pre  = new HarmonyMethod(typeof(Main).GetMethod(nameof(HbRenderSingleCamera_Prefix),  BindingFlags.Static | BindingFlags.NonPublic));
                var post = new HarmonyMethod(typeof(Main).GetMethod(nameof(HbRenderSingleCamera_Postfix), BindingFlags.Static | BindingFlags.NonPublic));
                var final = new HarmonyMethod(typeof(Main).GetMethod(nameof(EndRenderScope), BindingFlags.Static | BindingFlags.NonPublic));
                // Match only the installed CameraData worker. A signature drift
                // must not apply a typed patch to an unrelated overload.
                var worker = pipeType.GetMethod("RenderSingleCamera", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(ScriptableRenderContext), typeof(CameraData).MakeByRefType() }, null);
                if (worker == null || worker.IsStatic || worker.ReturnType != typeof(void) || worker.GetMethodBody() == null)
                    throw new MissingMethodException("WaaaghPipeline.RenderSingleCamera(context, ref CameraData)");
                _harmony.Patch(worker, prefix: pre, postfix: post, finalizer: final);
                _hbPatched = true;
                _log.Log("[cams] Typed camera worker scope installed; no argument-array capture.");
            }
            catch (Exception e) { _log.Log("[cams] heartbeat tap failed (" + e.Message + ")."); }
        }

        static void HbUnhook()
        {
            if (!_hbHooked) return;
            _hbHooked = false;   // tap stays patched for the process; counters gate on _active
            _hbBegins.Clear(); _hbEnds.Clear(); _hbLastRoster.Clear();
        }

        static bool HbRenderSingleCamera_Prefix(ScriptableRenderContext __0, ref CameraData __1, out RenderScopeState __state)
        {
            var camera = __1.Camera;
            BeginRenderScope(camera, ref __1, out __state);
            __state.skipped = SkipDesktopWorld(camera);
            if (__state.skipped) { CullForcedVisibility(ref __0, camera); return false; }
            HeartbeatBegin(camera);
            return true;
        }
        static void HbRenderSingleCamera_Postfix(RenderScopeState __state)
        { if (!__state.skipped) HeartbeatEnd(__state.camera); }

        static void HeartbeatBegin(Camera cam)
        {
            if (!DiagnosticsRecording || !_active || cam == null) return;
            int id = cam.GetInstanceID();
            int v; _hbBegins.TryGetValue(id, out v); _hbBegins[id] = v + 1;
            if (!_hbNames.ContainsKey(id))
                _hbNames[id] = cam.name + "(d" + cam.depth.ToString("0.#", CultureInfo.InvariantCulture) + (cam.targetTexture != null ? ",rt)" : ")");
        }

        static void HeartbeatEnd(Camera cam)
        {
            if (cam != null) _renderedFrames[cam.GetInstanceID()] = Time.frameCount;
            if (!DiagnosticsRecording || !_active || cam == null) return;
            int id = cam.GetInstanceID();
            int v; _hbEnds.TryGetValue(id, out v); _hbEnds[id] = v + 1;
        }

        static void HeartbeatTick()
        {
            if (!_active) return;
            if (_suspendRightEye) _runner?.SetRightEyeEnabled(false);   // enforce across eye rebuilds (BuildEyes re-enables)
            if (!DiagnosticsRecording) return;
            if (Time.unscaledTime < _hbNextReport) return;
            _hbNextReport = Time.unscaledTime + 10f;
            if (_hbBegins.Count == 0) { _hbLastRoster.Clear(); return; }
            var roster = new HashSet<int>(_hbBegins.Keys);
            bool changed = !roster.SetEquals(_hbLastRoster);
            bool anomaly = false;
            foreach (var kv in _hbBegins)
            {
                int ends; _hbEnds.TryGetValue(kv.Key, out ends);
                if (ends < kv.Value) { anomaly = true; break; }
            }
            string es = "";
            if (_runner != null)
                es = " | " + DescribeEyeState(_runner.GetEyeL(), "L") + " " + DescribeEyeState(_runner.GetEyeR(), "R") + " pinFights=" + _rtPinFights;
            bool esChanged = es != _eyeStateLast;
            _eyeStateLast = es;
            if (changed || anomaly || esChanged)
            {
                string msg = "[cams] allCameras=" + Camera.allCamerasCount + " rendered:";
                foreach (var kv in _hbBegins)
                {
                    int ends; _hbEnds.TryGetValue(kv.Key, out ends);
                    string nm; if (!_hbNames.TryGetValue(kv.Key, out nm)) nm = "cam#" + kv.Key;
                    msg += " " + nm + "=" + kv.Value + "/" + ends;
                }
                foreach (var id in _hbLastRoster)
                    if (!roster.Contains(id))
                    {
                        string nm; if (!_hbNames.TryGetValue(id, out nm)) nm = "cam#" + id;
                        msg += " STOPPED:" + nm;
                    }
                msg += es;
                _log.Log(msg);
            }
            _hbLastRoster = roster;
            _hbBegins.Clear(); _hbEnds.Clear();
        }

        // --- 0.6.93 (M4 stairstep, probe 1): per-eye shadow kill switch. ---
        // The stairstep survived every settings floor and all eight renderer features
        // disabled; core passes remain. A world-stable, camera-position-dependent,
        // pixelated edge is the classic fingerprint of a directional shadow cascade
        // boundary, and the eyes carry RenderShadows=True regardless of the quality
        // menu. While the panel toggle is on, this writes m_RenderShadows=false on the
        // eyes' data every frame (session only; toggling off forces a copy refresh).
        static bool _eyeShadowsOff = false;
        internal static bool _symProjProbe = true;   // 0.6.96 stairstep fix, ALWAYS ON since 0.6.151 (M6): symmetric eye projection + cropped submit (the legacy comparison toggle is gone)

        static FieldInfo _camDataRenderShadowsField;
        static bool _eyeShadowFieldUnavailable;

        internal static void ApplyEyeShadowProbe(Camera eye)
        {
            if (!_eyeShadowsOff || eye == null || _camDataType == null || _eyeShadowFieldUnavailable) return;
            if (_camDataRenderShadowsField == null)
            {
                _camDataRenderShadowsField = _camDataType.GetField("m_RenderShadows", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_camDataRenderShadowsField == null)
                {
                    _eyeShadowFieldUnavailable = true;
                    _log.Log("[stairstep] m_RenderShadows not found; shadow probe unavailable.");
                    return;
                }
            }
            var data = eye.GetComponent(_camDataType);
            if (data == null) return;
            try { _camDataRenderShadowsField.SetValue(data, false); } catch { }
        }

        // --- 0.6.92 (M4/12.9): pin the eyes' RenderType to Base every frame. ---
        // The camera-loop IL permits exactly one silent exclusion: TryBuildCameraStack
        // returns false iff the camera's additional-data RenderType != Base, and the
        // camera is then skipped with no render and no log. Something on the bridge
        // takes the eyes off Base after their first frame (mechanism unproven -- the
        // 300-frame 0.6.86 refresh SHOULD have restored them, and did not; the fight
        // counter below is the decisive datum). We write Base back every frame from
        // the Runner (execution order 31000 -- nearly the last word before rendering).
        static PropertyInfo _camDataRenderTypeProp;   // WaaaghAdditionalCameraData.RenderType
        static bool _rtPinUnavailable;
        static int _rtPinFights;                      // times the pin found non-Base and wrote Base back
        static string _eyeStateLast = "";

        internal static void PinEyeRenderType(Camera eye)
        {
            if (eye == null || _camDataType == null || _rtPinUnavailable) return;
            if (_camDataRenderTypeProp == null)
            {
                _camDataRenderTypeProp = _camDataType.GetProperty("RenderType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_camDataRenderTypeProp == null)
                {
                    _rtPinUnavailable = true;
                    _log.Log("[eyestate] RenderType property not found; pin unavailable.");
                    return;
                }
            }
            var data = eye.GetComponent(_camDataType);
            if (data == null) return;
            object v = null;
            try { v = _camDataRenderTypeProp.GetValue(data, null); } catch { return; }
            if (v == null) return;
            if (Convert.ToInt32(v) != 0)
            {
                try
                {
                    _camDataRenderTypeProp.SetValue(data, Enum.ToObject(_camDataRenderTypeProp.PropertyType, 0), null);
                    _rtPinFights++;
                }
                catch { }
            }
        }

        static string DescribeEyeState(Camera eye, string tag)
        {
            if (eye == null) return tag + ":none";
            string rt = "?";
            if (_camDataType != null && _camDataRenderTypeProp != null)
            {
                var data = eye.GetComponent(_camDataType);
                if (data == null) rt = "nodata";
                else { try { rt = Convert.ToInt32(_camDataRenderTypeProp.GetValue(data, null)).ToString(); } catch { rt = "err"; } }
            }
            return tag + ":rt=" + rt + ",en=" + (eye.enabled ? "1" : "0");
        }

        static bool _dsManualShow;
        internal static void ToggleDesktopMirror() { _dsManualShow = !_dsManualShow; }

        // --- scene attachment lifecycle (suspend/resume across loads; the OpenVR session stays up) ---

        static void AttachToScene()
        {
            if (!_active || _attached) return;
            InvalidatePointerRaycastCache();
            EnsureEyeRts();             // belt-and-braces only: RTs are created at StartVr and persist (Family A)
            ConvertHudToWorldSpace();   // convert HUD/modal canvases to world space and redirect their raycasters
            CreateCursor();             // the reticle (a child of the UI root)
            _runner?.BuildEyes();       // (re)create the eye cameras that copy Camera.main, targeting the eye RTs
            _reticleState = -1;         // re-apply the reticle look next frame
            _attached = true;
            _attachedCam = Camera.main;
            _atMainMenu = ResumingIntoMainMenu();   // 0.6.50: gate the vertical-fit panel sizing to the menu
            _resumeCandidate = null; _resumeStable = 0;
            RefreshIsolationRig();   // 0.6.87 (M4): (re)discover renderer features for the isolation rig
            _log.Log("[attach] scene attachment up; camera='" + (_attachedCam != null ? _attachedCam.name : "null") + "'." + (_atMainMenu ? " (main menu: vertical-fit UI)" : ""));
        }

        static void DetachFromScene()
        {
            ResetComfortCameraReference();
            if (!_attached) return;
            DetachNeuralIntegration();
            HideLiveOverlay();
            InvalidatePointerRaycastCache();
            StopForcedVisibility();
            StopDrawDistanceComparison(preservePendingStart: true);
            _attached = false;
            _attachedCam = null;
            _runner?.DestroyEyes();     // remove our eye cameras so the engine stops walking them during the load
            // Family A fix (0.6.70): tell the compositor to DROP our last-submitted frame. Left held, it keeps
            // reprojecting that frame on its own driver thread while the game churns GPU memory through the
            // suspend - a short no-loading-screen suspend (inventory) rides that straight into a driver
            // use-after-free. Cleared, the headset shows the SteamVR void/grid until we re-attach. The eye RTs
            // themselves stay alive (freed only in StopVr), so resume is just BuildEyes + convert.
            // OpenXR owns copies in its swapchains; Unity textures are never shared with the compositor.
            RestoreHudCanvases();       // restore the converted canvases + raycasters, destroy the UI root (+ reticle)
            _heldForLoadingLogged = false;
            _suspendStart = Time.unscaledTime;
            _log.Log("[detach] scene attachment down; showing the game's flat frame through OpenXR.");
        }

        // Called when a scene load is detected (hook flag) or Camera.main goes away (safety net): relinquish the
        // attachment now, keep the session and submit loop alive (the compositor's frame is cleared, so the
        // headset shows the SteamVR void/grid rather than a stale reprojection), and start waiting to resume.
        internal static void SuspendForTransition(string reason)
        {
            if (!_attached) return;
            _log.Log("[transition] suspend (" + reason + ").");
            DetachFromScene();
            _lastTransitionTime = Time.unscaledTime;
            _resumeCandidate = null; _resumeStable = 0;
        }

        // Called every frame while suspended. Re-attach once Camera.main has been steady for a few frames, enough
        // time has passed since the last scene op, AND the loading screen has cleared - so we rebuild exactly once,
        // after the whole multi-scene area load is done, never in the gaps between sub-scenes.
        internal static void PollResume()
        {
            if (_attached) return;
            var cam = Camera.main;
            if (cam == null) { _resumeCandidate = null; _resumeStable = 0; return; }
            if (cam == _resumeCandidate) _resumeStable++;
            else { _resumeCandidate = cam; _resumeStable = 1; }

            bool steady = _resumeStable >= ResumeStableFrames && Time.unscaledTime - _lastTransitionTime >= ResumeMinDelay;
            if (!steady) return;

            if (LoadingScreenShowing())
            {
                // Fail-safe: if the probe somehow never reads clear, resume anyway once we've waited a long time
                // with no scene activity, rather than leaving the headset frozen forever.
                bool timedOut = Time.unscaledTime - _suspendStart > ResumeHardTimeout
                                && Time.unscaledTime - _lastTransitionTime >= 3f;
                if (!timedOut)
                {
                    if (!_heldForLoadingLogged)
                    {
                        _heldForLoadingLogged = true;
                        _log.Log("[resume] camera steady but the loading screen is still up; holding the frozen frame.");
                    }
                    return;
                }
                _log.Log("[resume] WARNING: loading screen still reads active after " + ResumeHardTimeout + "s; resuming anyway (probe may be wrong).");
            }

            if (FlatWanted) return;
            AttachToScene();
        }

        // Are we resuming into the main menu? Distinct from gameplay because the MainMenu scene is only loaded at
        // the menu (loading a save unloads it). Checked by name across loaded scenes so a transient active scene
        // doesn't fool us.
        static bool ResumingIntoMainMenu()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && s.name.IndexOf("MainMenu", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        // The game's LoadingCanvas is present at the menu and in-game with zero active drawables, and carries many
        // while a load screen is on display. So "has active child renderers" is our load-in-progress signal. We
        // cache the canvas and re-find it only if it's been destroyed (Unity-null), so this is cheap per frame.
        static bool LoadingScreenShowing()
        {
            if (_loadingCanvas == null)
            {
                var all = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var c in all) if (c.name == "LoadingCanvas") { _loadingCanvas = c; break; }
            }
            if (_loadingCanvas == null || !_loadingCanvas.isActiveAndEnabled) return false;
            _loadingCanvas.GetComponentsInChildren(false, _loadingProbe);   // active child renderers only
            return _loadingProbe.Count > 0;
        }

        static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            _autoStartArmed = value && _launcherVrRequested;
            if (!value) StopVr();
            return true;
        }


        // ---- UI capture: render the game's own UICamera into our panel RT ----
        // A duplicate camera can't capture Screen Space - Camera canvases (they render only
        // through the camera they're bound to). Persistently retargeting that camera doesn't
        // work either: this pipeline ignores targetTexture on its auto-rendered cameras (but
        // honours clearFlags, which is what blacked out the desktop). Manual Camera.Render(),
        // however, DOES honour targetTexture (it's how our eye cameras work). So each frame we
        // briefly point UICamera at our RT, render it once by hand, and restore it - leaving the
        // game's own UICamera->screen pass untouched.

        // The game's UI camera: prefer the one named UICamera, else a camera that
        // renders only the UI layer.
        static Camera FindUICamera()
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            int uiMask = uiLayer >= 0 ? (1 << uiLayer) : (1 << 5);
            Camera uiOnly = null;
            foreach (var c in Camera.allCameras)
            {
                if (c.name.Contains("UICamera")) return c;
                if (c.cullingMask == uiMask && uiOnly == null) uiOnly = c;
            }
            return uiOnly;
        }

        // ---- UI capture: redirect UICamera into our panel RT via the SRP camera callbacks ----
        // Manual Camera.Render() doesn't draw Canvas UI in this pipeline, and a persistent
        // targetTexture set in LateUpdate didn't stick. So we set the target at the last moment,
        // in beginCameraRendering (before the SRP reads it), and restore it in endCameraRendering.

        static bool _camCallbacksHooked;
        static bool _uiCaptureLogged;
        static bool _uiReadback;
        static RenderTexture _savedUiTarget;
        static CameraClearFlags _savedUiClear;
        static Color _savedUiBg;

        static bool IsUiCam(Camera cam)
        {
            return cam != null && cam.name.Contains("UICamera");
        }

        static void HookCameraCallbacks()
        {
            if (_camCallbacksHooked) return;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering   += OnEndCameraRendering;
            _camCallbacksHooked = true;
        }

        static void UnhookCameraCallbacks()
        {
            if (!_camCallbacksHooked) return;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering   -= OnEndCameraRendering;
            _camCallbacksHooked = false;
        }

        static void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            EnforceCoverage77();
            HeartbeatBegin(cam);   // 0.6.89: per-camera render heartbeat
            if (_uiRT == null || !IsUiCam(cam)) return;
            _savedUiTarget = cam.targetTexture;
            _savedUiClear  = cam.clearFlags;
            _savedUiBg     = cam.backgroundColor;
            cam.targetTexture   = _uiRT;                       // redirect just before the SRP reads it
            cam.clearFlags      = CameraClearFlags.SolidColor; // clear colour so the RT is
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // transparent where no UI
            if (!_uiCaptureLogged) { _uiCaptureLogged = true; _log.Log("UICamera begin: redirecting '" + cam.name + "' to panel RT"); }
        }

        static void OnEndCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            HeartbeatEnd(cam);   // 0.6.89: per-camera render heartbeat
            if (!IsUiCam(cam)) return;
            cam.targetTexture   = _savedUiTarget;   // restore to the screen
            cam.clearFlags      = _savedUiClear;
            cam.backgroundColor = _savedUiBg;
        }

        static void RestoreUiCameraImmediate()
        {
            var ui = FindUICamera();
            if (ui != null) { ui.targetTexture = null; ui.clearFlags = CameraClearFlags.Depth; }
        }

        // One-time read-back of the panel RT so we can see what actually landed in it.
        internal static void ReadbackUiRtOnce()
        {
            if (_uiReadback || _uiRT == null) return;
            _uiReadback = true;
            var prev = RenderTexture.active;
            RenderTexture.active = _uiRT;
            var tex = new Texture2D(_uiRT.width, _uiRT.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, _uiRT.width, _uiRT.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            int rgb = 0, a = 0, maxA = 0;
            for (int i = 0; i < px.Length; i += 101)
            {
                var p = px[i];
                if (p.r > 8 || p.g > 8 || p.b > 8) rgb++;
                if (p.a > 8) a++;
                if (p.a > maxA) maxA = p.a;
            }
            UnityEngine.Object.Destroy(tex);
            _log.Log(string.Format("UI RT readback: sampled={0} rgb>8={1} alpha>8={2} maxAlpha={3}", (px.Length / 101) + 1, rgb, a, maxA));
        }

        // ---- World-space UI: convert the HUD canvases from Screen Space - Camera to World
        // Space so the (enabled) eye cameras render them as 3D geometry, head-locked in front
        // of the view. Fully restored on VR stop. ----

        class SavedCanvas
        {
            public Canvas canvas;
            public RenderMode mode;
            public Transform parent;
            public int siblingIndex;
            public Vector3 localPos;
            public Quaternion localRot;
            public Vector3 localScale;
            public bool isModal;   // full-screen modal (CommonCanvas): sits in front of the HUD, not in the stagger
            public Vector2 screenFrac;   // canvas rect-centre in normalised screen coords at convert time (0.5,0.5 = screen centre)
            public Camera cam;           // 0.6.127: original worldCamera at convert time (captured before the raycaster redirect)
            public Camera panelCam;      // 0.6.137: worldCamera as the conversion left it (post-redirect) - the reversion self-heal re-asserts exactly this
            public bool    panelGeomValid;   // 0.6.138: the four geometry fields below were captured
            public Vector2 panelAnchorMin;   // 0.6.138: as-converted RectTransform geometry - a brief
            public Vector2 panelAnchorMax;   //   ScreenSpaceCamera reversion lets the Canvas system
            public Vector2 panelPivot;       //   re-drive the rect to the render-target size, and the
            public Vector2 panelSize;        //   mode heal alone leaves that artefact in place
            public int hudViewportRevision; // only reflow when the headset layout changes, never pollute the original ledger
            public float hudFadeAspect;
            public CanvasGroup[] origGroups;   // 0.6.142: cached original-ancestor CanvasGroup chain (the vanilla alpha-hide path)
            public float groupsRescanAt;       // 0.6.142: chain re-scan throttle (catches runtime-added fade groups)
            public HudRepairLogState repairLog;
        }

        class PanelGeom { public Vector2 aMin, aMax, pivot, size; }
        // 0.6.139: first-capture-wins geometry per canvas instance. The first conversion of an
        // instance precedes any RTVR-induced ScreenSpace window by construction, so its capture is
        // pre-corruption truth; capture, heal, and the game-state restore all read this ledger.
        // Cleared only at full VR stop - it must survive suspends, which is the whole point.
        static readonly Dictionary<int, PanelGeom> _geomLedger = new Dictionary<int, PanelGeom>();
        static readonly List<CanvasGroup> _tmpGroups = new List<CanvasGroup>();   // 0.6.142: scratch for chain scans
        static readonly List<Mask> _dialogMasksDisabled = new List<Mask>();       // 0.6.148: Viewport stencil Masks we disabled (re-enabled at restore)
        static readonly List<RectMask2D> _dialogRectMasksAdded = new List<RectMask2D>();   // 0.6.150: rect clippers we added to stencil-only Viewports (destroyed at restore)
        static readonly List<SavedCanvas> _savedCanvases = new List<SavedCanvas>();
        // 0.6.129 diagnostic: verify restored canvas state twice - immediately (did the write land?)
        // and ~2 s later (did something re-write it?). Read-only; convicts the restore-regression mechanism.
        struct RestoreCheck { public Canvas canvas; public RenderMode mode; public Camera cam; public Transform parent; }
        static readonly List<RestoreCheck> _restoreChecks = new List<RestoreCheck>();
        static float _restoreCheckDue;
        static RectTransform _uiRoot;   // neutral holder (identity, 1920x1080) so parent scale/size can't collapse the canvases
        // World-space canvases whose GraphicRaycaster we repointed from UICamera to the game camera,
        // so the pointer ray actually reaches the panel. Restored on StopVr.
        static readonly List<Canvas> _redirectedCanvases = new List<Canvas>();
        static Camera _savedUiRaycastCam;
        static Canvas _cursorCanvas;    // our own reticle (the game's cursor is a hardware cursor, not in the frame)
        static Camera _pickCam;         // 0.6.81: disabled screen->panel mapping camera (see the conversion site)
        static RectTransform _cursorRect;
        static Image _cursorImg;        // the reticle's Image; sprite is the crosshair or the captured game cursor
        static Sprite _crosshairSprite;  // built-in cyan fallback, used before the game sets a cursor / on a null cursor

        // Cursor capture: the game sets its hardware cursor via Cursor.SetCursor (the texture is a real game
        // asset). A Harmony post-hook records the latest texture+hotspot here, and the reticle wears it.
        static Harmony _harmony;
        static bool _appQuitting;   // 0.6.118: set by Application.quitting; gates the OnUnload unpatch
        static Texture2D _gameCursorTex;
        static Vector2 _gameCursorHotspot;        // pixels from the texture's TOP-LEFT (Unity cursor convention)
        static volatile bool _cursorDirty;        // a SetCursor call arrived; rebuild the reticle on the next frame
        static Texture2D _appliedCursorTex;        // texture currently on the reticle (skip needless rebuilds)
        static readonly Dictionary<Texture2D, Sprite> _cursorSprites = new Dictionary<Texture2D, Sprite>();

        // The camera mod (Servo-Skull) locks the cursor to screen centre for mouselook and frees it on its
        // configurable key. We read its public CursorLocked flag by reflection â€” a soft link: if it's absent
        // the field just isn't found and we always show the pointer. While locked we show a faint centre
        // crosshair; while free we show the real game cursor.
        static bool _ssResolved;
        static FieldInfo _ssCursorLockedField;
        static int _reticleState = -1;   // applied reticle look: 0 free pointer, 1 locked crosshair, 2 locked + hidden

        // The canvases that actually carry the readable HUD. The surface world-marker containers
        // (Enemy/Npc/Party/MapObject/...) stay off the panel â€” per-unit adornments the overtip float
        // handles in 3D â€” as do per-character adornments and the mouse cursor. The space-map marker
        // containers below are the exception: on the maps they ARE the readable, clickable map.
        static readonly HashSet<string> HudCanvasNames = new HashSet<string>
        {
            "DynamicCanvas", "StaticCanvas", "PartyPCView", "SurfaceActionBarPCView",
            // M2 (0.6.79): space-map marker containers. On the system/sector maps these carry the clickable
            // planet/POI marker+label clusters and the orbit/ruler furniture (confirmed: map clicks land on the
            // markers, not the 3D bodies). Empty on surface attachments, so they convert as harmless blank layers.
            "PlanetsContainer", "SystemMapCircleArcsView", "StarsContainer", "ShipPositionRulersView"
        };
        // Full-screen modals the game also keeps on UICamera (game menu, full-screen dialogue events, â€¦).
        // Converted like the HUD, but positioned clearly in front of it so they occlude it while open.
        static readonly HashSet<string> ModalCanvasNames = new HashSet<string>
        {
            "CommonCanvas", "FadeCanvas"
            // FadeCanvas has dedicated full-view geometry in PositionCinematicFadeCanvas:
            // the game's original opacity/bars survive, in front of all PC HUD layers.
        };
        static bool ShouldConvert(string name) => HudCanvasNames.Contains(name) || ModalCanvasNames.Contains(name);
        // 0.6.82: the map-chrome subset of the keep-list - the layers whose content the game positions by
        // game-camera projection. These take the frustum-fit scale in chart mode (see PositionWorldSpaceUi).
        static readonly string[] MapContainerNames = { "PlanetsContainer", "SystemMapCircleArcsView", "StarsContainer", "ShipPositionRulersView" };
        static bool IsMapContainer(string name)
        {
            for (int i = 0; i < MapContainerNames.Length; i++) if (MapContainerNames[i] == name) return true;
            return false;
        }
        // 0.6.84: the two containers whose direct children are per-marker clusters - the glyphs the
        // counter-scale boosts. Arcs and rulers are excluded: their content is spatial furniture
        // (grid, orbits, edge rulers), not glyphic, and must stay at the registered fit.
        static readonly string[] MarkerContainerNames = { "PlanetsContainer", "StarsContainer" };
        static bool IsMarkerContainer(string name)
        {
            for (int i = 0; i < MarkerContainerNames.Length; i++) if (MarkerContainerNames[i] == name) return true;
            return false;
        }
        const float ModalDepth = 0.06f;   // metres a modal sits in front of the panel centre (the HUD spans 0..0.03)
        const float RefWidth = 1920f;   // reference canvas width; a full-screen canvas maps to the filled frustum width

        // Panel sizing uses the actual shared OpenXR view, independently of the
        // game's desktop camera FOV. uiWidth is occupancy of the fitted PC screen;
        // the dedicated pick camera always covers that same panel geometry.
        static bool  _atMainMenu;                // attachment is the main menu (vanilla, pre-load); tags the attach log


        // Option A (0.6.49): clamp the eye near plane to sit just in front of the HUD panel, so a view's custom
        // near clip (e.g. Servo-Skull View 2's 5 m) can't cull the panel at uiDistance. Only ever lowers the near
        // plane (never raises it), and tracks the live uiDistance slider, so closer/cheaper views are untouched.
        internal static float EyeNearPlane(float srcNear)
        {
            float panelNear = _cfg.uiDistance - (ModalDepth + 0.09f);   // just ahead of the nearest (modal) panel element
            return Mathf.Max(0.02f, Mathf.Min(srcNear, panelNear));
        }
        static bool _loggedUiLayout;

        // Only the map-registration blend is smoothed. Ordinary PC HUD scale and
        // distance advance together so hand zoom never changes apparent text size.
        const  float UiScaleLerp = 0.25f;   // ~90% converged in ~9 frames (~0.15 s at 60 fps)
        // Diagnostics: log the game camera's projection on each attach and when it shifts, so we can tell
        // whether residual wobble is a transient (handled by smoothing) or a genuine per-area FOV change.
        static bool  _logUiProjNow;
        static float _lastLoggedFov;
        static float _lastLoggedM00;
        static float _lastLoggedPanelWidth;
        static int   _lastUiView = int.MinValue;   // 0.6.48 diag: force a [ui-proj] log on the frame the active view changes

        // 0.6.82: map "chart" mode. The game lays out the map containers' content (planet/POI markers,
        // labels, orbit arcs, rulers) by projecting world positions through the GAME camera, so that
        // content only registers with the 3D bodies behind the panel when its layer spans exactly the
        // game frustum (the proven 0.6.79 fit). In chart mode the four map containers get that fit
        // scale while the rest of the panel keeps its uniform angular size. Detection = active
        // CanvasRenderers on the converted map containers (validated in 0.6.80: 0 on surface, 207 on
        // the system map, 0 after), polled every 15 frames; state reset at teardown.
        static bool  _chartFit;         // live map content detected on the converted containers
        static float _chartBlend;       // 0 = panel scale, 1 = frustum fit; lerped so entry/exit animates
        static bool  _chartProbeDone;   // the read-only structure dump fires once per attach, on the ON flip

        // Registry of positionable HUD elements shown in the settings selector. Add a row to expose another
        // canvas. 'def' is the panel-fraction (0..1, y up) the element's centre sits at out of the box.
        struct UiElement { public string label; public string canvas; public Vector2 def; }
        static readonly UiElement[] UiElements =
        {
            new UiElement { label = "Party portraits", canvas = "PartyPCView",            def = new Vector2(0.50f, 0.90f) },
            new UiElement { label = "Action bar",      canvas = "SurfaceActionBarPCView", def = new Vector2(0.50f, 0.50f) },
        };

        // Where a canvas sits on the panel (0..1, y up): a saved user override, else the registered default,
        // else dead centre. Read live every frame in PositionWorldSpaceUi, so the sliders move it immediately.
        static Vector2 PlacementFrac(string canvasName)
        {
            if (_cfg.uiPos.TryGetValue(canvasName, out var v)) return v;
            for (int i = 0; i < UiElements.Length; i++) if (UiElements[i].canvas == canvasName) return UiElements[i].def;
            return new Vector2(0.5f, 0.5f);
        }

        // 0.6.131: the area-transition selector spawns MID-ATTACH as its own natively world-space
        // canvas - 'TransitionPCView(Clone)', worldCamera=UICamera, parked at (0,0,2500) where
        // UICamera renders it on the flat screen (12.07 probe; identical pose VR-off). It never
        // exists at convert time and never qualifies (qualification wants ScreenSpaceCamera), so in
        // VR it sat 2.5 km from the eye cameras: invisible, though its clicks already worked via the
        // pick camera. Watch for it while attached and convert it on sight; the standard SavedCanvas
        // capture records its native WorldSpace mode and pose, so the standard restore returns it
        // exactly home.
        static float _lateConvertNext;
        // 0.6.143: empirical discriminator for the deployment banner canvas - the only thing the
        // P-dumps proved about it beyond name and mode is its content (ButtonImage/BackgroundImage
        // raycast hits). Bounded scan; only runs for WorldSpace canvases named exactly 'Canvas'.
        static bool HasDeploymentContent(Canvas c)
        {
            var kids = c.GetComponentsInChildren<RectTransform>(false);
            for (int i = 0; i < kids.Length; i++)
            {
                var n = kids[i].name;
                if (n == "ButtonImage" || n == "BackgroundImage") return true;
            }
            return false;
        }

        static void LateConvertWatcher()
        {
            if (!_attached || _uiRoot == null || _pickCam == null) return;
            MaintainPcNestedRaycasters();

            // 0.6.137/0.6.139: reversion self-heal - PER FRAME since 0.6.139. The 0.5s window let a
            // suspend race the heal (measured 12.07: cutscene rebind 20:31:10.521, detach .713),
            // carrying the SS-blown rect back into game state. The game's HUD rebind re-asserts
            // ScreenSpaceCamera + UICamera on converted canvases (measured 12.07: the action bar and
            // party portraits flipped within a second of 'SurfaceCombatInputLayer pushed', parent
            // untouched) - an SS-Camera canvas renders to the desktop backbuffer, so they vanish from
            // the headset. Any tracked canvas still parented on the panel whose mode or camera has
            // reverted is put back exactly as the conversion left it. This can never fight a suspend:
            // DetachFromScene clears _attached before any restore runs.
            for (int i = 0; i < _savedCanvases.Count; i++)
            {
                var sc = _savedCanvases[i];
                var cv = sc.canvas;
                if (cv == null || !cv.gameObject.activeInHierarchy) continue;   // 0.6.141: not isActiveAndEnabled - the visibility mirror drives Canvas.enabled
                if (cv.transform.parent != _uiRoot) continue;    // not on the panel: not ours to police
                // 0.6.141: visibility mirror. Re-parenting took the canvas out of the game's ancestor-
                // deactivation chain, so dialog/cutscene HUD hides no longer reach it (the party strip
                // stayed visible over dialogue). Mirror what the canvas would inherit in vanilla: the
                // original parent's activeInHierarchy. Change-gated log.
                // 0.6.142: the ancestor-deactivation mirror never fired (zero [vis-mirror] lines across
                // every scripted scene) and draw counts held steady - both GameObject-level hides are
                // falsified. The last mechanism standing: ancestor CanvasGroup alpha, which a re-parented
                // canvas no longer inherits. Mirror the original chain's combined alpha (product, nearest
                // first, stopping at ignoreParentGroups - Unity's own semantics). Chain re-scanned 1/s so
                // runtime-added fade groups are caught; alphas read per frame.
                if (sc.parent != null && Time.unscaledTime >= sc.groupsRescanAt)
                {
                    sc.groupsRescanAt = Time.unscaledTime + 1f;
                    _tmpGroups.Clear();
                    for (var p = sc.parent; p != null; p = p.parent)
                    {
                        var g = p.GetComponent<CanvasGroup>();
                        if (g != null) { _tmpGroups.Add(g); if (g.ignoreParentGroups) break; }
                    }
                    sc.origGroups = _tmpGroups.Count > 0 ? _tmpGroups.ToArray() : null;
                }
                float chainAlpha = 1f;
                if (sc.origGroups != null)
                    for (int gi = 0; gi < sc.origGroups.Length; gi++)
                    { var gg = sc.origGroups[gi]; if (gg != null) chainAlpha *= gg.alpha; }
                bool parentActive = sc.parent == null || sc.parent.gameObject.activeInHierarchy;
                bool wantVis = parentActive && chainAlpha > 0.05f && HudCanvasShown(cv.name);
                if (cv.enabled != wantVis)
                {
                    cv.enabled = wantVis;
                    if (DiagnosticsRecording) _log.Log("[vis-mirror] '" + cv.name + "' " + (wantVis ? "shown" : "hidden")
                        + " (parent " + (parentActive ? "active" : "inactive")
                        + ", chain alpha " + chainAlpha.ToString("0.00", CultureInfo.InvariantCulture) + ").");
                }
                HealConvertedCanvas(sc);
            }

            if (Time.unscaledTime < _lateConvertNext) return;   // 0.6.139: only the spawn scan below is throttled
            _lateConvertNext = Time.unscaledTime + 0.5f;

            var all = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            // 0.6.148: the dialogue-history overflow, convicted by the clip-state dump. The history
            // texts carry TMP stencil-mask instances (Stencil Id:1, Comp:Equal, ReadMask:1) written by
            // the stencil Mask on 'Viewport' - and that stencil sequence breaks in the world-space
            // transparent queue, so the texts draw wherever stencil happens to read 1. The SAME dump
            // proves rect clipping works here ('Text' clip=True), and 'Viewport' ALREADY carries a
            // RectMask2D. Fix: while attached, disable the Viewport's stencil Mask (showGraphic=False,
            // so nothing visual changes) - TMP re-resolves materials and the RectMask2D takes over the
            // clipping. Recorded and re-enabled at restore; a game-destroyed dialog null-skips.
            foreach (var c in all)
            {
                RedirectLatePcCanvas(c);
                if (c == null || !c.name.StartsWith("SurfaceDialogPCView")) continue;
                ApplySurfaceDialogLayout72(c);
                // 0.6.150: the [dialog-scan] trace convicted the misfire - there are TWO distinct
                // 'Viewport' objects. The answers panel's carries the RectMask2D (ScreenFont 'Text',
                // clip=True, clipping fine); the paper history's carries ONLY the stencil Mask
                // (hasRectMask2D=False), so the 148/149 "rect clipper must exist" guard correctly
                // refused, every tick. Fix: disable the stencil Mask AND ADD a RectMask2D to the
                // same GameObject - its RectTransform IS the viewport rect, so the clip region is
                // correct by construction, on the rect path this panel demonstrably renders.
                // Both actions are recorded and reversed at restore.
                foreach (var mk in c.GetComponentsInChildren<Mask>(true))
                {
                    if (mk.name != "Viewport" || !mk.enabled) continue;
                    if (!mk.gameObject.activeInHierarchy) continue;   // never touch inactive template copies
                    mk.enabled = false;
                    _dialogMasksDisabled.Add(mk);
                    bool added = false;
                    if (mk.GetComponent<RectMask2D>() == null)
                    {
                        _dialogRectMasksAdded.Add(mk.gameObject.AddComponent<RectMask2D>());
                        added = true;
                    }
                    _log.Log("[dialog-clip] '" + c.name + "' Viewport stencil Mask disabled"
                        + (added ? " and RectMask2D added" : "") + " - rect clipping takes over.");
                }
            }

            foreach (var c in all)
            {
                if (c == null || !c.isActiveAndEnabled) continue;
                if (c.renderMode != RenderMode.WorldSpace) continue;
                bool isTransition = c.name.StartsWith("TransitionPCView");
                // 0.6.143: the deployment banner/button ('Prepare for battle' / START THE BATTLE) lives on
                // a native-WorldSpace canvas named plain 'Canvas' (P-dump: raycast hits ButtonImage and
                // BackgroundImage, mode=WorldSpace sort=0) - the TransitionPCView class again: a screen-
                // plane world canvas, floating at scene depth with a parallax offset against the panel
                // reticle; clicks land only when the MONITOR cursor covers it. 'Canvas' is Unity's most
                // generic name (an idle ScreenSpaceOverlay 'Canvas' with draw=0 exists persistently -
                // possibly UMM's own UI), so the qualification is anchored to the observed content.
                bool isDeployCanvas = !isTransition && c.name == "Canvas" && HasDeploymentContent(c);
                if (!isTransition && !isDeployCanvas) continue;
                if (IsConvertedCanvas(c)) continue;
                var t = c.transform;
                // 0.6.146: the deployment canvas rendered panel-centre because unknown names fall to the
                // (0.50,0.50) PlacementFrac default. Recover the game's intended screen position by
                // projecting the pre-convert world position through the UI camera - the screen-plane
                // class parks exactly where the game wants it to appear. TransitionPCView keeps its
                // confirmed-good table placement; only the deploy canvas projects.
                Vector2 lateFrac = PlacementFrac(c.name);
                if (isDeployCanvas)
                {
                    var uiC = FindUICamera();
                    if (uiC != null)
                    {
                        var vp = uiC.WorldToViewportPoint(t.position);
                        if (vp.z > 0f && vp.x > -0.2f && vp.x < 1.2f && vp.y > -0.2f && vp.y < 1.2f)
                            lateFrac = new Vector2(Mathf.Clamp01(vp.x), Mathf.Clamp01(vp.y));
                    }
                }
                _savedCanvases.Add(new SavedCanvas {
                    canvas = c, mode = c.renderMode,
                    parent = t.parent, siblingIndex = t.GetSiblingIndex(),
                    localPos = t.localPosition, localRot = t.localRotation, localScale = t.localScale,
                    isModal = ModalCanvasNames.Contains(c.name),
                    screenFrac = lateFrac,
                    // Never record our own pick camera as the "original": a pre-polluted field
                    // (observed 12.07 - the fresh clone already carried RTVR_PickCam) must restore
                    // to the game's UI camera, exactly as the game itself reasserts VR-off.
                    cam = c.worldCamera == _pickCam ? FindUICamera() : c.worldCamera,
                    panelCam = _pickCam   // 0.6.137: the assignment two lines down makes this the as-converted state
                });
                t.SetParent(_uiRoot, false);
                c.worldCamera = _pickCam;   // clicks map monitor -> panel like every converted canvas
                var scL = _savedCanvases[_savedCanvases.Count - 1];   // 0.6.138/0.6.139: ledger geometry for the reversion self-heal
                var rtL = c.transform as RectTransform;
                if (rtL != null)
                {
                    int idL = c.GetInstanceID();
                    PanelGeom gL;
                    if (!_geomLedger.TryGetValue(idL, out gL))
                    {
                        gL = new PanelGeom { aMin = rtL.anchorMin, aMax = rtL.anchorMax, pivot = rtL.pivot, size = rtL.sizeDelta };
                        _geomLedger[idL] = gL;
                    }
                    scL.panelAnchorMin = gL.aMin; scL.panelAnchorMax = gL.aMax;
                    scL.panelPivot = gL.pivot; scL.panelSize = gL.size;
                    scL.panelGeomValid = true;
                }
                _log.Log("[late-convert] '" + c.name + "' converted to the panel (native WorldSpace; orig parent='"
                    + (scL.parent != null ? scL.parent.name : "none") + "', orig cam='"
                    + (scL.cam != null ? scL.cam.name : "null") + "', frac=("
                    + lateFrac.x.ToString("0.00", CultureInfo.InvariantCulture) + ","
                    + lateFrac.y.ToString("0.00", CultureInfo.InvariantCulture) + ")).");
            }
        }

        static void ConvertHudToWorldSpace()
        {
            EnsureHudPcProjectionHooks();
            _loggedUiLayout = false;
            _logUiProjNow   = true;   // and logs the camera projection once
            var uiCam = FindUICamera();
            var all = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            // Candidates = canvases live on UICamera right now. Log each with its drawable count
            // and whether we keep it, so we can see which ones actually carry visible content.
            var candidates = new List<Canvas>();
            foreach (var c in all)
                if (!IsLiveOverlayCanvas(c) && c.isActiveAndEnabled && c.renderMode == RenderMode.ScreenSpaceCamera
                    && (uiCam == null || c.worldCamera == uiCam))
                    candidates.Add(c);

            _log.Log("HUD candidates on UICamera (" + candidates.Count + "):");
            foreach (var c in candidates)
            {
                var rt = c.transform as RectTransform;
                int drawables = c.GetComponentsInChildren<CanvasRenderer>(false).Length;
                bool keep = ShouldConvert(c.name);
                _log.Log(string.Format("  {0} '{1}' sort={2} size={3:0}x{4:0} drawables={5}",
                    keep ? "KEEP" : "skip", c.name, c.sortingOrder,
                    rt != null ? rt.rect.width : 0f, rt != null ? rt.rect.height : 0f, drawables));
            }

            var toConvert = new List<Canvas>();
            foreach (var c in candidates)
                if (ShouldConvert(c.name)) toConvert.Add(c);
            CapturePcReferenceSize(toConvert);
            PreservePcCanvasHierarchy(toConvert);
            toConvert.Sort((a, b) => a.sortingOrder.CompareTo(b.sortingOrder));   // back-to-front

            // Neutral holder at the origin (identity scale/rotation), sized to a 1920x1080 reference
            // so stretch-anchored canvases inherit a real rect instead of collapsing to 0x0.
            if (_uiRoot == null)
            {
                var rootGo = new GameObject("RTVR_UiRoot", typeof(RectTransform));
                _uiRoot = rootGo.GetComponent<RectTransform>();
                _uiRoot.sizeDelta = _hudReferenceSize;
            }
            _uiRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _uiRoot.localScale = Vector3.one;

            // 0.6.81: dedicated pick camera. Never renders (disabled, mask 0) - it exists so screen-point
            // raycasts map the monitor's full range onto the panel's full extent, whatever the panel's size.
            // PositionWorldSpaceUi keeps its transform and fov matched to the panel every frame.
            if (_pickCam == null)
            {
                var pgo = new GameObject("RTVR_PickCam");
                _pickCam = pgo.AddComponent<Camera>();
                _pickCam.enabled = false;
                _pickCam.cullingMask = 0;
                _pickCam.nearClipPlane = 0.05f;
                _pickCam.farClipPlane  = 50f;
            }

            // 0.6.130: two-phase - capture EVERY entry before mutating ANY canvas. The single-pass
            // version read c.renderMode on nested canvases (root=False) after their roots had already
            // been converted earlier in the same loop, capturing WorldSpace as the "original" mode.
            // The restore then wrote that WorldSpace into the nested canvas's own serialized field:
            // masked while nested, live if the game later re-roots the view (the floating party
            // portraits in menu areas). Convicted by the 12.07 [restore-verify] 'want WorldSpace'
            // lines on exactly the four root=False converts. Captured pre-mutation, a nested canvas
            // records its root's true game mode; root captures are exact as before.
            foreach (var c in toConvert)
            {
                var t = c.transform;
                var rt = t as RectTransform;
                Vector2 frac = CapturePcPlacement(c, uiCam);
                _savedCanvases.Add(new SavedCanvas {
                    canvas = c, mode = c.renderMode,
                    parent = t.parent, siblingIndex = t.GetSiblingIndex(),
                    localPos = t.localPosition, localRot = t.localRotation, localScale = t.localScale,
                    isModal = ModalCanvasNames.Contains(c.name),
                    screenFrac = frac,
                    cam = c.worldCamera   // 0.6.127: pre-redirect original (RedirectRaycastersToGameCamera runs after this loop)
                });
                _log.Log(string.Format("  [ui-canvas] '{0}' root={1} rect={2:0}x{3:0} pivot=({4:0.00},{5:0.00}) placement=({6:0.00},{7:0.00})",
                    c.name, c.rootCanvas == c,
                    rt != null ? rt.rect.width : 0f, rt != null ? rt.rect.height : 0f,
                    rt != null ? rt.pivot.x : 0.5f, rt != null ? rt.pivot.y : 0.5f,
                    frac.x, frac.y));
            }
            foreach (var c in toConvert)
            {
                c.renderMode = RenderMode.WorldSpace;
                c.transform.SetParent(_uiRoot, false);   // keep local values; PositionWorldSpaceUi sets them each frame
            }
            _log.Log("World-space UI: converted " + _savedCanvases.Count + " canvas(es).");
            RedirectRaycastersToGameCamera();
            for (int i = 0; i < _savedCanvases.Count; i++)   // 0.6.137-0.6.139: as-converted camera + ledger geometry for the reversion self-heal
            {
                var sc0 = _savedCanvases[i];
                if (sc0.canvas == null) continue;
                if (sc0.canvas.worldCamera != _pickCam)   // 0.6.140: eventCamera=MainCamera cannot raycast the
                {                                          // panel (combat bar: 0 hits under cursor); pick-cam'd
                    sc0.canvas.worldCamera = _pickCam;     // canvases raycast fine. SavedCanvas.cam holds the
                    _log.Log("[redirect-all] '" + sc0.canvas.name + "' raycast camera -> pick camera.");   // original for restore.
                }
                sc0.panelCam = sc0.canvas.worldCamera;
                var rt0 = sc0.canvas.transform as RectTransform;
                if (rt0 == null) continue;
                int id0 = sc0.canvas.GetInstanceID();
                PanelGeom g0;
                if (!_geomLedger.TryGetValue(id0, out g0))
                {
                    g0 = new PanelGeom { aMin = rt0.anchorMin, aMax = rt0.anchorMax, pivot = rt0.pivot, size = rt0.sizeDelta };
                    _geomLedger[id0] = g0;
                }
                sc0.panelAnchorMin = g0.aMin; sc0.panelAnchorMax = g0.aMax;
                sc0.panelPivot = g0.pivot; sc0.panelSize = g0.size;
                sc0.panelGeomValid = true;
                // A scan that captured a blown rect (the 12.07 poisoning) self-repairs here: the ledger wins.
                if (rt0.sizeDelta != g0.size || rt0.pivot != g0.pivot || rt0.anchorMin != g0.aMin || rt0.anchorMax != g0.aMax)
                {
                    rt0.anchorMin = g0.aMin; rt0.anchorMax = g0.aMax; rt0.pivot = g0.pivot; rt0.sizeDelta = g0.size;
                    _log.Log("[geom-ledger] '" + sc0.canvas.name + "' scanned geometry differed from the ledger - repaired at convert.");
                }
            }
        }

        // The converted roots and their nested child canvases keep UICamera as their raycast event
        // camera, but UICamera's pointer ray doesn't reach the world-space panel (it lives in front of
        // the game camera now). So repoint every world-space UI raycaster that targets UICamera at the
        // game camera instead - matching SurfaceActionBarPCView/PartyPCView, which already work because
        // their event camera falls back to Camera.main. We skip '*Container' canvases: those are 3D
        // world-marker layers, not the flat HUD, and we don't want to disturb their picking.
        static bool IsConvertedCanvas(Canvas c)
        {
            for (int i = 0; i < _savedCanvases.Count; i++) if (_savedCanvases[i].canvas == c) return true;
            return false;
        }
        static void RedirectRaycastersToGameCamera()   // 0.6.81: 'game camera' historically; targets the pick camera now
        {
            var gameCam = Camera.main;          // confirmed to be 'MainCamera' by the input probe
            var uiCam = FindUICamera();
            if (gameCam == null || uiCam == null)
            {
                _log.Log("  Raycaster redirect skipped (gameCam or uiCam null).");
                return;
            }
            _savedUiRaycastCam = uiCam;
            var pick = _pickCam != null ? _pickCam : gameCam;   // 0.6.81: screen->panel mapping camera

            var all = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int n = 0;
            foreach (var c in all)
            {
                if (IsLiveOverlayCanvas(c) || !c.isActiveAndEnabled) continue;
                if (c.renderMode != RenderMode.WorldSpace) continue;
                if (c.worldCamera != uiCam) continue;           // only the ones routing through UICamera
                if (c.name.EndsWith("Container") && !IsConvertedCanvas(c)) continue;   // native world-marker layers stay; our converted map containers join the panel
                if (c.GetComponent<GraphicRaycaster>() == null) continue;
                c.worldCamera = pick;
                _redirectedCanvases.Add(c);
                n++;
            }
            _log.Log("  Raycaster redirect: repointed " + n + " canvas(es) from '" + uiCam.name + "' to '" + pick.name + "'.");
        }

        static void VerifyRestoredCanvases(string when)
        {
            if (!DiagnosticsRecording) return;
            int bad = 0;
            foreach (var rc in _restoreChecks)
            {
                if (rc.canvas == null) continue;   // Unity null = destroyed since the restore; nothing to verify
                bool modeOk = rc.canvas.renderMode == rc.mode;
                bool camOk  = rc.canvas.worldCamera == rc.cam;
                bool parOk  = rc.canvas.transform.parent == rc.parent;
                if (modeOk && camOk && parOk) continue;
                bad++;
                _log.Log("[restore-verify " + when + "] '" + rc.canvas.name + "'"
                         + " act=" + (rc.canvas.isActiveAndEnabled ? 1 : 0)
                         + " root=" + (rc.canvas.rootCanvas == rc.canvas ? 1 : 0)
                         + " mode=" + rc.canvas.renderMode + (modeOk ? "" : " (want " + rc.mode + ")")
                         + " cam=" + (rc.canvas.worldCamera == null ? "null" : rc.canvas.worldCamera.name)
                         + (camOk ? "" : " (want " + (rc.cam == null ? "null" : rc.cam.name) + ")")
                         + (parOk ? " parent ok" : " parent=" + (rc.canvas.transform.parent == null ? "null" : rc.canvas.transform.parent.name)
                                                  + " (want " + (rc.parent == null ? "null" : rc.parent.name) + ")"));
            }
            if (bad == 0) _log.Log("[restore-verify " + when + "] all " + _restoreChecks.Count + " restored canvases hold their game state.");
        }

        static void RestoreHudCanvases()
        {
            RestoreSurfaceDialogLayout72();
            StopPcHudPresentation();
            RestoreHudViewportMasks();
            StopSpatialUi();
            StopHudCapture();
            // 0.6.117 (doll-panel fix, part 2): the doll-room flanking panels' appear swing is a
            // world-referenced tween on unscaled time, bound during the first frames of the inventory
            // while RTVR is still attached. It runs (and usually completes) correctly against our
            // player-facing rotated chain - world pose right, local pose ~= design minus chain yaw -
            // and it is THIS restore, flattening the chain, that used to strand the finished pose
            // back-facing: the reversed-panel bug, magnitude equal to the player's facing at bind time.
            // The panels' world rotation is correct immediately before this restore, so snapshot it and
            // re-apply it after the chain flattens. Under a rotated chain that plants the design pose in
            // the restored frame; under an unrotated chain it is a mathematical no-op; a still-running
            // tween simply continues to its (world) target afterwards - verified live in 0.6.116. When
            // the panels are inactive (ordinary area transitions) nothing is captured, and the
            // idempotent second call skips via the empty-list gate.
            var preservedBg = new List<KeyValuePair<Transform, Quaternion>>();
            if (DiagnosticsRecording && _savedCanvases.Count > 0)
            {
                var actives = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < actives.Length; i++)
                {
                    var ac = actives[i];
                    if (ac == null) continue;
                    if (ac.name != "InventoryLeftCanvas" && ac.name != "InventoryRightCanvas") continue;
                    var abg = ac.transform.Find("Background");
                    if (abg != null) preservedBg.Add(new KeyValuePair<Transform, Quaternion>(abg, abg.rotation));
                }
            }

            // 0.6.148/0.6.150: undo the dialogue clipping swap - destroy the rect clippers we added,
            // then re-enable the stencil Masks we disabled (game-destroyed dialogs null-skip).
            foreach (var rm in _dialogRectMasksAdded)
                if (rm != null) UnityEngine.Object.Destroy(rm);
            _dialogRectMasksAdded.Clear();
            foreach (var mk in _dialogMasksDisabled)
                if (mk != null) mk.enabled = true;
            _dialogMasksDisabled.Clear();

            // Put raycast event cameras back to UICamera before anything else.
            foreach (var c in _redirectedCanvases)
                if (c != null) c.worldCamera = _savedUiRaycastCam;
            _redirectedCanvases.Clear();
            _savedUiRaycastCam = null;

            foreach (var s in _savedCanvases)
            {
                if (s.canvas == null) continue;   // Unity null = destroyed; skip
                s.canvas.enabled = true;          // 0.6.141: undo any visibility mirroring; vanilla canvases are enabled
                s.canvas.renderMode = s.mode;
                // 0.6.127: restore the original worldCamera captured at convert time. The redirected-list
                // loop above only covers canvases it tracked; untracked pick-cam assignments left dangling
                // references to the destroyed _pickCam, and a canvas whose worldCamera no longer points at
                // UICamera fails requalification on the next open (the 7 -> 1 conversion collapse). This
                // loop runs after the redirected-list loop, so the per-canvas capture wins where both apply
                // (it is the more correct value). Consequence: StaticCanvas requalifies and re-converts on
                // every open - exactly the world the 0.6.117 swing preservation was built for. Change-gated
                // log: a clean session stays quiet.
                if (s.canvas.worldCamera != s.cam)
                {
                    _log.Log("[cam-fix] '" + s.canvas.name + "' worldCamera '"
                             + (s.canvas.worldCamera == null ? "null" : s.canvas.worldCamera.name) + "' -> '"
                             + (s.cam == null ? "null" : s.cam.name) + "'.");
                    s.canvas.worldCamera = s.cam;
                }
                var t = s.canvas.transform;
                t.SetParent(s.parent, false);     // back under the original parent (s.parent may be Unity-null)
                if (s.parent != null) t.SetSiblingIndex(s.siblingIndex);
                t.localPosition = s.localPos;
                t.localRotation = s.localRot;
                t.localScale    = s.localScale;
                // 0.6.139: repair geometry from the ledger. A reversion racing the heal into a suspend
                // otherwise carries the SS-blown rect/pivot back into game state, where the next attach
                // captures it as truth (the 12.07 poisoning loop). No-op when nothing drifted.
                var rtR = t as RectTransform;
                PanelGeom gR;
                if (rtR != null && _geomLedger.TryGetValue(s.canvas.GetInstanceID(), out gR)
                    && (rtR.sizeDelta != gR.size || rtR.pivot != gR.pivot || rtR.anchorMin != gR.aMin || rtR.anchorMax != gR.aMax))
                {
                    rtR.anchorMin = gR.aMin; rtR.anchorMax = gR.aMax; rtR.pivot = gR.pivot; rtR.sizeDelta = gR.size;
                    _log.Log("[geom-ledger] '" + s.canvas.name + "' geometry repaired at restore.");
                }
            }
            // 0.6.129 diagnostic: snapshot what SHOULD now be true and verify it landed; check again in
            // ~2 s. Gated on a non-empty list so the detach's idempotent re-run (~100 ms after the early
            // restore) cannot wipe the pending +2 s evidence.
            if (_savedCanvases.Count > 0)
            {
                _restoreChecks.Clear();
                foreach (var s in _savedCanvases)
                    if (s.canvas != null)
                        _restoreChecks.Add(new RestoreCheck { canvas = s.canvas, mode = s.mode, cam = s.cam, parent = s.parent });
                if (_restoreChecks.Count > 0)
                {
                    VerifyRestoredCanvases("immediate");
                    _restoreCheckDue = Time.realtimeSinceStartup + 2f;
                }
            }
            _savedCanvases.Clear();
            for (int i = 0; i < preservedBg.Count; i++)
            {
                var bg = preservedBg[i].Key;
                if (bg == null) continue;
                float beforeY = NormAngle(bg.localEulerAngles.y);
                bg.rotation = preservedBg[i].Value;
                float afterY = NormAngle(bg.localEulerAngles.y);
                if (Mathf.Abs(Mathf.DeltaAngle(beforeY, afterY)) > 0.5f)
                    _log.Log("[swing-fix] '" + HierarchyPath(bg) + "' world rotation preserved across restore (locY "
                             + beforeY.ToString("0.0") + " -> " + afterY.ToString("0.0") + ").");
            }
            _chartFit = false; _chartBlend = 0f; _chartProbeDone = false;   // 0.6.82: chart state is per-attach
            _hudPanelDistanceWorld = 0;
            // The reticle is a child of _uiRoot, so destroying the holder destroys it too; just drop our refs.
            _cursorCanvas = null;
            _cursorRect = null;
            if (_uiRoot != null) { UnityEngine.Object.Destroy(_uiRoot.gameObject); _uiRoot = null; }
            if (_pickCam != null) { UnityEngine.Object.Destroy(_pickCam.gameObject); _pickCam = null; }   // 0.6.81
        }

        // One head-facing PC panel. OpenXR view geometry determines its available
        // extent; the pick camera spans the same original PC screen coordinates.
        internal static void PositionWorldSpaceUi(Camera cam, Vector3 headPos, Quaternion headRot)
        {
            if (cam == null) return;
            UpdatePcHudPresentation();
            ApplyPcViewportLayout();

            // Choose the coordinate space from this frame's chart state, before
            // placing either the panel or its picking camera.
            bool chartChanged = false;
            int mapRenderers = 0;
            if (!InNavigationMap) { chartChanged = _chartFit; _chartFit = false; _chartBlend = 0; }
            else if (Time.frameCount % 15 == 0)
            {
                foreach (var saved in _savedCanvases)
                    if (saved.canvas != null && IsMapContainer(saved.canvas.name))
                        mapRenderers += saved.canvas.GetComponentsInChildren<CanvasRenderer>(false).Length;
                bool chartOn = mapRenderers > 0;
                chartChanged = chartOn != _chartFit;
                _chartFit = chartOn;
            }
            float chartTarget = _chartFit ? 1f : 0f;
            _chartBlend = Mathf.Lerp(_chartBlend, chartTarget, UiScaleLerp);
            if (Mathf.Abs(_chartBlend - chartTarget) < 0.005f) _chartBlend = chartTarget;
            BeginHudStableSpace(ref headPos, ref headRot);

            Vector3 fwd = headRot * Vector3.forward;
            Vector3 up  = headRot * Vector3.up;
            Vector3 hudRight = headRot * Vector3.right;
            float uiFar = HudRenderLength(EyeFarClip(Mathf.Max(.01f, WorldScale * .02f), cam.farClipPlane));
            var panel = HudPanelLayout.Calculate(HudPresentationDistance, HudLayoutWorldScale, HudPresentationWidth,
                _hudReferenceSize.x, _hudReferenceSize.y, uiFar, 0, _hudViewPlanes, HudPresentationOffsetX, HudPresentationOffsetY);
            float uiWorldDistance = panel.Distance;
            _hudPanelDistanceWorld = uiWorldDistance;
            Vector3 center = headPos + fwd * uiWorldDistance + hudRight * panel.CentreX + up * panel.CentreY;
            Quaternion faceUser = Quaternion.LookRotation(fwd, up);   // forward = view dir (un-mirrored)

            // The PC screen geometry is independent of the game's camera frustum;
            // the dedicated pick camera spans the exact same panel geometry.
            // The size depends only on the user's panel preference, not the game
            // projection. Applying the same current scale to distance and size
            // keeps the HUD steady during two-hand zoom instead of lagging behind.
            float targetScale = panel.UnitsScale;
            float scale = targetScale;

            // Panel world extent at this scale: a full-screen 1920x1080 canvas spans exactly panelW x panelH.
            float panelW = panel.Width;
            float panelH = panel.Height;

            // 0.6.81: keep the pick camera spanning exactly the panel (vertical frustum = panelH at uiDistance),
            // seated where the panel placement originates so its rays cross the panel plane square-on.
            if (_pickCam != null)
            {
                _pickCam.transform.SetPositionAndRotation(headPos, faceUser);
                _pickCam.farClipPlane = Mathf.Max(HudRenderLength(50), uiWorldDistance * 1.25f);
                ApplyHudPickProjection(panel);
            }
            UpdateHudWorldPickingCamera();
            // 0.6.82: frustum-fit scale for the map containers. 1/m00 is the game camera's horizontal
            // half-tangent, so this is the container scale at which a full-screen 1920-wide canvas spans
            // exactly the game frustum at uiDistance - the scale at which game-projected map content sits
            // on the sightline to the 3D body it annotates (the 0.6.79 geometry, per-layer). Read live
            // each frame so map zoom tracks. cfg.uiWidth is deliberately NOT applied: registration is
            // absolute, not a style knob.
            float gameHalfTan = 1f / Mathf.Max(0.01f, cam.projectionMatrix.m00);
            float fitScale = (2f * uiWorldDistance * gameHalfTan) / _hudPanelReferenceWidth;

            // Chart detection (every 15 frames): live CanvasRenderers on the converted map containers -
            // the 0.6.80-validated signal. The blend chases the flag with the panel-scale lerp constant,
            // snapping when close so registration lands exactly.
            if (chartChanged)
            {
                    _log.Log(string.Format("[chart] map content {0}: container fit {1} - panelScale={2:0.00000} fitScale={3:0.00000} k={4:0.00} renderers={5}",
                        _chartFit ? "detected" : "gone", _chartFit ? "ON" : "OFF",
                        scale, fitScale, fitScale / Mathf.Max(1e-6f, scale), mapRenderers));
                    if (DiagnosticsRecording && _chartFit && !_chartProbeDone) { _chartProbeDone = true; DumpChartContainers(); }
            }

            Vector3 right = faceUser * Vector3.right;
            Vector3 upv   = faceUser * Vector3.up;

            int i = 0;
            Vector3 c0Before = Vector3.zero; bool c0Set = false; int live = 0;   // 0.6.48 diag: where the canvas actually was this frame, before we move it
            foreach (var s in _savedCanvases)
            {
                if (s.canvas == null) continue;
                var tr = s.canvas.transform;
                if (!c0Set) { c0Before = tr.position; c0Set = true; } live++;   // 0.6.48 diag
                if (s.canvas.name == "FadeCanvas")
                {
                    PositionCinematicFadeCanvas(s, headPos, faceUser, uiFar);
                    continue;
                }
                // 0.6.133: vanilla hides the party bar on the sector map while keeping it active and
                // drawing (VR-off dump draw=92 with no visible bar) - our per-frame placement was
                // dragging it back onto the panel. While the chart is live, park it five panel-heights
                // below instead; when the chart goes, normal placement resumes on its own. Spec (Tim,
                // 12.07): present in the inventory (a suspend - this loop never runs there), absent on
                // the map. The pick ray cannot reach the parked bar, so no phantom clicks.
                if (_chartFit && s.canvas.name == "PartyPCView")
                {
                    PlaceHudTransform(tr, center - upv * (panelH * 5f), faceUser, Vector3.one * scale);
                    if (!s.isModal) i++;
                    continue;
                }
                // Offset from the panel centre by the element's placement (read live, so the settings sliders
                // move it immediately): centre (0.5,0.5) stays put; PartyPCView (~0.5,0.9) rides up to the top.
                Vector2 frac = _cfg.uiPos.TryGetValue(s.canvas.name, out var customPlacement) ? customPlacement : s.screenFrac;
                var layer = HudPanelLayout.Calculate(HudPresentationDistance, HudLayoutWorldScale, HudPresentationWidth,
                    _hudReferenceSize.x, _hudReferenceSize.y, uiFar, s.isModal ? .012f : Mathf.Min(i, 8) * .001f,
                    _hudViewPlanes, HudPresentationOffsetX, HudPresentationOffsetY);
                float depthRatio = layer.Distance / uiWorldDistance;
                Vector3 lateral = right * ((frac.x - 0.5f) * panelW * depthRatio)
                                + upv   * ((frac.y - 0.5f) * panelH * depthRatio);
                Vector3 depth = fwd * (layer.Distance - uiWorldDistance) +
                    right * (layer.CentreX - panel.CentreX) + upv * (layer.CentreY - panel.CentreY);
                // tr.position sets the canvas pivot; correct for any pivot != centre so the rect CENTRE lands
                // on the target (full-screen HUD canvases are centre-pivoted, so this is usually zero).
                // 0.6.82: map containers blend from the panel scale to the frustum fit in chart mode,
                // so their game-projected content registers with the 3D behind the panel; everything
                // else (HUD, tooltips, modals) stays at the big uniform panel size.
                float effScale = (IsMapContainer(s.canvas.name) ? Mathf.Lerp(scale, fitScale, _chartBlend) : scale) * depthRatio;
                var rtc = tr as RectTransform;
                Vector3 pivotCorr = rtc != null
                    ? faceUser * (effScale * new Vector3(rtc.rect.center.x, rtc.rect.center.y, 0f))
                    : Vector3.zero;
                // Map containers remain registered to the game camera. The HUD's
                // fine position is a PC layout preference, not a chart offset.
                Vector3 mapOffset = IsMapContainer(s.canvas.name) ?
                    (right * layer.CentreX + upv * layer.CentreY) * _chartBlend : Vector3.zero;
                PlaceHudTransform(tr, center + lateral + depth - pivotCorr - mapOffset, faceUser, Vector3.one * effScale);
                // 0.6.84: marker counter-scale. The marker containers hold one cluster per direct child
                // (0.6.82 probe: centre pivots, localScale untouched by the game), each positioned by the
                // game at its projected screen point. The container's fit scale registers those pivots
                // with the 3D; stamping every direct child with scale/effScale holds the GLYPHS at full
                // panel size throughout the blend (factor 1 at blend 0, 1/k when fitted), so the map
                // reads at the size validated in 0.6.81 while staying registered. Interaction is
                // world-side (no raycasters in these trees), so this is purely visual. Stamped every
                // frame at our late execution order: newly spawned clones are covered, and the factor
                // returns to ~1 as the blend unwinds. Deeper descendants (hover/ping animations) are
                // untouched - only the cluster roots are scaled.
                if (_chartBlend > 0f && IsMarkerContainer(s.canvas.name))
                {
                    // 0.6.85: the boost is capped by the panel slider (markerBoostCap): 1.0 keeps markers
                    // at the registered fit; the 4.0 ceiling guards a pathological k on some future map.
                    float bf = Mathf.Min(scale / Mathf.Max(1e-6f, effScale), _cfg.markerBoostCap);
                    Vector3 boost = Vector3.one * bf;
                    for (int mc = 0; mc < tr.childCount; mc++) tr.GetChild(mc).localScale = boost;
                }
                if (!s.isModal) i++;
            }

            // Projection diagnostics: once per attach, and whenever FOV/m00 shift noticeably, so we can see
            // whether residual placement wobble is a transient (smoothing handles it) or a real per-area FOV.
            if (DiagnosticsRecording)
            {
            float fov = cam.fieldOfView;
            float m00 = cam.projectionMatrix.m00;   // 0.6.81: telemetry only - sizing no longer reads the frustum
            float asp = cam.aspect;
            int curView = ServoSkullActiveView();                                  // 0.6.48 diag
            if (curView != _lastUiView) { _logUiProjNow = true; _lastUiView = curView; }   // force a line on the switch frame
            if (_logUiProjNow || Mathf.Abs(fov - _lastLoggedFov) > 0.5f || Mathf.Abs(m00 - _lastLoggedM00) > 0.02f ||
                Mathf.Abs(panel.AngularWidth - _lastLoggedPanelWidth) > .5f)
            {
                _logUiProjNow = false;
                _lastLoggedFov = fov; _lastLoggedM00 = m00; _lastLoggedPanelWidth = panel.AngularWidth;
                _log.Log(string.Format("[ui-proj] cam='{0}' fov={1:0.0} aspect={2:0.000} m00={3:0.000} "
                    + "widthDeg={4:0.0} targetScale={5:0.00000} appliedScale={6:0.00000} dist={7:0.00} panelW={8:0.00}m",
                    cam.name, fov, asp, m00, panel.AngularWidth, targetScale, scale, panel.Distance / HudLayoutWorldScale, panelW / HudLayoutWorldScale));
                _log.Log("[ui-view-fit] projection=" + (_hudViewPlanes != null ? "tracked binocular frusta" : "startup fallback") +
                    " occupancy=" + _cfg.uiWidth.ToString("0.00", CultureInfo.InvariantCulture) +
                    " PC=" + _hudReferenceSize.x + "x" + _hudReferenceSize.y +
                    " desktop=" + Screen.width + "x" + Screen.height +
                    " aspectPolicy=" + (_cfg.uiAspect <= 0 ? "headset" : "manual") +
                    " offset=" + _cfg.uiOffsetX.ToString("0.000", CultureInfo.InvariantCulture) + "," +
                        _cfg.uiOffsetY.ToString("0.000", CultureInfo.InvariantCulture) +
                    " heightDeg=" + panel.VerticalFov.ToString("0.0", CultureInfo.InvariantCulture));
                _log.Log(string.Format("[ui-pos] view={0} live={1}/{2} camPos=({3:0.0},{4:0.0},{5:0.0}) "
                    + "center=({6:0.0},{7:0.0},{8:0.0}) c0Before=({9:0.0},{10:0.0},{11:0.0})",
                    curView, live, _savedCanvases.Count, headPos.x, headPos.y, headPos.z,
                    center.x, center.y, center.z, c0Before.x, c0Before.y, c0Before.z));
            }

            if (_savedCanvases.Count > 0 && !_loggedUiLayout)
            {
                _loggedUiLayout = true;
                foreach (var s in _savedCanvases)
                {
                    if (s.canvas == null) continue;
                    var rt = s.canvas.transform as RectTransform;
                    _log.Log(string.Format("  UI '{0}' frac=({1:0.00},{2:0.00}) pos={3} lossyScale={4:0.0000} rect={5:0}x{6:0}",
                        s.canvas.name, s.screenFrac.x, s.screenFrac.y,
                        s.canvas.transform.position.ToString("0.00"),
                        s.canvas.transform.lossyScale.x,
                        rt != null ? rt.rect.width : 0f, rt != null ? rt.rect.height : 0f));
                }
            }

            }

            // Reticle (0.6.83, dual-fan): two pointer spaces coexist, and the reticle seats on the ray of
            // whichever one owns what is under the cursor, so it always sits where a click will land.
            // - The PICK camera owns the repointed panel canvases (fan A).
            // - The GAME camera owns everything world-anchored (fan B): the game's own world picking
            //   (ground moves, ship moves), native world-space marker containers, floated overtips, and
            //   the fitted map layer - whose marker trees carry no raycasters at all (0.6.82 probe), so
            //   their interaction is world-side by construction.
            // Crossing between the spaces makes the reticle jump; that is the honest picture, not a bug.
            // The 5 cm pull-in stays ALONG THE OWNING RAY (the 0.6.81 fix for off-centre edge drift).
            if (_cursorCanvas != null)
            {
                _cursorCanvas.enabled = !TouchInputOwned;
                if (TouchInputOwned) return;
                bool cursorLocked = ServoSkullCursorLocked();   // camera mod holds the cursor at centre unless its free-key is held
                UpdateReticleAppearance(cursorLocked);

                var ct = _cursorCanvas.transform;
                ct.rotation   = faceUser;
                ct.localScale = Vector3.one * scale;

                if (cursorLocked)
                {
                    ct.position = center - fwd * 0.05f;   // faint crosshair at the panel centre (where the locked cursor aims)
                }
                else
                {
                    // 0.6.83: dual-fan seating (SeatFreeReticle). Apparent size is held roughly constant
                    // by scaling with the seat depth (the overtip pattern), so a deep world seat stays
                    // legible while a panel seat is unchanged (factor ~1 at uiDistance).
                    ct.position = SeatFreeReticle(cam, fwd, center, out float seatD);
                    ct.localScale = Vector3.one * scale * Mathf.Clamp(seatD / Mathf.Max(0.1f, _cfg.uiDistance), 0.25f, 64f);
                }

                if (_cursorRect != null) _cursorRect.anchoredPosition = Vector2.zero;   // pivot stays at the canvas centre â†’ on the pick point (or screen centre when locked)
            }
        }

        // 0.6.83: seat the free reticle. Fan A = the pick camera's ray, used when the top EventSystem
        // hit is owned by the pick camera (the repointed panel UI): seated on the hit canvas' plane -
        // the proven 0.6.81 maths, unchanged. Fan B = the game camera's ray, used for everything else,
        // because that is the fan the game itself picks with: a world-anchored UI hit (native marker
        // containers, floated overtips, anything EventSystem-visible that is not ours) seats at that
        // element's own depth along the ray, truthful wherever the element lives; with no hit at all
        // (bare world - ground moves, ship moves, the raycaster-less map markers) it seats at the panel
        // plane along the game ray, which on the map is exactly the fitted layer. Bare-world depth on
        // the surface is therefore approximate (direction is what coherence needs); a true terrain-depth
        // seat needs UnityEngine.PhysicsModule - a proposed follow-up, not taken in this build.
        // seatDist reports the chosen depth so the caller can hold the reticle's apparent size constant.
        static PointerEventData _pickPed;
        static readonly List<RaycastResult> _pickHits = new List<RaycastResult>();
        static Vector3 SeatFreeReticle(Camera gameCam, Vector3 fwd, Vector3 center, out float seatDist)
        {
            RaycastResult top = new RaycastResult();
            bool haveHit = TopPointerHit(ref top);

            if (haveHit && _pickCam != null && top.module != null && top.module.eventCamera == _pickCam)
            {
                Ray pickRay = _pickCam.ScreenPointToRay(Input.mousePosition);
                var cv = top.gameObject != null ? top.gameObject.GetComponentInParent<Canvas>() : null;
                float d = _cfg.uiDistance;
                if (cv != null)
                {
                    var plane = new Plane(cv.transform.forward, cv.transform.position);
                    if (!plane.Raycast(pickRay, out d)) d = _cfg.uiDistance;
                }
                seatDist = Mathf.Max(0.05f, d - 0.05f);
                return pickRay.GetPoint(seatDist);
            }

            Ray gameRay = gameCam.ScreenPointToRay(Input.mousePosition);
            if (haveHit && top.gameObject != null)
            {
                // A world-anchored element: its own depth along the game ray puts the reticle on it,
                // regardless of which canvas or camera arrangement it lives in.
                float hd = Vector3.Dot(top.gameObject.transform.position - gameRay.origin, gameRay.direction);
                if (hd > 0.05f)
                {
                    seatDist = Mathf.Max(0.05f, hd - 0.05f);
                    return gameRay.GetPoint(seatDist);
                }
            }
            // A cursor on the actual world hit remains aligned when the head
            // translates: the game's picking ray and input controls are unchanged.
            long worldRayStarted = DiagnosticTimestamp();
            bool foundSurface = Physics.Raycast(gameRay, out RaycastHit surface, gameCam.farClipPlane, ~0, QueryTriggerInteraction.Ignore);
            RecordModStage("PointerWorldRaycast", worldRayStarted);
            if (foundSurface)
            {
                seatDist = Mathf.Max(0.05f, surface.distance - 0.02f);
                return gameRay.GetPoint(seatDist);
            }
            var fall = new Plane(fwd, center);
            float fd;
            if (!fall.Raycast(gameRay, out fd)) fd = _cfg.uiDistance;
            seatDist = Mathf.Max(0.05f, fd - 0.05f);
            return gameRay.GetPoint(seatDist);
        }

        // The top EventSystem hit under the OS cursor, via the same raycast the game's own click
        // handling uses. False when the EventSystem is absent or nothing is under the cursor.
        static bool TopPointerHit(ref RaycastResult top)
        {
            long pointerStarted = DiagnosticTimestamp();
            try { return TopPointerHitOptimized(ref top); }
            finally { RecordModStage("PointerUiQuery", pointerStarted); }
        }

        // Our own reticle. The game's pointer is a hardware (OS) cursor â€” composited by the OS at
        // display time, never in the framebuffer we submit to the HMD â€” so it cannot appear in the
        // eye render. We draw a world-space crosshair on its own canvas, parented to the same neutral
        // _uiRoot holder as the HUD so parent scale can't collapse it. Positioned each frame in
        // PositionWorldSpaceUi to overlay the HUD panel.
        static void CreateCursor()
        {
            if (_uiRoot == null || _cursorCanvas != null) return;

            var canGo = new GameObject("RTVR_Cursor");
            canGo.layer = 5;   // UI layer â€” inside the eye cameras' culling mask (same as the HUD)
            var canRt = canGo.AddComponent<RectTransform>();
            _cursorCanvas = canGo.AddComponent<Canvas>();
            _cursorCanvas.renderMode = RenderMode.WorldSpace;
            _cursorCanvas.sortingOrder = 32760;   // tiebreaker; depth (nearest) is what really wins
            canRt.SetParent(_uiRoot, false);
            canRt.localPosition = Vector3.zero;
            canRt.localRotation = Quaternion.identity;
            canRt.localScale    = Vector3.one;
            canRt.sizeDelta     = new Vector2(RefWidth, RefWidth * 9f / 16f);   // 1920x1080, like the HUD

            var imgGo = new GameObject("Reticle");
            imgGo.layer = 5;
            _cursorRect = imgGo.AddComponent<RectTransform>();
            _cursorImg = imgGo.AddComponent<Image>();   // pulls in CanvasRenderer; uses the default UI material
            _crosshairSprite = MakeCrosshairSprite();
            _cursorImg.sprite = _crosshairSprite;
            _cursorImg.raycastTarget = false;
            _cursorRect.SetParent(canRt, false);
            _cursorRect.anchorMin = _cursorRect.anchorMax = _cursorRect.pivot = new Vector2(0.5f, 0.5f);
            _cursorRect.sizeDelta = new Vector2(40f, 40f);   // canvas units (~40 of 1920 wide)
            _cursorRect.anchoredPosition = Vector2.zero;

            _appliedCursorTex = null; _cursorDirty = true; _reticleState = -1;   // first frame applies the right look + live cursor

            _log.Log("  RTVR_Cursor created (world-space reticle).");
        }

        // True while Servo-Skull is holding the cursor at screen centre (mouselook, free-key up, no UI open).
        // Read once by reflection then cached; a soft link, so a missing camera mod just means "always free".
        static bool ServoSkullCursorLocked()
        {
            if (!_ssResolved)
            {
                _ssResolved = true;
                try
                {
                    Type t = null;
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        t = a.GetType("ServoSkullCameraControls.Main", false);
                        if (t != null) break;
                    }
                    if (t != null) _ssCursorLockedField = t.GetField("CursorLocked", BindingFlags.Public | BindingFlags.Static);
                    _log.Log(_ssCursorLockedField != null
                        ? "Servo-Skull cursor-lock state found; reticle will follow it."
                        : "Servo-Skull cursor-lock state not found; reticle always shows the pointer.");
                }
                catch { }
            }
            if (_ssCursorLockedField == null) return false;
            try { return (bool)_ssCursorLockedField.GetValue(null); } catch { return false; }
        }

        // Switch the reticle between the faint centre crosshair (locked), nothing (locked + crosshair off in
        // settings), and the live game cursor (free). Only does work when the state changes, or â€” while free â€”
        // when the cursor texture changes. Toggling the setting resets _reticleState so it re-applies at once.
        static void UpdateReticleAppearance(bool locked)
        {
            if (_cursorImg == null) return;
            int desired = !locked ? 0 : (_cfg.lockedCrosshair ? 1 : 2);
            if (desired == _reticleState)
            {
                if (desired == 0) ApplyGameCursorIfChanged();   // free: keep tracking cursor-texture changes
                return;
            }
            _reticleState = desired;

            if (desired == 1)        // locked, crosshair on
            {
                _cursorImg.enabled    = true;
                _cursorImg.sprite     = _crosshairSprite;
                _cursorImg.color      = new Color(1f, 1f, 1f, 0.35f);   // faint
                _cursorRect.sizeDelta = new Vector2(24f, 24f);          // small
                _cursorRect.pivot     = new Vector2(0.5f, 0.5f);
            }
            else if (desired == 2)   // locked, crosshair off in settings
            {
                _cursorImg.enabled = false;
            }
            else                     // free â†’ live pointer at full opacity
            {
                _cursorImg.enabled = true;
                _cursorImg.color   = Color.white;
                _appliedCursorTex  = null; _cursorDirty = true;
                ApplyGameCursorIfChanged();
            }
        }

        // Swap the reticle to the game's current cursor texture when it changes. The hotspot becomes the
        // RectTransform pivot, so the cursor's real click-point sits on the pick point (where the reticle is
        // positioned). Sprites are cached per texture to avoid per-change allocation. A null texture (the
        // game asked for the hardware default) falls back to the built-in crosshair.
        static void ApplyGameCursorIfChanged()
        {
            if (!_cursorDirty || _cursorImg == null) return;
            _cursorDirty = false;

            var tex = _gameCursorTex;
            if (tex == _appliedCursorTex) return;
            _appliedCursorTex = tex;

            if (tex == null)
            {
                _cursorImg.sprite     = _crosshairSprite;
                _cursorRect.sizeDelta = new Vector2(40f, 40f);
                _cursorRect.pivot     = new Vector2(0.5f, 0.5f);
                return;
            }

            float w = Mathf.Max(1f, tex.width), h = Mathf.Max(1f, tex.height);
            if (!_cursorSprites.TryGetValue(tex, out var sp))
            {
                sp = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f));   // pivot here is irrelevant for a UI Image
                _cursorSprites[tex] = sp;
            }
            _cursorImg.sprite     = sp;
            _cursorRect.sizeDelta = new Vector2(w, h);                                       // 1:1 with the cursor's pixels
            _cursorRect.pivot     = new Vector2(_gameCursorHotspot.x / w, 1f - _gameCursorHotspot.y / h);  // hotspot: px from top-left â†’ pivot from bottom-left
        }

        // A thin cyan crosshair on a transparent background, generated in code so we need no asset.
        static Sprite MakeCrosshairSprite()
        {
            const int S = 32;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            var clear = new Color32(0, 0, 0, 0);
            var line  = new Color32(0, 255, 255, 255);   // cyan, stands out against the bronze/green UI
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    bool onCross = (x == 15 || x == 16 || y == 15 || y == 16);
                    bool hole    = (x > 12 && x < 19 && y > 12 && y < 19) && !(x == 15 || x == 16 || y == 15 || y == 16);
                    px[y * S + x] = (onCross && !hole) ? line : clear;
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        }


        // Read-only diagnostic: discover how the game routes pointer input. The empirical finding
        // that an invisible mouse already hovers/clicks the world-space UI on the desktop says the
        // game uses Unity's EventSystem + GraphicRaycaster casting a camera ray. We log which camera
        // each raycaster uses, since that decides how to align the cursor with the headset view.
        static void ProbeInput()
        {
            var es = EventSystem.current;
            var allEs = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            _log.Log("INPUT PROBE:");
            _log.Log("  EventSystem.current=" + (es != null ? es.name : "null")
                + " module=" + (es != null && es.currentInputModule != null ? es.currentInputModule.GetType().FullName : "none")
                + " count=" + allEs.Length);
            _log.Log("  Camera.main=" + (Camera.main != null ? Camera.main.name : "null")
                + " UICamera=" + (FindUICamera() != null ? FindUICamera().name : "null"));
            foreach (var s in _savedCanvases)
            {
                if (s.canvas == null) continue;
                var gr = s.canvas.GetComponent<GraphicRaycaster>();
                _log.Log(string.Format("  '{0}' raycaster={1} worldCamera={2} eventCamera={3}",
                    s.canvas.name, gr != null,
                    s.canvas.worldCamera != null ? s.canvas.worldCamera.name : "null",
                    (gr != null && gr.eventCamera != null) ? gr.eventCamera.name : "null"));
            }
        }

        // On-demand diagnostic (Ctrl+Alt+P). Logs every active, non-empty canvas and then the full
        // raycast stack under the cursor. Run it while hovering a clickable element, while hovering a
        // dead one (and after opening the inventory/journal), and in dialogue. Together that tells us
        // whether a dead click is missing its target, blocked by something on top, or landing on a
        // Screen Space canvas we never converted to world space.
        // Read-only probe for the pre-gameplay screens (run from the main menu with Ctrl+Alt+M; VR need not
        // be running). Lists every camera and active canvas so we can see what draws the menu and whether the
        // gameplay MainCamera is absent â€” the groundwork for a theatre view at the menu / load screens.
        static void DumpUiState()
        {
            if (!AllDiagnosticsEnabled) return;
            _log.Log("==== UI STATE DUMP ====");
            _log.Log(string.Format("  mouse={0}  screen={1}x{2}",
                Input.mousePosition.ToString("0"), Screen.width, Screen.height));

            var all = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var active = new List<Canvas>();
            foreach (var c in all)
                if (c.isActiveAndEnabled)
                {
                    int draw = c.GetComponentsInChildren<CanvasRenderer>(false).Length;
                    bool rc = c.GetComponent<GraphicRaycaster>() != null;
                    if (draw > 0 || rc) active.Add(c);   // skip empty container canvases
                }
            active.Sort((a, b) => a.sortingOrder.CompareTo(b.sortingOrder));

            _log.Log("  Active non-empty canvases (" + active.Count + ", low sort first):");
            foreach (var c in active)
            {
                int draw = c.GetComponentsInChildren<CanvasRenderer>(false).Length;
                bool rc = c.GetComponent<GraphicRaycaster>() != null;
                bool keep = ShouldConvert(c.name);
                _log.Log(string.Format("    {0} '{1}' mode={2} sort={3} draw={4} rc={5} cam={6}",
                    keep ? "[CONV]" : "      ", c.name, c.renderMode, c.sortingOrder, draw, rc,
                    c.worldCamera != null ? c.worldCamera.name : "null"));
                if (keep)
                {
                    // 0.6.142: the original-ancestor CanvasGroup chain - the vanilla alpha-hide path a
                    // re-parented canvas no longer inherits. Convicts the hide mechanism directly if the
                    // alpha mirror's theory misses too.
                    Transform op = null;
                    foreach (var sv in _savedCanvases) if (sv.canvas == c) { op = sv.parent; break; }
                    if (op != null)
                    {
                        string chain = "";
                        for (var p = op; p != null; p = p.parent)
                        {
                            var g = p.GetComponent<CanvasGroup>();
                            if (g == null) continue;
                            chain += p.name + "=" + g.alpha.ToString("0.00", CultureInfo.InvariantCulture) + (g.ignoreParentGroups ? "(ipg) " : " ");
                            if (g.ignoreParentGroups) break;
                        }
                        _log.Log("           orig-chain groups: " + (chain.Length > 0 ? chain : "(none)"));
                    }
                }
            }

            // 0.6.146 diagnostic: dialogue clip-state. The overflow survived the W-toggle (shared-
            // material mutation falsified) and scroll-tracks with the content - a genuine mask escape.
            // Per the two-failure rule this names the mask type, the nested override canvases, and
            // whether clipping is even registering per text graphic. One P-dump mid-overflow convicts.
            foreach (var c in active)
            {
                if (!c.name.StartsWith("SurfaceDialogPCView")) continue;
                _log.Log("  Dialog clip-state ('" + c.name + "'):");
                foreach (var m in c.GetComponentsInChildren<RectMask2D>(true))
                    _log.Log("    RectMask2D on '" + m.name + "' enabled=" + m.enabled);
                foreach (var m in c.GetComponentsInChildren<Mask>(true))
                    _log.Log("    Mask on '" + m.name + "' enabled=" + m.enabled + " showGraphic=" + m.showMaskGraphic);
                foreach (var nc in c.GetComponentsInChildren<Canvas>(true))
                    if (nc != c) _log.Log("    nested Canvas '" + nc.name + "' overrideSorting=" + nc.overrideSorting + " sort=" + nc.sortingOrder);
                int textShown = 0, otherCount = 0;
                foreach (var g in c.GetComponentsInChildren<Graphic>(false))
                {
                    bool isText = g.GetType().Name.Contains("Text");
                    if (!isText) { otherCount++; continue; }
                    if (textShown++ >= 12) continue;
                    var cr = g.canvasRenderer;
                    _log.Log("    text '" + g.name + "' type=" + g.GetType().Name
                        + " clip=" + (cr != null && cr.hasRectClipping)
                        + " cull=" + (cr != null && cr.cull)
                        + " mat='" + (g.materialForRendering != null ? g.materialForRendering.name : "null") + "'");
                }
                _log.Log("    (" + otherCount + " non-text graphics; " + textShown + " text graphics total)");
            }

            // --- Area-transition selector subtree. The row-highlight marker lives inside the game's own
            // world-space TransitionPCView(Clone), which RTVR does NOT convert; dump the subtree to find the
            // marker and see whether its position tracks the selected row across captures.
            DumpTransitionSubtree();

            var es = EventSystem.current;
            if (es == null) { _log.Log("  EventSystem.current = null"); _log.Log("==== END DUMP ===="); return; }

            var ped = new PointerEventData(es) { position = Input.mousePosition };
            var hits = new List<RaycastResult>();
            es.RaycastAll(ped, hits);
            _log.Log("  Raycast under cursor (" + hits.Count + " hits, top first):");
            int n = 0;
            foreach (var r in hits)
            {
                if (n++ >= 8) { _log.Log("    ...(more)"); break; }
                var go = r.gameObject;
                var cv = go != null ? go.GetComponentInParent<Canvas>() : null;
                _log.Log(string.Format("    [{0}] '{1}' canvas='{2}' mode={3} sort={4} module={5}",
                    n, go != null ? go.name : "null",
                    cv != null ? cv.name : "?",
                    cv != null ? cv.renderMode.ToString() : "?",
                    cv != null ? cv.sortingOrder : 0,
                    r.module != null ? r.module.GetType().Name : "?"));
            }
            _log.Log("==== END DUMP ====");
        }

        // Dump the game's area-transition selector subtree (TransitionPCView(Clone)). The row-highlight
        // marker that misplaces in VR lives somewhere in here; this prints every node with its type, active
        // state, anchored + world position, and any label text, so we can identify the marker and check
        // whether its world position shifts when a different row is selected (it should, if it tracks).
        static void DumpTransitionSubtree()
        {
            Canvas tc = null;
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c != null && c.name.IndexOf("Transition", StringComparison.OrdinalIgnoreCase) >= 0) { tc = c; break; }
            if (tc == null) { _log.Log("  (no Transition* canvas present - open the area-transition selector, hover a row, then press P)"); return; }
            var ct = tc.transform;
            _log.Log(string.Format("  TransitionCanvas '{0}': mode={1} cam={2} sort={3} worldPos={4} lossyScale={5}",
                tc.name, tc.renderMode, tc.worldCamera != null ? tc.worldCamera.name : "null", tc.sortingOrder,
                ct.position.ToString("0.000"), ct.lossyScale.ToString("0.0000")));
            int budget = 400;
            DumpNode(ct, 0, ref budget);
            if (budget <= 0) _log.Log("    ...(node budget reached; tree truncated)");
        }

        static void DumpNode(Transform t, int depth, ref int budget)
        {
            if (t == null || budget-- <= 0) return;
            var g = t.GetComponent<Graphic>();
            string tag = g != null ? g.GetType().Name : (t.GetComponent<CanvasRenderer>() != null ? "CR" : "-");
            string txt = "";
            if (g != null && tag.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var pi = g.GetType().GetProperty("text");
                if (pi != null)
                {
                    var v = pi.GetValue(g) as string;
                    if (!string.IsNullOrEmpty(v)) txt = " text='" + (v.Length > 24 ? v.Substring(0, 24) : v) + "'";
                }
            }
            var rt = t as RectTransform;
            string ap = rt != null ? rt.anchoredPosition.ToString("0") : "-";
            _log.Log(string.Format("  {0}{1} [{2}] act={3} anchored={4} world={5}{6}",
                new string(' ', depth * 2), t.name, tag, t.gameObject.activeSelf ? "1" : "0",
                ap, t.position.ToString("0.0"), txt));
            // Don't descend into inactive subtrees: their children are inactive in the hierarchy too and
            // can't be the visible marker, but they burn budget (the hidden Footfall / Voidship map variants
            // and the parked act=0 rows accounted for ~35% of the 130-node walk). The node itself is already
            // printed above, so an inactive marker still shows as a single line - we just don't walk its children.
            if (!t.gameObject.activeSelf) return;
            for (int i = 0; i < t.childCount && budget > 0; i++)
                DumpNode(t.GetChild(i), depth + 1, ref budget);
        }

        // 0.6.82: read-only structure probe feeding the 0.6.83 marker counter-scale design. Fires once
        // per attach, on the chart ON flip: each populated map container's DIRECT children in full (the
        // marker inventory), then the children of the first two structured ones as depth-2 exemplars
        // (one cluster's internals). Budgeted; raw log strings; no behaviour.
        static void DumpChartContainers()
        {
            int budget = 120;
            _log.Log("[chart-probe] ==== map container structure ====");
            foreach (var s in _savedCanvases)
            {
                if (s.canvas == null || !IsMapContainer(s.canvas.name)) continue;
                var ct = s.canvas.transform;
                var crt = ct as RectTransform;
                int rend = s.canvas.GetComponentsInChildren<CanvasRenderer>(false).Length;
                _log.Log(string.Format("[chart-probe] '{0}' children={1} renderers={2} rect={3:0}x{4:0} localScale={5:0.00000}",
                    s.canvas.name, ct.childCount, rend,
                    crt != null ? crt.rect.width : 0f, crt != null ? crt.rect.height : 0f, ct.localScale.x));
                if (rend == 0) continue;   // blank layer (e.g. StarsContainer on the system map)
                int exemplars = 0;
                for (int ci = 0; ci < ct.childCount && budget > 0; ci++)
                {
                    var ch = ct.GetChild(ci);
                    DumpChartNode(ch, 1, ref budget);
                    if (exemplars < 2 && ch.gameObject.activeInHierarchy && ch.childCount > 0)
                    {
                        exemplars++;
                        for (int gi = 0; gi < ch.childCount && budget > 0; gi++)
                            DumpChartNode(ch.GetChild(gi), 2, ref budget);
                    }
                }
                if (budget <= 0) { _log.Log("[chart-probe] (line budget reached; dump truncated)"); break; }
            }
            _log.Log("[chart-probe] ==== end ====");
        }

        // One line per node: components (minus the ubiquitous RectTransform/CanvasRenderer), anchored
        // position, pivot, rect, local scale, and the node's active renderer count - the numbers the
        // 0.6.83 counter-scale needs (where the game anchors a cluster; about which pivot it would scale).
        static void DumpChartNode(Transform t, int depth, ref int budget)
        {
            if (t == null || budget-- <= 0) return;
            var rt = t as RectTransform;
            var comps = t.GetComponents<Component>();
            string names = ""; int shown = 0;
            for (int i = 0; i < comps.Length && shown < 6; i++)
            {
                if (comps[i] == null) continue;   // missing-script slots read as null
                string tn = comps[i].GetType().Name;
                if (tn == "RectTransform" || tn == "CanvasRenderer") continue;
                if (names.Length > 0) names += ",";
                names += tn; shown++;
            }
            _log.Log(string.Format("[chart-probe] {0}'{1}'{2} comps=[{3}] anch=({4:0.0},{5:0.0}) pivot=({6:0.00},{7:0.00}) rect={8:0}x{9:0} scale={10:0.000} renderers={11}",
                depth == 1 ? "  " : "    ", t.name, t.gameObject.activeInHierarchy ? "" : " (inactive)", names,
                rt != null ? rt.anchoredPosition.x : 0f, rt != null ? rt.anchoredPosition.y : 0f,
                rt != null ? rt.pivot.x : 0f, rt != null ? rt.pivot.y : 0f,
                rt != null ? rt.rect.width : 0f, rt != null ? rt.rect.height : 0f,
                t.localScale.x, t.GetComponentsInChildren<CanvasRenderer>(false).Length));
        }

        // One-shot read-only probe of DynamicCanvas's structure. We flatten this whole canvas onto the panel,
        // but it mixes true HUD with world-following content (interaction markers, floating combat text,
        // environmental dialogue) that should live in the world over its objects, not on a flat panel. This
        // dumps the top-level sub-trees - names, active state, sizes, and the component types present in each -
        // so we can decide which to keep flattening and which to handle separately. Best run with VR off, in a
        // scene where world icons are visible (near interactables, or during combat for floating text).
        static void DumpGraphicMaterials(Transform root, string prefix)
        {
            var gs = root.GetComponentsInChildren<Graphic>(true);
            if (gs == null || gs.Length == 0) { _log.Log(prefix + "(no UI Graphic components under this widget)"); return; }
            int n = Mathf.Min(gs.Length, 3);
            for (int i = 0; i < n; i++)
            {
                var g = gs[i];
                if (g == null) continue;
                var mat = g.material;
                var matFR = g.materialForRendering;
                string shader = (mat != null && mat.shader != null) ? mat.shader.name : "null";
                string z;
                if (mat == null) z = "no material";
                else if (mat.HasProperty("unity_GUIZTestMode")) z = "unity_GUIZTestMode(prop)=" + mat.GetFloat("unity_GUIZTestMode");
                else if (mat.HasProperty("_ZTest")) z = "_ZTest(prop)=" + mat.GetFloat("_ZTest");
                else z = "no per-material ZTest property (driven by Unity global)";
                _log.Log(string.Format("{0}graphic[{1}] {2}  mat='{3}' shader='{4}'  ztest: {5}  (forRender mat='{6}')",
                    prefix, i, g.GetType().Name, mat != null ? mat.name : "null", shader, z,
                    matFR != null ? matFR.name : "null"));
            }
        }

        // ---- World-space overtips (experimental; toggle Ctrl+Alt+W) ------------------------------------
        // Float the entity-anchored overtips out into the world over their objects, instead of flat on the
        // HUD panel. Each frame (after the game has positioned them) we read each widget's bound entity world
        // position by reflection and set the widget's world transform: floated above the object, billboarded
        // to face the player, at a fixed world size. Off by default, so it never regresses the flat HUD.
        static bool _worldOvertips;
        internal static bool EffectiveWorldOvertips => _worldOvertips && !InSpaceCombat && !InNavigationMap;
        static Transform _overtipsRoot;            // cached OvertipsPCView (parent of the per-object overtip containers)
        static readonly Dictionary<Transform, Vector3> _overtipOrigLocalPos = new Dictionary<Transform, Vector3>();   // 0.6.134: original localPosition per placed widget (restored on toggle-off / VR stop)
        static int _woFrame;                       // throttles the coverage log
        static MethodInfo _subjectPosMethod;       // ServoSkullCameraControls.Main.TryGetSubjectWorldPosition(out Vector3)
        static bool _subjectResolved;
        static FieldInfo _ssActiveViewField;       // ServoSkullCameraControls.Main._activeView (private static int; 0 vanilla, 1, 2)
        static bool _activeViewResolved;
        static readonly HashSet<int> _ztestPatched = new HashSet<int>();   // overtip materials already forced to render on top
        static int _savedGuiZTest = int.MinValue;  // 0.6.46: prior global unity_GUIZTestMode, captured before we override it (MinValue = not overridden)
        const float OvertipHeight          = 2.0f;      // metres above the object's anchor the overtip floats
        const float OvertipWorldScale      = 0.003f;    // world metres per UI unit (the floating size; tune from the headset)
        const float OvertipScaleRefDist    = 4.0f;      // 0.6.47: head distance (m) at/below which overtips keep base size; beyond it they grow ~linearly to hold angular size
        const float OvertipScaleMax        = 4.0f;      // 0.6.47: cap on distance growth, so far/clustered markers don't become giant
        const float OvertipCeilingAboveEye = 0.5f;      // fallback only: cap this far above eye height when the camera subject is unavailable

        static void ToggleWorldOvertips()
        {
            _worldOvertips = !_worldOvertips;
            _woFrame = 0;
            if (!_worldOvertips) RestoreOvertips();   // undo our transform overrides; the game re-places them flat
            _log.Log("World-space overtips: " + (_worldOvertips ? "ON" : "OFF"));
            _cfg.worldOvertips = _worldOvertips;   // 0.6.78: persist the choice
            SaveSettings();
        }

        // Called from Runner.LateUpdate after the eyes and flat panel are placed; 'head' is the eye midpoint.
        // Hide the transition selector's mis-aiming pantograph arm while VR is attached (see field comment).
        // Throttled; only does real work while the area-transition menu is actually open. Safe managed calls
        // only (FindObjectsByType / Transform.Find / SetActive) - no native transform reads.
        internal static void SuppressTransitionPantograph()
        {
            if (Time.unscaledTime < _pantographScanAt) return;
            _pantographScanAt = Time.unscaledTime + PantographScanInterval;

            Canvas tc = null;
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c != null && c.name.IndexOf("Transition", StringComparison.OrdinalIgnoreCase) >= 0) { tc = c; break; }
            if (tc == null) return;   // selector not open

            var arm = tc.transform.Find("PantographView");
            if (arm != null && arm.gameObject.activeSelf)
            {
                arm.gameObject.SetActive(false);
                _hiddenPantograph = arm.gameObject;
                _log.Log("[transition] hid the pantograph readout arm in VR (it mis-aims through Camera.main).");
            }
        }

        internal static void UpdateWorldOvertips(Vector3 head)
        {
            InstallNativeWorldUi66();
            UpdateOvertipBatchIsolation();
            if (!EffectiveWorldOvertips)
            {
                if (_overtipOrigLocalPos.Count != 0 || _savedGuiZTest != int.MinValue) RestoreOvertips();
                return;
            }
            if (!Attached) return;
            if (_overtipsRoot == null) _overtipsRoot = FindOvertipsRoot();
            if (_overtipsRoot == null) return;

            // 0.6.46 experiment: the interactable icon sprites use shader 'Sprites/Glitch', which exposes no
            // per-material ZTest property, so EnsureOvertipOnTop's SetInt is a silent no-op on them and world
            // geometry occludes them. Those shaders read ZTest from the unity_GUIZTestMode *global*, so force
            // it to Always while overtips are floated. Captured once; restored on toggle-off / VR stop, so flat
            // (non-VR) play is untouched. If this doesn't lift the clipping, the shader hardcodes ZTest and the
            // fix moves to a per-icon material swap instead.
            if (_savedGuiZTest == int.MinValue) _savedGuiZTest = Shader.GetGlobalInt("unity_GUIZTestMode");
            Shader.SetGlobalInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);

            // Cap non-unit markers at the player's overtip band, using the camera's focal SUBJECT (pitch- and
            // yaw-stable - it's the orbit centre, not the eye, so the cap no longer rides the camera as you look
            // around). Falls back to eye height only when Servo-Skull's subject isn't available. Unit overtips
            // are never clamped - they read correctly where the game puts them.
            float subjY; bool haveSubj = TryGetSubjectY(out subjY);
            float ceiling = haveSubj ? subjY + OvertipHeight : head.y + OvertipCeilingAboveEye;

            int placed = 0, clamped = 0, errors = 0;
            string egName = null; Vector3 egAnchor = Vector3.zero, egPos = Vector3.zero;
            for (int ci = 0; ci < _overtipsRoot.childCount; ci++)
            {
                var container = _overtipsRoot.GetChild(ci);
                if (container == null || !container.gameObject.activeInHierarchy) continue;
                for (int wi = 0; wi < container.childCount; wi++)
                {
                    var widget = container.GetChild(wi);
                    if (widget == null || !widget.gameObject.activeInHierarchy || _nativeWorldTransforms66.Contains(widget)) continue;
                    try
                    {
                        Vector3 anchor; bool isUnit;
                        if (!TryWidgetAnchor(widget, out anchor, out isUnit)) continue;
                        if (isUnit) { RegisterLegacyWorldInformation70(widget); continue; }
                        RegisterLegacyInteraction70(widget);
                        Vector3 wp = anchor + Vector3.up * OvertipHeight;
                        if (!isUnit && wp.y > ceiling) { wp.y = ceiling; clamped++; }
                        PlaceOvertip(widget, wp, head);
                        if (++placed == 1) EnsureOvertipOnTop(widget);   // patch the (shared) overtip materials to render on top, once per frame
                        if (egName == null && !isUnit) { egName = widget.name; egAnchor = anchor; egPos = wp; }
                    }
                    catch { errors++; }   // a single widget tearing down must not abort the whole pass
                }
            }

            if (DiagnosticsRecording && (_woFrame == 0 || (_woFrame % 120) == 0))
            {
                _log.Log(string.Format("[world-overtip] placed={0} clamped={1} errors={2} ceiling={3:0.0} ref={4} view={5}",
                    placed, clamped, errors, ceiling, haveSubj ? ("subj:" + subjY.ToString("0.0")) : ("eye:" + head.y.ToString("0.0")), ServoSkullActiveView()));
                if (egName != null)
                    _log.Log(string.Format("[world-overtip] eg '{0}' anchor=({1:0.0},{2:0.0},{3:0.0}) -> ({4:0.0},{5:0.0},{6:0.0}) headDist={7:0.0}",
                        egName, egAnchor.x, egAnchor.y, egAnchor.z, egPos.x, egPos.y, egPos.z, Vector3.Distance(head, egPos)));
            }
            PlaceNativeWorldUi66(head);
            _woFrame++;
        }

        // Read the camera's focal subject (the player) world Y via Servo-Skull's public accessor, by reflection
        // (same cross-mod pattern as CursorLocked). This is the orbit centre, so it is stable under pitch and yaw.
        static bool TryGetSubjectY(out float y)
        {
            y = 0f;
            if (!_subjectResolved)
            {
                _subjectResolved = true;
                try
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var t = asm.GetType("ServoSkullCameraControls.Main", false);
                        if (t != null) { _subjectPosMethod = t.GetMethod("TryGetSubjectWorldPosition", BindingFlags.Public | BindingFlags.Static); break; }
                    }
                }
                catch { }
                _log.Log(_subjectPosMethod != null
                    ? "Found Servo-Skull subject accessor; clamping overtips to the camera focal."
                    : "Servo-Skull subject accessor not found; overtips fall back to the eye-height clamp.");
            }
            if (_subjectPosMethod == null) return false;
            try
            {
                var args = new object[] { null };
                object ret = _subjectPosMethod.Invoke(null, args);
                if (ret is bool && (bool)ret && args[0] is Vector3) { y = ((Vector3)args[0]).y; return true; }
            }
            catch { }
            return false;
        }

        // Read the camera mod's active-view index (0 vanilla, 1, 2) by reflection - a soft link, same pattern as
        // CursorLocked/subject. The field is private static, hence NonPublic. Returns -1 if the camera mod or
        // field is absent, so view=-1 in the log is unambiguous (not the same as the camera mod's 0/vanilla).
        static int ServoSkullActiveView()
        {
            if (!_activeViewResolved)
            {
                _activeViewResolved = true;
                try
                {
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var t = a.GetType("ServoSkullCameraControls.Main", false);
                        if (t != null) { _ssActiveViewField = t.GetField("_activeView", BindingFlags.NonPublic | BindingFlags.Static); break; }
                    }
                }
                catch { }
                _log.Log(_ssActiveViewField != null
                    ? "Found Servo-Skull active-view field; tagging overtip logs with the view index."
                    : "Servo-Skull active-view field not found; overtip logs will show view=-1.");
            }
            if (_ssActiveViewField == null) return -1;
            try { return (int)_ssActiveViewField.GetValue(null); } catch { return -1; }
        }

        static Transform FindOvertipsRoot()
        {
            var all = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in all)
                if (c.name == "DynamicCanvas")
                {
                    var t = FindDescendant(c.transform, "OvertipsPCView") ?? FindDescendant(c.transform, "SurfaceOvertipsPCView");
                    if (t != null) return t;
                }
            return null;
        }

        static Transform FindDescendant(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDescendant(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        // A widget root is placeable if one of its view components has a bound VM whose graph yields a world
        // position. isUnit flags the unit overtips (the "overhead text boxes"), which set the clamp ceiling.
        static readonly ReflectionMetadataCache _uiMetadata = new ReflectionMetadataCache(IsFollowType);
        static readonly List<Component> _overTipComponents = new List<Component>();
        static readonly List<Graphic> _overTipGraphics = new List<Graphic>();
        static bool TryWidgetAnchor(Transform widget, out Vector3 pos, out bool isUnit)
        {
            pos = Vector3.zero; isUnit = false;
            widget.GetComponents(_overTipComponents);
            var comps = _overTipComponents;
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i] == null) continue;
                object vm = GetViewModel(comps[i]);
                if (vm == null || !TryFindEntityPosition(vm, 0, out pos)) continue;
                isUnit = vm.GetType().Name.IndexOf("Unit", StringComparison.Ordinal) >= 0;
                return true;
            }
            return false;
        }

        // Depth-first search of a ViewModel graph for a world position. Three ways an overtip exposes one:
        //   - the VM (or entity) has a Position that is a Vector3, or a ReactiveProperty<Vector3> (.Value),
        //   - an EntityRef<...> we can deref to a live entity that exposes the above (the unit chain), or
        //   - the object's View transform, as a fallback.
        // Cache the type layout; read live values so moving and rebound entities still track every frame.
        static bool TryFindEntityPosition(object obj, int level, out Vector3 pos)
        {
            pos = Vector3.zero;
            if (obj == null || level > 4) return false;
            var t = obj.GetType();

            // EntityRef<...> : deref to the live entity, then read a world position off it.
            if (t.IsGenericType && t.Name.StartsWith("EntityRef", StringComparison.Ordinal))
            {
                object ent = TryGetProp(obj, "Entity") ?? TryGetProp(obj, "EntityData") ?? TryGetProp(obj, "Value");
                return ent != null && TryReadWorldPos(ent, out pos);
            }

            // A directly-held VM/entity may expose the position itself (map-object / transition VMs do this,
            // holding the entity inline rather than via an EntityRef).
            if (TryReadWorldPos(obj, out pos)) return true;

            foreach (var field in _uiMetadata.FollowFields(t))
            {
                object val = null; try { val = field.GetValue(obj); } catch { }
                if (val != null && TryFindEntityPosition(val, level + 1, out pos)) return true;
            }
            return false;
        }

        // A world position read straight off an object: its Position (a Vector3, or a ReactiveProperty<Vector3>
        // unwrapped via .Value), else its View transform. Position is read by name, so 'CameraDistance' and other
        // reactive Vector3s are never mistaken for it.
        static bool TryReadWorldPos(object o, out Vector3 pos)
        {
            pos = Vector3.zero;
            if(TryNativeWorldPosition66(o,out pos))return true;
            object p = TryGetProp(o, "Position");
            if (p is Vector3) { pos = (Vector3)p; return true; }
            if (p != null)
            {
                var pt = p.GetType();
                if (pt.IsGenericType && pt.Name.StartsWith("ReactiveProperty", StringComparison.Ordinal))
                {
                    object v = TryGetProp(p, "Value");
                    if (v is Vector3) { pos = (Vector3)v; return true; }
                }
            }
            object view = TryGetProp(o, "View");
            var comp = view as Component;   if (comp != null) { pos = comp.transform.position; return true; }
            var go   = view as GameObject;  if (go   != null) { pos = go.transform.position;   return true; }
            return false;
        }

        // Float a widget at a world position, billboarded to the player, at a fixed world size.
        static void PlaceOvertip(Transform widget, Vector3 worldPos, Vector3 head)
        {
            IsolateWorldOvertipBatches(widget);
            if (!_overtipOrigLocalPos.ContainsKey(widget)) _overtipOrigLocalPos[widget] = widget.localPosition;   // 0.6.134: capture once, pre-override
            widget.position = worldPos;
            Vector3 away = worldPos - head;                // +Z away from the player so the face is toward them
            if (away.sqrMagnitude > 1e-4f)                 // (if the panel renders mirrored, swap to head - worldPos)
                widget.rotation = Quaternion.LookRotation(away, Vector3.up);
            var parent = widget.parent;
            float pls = (parent != null) ? parent.lossyScale.x : 1f;
            if (Mathf.Abs(pls) < 1e-6f) pls = 1f;
            // 0.6.47: scale up with head distance so far markers stay legible (constant angular size beyond the
            // reference distance), clamped so near markers keep base size and far ones don't balloon.
            float distFactor = Mathf.Clamp(Vector3.Distance(head, worldPos) / OvertipScaleRefDist, 1f, OvertipScaleMax);
            widget.localScale = Vector3.one * (OvertipWorldScale / pls) * distFactor;
        }

        // Force the overtip UI to render on top of world geometry so a model can't swallow it. The game's overtip
        // shaders take ZTest from the unity_GUIZTestMode global rather than a material property; setting it on the
        // (shared) material overrides that per-material. Cached by material instance, so each is touched once.
        static void EnsureOvertipOnTop(Transform widget)
        {
            widget.GetComponentsInChildren(true, _overTipGraphics);
            var gs = _overTipGraphics;
            int always = (int)UnityEngine.Rendering.CompareFunction.Always;
            for (int i = 0; i < gs.Count; i++)
            {
                var g = gs[i];
                var m = (g != null) ? g.material : null;
                if (m == null) continue;
                int id = m.GetInstanceID();
                if (_ztestPatched.Contains(id)) continue;
                _ztestPatched.Add(id);
                m.SetInt("unity_GUIZTestMode", always);                            // Owlcat/UI/Default + glitch FX (sprite/background)
                if (m.HasProperty("_ZTestMode")) m.SetInt("_ZTestMode", always);   // TextMeshPro (the bark/POI text)
                if (m.HasProperty("_ZTest"))     m.SetInt("_ZTest", always);       // any other UI shader that exposes it
            }
        }

        // Undo our transform overrides so the game's per-frame anchoredPosition puts the widgets back on the panel.
        static void RestoreOvertips()
        {
            RestoreNativeCombatOffsets68();
            RestoreNativeHealth68();
            StopOvertipBatchIsolation();
            _overTipComponents.Clear(); _overTipGraphics.Clear();
            RestoreOvertipZTest();   // 0.6.46: undo the global ZTest override first (no-op if we never set it)
            if (_overtipsRoot == null) return;
            for (int ci = 0; ci < _overtipsRoot.childCount; ci++)
            {
                var container = _overtipsRoot.GetChild(ci);
                for (int wi = 0; wi < container.childCount; wi++)
                {
                    var w = container.GetChild(wi);
                    w.localScale = Vector3.one;
                    w.localRotation = Quaternion.identity;
                }
            }
            // 0.6.134: restore positions too. PlaceOvertip writes widget.position (world); the
            // scale/rotation-only restore relied on the game re-driving positions - true for unit
            // overtips (re-projected every frame) but NOT for transition overtips (static map-object
            // markers, placed once), which stayed stranded at raised world anchors after VR stop and
            // after manual toggle-off (12.07, Tim). Local space stays valid under the unchanged parent.
            foreach (var kv in _overtipOrigLocalPos)
            {
                var t = kv.Key;
                if (t == null) continue;   // destroyed since capture
                t.localPosition = kv.Value;
            }
            _overtipOrigLocalPos.Clear();
        }

        // 0.6.46: restore the global unity_GUIZTestMode we overrode for the glitch-shader icons. Safe to call
        // unconditionally - it's a no-op unless we actually captured/overrode the value.
        static void RestoreOvertipZTest()
        {
            if (_savedGuiZTest == int.MinValue) return;
            Shader.SetGlobalInt("unity_GUIZTestMode", _savedGuiZTest);
            _savedGuiZTest = int.MinValue;
        }

        // Dump the bound-VM widgets under DynamicCanvas that do NOT resolve to an entity world position - one
        // example per view type. These are the icons still stuck flat on the panel (map-transition / POI
        // markers, interaction prompts, ...). DumpObject + ScanSpatialProps will surface whatever anchor they
        // do expose (a Vector3, a target Transform, a non-MechanicEntity ref), so we can extend the search.
        static void CollectBoundViews(Transform root, List<Component> outList, int max)
        {
            if (outList.Count >= max) return;
            if (root.gameObject.activeInHierarchy)
            {
                var comps = root.GetComponents<Component>();
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] == null) continue;
                    if (GetViewModel(comps[i]) != null) { outList.Add(comps[i]); break; }
                }
            }
            for (int i = 0; i < root.childCount && outList.Count < max; i++)
                CollectBoundViews(root.GetChild(i), outList, max);
        }

        // Active components under 'root' whose type name mentions overtip/locator (one per object), capped.
        static void CollectOvertipViews(Transform root, List<Component> outList, int max)
        {
            if (outList.Count >= max) return;
            if (root.gameObject.activeInHierarchy)
            {
                var comps = root.GetComponents<Component>();
                for (int i = 0; i < comps.Length; i++)
                {
                    var c = comps[i];
                    if (c == null) continue;
                    string n = c.GetType().Name.ToLowerInvariant();
                    if (n.Contains("overtip") || n.Contains("locator")) { outList.Add(c); break; }
                }
            }
            for (int i = 0; i < root.childCount && outList.Count < max; i++)
                CollectOvertipViews(root.GetChild(i), outList, max);
        }

        // Read a view's bound ViewModel (the auto-property backing field), searching up the type hierarchy.
        static object GetViewModel(Component c)
        {
            try
            {
                return _uiMetadata.ViewModelField(c.GetType())?.GetValue(c);
            }
            catch { }
            return null;
        }

        // Dump an object's spatial / entity / view-model fields, following entity-like references up to 2 levels
        // deep, plus any readable Position/View property. The aim is to surface a world position we can place an
        // overtip at in 3D. Bounded and defensive throughout (game internals can throw when touched).
        static void DumpObject(object obj, string indent, int level)
        {
            if (obj == null) { _log.Log(indent + "(null)"); return; }
            int shown = 0, depth = 0;
            var t = obj.GetType();
            while (t != null && t != typeof(object) && depth < 4 && shown < 18)
            {
                FieldInfo[] fields;
                try { fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); }
                catch { break; }
                for (int i = 0; i < fields.Length && shown < 18; i++)
                {
                    var f = fields[i];
                    var ft = f.FieldType;
                    bool anchor = IsAnchorType(ft);
                    bool follow = IsFollowType(ft);
                    if (!anchor && !follow) continue;
                    object val = null; try { val = f.GetValue(obj); } catch { }
                    _log.Log(string.Format("{0}{1} : {2}{3}", indent, f.Name, FriendlyType(ft), HintFor(val)));
                    shown++;
                    if (follow && level < 2 && val != null && !(val is UnityEngine.Object))
                        DumpObject(val, indent + "    ", level + 1);
                    else if (val != null && level < 3 && ft.IsGenericType && ft.Name.StartsWith("EntityRef", StringComparison.Ordinal))
                    {
                        object ent = TryGetProp(val, "Entity") ?? TryGetProp(val, "EntityData") ?? TryGetProp(val, "Value");
                        if (ent != null) { _log.Log(indent + "    -> deref " + ent.GetType().Name); DumpObject(ent, indent + "        ", level + 1); }
                        else _log.Log(indent + "    -> EntityRef deref null (tried .Entity / .EntityData / .Value)");
                    }
                }
                t = t.BaseType; depth++;
            }
            ScanSpatialProps(obj, indent);
            if (shown == 0) _log.Log(indent + "(no spatial/entity fields - see any spatial props above)");
        }

        // Log every readable, non-indexed property that returns a spatial type (Vector3/Vector2/Transform/
        // GameObject/Component) - this surfaces the world-position accessor whatever it is named. Spatial-typed
        // getters only, each guarded, capped; non-spatial getters are never invoked (no side effects).
        static void ScanSpatialProps(object obj, string indent)
        {
            try
            {
                var props = obj.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                int shown = 0;
                for (int i = 0; i < props.Length && shown < 8; i++)
                {
                    var p = props[i];
                    if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                    var pt = p.PropertyType;
                    bool spatial = pt == typeof(Vector3) || pt == typeof(Vector2) || pt == typeof(Transform)
                        || pt == typeof(GameObject) || typeof(Component).IsAssignableFrom(pt);
                    if (!spatial) continue;
                    object v = null; try { v = p.GetValue(obj, null); } catch { continue; }
                    _log.Log(string.Format("{0}.{1} : {2}{3}", indent, p.Name, pt.Name, HintFor(v)));
                    shown++;
                }
            }
            catch { }
        }

        // Read a named, non-indexed property's value, or null. Defensive.
        static object TryGetProp(object obj, string name)
        {
            return _uiMetadata.ReadProperty(obj, name);
        }

        // Reference types worth following one level deeper to chase a world position (entities, view models,
        // data/models). Unity objects, primitives, strings, enums and collections are not followed.
        static bool IsFollowType(Type t)
        {
            if (t.IsPrimitive || t.IsEnum || t == typeof(string)) return false;
            if (t.IsArray || t.IsGenericType) return false;
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return false;
            string n = t.Name;
            return n.IndexOf("Entity", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Unit", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("ViewModel", StringComparison.OrdinalIgnoreCase) >= 0
                || n.EndsWith("VM", StringComparison.Ordinal)
                || n.IndexOf("Model", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Data", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static int Depth(Transform t)
        {
            int d = 0; var p = t.parent;
            while (p != null && d < 64) { d++; p = p.parent; }
            return d;
        }

        // Wrap an angle into [-180, 180] for readable rotation logging (doll-panel fix, 0.6.117).
        static float NormAngle(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            else if (a < -180f) a += 360f;
            return a;
        }

        static string HierarchyPath(Transform t)
        {
            string s = t.name; var p = t.parent; int guard = 0;
            while (p != null && guard++ < 12) { s = p.name + "/" + s; p = p.parent; }
            return s;
        }

        // Field types worth reporting as a possible world anchor: Unity spatial types, game entities, the
        // MVVM ViewModel that likely holds the tracked object, or a collection of per-object entries.
        static bool IsAnchorType(Type t)
        {
            if (t == typeof(Transform) || t == typeof(RectTransform) || t == typeof(GameObject)
                || t == typeof(Vector3) || t == typeof(Vector2)) return true;
            if (t.IsArray || t.IsGenericType) return true;
            string n = t.Name;
            return n.IndexOf("Unit", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Entity", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("MapObject", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Marker", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Locator", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Overtip", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("ViewModel", StringComparison.OrdinalIgnoreCase) >= 0
                || n.EndsWith("VM", StringComparison.Ordinal);
        }

        static string FriendlyType(Type t)
        {
            if (t.IsArray) return (t.GetElementType() != null ? t.GetElementType().Name : "?") + "[]";
            if (t.IsGenericType)
            {
                var args = t.GetGenericArguments();
                var names = new List<string>();
                for (int i = 0; i < args.Length; i++) names.Add(args[i].Name);
                string b = t.Name; int tick = b.IndexOf('`'); if (tick >= 0) b = b.Substring(0, tick);
                return b + "<" + string.Join(", ", names.ToArray()) + ">";
            }
            return t.Name;
        }

        // Short read-only value hint: world position for a Transform/GameObject/Component, the value for a
        // Vector, element count for a collection. Defensive - destroyed Unity objects throw when dereferenced.
        static string HintFor(object v)
        {
            try
            {
                if (v == null) return " = null";
                var tr = v as Transform;  if (tr != null) return " = pos" + tr.position.ToString("0.0");
                var go = v as GameObject; if (go != null) return " = '" + go.name + "' pos" + go.transform.position.ToString("0.0");
                if (v is Vector3 v3) return " = " + v3.ToString("0.0");
                if (v is Vector2 v2) return " = " + v2.ToString("0.0");
                var comp = v as Component; if (comp != null) return " @ pos" + comp.transform.position.ToString("0.0");
                var col = v as System.Collections.ICollection; if (col != null) return " count=" + col.Count;
                return "";
            }
            catch { return " = <unreadable>"; }
        }
    }

    // High execution order so our LateUpdate (eye config, UI lock, overtip placement) runs AFTER the game's own
    // per-frame overtip positioning. Otherwise, in scenes whose component load order puts the game's positioner
    // last, it overwrites our world placement a frame later and the overtips oscillate ("swim").
}


