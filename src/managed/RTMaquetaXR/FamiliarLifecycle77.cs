using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Func<object,object> _familiarPart77;
        static void InstallFamiliarLifecycle77()
        {
            try
            {
                var entity=AccessTools.TypeByName("Kingmaker.EntitySystem.Entities.Base.Entity");
                var part=AccessTools.TypeByName("Kingmaker.UnitLogic.Parts.UnitPartFamiliar");
                foreach(var method in entity.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
                    if(method.Name=="GetOptional"&&method.IsGenericMethodDefinition&&method.GetGenericArguments().Length==1&&method.GetParameters().Length==0)
                        _familiarPart77=(Func<object,object>)TouchSelectionCallFactory.Build(typeof(Func<object,object>),method.MakeGenericMethod(part));
                if(_familiarPart77==null)throw new MissingMethodException("Entity.GetOptional<UnitPartFamiliar>()");
            }
            catch(Exception error){_log.Error("[familiar78/identity] "+error.Message);}
            var familiar=AccessTools.TypeByName("Kingmaker.Visual.Critters.FamiliarUnit");
            foreach(string name in new[]{"OnEnable","OnAreaLoadingComplete"}) FamiliarHook78(familiar,name,nameof(FamiliarUnitReady77));
            FamiliarHook78(familiar,"OnDisable",nameof(FamiliarRemoved76),true);
            var buff=AccessTools.TypeByName("Kingmaker.UnitLogic.Buffs.Buff");
            foreach(string name in new[]{"SpawnParticleEffect","SpawnFxFromBuffComponent"}) FamiliarHook78(buff,name,nameof(FamiliarBuffReady77));
            FamiliarHook78(buff,"ClearParticleEffect",nameof(FamiliarRemoved76),true);
            FamiliarSeed78(familiar,FamiliarUnitReady77);
        }

        static bool FamiliarEntity77(object entity)
        {
            if(entity==null)return false;
            if(ReadMember73(entity,"Master")!=null)return true;
            if(_familiarPart77==null)return false;
            try {return ReadMember73(_familiarPart77(entity),"Leader")!=null;}
            catch{return false;}
        }
        static void FamiliarUnitReady77(Component __instance)
        {
            if(__instance==null||!FamiliarEntity77(ReadMember73(__instance,"Unit")))return;
            // The native familiar root includes its model. Only particle/trail
            // renderers and lights belong to this cosmetic lease.
            RegisterFamiliarRoot76(__instance,__instance.gameObject,"familiar particle/trail/light cosmetics",true);
            var unit=ReadMember73(__instance,"Unit");
            var buffs=ReadMember73(unit,"Buffs");
            if((ReadMember73(buffs,"Enumerable")??buffs) is IEnumerable items)
                foreach(var item in items)FamiliarBuffReady77(item);
        }
        static void FamiliarBuffReady77(object __instance)
        {
            if(__instance==null)return;
            try
            {
                if(!PersistentFamiliarBuff77(__instance))return;
                foreach(string field in new[]{"m_ManagedEffects","m_TrailFxObjects"})
                    if(ReadMember73(__instance,field) is IEnumerable roots)
                        foreach(var root in roots)RegisterFamiliarRoot76(__instance,root as GameObject,"persistent familiar buff visual");
            }
            catch(Exception error){if(AllDiagnosticsEnabled)_log.Error("[familiar77/buff] "+error.Message);}
        }
        static bool PersistentFamiliarBuff77(object buff)
        {
            if(buff==null)return false;
            var context=ReadMember73(buff,"MaybeContext")??ReadMember73(buff,"Context");
            bool caster=FamiliarEntity77(ReadMember73(context,"MaybeCaster"));
            bool owner=FamiliarEntity77(ReadMember73(buff,"Owner"));
            if(!caster&&!owner)return false;
            if(FamiliarBlueprint78(buff)==FamiliarAuraBuff78)
            {FamiliarDecision77(buff,"catalogued continuous servoskull aura (combat lifetime)");return true;}
            bool permanent=ReadMember73(buff,"IsPermanent") is bool value&&value;
            bool retained=ReadMember73(buff,"EndCondition")?.ToString()=="RemainAfterCombat";
            bool eligible=permanent&&retained&&(caster||ReadMember73(context,"SourceAbilityContext")==null);
            FamiliarDecision77(buff,eligible?"permanent familiar buff":"excluded timed, combat-only or foreign ability buff");
            return eligible;
        }
        static void FamiliarDecision77(object owner,string reason)
        {
            if(AllDiagnosticsEnabled)_log.Log("[familiar77/eligibility] "+owner.GetType().FullName+": "+reason);
        }
    }
}
