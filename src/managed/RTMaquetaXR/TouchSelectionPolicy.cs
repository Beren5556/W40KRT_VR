namespace RTMaquetaXR
{
    // The installed game issues its ordinary click on release, not on down.
    // Keep that press until a deliberate eligible world drag claims it. There is
    // no delayed synthetic click: cancellation clears the original press.
    internal sealed class TouchSelectionPolicy
    {
        internal bool Captured { get; private set; }
        internal bool Pending { get; private set; }
        internal bool Active { get; private set; }
        internal bool Begin { get; private set; }
        internal bool Complete { get; private set; }
        internal bool Cancelled { get; private set; }
        internal bool ClickReleased { get; private set; }
        internal float AnchorX { get; private set; }
        internal float AnchorY { get; private set; }
        float dragThreshold, pressTime;
        bool requireRelease = true;
        bool previousTrigger, triggerHeld, freshPress;

        internal void Step(bool trigger, bool allowed)
        {
            Begin = Complete = Cancelled = ClickReleased = freshPress = false;
            Captured = Active;
            triggerHeld = trigger;
            if (!allowed)
            {
                Cancel();
                previousTrigger = trigger; return;
            }
            if (requireRelease)
            {
                if (!trigger) requireRelease = false;
                previousTrigger = trigger; return;
            }
            freshPress = trigger && !previousTrigger;
            previousTrigger = trigger;
        }

        internal bool StartCandidate(float x, float y, float threshold, float now)
        {
            if (!freshPress || Pending || Captured || !Finite(x) || !Finite(y) || !Finite(threshold) || threshold <= 0 || !Finite(now)) return false;
            AnchorX = x; AnchorY = y; Pending = true; freshPress = false;
            dragThreshold = threshold; pressTime = now;
            return true;
        }

        internal void UpdatePointer(float x, float y, float now, float aimDistance)
        {
            if (!Pending && !Active) return;
            if (!Finite(x) || !Finite(y) || !Finite(now) || now < pressTime || !Finite(aimDistance) || aimDistance < 0) { Cancel(); return; }
            if (Pending)
            {
                // Intent comes from the controller ray cone, not its magnified
                // intersection with a shallow floor. A stationary hand never
                // turns into a drag merely because a hold crossed 100 ms.
                if (aimDistance >= dragThreshold)
                {
                    Pending = false; Active = Begin = Captured = true;
                }
                else if (!triggerHeld) { Pending = false; ClickReleased = true; }
            }
            if (Active && !triggerHeld) { Active = false; Complete = Captured = true; }
        }

        internal void Cancel()
        {
            Captured |= Pending || Active;
            Cancelled |= Pending || Active;
            Pending = Active = Begin = Complete = ClickReleased = freshPress = false;
            requireRelease = true;
        }

        internal void Reset()
        {
            Captured = Pending = Active = Begin = Complete = Cancelled = ClickReleased = previousTrigger = triggerHeld = freshPress = false;
            requireRelease = true;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
