using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    // Structural changes invalidate the cached native FX component lists.
    public sealed class EngineFxHierarchy58 : MonoBehaviour
    {
        internal Action Changed;
        internal bool Root;
        void OnTransformChildrenChanged(){Changed?.Invoke();}
        void OnTransformParentChanged(){if(!Root)Changed?.Invoke();}
        void OnDestroy(){Changed?.Invoke();}
    }
    public static partial class Main
    {
        sealed class FxEntry58
        {
            internal GameObject Root;
            internal bool Dirty;
            internal readonly Dictionary<Type,object> Lists=new Dictionary<Type,object>();
            internal readonly List<EngineFxHierarchy58> Watches=new List<EngineFxHierarchy58>();
            internal void Invalidate(){Dirty=true;Lists.Clear();}
            internal void Release(){foreach(var watch in Watches)if(watch!=null){watch.Changed=null;UnityEngine.Object.Destroy(watch);}Watches.Clear();Lists.Clear();}
        }
        static readonly Dictionary<int,FxEntry58> _engineFx58=new Dictionary<int,FxEntry58>();
        static readonly Queue<int> _engineFxOrder58=new Queue<int>();
        static readonly HashSet<string> _fxStableComponents58=new HashSet<string>{
            "Kingmaker.Visual.Particles.HighlightAnimation","Kingmaker.Visual.FX.BleedingEffect.BleedingEffectSetup"};
        static void InstallFxReuse58()
        {
            EnginePatch58(4,EngineMethod58("Kingmaker.Visual.Particles.FxHelper","SpawnFxOnGameObject",5),transpiler:nameof(FxComponentsTranspiler58));
            EngineObserve58(4,EngineMethod58("Kingmaker.Visual.Particles.FxHelper","SpawnFxOnGameObject",5));
        }
        static void FxComponents58<T>(GameObject root,List<T> output)
        {
            if(!Engine58(4)||root==null){root.GetComponentsInChildren(output);return;}
            ++_engineBlocks58[4].Calls;
            int id=root.GetInstanceID();
            if(!_engineFx58.TryGetValue(id,out var entry)||entry.Root!=root)
            {
                var transforms=root.GetComponentsInChildren<Transform>(true);
                if(transforms.Length>32){root.GetComponentsInChildren(output);return;}
                if(entry!=null){entry.Release();_engineFx58.Remove(id);}
                while(_engineFx58.Count>=32&&_engineFxOrder58.Count>0)
                {int old=_engineFxOrder58.Dequeue();if(_engineFx58.TryGetValue(old,out var previous)){previous.Release();_engineFx58.Remove(old);}}
                entry=new FxEntry58{Root=root};_engineFx58[id]=entry;_engineFxOrder58.Enqueue(id);
                foreach(var transform in transforms){var watch=transform.gameObject.AddComponent<EngineFxHierarchy58>();watch.Root=transform==root.transform;watch.Changed=entry.Invalidate;entry.Watches.Add(watch);}
            }
            // Once an effect mutates structurally, use its native queries for
            // the rest of this instance's lifetime; do not guess new structure.
            if(entry.Dirty){root.GetComponentsInChildren(output);return;}
            if(entry.Lists.TryGetValue(typeof(T),out var value)&&value is T[] cached)
            {
                output.Clear();
                foreach(var item in cached)
                    if(item is Component component&&component!=null&&(component.gameObject==root||component.gameObject.activeInHierarchy))output.Add(item);
                ++_engineBlocks58[4].Reused;return;
            }
            var all=root.GetComponentsInChildren<T>(true);entry.Lists[typeof(T)]=all;
            root.GetComponentsInChildren(output);
        }
        static IEnumerable<CodeInstruction> FxComponentsTranspiler58(IEnumerable<CodeInstruction> source)
        {
            int matches=0;
            foreach(var instruction in source)
            {
                if(instruction.operand is MethodInfo m&&m.DeclaringType==typeof(GameObject)&&m.Name=="GetComponentsInChildren"&&m.IsGenericMethod&&
                    m.GetParameters().Length==1&&m.ReturnType==typeof(void)&&_fxStableComponents58.Contains(m.GetGenericArguments()[0].FullName))
                {instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(Main),nameof(FxComponents58)).MakeGenericMethod(m.GetGenericArguments()[0]);++matches;}
                yield return instruction;
            }
            if(matches!=2)throw new InvalidOperationException("Native stable FX query contract changed");
        }
    }
}
