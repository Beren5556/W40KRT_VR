using System;

namespace RTMaquetaXR
{
    // Geometry-only policy. A ground plane is an estimate, never an occlusion proof.
    internal sealed class AdaptiveDistancePolicy
    {
        internal float Current { get; private set; }
        float settled;
        internal void Reset(float ceiling) { Current = ceiling; settled = 0; }
        internal float Update(float ceiling, float needed, bool usable, float seconds)
        {
            if (!DrawDistanceOptions.Finite(ceiling) || ceiling <= 0) return Current = 1000;
            if (!usable || !DrawDistanceOptions.Finite(needed) || !DrawDistanceOptions.Finite(seconds) || seconds <= 0 || seconds > .25f)
            { Reset(ceiling); return Current; }
            float target = Math.Min(ceiling, Math.Max(20, needed));
            if (!DrawDistanceOptions.Finite(Current) || Current <= 0 || Current > ceiling) Current = ceiling;
            if (target >= Current) { Current = target; settled = 0; }
            else if (Current - target > 1)
            {
                settled += seconds;
                if (settled >= .25f) Current = Math.Max(target, Current - 8 * seconds);
            }
            else settled = 0;
            return Current;
        }
        internal static bool GroundDepth(float originY, float directionY, float floorY, out float depth)
        {
            depth = 0;
            if (!DrawDistanceOptions.Finite(originY) || !DrawDistanceOptions.Finite(directionY) ||
                !DrawDistanceOptions.Finite(floorY) || directionY >= -.05f || originY <= floorY) return false;
            depth = (floorY - originY) / directionY;
            return DrawDistanceOptions.Finite(depth) && depth > 0;
        }
    }

    internal static class AdaptiveGroundPolicy
    {
        internal const int ProbeCount = 5, MinimumAgreement = 4;
        internal const float MaximumHeightSpread = .75f, BelowSurfaceMargin = 2f;
        internal static bool TryPlane(float[] heights, bool[] valid, bool multipleLevels, bool bufferOverflow,
            out float floor, out int count, out float spread)
        {
            floor = 0; count = 0; spread = 0;
            if (heights == null || valid == null || heights.Length != ProbeCount || valid.Length != ProbeCount) return false;
            float lowest = float.MaxValue, highest = float.MinValue;
            for (int i = 0; i < ProbeCount; ++i)
            {
                if (!valid[i] || !DrawDistanceOptions.Finite(heights[i])) continue;
                ++count; lowest = Math.Min(lowest, heights[i]); highest = Math.Max(highest, heights[i]);
            }
            if (count > 0) spread = highest - lowest;
            if (multipleLevels || bufferOverflow || count < MinimumAgreement || spread > MaximumHeightSpread) return false;
            floor = lowest - BelowSurfaceMargin; return true;
        }
    }
}
