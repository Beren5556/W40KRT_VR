using System;
using System.Reflection;
using System.Reflection.Emit;

namespace RTMaquetaXR
{
    // One wrapper at the game's existing interface dispatch. It preserves the
    // original exactly-once call and exception even if observation itself fails.
    internal sealed class ControllerTickDispatch
    {
        readonly Action<object> tick;
        readonly Func<long> timestamp;
        readonly Action<int, object, long, long> observe;
        internal ControllerTickDispatch(Type contract, Func<long> timestamp, Action<int, object, long, long> observe)
        {
            var target = contract?.GetMethod("Tick", Type.EmptyTypes);
            if (contract == null || !contract.IsInterface || target == null || target.IsStatic || target.ReturnType != typeof(void))
                throw new InvalidOperationException("Expected installed IControllerTick.Tick() void contract");
            var method = new DynamicMethod("RTMaquetaXR_OriginalControllerTick", typeof(void), new[] { typeof(object) }, typeof(ControllerTickDispatch).Module, true);
            var il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, contract);
            il.Emit(OpCodes.Callvirt, target); il.Emit(OpCodes.Ret);
            tick = (Action<object>)method.CreateDelegate(typeof(Action<object>));
            this.timestamp = timestamp; this.observe = observe;
        }
        internal void Invoke(object controller, bool profile, int frame)
        {
            if (!profile) { tick(controller); return; }
            long started = timestamp();
            try { tick(controller); }
            finally
            {
                long ended = timestamp();
                try { observe(frame, controller, started, ended); }
                catch { /* Observation must not replace the game's exception. */ }
            }
        }
    }
}
