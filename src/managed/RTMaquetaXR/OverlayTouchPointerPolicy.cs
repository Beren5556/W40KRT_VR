namespace RTMaquetaXR
{
    // Presentation identity, not head pose or per-frame image serial. Ordinary
    // tracking motion must not invalidate a deliberate button press.
    internal struct OverlayTouchPointerContext
    {
        internal bool Ready, Flat, Flip;
        internal int ScreenWidth, ScreenHeight, Root, Left, Right;
        internal float PanelWidth, PanelHeight;
        internal bool Same(OverlayTouchPointerContext other) => Ready == other.Ready && Flat == other.Flat && Flip == other.Flip &&
            ScreenWidth == other.ScreenWidth && ScreenHeight == other.ScreenHeight && Root == other.Root &&
            Left == other.Left && Right == other.Right && Near(PanelWidth,other.PanelWidth) && Near(PanelHeight,other.PanelHeight);
        static bool Near(float a,float b) => System.Math.Abs(a-b) <= .0001f * System.Math.Max(1,System.Math.Max(System.Math.Abs(a),System.Math.Abs(b)));
    }

    internal sealed class OverlayTouchPointerContextGuard
    {
        bool known;
        OverlayTouchPointerContext previous;
        internal void Reset() { known = false; previous = default(OverlayTouchPointerContext); }
        // A captured release or a new Down on the same sample as a presentation
        // switch is cancelled and consumed. It must not reach the trigger's
        // ordinary Execute fallback merely because pointer hit-testing stopped.
        internal bool Observe(OverlayTouchPointerContext current, bool down, bool held, bool up, out bool consume)
        {
            bool cancel = !current.Ready || (known && !previous.Same(current));
            previous = current; known = current.Ready;
            consume = cancel && (down || held || up);
            return cancel;
        }
    }

    // A press belongs to one visible control and one menu revision. Tracking
    // loss or navigation cancels it; it can never click through to the game.
    internal sealed class OverlayTouchPointerPress
    {
        int target = -1, revision;
        bool captured;
        internal bool Captured => captured;
        internal void Cancel() { captured = false; target = -1; }
        internal int Step(bool allowed, bool hit, int hitTarget, int menuRevision,
            bool down, bool up, out bool consume)
        {
            consume = captured;
            if (!allowed) { Cancel(); return -1; }
            if (down)
            {
                Cancel(); consume = hit;
                if (hit) { captured = true; target = hitTarget; revision = menuRevision; }
            }
            if (!captured) return -1;
            consume = true;
            if (!hit || menuRevision != revision) { Cancel(); return -1; }
            if (!up) return -1;
            int action = target == hitTarget ? target : -1;
            Cancel(); return action;
        }
    }
}
