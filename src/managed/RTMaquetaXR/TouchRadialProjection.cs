using System;
namespace RTMaquetaXR
{
    // A short adaptive filter in the actual wheel plane, never a scene hit.
    // Its result drives BOTH the illuminated sector and the visible beam end.
    // Fast intentional movement remains direct; tiny hand tremor is damped.
    internal sealed class TouchRadialPointerFilter
    {
        Point3 filtered;
        double last = double.NaN;
        long generation = -1;
        internal Point3 Step(Point3 point, double now, long revision)
        {
            double dt = now - last; last = now;
            if (revision != generation || double.IsNaN(dt) || dt <= 0 || dt > .15)
            { generation = revision; return filtered = point; }
            float dx = point.x - filtered.x, dy = point.y - filtered.y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            double response = distance > 45 ? .006 : distance > 10 ? .012 : .025;
            float blend = (float)(1 - Math.Exp(-dt / response));
            filtered = new Point3(filtered.x + dx * blend, filtered.y + dy * blend, 0);
            return filtered;
        }
    }
    // Physical world space only. Canonical HUD camera transforms cannot leak
    // into input; this plane stays fixed until the wheel closes.
    internal static class TouchRadialProjection
    {
        internal const float MetresPerPixel = .0001625f;
        internal static bool InPointerField(Point3 local) => Finite(local) && Math.Abs(local.x) <= 550 && Math.Abs(local.y) <= 550;
        internal static float FurthestDepth(float centreDepth, float rightDepthPerPixel, float upDepthPerPixel, bool information)
        {
            float wheel=centreDepth+486*Math.Abs(rightDepthPerPixel)+620*Math.Abs(upDepthPerPixel);
            if(!information)return wheel;
            float card=centreDepth+480*Math.Abs(rightDepthPerPixel)+Math.Max(-520*upDepthPerPixel,-1020*upDepthPerPixel);
            return Math.Max(wheel,card);
        }
        internal static bool Intersect(Point3 origin, Point3 direction, Point3 center, Rotation4 rotation,
            float worldPixel, out Point3 local, out Point3 world)
        {
            local = world = default(Point3);
            if (!Finite(worldPixel) || worldPixel <= 0 || !Finite(origin) || !Finite(direction) || !Finite(center)) return false;
            if(!Finite(rotation.x)||!Finite(rotation.y)||!Finite(rotation.z)||!Finite(rotation.w)||rotation.x*rotation.x+rotation.y*rotation.y+rotation.z*rotation.z+rotation.w*rotation.w<1e-6f)return false;
            var inverse = rotation.Inverse(); var from = inverse.Rotate(origin - center); var vector = inverse.Rotate(direction);
            if (!Finite(from) || !Finite(vector) || Math.Abs(vector.z) < 1e-6f) return false;
            float distance = -from.z / vector.z;
            if (!Finite(distance) || distance <= 0) return false;
            local = (from + vector * distance) * (1 / worldPixel); world = origin + direction * distance;
            return Finite(local) && Finite(world);
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Point3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
    }
}
