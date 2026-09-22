namespace RTMaquetaXR
{
    // One scripted sequence owns the initial approach. The first deliberate
    // tabletop gesture relinquishes that ownership until the sequence ends.
    internal sealed class CinematicControlPolicy
    {
        internal bool Manual { get; private set; }
        bool armed;
        internal void Reset() { Manual = armed = false; }
        internal bool Observe(bool allowed, bool gesture)
        {
            if (!allowed) { armed = false; return false; }
            if (Manual) return false;
            if (!gesture) { armed = true; return false; }
            if (!armed) return false;
            Manual = true; return true;
        }
    }
}
