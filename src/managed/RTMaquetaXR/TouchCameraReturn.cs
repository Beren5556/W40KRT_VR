using System;
using UnityEngine;

namespace RTMaquetaXR
{
    // This token owns only the view that existed before a temporary native UI.
    // It never searches the selection or performs a collision/framing pass.
    internal sealed class TouchCameraReturnPolicy
    {
        internal bool Interrupted { get; private set; }
        internal bool Captured { get; private set; }
        internal bool Pending { get; private set; }
        internal long Requests { get; private set; }
        internal long Attempts { get; private set; }
        internal bool Observe(bool interrupted, bool available)
        {
            bool capture = interrupted && !Interrupted && available;
            if (interrupted && !Interrupted)
            {
                Captured = capture; Pending = false;
                if (capture) ++Requests;
            }
            else if (!interrupted && Interrupted) Pending = Captured;
            Interrupted = interrupted;
            return capture;
        }
        internal void Request() { if (Captured && !Interrupted) Pending = true; }
        internal void Complete() { if (Pending) ++Attempts; Cancel(); }
        internal void Cancel() { Captured = Pending = false; }
        internal void Reset() { Cancel(); Interrupted = false; }
    }

    public static partial class Main
    {
        static readonly TouchCameraReturnPolicy _touchCameraReturn = new TouchCameraReturnPolicy();
        static TouchTabletopSnapshot _touchCameraReturnSnapshot;

        internal static void ObserveTouchCameraPresentation(bool interrupted, bool loadingOrMainMenu)
        {
            if (loadingOrMainMenu || _touchCameraLoad.Pending || !TouchInputOwned)
            { _touchCameraReturn.Reset(); return; }
            if (interrupted && _touchCameraReturn.Interrupted && _touchCameraReturn.Captured &&
                !CinematicWanted && (TouchGestureActive || _touchGroupMove.Moving))
                _touchCameraReturn.Cancel(); // Deliberate movement while a nonmodal panel is visible wins.
            bool available = _touchTabletop.Initialized && !_touchFirstPerson && !CinematicWanted;
            if (_touchCameraReturn.Observe(interrupted, available))
                _touchCameraReturnSnapshot = _touchTabletop.Capture();
        }

        // Kept as the shared native-window exit notification. The historical
        // name is not its policy: without an entry snapshot it does nothing.
        internal static void RequestTouchCameraGroupReturn()
        {
            if (!TouchInputOwned || _touchFirstPerson || _touchCameraLoad.Pending) return;
            _touchCameraReturn.Request();
        }

        static void UpdateTouchCameraGroupReturn(XrFrame frame, Vector3 referencePosition, Quaternion referenceRotation)
        {
            if (!_touchCameraReturn.Pending) return;
            if (_touchCameraLoad.Pending || _touchFirstPerson || !TouchInputOwned)
            { _touchCameraReturn.Cancel(); return; }
            var touch = CurrentTouchFrame;
            bool ready = _attached && !_modeFlat && (_modeName == "Default" || _modeName == "Pause") &&
                !PresentationTransition && !ObservedNativeLoading && frame.valid != 0 && frame.shouldRender != 0 &&
                touch.ready != 0 && touch.focused != 0 && touch.serial != 0 && touch.serial == frame.serial && Application.isFocused;
            bool blocked = CinematicWanted || _tutorialShowing || TouchOverlayOpen || TouchOverlayChordCaptured ||
                TouchRadialCaptured || _touchUiPress.Captured || TouchMenuWindowVisible;
            if (ready && !blocked && (TouchGestureActive || TouchSelectionCaptured || _touchCameraPendingGroup ||
                _touchCameraPendingUnit != null || _touchGroupMove.Moving ||
                (!_touchOverUi && (_touchPrimary.Down || _touchSecondary.Down))))
            { _touchCameraReturn.Cancel(); return; }
            bool neutral = TouchGuideLoadPolicy.InputNeutral(touch.left.buttons, touch.right.buttons,
                touch.left.trigger, touch.right.trigger, touch.left.squeeze, touch.right.squeeze,
                touch.left.stickX, touch.left.stickY, touch.right.stickX, touch.right.stickY);
            if (!ready || blocked || !neutral) return;
            try
            {
                if (TouchCameraLoadMenuBlocked()) return;
                _touchCameraReturn.Complete(); // Consume before restore; failures cannot retry every frame.
                _touchTabletop.Restore(_touchCameraReturnSnapshot);
                // A deliberate scale option changed while the window was open
                // remains authoritative; we restore the pose, never settings.
                if (Mathf.Abs(_touchTabletop.Scale - TouchWorldScale) > .00001f)
                    _touchTabletop.SetScale(TouchWorldScale);
                _touchFollowReady = _touchFollowWasMoving = false;
                _touchThirdPersonFollower.Reset();
            }
            catch (Exception error) { _touchCameraReturn.Cancel(); TouchCameraFocusError(error); }
        }
    }
}
