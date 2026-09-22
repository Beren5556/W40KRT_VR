using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    // Each true is paired with a false on the same native view. The native
    // CountingGuard retains mouse hover/other owners; never overwrite its count.
    internal sealed class TouchLootHighlightLease65
    {
        readonly HashSet<object> held = new HashSet<object>();
        readonly HashSet<object> next = new HashSet<object>();
        readonly List<object> removed = new List<object>();
        internal int Count => held.Count;
        internal void Refresh(IEnumerable entities, Func<object, object> view,
            Func<object, bool> eligible, Action<object, bool> write)
        {
            next.Clear();
            if (entities != null)
                foreach (object entity in entities)
                {
                    if (entity == null || !eligible(entity)) continue;
                    object target = view(entity);
                    if (target == null || !next.Add(target) || held.Contains(target)) continue;
                    write(target, true); held.Add(target);
                }
            removed.Clear();
            foreach (object target in held) if (!next.Contains(target)) removed.Add(target);
            foreach (object target in removed) { held.Remove(target); write(target, false); }
            removed.Clear();
        }
        internal void Release(Action<object, bool> write, Action<Exception> error)
        {
            foreach (object target in held)
                try { write(target, false); } catch (Exception e) { error(e); }
            held.Clear(); next.Clear(); removed.Clear();
        }
    }

    internal sealed class TouchLootHighlightContracts65
    {
        internal Func<object> Game;
        internal PcUiPath Pool;
        internal Func<object, object> View;
        internal Func<object, bool> Revealed, Fog, Perceived, Interactable;
        internal Action<object, bool> Highlight;
        internal static TouchLootHighlightContracts65 Create(Func<string, Type> find)
        {
            var game=find("Kingmaker.Game");
            var entity=find("Kingmaker.EntitySystem.Entities.MapObjectEntity");
            var view=find("Kingmaker.View.MapObjects.MapObjectView");
            return new TouchLootHighlightContracts65 {
                Game=(Func<object>)TouchSelectionCallFactory.Build(typeof(Func<object>),
                    TouchSelectionCallFactory.ExactMethod(game,"get_Instance",game,true)),
                Pool=PcUiPath.Create(game,"State","MapObjects"),
                View=PcUiPath.Getter(PcUiPath.Property(entity,"View")),
                Revealed=ReadBool(entity,"IsRevealed"), Fog=ReadBool(entity,"IsInFogOfWar"),
                Perceived=ReadBool(entity,"IsAwarenessCheckPassed"),
                Interactable=ReadBool(view,"HighlightOnHover"),
                Highlight=(Action<object,bool>)TouchSelectionCallFactory.Build(typeof(Action<object,bool>),
                    TouchSelectionCallFactory.ExactMethod(view,"set_Highlighted",typeof(void),false,typeof(bool)))
            };
        }
        static Func<object,bool> ReadBool(Type type,string property) =>
            (Func<object,bool>)TouchSelectionCallFactory.Build(typeof(Func<object,bool>),PcUiPath.Property(type,property).GetGetMethod(true));
        internal bool Eligible(object entity)
        {
            var view=View(entity) as Component;
            return view!=null && view.gameObject.activeInHierarchy && Revealed(entity) && !Fog(entity) && Perceived(entity) && Interactable(view);
        }
        internal void Set(object view,bool value)
        {
            if(view is Component component && component!=null) Highlight(view,value);
        }
    }

    public static partial class Main
    {
        static TouchLootHighlightContracts65 _touchLootContracts65;
        static readonly TouchLootHighlightLease65 _touchLootLease65=new TouchLootHighlightLease65();
        static float _touchLootRefresh65;
        static string _touchLootFault65;
        static void InstallTouchLootHighlight65()
        {
            try { _touchLootContracts65=TouchLootHighlightContracts65.Create(AccessTools.TypeByName); }
            catch(Exception error) { _touchLootContracts65=null; ReportTouchLootHighlight65(error); }
        }
        static void UpdateTouchLootHighlight65()
        {
            var c=_touchLootContracts65; if(c==null)return;
            if(_touchHighlightOwner!=null && Time.unscaledTime<_touchLootRefresh65)return;
            try
            {
                var pool=c.Pool.Read(c.Game()) as IEnumerable;
                if(pool==null) { RestoreTouchExplorationHighlight(); return; }
                if(!ReferenceEquals(pool,_touchHighlightOwner))
                { RestoreTouchExplorationHighlight(); _touchHighlightOwner=pool; ++_touchHighlightToggles; }
                _touchLootLease65.Refresh(pool,c.View,c.Eligible,c.Set);
                _touchLootRefresh65=Time.unscaledTime+.25f;
            }
            catch(Exception error) { RestoreTouchExplorationHighlight(); ReportTouchLootHighlight65(error); }
        }
        static void ReleaseTouchLootHighlight65()
        {
            var c=_touchLootContracts65;
            if(c!=null)_touchLootLease65.Release(c.Set,ReportTouchLootHighlight65);
            _touchLootRefresh65=0;
        }
        static void ReportTouchLootHighlight65(Exception error)
        {
            if(_touchLootFault65==error.Message)return;
            _touchLootFault65=error.Message; _log.Error("[touch/loot-highlight] "+error.Message);
        }
    }
}
