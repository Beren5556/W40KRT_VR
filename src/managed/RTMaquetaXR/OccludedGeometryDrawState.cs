using System;
using System.Collections.Generic;

namespace RTMaquetaXR
{
    internal struct OccludedDrawScope { internal int Epoch; internal bool Entered, PreviousHidden; internal object PreviousOwner; }
    // Values mirror Unity ShadowCastingMode: Off=0, On=1, TwoSided=2,
    // ShadowsOnly=3. TwoSided is deliberately excluded: converting it to
    // ShadowsOnly would change its back-face shadow-casting behavior.
    internal sealed class OccludedGeometryDrawState<T> where T : class
    {
        sealed class Entry
        {
            internal T Item; internal object Owner; internal int OriginalShadow;
            internal bool ShadowChanged, ForceChanged;
        }
        readonly Dictionary<T, Entry> entries = new Dictionary<T, Entry>();
        readonly List<T> remove = new List<T>();
        readonly Func<T, bool> alive, eligible, readForce;
        readonly Func<T, int> readShadow;
        readonly Action<T, bool> writeForce;
        readonly Action<T, int> writeShadow;
        int epoch = 1;
        object currentOwner;
        internal bool Hidden { get; private set; }
        internal int Count => entries.Count;
        internal int PendingRestores
        { get { int count=0;foreach(var entry in entries.Values) if(entry.ForceChanged || entry.ShadowChanged) ++count;return count; } }
        internal long Writes { get; private set; }
        internal long Restores { get; private set; }
        internal OccludedGeometryDrawState(Func<T, bool> alive, Func<T, bool> eligible,
            Func<T, bool> readForce, Action<T, bool> writeForce,
            Func<T, int> readShadow, Action<T, int> writeShadow)
        { this.alive=alive; this.eligible=eligible; this.readForce=readForce; this.writeForce=writeForce; this.readShadow=readShadow; this.writeShadow=writeShadow; }

        internal void Observe(T item, object owner, bool exactlyZero)
        {
            if (item == null) return;
            if (entries.TryGetValue(item, out var existing))
            {
                if (exactlyZero && ReferenceEquals(existing.Owner,owner)) return;
                Restore(existing); entries.Remove(item);
            }
            if (exactlyZero && alive(item)) entries.Add(item,new Entry { Item=item,Owner=owner });
        }
        internal void RemoveOwner(object owner)
        {
            remove.Clear();
            foreach(var pair in entries) if(ReferenceEquals(pair.Value.Owner,owner))
            { Restore(pair.Value); remove.Add(pair.Key); }
            foreach(var key in remove) entries.Remove(key);
            remove.Clear();
        }
        internal OccludedDrawScope Begin(bool hide,object owner=null)
        {
            if (!Hidden && (!hide || entries.Count == 0)) return default;
            var scope=new OccludedDrawScope { Epoch=epoch,Entered=true,PreviousHidden=Hidden,PreviousOwner=currentOwner };
            SetHidden(hide,owner); return scope;
        }
        internal void End(OccludedDrawScope scope)
        { End(scope,scope.PreviousOwner); }
        internal void End(OccludedDrawScope scope,object liveOwner)
        {
            if(!scope.Entered || scope.Epoch!=epoch) return;
            // A service can be replaced while a nested non-eye camera suspends
            // the hide. Never revive the previous area's entries on its return.
            if(scope.PreviousHidden && !ReferenceEquals(scope.PreviousOwner,liveOwner))
            { InvalidateScopes(); return; }
            SetHidden(scope.PreviousHidden,scope.PreviousOwner);
        }
        void SetHidden(bool hide,object owner)
        {
            if(Hidden==hide && (!hide || ReferenceEquals(owner,currentOwner))) return;
            if(Hidden && hide) RestoreAll();
            if(!hide) { RestoreAll(); return; }
            // Mark ownership before applying, so failure recovery can unwind a
            // partially applied list. Rendering callbacks do not discover scene objects.
            Hidden=true;currentOwner=owner;remove.Clear();
            foreach(var pair in entries)
            {
                var entry=pair.Value;
                if(owner!=null && !ReferenceEquals(owner,entry.Owner)) { remove.Add(pair.Key);continue; }
                if(!alive(entry.Item)) { remove.Add(pair.Key); continue; }
                // An incompatible entry can safely wait for a new native fade
                // event. Do not recheck thousands of rejected materials per eye.
                if(!eligible(entry.Item) || readForce(entry.Item)) { remove.Add(pair.Key); continue; }
                int shadow=readShadow(entry.Item);
                entry.OriginalShadow=shadow;
                if(shadow==0)
                { entry.ForceChanged=true; writeForce(entry.Item,true); ++Writes; }
                else if(shadow==1)
                { entry.ShadowChanged=true; writeShadow(entry.Item,3); ++Writes; }
            }
            foreach(var key in remove) entries.Remove(key);
            remove.Clear();
        }
        void Restore(Entry entry)
        {
            if(alive(entry.Item))
            {
                // Restore only values still owned by this scope. An external
                // change to a different value is never overwritten.
                if(entry.ForceChanged && readForce(entry.Item)) { writeForce(entry.Item,false); ++Restores; }
                if(entry.ShadowChanged && readShadow(entry.Item)==3) { writeShadow(entry.Item,entry.OriginalShadow); ++Restores; }
            }
            entry.ForceChanged=entry.ShadowChanged=false;
        }
        internal void RestoreAll()
        {
            Exception failure=null;
            foreach(var entry in entries.Values)
                try { Restore(entry); } catch(Exception error) { if(failure==null) failure=error; }
            Hidden=false;
            if(failure!=null) throw failure;
        }
        internal void Clear()
        {
            ++epoch;
            // Keep ownership if a Unity setter fails. The next camera or stop
            // retry must still know which renderer needs restoring.
            RestoreAll(); entries.Clear(); remove.Clear();
        }
        internal void InvalidateScopes()
        { ++epoch; RestoreAll(); }
    }
}
