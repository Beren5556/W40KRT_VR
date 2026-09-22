using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static TouchLocalMapContracts _touchLocalMapContracts;
        static Harmony _touchLocalMapHarmony;
        static readonly TouchLocalMapPolicy _touchLocalMapPolicy = new TouchLocalMapPolicy();
        static readonly TouchButtonLatch _touchLocalMapCloseStick = new TouchButtonLatch();
        static readonly List<Transform> _touchLocalMapMarkers = new List<Transform>(64);
        static Component _touchLocalMapView, _touchLocalMapService;
        static object _touchLocalMapModel, _touchLocalMapMenuModel;
        static RectTransform _touchLocalMapImage, _touchLocalMapFrame;
        static Quaternion _touchLocalMapOriginalRotation;
        static bool _touchLocalMapFailure;
        static long _touchLocalMapPanFrames, _touchLocalMapZoomFrames, _touchLocalMapTurnFrames;

        internal static bool TouchLocalMapOwnsAxes => TouchInputOwned && _touchLocalMapView != null && _touchLocalMapModel != null;
        // One native full-window presentation, irrespective of its opener.
        // These shared spatial interfaces remain inert so a map can never be
        // leased/reparented into the wheel or change world presentation mode.
        internal static bool TouchLocalMapCompactVisible => false;
        internal static bool TouchLocalMapCompactRequested => false;
        internal static Component TouchLocalMapCompactWindow => null;
        internal static Component TouchLocalMapCompactService => null;
        internal static bool KeepSceneForTouchLocalMap(string mode) => false;

        // Parent spatial renderer invokes this AFTER relocating the real map.
        // Otherwise a branch snapshot would also suppress the map's children.
        internal static void UpdateTouchLocalMapChromeSuppression()
        {
        }
        internal static void RestoreTouchLocalMapChrome()
        {
        }

        internal static void InstallTouchLocalMap()
        {
            if (_touchLocalMapContracts != null) return;
            try
            {
                _touchLocalMapContracts = TouchLocalMapContracts.Create(AccessTools.TypeByName);
                _touchLocalMapHarmony = new Harmony("RTMaquetaXR.TouchLocalMap");
                _touchLocalMapHarmony.Patch(_touchLocalMapContracts.RotateMethod,
                    prefix: new HarmonyMethod(typeof(Main), nameof(TouchLocalMapBeforeNativeRotation)));
                _touchLocalMapHarmony.Patch(_touchLocalMapContracts.FrameAngleMethod,
                    prefix: new HarmonyMethod(typeof(Main), nameof(TouchLocalMapBeforeNativeFrame)));
                _touchLocalMapFailure = false;
            }
            catch (Exception error)
            {
                StopTouchLocalMap();
                _log.Error("[touch/map] Native map controls unavailable; standard window retained: " + error.Message);
            }
        }

        internal static void ProcessTouchLocalMap(bool overlayWasOpen)
        {
            var c = _touchLocalMapContracts;
            if (c == null) return;
            try
            {
                object surface = c.ReadSurface();
                var view = c.MapPath.Read(surface) as Component;
                object model = view != null && view.gameObject.activeInHierarchy ? c.Model(view) : null;
                if (model == null)
                {
                    if (_touchLocalMapView != null) ClearTouchLocalMapContext();
                    return;
                }
                if (view != _touchLocalMapView || !ReferenceEquals(model, _touchLocalMapModel))
                {
                    ClearTouchLocalMapContext();
                    _touchLocalMapView = view; _touchLocalMapModel = model;
                    _touchLocalMapService = c.ServicePath.Read(surface) as Component;
                    var menu = c.MenuPath.Read(surface); _touchLocalMapMenuModel = menu == null ? null : c.MenuModel(menu);
                    var image = c.Image(view) as RawImage;
                    _touchLocalMapImage = image == null ? null : image.rectTransform;
                    _touchLocalMapFrame = c.Frame(view) as RectTransform;
                    if (_touchLocalMapImage == null) throw new InvalidOperationException("Native local map image is absent");
                    _touchLocalMapOriginalRotation = _touchLocalMapImage.localRotation;
                    CacheTouchLocalMapMarkers(c.FrameBlock(view) as Transform);
                    StopTouchGroupMovement(); _touchGameAxesArmed = false;
                    _touchTabletop.Cancel(true);
                    _log.Log("[touch/map] Native full map; right stick pans, left zooms/rotates.");
                }
                bool allowed = _touchSampleValid && !overlayWasOpen && !TouchOverlayOpen && !TouchOverlayChordCaptured &&
                    !TouchRadialCaptured && !TouchCameraFaulted && !TouchSelectionCaptured && !_touchUiPress.Captured &&
                    !TouchMenuBlockingModal() && _touchSample.left.squeeze < .35f && _touchSample.right.squeeze < .35f &&
                    (_touchSample.left.activeControls & (uint)XrTouchControl.Stick) != 0 &&
                    (_touchSample.right.activeControls & (uint)XrTouchControl.Stick) != 0;
                bool clicked = (_touchSample.left.activeControls & (uint)XrTouchControl.StickClick) != 0 &&
                    (_touchSample.left.buttons & (uint)XrTouchButton.StickClick) != 0;
                _touchLocalMapCloseStick.Step(clicked, allowed);
                if (_touchLocalMapCloseStick.Down) { CloseTouchLocalMap(); return; }
                var motion = _touchLocalMapPolicy.Step(allowed, _touchSample.left.stickX, _touchSample.left.stickY,
                    _touchSample.right.stickX, _touchSample.right.stickY, Time.unscaledDeltaTime);
                _touchGameAxesArmed = false;
                if (motion.Zoom != 0) { c.Zoom(view, motion.Zoom); ++_touchLocalMapZoomFrames; }
                if (motion.Rotation != 0)
                {
                    SetTouchLocalMapRotation(_touchLocalMapImage.localEulerAngles.z + motion.Rotation);
                    ++_touchLocalMapTurnFrames;
                }
                if (motion.PanX != 0 || motion.PanY != 0)
                { c.Pan(view, new Vector2(motion.PanX, motion.PanY)); ++_touchLocalMapPanFrames; }
                _touchLocalMapFailure = false;
            }
            catch (Exception error)
            {
                ClearTouchLocalMapContext();
                if (!_touchLocalMapFailure) _log.Error("[touch/map] Controls suspended; native window retained: " + error.Message);
                _touchLocalMapFailure = true;
            }
        }

        static void CacheTouchLocalMapMarkers(Transform frameBlock)
        {
            _touchLocalMapMarkers.Clear();
            for (int i = 0; i < _touchLocalMapImage.childCount; ++i)
            {
                var group = _touchLocalMapImage.GetChild(i);
                if (group == frameBlock) continue;
                for (int j = 0; j < group.childCount; ++j) _touchLocalMapMarkers.Add(group.GetChild(j));
            }
        }
        static void SetTouchLocalMapRotation(float angle)
        {
            if (_touchLocalMapImage == null || float.IsNaN(angle) || float.IsInfinity(angle)) return;
            var rotation = Quaternion.Euler(0, 0, angle);
            _touchLocalMapImage.localRotation = rotation;
            var counter = Quaternion.Inverse(rotation);
            foreach (var marker in _touchLocalMapMarkers) if (marker != null) marker.localRotation = counter;
        }
        static bool TouchLocalMapBeforeNativeRotation(object __instance, float __0)
        {
            if (!TouchInputOwned || !ReferenceEquals(__instance, _touchLocalMapView) || _touchLocalMapImage == null) return true;
            SetTouchLocalMapRotation(__0); return false;
        }
        static bool TouchLocalMapBeforeNativeFrame(object __instance, float __0)
        {
            if (!TouchInputOwned || !ReferenceEquals(__instance, _touchLocalMapView) || _touchLocalMapFrame == null) return true;
            // Native code assigns world Euler angles because its canvas was
            // originally planar. Keep the camera indicator inside the VR map.
            _touchLocalMapFrame.localRotation = Quaternion.Euler(0, 0, -__0); return false;
        }

        internal static void CloseTouchLocalMap()
        {
            var c = _touchLocalMapContracts;
            if (c == null || _touchLocalMapMenuModel == null) return;
            var menu = c.MenuPath.Read(c.ReadSurface());
            if (menu == null || !ReferenceEquals(c.MenuModel(menu), _touchLocalMapMenuModel)) return;
            c.Close(_touchLocalMapMenuModel); _touchExplorationMapFrame = true; _touchGameAxesArmed = false;
            _pcHudNext = 0;
        }
        static void ClearTouchLocalMapContext()
        {
            RestoreTouchLocalMapChrome();
            if (_touchLocalMapImage != null) SetTouchLocalMapRotation(_touchLocalMapOriginalRotation.eulerAngles.z);
            _touchLocalMapView = _touchLocalMapService = null; _touchLocalMapModel = _touchLocalMapMenuModel = null;
            _touchLocalMapImage = _touchLocalMapFrame = null; _touchLocalMapMarkers.Clear();
            _touchLocalMapCloseStick.Cancel(); _touchLocalMapPolicy.Reset();
        }
        internal static void StopTouchLocalMap()
        {
            ClearTouchLocalMapContext();
            if (!_appQuitting) _touchLocalMapHarmony?.UnpatchAll(_touchLocalMapHarmony.Id);
            _touchLocalMapHarmony = null; _touchLocalMapContracts = null;
        }
        internal static object TouchLocalMapSnapshot() => new { Bound = _touchLocalMapContracts != null,
            Open = TouchLocalMapOwnsAxes, Compact = TouchLocalMapCompactVisible,
            PanFrames = _touchLocalMapPanFrames, ZoomFrames = _touchLocalMapZoomFrames, TurnFrames = _touchLocalMapTurnFrames };
    }
}
