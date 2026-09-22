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
        static Harmony _emptyOccludedHighlightHarmony;
        static Func<object, object> _emptyOccludedHighlightRenderers;
        static Action<object, int> _emptyOccludedHighlightSetCount;
        static FieldInfo _emptyOccludedHighlightBounds;
        static bool _emptyOccludedHighlightReady;
        static string _emptyOccludedHighlightFailure;
        static long _emptyOccludedHighlightSkipped;
        static readonly HashSet<object> _emptyOccludedHighlightPending = new HashSet<object>();

        static void InstallEmptyOccludedHighlightOptimization()
        {
            if (_emptyOccludedHighlightHarmony != null) return;
            try
            {
                var type = typeof(Owlcat.Runtime.Visual.Waaagh.RenderingData).Assembly.GetType("Owlcat.Runtime.Visual.Waaagh.RendererFeatures.OccludedObjectHighlighting.OccludedObjectHighlightingFeature");
                var list = AccessTools.Field(type, "m_RendererInfos");
                var count = AccessTools.Field(type, "m_CurrentCount");
                _emptyOccludedHighlightBounds = AccessTools.Field(type, "m_Bounds");
                if (list == null || !typeof(ICollection).IsAssignableFrom(list.FieldType) || count?.FieldType != typeof(int) ||
                    _emptyOccludedHighlightBounds == null || list.IsStatic || count.IsStatic || _emptyOccludedHighlightBounds.IsStatic)
                    throw new MissingFieldException("Native occluded highlighter list/bounds/count changed");
                _emptyOccludedHighlightRenderers = TouchSelectionCallFactory.FieldGetter(list);
                var setter = new DynamicMethod("RTMaquetaXR_EmptyOccludedHighlightCount", typeof(void), new[] { typeof(object), typeof(int) }, typeof(Main).Module, true);
                var il = setter.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, type);
                il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, count); il.Emit(OpCodes.Ret);
                _emptyOccludedHighlightSetCount = (Action<object, int>)setter.CreateDelegate(typeof(Action<object, int>));
                const BindingFlags declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                var start = type.GetMethod("StartSetupJobs", declared, null, new[] { typeof(Owlcat.Runtime.Visual.Waaagh.RenderingData).MakeByRefType() }, null);
                var complete = type.GetMethod("CompleteSetupJobs", declared, null, Type.EmptyTypes, null);
                // AccessTools.Method without parameters selected inherited
                // Dispose(), rejecting the whole optimization at startup.
                var dispose = type.GetMethod("Dispose", declared, null, new[] { typeof(bool) }, null);
                if (start == null || complete == null || dispose == null || dispose.ReturnType != typeof(void) ||
                    dispose.GetParameters().Length != 1 || dispose.GetParameters()[0].ParameterType != typeof(bool) || start.ReturnType != typeof(void) || complete.ReturnType != typeof(void) ||
                    start.GetParameters().Length != 1 || complete.GetParameters().Length != 0)
                    throw new MissingMethodException("Native occluded highlighter setup pair changed");
                _emptyOccludedHighlightHarmony = new Harmony("RTMaquetaXR.EmptyOccludedHighlightJobs");
                _emptyOccludedHighlightHarmony.Patch(start,
                    prefix: new HarmonyMethod(typeof(Main), nameof(EmptyOccludedHighlightStart)),
                    transpiler: new HarmonyMethod(typeof(Main), nameof(EmptyOccludedHighlightTranspiler)),
                    finalizer: new HarmonyMethod(typeof(Main), nameof(EmptyOccludedHighlightFinalizer)));
                _emptyOccludedHighlightHarmony.Patch(complete, prefix: new HarmonyMethod(typeof(Main), nameof(EmptyOccludedHighlightComplete)));
                _emptyOccludedHighlightHarmony.Patch(dispose, prefix: new HarmonyMethod(typeof(Main), nameof(EmptyOccludedHighlightDispose)));
                _emptyOccludedHighlightReady = true;
            }
            catch (Exception error)
            {
                _emptyOccludedHighlightHarmony?.UnpatchAll(_emptyOccludedHighlightHarmony.Id); _emptyOccludedHighlightHarmony = null;
                _emptyOccludedHighlightReady = false; _emptyOccludedHighlightFailure = error.Message; _emptyOccludedHighlightPending.Clear();
                _log?.Error("[performance/occluded-highlights] Native setup retained: " + error.Message);
            }
        }
        static void EmptyOccludedHighlightStart(object __instance) => _emptyOccludedHighlightPending.Remove(__instance);
        static Exception EmptyOccludedHighlightFinalizer(object __instance, Exception __exception)
        {
            if (__exception != null) _emptyOccludedHighlightPending.Remove(__instance);
            return __exception;
        }
        static void EmptyOccludedHighlightDispose(object __instance) => _emptyOccludedHighlightPending.Remove(__instance);
        static IEnumerable<CodeInstruction> EmptyOccludedHighlightTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = new List<CodeInstruction>(instructions);
            int anchor = -1, matches = 0, clear = -1;
            for (int i = 0; i < code.Count; ++i)
            {
                if (code[i].operand is MethodInfo method && method.Name == "Clear" && method.DeclaringType.IsGenericType &&
                    method.DeclaringType.GetGenericArguments()[0].FullName == "Owlcat.Runtime.Visual.Waaagh.RendererFeatures.OccludedObjectHighlighting.OccludedObjectHighlightingFeature+RendererInfo") clear = i;
                if (i > 0 && i + 1 < code.Count && code[i].opcode == OpCodes.Ldflda && Equals(code[i].operand, _emptyOccludedHighlightBounds) &&
                    code[i - 1].opcode == OpCodes.Ldarg_0 && code[i + 1].operand is MethodInfo next && next.Name == "get_IsCreated")
                { if (anchor < 0) anchor = i - 1; ++matches; }
            }
            // First check enters capacity management; the second check guards
            // disposing an undersized existing array inside that branch.
            if (matches != 2 || clear < 0 || anchor <= clear || code[anchor].blocks.Count != 0)
                throw new InvalidOperationException("Native occluded highlighter collection/bounds boundary changed");
            var continuation = generator.DefineLabel();
            var load = new CodeInstruction(OpCodes.Ldarg_0);
            load.labels.AddRange(code[anchor].labels); code[anchor].labels.Clear(); code[anchor].labels.Add(continuation);
            code.InsertRange(anchor, new[] { load,
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Main), nameof(EmptyOccludedHighlightAfterCollection))),
                new CodeInstruction(OpCodes.Brfalse, continuation), new CodeInstruction(OpCodes.Ret) });
            return code;
        }
        static bool EmptyOccludedHighlightAfterCollection(object instance)
        {
            if (!_emptyOccludedHighlightReady || !EngineEffectPolicy.EmptyHighlight(_active, _attached, _modeFlat,
                IsEye(_renderingCamera), ((ICollection)_emptyOccludedHighlightRenderers(instance)).Count)) return false;
            // All native occluded highlighters have been collected without changing
            // their materials, colors or active state.
            // Zero inputs imply zero visible results. The previous camera's job
            // has already completed; this invocation schedules no job at all.
            _emptyOccludedHighlightSetCount(instance, 0);
            _emptyOccludedHighlightPending.Add(instance); ++_emptyOccludedHighlightSkipped;
            return true;
        }
        static bool EmptyOccludedHighlightComplete(object __instance) => !_emptyOccludedHighlightPending.Remove(__instance);
        static void StopEmptyOccludedHighlightOptimization()
        {
            _emptyOccludedHighlightReady = false; _emptyOccludedHighlightPending.Clear();
            _emptyOccludedHighlightHarmony?.UnpatchAll(_emptyOccludedHighlightHarmony.Id); _emptyOccludedHighlightHarmony = null;
        }
        static object EmptyOccludedHighlightSnapshot() => new {
            Ready = _emptyOccludedHighlightReady, Failure = _emptyOccludedHighlightFailure, EmptySetupsAvoided = _emptyOccludedHighlightSkipped,
            Scope = "Only empty native occluded-object renderer collections. Skip bounds/jobs and preserve every non-empty per-eye visibility result."
        };
    }
}
