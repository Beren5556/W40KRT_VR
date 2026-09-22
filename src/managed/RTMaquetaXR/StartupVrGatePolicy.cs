using System;

namespace RTMaquetaXR
{
    // One initial admission gate. Runtime focus loss or an ordinary later load
    // never re-arms it and can therefore never strand gameplay behind a menu.
    internal sealed class StartupVrGatePolicy
    {
        internal bool Completed { get; private set; }
        internal bool Bypassed { get; private set; }
        internal bool Blocking { get; private set; }
        internal double Started { get; private set; } = double.NaN;
        internal bool Update(bool automatic, bool mainMenu, bool presented, bool quitting, double now)
        {
            if (quitting || Completed || Bypassed) return Blocking = false;
            if (!automatic) return Blocking = false;
            if (presented) { Completed = true; return Blocking = false; }
            if (!mainMenu && !Blocking) return false;
            if (double.IsNaN(Started)) Started = now;
            return Blocking = true;
        }
        internal bool EscapeAvailable(double now) => Blocking && !double.IsNaN(Started) && now - Started >= 8;
        internal void Bypass() { Bypassed = true; Blocking = false; }
    }
}
