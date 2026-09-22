using System;
using System.Collections.Generic;

namespace RTMaquetaXR
{
    internal struct TouchHeadVisibilityScope { internal int Epoch; internal bool Entered, PreviousHidden; }
    // CPU-only ownership ledger. Rendering toggles allocate nothing, preserve
    // pre-existing hidden flags and unwind nested camera/exception scopes.
    internal sealed class TouchHeadVisibilityState<T> where T : class
    {
        sealed class Entry { internal T Item; internal bool Changed; }
        readonly List<Entry> entries = new List<Entry>();
        readonly Func<T,bool> alive, read;
        readonly Action<T,bool> write;
        int epoch = 1;
        internal bool Hidden { get; private set; }
        internal int Count => entries.Count;
        internal long Writes { get; private set; }
        internal long Restores { get; private set; }
        internal TouchHeadVisibilityState(Func<T,bool> alive, Func<T,bool> read, Action<T,bool> write)
        { this.alive = alive; this.read = read; this.write = write; }
        internal void Capture(IEnumerable<T> items)
        {
            Clear();
            foreach(var item in items) if(item != null && alive(item)) entries.Add(new Entry { Item=item });
        }
        internal TouchHeadVisibilityScope Begin(bool hide)
        {
            var scope = new TouchHeadVisibilityScope { Epoch=epoch, Entered=true, PreviousHidden=Hidden };
            Apply(hide); return scope;
        }
        internal void End(TouchHeadVisibilityScope scope)
        {
            if(scope.Entered && scope.Epoch==epoch) Apply(scope.PreviousHidden);
        }
        internal void Restore() { Apply(false); }
        internal void Clear()
        {
            Restore(); entries.Clear(); ++epoch;
        }
        void Apply(bool hide)
        {
            if(hide && Hidden) return;
            Hidden=hide; Exception failure=null;
            foreach(var entry in entries)
            {
                try
                {
                    if(!alive(entry.Item)) { entry.Changed=false; continue; }
                    if(hide)
                    {
                        // A renderer already hidden by the game or another mod
                        // stays outside this ledger's ownership.
                        if(!read(entry.Item)) { entry.Changed=true; write(entry.Item,true); ++Writes; }
                    }
                    else if(entry.Changed)
                    {
                        if(read(entry.Item)) { write(entry.Item,false); ++Restores; }
                        entry.Changed=false;
                    }
                }
                catch(Exception error) { if(failure==null) failure=error; }
            }
            if(failure!=null) throw failure;
        }
    }
}
