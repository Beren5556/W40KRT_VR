using System;

namespace RTMaquetaXR
{
    internal sealed class CombatFocusPolicy
    {
        internal bool InCombat { get; private set; }
        internal bool Pending { get; private set; }
        internal bool Moving { get; private set; }
        internal bool OwnsView { get; private set; }
        internal bool Returning { get; private set; }
        internal void Observe(bool combat)
        {
            if (combat == InCombat) return;
            InCombat = combat;
            if (combat) { Pending = true; Returning = false; }
            else { Pending = Moving = Returning = OwnsView = false; }
        }
        internal void Begin() { Pending = false; Moving = OwnsView = true; Returning = false; }
        internal void Cancel(bool userMoved)
        {
            Pending = Moving = false;
            if (userMoved || Returning) OwnsView = Returning = false;
        }
        internal void Arrive() { Moving = false; if (Returning) Returning = OwnsView = false; }
        internal void Reset() { InCombat = Pending = Moving = OwnsView = Returning = false; }
        internal static float Distance(float scale, float height)
        {
            if (!CinematicCloseupPolicy.Finite(scale) || scale <= 0 || !CinematicCloseupPolicy.Finite(height) || height <= 0) return 0;
            // A tactical close view still has space for adjacent grid cells.
            return Math.Max(scale * .65f, height * .5f / (float)Math.Tan(18 * Math.PI / 360));
        }
        internal const float EntryPitch = 72f;
        internal static float TacticalDistance(float scale, float height, float radius)
        {
            if (!CinematicCloseupPolicy.Finite(scale) || scale <= 0 || !CinematicCloseupPolicy.Finite(height) || height <= 0 ||
                !CinematicCloseupPolicy.Finite(radius) || radius < 0) return 0;
            return Math.Max(scale * .65f, Math.Max(Math.Max(height, 1.4f) * 2.4f, radius * 1.9f));
        }
    }
}
