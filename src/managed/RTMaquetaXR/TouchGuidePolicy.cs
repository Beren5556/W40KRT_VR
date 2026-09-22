using System;

namespace RTMaquetaXR
{
    internal sealed class TouchGuideLoadPolicy
    {
        internal bool Pending { get; private set; }
        internal bool AreaReady { get; private set; }
        internal int Requests { get; private set; }
        internal int Shown { get; private set; }
        double stableSince = -1;
        internal void Request() { ++Requests; Pending = Shown == 0; AreaReady = false; stableSince = -1; }
        internal void Acknowledge() { Cancel(); if (Shown == 0) Shown = 1; }
        internal void Loaded() { if (Pending) AreaReady = true; }
        internal void Cancel() { Pending = AreaReady = false; stableSince = -1; }
        internal static bool InputNeutral(uint leftButtons, uint rightButtons, float leftTrigger, float rightTrigger,
            float leftGrip, float rightGrip, float leftX, float leftY, float rightX, float rightY)
        {
            // Resting a thumb on a capacitive surface is neutral. Only actual
            // digital presses, squeezes/triggers and displaced sticks defer help.
            uint digital = ~TouchBindings.ThumbrestContact;
            return (leftButtons & digital) == 0 && (rightButtons & digital) == 0 &&
                leftTrigger < .35f && rightTrigger < .35f && leftGrip < .35f && rightGrip < .35f &&
                Math.Abs(leftX) < .2f && Math.Abs(leftY) < .2f && Math.Abs(rightX) < .2f && Math.Abs(rightY) < .2f;
        }
        internal bool Step(bool normalPlay, bool sceneReady, bool inputNeutral, bool overlayBusy, double now)
        {
            if (!Pending) return false;
            if (!AreaReady || !normalPlay || !sceneReady || !inputNeutral || overlayBusy || double.IsNaN(now) || double.IsInfinity(now))
            { stableSince = -1; return false; }
            if (stableSince < 0 || now < stableSince) { stableSince = now; return false; }
            if (now - stableSince < 1.25) return false;
            Cancel(); ++Shown; return true;
        }
    }
}
