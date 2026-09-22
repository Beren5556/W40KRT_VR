using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Harmony _touchNavigationMapHarmony;
        static readonly NavigationMapInputEpoch _touchNavigationMapEpoch = new NavigationMapInputEpoch();
        static long _touchNavigationMapPointerFrames, _touchNavigationMapZoomSamples;
        internal static bool TouchNavigationMapPointerAllowed
        {
            get
            {
                if (!InNavigationMap) return false;
                return NavigationMapPolicy.Interactive(SpatialGameMode, true, _modeFlat, _touchHasPoint,
                    TouchGameInputAllowed, NativeUiOnlyPresentation(), TouchMenuWindowVisible, TouchMenuBlockingModal(), NativeTutorialInputBlocked);
            }
        }
        // Called before stepping button latches in EnsureTouchSample. Entering
        // another map/area, a scripted trip or a modal must never carry an old
        // press or held scroll into its new native owner.
        internal static void ObserveTouchNavigationMapInput()
        {
            if (!_touchNavigationMapEpoch.Observe(InNavigationMap, SpatialGameContextRevision, SpatialGameMode)) return;
            CancelTouchGameGestures();
            _touchPrimary.Cancel(); _touchSecondary.Cancel(); _touchA.Cancel(); _touchB.Cancel();
            _touchX.Cancel(); _touchY.Cancel(); _touchMenu.Cancel();
            _touchGameCancel.Cancel(); _touchGamePause.Cancel(); _touchAnyButton.Cancel();
            _touchGameAxesArmed = false; _touchRayReady = _touchHasPoint = _touchOverUi = _touchClickFrozen = false;
            _navigationSticksArmed = _navigationZoomArmed = false;
        }

        // The original PointerController owns object/ray/UI priority and route
        // eligibility. It receives screen coordinates obtained from the actual
        // submitted quad, then casts from the native map camera.
        static bool AllowTouchNavigationMapPointer()
        {
            if (!TouchNavigationMapPointerAllowed) return false;
            ++_touchNavigationMapPointerFrames; return true;
        }

        static void InstallTouchNavigationMaps()
        {
            if (_touchNavigationMapHarmony != null) return;
            try
            {
                var zoom = AccessTools.TypeByName("Kingmaker.View.CameraZoom");
                var tick = AccessTools.Method(zoom, "TickZoom", Type.EmptyTypes);
                if (tick == null || tick.ReturnType != typeof(void)) throw new MissingMethodException("CameraZoom.TickZoom");
                var rig = AccessTools.TypeByName("Kingmaker.View.CameraRig");
                var edge = AccessTools.Method(rig, "GetCameraScrollShiftByMouse", Type.EmptyTypes);
                if (edge == null || edge.ReturnType != typeof(Vector2)) throw new MissingMethodException("CameraRig.GetCameraScrollShiftByMouse");
                _touchNavigationMapHarmony = new Harmony("RTMaquetaXR.TouchNavigationMaps");
                _touchNavigationMapHarmony.Patch(tick,
                    transpiler: new HarmonyMethod(typeof(Main), nameof(TouchNavigationMapZoomTranspiler)));
                _touchNavigationMapHarmony.Patch(edge,
                    prefix: new HarmonyMethod(typeof(Main), nameof(TouchNavigationMapEdgePan)));
                InstallNavigationCameraControls();
                _log.Log("[touch/navigation] Maps retain native PC click, double-click, drag and route checks; right-stick scroll feeds native galaxy zoom when its camera permits it.");
            }
            catch (Exception error)
            {
                StopTouchNavigationMaps();
                _log.Error("[touch/navigation] Native zoom adapter unavailable; map panel and native UI retained: " + error.Message);
            }
        }
        static IEnumerable<CodeInstruction> TouchNavigationMapZoomTranspiler(IEnumerable<CodeInstruction> source)
        {
            var axis = AccessTools.Method(typeof(Input), nameof(Input.GetAxis), new[] { typeof(string) });
            var replacement = AccessTools.Method(typeof(Main), nameof(TouchNavigationMapAxis));
            int count = 0;
            foreach (var instruction in source)
            {
                if (instruction.Calls(axis)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; ++count; }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Native map zoom Input.GetAxis route changed: " + count);
        }
        static float TouchNavigationMapAxis(string axis)
        {
            if (!TouchInputOwned || !InNavigationMap) return Input.GetAxis(axis);
            if (axis != "Mouse ScrollWheel") return 0;
            EnsureTouchSample();
            float sample = 0; // Left-stick zoom is injected before the native UI/edge gate.
            if (sample != 0) ++_touchNavigationMapZoomSamples;
            return sample;
        }
        static bool TouchNavigationMapEdgePan(ref Vector2 __result)
        {
            if (!TouchInputOwned || !InNavigationMap) return true;
            EnsureTouchSample();
            // The off-panel sentinel must not become the game's "scroll even
            // outside the screen" command. A visible native widget also owns
            // its edge, rather than moving the map beneath that widget.
            var events = EventSystem.current;
            if (TouchNavigationMapPointerAllowed && (events == null || !events.IsPointerOverGameObject())) return true;
            __result = Vector2.zero; return false;
        }
        static void StopTouchNavigationMaps()
        {
            if (!_appQuitting) _touchNavigationMapHarmony?.UnpatchAll(_touchNavigationMapHarmony.Id);
            _touchNavigationMapHarmony = null;
            _touchNavigationMapEpoch.Clear();
        }
        internal static object TouchNavigationMapSnapshot() => new {
            Galactic = InGalacticMap, System = InStarSystemMap, NativeZoomAdapter = _touchNavigationMapHarmony != null,
            PointerFrames = _touchNavigationMapPointerFrames, ZoomSamples = _touchNavigationMapZoomSamples,
            Presentation = "Native PC map in independent VR panel", SceneRay = "Native camera from panel UV"
        };
    }
}
