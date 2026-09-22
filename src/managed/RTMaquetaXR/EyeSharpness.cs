using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _eyeSharpnessHook;
        static Func<float> _originalTaaSharpness;

        static void InstallEyeSharpnessHook()
        {
            if (_eyeSharpnessHook) return;
            try
            {
                var type = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.WaaaghPipeline");
                var source = AccessTools.Method(type, "GetTemporalAntialiasingSharpness");
                var target = AccessTools.Method(type, "InitializeAdditionalCameraData");
                if (source == null || target == null || !target.IsStatic || target.GetParameters()[0].ParameterType != typeof(Camera))
                    throw new MissingMethodException("TAA sharpness camera context");
                _originalTaaSharpness = (Func<float>)Delegate.CreateDelegate(typeof(Func<float>), source);
                _harmony.Patch(target, transpiler: new HarmonyMethod(typeof(Main), nameof(EyeSharpnessTranspiler)));
                _eyeSharpnessHook = true;
                _log.Log("[quality] Native TAA sharpening for eye cameras; strength=" + _cfg.taaSharpness);
            }
            catch (Exception e) { _log.Error("[quality] TAA sharpening hook: " + e.Message); }
        }

        static IEnumerable<CodeInstruction> EyeSharpnessTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(instructions);
            var source = _originalTaaSharpness.Method;
            int index = -1, count = 0;
            for (int i = 0; i < result.Count; ++i)
                if (Equals(result[i].operand, source)) { index = i; ++count; }
            if (count != 1) throw new InvalidOperationException("Unexpected TAA sharpening lookup");
            // Branch labels stay on the camera load, so every path has one argument.
            result[index].opcode = OpCodes.Ldarg_0; result[index].operand = null;
            result.Insert(index + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Main), nameof(EyeTaaSharpness))));
            return result;
        }
        static float EyeTaaSharpness(Camera camera)
        {
            return _active && EffectiveEyeAa == EyeAaMode.Taa && IsEye(camera) ? Mathf.Clamp01(_cfg.taaSharpness) : _originalTaaSharpness();
        }
    }
}
