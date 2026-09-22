using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        sealed class AiLoopBudget58 {internal int Deferrals,Boundaries;internal long Tick;}
        struct AiBudgetCall58 {internal long Start,Serial;}
        static readonly ConditionalWeakTable<object,AiLoopBudget58> _aiLoopBudgets58=new ConditionalWeakTable<object,AiLoopBudget58>();
        static Type _aiAbilityLoop58;
        static long _aiBudgetStart58,_aiBudgetSerial58,_aiNextSerial58;
        static void InstallAiBudget58()
        {
            _aiAbilityLoop58=AccessTools.TypeByName("Kingmaker.AI.BehaviourTrees.Nodes.LoopOverAbilities")??throw new TypeLoadException("LoopOverAbilities");
            EnginePatch58(9,EngineMethod58("Kingmaker.AI.BehaviourTrees.BehaviourTree","Tick",0),nameof(AiBudgetStart58),finalizer:nameof(AiBudgetEnd58));
            EnginePatch58(9,EngineMethod58("Kingmaker.AI.BehaviourTrees.Nodes.Loop","InitInternal",0),postfix:nameof(AiLoopReset58));
            EnginePatch58(9,EngineMethod58("Kingmaker.AI.BehaviourTrees.Nodes.Loop","TickInternal",1),transpiler:nameof(AiLoopTranspiler58));
        }
        static void AiBudgetStart58(out AiBudgetCall58 __state)
        {
            __state=new AiBudgetCall58{Start=_aiBudgetStart58,Serial=_aiBudgetSerial58};
            _aiBudgetStart58=Engine58(9)?Stopwatch.GetTimestamp():0;_aiBudgetSerial58=++_aiNextSerial58;
        }
        static Exception AiBudgetEnd58(Exception __exception,AiBudgetCall58 __state)
        {_aiBudgetStart58=__state.Start;_aiBudgetSerial58=__state.Serial;return __exception;}
        static void AiLoopReset58(object __instance)
        {if(_aiLoopBudgets58.TryGetValue(__instance,out var state)){state.Deferrals=state.Boundaries=0;state.Tick=0;}}
        static bool AiLoopYield58(object loop)
        {
            // Only the two audited ability-selection subtrees use this exact
            // concrete type. Move/cast commands are outside these loops.
            if(!Engine58(9)||_aiBudgetStart58==0||loop.GetType()!=_aiAbilityLoop58)return false;
            var state=_aiLoopBudgets58.GetOrCreateValue(loop);
            if(state.Tick!=_aiBudgetSerial58){state.Tick=_aiBudgetSerial58;state.Boundaries=0;}
            ++_engineBlocks58[9].Calls;
            if(++state.Boundaries<=1||state.Deferrals>=4)return false;
            if((Stopwatch.GetTimestamp()-_aiBudgetStart58)*1000.0/Stopwatch.Frequency<2.0)return false;
            ++state.Deferrals;++_engineBlocks58[9].Deferred;return true;
        }
        static IEnumerable<CodeInstruction> AiLoopTranspiler58(IEnumerable<CodeInstruction> source,ILGenerator generator)
        {
            var code=new List<CodeInstruction>(source);int changed=0;
            for(int i=0;i<code.Count;i++)
            {
                if(i+1<code.Count&&code[i].opcode==OpCodes.Ldarg_0&&code[i+1].opcode==OpCodes.Ldfld&&
                    code[i+1].operand is FieldInfo field&&field.Name=="NextIteration")
                {
                    if(code[i].blocks.Count!=0)throw new InvalidOperationException("AI boundary exception contract changed");
                    var resume=generator.DefineLabel();var first=new CodeInstruction(OpCodes.Ldarg_0);
                    first.labels.AddRange(code[i].labels);code[i].labels.Clear();code[i].labels.Add(resume);
                    yield return first;
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(Main),nameof(AiLoopYield58)));
                    yield return new CodeInstruction(OpCodes.Brfalse,resume);
                    // IterationDone is already true. The next Tick resumes
                    // here BEFORE advancing the native ability enumerator.
                    yield return new CodeInstruction(OpCodes.Ldc_I4_3);yield return new CodeInstruction(OpCodes.Ret);++changed;
                }
                yield return code[i];
            }
            if(changed!=1)throw new InvalidOperationException("AI resumable boundary changed");
        }
    }
}
