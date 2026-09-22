using System;

namespace RTMaquetaXR
{
    // A transient menu/camera transition must not enable a third world render
    // permanently. Recovery observes completed frames; it never trusts an eye
    // from the preceding frame or attempts another copy in the failing frame.
    internal sealed class DesktopMirrorRecovery
    {
        internal bool Fallback { get; private set; }
        internal string Reason { get; private set; }
        internal int Failures { get; private set; }
        internal int Recoveries { get; private set; }
        internal int GoodFrames { get; private set; }
        internal int RetryFrame { get; private set; }
        int lastObserved = -1, copyFailures;

        internal void Reset()
        {
            Fallback = false; Reason = null;
            Failures = Recoveries = GoodFrames = RetryFrame = copyFailures = 0;
            lastObserved = -1;
        }

        internal void Fail(int frame, string reason, bool copyError)
        {
            Fallback = true; Reason = reason; ++Failures; GoodFrames = 0;
            lastObserved = frame;
            // Broken graphics copies retry with bounded backoff. A temporary
            // camera/eye mismatch needs only two subsequent healthy frames.
            if (copyError) ++copyFailures;
            RetryFrame = frame + (copyError ? Math.Min(600, 30 * (1 << Math.Min(copyFailures, 4))) : 1);
        }

        internal bool Observe(int frame, bool stereoPrepared, bool screenSafe, bool completePair)
        {
            if (!Fallback || frame <= lastObserved) return false;
            bool consecutive = lastObserved == frame - 1;
            lastObserved = frame;
            if (!stereoPrepared || !screenSafe || !completePair || frame < RetryFrame)
            { GoodFrames = 0; return false; }
            GoodFrames = consecutive ? GoodFrames + 1 : 1;
            if (GoodFrames < 2) return false;
            Fallback = false; Reason = null; GoodFrames = 0; ++Recoveries;
            return true;
        }

        internal void Copied() { copyFailures = 0; }
    }
}
