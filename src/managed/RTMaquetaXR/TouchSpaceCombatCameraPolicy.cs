using System;

namespace RTMaquetaXR
{
    internal static class TouchSpaceCombatCameraPolicy
    {
        internal const float Pitch = 58f, MinimumTilt = -75f, MaximumTilt = 75f;
        // One unit of physical motion maps to this many native board units.
        // Derive a new profile from the actual ship, never a human-height floor.
        internal static float Scale(float radius, float nativeDistance)
        {
            if (!ComfortCameraOptions.Finite(radius) || radius <= 0) radius = 1;
            if (!ComfortCameraOptions.Finite(nativeDistance) || nativeDistance <= 0) nativeDistance = radius * 4;
            return ComfortCameraOptions.Clamp(radius * 4.5f, 2, 2000);
        }
        internal static float Near(float baseline) => Math.Max(.1f, baseline * .08f);
        internal static float Far(float baseline) => baseline * 8f;
        internal static float Distance(float radius, float scale) => Math.Max(radius * 3.2f, scale * .95f);
    }
}

