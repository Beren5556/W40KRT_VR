using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace RTMaquetaXR
{
    // SetMechanicSlot refreshes synchronously, then schedules UpdateResources at
    // +0.5 s. The native code discards that subscription, even when the slot is
    // disposed on a selection/turn transition. Keep only the latest subscription
    // per live slot and cancel obsolete work at its native disposal boundary.
    internal sealed class ActionBarPendingRefresh
    {
        internal IDisposable Subscription;
        internal Action Original;
        internal readonly Action Invoke;
        internal bool Disposed;
        internal ActionBarPendingRefresh() { Invoke = Run; }
        void Run()
        {
            var action = Original; Original = null; Subscription = null;
            if (!Disposed && action != null) action();
        }
        internal bool Cancel()
        {
            var subscription = Subscription; Subscription = null; Original = null;
            if (subscription == null) return false;
            subscription.Dispose(); return true;
        }
    }

    public static partial class Main
    {
        static readonly ConditionalWeakTable<object, ActionBarPendingRefresh> _actionBarPending =
            new ConditionalWeakTable<object, ActionBarPendingRefresh>();
        static Func<Action, float, bool, IDisposable> _actionBarNativeDelay;
        static MethodInfo _actionBarDelayMethod;
        static bool _actionBarRefreshInstalled;
        static string _actionBarRefreshError;
        static long _actionBarRefreshScheduled, _actionBarRefreshReplaced, _actionBarRefreshDisposed;

        static void InstallActionBarRefreshOptimizations()
        {
            if (_actionBarRefreshInstalled) return;
            try
            {
                var slot = AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.ActionBar.ActionBarSlotVM");
                var mechanic = AccessTools.TypeByName("Kingmaker.UI.Models.UnitSettings.MechanicActionBarSlot");
                var invoker = AccessTools.TypeByName("Owlcat.Runtime.UniRx.DelayedInvoker");
                _actionBarDelayMethod = TouchSelectionCallFactory.ExactMethod(invoker, "InvokeInTime", typeof(IDisposable), true,
                    typeof(Action), typeof(float), typeof(bool));
                _actionBarNativeDelay = (Func<Action, float, bool, IDisposable>)Delegate.CreateDelegate(
                    typeof(Func<Action, float, bool, IDisposable>), _actionBarDelayMethod);
                var change = TouchSelectionCallFactory.ExactMethod(slot, "SetMechanicSlot", typeof(void), false, mechanic);
                var dispose = TouchSelectionCallFactory.ExactMethod(slot, "DisposeImplementation", typeof(void), false);
                _harmony.Patch(change, transpiler: new HarmonyMethod(typeof(Main), nameof(ActionBarRefreshTranspiler)));
                _harmony.Patch(dispose, prefix: new HarmonyMethod(typeof(Main), nameof(ActionBarDisposeRefreshPrefix)));
                _actionBarRefreshInstalled = true;
                _log.Log("[performance] Obsolete action-bar slot refresh cancellation installed; synchronous model updates and native 0.5 s refresh retained");
            }
            catch (Exception e) { _actionBarRefreshError = e.Message; _log.Error("[performance] Action-bar refresh optimization unavailable: " + e.Message); }
        }
        static IEnumerable<CodeInstruction> ActionBarRefreshTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(instructions); int replaced = 0;
            foreach (var instruction in result)
                if (instruction.opcode == OpCodes.Call && Equals(instruction.operand, _actionBarDelayMethod))
                { instruction.operand = AccessTools.Method(typeof(Main), nameof(ScheduleActionBarRefresh)); ++replaced; }
            if (replaced != 1) throw new InvalidOperationException("SetMechanicSlot delayed refresh contract changed");
            return result;
        }
        static IDisposable ScheduleActionBarRefresh(Action action, float seconds, bool realtime)
        {
            if (!_active || action == null || action.Target == null) return _actionBarNativeDelay(action, seconds, realtime);
            var pending = _actionBarPending.GetValue(action.Target, CreateActionBarPending);
            if (pending.Cancel()) ++_actionBarRefreshReplaced;
            pending.Disposed = false; pending.Original = action;
            // Preserve the exact native scheduler, clock mode and deadline.
            // The immediate UpdateResources call earlier in SetMechanicSlot is
            // not intercepted, so wheels and availability always see live data.
            pending.Subscription = _actionBarNativeDelay(pending.Invoke, seconds, realtime);
            ++_actionBarRefreshScheduled;
            return pending.Subscription;
        }
        static ActionBarPendingRefresh CreateActionBarPending(object unused) { return new ActionBarPendingRefresh(); }
        static void ActionBarDisposeRefreshPrefix(object __instance)
        {
            ActionBarPendingRefresh pending;
            if (!_actionBarPending.TryGetValue(__instance, out pending)) return;
            pending.Disposed = true;
            if (pending.Cancel()) ++_actionBarRefreshDisposed;
            _actionBarPending.Remove(__instance);
        }
        static object ActionBarRefreshSnapshot()
        {
            return new { Installed = _actionBarRefreshInstalled, Error = _actionBarRefreshError,
                Scheduled = _actionBarRefreshScheduled, ReplacedPendingRefreshes = _actionBarRefreshReplaced,
                CancelledDisposedSlotRefreshes = _actionBarRefreshDisposed,
                NativeSynchronousRefreshPreserved = true, NativeDelaySeconds = 0.5,
                AvoidedCallbacksAreNotMeasuredCpuSavings = true };
        }
    }
}
