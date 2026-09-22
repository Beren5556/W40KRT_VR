using System;

namespace RTMaquetaXR
{
    // Per-Unity-frame input snapshot. Losing focus/manipulating the table cancels
    // a click rather than generating an up edge that could issue a game command.
    internal sealed class TouchButtonLatch
    {
        internal bool Held { get; private set; }
        internal bool Down { get; private set; }
        internal bool Up { get; private set; }
        bool requireRelease = true;
        internal void Step(bool pressed, bool allowed)
        {
            Down = Up = false;
            if (!allowed) { Held = false; requireRelease = true; return; }
            if (requireRelease) { Held = false; if (!pressed) requireRelease = false; return; }
            Down = pressed && !Held; Up = !pressed && Held; Held = pressed;
        }
        internal void Cancel() { Held = Down = Up = false; requireRelease = true; }
    }

    internal sealed class TouchAxisRepeat
    {
        int previous;
        float next;
        internal int Step(float value, float now, bool allowed)
        {
            if (!allowed || float.IsNaN(value) || float.IsInfinity(value)) { previous = 0; return 0; }
            int sign = value > .65f ? 1 : value < -.65f ? -1 : 0;
            if (Math.Abs(value) < .35f) previous = 0;
            if (sign == 0) return 0;
            if (sign != previous) { previous = sign; next = now + .42f; return sign; }
            if (now < next) return 0;
            next = now + .13f; return sign;
        }
        internal void Clear() { previous = 0; next = 0; }
    }

    internal static class TouchPointerMath
    {
        // Ray is expressed in panel-local coordinates (Unity forward +Z).
        internal static bool PanelPoint(Point3 origin, Point3 direction, float width, float height,
            bool flip, out float u, out float v)
        {
            u = v = 0;
            if (!Finite(origin.x) || !Finite(origin.y) || !Finite(origin.z) ||
                !Finite(direction.x) || !Finite(direction.y) || !Finite(direction.z) ||
                !Finite(width) || !Finite(height) || width <= 0 || height <= 0 || Math.Abs(direction.z) < .00001f) return false;
            float distance = -origin.z / direction.z;
            if (!Finite(distance) || distance <= 0) return false;
            u = .5f + (origin.x + direction.x * distance) / width;
            v = .5f + (origin.y + direction.y * distance) / height;
            if (flip) v = 1 - v;
            return Finite(u) && Finite(v);
        }
        internal static bool InPanel(float u, float v) => u >= 0 && u <= 1 && v >= 0 && v <= 1;
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool AnalogPressed(float value, bool held) => Finite(value) && value >= (held ? .45f : .65f);
    }
}
