using System;

namespace RTMaquetaXR
{
    // Tangent bounds use positive camera Z, unlike Unity's view matrix. The
    // caller moves its auxiliary camera backwards by the same shift passed in.
    internal struct StereoVisibilityBounds
    {
        public float Left, Right, Bottom, Top, Near, Far;
    }

    internal static class StereoVisibilityVolume
    {
        internal static bool TryFit(Point3[] corners, float backwardsShift, out StereoVisibilityBounds bounds)
        {
            bounds = default(StereoVisibilityBounds);
            if (corners == null || corners.Length != 16 || !Finite(backwardsShift) || backwardsShift < 0)
                return false;

            double left = double.PositiveInfinity, right = double.NegativeInfinity;
            double bottom = double.PositiveInfinity, top = double.NegativeInfinity;
            double near = double.PositiveInfinity, far = double.NegativeInfinity;
            for (int i = 0; i < corners.Length; ++i)
            {
                Point3 corner = corners[i];
                if (!Finite(corner.x) || !Finite(corner.y) || !Finite(corner.z)) return false;
                // Do the translation and division in double: adding two finite
                // floats can overflow a float before range validation occurs.
                double z = (double)corner.z + backwardsShift;
                if (z <= 0.0001) return false;
                double x = corner.x / z, y = corner.y / z;
                left = Math.Min(left, x); right = Math.Max(right, x);
                bottom = Math.Min(bottom, y); top = Math.Max(top, y);
                near = Math.Min(near, z); far = Math.Max(far, z);
            }

            // Extrema of x/z and y/z over a convex frustum with z>0 occur
            // at its vertices. Their union therefore fits within these six
            // planes; the margin also covers conversion back to float.
            var fitted = new StereoVisibilityBounds {
                Left = (float)(left - Margin(left)), Right = (float)(right + Margin(right)),
                Bottom = (float)(bottom - Margin(bottom)), Top = (float)(top + Margin(top)),
                // A near plane cannot cross the auxiliary camera. Very close
                // inputs still get a conservative positive half-depth plane.
                Near = (float)Math.Max(near * 0.5, near - Margin(near)),
                Far = (float)(far + Margin(far))
            };
            if (!Finite(fitted.Left) || !Finite(fitted.Right) || !Finite(fitted.Bottom) ||
                !Finite(fitted.Top) || !Finite(fitted.Near) || !Finite(fitted.Far) ||
                fitted.Left >= fitted.Right || fitted.Bottom >= fitted.Top ||
                fitted.Near <= 0 || fitted.Near >= fitted.Far ||
                fitted.Left > left || fitted.Right < right || fitted.Bottom > bottom ||
                fitted.Top < top || fitted.Near > near || fitted.Far < far)
                return false;

            bounds = fitted;
            return true;
        }

        static double Margin(double value) => Math.Abs(value) * 0.0001 + 0.0001;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
