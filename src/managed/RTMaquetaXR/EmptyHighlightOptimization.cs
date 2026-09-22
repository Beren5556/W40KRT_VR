using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Harmony _emptyHighlightHarmony;
        static Func<object, object> _emptyHighlightRenderers;
        static Action<object, int> _emptyHighlightSetCount;
        static FieldInfo _emptyHighlightBounds;
        static bool _emptyHighlightReady;
        static string _emptyHighlightFailure;
        static long _emptyHighlightSkipped;
        static readonly HashSet<object> _emptyHighlightPending = new HashSet<object>();

        static void InstallEmptyHighlightOptimization()
        {
            if (_emptyHighlightHarmony != null) return;
            try
            {
                var type = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.RendererFeatures.Highlighting.HighlightingFeature");
                var list = AccessTools.Field(type, "m_RendererInfos");
                var count = AccessTools.Field(type, "m_CurrentCount");
                _emptyHighlightBounds = AccessTools.Field(type, "m_Bounds");
                if (list == null || !typeof(ICollection).IsAssignableFrom(list.FieldType) || count?.FieldType != typeof(int) ||
                    _emptyHighlightBounds == null || list.IsStatic || count.IsStatic || _emptyHighlightBounds.IsStatic)
                    throw new MissingFieldException("Native highlighter list/bounds/count changed");
                _emptyHighlightRenderers = TouchSelectionCallFactory.FieldGetter(list);
                var setter = new DynamicMethod("RTMaquetaXR_EmptyHighlightCount", typeof(void), new[] { typeof(object), typeof(int) }, typeof(Main).Module, true);
                var il = setter.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, type);
                il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, count); il.Emit(OpCodes.Ret);
                _emptyHighlightSetCount = (Action<object, int>)setter.CreateDelegate(typeof(Action<object, int>));
                var start = AccessTools.Method(type, "StartSetupJobs");
                var complete = AccessTools.Method(type, "CompleteSetupJobs");
                if (start == null || complete == null || start.ReturnType != typeof(void) || complete.ReturnType != typeof(void) ||
                    start.GetParameters().Length != 1 || complete.GetParameters().Length != 0)
                    throw new MissingMethodException("Native highlighter setup pair changed");
                _emptyHighlightHarmony = new Harmony("RTMaquetaXR.EmptyHighlightJobs");
                _emptyHighlightHarmony.Patch(start,
                    prefix: new HarmonyMethod(typeof(Main), nameof(EmptyHighlightStart)),
                    transpiler: new HarmonyMethod(typeof(Main), nameof(EmptyHighlightTranspiler)));
                _emptyHighlightHarmony.Patch(complete, prefix: new HarmonyMethod(typeof(Main), nameof(EmptyHighlightComplete)));
                _emptyHighlightReady = true;
            }
            catch (Exception error)
            {
                _emptyHighlightHarmony?.UnpatchAll(_emptyHighlightHarmony.Id); _emptyHighlightHarmony = null;
                _emptyHighlightReady = false; _emptyHighlightFailure = error.Message; _emptyHighlightPending.Clear();
                _log?.Error("[performance/highlights] Native setup retained: " + error.Message);
            }
        }
        static void EmptyHighlightStart(object __instance) => _emptyHighlightPending.Remove(__instance);
        static IEnumerable<CodeInstruction> EmptyHighlightTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = new List<CodeInstruction>(instructions);
            int anchor = -1, matches = 0, clear = -1;
            for (int i = 0; i < code.Count; ++i)
            {
                if (code[i].operand is MethodInfo method && method.Name == "Clear" && method.DeclaringType.IsGenericType &&
                    method.DeclaringType.GetGenericArguments()[0].FullName == "Owlcat.Runtime.Visual.Waaagh.RendererFeatures.Highlighting.HighlightingFeature+RendererInfo") clear = i;
                if (i > 0 && i + 1 < code.Count && code[i].opcode == OpCodes.Ldflda && Equals(code[i].operand, _emptyHighlightBounds) &&
                    code[i - 1].opcode == OpCodes.Ldarg_0 && code[i + 1].operand is MethodInfo next && next.Name == "get_IsCreated")
                { if (anchor < 0) anchor = i - 1; ++matches; }
            }
            // First check enters capacity management; the second check guards
            // disposing an undersized existing array inside that branch.
            if (matches != 2 || clear < 0 || anchor <= clear || code[anchor].blocks.Count != 0)
                throw new InvalidOperationException("Native highlighter collection/bounds boundary changed");
            var continuation = generator.DefineLabel();
            var load = new CodeInstruction(OpCodes.Ldarg_0);
            load.labels.AddRange(code[anchor].labels); code[anchor].labels.Clear(); code[anchor].labels.Add(continuation);
            code.InsertRange(anchor, new[] { load,
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Main), nameof(EmptyHighlightAfterCollection))),
                new CodeInstruction(OpCodes.Brfalse, continuation), new CodeInstruction(OpCodes.Ret) });
            return code;
        }
        static bool EmptyHighlightAfterCollection(object instance)
        {
            if (!_emptyHighlightReady || !EngineEffectPolicy.EmptyHighlight(_active, _attached, _modeFlat,
                IsEye(_renderingCamera), ((ICollection)_emptyHighlightRenderers(instance)).Count)) return false;
            // All native highlighters have been collected and colors updated.
            // Zero inputs imply zero visible results. The previous camera's job
            // has already completed; this invocation schedules no job at all.
            _emptyHighlightSetCount(instance, 0);
            _emptyHighlightPending.Add(instance); ++_emptyHighlightSkipped;
            return true;
        }
        static bool EmptyHighlightComplete(object __instance) => !_emptyHighlightPending.Remove(__instance);
        static void StopEmptyHighlightOptimization()
        {
            _emptyHighlightReady = false; _emptyHighlightPending.Clear();
            _emptyHighlightHarmony?.UnpatchAll(_emptyHighlightHarmony.Id); _emptyHighlightHarmony = null;
        }
        static object EmptyHighlightSnapshot() => new {
            Ready = _emptyHighlightReady, Failure = _emptyHighlightFailure, EmptySetupsAvoided = _emptyHighlightSkipped,
            Scope = "Only after native renderer collection is empty. No eye culling is reused; non-empty setup and per-eye visibility stay native."
        };
    }
}
