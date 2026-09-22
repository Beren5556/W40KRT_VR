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
        // Audit: Code.dll f564230e-b1b2-45d8-ac09-1010963ed370. Each ability
        // selection clears/disposes all slot VMs, then recreates 20+ of them.
        // Their constructor only allocates reactive fields, subscribes the VM
        // to EventBus, stores four immutable arguments and calls SetMechanicSlot.
        // Reuse the VM at the same index/configuration, while executing that
        // native binding immediately and retaining every original notification.
        sealed class AbilityReuseScope
        {
            internal object Part;
            internal AbilityReuseScope Parent;
            internal object[] Slots;
            internal object Overdrive;
        }
        [ThreadStatic] static AbilityReuseScope _abilityReuseScope;
        static bool _abilityReuseInstalled;
        static string _abilityReuseError;
        static long _abilitySlotReused, _abilitySlotCreated, _abilityRebuilds;
        static MethodInfo _abilityOriginalClear;
        static ConstructorInfo _abilitySlotConstructor;
        static Func<object, object> _abilitySlots, _abilityRawList, _abilityOverdrive;
        static Action<object, object> _abilityWriteOverdrive, _abilitySetMechanic;
        static Action<object> _abilityNativeClear, _abilityResetSlot, _abilityPrepareSlot;
        static Func<object, int, bool, object, int, object> _abilityCreateSlot;
        static Func<object, int, bool, object, int, bool> _abilityCanReuse;

        static void InstallActionBarSlotReuse()
        {
            if (_abilityReuseInstalled) return;
            try
            {
                var part = AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.ActionBar.Surface.SurfaceActionBarPartAbilitiesVM");
                if (part == null || part.Module.ModuleVersionId != new Guid("f564230e-b1b2-45d8-ac09-1010963ed370"))
                    throw new InvalidOperationException("Ability slot reuse requires the audited game module");
                var slot = AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.ActionBar.ActionBarSlotVM");
                var mechanic = AccessTools.TypeByName("Kingmaker.UI.Models.UnitSettings.MechanicActionBarSlot");
                var change = TouchSelectionCallFactory.ExactMethod(part, "OnUnitChanged", typeof(void), false);
                _abilityOriginalClear = TouchSelectionCallFactory.ExactMethod(part.BaseType, "ClearSlots", typeof(void), false);
                _abilityNativeClear = (Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>), _abilityOriginalClear);
                var slotsField = AccessTools.Field(part, "Slots");
                _abilitySlots = TouchSelectionCallFactory.FieldGetter(slotsField);
                _abilityRawList = TouchSelectionCallFactory.FieldGetter(AccessTools.Field(slotsField.FieldType, "m_List"));
                var overdrive = TouchSelectionCallFactory.ExactField(part, "OverdriveSlotVM", slot);
                _abilityOverdrive = TouchSelectionCallFactory.FieldGetter(overdrive);
                _abilityWriteOverdrive = AbilityReferenceSetter(overdrive);
                var set = TouchSelectionCallFactory.ExactMethod(slot, "SetMechanicSlot", typeof(void), false, mechanic);
                _abilitySetMechanic = (Action<object, object>)TouchSelectionCallFactory.Build(typeof(Action<object, object>), set);
                _abilityPrepareSlot = (Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>),
                    TouchSelectionCallFactory.ExactMethod(slot, "DisposeImplementation", typeof(void), false));
                _abilitySlotConstructor = null;
                foreach (var constructor in slot.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    var p = constructor.GetParameters();
                    if (p.Length == 5 && p[0].ParameterType == mechanic && p[1].ParameterType == typeof(int) &&
                        p[2].ParameterType == typeof(bool) && p[3].ParameterType.FullName == "UniRx.BoolReactiveProperty" && p[4].ParameterType.IsEnum)
                        _abilitySlotConstructor = constructor;
                }
                if (_abilitySlotConstructor == null) throw new MissingMethodException("Native ability slot constructor");
                BuildAbilityReuseFactory(slot);
                _abilityResetSlot = BuildAbilitySlotReset(slot, mechanic);
                _abilityCanReuse = BuildAbilitySlotCompatibility(slot);
                _harmony.Patch(change, prefix: new HarmonyMethod(typeof(Main), nameof(AbilityReusePrefix)),
                    finalizer: new HarmonyMethod(typeof(Main), nameof(AbilityReuseFinalizer)),
                    transpiler: new HarmonyMethod(typeof(Main), nameof(AbilityReuseTranspiler)));
                _abilityReuseInstalled = true;
                _log?.Log("[performance] Ability slot VMs reused by index; original selection, slot models, binding and notifications remain synchronous");
            }
            catch (Exception e) { _abilityReuseError = e.Message; _log?.Error("[performance] Ability slot reuse unavailable: " + e.Message); }
        }
        static Action<object, object> AbilityReferenceSetter(FieldInfo field)
        {
            var method = new DynamicMethod("RTVR_AbilityField", typeof(void), new[] { typeof(object), typeof(object) }, typeof(Main).Module, true);
            var il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, field.DeclaringType);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Castclass, field.FieldType); il.Emit(OpCodes.Stfld, field); il.Emit(OpCodes.Ret);
            return (Action<object, object>)method.CreateDelegate(typeof(Action<object, object>));
        }
        static void BuildAbilityReuseFactory(Type slot)
        {
            var p = _abilitySlotConstructor.GetParameters();
            var signature = new[] { typeof(object), typeof(int), typeof(bool), typeof(object), typeof(int) };
            var create = new DynamicMethod("RTVR_NewAbilitySlot", typeof(object), signature, typeof(Main).Module, true);
            var il = create.GetILGenerator();
            for (int i = 0; i < 5; ++i) { il.Emit(OpCodes.Ldarg, i); if (i == 0 || i == 3) il.Emit(OpCodes.Castclass, p[i].ParameterType); }
            il.Emit(OpCodes.Newobj, _abilitySlotConstructor); il.Emit(OpCodes.Ret);
            _abilityCreateSlot = (Func<object, int, bool, object, int, object>)create.CreateDelegate(typeof(Func<object, int, bool, object, int, object>));
        }
        static Func<object, int, bool, object, int, bool> BuildAbilitySlotCompatibility(Type slot)
        {
            var method = new DynamicMethod("RTVR_AbilitySlotConfiguration", typeof(bool),
                new[] { typeof(object), typeof(int), typeof(bool), typeof(object), typeof(int) }, typeof(Main).Module, true);
            var il = method.GetILGenerator(); var mismatch = il.DefineLabel();
            string[] names = { "<Index>k__BackingField", "IsInCharScreen", "MoveAbilityMode", "WeaponSlotType" };
            for (int i = 0; i < names.Length; ++i)
            {
                var field = AccessTools.Field(slot, names[i]); if (field == null) throw new MissingFieldException(slot.FullName, names[i]);
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, slot); il.Emit(OpCodes.Ldfld, field);
                il.Emit(OpCodes.Ldarg, i + 1); il.Emit(OpCodes.Bne_Un, mismatch);
            }
            il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret); il.MarkLabel(mismatch); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            return (Func<object, int, bool, object, int, bool>)method.CreateDelegate(typeof(Func<object, int, bool, object, int, bool>));
        }
        static Action<object> BuildAbilitySlotReset(Type slot, Type mechanic)
        {
            var method = new DynamicMethod("RTVR_ResetReusedAbilitySlot", typeof(void), new[] { typeof(object) }, typeof(Main).Module, true);
            var il = method.GetILGenerator();
            // These values are initialized by the native constructor but are not
            // unconditionally overwritten by SetMechanicSlot/UpdateResources.
            foreach (string name in new[] { "IsSelected", "IsOnCooldown", "IsAlerted", "IsSelectionBusy", "IsFake", "CurrentAmmo", "MaxAmmo", "CooldownText", "MicroAbilityIcon" })
            {
                var field = AccessTools.Field(slot, name); var setter = AccessTools.PropertySetter(field.FieldType, "Value");
                var valueType = setter.GetParameters()[0].ParameterType;
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, slot); il.Emit(OpCodes.Ldfld, field);
                il.Emit(valueType.IsValueType ? OpCodes.Ldc_I4_0 : OpCodes.Ldnull); il.Emit(OpCodes.Callvirt, setter);
            }
            foreach (string name in new[] { "m_TempTooltipState", "m_TargetSelectionStarted", "m_OriginIcon", "<MechanicActionBarSlot>k__BackingField" })
            {
                var field = AccessTools.Field(slot, name); if (field == null) throw new MissingFieldException(slot.FullName, name);
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, slot);
                il.Emit(field.FieldType.IsValueType ? OpCodes.Ldc_I4_0 : OpCodes.Ldnull); il.Emit(OpCodes.Stfld, field);
            }
            il.Emit(OpCodes.Ret); return (Action<object>)method.CreateDelegate(typeof(Action<object>));
        }
        static void AbilityReusePrefix(object __instance, out AbilityReuseScope __state)
        {
            __state = null;
            if (!_active) return;
            __state = new AbilityReuseScope { Part = __instance, Parent = _abilityReuseScope };
            _abilityReuseScope = __state;
        }
        static IEnumerable<CodeInstruction> AbilityReuseTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(); int clears = 0, constructors = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Callvirt && Equals(instruction.operand, _abilityOriginalClear))
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(Main), nameof(BeginAbilitySlotReuse)); ++clears; }
                else if (instruction.opcode == OpCodes.Newobj && Equals(instruction.operand, _abilitySlotConstructor))
                {
                    // A regular MethodInfo survives subsequent Harmony re-patches
                    // on Mono. Calling a DynamicMethod operand here caused Invalid
                    // IL when CPU profiling was installed after slot reuse.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(Main), nameof(ReuseAbilitySlot));
                    result.Add(instruction);
                    result.Add(new CodeInstruction(OpCodes.Castclass, _abilitySlotConstructor.DeclaringType));
                    ++constructors; continue;
                }
                result.Add(instruction);
            }
            if (clears != 1 || constructors != 2) throw new InvalidOperationException("Native ability rebuild shape changed");
            return result;
        }
        static void BeginAbilitySlotReuse(object part)
        {
            var scope = _abilityReuseScope;
            if (scope == null || !ReferenceEquals(scope.Part, part)) { _abilityNativeClear(part); return; }
            var list = (IList)_abilityRawList(_abilitySlots(part));
            scope.Slots = new object[list.Count]; list.CopyTo(scope.Slots, 0);
            scope.Overdrive = _abilityOverdrive(part);
            // Detach from the existing native collection without disposing VMs
            // that this same synchronous rebuild is about to bind again.
            list.Clear(); _abilityWriteOverdrive(part, null); ++_abilityRebuilds;
        }
        static object ReuseAbilitySlot(object mechanic, int index, bool characterScreen, object move, int weapon)
        {
            var scope = _abilityReuseScope; object old = null;
            if (scope != null && scope.Slots != null)
            {
                if (index >= 0 && index < scope.Slots.Length) old = scope.Slots[index];
                else if (index == -1) old = scope.Overdrive;
            }
            if (old != null && _abilityCanReuse(old, index, characterScreen, move, weapon))
            {
                // Execute native hover/conversion cleanup, retain its EventBus
                // subscription, then bind the current native mechanic slot.
                _abilityPrepareSlot(old); _abilityResetSlot(old); _abilitySetMechanic(old, mechanic);
                if (index == -1) scope.Overdrive = null; else scope.Slots[index] = null;
                ++_abilitySlotReused; return old;
            }
            ++_abilitySlotCreated; return _abilityCreateSlot(mechanic, index, characterScreen, move, weapon);
        }
        static Exception AbilityReuseFinalizer(Exception __exception, AbilityReuseScope __state)
        {
            if (__state == null) return __exception;
            _abilityReuseScope = __state.Parent;
            try
            {
                if (__state.Slots != null) foreach (var slot in __state.Slots) (slot as IDisposable)?.Dispose();
                (__state.Overdrive as IDisposable)?.Dispose();
            }
            catch (Exception e) { if (__exception == null) return e; }
            return __exception;
        }
        static object ActionBarSlotReuseSnapshot()
        {
            return new { Installed = _abilityReuseInstalled, Error = _abilityReuseError,
                Rebuilds = _abilityRebuilds, ReusedSlotModels = _abilitySlotReused, NewSlotModels = _abilitySlotCreated,
                NativeSynchronousBindingPreserved = true, NativeSelectionAndTurnLogicPreserved = true,
                AllocationCountsAreNotMeasuredFrameTimeSavings = true };
        }
    }
}
