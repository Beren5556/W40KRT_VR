using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RTMaquetaXR
{
    internal static class EngineBudgetPolicy58
    {
        internal static bool Yield(int completed, double milliseconds, int queued) =>
            completed > 0 && completed < queued && (completed >= 128 || milliseconds >= 2.0);
        internal static bool LogAccepted(bool enabled, int minimum, int severity) => enabled && severity >= minimum;
    }
    public static partial class Main
    {
        static Func<bool> _aiAcceptsLog58;
        static long _combatLogStarted58;
        static void InstallAiLogGuard58()
        {
            var logger=AccessTools.TypeByName("Owlcat.Runtime.Core.Logging.Logger");
            var channel=AccessTools.Field(AccessTools.TypeByName("Kingmaker.PFLog"),"AI");
            var instance=EngineMethod58(logger.FullName,"get_Instance",0);
            var enabled=AccessTools.Field(logger,"Enabled");
            var minimum=AccessTools.PropertyGetter(channel.FieldType,"MinLevel");
            if(!instance.IsStatic||enabled.FieldType!=typeof(bool)||!minimum.ReturnType.IsEnum)
                throw new InvalidOperationException("AI logger filter changed");
            var method=new DynamicMethod("RTVR_AI_log_filter58",typeof(bool),Type.EmptyTypes,typeof(Main),true);
            var il=method.GetILGenerator();
            il.Emit(OpCodes.Call,instance); il.Emit(OpCodes.Ldfld,enabled);
            il.Emit(OpCodes.Ldsfld,channel); il.Emit(OpCodes.Callvirt,minimum);
            il.Emit(OpCodes.Ldc_I4_0); // Native Log(string) uses LogSeverity.Message (0).
            il.Emit(OpCodes.Call,AccessTools.Method(typeof(EngineBudgetPolicy58),nameof(EngineBudgetPolicy58.LogAccepted)));
            il.Emit(OpCodes.Ret);_aiAcceptsLog58=(Func<bool>)method.CreateDelegate(typeof(Func<bool>));
            EnginePatch58(7,EngineMethod58("Kingmaker.AI.DebugUtilities.AILogger","Log",1),nameof(AiLogPrefix58));
        }
        static bool AiLogPrefix58()
        {
            if(!Engine58(7))return true;
            try { ++_engineBlocks58[7].Calls;bool accepts=_aiAcceptsLog58();if(!accepts)++_engineBlocks58[7].Reused;return accepts; }
            catch(Exception e){EngineFail58(7,e);return true;}
        }
        static void InstallCombatLogBudget58()
        {
            var method=EngineMethod58("Kingmaker.UI.Models.Log.GameLogController","Tick",0);
            EnginePatch58(6,method,nameof(CombatLogStart58),finalizer:nameof(CombatLogEnd58),transpiler:nameof(CombatLogTranspiler58));
            EngineObserve58(6,method);
        }
        static void CombatLogStart58(out long __state)
        {__state=_combatLogStarted58;_combatLogStarted58=Engine58(6)?Stopwatch.GetTimestamp():0;}
        static Exception CombatLogEnd58(Exception __exception,long __state){_combatLogStarted58=__state;return __exception;}
        static int CombatLogCount58(int count,int processed)
        {
            if(_combatLogStarted58==0||!Engine58(6))return count;
            ++_engineBlocks58[6].Calls;
            if(EngineBudgetPolicy58.Yield(processed,(Stopwatch.GetTimestamp()-_combatLogStarted58)*1000.0/Stopwatch.Frequency,count))
            {++_engineBlocks58[6].Deferred;return 0;}
            return count;
        }
        static IEnumerable<CodeInstruction> CombatLogTranspiler58(IEnumerable<CodeInstruction> source)
        {
            var code=new List<CodeInstruction>(source);int replaced=0;
            for(int i=0;i<code.Count;i++)
            {
                yield return code[i];
                if(code[i].operand is MethodInfo m&&m.Name=="get_Count"&&m.DeclaringType.IsGenericType&&
                    m.DeclaringType.GetGenericArguments()[0].FullName=="Kingmaker.UI.Models.Log.Events.GameLogEvent")
                {
                    // Native loop's index is local 1. Its cleanup removes only
                    // consumed null entries; all unprocessed events stay queued.
                    if(i+1>=code.Count||(code[i+1].opcode!=OpCodes.Blt&&code[i+1].opcode!=OpCodes.Blt_S))
                        throw new InvalidOperationException("Combat history loop changed");
                    yield return new CodeInstruction(OpCodes.Ldloc_1);
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(Main),nameof(CombatLogCount58)));++replaced;
                }
            }
            if(replaced!=1)throw new InvalidOperationException("Combat history budget requires exactly one event loop");
        }
    }
}
