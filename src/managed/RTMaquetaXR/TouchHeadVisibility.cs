using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Owlcat.Runtime.Visual.Waaagh;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly TouchHeadVisibilityState<Renderer> _touchHeadVisuals = new TouchHeadVisibilityState<Renderer>(
            renderer=>renderer!=null, renderer=>renderer.forceRenderingOff, (renderer,value)=>renderer.forceRenderingOff=value);
        static readonly HashSet<object> _touchHeadHighlighters = new HashSet<object>();
        static TouchHeadVisibilityContracts _touchHeadVisualContracts;
        static Transform _touchHeadVisualRoot;
        static bool _touchHeadVisualDirty, _touchHeadVisualFault, _touchHeadVisualInstalled;
        static long _touchHeadVisualCaptures, _touchHeadVisualEyeScopes, _touchHeadHighlightSkips;
        static string _touchHeadVisualError;
        static void InstallTouchHeadVisibility()
        {
            // InstallTouchGameInput always tears down this Harmony owner first.
            // Reinstall after every restart instead of trusting stale ready state.
            _touchHeadVisualInstalled=false;
            var patched=new List<MethodBase>();
            try
            {
            _touchHeadVisualContracts=TouchHeadVisibilityContracts.Create(AccessTools.TypeByName);
            patched.Add(_touchHeadVisualContracts.Render);
            _touchHarmony.Patch(_touchHeadVisualContracts.Render,
                prefix:new HarmonyMethod(typeof(TouchHeadRenderPatch),nameof(TouchHeadRenderPatch.Prefix)),
                finalizer:new HarmonyMethod(typeof(TouchHeadRenderPatch),nameof(TouchHeadRenderPatch.Finalizer)));
            patched.Add(_touchHeadVisualContracts.AvatarChanged);
            _touchHarmony.Patch(_touchHeadVisualContracts.AvatarChanged,postfix:new HarmonyMethod(typeof(Main),nameof(TouchHeadAvatarChanged)));
            patched.Add(_touchHeadVisualContracts.EquipmentChanged);
            _touchHarmony.Patch(_touchHeadVisualContracts.EquipmentChanged,postfix:new HarmonyMethod(typeof(Main),nameof(TouchHeadAvatarChanged)));
            patched.Add(_touchHeadVisualContracts.RendererInfos);
            _touchHarmony.Patch(_touchHeadVisualContracts.RendererInfos,prefix:new HarmonyMethod(typeof(Main),nameof(TouchHeadHighlightPrefixFactory)));
            // Existing callers can be inlined in Mono. Recompile the two exact
            // highlighter consumers; all their original instructions remain.
            patched.Add(_touchHeadVisualContracts.HighlightSetup);
            _touchHarmony.Patch(_touchHeadVisualContracts.HighlightSetup,transpiler:new HarmonyMethod(typeof(Main),nameof(TouchHeadRecompile)));
            patched.Add(_touchHeadVisualContracts.HighlightRender);
            _touchHarmony.Patch(_touchHeadVisualContracts.HighlightRender,transpiler:new HarmonyMethod(typeof(Main),nameof(TouchHeadRecompile)));
            _touchHeadVisualFault=false; _touchHeadVisualError=null; _touchHeadVisualInstalled=true;
            }
            catch(Exception error)
            {
                // Fail closed if a game update changes one contract. Remove only
                // this module's exact patches, retaining all other camera hooks.
                foreach(var method in patched)
                    try { _touchHarmony.Unpatch(method,HarmonyPatchType.All,_touchHarmony.Id); } catch { }
                _touchHeadVisualInstalled=false; _touchHeadVisualContracts=null;
                TouchHeadVisualFailure(error);
            }
        }
        static IEnumerable<CodeInstruction> TouchHeadRecompile(IEnumerable<CodeInstruction> instructions)=>instructions;
        // Harmony shares __state by PATCH DECLARING TYPE, even across different
        // Harmony owners. Heartbeat on the same worker uses Main.RenderScopeState;
        // placing these callbacks on Main would overwrite its Camera references.
        // Keep the paired state callbacks together in their own declaring type.
        internal static class TouchHeadRenderPatch
        {
            internal static void Prefix(ref CameraData __1,out TouchHeadVisibilityScope __state)=>TouchHeadRenderBegin(ref __1,out __state);
            internal static Exception Finalizer(Exception __exception,TouchHeadVisibilityScope __state)=>TouchHeadRenderEnd(__exception,__state);
        }
        static DynamicMethod TouchHeadHighlightPrefixFactory(MethodBase original)=>
            CombatVisualPrefixFactory.Build(((MethodInfo)original).ReturnType,AccessTools.Method(typeof(Main),nameof(TouchHeadSuppressHighlight)));
        static bool TouchHeadSuppressHighlight(object instance)
        {
            if(!_touchHeadVisuals.Hidden || !_touchHeadHighlighters.Contains(instance)) return false;
            ++_touchHeadHighlightSkips; return true;
        }
        static void TouchHeadAvatarChanged(object __instance)
        {
            if(!_touchFirstPerson || _touchHeadRoot==null) return;
            var component=__instance as Component;
            if(component!=null && (component.transform==_touchHeadRoot || component.transform.IsChildOf(_touchHeadRoot))) _touchHeadVisualDirty=true;
        }
        static void RefreshTouchHeadVisuals()
        {
            RestoreTouchHeadVisuals(true);
            if(!_touchHeadVisualInstalled || _touchHeadVisualFault || _touchHeadVisualContracts==null || _touchHeadRoot==null) return;
            long started=DiagnosticsRecording ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            try
            {
                _touchHeadVisuals.Capture(_touchHeadRoot.GetComponentsInChildren<Renderer>(true));
                foreach(var highlighter in _touchHeadRoot.GetComponentsInChildren(_touchHeadVisualContracts.HighlighterType,true))
                    if(highlighter!=null) _touchHeadHighlighters.Add(highlighter);
                _touchHeadVisualRoot=_touchHeadRoot; _touchHeadVisualDirty=false; ++_touchHeadVisualCaptures;
            }
            catch(Exception error){ TouchHeadVisualFailure(error); }
            finally { if(started!=0) RecordModStage("HeadViewVisualCapture",started); }
        }
        static void TouchHeadRenderBegin(ref CameraData __1,out TouchHeadVisibilityScope __state)
        {
            __state=default;
            if(_touchHeadVisuals.Count==0 && !_touchHeadVisuals.Hidden) return;
            try
            {
                bool hide=!_touchHeadVisualFault && _active && _attached && !_modeFlat && TouchCameraFirstPersonActive &&
                    _touchHeadVisualRoot!=null && _touchHeadVisualRoot==_touchHeadRoot && IsEye(__1.Camera);
                if(!hide && !_touchHeadVisuals.Hidden) return;
                __state=_touchHeadVisuals.Begin(hide);
                if(hide) ++_touchHeadVisualEyeScopes;
            }
            catch(Exception error){TouchHeadVisualFailure(error);}
        }
        static Exception TouchHeadRenderEnd(Exception __exception,TouchHeadVisibilityScope __state)
        {
            try { _touchHeadVisuals.End(__state); }
            catch(Exception error){TouchHeadVisualFailure(error);}
            return __exception;
        }
        internal static void RestoreTouchHeadVisuals(bool clear)
        {
            try
            {
                if(clear) { _touchHeadVisuals.Clear(); _touchHeadHighlighters.Clear(); _touchHeadVisualRoot=null; _touchHeadVisualDirty=false; }
                else _touchHeadVisuals.Restore();
            }
            catch(Exception error){ TouchHeadVisualFailure(error); }
        }
        static void TouchHeadVisualFailure(Exception error)
        {
            bool report=!_touchHeadVisualFault;
            _touchHeadVisualFault=true; _touchHeadVisualError=error.Message;
            try{_touchHeadVisuals.Restore();}catch{}
            if(report) _log.Error("[touch/head-visuals] Local avatar hiding disabled; rendering restored: "+error.Message);
        }
        static object TouchHeadVisualSnapshot()=>new {
            Ready=_touchHeadVisualInstalled, Fault=_touchHeadVisualFault, Error=_touchHeadVisualError,
            Hidden=_touchHeadVisuals.Hidden, Renderers=_touchHeadVisuals.Count, Captures=_touchHeadVisualCaptures,
            EyeScopes=_touchHeadVisualEyeScopes, HideWrites=_touchHeadVisuals.Writes, RestoreWrites=_touchHeadVisuals.Restores,
            HighlightSkips=_touchHeadHighlightSkips
        };
    }
}
