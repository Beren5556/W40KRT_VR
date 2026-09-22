using System;

namespace RTMaquetaXR
{
    // A load token is distinct from help visibility and Unity UI sub-scenes.
    // Only saved/new-game requests or the typed native area activation arm it;
    // tutorials and duplicated AreaReady notifications cannot rearm it.
    internal sealed class TouchCameraLoadPolicy
    {
        internal bool Pending { get; private set; }
        internal bool AreaReady { get; private set; }
        internal int Requests { get; private set; }
        internal int Attempts { get; private set; }
        double stableSince = -1;
        object stableLeader;
        int stableCount;
        internal void Request() { Cancel(); Pending = true; ++Requests; }
        internal void Loaded() { if (Pending) AreaReady = true; }
        internal void Cancel() { Pending = AreaReady = false; stableSince = -1; stableLeader = null; stableCount = 0; }
        internal void Defer() { stableSince = -1; stableLeader = null; stableCount = 0; }
        internal void UserIntent(bool cameraAvailable, bool deliberateInput)
        {
            if (!Pending) return;
            if (!cameraAvailable) { Defer(); return; }
            if (AreaReady && deliberateInput) Cancel();
        }
        internal bool Step(bool sceneReady, bool inputNeutral, bool blocked, object leader, int count, double now)
        {
            if (!Pending) return false;
            if (!AreaReady || !sceneReady || !inputNeutral || blocked || leader == null || count <= 0 || count > 32 ||
                double.IsNaN(now) || double.IsInfinity(now))
            { stableSince = -1; stableLeader = null; stableCount = 0; return false; }
            if (stableSince < 0 || now < stableSince || !ReferenceEquals(leader, stableLeader) || count != stableCount)
            { stableSince = now; stableLeader = leader; stableCount = count; return false; }
            // Let selection/view ownership settle. This expires before the
            // normal introductory help delay and never delays native loading.
            if (now - stableSince < .25) return false;
            Cancel(); ++Attempts; return true;
        }
    }
}
