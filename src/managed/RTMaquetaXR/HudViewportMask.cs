using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    // A desktop UI camera clips everything at its viewport. A world-space
    // Canvas has no such edge: offscreen portrait frames and animated windows
    // can appear in the wider OpenXR capture. Restore that boundary on the GPU.
    // The ordinary RectMask2D loops over all Graphics and writes softness every
    // frame; this viewport only publishes its rect when changed/newly bound.
    // It never calls Cull or changes native visibility, alpha or materials.
    public sealed class HudViewportMask : RectMask2D
    {
        readonly HashSet<IClippable> targets = new HashSet<IClippable>();
        readonly HashSet<IClippable> pending = new HashSet<IClippable>();
        readonly List<IClippable> work = new List<IClippable>();
        Rect previous;
        bool prepared;
        internal long ClipWrites, SteadyFrames;

        internal void Registered(IClippable value)
        {
            if (value == null) return;
            targets.Add(value);
            // Re-registering the same pooled Graphic can clear its native
            // clipping without changing identity, so it must be refreshed too.
            pending.Add(value);
        }
        internal void Unregistered(IClippable value)
        { if (value != null) { targets.Remove(value); pending.Remove(value); } }

        public override void PerformClipping()
        {
            if (!isActiveAndEnabled) return;
            Rect current = canvasRect;
            // Root world-to-local rounding can vary while chart mode follows
            // the head. Sub-pixel noise must not republish every HUD Graphic.
            bool changed = !prepared || System.Math.Abs(current.x - previous.x) > .03125f ||
                System.Math.Abs(current.y - previous.y) > .03125f ||
                System.Math.Abs(current.width - previous.width) > .03125f ||
                System.Math.Abs(current.height - previous.height) > .03125f;
            if (!changed && pending.Count == 0) { ++SteadyFrames; return; }
            if (!changed) current = previous;
            work.Clear();
            foreach (var target in changed ? targets : pending) work.Add(target);
            pending.Clear(); previous = current; prepared = true;
            foreach (var target in work)
            {
                // Custom game callbacks may synchronously rebind another item;
                // enumerate the reusable snapshot and honor removals.
                if (!targets.Contains(target)) continue;
                if (target is Object native && native == null)
                { targets.Remove(target); continue; }
                target.SetClipRect(current, true);
                ++ClipWrites;
            }
            work.Clear();
        }

        protected override void OnDisable()
        {
            // Native mask teardown restores each Graphic's ordinary clip parent.
            base.OnDisable();
            targets.Clear(); pending.Clear(); work.Clear(); prepared = false;
        }
    }
}
