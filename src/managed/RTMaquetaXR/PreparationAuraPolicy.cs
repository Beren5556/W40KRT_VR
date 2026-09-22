using System;
using System.Collections.Generic;

namespace RTMaquetaXR
{
    // Only visual spawn/clear operations are injected. This controller cannot
    // add/remove a fact, advance a turn, change selection or modify a blueprint.
    internal sealed class PreparationAuraPolicy
    {
        readonly Func<object, bool> matches, alive;
        readonly Action<object> clear, spawn;
        readonly HashSet<object> hidden = new HashSet<object>();
        readonly List<object> restore = new List<object>();
        internal bool Suppressed { get; private set; }
        internal long Prevented { get; private set; }
        internal long Cleared { get; private set; }
        internal long Restored { get; private set; }
        internal int Hidden => hidden.Count;
        internal PreparationAuraPolicy(Func<object, bool> matches, Func<object, bool> alive, Action<object> clear, Action<object> spawn)
        { this.matches = matches; this.alive = alive; this.clear = clear; this.spawn = spawn; }
        internal bool BeforeSpawn(object buff)
        {
            if (!Suppressed || buff == null || !matches(buff)) return true;
            hidden.Add(buff); ++Prevented; return false;
        }
        internal void Forget(object buff) { if (buff != null) hidden.Remove(buff); }
        internal void Set(bool suppressed, IEnumerable<object> existing = null)
        {
            if (Suppressed == suppressed) return;
            Suppressed = suppressed;
            if (suppressed)
            {
                if (existing == null) return;
                foreach (var buff in existing)
                {
                    if (buff == null || !matches(buff) || !alive(buff) || !hidden.Add(buff)) continue;
                    // Track before invoking external code so a partial failure
                    // can still restore the buff's native visuals on teardown.
                    clear(buff); ++Cleared;
                }
                return;
            }
            restore.Clear(); restore.AddRange(hidden); hidden.Clear();
            Exception failure = null;
            try
            {
                foreach (var buff in restore)
                    try { if (alive(buff)) { spawn(buff); ++Restored; } }
                    catch (Exception error) { if (failure == null) failure = error; }
            }
            finally { restore.Clear(); }
            if (failure != null) throw failure;
        }
    }
}
