using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly TouchOverlayChordPolicy _touchOverlayChord = new TouchOverlayChordPolicy();
        static readonly TouchRadialSettingsChordPolicy _touchRadialSettingsChord = new TouchRadialSettingsChordPolicy();
        static bool _overlayLeftGripPressed, _overlayRightGripPressed;
        internal static bool TouchOverlayChordCaptured => TouchInputOwned && _touchOverlayChord.Captured && !_touchRadialSettingsChord.Deferring;
        // Runner may already have the next OpenXR pose/button sample after the
        // game's once-per-frame edge snapshot. Veto its camera gesture without
        // advancing the chord clock or emitting button edges a second time.
        static bool TouchOverlayChordPressed(XrTouchFrame sample)
        {
            const uint controls = (uint)(XrTouchControl.Trigger | XrTouchControl.Squeeze);
            return (sample.left.activeControls & controls) == controls && (sample.right.activeControls & controls) == controls &&
                TouchOverlayChordPolicy.AllPressed(sample.left.trigger, sample.right.trigger, sample.left.squeeze, sample.right.squeeze);
        }

        // Runs before any navigation or game-button edge is exposed. Flat menus
        // use the same gate and do not depend on an attached world camera.
        static bool ProcessTouchOverlayChord()
        {
            var left = _touchSample.left; var right = _touchSample.right;
            const uint controls = (uint)(XrTouchControl.Trigger | XrTouchControl.Squeeze);
            bool valid = _touchSampleValid && LiveOverlayVr && (left.activeControls & controls) == controls &&
                (right.activeControls & controls) == controls && TouchPointerMath.Finite(left.trigger) &&
                TouchPointerMath.Finite(right.trigger) && TouchPointerMath.Finite(left.squeeze) && TouchPointerMath.Finite(right.squeeze);
            _overlayLeftGripPressed = TouchPointerMath.AnalogPressed(left.squeeze, _overlayLeftGripPressed);
            _overlayRightGripPressed = TouchPointerMath.AnalogPressed(right.squeeze, _overlayRightGripPressed);
            _touchRadialSettingsChord.Step(_touchRadial.Visible && _touchRadial.Side==0 && !TouchOverlayOpen,
                _touchRawLeftTrigger,_overlayLeftGripPressed,_touchRawRightTrigger,_overlayRightGripPressed,valid,
                _touchRadial.PreviewIndex,Time.unscaledTime);
            if(_touchRadialSettingsChord.Deferring && !_touchRawRightTrigger)
                _touchOverlayChord.CancelPendingWheelClick();
            bool previouslyCaptured = _touchOverlayChord.Captured;
            bool toggle = _touchOverlayChord.Step(_touchRawLeftTrigger, _touchRawRightTrigger,
                _overlayLeftGripPressed, _overlayRightGripPressed, valid, Time.unscaledTime);
            if ((_touchOverlayChord.Captured || previouslyCaptured) && !_touchRadialSettingsChord.Deferring)
            {
                // The final all-released sample is consumed too. No Up event,
                // selected menu action or old table gesture survives the chord.
                if (!previouslyCaptured || toggle) CancelTouchGameGestures();
                _touchPrimary.Cancel(); _touchSecondary.Cancel(); _touchOverlayTrigger.Cancel();
                _touchGameCancel.Cancel(); _touchGamePause.Cancel(); _touchAnyButton.Cancel();
                _touchGameAxesArmed = false; _touchClickFrozen = false;
                _touchNavVertical.Clear(); _touchNavHorizontal.Clear();
                _touchTabletop.Cancel(true);
            }
            if (toggle)
            {
                if (TouchOverlayOpen) HideLiveOverlay(); else _liveNavigation.Cycle();
                _liveMessage = null; _liveNextTextUpdate = 0;
                LogLiveOverlayInput("Touch: four buttons held for one second");
            }
            return _touchOverlayChord.Captured || previouslyCaptured || toggle;
        }

        static void ResetTouchOverlayChord()
        {
            _touchOverlayChord.Reset(); _overlayLeftGripPressed = _overlayRightGripPressed = false;
        }
    }
}
