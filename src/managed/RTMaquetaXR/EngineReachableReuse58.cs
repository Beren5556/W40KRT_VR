using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        internal struct ReachableKey58 : IEquatable<ReachableKey58>
        {
            internal object Agent;internal Vector3 Origin;internal float Points;internal bool Threat;internal long Mutation;
            public bool Equals(ReachableKey58 k)=>ReferenceEquals(Agent,k.Agent)&&Origin.Equals(k.Origin)&&Points.Equals(k.Points)&&Threat==k.Threat&&Mutation==k.Mutation;
            public override bool Equals(object value)=>value is ReachableKey58 k&&Equals(k);
            public override int GetHashCode()=>unchecked(RuntimeHelpers.GetHashCode(Agent)*397^Origin.GetHashCode()^Points.GetHashCode()^Threat.GetHashCode()^Mutation.GetHashCode());
        }
        public struct ReachableCall58 {internal ReachableKey58 Key;internal bool Store;}
        static readonly Dictionary<ReachableKey58,object> _reachable58=new Dictionary<ReachableKey58,object>();
        static int _reachableFrame58=-1,_reachableDepth58;
        static void InstallReachableReuse58()
        {
            var visual=EngineMethod58("Kingmaker.Controllers.Units.UnitMovableAreaController","GetMovableArea",2);
            EnginePatch58(1,visual,nameof(ReachableVisualStart58),finalizer:nameof(ReachableVisualEnd58));
            var search=EngineMethod58("Kingmaker.Pathfinding.PathfindingService","FindAllReachableTiles_Blocking",4);
            var generic=search.ReturnType.GetGenericArguments();
            if(generic.Length!=2)throw new InvalidOperationException("Reachable node map changed");
            EngineConcreteHooks58.Patch(_harmony,search,AccessTools.Method(typeof(Main),nameof(ReachablePrefix58)).MakeGenericMethod(generic),AccessTools.Method(typeof(Main),nameof(ReachablePostfix58)).MakeGenericMethod(generic));
            foreach(string name in new[]{"HandleUnitMovement","HandleUnitSpawned","ClearChargePathCache"})EngineInvalidateAt58(EngineMethod58("Kingmaker.Pathfinding.PathfindingService",name));
            EnginePatch58(1,EngineMethod58("Kingmaker.Pathfinding.PathfindingService","ClearChargePathCache"),postfix:nameof(ClearReachableReuse58));
            foreach(var method in visual.DeclaringType.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                if(method.Name.StartsWith("Handle",StringComparison.Ordinal)&&method.Name!="HandlePlayerInputUnlocked"&&method.Name!="HandlePlayerInputLocked"&&method.Name!="HandleTurnBasedModeResumed")EngineInvalidateAt58(method);
            foreach(string name in new[]{"set_ActionPointsBlue","set_ActionPointsBlueMax","set_ActionPointsYellow"})
                EngineInvalidateAt58(EngineMethod58("Kingmaker.Controllers.Combat.PartUnitCombatState",name,1));
            foreach(string name in new[]{"set_Walkable","set_Penalty","set_Tag"})EngineInvalidateAt58(EngineMethod58("Pathfinding.GraphNode",name,1));
            InvalidateAppliedEffects58();
            EngineObserve58(1,search);
        }
        static void ReachableVisualStart58(out int __state){__state=_reachableDepth58;if(Engine58(1))++_reachableDepth58;}
        static Exception ReachableVisualEnd58(Exception __exception,int __state){_reachableDepth58=__state;return __exception;}
        public static bool ReachablePrefix58<TNode,TCell>(object __0,Vector3 __1,float __2,bool __3,ref Dictionary<TNode,TCell> __result,out ReachableCall58 __state)
        {
            __state=default;if(!Engine58(1)||_reachableDepth58==0||__0==null)return true;
            if(_reachableFrame58!=Time.frameCount){_reachable58.Clear();_reachableFrame58=Time.frameCount;}
            var key=new ReachableKey58{Agent=__0,Origin=__1,Points=__2,Threat=__3,Mutation=_engineMutation58};++_engineBlocks58[1].Calls;
            if(_reachable58.TryGetValue(key,out var value)&&value is Dictionary<TNode,TCell> nodes)
            {
                // Only native visual callers qualify. Return a separate map:
                // no caller can corrupt the cached map or authorize an order.
                __result=new Dictionary<TNode,TCell>(nodes,nodes.Comparer);++_engineBlocks58[1].Reused;return false;
            }
            __state=new ReachableCall58{Key=key,Store=_reachable58.Count<8};return true;
        }
        public static void ReachablePostfix58<TNode,TCell>(Dictionary<TNode,TCell> __result,ReachableCall58 __state)
        {
            if(__state.Store&&__result!=null&&__result.Count<=8192&&__state.Key.Mutation==_engineMutation58)
                _reachable58[__state.Key]=new Dictionary<TNode,TCell>(__result,__result.Comparer);
        }
        static void ClearReachableReuse58(){_reachable58.Clear();_reachableFrame58=-1;}
    }
}
