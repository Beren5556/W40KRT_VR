using System;

namespace RTMaquetaXR
{
    internal struct SelectionPoint
    {
        internal float X, Y;
        internal SelectionPoint(float x, float y) { X = x; Y = y; }
    }

    internal struct SelectionRect
    {
        internal float MinX, MinY, MaxX, MaxY;
        internal SelectionRect(float x, float y) { MinX = MaxX = x; MinY = MaxY = y; }
        internal void Include(SelectionPoint p)
        { MinX = Math.Min(MinX, p.X); MinY = Math.Min(MinY, p.Y); MaxX = Math.Max(MaxX, p.X); MaxY = Math.Max(MaxY, p.Y); }
        internal SelectionPoint Corner(int index) => new SelectionPoint(index == 0 || index == 3 ? MinX : MaxX, index < 2 ? MinY : MaxY);
    }

    // Both the drawn frame and membership use one scene plane captured at down.
    // No screen pixels, HUD dimensions, eye matrices or subsequent head pose.
    internal struct TouchSelectionPlane
    {
        internal Point3 Origin, Right, Forward, Normal;
        internal static bool TryCreate(Point3 origin, Point3 right, Point3 normal, out TouchSelectionPlane plane)
        {
            plane = default;
            if (!Finite(origin) || !Finite(right) || !Finite(normal) || !Normalize(ref normal)) return false;
            right = right - normal * Dot(right, normal);
            if (!Normalize(ref right))
            {
                right = Cross(normal, Math.Abs(normal.y) < .9f ? new Point3(0, 1, 0) : new Point3(0, 0, 1));
                if (!Normalize(ref right)) return false;
            }
            Point3 forward = Cross(right, normal);
            plane = new TouchSelectionPlane { Origin = origin, Right = right, Forward = forward, Normal = normal }; return true;
        }
        internal SelectionPoint Project(Point3 point)
        { Point3 offset = point - Origin; return new SelectionPoint(Dot(offset, Right), Dot(offset, Forward)); }
        internal Point3 World(SelectionPoint point) => Origin + Right * point.X + Forward * point.Y;
        internal bool RayPoint(Point3 origin, Point3 direction, float maxDistance, out SelectionPoint point)
        {
            point = default;
            if (!Finite(origin) || !Finite(direction) || !Finite(maxDistance) || maxDistance <= 0 || !Normalize(ref direction)) return false;
            float denominator = Dot(direction, Normal);
            if (Math.Abs(denominator) < .02f) return false;
            float distance = Dot(Origin - origin, Normal) / denominator;
            if (!Finite(distance) || distance < 0 || distance > maxDistance) return false;
            point = Project(origin + direction * distance); return true;
        }

        // Exact SAT overlap with the orthogonal projection of all eight AABB
        // corners (a zonotope), not the old PC lower-left/upper-right shortcut.
        // For tabletop footprints extents.y is zero; rotation is still handled.
        internal bool Overlaps(SelectionRect rect, Point3 center, Point3 extents)
        {
            if (!Finite(center) || !Finite(extents) || extents.x < 0 || extents.y < 0 || extents.z < 0) return false;
            SelectionPoint projected = Project(center);
            float dx = projected.X - (rect.MinX + rect.MaxX) * .5f, dy = projected.Y - (rect.MinY + rect.MaxY) * .5f;
            float hx = (rect.MaxX - rect.MinX) * .5f, hy = (rect.MaxY - rect.MinY) * .5f;
            return AxisOverlap(dx, dy, hx, hy, extents, 1, 0) && AxisOverlap(dx, dy, hx, hy, extents, 0, 1) &&
                AxisOverlap(dx, dy, hx, hy, extents, -Forward.x, Right.x) &&
                AxisOverlap(dx, dy, hx, hy, extents, -Forward.y, Right.y) &&
                AxisOverlap(dx, dy, hx, hy, extents, -Forward.z, Right.z);
        }
        bool AxisOverlap(float dx, float dy, float hx, float hy, Point3 extents, float ax, float ay)
        {
            float radius = hx * Math.Abs(ax) + hy * Math.Abs(ay) +
                extents.x * Math.Abs(Right.x * ax + Forward.x * ay) +
                extents.y * Math.Abs(Right.y * ax + Forward.y * ay) +
                extents.z * Math.Abs(Right.z * ax + Forward.z * ay);
            return Math.Abs(dx * ax + dy * ay) <= radius + .00001f;
        }
        internal SelectionRect IncludeFootprint(SelectionRect rect, Point3 center, Point3 extents)
        {
            for (int i = 0; i < 8; ++i)
                rect.Include(Project(center + new Point3((i & 1) == 0 ? -extents.x : extents.x,
                    (i & 2) == 0 ? -extents.y : extents.y, (i & 4) == 0 ? -extents.z : extents.z)));
            return rect;
        }
        internal static float DragThreshold(float worldScale, float distanceToPlane)
        {
            // At least 18 physical millimetres, or about 1.4 degrees at the
            // target, measured PERPENDICULAR to the aiming ray. Distances on
            // the floor can explode near the horizon and are not hand motion.
            return Math.Max(Math.Max(.001f, worldScale) * .018f, Math.Max(0, distanceToPlane) * .025f);
        }
        internal static bool AimDistance(Point3 anchor, Point3 origin, Point3 direction, out float distance)
        {
            distance = 0;
            if (!Finite(anchor) || !Finite(origin) || !Finite(direction) || !Normalize(ref direction)) return false;
            Point3 offset = anchor - origin;
            float along = Dot(offset, direction);
            if (along <= 0) return false;
            Point3 perpendicular = offset - direction * along;
            distance = (float)Math.Sqrt(Dot(perpendicular, perpendicular));
            return Finite(distance);
        }
        internal static float Dot(Point3 a, Point3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        static Point3 Cross(Point3 a, Point3 b) => new Point3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        static bool Normalize(ref Point3 p)
        { float length = (float)Math.Sqrt(Dot(p, p)); if (!Finite(length) || length < .00001f) return false; p = p * (1 / length); return true; }
        internal static bool Finite(Point3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
