using System;
namespace RTMaquetaXR
{
    // Integrate observed travel, never an absolute focus point or a camera angle.
    internal sealed class FollowTranslation80
    {
        Point3 remaining;
        internal void Reconcile(Point3 requested, Point3 applied, float maximumDebt)
        {
            if (!Finite(requested) || !Finite(applied)) { Reset(); return; }
            remaining += new Point3(requested.x - applied.x, 0, requested.z - applied.z);
            float length = TouchTabletopState.Length(remaining);
            float limit = ComfortCameraOptions.Clamp(maximumDebt, .05f, 2f);
            if (length > limit) remaining = remaining * (limit / length);
        }
        internal void Reset() { remaining = default; }
        internal Point3 Step(Point3 travel, bool baselineValid, bool moving, bool wasMoving, float seconds)
        {
            if (!baselineValid || !Finite(travel) || TouchTabletopState.Length(travel) > TouchCameraFollowPolicy.TeleportDistance ||
                !ComfortCameraOptions.Finite(seconds)) { Reset(); return default; }
            // Unapplied collision travel is retained (bounded by Reconcile),
            // including across stop/restart. A new selection invalidates baseline.
            if (moving || wasMoving) remaining += new Point3(travel.x, 0, travel.z);
            float blend = (float)(1 - Math.Exp(-ComfortCameraOptions.Seconds(seconds) / .16f));
            Point3 step = remaining * blend;
            remaining -= step;
            if (TouchTabletopState.Length(remaining) < .0001f) { step += remaining; Reset(); }
            return step;
        }
        static bool Finite(Point3 p) => ComfortCameraOptions.Finite(p.x) && ComfortCameraOptions.Finite(p.y) && ComfortCameraOptions.Finite(p.z);
    }
}
