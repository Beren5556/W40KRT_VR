using System;

namespace RTMaquetaXR
{
    // UI only: squeezing a tracked trigger moves the aim by more than a mouse's
    // few-pixel drag threshold. Hold the accepted down point inside a small,
    // resolution-independent dead zone. Deliberate movement unlocks permanently
    // until release, so ScrollRects, sliders and inventory drags still work.
    internal sealed class TouchUiPressPolicy
    {
        internal bool Captured { get; private set; }
        internal bool Dragging { get; private set; }
        internal float Threshold { get; private set; }
        float anchorX, anchorY;
        int width, height;

        internal bool Begin(float x, float y, int screenWidth, int screenHeight)
        {
            Clear();
            if (!Finite(x) || !Finite(y) || screenWidth <= 0 || screenHeight <= 0) return false;
            anchorX = x; anchorY = y; width = screenWidth; height = screenHeight;
            Threshold = Math.Max(2, Math.Min(width, height) * .012f);
            Captured = true; return true;
        }

        internal bool Apply(float x, float y, int screenWidth, int screenHeight, out float acceptedX, out float acceptedY)
        {
            acceptedX = x; acceptedY = y;
            if (!Captured) return Finite(x) && Finite(y);
            if (!Finite(x) || !Finite(y) || width != screenWidth || height != screenHeight)
            { Clear(); return false; }
            float dx = x - anchorX, dy = y - anchorY;
            if (!Dragging && dx * dx + dy * dy > Threshold * Threshold) Dragging = true;
            if (!Dragging) { acceptedX = anchorX; acceptedY = anchorY; }
            return true;
        }

        internal void Clear() { Captured = Dragging = false; Threshold = 0; }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
