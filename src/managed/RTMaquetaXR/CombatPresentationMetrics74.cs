using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    // A separate declaring type keeps Harmony's __state slot independent of
    // other profiling patches in Main on the same native method.
    internal static class PresentationMetricsHooks74
    {
        static void Prefix(object __instance,out long __state)=>Main.PresentationCostStart74(__instance,out __state);
        static void Postfix(MethodBase __originalMethod,long __state,bool __runOriginal)=>Main.PresentationCostEnd74(__originalMethod,__state,__runOriginal);
    }
    public static partial class Main
    {
        sealed class PresentationCost74
        {
            internal readonly string Name;
            internal long Calls,QueryCalls,WithoutQueryCalls;
            internal double TotalMs,MaxCallMs,FrameMs,MaxFrameMs;
            internal int Frame=-1;
            internal PresentationCost74(string name){Name=name;}
            internal void Record(long start)
            {
                double elapsed=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;
                if(Frame!=Time.frameCount){Frame=Time.frameCount;FrameMs=0;}
                ++Calls;if(TouchWorldInspectionRequested70)++QueryCalls;else ++WithoutQueryCalls;
                TotalMs+=elapsed;FrameMs+=elapsed;MaxCallMs=Math.Max(MaxCallMs,elapsed);MaxFrameMs=Math.Max(MaxFrameMs,FrameMs);
                RecordModStage(Name,start);
                // The normal trace stores the last call; this scope also keeps
                // the sum for the frame so repeated native callbacks are visible.
                _currentModStages[Name]=FrameMs;
            }
            internal object Snapshot()=>new{Calls,QueryCalls,WithoutQueryCalls,AverageCallMs=Calls==0?0:TotalMs/Calls,MaxCallMs,MaxFrameMs,LastFrameMs=FrameMs,LastFrame=Frame};
        }
        static readonly Dictionary<MethodBase,PresentationCost74> _presentationCosts74=new Dictionary<MethodBase,PresentationCost74>();
        static string _presentationMetricsFault74;
        static void InstallPresentationMetrics74()
        {
            try
            {
                var specifications=new[]{
                    new[]{"Kingmaker.Code.UI.MVVM.VM.Overtips.Unit.OvertipEntityUnitVM","OnUpdateHandler","NativeOvertipPosition74"},
                    new[]{"Kingmaker.Code.UI.MVVM.VM.Overtips.Unit.UnitOvertipParts.OvertipHitChanceBlockVM","UpdateProperties","NativeHitPresentation74"},
                    new[]{"Kingmaker.Code.UI.MVVM.VM.Overtips.Unit.UnitOvertipParts.OvertipHealthBlockVM","UpdateProperties","NativeHealthPresentation74"},
                    new[]{"Kingmaker.Code.UI.MVVM.VM.Overtips.Unit.UnitOvertipParts.OvertipCoverBlockVM","UpdateCover","NativeCoverPresentation74"},
                    new[]{"Kingmaker.Code.UI.MVVM.VM.Party.UnitBuffPartVM","UpdateData","NativeBuffPresentation74"},
                    new[]{"Kingmaker.Code.UI.MVVM.View.Overtips.Unit.OvertipUnitView","UpdateVisibility","NativeOvertipVisibility74"}
                };
                foreach(var spec in specifications)
                {
                    var method=AccessTools.DeclaredMethod(AccessTools.TypeByName(spec[0]),spec[1]);
                    if(method==null||_presentationCosts74.ContainsKey(method))continue;
                    _presentationCosts74.Add(method,new PresentationCost74(spec[2]));
                    _harmony.Patch(method,prefix:new HarmonyMethod(typeof(PresentationMetricsHooks74),"Prefix"),
                        postfix:new HarmonyMethod(typeof(PresentationMetricsHooks74),"Postfix"));
                }
            }
            catch(Exception error){_presentationMetricsFault74=error.GetBaseException().Message;_log?.Error("[ui74/metrics] "+error);}
        }
        internal static void PresentationCostStart74(object __instance,out long __state)
        {
            __state=0;
            if(!DiagnosticsRecording||!CombatPresentationContext74)return;
            bool owned=_presentationModels74.ContainsKey(__instance)||_presentationRoots74.ContainsKey(__instance)||
                __instance is Component view&&_worldInformationSources70.ContainsKey(view);
            if(owned)__state=Stopwatch.GetTimestamp();
        }
        internal static void PresentationCostEnd74(MethodBase __originalMethod,long __state,bool __runOriginal)
        {if(__runOriginal&&__state!=0&&DiagnosticsRecording&&_presentationCosts74.TryGetValue(__originalMethod,out var cost))cost.Record(__state);}
        static object PresentationCostsSnapshot74()
        {
            var values=new Dictionary<string,object>();
            foreach(var cost in _presentationCosts74.Values)values[cost.Name]=cost.Snapshot();
            return new{Scopes=values,Fault=_presentationMetricsFault74};
        }
    }
}
