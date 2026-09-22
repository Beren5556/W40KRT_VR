using System;

namespace RTMaquetaXR
{
    // Measured on the installed ServoSkull_02 rest mesh, in Unity coordinates
    // (not the X-mirrored OBJ export). Vertex 5827 is the green screen's apex;
    // its outward normal is +Z and the instrument's upright direction is +Y.
    internal static class TouchServoAlignment
    {
        internal const float ScreenX = .10391768f;
        internal const float ScreenY = 1.75010264f;
        internal const float ScreenZ = .03209279f;
    }

    // Aim and grip are different runtime-defined controller spaces. Calibrate
    // their rigid relation per hand, rather than guessing a Quest pitch angle.
    // A temporarily unavailable aim pose can still follow the tracked grip.
    internal sealed class TouchServoPoseState
    {
        bool calibrated;
        ViewPose gripToAim;

        internal bool TryPose(bool gripTracked, ViewPose grip, bool aimValid, ViewPose aim, out ViewPose screen)
        {
            screen = default(ViewPose);
            if (!gripTracked || !Finite(grip)) return false;
            if (aimValid && Finite(aim))
            {
                Rotation4 inverse = grip.rotation.Inverse();
                gripToAim = new ViewPose(inverse.Rotate(aim.position - grip.position), inverse * aim.rotation);
                calibrated = true;
                // Keep the exact same origin and orientation as the targeting
                // ray when available, without accumulating round-off each frame.
                screen = aim;
                return true;
            }
            if (!calibrated) return false;
            screen = new ViewPose(grip.position + grip.rotation.Rotate(gripToAim.position), grip.rotation * gripToAim.rotation);
            return Finite(screen);
        }

        internal void Reset() { calibrated = false; gripToAim = default(ViewPose); }

        static bool Finite(ViewPose pose)
        {
            Point3 p = pose.position; Rotation4 q = pose.rotation;
            float norm = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return Finite(p.x) && Finite(p.y) && Finite(p.z) && Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w) &&
                norm > .000001f && norm < 100f;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
