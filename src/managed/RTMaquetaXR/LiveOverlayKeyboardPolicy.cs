namespace RTMaquetaXR
{
    internal sealed class LiveOverlayKeyboardPolicy
    {
        bool held, requireRelease;
        internal bool Captured { get; private set; }
        internal bool Step(bool pressed, bool available)
        {
            bool previous = held;
            held = pressed;
            Captured = available && (pressed || previous);
            if (!available) { requireRelease = pressed; return false; }
            if (requireRelease) { if (!pressed) requireRelease = false; return false; }
            return pressed && !previous;
        }
        internal void Reset() { held = requireRelease = Captured = false; }
    }
}
