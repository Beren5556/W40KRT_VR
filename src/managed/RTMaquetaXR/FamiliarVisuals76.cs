using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    // A lease on renderers/lights of an identified native FX root, never on a
    // familiar, entity, aura rules, audio source or animation controller.
    [DefaultExecutionOrder(10000)]
    internal sealed class FamiliarFxVisual76 : MonoBehaviour
    {
        Renderer[] renderers;Light[] lights;bool[] forced,lit;bool hidden,cosmeticSubset;
        internal bool Released78;
        internal bool FullRoot78 => !cosmeticSubset;
        internal bool Dirty78;
        internal long HierarchyScans79;
        void WatchHierarchy78()
        {
            foreach(var child in GetComponentsInChildren<Transform>(true))
            {
                var watch=child.GetComponent<FamiliarHierarchy78>();
                if(watch==null)watch=child.gameObject.AddComponent<FamiliarHierarchy78>();
                watch.Owner=this;
            }
        }
        internal void Initialize(bool subset=false)
        {
            ++HierarchyScans79;
            if(renderers!=null)SetHidden(false);
            cosmeticSubset=subset;
            renderers=GetComponentsInChildren<Renderer>(true);lights=GetComponentsInChildren<Light>(true);
            if(subset)renderers=Array.FindAll(renderers,r=>r!=null&&CosmeticRenderer(r));
            forced=new bool[renderers.Length];lit=new bool[lights.Length];
            WatchHierarchy78();Dirty78=false;
        }
        static bool CosmeticRenderer(Renderer renderer)
        {
            string type=renderer.GetType().Name;
            return type=="ParticleSystemRenderer"||type=="TrailRenderer"||type=="LineRenderer";
        }
        void OnTransformChildrenChanged(){Dirty78=true;}
        // Pool activation notifies both this root and all descendant watchers.
        // Reapply known leases immediately, then coalesce their invalidations
        // into one scan in LateUpdate, before rendering the reactivated FX.
        void OnEnable(){if(renderers!=null){Dirty78=true;SetHidden(Main.HideFamiliarVisuals76);}}
        internal void SetHidden(bool value)
        {
            if(renderers==null)Initialize();
            if(value==hidden)
            {
                // Native flicker scripts can re-enable a light. Keep their
                // requested state, suppressing only its rendering this frame.
                if(hidden)for(int i=0;i<renderers.Length;i++)if(renderers[i]!=null)renderers[i].forceRenderingOff=true;
                if(hidden)for(int i=0;i<lights.Length;i++)if(lights[i]!=null&&lights[i].enabled){lit[i]=true;lights[i].enabled=false;}
                return;
            }
            hidden=value;
            for(int i=0;i<renderers.Length;i++)if(renderers[i]!=null)
            {if(value){forced[i]=renderers[i].forceRenderingOff;renderers[i].forceRenderingOff=true;}else renderers[i].forceRenderingOff=forced[i];}
            for(int i=0;i<lights.Length;i++)if(lights[i]!=null)
            {if(value){lit[i]=lights[i].enabled;lights[i].enabled=false;}else lights[i].enabled=lit[i];}
        }
        void LateUpdate()
        {
            if(Released78)return;
            if(Dirty78)Initialize(cosmeticSubset);
            SetHidden(Main.HideFamiliarVisuals76);
        }
        void OnDisable(){SetHidden(false);}
        void OnDestroy(){SetHidden(false);}
        internal int Renderers=>renderers==null?0:renderers.Length;
        internal int Lights=>lights==null?0:lights.Length;
    }
    internal sealed class FamiliarHierarchy78 : MonoBehaviour
    {
        internal FamiliarFxVisual76 Owner;
        void OnTransformChildrenChanged(){if(Owner!=null&&!Owner.Released78)Owner.Dirty78=true;}
        void OnEnable(){if(Owner!=null&&!Owner.Released78)Owner.Dirty78=true;}
    }
    public static partial class Main
    {
        static bool _familiarHooks76;
        static readonly Dictionary<object,List<FamiliarFxVisual76>> _familiarOwners76=new Dictionary<object,List<FamiliarFxVisual76>>();
        static readonly List<object> _familiarDead76=new List<object>();
        static float _familiarPrune76;
        static long _familiarRoots76;
        static bool _familiarLastHidden78;
        internal static bool HideFamiliarVisuals76=>_cfg.familiarEffectsOnY76&&CombatPresentationContext74&&!_touchY.Held;
        static void InstallFamiliarVisuals76()
        {
            if(_familiarHooks76)return;
            _familiarHooks76=true; // One attempt: optional partial failures never duplicate hooks.
            var pet=AccessTools.TypeByName("Kingmaker.Visual.Particles.SpawnPetFxOnStart");
            var area=AccessTools.TypeByName("Kingmaker.View.MapObjects.AreaEffectView");
            FamiliarHook78(pet,"TrySpawnFx",nameof(FamiliarSpawned76));
            FamiliarHook78(pet,"OnDestroy",nameof(FamiliarRemoved76),true);
            foreach(string method in new[]{"SpawnFxs","AttachManagedFx"}) FamiliarHook78(area,method,nameof(FamiliarAreaSpawned76));
            foreach(string method in new[]{"RemoveFxs","OnDisable"}) FamiliarHook78(area,method,nameof(FamiliarRemoved76),true);
            FamiliarHook78(AccessTools.TypeByName("Kingmaker.Visual.Decals.FxDecal"),"get_IsVisible",nameof(FamiliarDecalVisible78));
            InstallFamiliarLifecycle77();
            FamiliarSeed78(pet,FamiliarSpawned76);
            FamiliarSeed78(area,FamiliarAreaSpawned76);
        }

        static void FamiliarSpawned76(Component __instance)
        {
            if(__instance==null)return;
            try{RegisterFamiliarRoot76(__instance,ReadMember73(__instance,"m_SpawnedFx") as GameObject,"native pet attachment cosmetics");}
            catch(Exception e){_log.Error("[familiar76/register] "+e.Message);}
        }
        static void FamiliarAreaSpawned76(Component __instance)
        {
            if(__instance==null)return;
            try
            {
                // Persistent, attached aura of a pet, without an attack ability
                // context. Timed ground attacks/other units are not cosmetics.
                if(!(ReadMember73(__instance,"OnUnit") is bool attached)||!attached)return;
                var context=ReadMember73(__instance,"Context");
                if(context==null)return;
                var caster=ReadMember73(context,"MaybeCaster");
                if(!FamiliarEntity77(caster))return;
                bool catalogued=CataloguedFamiliarArea78(__instance);
                if(!catalogued && ReadMember73(__instance,"m_Duration")!=null)
                {FamiliarDecision77(__instance,"excluded unknown finite area blueprint="+FamiliarBlueprint78(ReadMember73(__instance,"Data")));return;}
                if(!catalogued && ReadMember73(context,"SourceAbilityContext")!=null)
                {
                    var fact=ReadMember73(ReadMember73(ReadMember73(__instance,"Data"),"SourceFact"),"Fact");
                    if(!PersistentFamiliarBuff77(fact))
                    {FamiliarDecision77(__instance,"excluded ability area without permanent familiar buff provenance");return;}
                }
                foreach(string field in new[]{"m_SpawnedFxObjects","m_ManagedFxSettingsEffects"})
                    if(ReadMember73(__instance,field) is IEnumerable roots)
                        foreach(var root in roots)RegisterFamiliarRoot76(__instance,root as GameObject,"persistent pet aura");
            }
            catch(Exception e){_log.Error("[familiar76/aura] "+e.Message);}
        }
        static void RegisterFamiliarRoot76(object owner,GameObject root,string kind,bool cosmeticSubset=false)
        {
            if(root==null||owner==null||!cosmeticSubset&&owner is Component component&&root==component.gameObject)return;
            if(!_familiarOwners76.TryGetValue(owner,out var roots)){roots=new List<FamiliarFxVisual76>();_familiarOwners76.Add(owner,roots);}
            var visual=root.GetComponent<FamiliarFxVisual76>();
            if(visual==null||visual.Released78){visual=root.AddComponent<FamiliarFxVisual76>();visual.Initialize(cosmeticSubset);}
            if(roots.Contains(visual))return;
            roots.Add(visual);++_familiarRoots76;visual.SetHidden(HideFamiliarVisuals76);
            if(DiagnosticsRecording)_log.Log("[familiar76] "+kind+" root="+root.name+" renderers="+visual.Renderers+" lights="+visual.Lights+"; simulation/audio retained");
        }
        static void FamiliarRemoved76(object __instance)
        {
            if(__instance==null||!_familiarOwners76.TryGetValue(__instance,out var roots))return;
            _familiarOwners76.Remove(__instance);
            foreach(var root in roots)if(root!=null)
            {
                bool retained=false;foreach(var other in _familiarOwners76.Values)if(other.Contains(root)){retained=true;break;}
                if(!retained){root.Released78=true;root.enabled=false;root.SetHidden(false);UnityEngine.Object.Destroy(root);}
            }
        }
        internal static void PruneFamiliarVisuals76()
        {
            bool hidden78=HideFamiliarVisuals76;
            if(hidden78!=_familiarLastHidden78)
            {
                _familiarLastHidden78=hidden78;
                if(AllDiagnosticsEnabled)_log.Log("[familiar78/presentation] hidden="+hidden78+" Y="+_touchY.Held+" owners="+_familiarOwners76.Count);
            }
            if(Time.unscaledTime<_familiarPrune76)return;_familiarPrune76=Time.unscaledTime+2;
            _familiarDead76.Clear();
            foreach(var pair in _familiarOwners76)
            {
                pair.Value.RemoveAll(x=>x==null);
                if(pair.Key is UnityEngine.Object owner&&owner==null||pair.Value.Count==0)_familiarDead76.Add(pair.Key);
            }
            foreach(var key in _familiarDead76)FamiliarRemoved76(key);
        }
        static object FamiliarVisualSnapshot76()
        {
            int renderers=0,lights=0;
            foreach(var owner in _familiarOwners76.Values)foreach(var root in owner)if(root!=null){renderers+=root.Renderers;lights+=root.Lights;}
            return new{Enabled=_cfg.familiarEffectsOnY76,Hidden=HideFamiliarVisuals76,Owners=_familiarOwners76.Count,RegisteredRoots=_familiarRoots76,Renderers=renderers,Lights=lights,Contracts=new Dictionary<string,bool>(_familiarContracts78),SimulationAndAudioRetained=true};
        }
        static void RestoreFamiliarVisuals76()
        {foreach(var pair in _familiarOwners76)foreach(var root in pair.Value)if(root!=null)root.SetHidden(false);}
    }
}
