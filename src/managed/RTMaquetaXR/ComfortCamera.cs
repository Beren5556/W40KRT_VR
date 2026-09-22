using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _touchCameraInstalled, _touchCameraSession, _touchCameraFault, _touchCameraStopRequested;
        static string _touchCameraStatus = "pending";
        static PropertyInfo _touchRigInstance, _touchZoomLength, _touchDefaultZoom, _touchFovDefault,
            _touchPhysicalZoom, _touchPhysicalMin, _touchPhysicalMax;
        static FieldInfo _touchRigCamera, _touchRigZoom, _touchZoomCamera, _touchZoomPlayer, _touchZoomTarget,
            _touchZoomSmooth, _touchZoomGamepad, _touchScrollOffset, _touchScrollBy2D, _touchRotateOffset,
            _touchRotationMouse, _touchRotationKeyboard, _touchBaseMouse;
        static Camera _touchPresetCamera;
        static object _touchPresetZoom;
        static float _touchSavedFov, _touchPresetFov, _touchPresetScroll;
        static Vector3 _touchSavedLocalPosition, _touchPresetLocalPosition;
        static float _touchSavedPlayer, _touchSavedTarget, _touchSavedSmooth, _touchSavedGamepad;
        static bool _touchPresetPhysical;
        static int _touchClearedRigFrame = -1, _touchToyBoxHooks;
        static long _touchSuppressedRigTicks, _touchSuppressedZoomTicks, _touchSuppressedToyBox;

        // A runtime failure retains input ownership until the outer VR lifecycle
        // stops. Falling through here would re-enable unbounded game/ToyBox input.
        static bool TouchCameraOwned => _touchCameraInstalled && _touchCameraSession && _active &&
            (_touchCameraFault || (_attached && !_modeFlat && _attachedCam != null));
        internal static bool TouchCameraFaulted => _touchCameraFault;
        internal static string TouchCameraFaultReason => _touchCameraFault ? _touchCameraStatus : null;
        internal static bool ConsumeTouchCameraStopRequest(out string reason)
        {
            reason = TouchCameraFaultReason;
            if (!_touchCameraStopRequested) return false;
            _touchCameraStopRequested = false; return true;
        }
        internal static bool ComfortControlsAvailable => _touchCameraInstalled && !_touchCameraFault;
        internal static string CameraModeName => "Touch · cámara acotada";
        internal static float TouchWorldScale => InSpaceCombat ? _touchSpaceScale : Release48Settings.WorldScale(_cfg.worldScale, _cfg.limitExplorationZoom);
        internal static float TouchTurnSpeed => ComfortCameraOptions.Speed(_cfg.comfort.touchTurnSpeed);
        internal static float TouchGestureTurnSpeed => ComfortCameraOptions.Speed(_cfg.comfort.touchGestureTurnSpeed);
        internal static void SetTouchGestureTurnSpeed(float value)
        {
            value = ComfortCameraOptions.Speed(value);
            if (_cfg.comfort.touchGestureTurnSpeed == value) return;
            _cfg.comfort.touchGestureTurnSpeed = value; MarkSettingsDirty();
        }
        internal static float TouchZoomSpeed => ComfortCameraOptions.Speed(_cfg.comfort.touchZoomSpeed);
        internal static float TouchMoveSpeed => ComfortCameraOptions.Speed(_cfg.comfort.touchMoveSpeed);
        internal static void SetTouchMoveSpeed(float value)
        {
            value = ComfortCameraOptions.Speed(value);
            if (_cfg.comfort.touchMoveSpeed == value) return;
            _cfg.comfort.touchMoveSpeed = value; MarkSettingsDirty();
        }
        internal static void SetTouchTurnSpeed(float value)
        {
            value = ComfortCameraOptions.Speed(value);
            if (_cfg.comfort.touchTurnSpeed == value) return;
            _cfg.comfort.touchTurnSpeed = value; MarkSettingsDirty();
        }
        internal static void SetTouchZoomSpeed(float value)
        {
            value = ComfortCameraOptions.Speed(value);
            if (_cfg.comfort.touchZoomSpeed == value) return;
            _cfg.comfort.touchZoomSpeed = value; MarkSettingsDirty();
        }
        internal static void SetTouchWorldScale(float value)
        {
            if (InSpaceCombat) return; // Spatial scale is gesture-bounded relative to the ship, independently of terrestrial settings.
            value = Release48Settings.WorldScale(value, _cfg.limitExplorationZoom);
            if (_cfg.worldScale == value) return;
            _cfg.worldScale = value;
            _touchTabletop.SetScale(value);
            MarkSettingsDirty();
        }

        static FieldInfo TouchCameraField(Type type, string name, Type expected)
        {
            var field = AccessTools.Field(type, name);
            if (field == null || field.IsStatic || field.FieldType != expected) throw new MissingFieldException(type?.FullName, name);
            return field;
        }
        static PropertyInfo TouchCameraProperty(Type type, string name, Type expected)
        {
            var property = AccessTools.Property(type, name);
            if (property == null || property.PropertyType != expected || property.GetGetMethod(true) == null)
                throw new MissingMemberException(type?.FullName, name);
            return property;
        }
        // Existing lifecycle entry names intentionally remain, but the old
        // Libre/Confort camera hooks and settings no longer exist.
        internal static void InstallComfortCameraControls()
        {
            _touchCameraSession = true; _touchCameraFault = _touchCameraStopRequested = false;
            _touchCameraStatus = _touchCameraInstalled ? "ready" : "pending";
            if (_touchCameraInstalled) return;
            _touchToyBoxHooks = 0;
            var patched = new List<MethodBase>();
            try
            {
                var rig = AccessTools.TypeByName("Kingmaker.View.CameraRig");
                var zoom = AccessTools.TypeByName("Kingmaker.View.CameraZoom");
                if (rig == null || zoom == null) throw new TypeLoadException("CameraRig / CameraZoom");
                _touchRigInstance = TouchCameraProperty(rig, "Instance", rig);
                _touchRigCamera = TouchCameraField(rig, "<Camera>k__BackingField", typeof(Camera));
                _touchRigZoom = TouchCameraField(rig, "<CameraZoom>k__BackingField", zoom);
                _touchZoomCamera = TouchCameraField(zoom, "m_Camera", typeof(Camera));
                _touchZoomPlayer = TouchCameraField(zoom, "m_PlayerScrollPosition", typeof(float));
                _touchZoomTarget = TouchCameraField(zoom, "m_ScrollPosition", typeof(float));
                _touchZoomSmooth = TouchCameraField(zoom, "m_SmoothScrollPosition", typeof(float));
                _touchZoomGamepad = TouchCameraField(zoom, "m_GamepadScrollPosition", typeof(float));
                _touchScrollOffset = TouchCameraField(rig, "m_ScrollOffset", typeof(Vector2));
                _touchScrollBy2D = TouchCameraField(rig, "m_ScrollBy2D", typeof(Vector2));
                _touchRotateOffset = TouchCameraField(rig, "m_RotateOffset", typeof(float));
                _touchRotationMouse = TouchCameraField(rig, "m_RotationByMouse", typeof(bool));
                _touchRotationKeyboard = TouchCameraField(rig, "m_RotationByKeyboard", typeof(bool));
                _touchBaseMouse = TouchCameraField(rig, "m_BaseMousePoint", typeof(Vector3?));
                _touchZoomLength = TouchCameraProperty(zoom, "ZoomLength", typeof(float));
                _touchDefaultZoom = TouchCameraProperty(zoom, "FovDefaultNormalized", typeof(float));
                _touchFovDefault = TouchCameraProperty(zoom, "FovDefault", typeof(float));
                _touchPhysicalZoom = TouchCameraProperty(zoom, "EnablePhysicalZoom", typeof(bool));
                _touchPhysicalMin = TouchCameraProperty(zoom, "PhysicalZoomMin", typeof(float));
                _touchPhysicalMax = TouchCameraProperty(zoom, "PhysicalZoomMax", typeof(float));
                foreach (string method in new[] { "TickScroll", "TickRotate" })
                    PatchTouchCamera(AccessTools.Method(rig, method, Type.EmptyTypes), nameof(TouchRigInputPrefix), patched);
                PatchTouchCamera(AccessTools.Method(zoom, "TickZoom", Type.EmptyTypes), nameof(TouchZoomInputPrefix), patched);

                // Isolate only ToyBox's explicitly attributed camera callbacks.
                // Returning true from a skipped ToyBox prefix lets vanilla code
                // continue; its other mod features are never patched here.
                var toybox = AccessTools.TypeByName("ToyBox.BagOfPatches.CameraPatches");
                if (toybox != null)
                    foreach (var nested in toybox.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                        foreach (var method in nested.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                        {
                            bool callback = false;
                            foreach (var attribute in method.GetCustomAttributesData())
                                if (attribute.AttributeType.FullName == "HarmonyLib.HarmonyPrefix" || attribute.AttributeType.FullName == "HarmonyLib.HarmonyPostfix") callback = true;
                            if (!callback) continue;
                            if (method.ReturnType != typeof(bool) && method.ReturnType != typeof(void))
                                throw new InvalidOperationException("Unexpected ToyBox camera callback: " + method.Name);
                            PatchTouchCamera(method, method.ReturnType == typeof(bool) ? nameof(TouchToyBoxBooleanPrefix) : nameof(TouchToyBoxVoidPrefix), patched);
                            ++_touchToyBoxHooks;
                        }
                _touchCameraInstalled = true; _touchCameraStatus = "ready";
                _log.Log("[touch/camera] One bounded camera; game camera inputs isolated in stereo VR; ToyBox camera callbacks=" + _touchToyBoxHooks + "; no game settings edited.");
            }
            catch (Exception error)
            {
                foreach (var method in patched)
                    foreach (string patch in new[] { nameof(TouchRigInputPrefix), nameof(TouchZoomInputPrefix), nameof(TouchToyBoxBooleanPrefix), nameof(TouchToyBoxVoidPrefix) })
                        try { _harmony.Unpatch(method, AccessTools.Method(typeof(Main), patch)); } catch { }
                TouchCameraFailure(error);
                throw new InvalidOperationException("No se pudo preparar la cámara Touch acotada", error);
            }
        }
        static void PatchTouchCamera(MethodInfo method, string prefix, List<MethodBase> patched)
        {
            if (method == null) throw new MissingMethodException("Touch camera input hook");
            patched.Add(method);
            _harmony.Patch(method, prefix: new HarmonyMethod(typeof(Main), prefix) { priority = Priority.First });
        }
        static bool TouchToyBoxBooleanPrefix(ref bool __result)
        {
            if (!TouchCameraOwned) return true;
            ++_touchSuppressedToyBox; __result = true; return false;
        }
        static bool TouchToyBoxVoidPrefix()
        {
            if (!TouchCameraOwned) return true;
            ++_touchSuppressedToyBox; return false;
        }
        static bool TouchRigInputPrefix(object __instance)
        {
            if (!TouchCameraOwned) return true;
            if (_touchCameraFault) return false;
            if (CinematicCameraOwnsInput) return true;
            try
            {
                if ((_touchRigCamera.GetValue(__instance) as Camera) != _attachedCam) return true;
                if (_touchClearedRigFrame != Time.frameCount)
                {
                    _touchScrollOffset.SetValue(__instance, Vector2.zero); _touchScrollBy2D.SetValue(__instance, Vector2.zero);
                    _touchRotateOffset.SetValue(__instance, 0f); _touchRotationMouse.SetValue(__instance, false);
                    _touchRotationKeyboard.SetValue(__instance, false); _touchBaseMouse.SetValue(__instance, null);
                    _touchClearedRigFrame = Time.frameCount;
                }
                ++_touchSuppressedRigTicks; return false;
            }
            catch (Exception error) { TouchCameraFailure(error); return false; }
        }
        static bool TouchZoomInputPrefix(object __instance)
        {
            if (!TouchCameraOwned) return true;
            if (_touchCameraFault) return false;
            if (CinematicCameraOwnsInput) return true;
            try
            {
                if ((_touchZoomCamera.GetValue(__instance) as Camera) != _attachedCam) return true;
                _touchZoomGamepad.SetValue(__instance, 0f); ++_touchSuppressedZoomTicks;
                return false;
            }
            catch (Exception error) { TouchCameraFailure(error); return false; }
        }

        static bool PrepareTouchCameraPreset(Camera source, out Vector3 pivot)
        {
            pivot = source == null ? Vector3.zero : source.transform.position;
            if (_touchCameraFault || !TouchCameraOwned || source != _attachedCam) return false;
            try
            {
                var rig = _touchRigInstance.GetValue(null, null) as Component;
                if (rig == null || (_touchRigCamera.GetValue(rig) as Camera) != source) return false;
                pivot = rig.transform.position;
                if (source != _touchPresetCamera)
                {
                    RestoreTouchCameraPreset();
                    var zoom = _touchRigZoom.GetValue(rig);
                    if (zoom == null || (_touchZoomCamera.GetValue(zoom) as Camera) != source) return false;
                    float length = (float)_touchZoomLength.GetValue(zoom, null);
                    float normalized = (float)_touchDefaultZoom.GetValue(zoom, null);
                    float fov = (float)_touchFovDefault.GetValue(zoom, null);
                    if (!ComfortCameraOptions.Finite(length) || length <= 0 || !ComfortCameraOptions.Finite(normalized) ||
                        !ComfortCameraOptions.Finite(fov) || fov < 1 || fov > 160) throw new InvalidOperationException("Invalid game camera default zoom");
                    _touchPresetZoom = zoom; _touchPresetCamera = source;
                    _touchSavedFov = source.fieldOfView; _touchPresetFov = fov;
                    _touchSavedLocalPosition = source.transform.localPosition; _touchPresetLocalPosition = _touchSavedLocalPosition;
                    _touchSavedPlayer = (float)_touchZoomPlayer.GetValue(zoom); _touchSavedTarget = (float)_touchZoomTarget.GetValue(zoom);
                    _touchSavedSmooth = (float)_touchZoomSmooth.GetValue(zoom); _touchSavedGamepad = (float)_touchZoomGamepad.GetValue(zoom);
                    _touchPresetScroll = Mathf.Clamp01(normalized) * length;
                    _touchPresetPhysical = (bool)_touchPhysicalZoom.GetValue(zoom, null);
                    if (_touchPresetPhysical)
                        _touchPresetLocalPosition.z = Mathf.Lerp((float)_touchPhysicalMin.GetValue(zoom, null), (float)_touchPhysicalMax.GetValue(zoom, null), Mathf.Clamp01(normalized));
                    _log.Log("[touch/camera] Fixed game-default FOV=" + fov.ToString("0.0") + "; sole zoom is bounded table scale 6..14; pre-VR zoom retained for restoration.");
                }
                source.fieldOfView = _touchPresetFov;
                if (_touchPresetPhysical) source.transform.localPosition = _touchPresetLocalPosition;
                _touchZoomPlayer.SetValue(_touchPresetZoom, _touchPresetScroll); _touchZoomTarget.SetValue(_touchPresetZoom, _touchPresetScroll);
                _touchZoomSmooth.SetValue(_touchPresetZoom, _touchPresetScroll); _touchZoomGamepad.SetValue(_touchPresetZoom, 0f);
                return true;
            }
            catch (Exception error) { TouchCameraFailure(error); return false; }
        }
        static void RestoreTouchCameraPreset()
        {
            try
            {
                if (_touchPresetCamera != null)
                {
                    if (Mathf.Approximately(_touchPresetCamera.fieldOfView, _touchPresetFov)) _touchPresetCamera.fieldOfView = _touchSavedFov;
                    if (_touchPresetPhysical && _touchPresetCamera.transform.localPosition == _touchPresetLocalPosition)
                        _touchPresetCamera.transform.localPosition = _touchSavedLocalPosition;
                    RestoreTouchScalar(_touchZoomPlayer, _touchSavedPlayer, _touchPresetScroll);
                    RestoreTouchScalar(_touchZoomTarget, _touchSavedTarget, _touchPresetScroll);
                    RestoreTouchScalar(_touchZoomSmooth, _touchSavedSmooth, _touchPresetScroll);
                    RestoreTouchScalar(_touchZoomGamepad, _touchSavedGamepad, 0f);
                }
            }
            catch (Exception error) { _log.Error("[touch/camera] Restore: " + error.Message); }
            _touchPresetCamera = null; _touchPresetZoom = null;
        }
        static void RestoreTouchScalar(FieldInfo field, float saved, float owned)
        {
            if (_touchPresetZoom != null && Mathf.Approximately((float)field.GetValue(_touchPresetZoom), owned)) field.SetValue(_touchPresetZoom, saved);
        }
        internal static void ResetComfortCameraReference()
        {
            RestoreTouchCameraPreset(); _touchTabletop.Cancel(true); _touchPoseReady = false; _touchClearedRigFrame = -1;
        }
        internal static void ResetTouchTabletopForScene() { ResetTouchCameraFocus(); ResetComfortCameraReference(); _touchTabletop.Reset(); }
        internal static void StopComfortCameraControls()
        {
            // Keep an outstanding failure request until Main consumes it, even
            // when Start's catch has already called Stop in this same frame.
            _touchCameraSession = false; ResetTouchTabletopForScene();
        }
        static void TouchCameraFailure(Exception error)
        {
            if (_touchCameraFault) return;
            _touchCameraFault = _touchCameraStopRequested = true; _touchCameraStatus = "stopped: " + error.Message;
            // Restoration is deferred to Stop, after stereo cameras and Touch
            // input have been disabled by the owning VR lifecycle.
            _touchTabletop.Cancel(true); _touchPoseReady = false;
            _log.Error("[touch/camera] " + _touchCameraStatus);
        }
        internal static object ComfortCameraSnapshot() => new {
            Mode = "Touch bounded tabletop", Available = ComfortControlsAvailable, Session = _touchCameraSession,
            Status = _touchCameraStatus, Gesture = TouchGestureName, Limited = TouchGestureLimited, PoseSerial = _touchTabletopSerial,
            WorldScale = TouchWorldScale, ScaleMin = ComfortCameraOptions.ScaleMin, ScaleMax = ComfortCameraOptions.ScaleMax,
            TiltDegrees = _touchTabletop.Tilt, TiltMin = ComfortCameraOptions.TiltMin, TiltMax = ComfortCameraOptions.TiltMax,
            TurnSpeed = TouchTurnSpeed, ZoomSpeed = TouchZoomSpeed, MoveSpeed = TouchMoveSpeed, FixedGameFov = _touchPresetCamera != null ? (float?)_touchPresetFov : null,
            ToyBoxCameraHooks = _touchToyBoxHooks, ToyBoxCameraCallbacksSuppressed = _touchSuppressedToyBox,
            GameCameraTicksSuppressed = _touchSuppressedRigTicks, GameZoomTicksSuppressed = _touchSuppressedZoomTicks,
            WorldObjectsTransformed = false, HeadTrackingFiltered = false
        };
    }
}

