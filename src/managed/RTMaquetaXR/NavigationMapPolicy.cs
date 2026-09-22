using System;

namespace RTMaquetaXR
{
    // The map camera remains a native PC camera inside a VR panel. These
    // dimensions describe the panel, never a ship route or astronomical zoom.
    internal struct NavigationMapPanel
    {
        internal float Width, Distance, Aspect, OffsetX, OffsetY;
        internal NavigationMapPanel(float width, float distance, float aspect, float x, float y)
        {
            Width = NavigationMapPolicy.Width(width); Distance = NavigationMapPolicy.Distance(distance);
            Aspect = NavigationMapPolicy.Aspect(aspect);
            OffsetX = NavigationMapPolicy.Offset(x); OffsetY = NavigationMapPolicy.Offset(y);
        }
    }

    internal static class NavigationMapPolicy
    {
        internal static float Width(float value) => Clamp(value, .45f, 1, .82f);
        internal static float Distance(float value) => Clamp(value, .5f, 3, .5f);
        internal static float Aspect(float value) => !Finite(value) || value <= 0 ? 0 : Clamp(value, .8f, 2.4f, 0);
        internal static float Offset(float value) => Clamp(value, -.65f, .65f, 0);
        internal static bool Neutral(float x,float y) => Finite(x)&&Finite(y)&&Math.Abs(x)<.2f&&Math.Abs(y)<.2f;
        // CameraZoom divides scroll by ZoomLength before interpolating these
        // endpoints. Increasing only ZoomLength never extended visible range.
        // PhysicalZoomMin is the far endpoint at normalized scroll zero.
        internal static float WarpPhysicalFar(float native) => Finite(native) && native < 0 ? native * 4 : native;
        internal static float WarpFovFar(float native) => Finite(native) && native > 0 && native < 110 ? Math.Min(110, native * 1.5f) : native;
        internal static float NextAspect(float current, int direction)
        {
            if (direction == 0) return Aspect(current);
            float next = current <= 0 ? (direction < 0 ? 2.4f : .8f) :
                (float)Math.Round((current + Math.Sign(direction) * .1f) * 10) / 10;
            return next < .79f || next > 2.41f ? 0 : Aspect(next);
        }
        internal static bool Interactive(string mode, bool navigationMap, bool flat, bool panelHit,
            bool inputAllowed, bool nativeUiOnly, bool serviceWindow, bool modal, bool tutorial) =>
            navigationMap && flat && panelHit && inputAllowed && !nativeUiOnly && !serviceWindow && !modal && !tutorial &&
            (mode == "GlobalMap" || mode == "StarSystem");

        // This replaces the native scroll wheel sample, not its ZoomLength,
        // locks, smoothing or permission checks. Integrate by elapsed time so
        // headset refresh rate cannot change the speed of native zoom.
        internal static float ZoomWheel(float uiScrollY, float dt, bool allowed) =>
            allowed && Finite(uiScrollY) && Finite(dt) && dt > 0 ?
                Clamp(uiScrollY, -5, 5, 0) * .14f * Math.Min(dt, .05f) : 0;
        static float Clamp(float value, float low, float high, float fallback) =>
            !Finite(value) ? fallback : Math.Max(low, Math.Min(high, value));
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    internal sealed class NavigationMapInputEpoch
    {
        long revision = long.MinValue;
        string mode;
        bool map;
        internal bool Observe(bool currentMap, long currentRevision, string currentMode)
        {
            bool cancel = (map || currentMap) &&
                (map != currentMap || revision != currentRevision || mode != currentMode);
            map = currentMap; revision = currentRevision; mode = currentMode;
            return cancel;
        }
        internal void Clear() { revision = long.MinValue; mode = null; map = false; }
    }
}
