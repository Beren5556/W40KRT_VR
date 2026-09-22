using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _nativeCardLayoutAttempted, _nativeCardLayoutReady;
        static Type _nativeCardGroupType;
        static long _nativeCardDeferredSections;
        static Func<object, object> _nativeCardGroupModel, _nativeCardGroupLayout;
        static Func<object, object> _nativeCardGroupPreferredHeight;
        static readonly MethodInfo _nativeImmediateLayout = typeof(LayoutRebuilder).GetMethod(nameof(LayoutRebuilder.ForceRebuildLayoutImmediate));

        static void InstallNativeCardLayoutBatch()
        {
            if (_nativeCardLayoutAttempted) return;
            _nativeCardLayoutAttempted = true;
            try
            {
                _nativeCardGroupType = AccessTools.TypeByName("Kingmaker.UI.MVVM.View.Tooltip.Bricks.TooltipBricksGroupView");
                var model = PcUiPath.Property(_nativeCardGroupType, "ViewModel");
                var layout = PcUiPath.Field(model.PropertyType, "LayoutParams");
                _nativeCardGroupModel = PcUiPath.Getter(model);
                _nativeCardGroupLayout = TouchSelectionCallFactory.FieldGetter(layout);
                var heightField = PcUiPath.Field(layout.FieldType, "PreferredElementHeight");
                if (heightField.FieldType != typeof(float?)) throw new MissingFieldException("PreferredElementHeight is no longer nullable float");
                var heightOwner = Expression.Parameter(typeof(object), "owner");
                _nativeCardGroupPreferredHeight = Expression.Lambda<Func<object, object>>(Expression.Convert(
                    Expression.Field(Expression.Convert(heightOwner, heightField.DeclaringType), heightField), typeof(object)), heightOwner).Compile();
                var add = AccessTools.Method(_nativeCardGroupType, "AddChild", new[] { typeof(RectTransform) });
                var section = AccessTools.Method(AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.View.InfoWindow.InfoSectionView"), "TooltipDataChanged", Type.EmptyTypes);
                if (add == null || section == null) throw new MissingMethodException("Native card layout build contract unavailable");
                _harmony.Patch(add, transpiler: new HarmonyMethod(typeof(Main), nameof(NativeCardGroupLayoutTranspiler)));
                _harmony.Patch(section, prefix: new HarmonyMethod(typeof(Main), nameof(NativeCardSectionBegin)),
                    finalizer: new HarmonyMethod(typeof(Main), nameof(NativeCardSectionEnd)),
                    transpiler: new HarmonyMethod(typeof(Main), nameof(NativeCardSectionLayoutTranspiler)));
                _nativeCardLayoutReady = true;
                _log.Log("[ui/native-card] Coalescing wheel-owned synchronous and deferred native card sections; native measurement barriers retained.");
            }
            catch (Exception error) { _nativeCardLayoutReady = false; _log.Error("[ui/native-card] Native layout retained: " + error.Message); }
        }
        internal static IDisposable BeginNativeCardLayout()
        {
            NativeCardLayoutBatch.MeasureCosts76=DiagnosticsRecording&&_cfg.detailedProfiling;
            return _nativeCardLayoutReady && _cfg.coalesceHudLayout && _active && _attached && !_modeFlat ? new NativeCardLayoutBatch() : null;
        }
        // InfoSectionView observes its model on LateUpdate. Its actual brick
        // construction therefore outlives the synchronous HandleInfoRequest
        // scope. Open a fresh scope ONLY for a section of our still-owned card.
        static void NativeCardSectionBegin(Component __instance, out IDisposable __state)
        {
            __state = null;
            NativeCardLayoutBatch.MeasureCosts76=DiagnosticsRecording&&_cfg.detailedProfiling;
            if (!_nativeCardLayoutReady || !_cfg.coalesceHudLayout || !_active || !_attached || _modeFlat ||
                __instance == null || NativeCardLayoutBatch.Active) return;
            try
            {
                var content = TouchRadialInformationContent;
                if (content == null || !NativeCardOwnership65.Contains(__instance.transform, content.transform)) return;
                __state = new NativeCardLayoutBatch();
                ++_nativeCardDeferredSections;
            }
            catch (Exception error)
            {
                // Ownership disappearing during a native transition must never
                // prevent the original game's section from being constructed.
                _nativeCardLayoutReady=false;
                _log.Error("[ui/native-card] Native section layout retained: " + error.Message);
            }
        }
        static Exception NativeCardSectionEnd(Exception __exception, IDisposable __state)
        {
            // Harmony finalizer runs for successful and exceptional native
            // builds; never leave batching enabled for another game window.
            try { __state?.Dispose(); }
            catch (Exception error)
            {
                _nativeCardLayoutReady = false;
                _log.Error("[ui/native-card] Deferred layout disabled: " + error.Message);
                return __exception ?? error;
            }
            return __exception;
        }
        static void NativeCardGroupLayout(RectTransform root)
        {
            if (_nativeCardLayoutReady && NativeCardLayoutBatch.Active && root != null)
            {
                var group = root.GetComponent(_nativeCardGroupType);
                var model = group == null ? null : _nativeCardGroupModel(group);
                var layout = model == null ? null : _nativeCardGroupLayout(model);
                var height = layout == null ? null : _nativeCardGroupPreferredHeight(layout);
                // AddChild immediately calls UpdateElements for an explicit
                // preferred height. Keep that precise original ordering.
                if (!(height is float value && value > 0) && NativeCardLayoutBatch.Queue(root)) return;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        }
        static void NativeCardSectionLayout(RectTransform root)
        {
            NativeCardLayoutBatch.MeasureCosts76=DiagnosticsRecording&&_cfg.detailedProfiling;
            NativeCardLayoutBatch.Measure(root);
        }
        static IEnumerable<CodeInstruction> NativeCardGroupLayoutTranspiler(IEnumerable<CodeInstruction> instructions) =>
            NativeCardLayoutTranspiler(instructions, nameof(NativeCardGroupLayout));
        static IEnumerable<CodeInstruction> NativeCardSectionLayoutTranspiler(IEnumerable<CodeInstruction> instructions) =>
            NativeCardLayoutTranspiler(instructions, nameof(NativeCardSectionLayout));
        static IEnumerable<CodeInstruction> NativeCardLayoutTranspiler(IEnumerable<CodeInstruction> instructions, string replacement)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(_nativeImmediateLayout))
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(Main), replacement); ++count; }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Expected exactly one native immediate card layout call, found " + count);
        }
    }
}
