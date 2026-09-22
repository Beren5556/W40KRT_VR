using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    // A synchronous native card build adds many children to the same groups.
    // Rebuild each group once, before the surrounding native section measures
    // it. No result, model, texture, animation or reactive update is cached.
    internal sealed class NativeCardLayoutBatch : IDisposable
    {
        static NativeCardLayoutBatch current;
        readonly NativeCardLayoutBatch previous;
        readonly List<RectTransform> pending = new List<RectTransform>();
        readonly HashSet<RectTransform> queued = new HashSet<RectTransform>();
        bool disposed;
        readonly long started76;
        internal static long BuildTicks76,MeasuredBuilds76;
        internal static long Requests, Coalesced, Rebuilds, LayoutTicks76;
        internal static bool MeasureCosts76;
        internal static bool Active => current != null;
        internal NativeCardLayoutBatch() { previous = current; current = this; if(previous==null&&MeasureCosts76)started76=System.Diagnostics.Stopwatch.GetTimestamp(); }
        internal static bool Queue(RectTransform root)
        {
            if (current == null || root == null) return false;
            ++Requests;
            if (current.queued.Add(root)) current.pending.Add(root); else ++Coalesced;
            return true;
        }
        internal static void FlushCurrent() { current?.Flush(); }
        internal static void Measure(RectTransform root)
        {
            // A section measurement traverses all its children itself. Rebuilding
            // every queued child first repeats that same hierarchy N times.
            if(current!=null&&root!=null)
            {
                for(int i=current.pending.Count-1;i>=0;--i)
                {
                    var child=current.pending[i];
                    if(child==null||child==root||child.IsChildOf(root))
                    {current.pending.RemoveAt(i);current.queued.Remove(child);++Coalesced;}
                }
                current.Flush();
            }
            if(root!=null){Rebuild76(root);}
        }
        void Flush()
        {
            if (pending.Count == 0) return;
            // Local copy permits a layout callback to open another native card.
            var roots = pending.ToArray(); pending.Clear(); queued.Clear();
            Array.Sort(roots, (a, b) => Depth(b).CompareTo(Depth(a)));
            foreach (var root in roots)
            {
                if(root==null)continue;
                bool covered=false;
                foreach(var parent in roots)
                    if(parent!=null&&parent!=root&&root.IsChildOf(parent)){covered=true;break;}
                if(covered){++Coalesced;continue;}
                Rebuild76(root);
            }
        }
        static void Rebuild76(RectTransform root)
        {
            long start=MeasureCosts76?System.Diagnostics.Stopwatch.GetTimestamp():0;
            try{LayoutRebuilder.ForceRebuildLayoutImmediate(root);++Rebuilds;}
            finally{if(start!=0)LayoutTicks76+=System.Diagnostics.Stopwatch.GetTimestamp()-start;}
        }
        static int Depth(Transform root)
        { int depth = 0; for (var node = root; node != null; node = node.parent) ++depth; return depth; }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { Flush(); }
            finally { if(started76!=0){BuildTicks76+=System.Diagnostics.Stopwatch.GetTimestamp()-started76;++MeasuredBuilds76;} current = previous; pending.Clear(); queued.Clear(); }
        }
    }
}
