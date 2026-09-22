using System;

namespace RTMaquetaXR
{
    // World dimensions come from the same native navigation graph used by
    // GridVisualizer. Neither the player ship nor the eye far clip owns them.
    internal static class SpaceBackdropPolicy65
    {
        internal static bool TryDimensions(float width, float height, out float span, out float depth)
        {
            span = depth = 0;
            if (!Finite(width) || !Finite(height) || width <= 0 || height <= 0) return false;
            float side = Math.Max(width, height);
            // Reject a corrupt/unready graph rather than displaying an
            // arbitrary enormous fallback. The native backdrop stays intact.
            if (side > 100000) return false;
            float margin = Math.Max(8, side * .15f);
            span = (side + margin * 2) * .1f;
            depth = .025f; // Just behind the board, avoiding coincident depth.
            return true;
        }
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
