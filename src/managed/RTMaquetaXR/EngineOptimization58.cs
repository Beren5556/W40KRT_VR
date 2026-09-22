using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace RTMaquetaXR
{
    internal static class EngineOptimizationPolicy58
    {
        internal const int Count = 10, All = (1 << Count) - 1;
        internal static bool Enabled(bool enabled, int mask, int block) => enabled && block >= 0 && block < Count && (mask & (1 << block)) != 0;
        internal static bool Restart(bool applied, int appliedMask, bool requested, int requestedMask) => applied != requested || (appliedMask & All) != (requestedMask & All);
    }
    public static partial class Main
    {
        sealed class EngineBlock58
        {
            internal bool Installed;
            internal string Error;
            internal long Calls, Reused, Deferred, Timed;
            internal double Milliseconds;
        }
        internal struct EngineMeasure58 { internal int Block; internal long Started; }
        static readonly EngineBlock58[] _engineBlocks58 = CreateEngineBlocks58();
        static readonly Dictionary<MethodBase,int> _engineMeasures58 = new Dictionary<MethodBase,int>();
        static bool _engineCaptured58, _engineEnabled58, _engineInstalled58;
        static int _engineMask58, _engineThread58;
        static long _engineMutation58;
        static EngineBlock58[] CreateEngineBlocks58()
        {
            var blocks = new EngineBlock58[EngineOptimizationPolicy58.Count];
            for (int i = 0; i < blocks.Length; i++) blocks[i] = new EngineBlock58();
            return blocks;
        }
        static void CaptureEngineOptimizations58()
        {
            if (_engineCaptured58) return;
            _engineCaptured58 = true; _engineEnabled58 = _cfg.engineOptimizationsEnabled;
            _engineMask58 = _cfg.engineOptimizationMask & EngineOptimizationPolicy58.All;
            _engineThread58 = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }
        static bool Engine58(int block) => EngineOptimizationPolicy58.Enabled(_engineEnabled58, _engineMask58, block) &&
            _engineBlocks58[block].Installed && _engineBlocks58[block].Error == null && _active &&
            System.Threading.Thread.CurrentThread.ManagedThreadId == _engineThread58;
        static void InstallEngineOptimizations58()
        {
            if (_engineInstalled58 || !_engineEnabled58) return;
            _engineInstalled58 = true;
            Action[] installers = { InstallAttackReuse58, InstallReachableReuse58, InstallWeaponReuse58,
                InstallPresentationReuse58, InstallFxReuse58, InstallTacticalGeometry58,
                InstallCombatLogBudget58, InstallAiLogGuard58, InstallInitiativeReuse58, InstallAiBudget58 };
            for (int block=0; block<installers.Length; block++)
            {
                if (!EngineOptimizationPolicy58.Enabled(_engineEnabled58,_engineMask58,block)) continue;
                try {
                    if(block!=3 && AccessTools.TypeByName("Kingmaker.Game")?.Module.ModuleVersionId != new Guid("f564230e-b1b2-45d8-ac09-1010963ed370"))
                        throw new InvalidOperationException("Game module differs from the audited engine contracts");
                    installers[block](); _engineBlocks58[block].Installed=true;
                }
                catch (Exception error) { EngineFail58(block,error); }
            }
        }
        static void EngineFail58(int block, Exception error)
        {
            var state=_engineBlocks58[block];
            if(state.Error!=null)return;
            state.Error=error.GetType().Name+": "+error.Message;
            _log?.Error("[engine/block "+(block+1)+"] Native path retained: "+state.Error);
        }
        static MethodInfo EngineMethod58(string type, string method, int parameters=-1)
        {
            var t=AccessTools.TypeByName(type)??throw new TypeLoadException(type);
            MethodInfo found=null;
            foreach(var candidate in t.GetMethods(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                if((candidate.Name==method||candidate.Name.EndsWith("."+method,StringComparison.Ordinal))&&
                    (parameters<0||candidate.GetParameters().Length==parameters))
                { if(found!=null)throw new AmbiguousMatchException(type+"."+method);found=candidate; }
            return found??throw new MissingMethodException(type,method);
        }
        static void EnginePatch58(int block, MethodInfo method, string prefix=null, string postfix=null, string finalizer=null, string transpiler=null)
        {
            _harmony.Patch(method,prefix:prefix==null?null:new HarmonyMethod(typeof(Main),prefix),
                postfix:postfix==null?null:new HarmonyMethod(typeof(Main),postfix),
                finalizer:finalizer==null?null:new HarmonyMethod(typeof(Main),finalizer),
                transpiler:transpiler==null?null:new HarmonyMethod(typeof(Main),transpiler));
        }
        static void EngineObserve58(int block, MethodInfo method)
        {
            if(_engineMeasures58.ContainsKey(method))return;
            _engineMeasures58.Add(method,block);
            _harmony.Patch(method,prefix:new HarmonyMethod(typeof(EngineTimingHooks58),"Prefix"),finalizer:new HarmonyMethod(typeof(EngineTimingHooks58),"Finalizer"));
        }
        internal static void EngineMeasurePrefix58(MethodBase __originalMethod,out EngineMeasure58 __state)
        {
            __state=default;
            if(!DiagnosticsRecording||!_engineMeasures58.TryGetValue(__originalMethod,out var block)||!Engine58(block))return;
            __state.Block=block;__state.Started=Stopwatch.GetTimestamp();
        }
        internal static Exception EngineMeasureFinal58(Exception __exception,EngineMeasure58 __state)
        {
            if(__state.Started!=0)
            {
                var block=_engineBlocks58[__state.Block];++block.Timed;
                block.Milliseconds+=(Stopwatch.GetTimestamp()-__state.Started)*1000.0/Stopwatch.Frequency;
            }
            return __exception;
        }
        static void EngineMutation58() { if(_active)++_engineMutation58; }
        static void ReleaseEngineCaches58()
        {
            ++_engineMutation58;ClearReachableReuse58();
            foreach(var entry in _engineFx58.Values)entry.Release();
            _engineFx58.Clear();_engineFxOrder58.Clear();
            _tacticalPaths58=new System.Runtime.CompilerServices.ConditionalWeakTable<object,TacticalPath58>();
        }
        static object EngineOptimizationSnapshot58()
        {
            var blocks=new object[_engineBlocks58.Length];
            for(int i=0;i<blocks.Length;i++)
            {
                var b=_engineBlocks58[i];blocks[i]=new { Block=i+1, Requested=EngineOptimizationPolicy58.Enabled(_engineEnabled58,_engineMask58,i),
                    b.Installed,b.Error,b.Calls,b.Reused,b.Deferred,b.Timed,ObservedInclusiveMs=b.Milliseconds };
            }
            return new { StartupEnabled=_engineEnabled58,StartupMask=_engineMask58,
                RestartRequired=EngineOptimizationPolicy58.Restart(_engineEnabled58,_engineMask58,_cfg.engineOptimizationsEnabled,_cfg.engineOptimizationMask),
                Blocks=blocks, CountersAreNotMeasuredSavings=true };
        }
        static readonly string[] EngineNames58={"Attack calculations","Reachable cells","Weapon and ability bars","Tutorials and transitions",
            "Effect preparation","Tactical geometry","Combat history","AI diagnostics","Participants and initiative","AI evaluation budget · experimental"};
        static readonly string[] EngineDescriptions58={
            "Reuse audited attack geometry inside the same operation. Conditions, damage and random decisions remain native.",
            "Reuse unchanged movement calculations with invalidation when gameplay state changes.",
            "Reuse compatible native weapon action models while refreshing the current weapons, abilities and availability.",
            "Avoid redundant native panel layout during transitions, retaining original tutorial and window behaviour.",
            "Reuse audited effect component lists in a bounded cache. Structural changes return to the native path.",
            "Avoid unchanged tactical geometry rebuilds. Movement and attack indicators stay visible.",
            "Spread queued combat-history presentation across frames without dropping events or changing combat rules.",
            "Skip formatting AI diagnostics when the native logger would discard them. Enabled messages and errors remain available.",
            "Reuse participant enumeration within the same initiative operation. Rebuild for the next operation so arrivals and departures stay current.",
            "Experimental: yield between audited AI evaluation iterations. Preserves candidates and resumes the decision; does not lower the game frame rate."};
        static OverlayOption EngineOptimizationMenu58()
        {
            var options=new List<OverlayOption>();
            options.Add(ImageToggle("Engine optimizations","Enable this independent optimization package at the next game launch. Each block can be disabled separately. Changes require restarting the game.",
                ()=>_cfg.engineOptimizationsEnabled,value=>{_cfg.engineOptimizationsEnabled=value;MarkSettingsDirty();SaveSettings();}));
            for(int i=0;i<EngineNames58.Length;i++)
            {
                int block=i;
                options.Add(ImageToggle(EngineNames58[i],EngineDescriptions58[i]+" Restart the game to apply changes.",
                    ()=> (_cfg.engineOptimizationMask&(1<<block))!=0,
                    value=>{if(value)_cfg.engineOptimizationMask|=1<<block;else _cfg.engineOptimizationMask&=~(1<<block);MarkSettingsDirty();SaveSettings();}));
            }
            options.Add(new OverlayOption { Label="Applied engine blocks", Description="Shows applied blocks and saved changes pending a game restart. A block with an incompatible native contract retains the original game path.",
                Value=()=>{
                    int count=0;foreach(var b in _engineBlocks58)if(b.Installed&&b.Error==null)++count;
                    string value=count+" / 10";
                    if(EngineOptimizationPolicy58.Restart(_engineEnabled58,_engineMask58,_cfg.engineOptimizationsEnabled,_cfg.engineOptimizationMask))value+=" · "+ModLocalization.Text("Restart required");
                    return value;
                }});
            return ImageGroup("Engine optimizations", "Independent blocks for combat and transition performance. Every change requires a game restart. Timings and results depend on the scene.",options.ToArray());
        }
    }
}
