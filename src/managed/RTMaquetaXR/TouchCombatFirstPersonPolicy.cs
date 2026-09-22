using System;

namespace RTMaquetaXR
{
    // A camera gesture observes the native ground click. It never emits or
    // replays movement, spends action points, or changes a native target.
    internal sealed class TouchCombatFirstPersonPolicy
    {
        internal const float PositionTolerance = .75f;
        object unit, area;
        long revision, turn;
        float at = -1;
        Point3 point;
        bool accepted;
        internal void Reset() { unit = area = null; at = -1; accepted = false; }
        internal bool Click(object actor, object scene, long sceneRevision, long turnToken, Point3 target, float now, bool nativeAccepted)
        {
            if (actor == null || scene == null || !TouchTabletopState.Finite(target) || !ComfortCameraOptions.Finite(now) || now < 0)
            { Reset(); return false; }
            bool doubleClick = ReferenceEquals(actor, unit) && ReferenceEquals(scene, area) && revision == sceneRevision && turn == turnToken &&
                now > at && now - at <= TouchCameraFocusPolicy.DoubleClickSeconds &&
                TouchTabletopState.Length(target - point) <= PositionTolerance;
            bool enter = doubleClick && (accepted || nativeAccepted);
            if (enter) { Reset(); return true; }
            unit = actor; area = scene; revision = sceneRevision; turn = turnToken; point = target; at = now; accepted = nativeAccepted;
            return false;
        }
    }
}
