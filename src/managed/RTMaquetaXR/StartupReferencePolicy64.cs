using System;

namespace RTMaquetaXR
{
    // A runtime may expose valid predicted views before its initial reference
    // space has settled. Do not show a transient first anchor to either eye.
    // Frames still begin/end normally while presentation has no layers.
    internal sealed class StartupReferencePolicy64
    {
        ulong origin, serial;
        int samples;
        double since;
        internal bool Ready { get; private set; }
        internal bool Observe(ulong revision, ulong frameSerial, double now, bool poseUsable)
        {
            if (Ready) return true;
            if (!poseUsable || revision == 0 || frameSerial == 0 || double.IsNaN(now) || double.IsInfinity(now))
            { origin = serial = 0; samples = 0; return false; }
            if (origin != revision || samples == 0 || now < since)
            { origin = revision; serial = frameSerial; since = now; samples = 1; return false; }
            if (serial != frameSerial) { serial = frameSerial; ++samples; }
            // No still-head requirement: normal physical motion remains valid.
            // At 72/90/120 Hz this takes roughly 80 ms, not a multi-second wait.
            Ready = samples >= 3 && now - since >= .08;
            return Ready;
        }
    }
}
