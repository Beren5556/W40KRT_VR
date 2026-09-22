using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly NativeUiWarningPolicy _nativeUiWarnings=new NativeUiWarningPolicy();
        static Action<object,string,object[]> _nativeUiWarningCall;
        static MethodInfo _nativeUiWarningMethod;
        static bool _nativeUiWarningsInstalled;
        static string _nativeUiWarningFault;
        const string NativeMissingBindingFormat="Bind: no binding named {0}";
        static void InstallNativeUiWarnings()
        {
            _nativeUiWarnings.Reset();
            if(_nativeUiWarningsInstalled)return;
            try
            {
                var keyboard=AccessTools.TypeByName("Kingmaker.UI.InputSystems.KeyboardAccess");
                var target=keyboard?.GetMethod("Bind",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(string),typeof(Action)},null);
                var log=AccessTools.TypeByName("Owlcat.Runtime.Core.Logging.LogChannel");
                _nativeUiWarningMethod=log?.GetMethod("Warning",BindingFlags.Instance|BindingFlags.Public,null,new[]{typeof(string),typeof(object[])},null);
                if(target==null||target.ReturnType!=typeof(IDisposable)||_nativeUiWarningMethod==null||_nativeUiWarningMethod.ReturnType!=typeof(void))
                    throw new MissingMethodException("KeyboardAccess.Bind / LogChannel.Warning exact contract");
                _nativeUiWarningCall=(Action<object,string,object[]>)TouchSelectionCallFactory.Build(typeof(Action<object,string,object[]>),_nativeUiWarningMethod);
                _harmony.Patch(target,transpiler:new HarmonyMethod(typeof(Main),nameof(NativeUiWarningTranspiler)));
                _nativeUiWarningsInstalled=true;_nativeUiWarningFault=null;
            }
            catch(Exception error){_nativeUiWarningFault=error.Message;_log.Error("[performance/ui] Repeated binding warnings retain native behaviour: "+error.Message);}
        }
        static IEnumerable<CodeInstruction> NativeUiWarningTranspiler(IEnumerable<CodeInstruction> source)
        {
            var codes=new List<CodeInstruction>(source);int warnings=0,formats=0;
            foreach(var code in codes)
            {
                if(code.opcode==OpCodes.Ldstr&&Equals(code.operand,NativeMissingBindingFormat))++formats;
                if(code.opcode==OpCodes.Callvirt&&Equals(code.operand,_nativeUiWarningMethod))++warnings;
            }
            if(formats!=1||warnings!=1)throw new InvalidOperationException("Expected one missing-binding diagnostic in KeyboardAccess.Bind");
            foreach(var code in codes)
                if(code.opcode==OpCodes.Callvirt&&Equals(code.operand,_nativeUiWarningMethod))
                {code.opcode=OpCodes.Call;code.operand=AccessTools.Method(typeof(Main),nameof(NativeUiMissingBindingWarning));}
            return codes;
        }
        static void NativeUiMissingBindingWarning(object channel,string format,object[] args)
        {
            // Preserve the native first warning and every unrelated/malformed
            // diagnostic. OFF/flat mode retains the original logging behaviour.
            bool emit=true;
            if(_active&&_attached&&!_modeFlat&&format==NativeMissingBindingFormat&&args!=null&&args.Length==1&&args[0] is string name)
                emit=_nativeUiWarnings.ShouldEmit(true,name);
            if(emit)_nativeUiWarningCall(channel,format,args);
        }
        static object NativeUiWarningsSnapshot()=>new {
            Installed=_nativeUiWarningsInstalled,Failure=_nativeUiWarningFault,
            OriginalWarningsEmitted=_nativeUiWarnings.Emitted,RepeatedWarningsCoalesced=_nativeUiWarnings.Suppressed,
            OccurrencesByBinding=_nativeUiWarnings.Snapshot(),
            Scope="One native Bind warning only; first message retained, every duplicate counted, bindings/callbacks unchanged"
        };
        static void StopNativeUiWarnings()
        {
            if(DiagnosticsRecording&&_nativeUiWarnings.Suppressed>0)
                _log.Log("[performance/ui] Missing-binding warnings: "+_nativeUiWarnings.Emitted+" original messages; "+_nativeUiWarnings.Suppressed+" repeats coalesced. Per-binding counts are in the performance report.");
            _nativeUiWarnings.Reset();
        }
        static void InstallNativeUiPerformance()
        {
            InstallGameCpuMethod(AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.ActionBar.Surface.SurfaceActionBarPartAbilitiesVM"),"OnUnitChanged",Type.EmptyTypes,GameCpuPhase.ActionBarAbilities);
            InstallGameCpuMethod(AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.ActionBar.Surface.SurfaceActionBarPartConsumablesVM"),"OnUnitChanged",Type.EmptyTypes,GameCpuPhase.ActionBarConsumables);
            InstallGameCpuMethod(AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.ActionBar.Surface.SurfaceActionBarPartWeaponsVM"),"OnUnitChanged",Type.EmptyTypes,GameCpuPhase.ActionBarWeapons);
            InstallGameCpuMethod(AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.SurfaceCombat.MomentumAndVeil.SurfaceMomentumVM"),"OnUnitChanged",Type.EmptyTypes,GameCpuPhase.ActionBarMomentum);
            InstallNativeUiSpecialScope("Kingmaker.UI.InputSystems.KeyboardAccess","Bind",new[]{typeof(string),typeof(Action)},typeof(IDisposable),false,GameCpuPhase.UiKeyboardBind);
            InstallGameCpuMethod(AccessTools.TypeByName("Owlcat.Runtime.UI.Controls.Button.OwlcatButton"),"OnPointerClick",new[]{typeof(UnityEngine.EventSystems.PointerEventData)},GameCpuPhase.UiButtonClick);
            InstallGameCpuMethod(AccessTools.TypeByName("Owlcat.Runtime.UI.Controls.Button.OwlcatMultiButton"),"OnPointerClick",new[]{typeof(UnityEngine.EventSystems.PointerEventData)},GameCpuPhase.UiMultiButtonClick);
            InstallNativeUiSpecialScope("Owlcat.Runtime.UI.Utility.WidgetFactoryStash","ResetStash",Type.EmptyTypes,typeof(void),true,GameCpuPhase.UiWidgetCacheReset);
        }
        static void InstallNativeUiSpecialScope(string typeName,string name,Type[] parameters,Type result,bool isStatic,GameCpuPhase phase)
        {
            try
            {
                var method=AccessTools.DeclaredMethod(AccessTools.TypeByName(typeName),name,parameters);
                if(method==null||method.IsStatic!=isStatic||method.ReturnType!=result||method.GetMethodBody()==null)
                    throw new MissingMethodException(typeName+"."+name);
                var prefix=AccessTools.Method(typeof(GameCpuObservationHooks),nameof(GameCpuObservationHooks.Prefix));
                bool installed=false;var patches=Harmony.GetPatchInfo(method);
                if(patches!=null)foreach(var patch in patches.Prefixes)
                    if(patch.owner==_harmony.Id&&patch.PatchMethod==prefix){installed=true;break;}
                _gameCpuMethods[method]=phase;
                if(!installed)_harmony.Patch(method,prefix:new HarmonyMethod(prefix),finalizer:new HarmonyMethod(typeof(GameCpuObservationHooks),nameof(GameCpuObservationHooks.Finalizer)));
                _gameCpuHookStatus[phase.ToString()]=typeName+"."+name;
            }
            catch(Exception error){_gameCpuHookStatus[phase.ToString()]="unavailable: "+error.Message;}
        }
    }
}
