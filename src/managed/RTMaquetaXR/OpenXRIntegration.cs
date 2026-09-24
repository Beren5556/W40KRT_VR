using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static string _modFolder, _lastStartupStatus;
        static bool _restartRequested, _modeFlat = true, _comfortInstalled;
        static PresentationSurface _presentationSurface = PresentationSurface.Panel;
        static bool _actionCameraActive, _actionHookInstalled;
        static RenderTexture _flatFrame;
        static string _modeName = "Unknown";
        static PropertyInfo _gameInstance, _gameMode;
        static FieldInfo _gameModeNameField;
        static PropertyInfo _cameraRigInstance;
        static Camera _zoomCamera;
        static float _zoomBaseFov;
        static readonly Dictionary<int, int> _renderedFrames = new Dictionary<int, int>();
        internal static bool FlatWanted => _modeFlat;
        // FlatWanted stops scene rendering. Only an intentional panel may show
        // the monitor image; a missed stereo frame must never flatten the world.
        internal static bool PresentationQuadWanted => _presentationSurface == PresentationSurface.Panel;
        internal static bool PresentationTransition => _presentationSurface == PresentationSurface.Transition;
        internal static float PanelDistance => HudPresentationDistance;
        internal static Vector3 ViewAnchor(Camera camera)
        {
            if (camera == null) return Vector3.zero;
            if (camera != _zoomCamera)
            {
                _zoomCamera = camera; _zoomBaseFov = camera.fieldOfView;
                _log.Log("[zoom] FOV baseline=" + _zoomBaseFov + "; changes mapped to VR dolly, headset FOV stays physical");
            }
            try
            {
                if (_cameraRigInstance == null)
                    _cameraRigInstance = AccessTools.Property(AccessTools.TypeByName("Kingmaker.View.CameraRig"), "Instance");
                var rig = _cameraRigInstance?.GetValue(null, null) as Component;
                if (rig == null) return camera.transform.position;
                Vector3 pivot = rig.transform.position;
                return pivot + (camera.transform.position - pivot) * Geometry.ZoomFactor(camera.fieldOfView, _zoomBaseFov);
            }
            catch { return camera.transform.position; }
        }
        internal static void RequestRestart() { _restartRequested = true; }
        static void CheckRestart()
        {
            if (!_restartRequested) return;
            _restartRequested = false;
            StopVr(); _autoStartArmed = _cfg.autoStartVr;
            _nextAutoStart = Time.realtimeSinceStartup + 10;
        }
        static void LogStartupStatus()
        {
            if (_lastStartupStatus == OpenXR.Status) return;
            _lastStartupStatus = OpenXR.Status; _log.Log("[OpenXR] " + OpenXR.Status);
        }
        internal static void UpdateFlatMode()
        {
            RefreshSpatialGameContext();
            string mode = ReadGameMode();
            UpdatePresentationContext(mode);
            bool mainMenu = ResumingIntoMainMenu(), loading = LoadingScreenShowing();
            var surface = PresentationSurfacePolicy.Resolve(_dsManualShow, mainMenu, loading, CinematicVideoShowing(mode),
                PresentationStereo(mode), !InNavigationMap && (InSpaceCombat || CinematicPolicy.StereoMode(_presentationSceneMode)), mode, NativeUiOnlyPresentation() || ManagementWindowOpen66());
            bool flat = surface != PresentationSurface.Scene;
            if (!flat && _presentationSurface == PresentationSurface.Panel && !mainMenu && !loading)
                RequestTouchCameraGroupReturn();
            ObserveTouchCameraPresentation(surface != PresentationSurface.Scene || _tutorialShowing || TouchMenuWindowVisible, mainMenu || loading);
            if (surface != PresentationSurface.Transition) UpdateCinematicMode(PresentationSceneMode(mode), flat);
            if (flat != _modeFlat || mode != _modeName)
            {
                _log.Log("[mode] " + mode + " -> " + surface + " inside persistent OpenXR");
            }
            _presentationSurface = surface; _modeFlat = flat; _modeName = mode;
            // Actual scene operations and camera replacement already own their
            // teardown. Temporary pauses/load overlays do not destroy resources.
            if (surface == PresentationSurface.Panel && _attached) SuspendForTransition("intentional menu / prerecorded video panel");
            if (mainMenu && !loading) _presentationSceneMode = null;
            UpdateManagementText81();
        }
        static bool GameplayMode(string mode)
        {
            return CinematicPolicy.StereoMode(mode);
        }
        static string ReadGameMode()
        {
            try
            {
                if (_gameInstance == null)
                {
                    var game = AccessTools.TypeByName("Kingmaker.Game");
                    _gameInstance = AccessTools.Property(game, "Instance");
                    _gameMode = AccessTools.Property(game, "CurrentMode");
                }
                object instance = _gameInstance?.GetValue(null, null);
                object value = instance == null ? null : _gameMode?.GetValue(instance, null);
                if (value == null) return "None";
                // GameModeType is a value class with a public Name field, not an enum.
                if (_gameModeNameField == null || _gameModeNameField.DeclaringType != value.GetType())
                    _gameModeNameField = AccessTools.Field(value.GetType(), "Name");
                return _gameModeNameField?.GetValue(value)?.ToString() ?? value.ToString();
            }
            catch (Exception e) { _log.Log("[mode] " + e.Message); return "Unknown"; }
        }
        internal static bool BothEyesRendered(Camera left, Camera right, int frame)
        {
            if (left == null || right == null || !_hbPatched) return false;
            return _renderedFrames.TryGetValue(left.GetInstanceID(), out int l) && l == frame &&
                   _renderedFrames.TryGetValue(right.GetInstanceID(), out int r) && r == frame;
        }
        internal static RenderTexture CaptureFlatFrame()
        {
            int width = Mathf.Max(64, Screen.width), height = Mathf.Max(64, Screen.height);
            if (_flatFrame == null || _flatFrame.width != width || _flatFrame.height != height)
            {
                ReleaseFlatFrame();
                _flatFrame = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                _flatFrame.Create();
                OpenXR.PrepareTexture(_flatFrame);
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_flatFrame);
            return _flatFrame;
        }
        static void ReleaseFlatFrame()
        {
            if (_flatFrame == null) return;
            OpenXR.ForgetTexture(_flatFrame);
            _flatFrame.Release(); UnityEngine.Object.Destroy(_flatFrame); _flatFrame = null;
        }
        // Flat menus use the same Touch ray mapped onto the submitted OpenXR quad.
        internal static void DrawFlatCursor()
        {
            if (!_active || !_modeFlat || (!TouchInputOwned && !_cfg.mirrorCursor) || Event.current.type != EventType.Repaint) return;
            var position = TouchInputOwned ? (Vector3)TouchPointerScreen : Input.mousePosition;
            var texture = _gameCursorTex;
            if (texture != null)
                GUI.DrawTexture(new Rect(position.x - _gameCursorHotspot.x, Screen.height - position.y - _gameCursorHotspot.y,
                    texture.width, texture.height), texture);
            else
            {
                Color saved = GUI.color; GUI.color = Color.cyan;
                GUI.DrawTexture(new Rect(position.x - 6, Screen.height - position.y - 1, 13, 3), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(position.x - 1, Screen.height - position.y - 6, 3, 13), Texture2D.whiteTexture);
                GUI.color = saved;
            }
        }
        internal static void WriteDiagnostics(XrStats stats, XrFrame frame, bool stereo)
        {
            if (!DiagnosticsRecording) return;
            long reportStarted = BeginDiagnosticReportMeasurement();
            try
            {
                bool haveStereoMeasurements = _stereoMeasurements.ContainsKey("FrameInterval");
                var record = new {
                    Session = _diagnosticSession,
                    Version = BuildTag, Utc = DateTime.UtcNow.ToString("o"), Runtime = "OpenXR / " + OpenXrRuntime.Name,
                    GameMode = _modeName, StereoThisFrame = stereo, HeadPoseValid = frame.valid != 0,
                    Position = new { frame.head.x, frame.head.y, frame.head.z },
                    EyeResolution = new { frame.width, frame.height },
                    SymmetricImageUsage = new {
                        Left = EyeImageMapping(frame.left.fov, frame.width, frame.height),
                        Right = EyeImageMapping(frame.right.fov, frame.width, frame.height)
                    },
                    Resolution = new {
                        VdxrRecommendedWidth = OpenXR.RecommendedWidth, VdxrRecommendedHeight = OpenXR.RecommendedHeight,
                        AppliedModScale = OpenXR.AppliedRenderScale, NextStartModScale = _cfg.renderScale
                    },
                    WorldScale = _cfg.worldScale, Pairs = stats.submittedPairs, FlatFrames = stats.flatFrames,
                    Errors = stats.failedFrames, LeftFrame = stats.lastLeftSerial, RightFrame = stats.lastRightSerial,
                    BothEyesSameFrame = stats.submittedPairs > 0 && stats.lastLeftSerial == stats.lastRightSerial,
                    WaaaghHook = _hbPatched, ComfortHook = _comfortInstalled, ActionCameraHook = _actionHookInstalled,
                    ActionCamera = _actionCameraActive, Status = OpenXR.Status,
                    Presentation = new { Surface = _presentationSurface.ToString(), SceneMode = _presentationSceneMode,
                        TutorialVisible = _tutorialShowing, Cinematic = _cinematicWanted, ManualCamera = _cinematicControl.Manual,
                        Participants = _cinematicParticipants.Count, Framing = _cinematicCloseupStatus },
                    Performance = PerformanceSnapshot(), Overlay = LiveOverlaySnapshot(), CameraControls = ComfortCameraSnapshot(),
                    Loading = LoadingStateSnapshot(),
                    Touch = TouchInputSnapshot(), DiagnosticWriter = DiagnosticWriterSnapshot()
                };
                EnqueueDiagnosticSnapshot(record, haveStereoMeasurements);
            }
            finally { EndDiagnosticReportMeasurement(reportStarted); }
        }
        static object EyeImageMapping(XrFov fov, int width, int height)
        {
            float l = Mathf.Tan(fov.left), r = Mathf.Tan(fov.right), b = Mathf.Tan(fov.down), t = Mathf.Tan(fov.up);
            float x = Mathf.Max(Mathf.Abs(l), Mathf.Abs(r)), y = Mathf.Max(Mathf.Abs(b), Mathf.Abs(t));
            if (x <= 0 || y <= 0 || width <= 0 || height <= 0) return null;
            float horizontal = (r - l) / (2 * x), vertical = (t - b) / (2 * y);
            return new {
                HorizontalFraction = horizontal, VerticalFraction = vertical,
                OutputTextureRegionWidth = Mathf.RoundToInt(width * horizontal),
                OutputTextureRegionHeight = Mathf.RoundToInt(height * vertical)
            };
        }
        static FieldInfo _anchorShake, _cameraShake;
        static PropertyInfo _rigCamera;
        static MethodInfo _shakeTick;
        static FieldInfo _shake;
        static void InstallComfortHook()
        {
            InstallCinematicHooks();
            if (_comfortInstalled) return;
            var type = AccessTools.TypeByName("Kingmaker.View.CameraRig");
            var method = AccessTools.Method(type, "TickShake");
            _anchorShake = AccessTools.Field(type, "m_AnchorShakeOffset");
            _cameraShake = AccessTools.Field(type, "m_CameraShakeOffset");
            _rigCamera = AccessTools.Property(type, "Camera");
            _shake = AccessTools.Field(type, "m_Shake");
            if (method == null || _anchorShake == null || _cameraShake == null || _rigCamera == null || _shake == null)
            { _log.Log("[comfort] CameraRig shape mismatch; no patch applied"); return; }
            _shakeTick = AccessTools.Method(_shake.FieldType, "Tick", new[] { typeof(float) });
            _harmony.Patch(method, prefix: new HarmonyMethod(typeof(Main), nameof(StableCameraPrefix)));
            _comfortInstalled = true; _log.Log("[comfort] Camera shake suppressed only during stereo VR; flat settings preserved");
            var follow = AccessTools.TypeByName("Kingmaker.Controllers.Units.CameraFollowController");
            var start = AccessTools.Method(follow, "StartActionCamera");
            var stop = AccessTools.Method(follow, "StopActionCamera");
            if (start != null && stop != null)
            {
                _harmony.Patch(start, prefix: new HarmonyMethod(typeof(Main), nameof(ActionCameraStart)));
                _harmony.Patch(stop, postfix: new HarmonyMethod(typeof(Main), nameof(ActionCameraStop)));
                _actionHookInstalled = true;
                _log.Log("[comfort] Action and rendered cinematic cameras stay in true stereo VR; authored camera and game logic retained");
            }
        }
        static void ActionCameraStart() { _actionCameraActive = true; }
        static void ActionCameraStop() { _actionCameraActive = false; }
        static bool StableCameraPrefix(object __instance)
        {
            if (!_active || _modeFlat) return true;
            // Let timers finish, undo any residual offsets once, and keep the
            // camera rig's regular movement/rotation/control code untouched.
            try
            {
                var rig = __instance as Component;
                var camera = _rigCamera.GetValue(__instance, null) as Camera;
                if (rig == null || camera == null) return true;
                rig.transform.position -= (Vector3)_anchorShake.GetValue(__instance);
                camera.transform.position -= (Vector3)_cameraShake.GetValue(__instance);
                _anchorShake.SetValue(__instance, Vector3.zero);
                _cameraShake.SetValue(__instance, Vector3.zero);
                _shakeTick?.Invoke(_shake.GetValue(__instance), new object[] { Time.deltaTime });
                var preserveAim = AccessTools.Property(__instance.GetType(), "DontForceLookAtTarget");
                if (!CinematicCameraOwnsInput && preserveAim != null && !(bool)preserveAim.GetValue(__instance, null))
                    camera.transform.LookAt(rig.transform.position);
                return false;
            }
            catch { return true; }
        }
    }
}
