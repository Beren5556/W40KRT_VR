using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        sealed class TacticalPath58
        {
            internal object[] Nodes;
            internal Vector3[] Positions;
            internal Transform Target;
            internal bool Flag,Valid,Prepared;
            internal Vector3 Offset;
            internal object SurfaceConfig,SpaceConfig,Mode,Graph;
            internal long Mutation;
        }
        static ConditionalWeakTable<object,TacticalPath58> _tacticalPaths58=new ConditionalWeakTable<object,TacticalPath58>();
        static Func<object,object> _tacticalSurface58,_tacticalSpace58,_tacticalMode58;
        static Func<object,bool> _tacticalShown58;
        static Func<object> _tacticalGraph58;
        static Func<object,Vector3> _tacticalPosition58;
        static void InstallTacticalGeometry58()
        {
            var type=AccessTools.TypeByName("Kingmaker.UI.SurfaceCombatHUD.CombatHudPathRenderer");
            _tacticalSurface58=EngineBoxedField58(AccessTools.Field(type,"m_SurfaceCombatConfig"));
            _tacticalSpace58=EngineBoxedField58(AccessTools.Field(type,"m_SpaceCombatConfig"));
            _tacticalMode58=EngineBoxedField58(AccessTools.Field(type,"m_UseSpaceCombatConfig"));
            _tacticalShown58=(Func<object,bool>)TouchSelectionCallFactory.Build(typeof(Func<object,bool>),AccessTools.PropertyGetter(type,"PathShown"));
            _tacticalGraph58=(Func<object>)TouchSelectionCallFactory.Build(typeof(Func<object>),EngineMethod58("Kingmaker.UI.SurfaceCombatHUD.CombatHudGraphDataSource","FindGraph",0));
            _tacticalPosition58=(Func<object,Vector3>)TouchSelectionCallFactory.Build(typeof(Func<object,Vector3>),
                AccessTools.PropertyGetter(AccessTools.TypeByName("Pathfinding.GraphNode"),"Vector3Position"));
            EnginePatch58(5,EngineMethod58(type.FullName,"Show",4),nameof(TacticalPathPrefix58),nameof(TacticalPathPostfix58));
            EnginePatch58(5,EngineMethod58(type.FullName,"EnsurePrerequisites",1),postfix:nameof(TacticalPrepared58));
            EngineObserve58(5,EngineMethod58(type.FullName,"Show",4));
            foreach(var method in type.GetMethods(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic))
                if(method.DeclaringType==type&&(method.Name.Contains("OnArea")||method.Name=="Hide"||method.Name=="OnDisable"||method.Name=="OnDestroy"))EngineInvalidateAt58(method);
        }
        static bool TacticalPathPrefix58(object __instance,object __0,Transform __1,bool __2,Vector3 __3,out TacticalPath58 __state)
        {
            __state=null;if(!Engine58(5)||!(__0 is IList nodes)||nodes.Count>2048)return true;
            try
            {
                var value=_tacticalPaths58.GetOrCreateValue(__instance);++_engineBlocks58[5].Calls;
                object surface=_tacticalSurface58(__instance),space=_tacticalSpace58(__instance),mode=_tacticalMode58(__instance),graph=_tacticalGraph58();
                bool equal=value.Valid&&value.Mutation==_engineMutation58&&value.Target==__1&&value.Flag==__2&&value.Offset.Equals(__3)&&
                    ReferenceEquals(graph,value.Graph)&&graph!=null&&Equals(surface,value.SurfaceConfig)&&Equals(space,value.SpaceConfig)&&Equals(mode,value.Mode)&&value.Nodes.Length==nodes.Count;
                if(equal)for(int i=0;i<nodes.Count;i++)if(!ReferenceEquals(value.Nodes[i],nodes[i])||!value.Positions[i].Equals(_tacticalPosition58(nodes[i]))){equal=false;break;}
                if(equal){++_engineBlocks58[5].Reused;return false;}
                value.Valid=false;value.Prepared=false;value.Target=__1;value.Flag=__2;value.Offset=__3;value.SurfaceConfig=surface;value.SpaceConfig=space;value.Mode=mode;value.Graph=graph;
                value.Mutation=_engineMutation58;
                if(value.Nodes==null||value.Nodes.Length!=nodes.Count){value.Nodes=new object[nodes.Count];value.Positions=new Vector3[nodes.Count];}
                for(int i=0;i<nodes.Count;i++){value.Nodes[i]=nodes[i];value.Positions[i]=_tacticalPosition58(nodes[i]);}
                __state=value;return true;
            }
            catch(Exception e){EngineFail58(5,e);return true;}
        }
        static void TacticalPathPostfix58(object __instance,TacticalPath58 __state)
        {
            // Show can return without creating its service while a graph is
            // loading. Do not remember that as a successfully submitted path.
            if(__state!=null)__state.Valid=__state.Prepared&&__state.Graph!=null&&_tacticalShown58(__instance)==(__state.Nodes.Length>0);
        }
        static void TacticalPrepared58(object __instance,bool __result)
        {if(Engine58(5)&&_tacticalPaths58.TryGetValue(__instance,out var value))value.Prepared=__result;}
        static Func<object,object> EngineBoxedField58(FieldInfo field)
        {
            if(field==null||field.IsStatic)throw new MissingFieldException("Native instance field required");
            var method=new DynamicMethod("RTVR_EngineField58",typeof(object),new[]{typeof(object)},typeof(Main),true);
            var il=method.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Castclass,field.DeclaringType);il.Emit(OpCodes.Ldfld,field);
            if(field.FieldType.IsValueType)il.Emit(OpCodes.Box,field.FieldType);il.Emit(OpCodes.Ret);
            return (Func<object,object>)method.CreateDelegate(typeof(Func<object,object>));
        }
    }
}
