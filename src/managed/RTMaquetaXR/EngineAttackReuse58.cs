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
        internal struct PatternKey58 : IEquatable<PatternKey58>
        {
            internal object Pattern, Cast, Target;
            internal Vector3 Direction;
            internal int Flags;
            internal long Mutation;
            public bool Equals(PatternKey58 k) => ReferenceEquals(Pattern,k.Pattern)&&ReferenceEquals(Cast,k.Cast)&&
                ReferenceEquals(Target,k.Target)&&Direction.Equals(k.Direction)&&Flags==k.Flags&&Mutation==k.Mutation;
            public override bool Equals(object value) => value is PatternKey58 k&&Equals(k);
            public override int GetHashCode() => unchecked((Pattern==null?0:RuntimeHelpers.GetHashCode(Pattern))*397 ^
                (Cast==null?0:RuntimeHelpers.GetHashCode(Cast))*31 ^ (Target==null?0:RuntimeHelpers.GetHashCode(Target)) ^ Direction.GetHashCode() ^ Flags ^ Mutation.GetHashCode());
        }
        internal sealed class AttackScope58
        {
            internal AttackScope58 Previous;
            internal readonly Dictionary<PatternKey58,object> Patterns=new Dictionary<PatternKey58,object>();
        }
        public struct PatternCall58 { internal AttackScope58 Scope; internal PatternKey58 Key; internal bool Store; }
        static AttackScope58 _attackScope58;
        static readonly Stack<AttackScope58> _attackPool58=new Stack<AttackScope58>();
        static readonly HashSet<MethodInfo> _engineInvalidators58=new HashSet<MethodInfo>();
        static void EngineInvalidateAt58(MethodInfo method)
        {
            if(!_engineInvalidators58.Add(method))return;
            EnginePatch58(0,method,nameof(EngineMutation58),nameof(EngineMutation58));
        }
        static void InvalidateAppliedEffects58()
        {
            var type=AccessTools.TypeByName("Kingmaker.Controllers.AbilityExecutionProcess");int matches=0;
            foreach(var method in type.GetMethods(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                if(method.Name=="DoApplyEffect"){EngineInvalidateAt58(method);++matches;}
            if(matches!=2)throw new InvalidOperationException("Native effect application overloads changed");
        }
        static HarmonyMethod EngineGeneric58(string method,Type type) => new HarmonyMethod(AccessTools.Method(typeof(Main),method).MakeGenericMethod(type));
        static void InstallAttackReuse58()
        {
            var scope=EngineMethod58("Kingmaker.Controllers.AbilityExecutionProcess","ApplyEffectHit",5);
            var pattern=EngineMethod58("Kingmaker.UnitLogic.Abilities.Components.Patterns.AoEPattern","GetOriented",9);
            var p=pattern.GetParameters();
            if(pattern.IsStatic||p[2].ParameterType!=typeof(Vector3)||pattern.ReturnType.FullName!="Kingmaker.UnitLogic.Abilities.Components.Patterns.OrientedPatternData")throw new InvalidOperationException("Native oriented pattern contract changed");
            for(int i=3;i<p.Length;i++)if(p[i].ParameterType!=typeof(bool))throw new InvalidOperationException("Native pattern flags changed");
            EnginePatch58(0,scope,nameof(AttackScopePrefix58),finalizer:nameof(AttackScopeFinal58));
            EngineConcreteHooks58.Patch(_harmony,pattern,AccessTools.Method(typeof(Main),nameof(PatternPrefix58)).MakeGenericMethod(pattern.ReturnType),AccessTools.Method(typeof(Main),nameof(PatternPostfix58)).MakeGenericMethod(pattern.ReturnType));
            InvalidateAppliedEffects58();
            EngineObserve58(0,pattern);
        }
        static void AttackScopePrefix58(out AttackScope58 __state)
        {
            __state=null;if(!Engine58(0))return;
            __state=_attackPool58.Count>0?_attackPool58.Pop():new AttackScope58();
            __state.Previous=_attackScope58;_attackScope58=__state;
        }
        static Exception AttackScopeFinal58(Exception __exception,AttackScope58 __state)
        {
            if(__state!=null)
            {
                _attackScope58=__state.Previous;__state.Previous=null;__state.Patterns.Clear();
                if(_attackPool58.Count<8)_attackPool58.Push(__state);
            }
            return __exception;
        }
        public static bool PatternPrefix58<T>(object __instance,object __0,object __1,Vector3 __2,bool __3,bool __4,bool __5,bool __6,bool __7,bool __8,
            ref T __result,out PatternCall58 __state)
        {
            __state=default;
            if(!Engine58(0)||_attackScope58==null)return true;
            var scope=_attackScope58;var key=new PatternKey58 { Pattern=__instance,Cast=__0,Target=__1,Direction=__2,
                Flags=(__3?1:0)|(__4?2:0)|(__5?4:0)|(__6?8:0)|(__7?16:0)|(__8?32:0),Mutation=_engineMutation58 };
            ++_engineBlocks58[0].Calls;
            if(scope.Patterns.TryGetValue(key,out object value)&&value is T cached)
            {__result=cached;++_engineBlocks58[0].Reused;return false;}
            __state=new PatternCall58{Scope=scope,Key=key,Store=scope.Patterns.Count<64};return true;
        }
        public static void PatternPostfix58<T>(T __result,PatternCall58 __state)
        {
            if(__state.Store&&__state.Scope!=null&&__state.Key.Mutation==_engineMutation58&&Engine58(0))__state.Scope.Patterns[__state.Key]=__result;
        }
    }
}
