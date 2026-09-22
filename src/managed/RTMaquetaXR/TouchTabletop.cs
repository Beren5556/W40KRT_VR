using System;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly TouchTabletopState _touchTabletop = new TouchTabletopState();
        static bool _touchPoseReady, _touchHaveTrackingReference;
        static Vector3 _touchTrackingReferencePosition;
        static Quaternion _touchTrackingReferenceRotation;
        static Camera _touchTabletopSource;
        static ulong _touchTabletopSerial;
        internal static string TouchGestureName => TouchCameraFirstPersonActive ? "Head view · bring held grips together to zoom out" :
            _touchTabletop.Gesture == TouchTabletopGesture.PanLeft ? "Move table · left grip" :
            _touchTabletop.Gesture == TouchTabletopGesture.PanRight ? "Move table · right grip" :
            _touchTabletop.Gesture == TouchTabletopGesture.RotateAndScale ? "Rotate / scale · both grips" :
            _touchTabletop.Gesture == TouchTabletopGesture.Tilt ? "Tilt · left stick" :
            _touchTabletop.Gesture == TouchTabletopGesture.Turn ? "Rotate · left stick" :
            _touchTabletop.Gesture == TouchTabletopGesture.WaitingForRelease ? "Release grips and centre left stick" : "Idle";
        internal static bool TouchGestureLimited => _touchTabletop.Limited;
        internal static bool TouchGestureActive => _touchTabletop.Manipulating;
        internal static bool TouchTabletopPoseReady => _touchPoseReady && !_touchCameraFault;

        internal static void UpdateTouchTabletop(XrFrame frame, Vector3 referencePosition, Quaternion referenceRotation,
            Camera source, out Vector3 gameAnchor, out Quaternion gameRotation)
        {
            gameAnchor = source != null ? source.transform.position : Vector3.zero;
            gameRotation = source != null ? source.transform.rotation : Quaternion.identity;
            if (InSpaceCombat) { UpdateTouchSpaceCombatTabletop(frame, referencePosition, referenceRotation, source, out gameAnchor, out gameRotation); return; }
            LeaveTouchSpaceCombatCamera();
            ObserveTouchCameraLoadInput(frame);
            if (CinematicWanted)
            {
                UpdateCinematicTabletop(frame, referencePosition, referenceRotation, source, out gameAnchor, out gameRotation);
                return;
            }
            if (!PrepareTouchCameraPreset(source, out Vector3 pivot)) { _touchPoseReady = false; return; }
            try
            {
                if (!_touchTabletop.Initialized)
                {
                    _touchTabletop.Initialize(new ViewPose(TablePoint(source.transform.position), TableRotation(source.transform.rotation)), TouchWorldScale, TablePoint(pivot));
                }
                if (source != _touchTabletopSource)
                {
                    _touchTabletopSource = source;
                    _touchHaveTrackingReference = false;
                }
                if (!TouchCameraFirstPersonActive)
                {
                    TickTouchTabletopControls(frame, referencePosition, referenceRotation);
                    UpdateCombatFocus(source, frame, referencePosition, referenceRotation, CurrentTouchFrame);
                }
                UpdateTouchCameraFocus(frame, referencePosition, referenceRotation, source);
                gameAnchor = TableVector(_touchTabletop.Pose.position);
                gameRotation = TableQuaternion(_touchTabletop.Pose.rotation);
                _touchPoseReady = true; _touchTabletopSerial = frame.serial;
            }
            catch (Exception error) { TouchCameraFailure(error); }
        }

        static bool TouchTabletopControlsAllowed(XrFrame frame, XrTouchFrame touch) =>
            TouchInputOwned && !InNavigationMap && !TouchLocalMapOwnsAxes && touch.ready != 0 && touch.focused != 0 && Application.isFocused &&
            frame.valid != 0 && frame.shouldRender != 0 && touch.serial == frame.serial && touch.serial != 0 && !TouchOverlayOpen &&
            !TouchOverlayChordCaptured && !TouchOverlayChordPressed(touch) && !TouchRadialCaptured &&
            !TouchRadialChordPressed(touch) && !TouchDistanceAdjustmentRequested(touch);

        static bool TouchStickCameraRequested(XrTouchFrame touch) => !TouchLocalMapOwnsAxes && !TouchCameraFirstPersonActive && !_touchUiPress.Captured &&
            !TouchSelectionSuppressTilt(touch) && (touch.left.activeControls & (uint)XrTouchControl.Stick) != 0 &&
            (Mathf.Abs(touch.left.stickX) > .2f || Mathf.Abs(touch.left.stickY) > .2f);

        // Gameplay and manually positioned cinematics execute exactly the same
        // physical gesture mapping, sensitivities, scale limits and focus gates.
        static void TickTouchTabletopControls(XrFrame frame, Vector3 referencePosition, Quaternion referenceRotation)
        {
            if (!_touchHaveTrackingReference || referencePosition != _touchTrackingReferencePosition ||
                Quaternion.Angle(referenceRotation, _touchTrackingReferenceRotation) > .001f)
            {
                _touchTrackingReferencePosition = referencePosition; _touchTrackingReferenceRotation = referenceRotation;
                _touchHaveTrackingReference = true; _touchTabletop.Cancel(true);
            }
            bool spatial = InSpaceCombat;
            if (!spatial) _touchTabletop.ConfigureLimits(_cfg.limitExplorationZoom ? 6 : .1f, _cfg.limitExplorationZoom ? 14 : 10000,
                ComfortCameraOptions.TiltMin, ComfortCameraOptions.TiltMax);
            float scale = spatial ? _touchSpaceScale : TouchWorldScale;
            if (Mathf.Abs(scale - _touchTabletop.Scale) > .00001f) _touchTabletop.SetScale(scale);
            XrTouchFrame touch = CurrentTouchFrame;
            var inverse = Quaternion.Inverse(referenceRotation);
            // OpenXR may retain a valid estimated pose outside the tracking cameras.
            // A missing TRACKED bit alone is not loss of a valid runtime pose.
            bool leftValid = touch.left.GripValid && (touch.left.activeControls & (uint)XrTouchControl.Squeeze) != 0;
            bool rightValid = touch.right.GripValid && (touch.right.activeControls & (uint)XrTouchControl.Squeeze) != 0;
            _touchTabletop.Tick(new TouchTabletopInput {
                Allowed = TouchTabletopControlsAllowed(frame, touch), LeftValid = leftValid, RightValid = rightValid,
                Left = TablePoint(inverse * (touch.left.grip.Position - referencePosition)),
                Right = TablePoint(inverse * (touch.right.grip.Position - referencePosition)),
                PhysicalUpReference = TablePoint(inverse * Vector3.up),
                LeftSqueeze = touch.left.squeeze, RightSqueeze = touch.right.squeeze,
                StickBlocked = TouchSelectionSuppressTilt(touch) || _touchUiPress.Captured,
                TiltAxis = (touch.left.activeControls & (uint)XrTouchControl.Stick) != 0 ? touch.left.stickY : 0,
                TurnAxis = (touch.left.activeControls & (uint)XrTouchControl.Stick) != 0 ? touch.left.stickX : 0,
                Seconds = Time.unscaledDeltaTime
            }, TouchTurnSpeed, TouchZoomSpeed, TouchMoveSpeed, TouchGestureTurnSpeed);
            if (spatial) _touchSpaceScale = _touchTabletop.Scale;
            else if (_cfg.worldScale != _touchTabletop.Scale)
            {
                _cfg.worldScale = _touchTabletop.Scale;
                MarkSettingsDirty();
            }
        }

        internal static bool TryTouchWorldRay(bool left, out Ray ray)
        {
            ray = default(Ray);
            XrTouchFrame touch = CurrentTouchFrame;
            if (touch.ready == 0 || touch.focused == 0 || touch.serial == 0 || !Application.isFocused) return false;
            XrTouchHand hand = left ? touch.left : touch.right;
            if (!hand.AimValid) return false;
            if (_modeFlat) { ray = new Ray(hand.aim.Position * WorldScale, hand.aim.Rotation * Vector3.forward); return true; }
            if (!_touchPoseReady || !_touchHaveTrackingReference || !TouchCameraOwned || !_touchTabletop.Initialized) return false;
            var inverse = Quaternion.Inverse(_touchTrackingReferenceRotation);
            Point3 point = TablePoint(inverse * (hand.aim.Position - _touchTrackingReferencePosition));
            Point3 direction = TablePoint(inverse * (hand.aim.Rotation * Vector3.forward));
            if (!TouchTabletopState.Finite(point) || !TouchTabletopState.Finite(direction)) return false;
            Vector3 worldDirection = TableVector(_touchTabletop.MapDirection(direction));
            if (worldDirection.sqrMagnitude < .000001f) return false;
            ray = new Ray(TableVector(_touchTabletop.MapPoint(point)), worldDirection.normalized);
            return true;
        }
        static Point3 TablePoint(Vector3 value) => new Point3(value.x, value.y, value.z);
        static Vector3 TableVector(Point3 value) => new Vector3(value.x, value.y, value.z);
        static Rotation4 TableRotation(Quaternion value) => new Rotation4(value.x, value.y, value.z, value.w);
        static Quaternion TableQuaternion(Rotation4 value) => new Quaternion(value.x, value.y, value.z, value.w);
    }
}

