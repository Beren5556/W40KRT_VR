namespace RTMaquetaXR
{
    internal static class EngineEffectPolicy
    {
        // These profiles affect presentation guards, never simulation or UI.
        internal static int Normalize(int value) => value >= 0 && value <= 2 ? value : 0;
        internal static bool Suppress(int profile, int minimum, bool active, bool attached, bool flat, bool exactEye) =>
            active && attached && !flat && exactEye && minimum > 0 && Normalize(profile) >= minimum;
        internal static bool EmptyHighlight(bool active, bool attached, bool flat, bool exactEye, int count) =>
            active && attached && !flat && exactEye && count == 0;
    }
}
