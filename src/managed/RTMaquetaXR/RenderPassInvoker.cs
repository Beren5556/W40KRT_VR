using System;
using System.Reflection;
using System.Reflection.Emit;

namespace RTMaquetaXR
{
    internal static class RenderPassInvoker
    {
        // Keep virtual dispatch and propagate the original exception directly.
        // Reflection.Invoke would wrap it and allocate argument arrays per pass.
        internal static Action<object, object> Create(MethodInfo method)
        {
            if (method == null || method.DeclaringType == null || method.IsStatic || method.ReturnType != typeof(void) ||
                method.DeclaringType.IsValueType || method.ContainsGenericParameters)
                throw new ArgumentException("Expected an instance render method", nameof(method));
            var args = method.GetParameters();
            if (args.Length != 1 || args[0].ParameterType.IsValueType || args[0].ParameterType.IsByRef)
                throw new ArgumentException("Expected one reference context", nameof(method));
            var dynamic = new DynamicMethod("RTMaquetaXR_OriginalPass", typeof(void),
                new[] { typeof(object), typeof(object) }, typeof(RenderPassInvoker).Module, true);
            var il = dynamic.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, method.DeclaringType);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Castclass, args[0].ParameterType);
            il.Emit(OpCodes.Callvirt, method); il.Emit(OpCodes.Ret);
            return (Action<object, object>)dynamic.CreateDelegate(typeof(Action<object, object>));
        }
    }
}
