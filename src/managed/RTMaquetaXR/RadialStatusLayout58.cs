using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Type _radialBuffView58,_radialBuffPart76;
        static System.Reflection.FieldInfo _radialBuffTemplate76;
        static Func<object,object> _radialBuffModel58,_radialBuffIdentity58;
        static readonly HashSet<object> _radialBuffIdentities58=new HashSet<object>(ReferenceIdentity58.Instance);
        static readonly List<Component> _radialDuplicateBuffs58=new List<Component>();
        static readonly List<Component> _radialNextDuplicateBuffs58=new List<Component>();
        static long _radialOrphanBuffs58,_radialDuplicateCount58;
        sealed class ReferenceIdentity58 : IEqualityComparer<object>
        {
            internal static readonly ReferenceIdentity58 Instance=new ReferenceIdentity58();
            public new bool Equals(object a,object b)=>ReferenceEquals(a,b);
            public int GetHashCode(object value)=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
        static void EnsureRadialBuffContracts76()
        {
            if(_radialBuffIdentity58!=null)return;
            _radialBuffPart76=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.View.Party.PC.UnitBuffPartPCView");
            _radialBuffView58=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.View.Other.BuffPCView");
            if(_radialBuffPart76==null||_radialBuffView58==null)throw new TypeLoadException("Native buff views unavailable");
            _radialBuffTemplate76=PcUiPath.Field(_radialBuffPart76,"m_BuffView");
            _radialBuffModel58=PcUiPath.Getter(PcUiPath.Property(_radialBuffView58,"ViewModel"));
            var model=PcUiPath.Property(_radialBuffView58,"ViewModel").PropertyType;
            _radialBuffIdentity58=PcUiPath.Getter(PcUiPath.Property(model,"Buff"));
        }
        static void PrepareRadialStatusPortrait58(GameObject portrait)
        {
            EnsureRadialBuffContracts76();
            var parts=_radialBuffPart76;
            var templates=new HashSet<Component>();
            var containers=new HashSet<Transform>();
            foreach(Component part in portrait.GetComponentsInChildren(parts,true))
            {
                containers.Add(part.transform);
                if(_radialBuffTemplate76.GetValue(part) is Component template)
                {templates.Add(template);if(template.transform.parent!=null)containers.Add(template.transform.parent);}
            }
            RadialBuffHierarchyChanged74();
            // A live clone must not inherit materialized pooled icons which
            // its fresh VM would recreate. Preserve the actual native templates.
            foreach(Component buff in portrait.GetComponentsInChildren(_radialBuffView58,true))
                if(!templates.Contains(buff))
                {
                    if(buff.transform.parent!=null)containers.Add(buff.transform.parent);
                    buff.gameObject.SetActive(false);
                    // Destroy is deferred until end of frame. Detach the cloned
                    // runtime icon now so native Initialize cannot discover it.
                    buff.transform.SetParent(null,false);UnityEngine.Object.Destroy(buff.gameObject);++_radialOrphanBuffs58;
                }
            // Observe insertion containers, not every Image/Text/ornament in
            // every buff. The model fingerprint already detects identity changes.
            foreach(var node in containers)
                if(node!=null&&node.IsChildOf(portrait.transform)&&node.GetComponent<RadialBuffHierarchy74>()==null)
                    node.gameObject.AddComponent<RadialBuffHierarchy74>();

        }
        static void RefreshRadialStatusIdentity58()
        {
            if(_radialStatusPortrait==null||_radialBuffModel58==null||!RadialBuffsChanged74())return;
            // Restore only icons hidden by this pass. Never deduplicate images
            // or blueprints: separate effects/stacks can legitimately share art.
            _radialNextDuplicateBuffs58.Clear();_radialBuffIdentities58.Clear();
            foreach(Component view in _radialStatusPortrait.GetComponentsInChildren(_radialBuffView58,true))
            {
                bool hidden=_radialDuplicateBuffs58.Contains(view);
                if(!view.gameObject.activeInHierarchy&&!hidden)continue;
                var model=_radialBuffModel58(view);var identity=model==null?null:_radialBuffIdentity58(model);
                if(identity==null)continue;
                if(!_radialBuffIdentities58.Add(identity))
                {if(view.gameObject.activeSelf){view.gameObject.SetActive(false);++_radialDuplicateCount58;}_radialNextDuplicateBuffs58.Add(view);}
                else if(hidden)view.gameObject.SetActive(true);
            }
            _radialDuplicateBuffs58.Clear();_radialDuplicateBuffs58.AddRange(_radialNextDuplicateBuffs58);
        }
    }
}
