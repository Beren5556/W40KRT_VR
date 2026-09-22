using System;

namespace RTMaquetaXR
{
    internal static class HudCapturePolicy
    {
        internal static float IncludeHelper(float depth, Point3 helper, Point3 head, Point3 forward, bool visible)
        {
            if (!visible) return depth;
            Point3 offset = helper - head;
            float candidate = offset.x * forward.x + offset.y * forward.y + offset.z * forward.z;
            return !float.IsNaN(candidate) && !float.IsInfinity(candidate) && candidate > 0 ? Math.Max(depth, candidate) : depth;
        }
        // Capture depth and compositor plane depth are different contracts. The
        // menu and gesture card may sit behind a deliberately close game HUD.
        // Extending only the private UI camera cannot admit scene geometry.
        internal static float FarClip(float panelDistance, float uiDistance, float helperDepth)
        {
            float far = Math.Max(panelDistance, uiDistance) * 1.1f;
            if (!float.IsNaN(helperDepth) && !float.IsInfinity(helperDepth) && helperDepth > 0)
                far = Math.Max(far, helperDepth * 1.1f);
            return far;
        }
    }
}
