using System;
using UnityEngine;
using HarmonyLib;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static long _touchSpaceCameraRevision = -1, _touchSpaceFocusCount;
        static bool _touchSpaceCameraActive, _touchSpaceFocusPending;
        static float _touchSpaceScale = 10, _touchSpaceBaseline = 10, _touchSpaceNextProbe;
        static string _touchSpaceCameraFault;
        static object _touchSpaceExplicitUnit;
        static bool _touchSpaceExplicitHead;
        internal static float TouchSpaceWorldScale => _touchSpaceScale;

        internal static void RequestTouchSpaceCombatFocus()
        {
            if (!InSpaceCombat) return;
            _touchSpaceFocusPending = true; _touchSpaceNextProbe = 0;
        }

        static void LeaveTouchSpaceCombatCamera()
        {
            if (!_touchSpaceCameraActive) return;
            LeaveTouchHeadView(); _touchSpaceExplicitUnit = null;
            _touchSpaceCameraActive = false; _touchSpaceFocusPending = false;
            _touchTabletop.ConfigureLimits(ComfortCameraOptions.ScaleMin, ComfortCameraOptions.ScaleMax,
                ComfortCameraOptions.TiltMin, ComfortCameraOptions.TiltMax);
            _touchTabletop.Reset(); _touchPoseReady = false; _touchHaveTrackingReference = false;
        }

        static void UpdateTouchSpaceCombatTabletop(XrFrame frame, Vector3 referencePosition, Quaternion referenceRotation,
            Camera source, out Vector3 gameAnchor, out Quaternion gameRotation)
        {
            gameAnchor = source == null ? Vector3.zero : source.transform.position;
            gameRotation = source == null ? Quaternion.identity : source.transform.rotation;
            if (source == null || source != _attachedCam || !TouchCameraOwned || _touchCameraFault) { _touchPoseReady = false; return; }
            try
            {
                bool entered = !_touchSpaceCameraActive || _touchSpaceCameraRevision != SpatialGameContextRevision;
                if (entered)
                {
                    // These are only mod-owned camera/input states. Never patch
                    // native unit-start-turn handlers: they also select units.
                    RestoreTouchCameraPreset(); ResetTouchCameraFocus(); StopTouchGroupMovement(); CancelTouchSelection();
                    _touchSpaceExplicitUnit=null;_touchSpaceExplicitHead=false;
                    _touchSpaceCameraActive = true; _touchSpaceCameraRevision = SpatialGameContextRevision;
                    _touchTabletop.Reset(); _touchSpaceFocusPending = true; _touchSpaceNextProbe = 0;
                    _touchHaveTrackingReference = false;
                }
                var touch = CurrentTouchFrame;
                bool available = frame.valid != 0 && frame.shouldRender != 0 && touch.ready != 0 && touch.focused != 0 &&
                    frame.serial != 0 && frame.serial == touch.serial && !PresentationTransition && !ObservedNativeLoading;
                if (_touchSpaceExplicitUnit != null && available && !TouchOverlayOpen && !TouchRadialCaptured)
                {
                    var unit = _touchSpaceExplicitUnit; _touchSpaceExplicitUnit = null;
                    if (_touchSpaceExplicitHead) EnterTouchHeadView(unit, frame, referencePosition, referenceRotation);
                    else FrameTouchSpaceUnit(unit, frame, referencePosition, referenceRotation);
                }
                if (_touchFirstPerson)
                {
                    UpdateTouchHeadView(frame, referencePosition, referenceRotation);
                    gameAnchor = TableVector(_touchTabletop.Pose.position); gameRotation = TableQuaternion(_touchTabletop.Pose.rotation);
                    _touchPoseReady = true; _touchTabletopSerial = frame.serial; return;
                }
                bool neutral = TouchGuideLoadPolicy.InputNeutral(touch.left.buttons, touch.right.buttons,
                    touch.left.trigger, touch.right.trigger, touch.left.squeeze, touch.right.squeeze,
                    touch.left.stickX, touch.left.stickY, touch.right.stickX, touch.right.stickY);
                if ((_touchSpaceFocusPending || !_touchTabletop.Initialized) && available && neutral &&
                    !TouchOverlayOpen && !TouchOverlayChordCaptured && !TouchRadialCaptured && !_touchUiPress.Captured &&
                    Time.unscaledTime >= _touchSpaceNextProbe)
                {
                    _touchSpaceNextProbe = Time.unscaledTime + .2f;
                    if (TryTouchSpaceAnchor(out Bounds bounds))
                    {
                        bool first = !_touchTabletop.Initialized;
                        float radius = Mathf.Max(.1f, Mathf.Max(bounds.extents.x, bounds.extents.z));
                        if (first)
                        {
                            _touchSpaceBaseline = TouchSpaceCombatCameraPolicy.Scale(radius, (source.transform.position - bounds.center).magnitude);
                            _touchSpaceScale = _touchSpaceBaseline;
                            _touchTabletop.ConfigureLimits(TouchSpaceCombatCameraPolicy.Near(_touchSpaceBaseline),
                                TouchSpaceCombatCameraPolicy.Far(_touchSpaceBaseline), TouchSpaceCombatCameraPolicy.MinimumTilt, TouchSpaceCombatCameraPolicy.MaximumTilt);
                        }
                        var spatial = _spatialGameContracts; var game = spatial.Game(); var player = spatial.Player(game);
                        var ownView = player == null ? null : spatial.View(spatial.PlayerShip(player)) as Component;
                        Quaternion visible = first && ownView != null ? ownView.transform.rotation :
                            TableQuaternion(_touchTabletop.Pose.rotation) * Quaternion.Inverse(referenceRotation) * frame.head.Rotation;
                        Vector3 heading = Vector3.ProjectOnPlane(visible * Vector3.forward, Vector3.up).normalized;
                        if (heading.sqrMagnitude < .5f) heading = Vector3.forward;
                        float pitch = TouchSpaceCombatCameraPolicy.Pitch * Mathf.Deg2Rad;
                        Vector3 forward = heading * Mathf.Cos(pitch) - Vector3.up * Mathf.Sin(pitch);
                        float distance = TouchSpaceCombatCameraPolicy.Distance(radius, _touchSpaceScale);
                        Vector3 desired = bounds.center - forward * distance;
                        var pose = SpacePlacement76(desired,forward,frame,referencePosition,referenceRotation,_touchSpaceScale);
                        _touchTabletop.Initialize(pose, _touchSpaceScale, TablePoint(bounds.center));
                        _touchSpaceFocusPending = false; ++_touchSpaceFocusCount;
                        _touchSpaceCameraFault = null;
                    }
                }
                if (!_touchTabletop.Initialized) { _touchPoseReady = false; return; }
                _touchTabletopSource = source;
                TickTouchTabletopControls(frame, referencePosition, referenceRotation);
                var savedTable=_touchTabletop.Capture();
                var levelPose=SpaceBoardLevelPolicy58.LevelInHeading76(savedTable.Pose,new ViewPose(TablePoint(referencePosition),TableRotation(referenceRotation)),TablePoint(frame.head.Position),savedTable.Scale,_spaceLevelHeading76);
                _touchTabletop.FollowPose(levelPose,_touchTabletop.MapPoint(savedTable.PivotReference));
                gameAnchor = TableVector(_touchTabletop.Pose.position); gameRotation = TableQuaternion(_touchTabletop.Pose.rotation);
                _touchPoseReady = true; _touchTabletopSerial = frame.serial;
            }
            catch (Exception error)
            {
                // Do not disable VR or alter a queued native order if optional
                // framing contracts disappear during an area unload.
                if (_touchSpaceCameraFault != error.Message) { _touchSpaceCameraFault = error.Message; _log.Error("[space/camera] " + error.Message); }
                _touchTabletop.Cancel(true); _touchPoseReady = false;
            }
        }

        static bool TryTouchSpaceAnchor(out Bounds bounds, object preferred = null)
        {
            bounds = default;
            var c = _spatialGameContracts; object game = c?.Game();
            if (game == null) return false;
            c.EnsureCamera(AccessTools.TypeByName);
            var player = c.Player(game);
            object unit = preferred ?? (player == null ? null : c.PlayerShip(player));
            var view = unit == null ? null : c.View(unit) as Component;
            if (view == null || !view.gameObject.activeInHierarchy)
            {
                unit = player == null ? null : c.PlayerShip(player);
                view = unit == null ? null : c.View(unit) as Component;
            }
            if (view == null || !view.gameObject.activeInHierarchy) return false;
            Vector3 origin = c.Position(unit);
            if (!TouchTabletopState.Finite(TablePoint(origin))) return false;
            bounds = new Bounds(origin, Vector3.one * 2f); bool found = false;
            // Bounded one-shot mesh query; particle beams and effects are not
            // ship extents. Never run this on each turn, each pointer or each eye.
            var renderers = view.GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < renderers.Length && i < 128; ++i)
            {
                var r = renderers[i]; if (!SpaceHullRenderer76(r)) continue;
                if (!r.enabled || !TouchTabletopState.Finite(TablePoint(r.bounds.center)) || !TouchTabletopState.Finite(TablePoint(r.bounds.extents))) continue;
                if (!found) { bounds = r.bounds; found = true; } else bounds.Encapsulate(r.bounds);
            }
            return true;
        }
        static void TouchSpaceUnitClicked(GameObject target,int button,bool simulate,bool accepted)
        {
            if(!accepted||button!=0||simulate||!_touchPrimary.Up||!TouchInputOwned||!TouchGameInputAllowed||_modeFlat||
                !InSpaceCombat||TouchOverlayOpen||TouchRadialCaptured||_touchOverUi||target==null||_touchGamePointer==null||
                _touchBoxPointerMode(_touchGamePointer)!=0||_touchCameraLastClickFrame==Time.frameCount)return;
            var view=target.GetComponentInParent(_touchBoxUnitViewType);var unit=view==null?null:_touchBoxViewEntity(view);
            if(unit==null||_touchCameraFocusContracts.Dead(unit))return;
            _touchCameraLastClickFrame=Time.frameCount;
            var action=_touchCameraFocus.Click(unit,Time.unscaledTime);
            if(!TouchDeepRelease&&action!=TouchCameraClick.Focus)return;
            _touchSpaceExplicitUnit=unit;_touchSpaceExplicitHead=TouchDeepRelease;
        }
        static void FrameTouchSpaceUnit(object unit,XrFrame frame,Vector3 referencePosition,Quaternion referenceRotation)
        {
            var c=_spatialGameContracts;c.EnsureCamera(AccessTools.TypeByName);
            var view=c.View(unit) as Component;
            if(view==null||!TryTouchSpaceAnchor(out Bounds bounds,unit))return;
            LeaveTouchHeadView();
            Vector3 heading=Vector3.ProjectOnPlane(view.transform.forward,Vector3.up).normalized;
            if(heading.sqrMagnitude<.5f)heading=Vector3.forward;
            float radius=Mathf.Max(bounds.extents.x,bounds.extents.z);
            float scale=Mathf.Clamp(radius*4.5f,TouchSpaceCombatCameraPolicy.Near(_touchSpaceBaseline),TouchSpaceCombatCameraPolicy.Far(_touchSpaceBaseline));
            float pitch=48*Mathf.Deg2Rad;
            Vector3 forward=heading*Mathf.Cos(pitch)-Vector3.up*Mathf.Sin(pitch);
            var desired=new ViewPose(TablePoint(bounds.center-forward*TouchSpaceCombatCameraPolicy.Distance(radius,scale)),TableRotation(Quaternion.LookRotation(forward,Vector3.up)));
            var pose=SpacePlacement76(TableVector(desired.position),forward,frame,referencePosition,referenceRotation,scale);
            _touchSpaceScale=scale;_touchTabletop.Initialize(pose,scale,TablePoint(bounds.center));_touchSpaceFocusPending=false;++_touchSpaceFocusCount;
        }
        internal static object SpaceCombatCameraSnapshot() => new {
            Active = _touchSpaceCameraActive && InSpaceCombat, Ready = _touchPoseReady,
            Scale = _touchSpaceScale, Baseline = _touchSpaceBaseline, InitialOrExplicitFrames = _touchSpaceFocusCount,
            Pending = _touchSpaceFocusPending, Error = _touchSpaceCameraFault,
            Policy = "Stable tactical table; ship-relative bounds; explicit focus; native pointer orders; no terrestrial movement or head view"
        };
    }
}


