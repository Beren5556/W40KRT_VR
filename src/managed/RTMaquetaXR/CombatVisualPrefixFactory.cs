using System;
using System.Reflection;
using System.Reflection.Emit;

namespace RTMaquetaXR
{
    internal static class CombatVisualPrefixFactory
    {
        internal static DynamicMethod Build(Type listType, MethodInfo gate)
        {
            if (!listType.IsGenericType || listType.GetGenericTypeDefinition() != typeof(System.Collections.Generic.List<>))
                throw new ArgumentException("Combat highlighter return contract must be a List<T>");
            if (!gate.IsStatic || gate.ReturnType != typeof(bool) || gate.GetParameters().Length != 1 || gate.GetParameters()[0].ParameterType != typeof(object))
                throw new ArgumentException("Combat highlight scope must be bool(object)");
            var method = new DynamicMethod("RTMaquetaXR_CombatUnitRenderers", typeof(bool),
                new[] { typeof(object), listType.MakeByRefType() }, typeof(CombatVisualPrefixFactory), true);
            method.DefineParameter(1, ParameterAttributes.None, "__instance");
            method.DefineParameter(2, ParameterAttributes.None, "__result");
            var il = method.GetILGenerator(); var ordinary = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, gate); il.Emit(OpCodes.Brfalse_S, ordinary);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stind_Ref);
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            il.MarkLabel(ordinary); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
            return method;
        }
    }
}
