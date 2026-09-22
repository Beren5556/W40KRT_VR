namespace RTMaquetaXR
{
    // The right grip owns the lifetime of a LEFT-wheel native card. Returning
    // the stick/ray to neutral keeps that card fixed until the grip is released.
    internal sealed class TouchRadialInfoGripPolicy
    {
        internal int Index { get; private set; } = -1;
        int candidate = -1;
        float candidateSince;
        // Native actor inspection can take tens/hundreds of milliseconds. A
        // transient ray crossing must not construct a fresh native window.
        // The first card opens immediately; intentional replacements settle
        // for only 120 ms. This never delays release or action confirmation.
        internal int StepStable(bool visible, bool leftWheel, bool grip, int pointed, int count, float now)
        {
            if (!visible || !leftWheel || !grip || float.IsNaN(now) || float.IsInfinity(now)) { Reset(); return -1; }
            if (Index < 0 || Index >= count) return Step(visible,leftWheel,grip,pointed,count);
            if (pointed < 0 || pointed >= count || pointed == Index) { candidate=-1; return Index; }
            if (candidate != pointed || now < candidateSince) { candidate=pointed; candidateSince=now; }
            if (now-candidateSince < .12f) return Index;
            candidate=-1; return Step(visible,leftWheel,grip,pointed,count);
        }
        internal int Step(bool visible, bool leftWheel, bool grip, int pointed, int count)
        {
            if (!visible || !leftWheel || !grip) { Reset(); return -1; }
            if (pointed >= 0 && pointed < count) Index = pointed;
            if (Index >= count) Index = -1;
            return Index;
        }
        internal void Reset() { Index = candidate = -1; candidateSince=0; }
    }
}
