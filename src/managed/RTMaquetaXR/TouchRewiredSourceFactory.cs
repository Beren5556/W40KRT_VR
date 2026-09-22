using System;
using System.Reflection;
using System.Reflection.Emit;

namespace RTMaquetaXR
{
    // Engine-independent factory so exact interface-return/ref-int prefixes can
    // be exercised with the installed Harmony assembly in a disposable process.
    internal static class TouchRewiredSourceFactory
    {
        internal static DynamicMethod Build(MethodInfo method, MethodInfo ownership)
        {
            bool count = method.Name == "GetMouseInputSourceCount";
            if ((!count && method.Name != "GetMouseInputSource") || method.GetParameters().Length != (count ? 1 : 2) ||
                (count && method.ReturnType != typeof(int)) || (!count && !method.ReturnType.IsInterface))
                throw new InvalidOperationException("Rewired mouse source contract changed");
            var owner = method.DeclaringType;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var defaultPlayer = owner.GetMethod("IsDefaultPlayer", flags, null, new[] { typeof(int) }, null)
                ?? throw new MissingMethodException(owner.FullName, "IsDefaultPlayer");
            var defaultMouse = owner.GetProperty("defaultMouseInputSource", flags)?.GetGetMethod(true)
                ?? throw new MissingMethodException(owner.FullName, "defaultMouseInputSource");
            var types = count ? new[] { typeof(object), typeof(int), typeof(int).MakeByRefType() } :
                new[] { typeof(object), typeof(int), typeof(int), method.ReturnType.MakeByRefType() };
            var prefix = new DynamicMethod("RTMaquetaXR_" + method.Name, typeof(bool), types, typeof(TouchRewiredSourceFactory).Module, true);
            prefix.DefineParameter(1, ParameterAttributes.None, "__instance");
            prefix.DefineParameter(2, ParameterAttributes.None, "__0");
            if (!count) prefix.DefineParameter(3, ParameterAttributes.None, "__1");
            prefix.DefineParameter(types.Length, ParameterAttributes.None, "__result");
            var il = prefix.GetILGenerator(); var owned = il.DefineLabel(); var none = il.DefineLabel();
            il.Emit(OpCodes.Call, ownership);
            il.Emit(OpCodes.Brtrue_S, owned); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
            il.MarkLabel(owned);
            // Keep the module's default player. Its stored multiplayer and
            // virtual-cursor sources are untouched and resume after VR stops.
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, owner); il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, defaultPlayer); il.Emit(OpCodes.Brfalse_S, none);
            if (!count) { il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Brtrue_S, none); }
            il.Emit(OpCodes.Ldarg, types.Length - 1);
            if (count) { il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stind_I4); }
            else
            {
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, owner);
                il.Emit(OpCodes.Call, defaultMouse); il.Emit(OpCodes.Stind_Ref);
            }
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            il.MarkLabel(none); il.Emit(OpCodes.Ldarg, types.Length - 1);
            if (count) { il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stind_I4); }
            else { il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stind_Ref); }
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            return prefix;
        }
    }
}
