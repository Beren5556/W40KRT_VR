using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly TouchDrawDistancePolicy _touchDistance = new TouchDrawDistancePolicy();
        static float _touchDistanceFeedbackUntil, _touchDistanceNextApply;
        static bool _touchDistancePending;
        static string _touchDistanceFeedback;
        static int _touchDistanceFeedbackLanguage = -1;
        static float _touchDistanceFeedbackValue;
        internal static bool TouchDistanceReserved => _cfg.touchDrawDistanceShortcut && _touchDistance.Reserved;
        internal static string TouchDistanceFeedback
        {
            get
            {
                if (Time.unscaledTime > _touchDistanceFeedbackUntil || _touchDistanceFeedback == null) return null;
                if (_touchDistanceFeedbackLanguage != ModLocalization.Revision)
                {
                    _touchDistanceFeedback = ModLocalization.Format("Draw distance: {0} units", _touchDistanceFeedbackValue.ToString("0", ModLocalization.Culture));
                    _touchDistanceFeedbackLanguage = ModLocalization.Revision;
                }
                return _touchDistanceFeedback;
            }
        }
        static bool TouchDistanceContact(XrTouchFrame sample) =>
            (sample.left.activeControls & TouchBindings.ThumbrestControl) != 0 &&
            (sample.left.buttons & TouchBindings.ThumbrestContact) != 0;
        internal static bool TouchDistanceAdjustmentRequested(XrTouchFrame sample) =>
            !InSpaceCombat && !InNavigationMap && _cfg.touchDrawDistanceShortcut && !TouchLocalMapOwnsAxes && sample.ready != 0 && sample.focused != 0 && !TouchOverlayOpen &&
            TouchDistanceContact(sample) && (sample.right.activeControls & TouchBindings.StickControl) != 0 &&
            TouchPointerMath.Finite(sample.right.stickY) && Mathf.Abs(sample.right.stickY) > TouchBindings.DistanceDeadZone;

        static void ProcessTouchDrawDistance()
        {
            if (InSpaceCombat || InNavigationMap || !_cfg.touchDrawDistanceShortcut) { ResetTouchDrawDistance(); return; }
            bool available = _touchSampleValid && _active && _attached && !_modeFlat && !TouchOverlayOpen && !TouchLocalMapOwnsAxes &&
                !TouchOverlayChordCaptured && !TouchRadialCaptured && !_drawDistanceTrial.Running && !_drawDistanceStartGate.Armed &&
                !_touchRawRightTrigger && !_touchRawLeftTrigger && !TouchSelectionCaptured &&
                (_touchSample.right.activeControls & TouchBindings.StickControl) != 0;
            bool wasReserved = _touchDistance.Reserved;
            int mode = RequestedDrawDistance;
            float current = mode == 1 ? 100 : mode == 2 ? 60 : mode == 3 ? _cfg.drawDistanceCustom :
                _drawDistanceApplied ? _drawDistanceEffectiveFar : 100;
            bool changed = _touchDistance.Step(available, TouchDistanceContact(_touchSample),
                _touchSample.right.stickX, _touchSample.right.stickY, Time.unscaledDeltaTime, current);
            _touchDistancePending |= changed;
            if (_touchDistance.Reserved)
            {
                _touchGameAxesArmed = false;
                if (!wasReserved) StopTouchGroupMovement();
            }
            if (available && _touchDistancePending && (Time.unscaledTime >= _touchDistanceNextApply || !_touchDistance.Adjusting))
            {
                SetCustomDrawDistance(Mathf.Round(_touchDistance.Value), "touch-shortcut");
                _touchDistanceNextApply = Time.unscaledTime + .1f; _touchDistancePending=false;
            }
            if (!available) _touchDistancePending=false;
            if (_touchDistance.Adjusting)
            {
                _touchDistanceFeedbackValue = Mathf.Round(_touchDistance.Value);
                _touchDistanceFeedback = ModLocalization.Format("Draw distance: {0} units", _touchDistanceFeedbackValue.ToString("0", ModLocalization.Culture));
                _touchDistanceFeedbackLanguage = ModLocalization.Revision;
                _touchDistanceFeedbackUntil = Time.unscaledTime + 2f;
            }
        }
        static void ResetTouchDrawDistance()
        { _touchDistance.Reset(); _touchDistanceFeedbackUntil=_touchDistanceNextApply=0; _touchDistanceFeedback=null; _touchDistancePending=false; }
        static void SetTouchDrawDistanceShortcut(bool enabled)
        {
            _cfg.touchDrawDistanceShortcut = enabled;
            ResetTouchDrawDistance();
            _touchGameAxesArmed = false;
            MarkSettingsDirty();
            _log.Log("[distance] Touch shortcut " + (enabled ? "enabled" : "disabled") + "; range unchanged.");
        }
    }
}
