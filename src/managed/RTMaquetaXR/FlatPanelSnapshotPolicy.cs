namespace RTMaquetaXR
{
    // A busy snapshot copy may reuse a recently observed panel. An invalid
    // panel, a session boundary or a newer recenter/stereo submission never may.
    internal sealed class FlatPanelSnapshotPolicy
    {
        bool haveValue;
        long observedAt;
        ulong minimumSerial;
        internal void Reset() { haveValue = false; observedAt = 0; minimumSerial = 0; }
        internal void Invalidate(ulong serial)
        {
            haveValue = false; observedAt = 0;
            if (serial > minimumSerial) minimumSerial = serial;
        }
        internal bool Accept(int result, bool valid, ulong serial, long now, long frequency, out bool useCached)
        {
            useCached = false;
            if (now < 0 || frequency <= 0) { haveValue = false; return false; }
            if (result == 1 && valid && serial != 0 && serial >= minimumSerial)
            { haveValue = true; observedAt = now; return true; }
            if (result == -1 && haveValue && now >= observedAt && (now - observedAt) / (double)frequency < .3)
            { useCached = true; return true; }
            haveValue = false;
            return false;
        }
    }
}
