using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _nativeHudSelectionReady;
        static string _nativeHudSelectionFailure;
        static long _nativeHudNullKeysSkipped;

        static void InstallNativeHudSelectionFix()
        {
            _nativeHudSelectionReady=false; _nativeHudSelectionFailure=null;
            try
            {
                var type=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.SurfaceCombat.SurfaceHUDVM");
                var target=type?.GetMethod("OnUnitChanged",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,Type.EmptyTypes,null);
                if(target==null||target.ReturnType!=typeof(void))throw new MissingMethodException("SurfaceHUDVM.OnUnitChanged() void");
                _touchHarmony.Patch(target,transpiler:new HarmonyMethod(typeof(Main),nameof(NativeHudSelectionTranspiler)));
                _nativeHudSelectionReady=true;
                _log.Log("[hud/selection] Native mark-map lookup tolerates a cleared selection; all UI transitions are retained.");
            }
            catch(Exception error)
            {
                _nativeHudSelectionFailure=error.Message;
                _log.Error("[hud/selection] Optional native null-key fix unavailable; VR and Touch retained: "+error.Message);
            }
        }

        static IEnumerable<CodeInstruction> NativeHudSelectionTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes=instructions.ToList();
            CodeInstruction match=null;int count=0;
            foreach(var code in codes)
                if(code.opcode==OpCodes.Call&&code.operand is MethodInfo method&&
                    method.DeclaringType==typeof(Enumerable)&&method.Name==nameof(Enumerable.Contains)&&method.IsGenericMethod&&
                    method.GetParameters().Length==2&&method.GetGenericArguments()[0].FullName=="Kingmaker.EntitySystem.Entities.MechanicEntity")
                { match=code;++count; }
            if(count!=1)throw new InvalidOperationException("Expected exactly one native SurfaceHUD mark-map Contains lookup");
            var original=(MethodInfo)match.operand;
            if(original.GetGenericArguments()[0].IsValueType)throw new InvalidOperationException("Native HUD selected unit must be a reference type");
            match.operand=typeof(Main).GetMethod(nameof(NativeHudSelectionContains),BindingFlags.Static|BindingFlags.NonPublic)
                .MakeGenericMethod(original.GetGenericArguments());
            return codes;
        }

        static bool NativeHudSelectionContains<T>(IEnumerable<T> source,T value) where T:class
        {
            if(TouchInputOwned&&ReferenceEquals(value,null)) { ++_nativeHudNullKeysSkipped;return false; }
            return Enumerable.Contains(source,value);
        }

        internal static object NativeHudSelectionFixSnapshot()=>new {
            Ready=_nativeHudSelectionReady&&TouchInputOwned,Failure=_nativeHudSelectionFailure,NullKeysSkipped=_nativeHudNullKeysSkipped,
            Scope="SurfaceHUDVM.OnUnitChanged mark-map lookup only; clears UI normally, without discarding selection or game ticks"
        };
    }
}
