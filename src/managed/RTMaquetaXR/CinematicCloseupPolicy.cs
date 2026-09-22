using System;

namespace RTMaquetaXR
{
    internal static class CinematicCloseupPolicy
    {
        internal const float TargetCharacterDegrees = 28f;
        internal const float MinimumPhysicalDistance = .4f;
        internal const float MoveMetersPerSecond = .8f;
        internal const float TurnDegreesPerSecond = 35f;
        internal const float CollisionRadiusMeters = .07f;
        internal static float FitDistance(float x, float y, float z, float horizontalFov, float verticalFov)
        {
            if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(horizontalFov) || !Finite(verticalFov) ||
                horizontalFov < 1 || horizontalFov > 170 || verticalFov < 1 || verticalFov > 170) return 0;
            double horizontal = Math.Tan(horizontalFov * .34 * Math.PI / 180);
            double vertical = Math.Tan(verticalFov * .30 * Math.PI / 180);
            return (float)Math.Max(0, Math.Max(Math.Abs(x) / horizontal - z, Math.Abs(y) / vertical - z));
        }
        internal static float Distance(float sourceDistance, float characterHeight, float scale, float sourceFov, float headsetFov)
        {
            if (!Finite(sourceDistance) || sourceDistance <= 0 || !Finite(scale) || scale <= 0) return 0;
            float desired;
            if (Finite(characterHeight) && characterHeight > 0)
                desired = characterHeight * .5f / (float)Math.Tan(TargetCharacterDegrees * Math.PI / 360);
            else
            {
                if (!Finite(sourceFov) || !Finite(headsetFov) || sourceFov < 1 || sourceFov > 160 || headsetFov < 1 || headsetFov > 160) return sourceDistance;
                // An authored telephoto shot cannot be copied literally into a
                // wide HMD lens. Dolly along its existing composition instead.
                desired = sourceDistance * (float)(Math.Tan(sourceFov * Math.PI / 360) / Math.Tan(headsetFov * Math.PI / 360));
            }
            return Math.Min(sourceDistance, Math.Max(scale * MinimumPhysicalDistance, desired));
        }
        internal static float Travel(float remaining, float scale, float seconds)
        {
            if (!Finite(remaining) || !Finite(scale) || !Finite(seconds) || remaining <= 0 || scale <= 0 || seconds <= 0) return 0;
            seconds = Math.Min(.05f, seconds);
            return Math.Min(remaining * (1f - (float)Math.Exp(-seconds / .4f)), scale * MoveMetersPerSecond * seconds);
        }
        internal static bool KeepAuthored(float distance, float wanted, float forwardCosine) =>
            Finite(distance) && Finite(wanted) && Finite(forwardCosine) && distance > 0 && wanted > 0 &&
            distance <= wanted * 1.12f && forwardCosine >= .8f;
        internal static float SafeTravel(float distance, float nearestHit, bool overlap, bool saturated)
        {
            if (overlap || saturated || !Finite(distance) || !Finite(nearestHit) || distance <= 0) return 0;
            return Math.Max(0, Math.Min(distance, nearestHit - .02f));
        }
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
