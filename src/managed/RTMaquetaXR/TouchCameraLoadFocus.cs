using System;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly TouchCameraLoadPolicy _touchCameraLoad = new TouchCameraLoadPolicy();
        static float _touchCameraLoadNextProbe;
        static bool _touchCameraAreaResetPending;
        static void TouchCameraLoadRequested()
        { _touchCameraLoad.Request(); _touchCameraLoadNextProbe = 0; _touchCameraAreaResetPending = true; }
        // Exact native area activation, including switching to an already
        // streamed area. Unity UI scene loads never reach this game callback.
        static void TouchCameraNativeAreaActivated()
        {
            if (!_active) return;
            TouchSpatialAreaActivated();
            _touchCameraAreaResetPending = true;
            if (!_touchCameraLoad.Pending) _touchCameraLoad.Request();
            _touchCameraLoadNextProbe = 0;
        }
        static Exception TouchCameraAreaActivationFinished(Exception __exception)
        { TouchCameraLoadFinished(__exception); return __exception; }
        internal static bool ConsumeTouchCameraAreaReset()
        {
            if (!_touchCameraAreaResetPending) return false;
            _touchCameraAreaResetPending = false; return true;
        }
        static void TouchCameraLoadFinished(Exception error)
        { if (error != null) { _touchCameraLoad.Cancel(); _touchCameraAreaResetPending = false; } }
        static void TouchCameraLoadAreaReady() { _touchCameraLoad.Loaded(); }
        static void CancelTouchCameraLoadFocus() { _touchCameraLoad.Cancel(); _touchCameraAreaResetPending = false; }

        static void ObserveTouchCameraLoadInput(XrFrame frame)
        {
            if (InSpaceCombat || InNavigationMap) { _touchCameraLoad.Cancel(); return; }
            if (!_touchCameraLoad.Pending) return;
            var touch = CurrentTouchFrame;
            bool available = _attached && !_modeFlat && !PresentationTransition &&
                (_modeName == "Default" || CinematicWanted) && !_tutorialShowing &&
                TouchInputOwned && touch.ready != 0 && touch.focused != 0 && frame.valid != 0 &&
                frame.shouldRender != 0 && touch.serial != 0 && touch.serial == frame.serial && Application.isFocused &&
                !TouchOverlayOpen && !TouchOverlayChordCaptured && !TouchOverlayChordPressed(touch);
            // A trigger already held to dismiss loading is not a new click.
            // Grips/sticks or a new world click mean the user has taken control.
            bool intent = TouchGestureActive || TouchSelectionCaptured || _touchFirstPerson ||
                _touchCameraPendingGroup || _touchCameraPendingUnit != null ||
                touch.left.squeeze > .35f || touch.right.squeeze > .35f ||
                Mathf.Abs(touch.left.stickX) > .2f || Mathf.Abs(touch.left.stickY) > .2f ||
                Mathf.Abs(touch.right.stickX) > .2f || Mathf.Abs(touch.right.stickY) > .2f ||
                (!_touchOverUi && (_touchPrimary.Down || _touchSecondary.Down));
            if (available && intent && !CinematicWanted) available = !TouchCameraLoadMenuBlocked();
            _touchCameraLoad.UserIntent(available, intent);
            if (CinematicWanted) _touchCameraLoad.Defer();
        }

        static bool TouchCameraLoadMenuBlocked()
        {
            // The native menu can own input while GameMode remains Default.
            // Reuse the same bound UI-owner contract as group movement.
            try
            {
                var contracts = _touchGroupContracts;
                var game = contracts == null ? null : contracts.Game();
                return game == null || contracts.UiBlocked(game);
            }
            catch { return true; }
        }

        static void UpdateTouchCameraLoadFocus(XrFrame frame, Vector3 referencePosition, Quaternion referenceRotation)
        {
            if (InSpaceCombat || InNavigationMap) { _touchCameraLoad.Cancel(); return; }
            if (!_touchCameraLoad.Pending) return;
            var touch = CurrentTouchFrame;
            bool ready = _touchTabletop.Initialized && _attached && !_modeFlat && _modeName == "Default" &&
                !PresentationTransition && !ObservedNativeLoading && TouchInputOwned &&
                frame.valid != 0 && frame.shouldRender != 0 && touch.ready != 0 && touch.focused != 0 &&
                touch.serial != 0 && touch.serial == frame.serial && Application.isFocused;
            bool blocked = CinematicWanted || _tutorialShowing || TouchOverlayOpen || TouchOverlayChordCaptured ||
                _touchUiPress.Captured || _touchFirstPerson || TouchGestureActive || TouchSelectionCaptured ||
                _touchCameraPendingGroup || _touchCameraPendingUnit != null;
            bool neutral = TouchGuideLoadPolicy.InputNeutral(touch.left.buttons, touch.right.buttons,
                touch.left.trigger, touch.right.trigger, touch.left.squeeze, touch.right.squeeze,
                touch.left.stickX, touch.left.stickY, touch.right.stickX, touch.right.stickY);
            if (!ready || blocked || !neutral)
            { _touchCameraLoad.Step(ready, neutral, blocked, null, 0, Time.unscaledTime); return; }
            if (Time.unscaledTime < _touchCameraLoadNextProbe) return;
            _touchCameraLoadNextProbe = Time.unscaledTime + .1f;
            try
            {
                if (TouchCameraLoadMenuBlocked())
                { _touchCameraLoad.Defer(); return; }
                object leader = TouchCameraLoadPartyLeader(out int count);
                if (!_touchCameraLoad.Step(true, true, false, leader, count, Time.unscaledTime)) return;
                // Consume before the one bounded framing attempt. Obstruction
                // or an exception cannot keep retrying and steal a later view.
                FocusTouchLoadedParty(leader, frame, referencePosition, referenceRotation);
                _log.Log("[touch/camera] Post-load group framing attempted once after request " + _touchCameraLoad.Requests);
            }
            catch (Exception error) { _touchCameraLoad.Cancel(); TouchCameraFocusError(error); }
        }
    }
}

