using System;
using System.Reflection;
using System.Reflection.Emit;

namespace RTMaquetaXR
{
    internal static class TouchSelectionCallFactory
    {
        // Owlcat hides typed properties (View/EntityData) at several levels.
        // GetProperty(name), including AccessTools.PropertyGetter, is ambiguous
        // even when the nearest class has the getter we need. Resolve the full
        // CLR accessor signature on each declaring type instead.
        internal static MethodInfo ExactMethod(Type type, string name, Type result, bool isStatic, params Type[] arguments)
        {
            if (type == null || result == null) throw new MissingMethodException("Missing Touch selection contract type: " + name);
            for (var current = type; current != null; current = current.BaseType)
            {
                MethodInfo found = null;
                foreach (var method in current.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (method.Name != name || method.IsStatic != isStatic || method.ReturnType != result || method.ContainsGenericParameters) continue;
                    var parameters = method.GetParameters();
                    if (parameters.Length != arguments.Length) continue;
                    bool match = true;
                    for (int i = 0; i < parameters.Length; ++i) if (parameters[i].ParameterType != arguments[i]) { match = false; break; }
                    if (!match) continue;
                    if (found != null) throw new AmbiguousMatchException("Duplicate exact Touch selection contract: " + type.FullName + "." + name);
                    found = method;
                }
                if (found != null) return found;
            }
            throw new MissingMethodException(type.FullName, name + " -> " + result.FullName);
        }

        internal static FieldInfo ExactField(Type type, string name, Type fieldType)
        {
            if (type == null || fieldType == null) throw new MissingFieldException("Missing Touch selection contract type: " + name);
            for (var current = type; current != null; current = current.BaseType)
                foreach (var field in current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    if (field.Name == name && field.FieldType == fieldType) return field;
            throw new MissingFieldException(type.FullName, name + " : " + fieldType.FullName);
        }

        // Compile the actual installed contracts once: no Invoke, argument
        // arrays or closed delegate allocation while a rectangle is dragged.
        internal static Delegate Build(Type delegateType, MethodInfo method)
        {
            if (method == null) throw new MissingMethodException("Missing Touch multiple-selection method");
            var signature = delegateType.GetMethod("Invoke");
            var parameters = signature.GetParameters();
            var types = new Type[parameters.Length];
            for (int i = 0; i < types.Length; ++i) types[i] = parameters[i].ParameterType;
            int receiver = method.IsStatic ? 0 : 1;
            var actual = method.GetParameters();
            if (actual.Length + receiver != types.Length) throw new InvalidOperationException("Selection delegate arity: " + method);
            for (int i = 0; i < actual.Length; ++i)
                if (actual[i].ParameterType != types[i + receiver] && !(types[i + receiver] == typeof(object) && !actual[i].ParameterType.IsValueType && !actual[i].ParameterType.IsByRef))
                    throw new InvalidOperationException("Selection delegate parameter: " + method);
            if (method.ReturnType != signature.ReturnType && !(signature.ReturnType == typeof(object) && !method.ReturnType.IsValueType) &&
                !(signature.ReturnType == typeof(int) && method.ReturnType.IsEnum)) throw new InvalidOperationException("Selection delegate result: " + method);
            var dynamic = new DynamicMethod("RTMaquetaXR_" + method.Name, signature.ReturnType, types, typeof(TouchSelectionCallFactory).Module, true);
            var il = dynamic.GetILGenerator();
            if (!method.IsStatic) { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, method.DeclaringType); }
            for (int i = 0; i < actual.Length; ++i)
            {
                il.Emit(OpCodes.Ldarg, i + receiver);
                if (types[i + receiver] == typeof(object) && actual[i].ParameterType != typeof(object)) il.Emit(OpCodes.Castclass, actual[i].ParameterType);
            }
            il.Emit(method.IsStatic ? OpCodes.Call : OpCodes.Callvirt, method);
            if (signature.ReturnType == typeof(int) && method.ReturnType.IsEnum) il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ret);
            return dynamic.CreateDelegate(delegateType);
        }

        internal static Func<object, object> FieldGetter(FieldInfo field)
        {
            if (field == null) throw new MissingFieldException("Missing Touch selection field");
            if (field.IsStatic || field.FieldType.IsValueType) throw new InvalidOperationException("Selection reference field: " + field);
            var dynamic = new DynamicMethod("RTMaquetaXR_Field_" + field.Name, typeof(object), new[] { typeof(object) }, typeof(TouchSelectionCallFactory).Module, true);
            var il = dynamic.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, field.DeclaringType); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
            return (Func<object, object>)dynamic.CreateDelegate(typeof(Func<object, object>));
        }

    }
}
