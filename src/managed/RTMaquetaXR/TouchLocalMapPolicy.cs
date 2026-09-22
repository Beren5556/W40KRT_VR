using System;

namespace RTMaquetaXR
{
    internal struct TouchLocalMapMotion
    {
        internal float PanX, PanY, Zoom, Rotation;
        internal bool Active => PanX != 0 || PanY != 0 || Zoom != 0 || Rotation != 0;
    }
    internal sealed class TouchLocalMapPolicy
    {
        bool armed;
        internal TouchLocalMapMotion Step(bool allowed, float lx, float ly, float rx, float ry, float dt)
        {
            if (!allowed || !Finite(lx) || !Finite(ly) || !Finite(rx) || !Finite(ry) || !Finite(dt) || dt <= 0 || dt > .25f)
            { armed = false; return default(TouchLocalMapMotion); }
            if (!armed)
            {
                if (Math.Abs(lx) <= .2f && Math.Abs(ly) <= .2f && Math.Abs(rx) <= .2f && Math.Abs(ry) <= .2f) armed = true;
                return default(TouchLocalMapMotion);
            }
            dt = Math.Min(dt, .05f);
            // Screen-relative content movement; native map bounds still clamp.
            return new TouchLocalMapMotion { PanX = Axis(rx) * 480 * dt, PanY = Axis(ry) * 480 * dt,
                Zoom = Axis(ly) * 6 * dt, Rotation = -Axis(lx) * 55 * dt };
        }
        internal void Reset() { armed = false; }
        static float Axis(float x) => Math.Abs(x) <= .2f ? 0 : Math.Sign(x) * Math.Min(1, (Math.Abs(x) - .2f) / .8f);
        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    }
}
