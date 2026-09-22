using System;

namespace RTMaquetaXR
{
    // Four physical buttons, one continuous hold and one toggle. A broken hold,
    // focus loss or missing sample cannot manufacture a release or rearm it.
    internal sealed class TouchOverlayChordPolicy
    {
        internal const double HoldSeconds = 1;
        internal const double MaximumSampleGap = .3;
        // Conservative pose-only veto: the next camera sample can already form
        // a chord with controls maintained inside the .45-.65 hysteresis band.
        // This does not qualify a fresh press or start the one-second timer.
        internal static bool AllPressed(float leftTrigger, float rightTrigger, float leftGrip, float rightGrip) =>
            Finite(leftTrigger) && Finite(rightTrigger) && Finite(leftGrip) && Finite(rightGrip) &&
            leftTrigger >= .45f && rightTrigger >= .45f && leftGrip >= .45f && rightGrip >= .45f;
        internal bool Captured { get; private set; }
        internal double Progress { get; private set; }
        bool armed, holding;
        double started, lastTime = double.NaN;

        internal bool Step(bool leftTrigger, bool rightTrigger, bool leftGrip, bool rightGrip, bool valid, double now)
        {
            bool released = !leftTrigger && !rightTrigger && !leftGrip && !rightGrip;
            bool all = leftTrigger && rightTrigger && leftGrip && rightGrip;
            bool continuous = Finite(now) && (!Finite(lastTime) || (now >= lastTime && now - lastTime <= MaximumSampleGap));
            lastTime = Finite(now) ? now : double.NaN;
            if (!valid || !continuous) { RequireRelease(); return false; }
            if (!armed)
            {
                Captured = !released; Progress = 0;
                if (released) armed = true;
                return false;
            }
            if (!all)
            {
                if (holding || Captured)
                {
                    RequireRelease();
                    if (released) { armed = true; Captured = false; }
                }
                return false;
            }
            Captured = true;
            if (!holding) { started = now; holding = true; }
            Progress = Math.Max(0, Math.Min(1, (now - started) / HoldSeconds));
            if (now - started < HoldSeconds) return false;
            RequireRelease();
            return true;
        }

        void RequireRelease() { armed = holding = false; Captured = true; Progress = 0; }
        // Only the lower-priority wheel's deferred short click calls this,
        // after the opposite trigger is released. A completed toggle cannot.
        internal void CancelPendingWheelClick() { armed=true;holding=Captured=false;Progress=0; }
        internal void Reset() { armed = holding = Captured = false; Progress = 0; lastTime = double.NaN; }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
