using System;
using System.Reflection;
using System.Reflection.Emit;
namespace RTMaquetaXR
{
    // A cached, allocation-free read of the game's actual camera-stack intent.
    // Camera.main == null alone is not a licence to flatten a 3D scene.
    internal static class NativeUiPresentationContract
    {
        internal static Func<bool> Create(Func<string, Type> find)
        {
            var type = find("Kingmaker.Visual.CameraStackManager") ?? throw new MissingMemberException("Native camera stack unavailable");
            var state = type.GetNestedType("CameraStackState", BindingFlags.Public | BindingFlags.NonPublic);
            if (state == null || !state.IsEnum || Enum.GetUnderlyingType(state) != typeof(int))
                throw new MissingMemberException("Native camera stack state changed");
            var field = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            var getter = type.GetProperty("State", BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod();
            if (field == null || field.FieldType != type || getter == null || getter.ReturnType != state || getter.GetParameters().Length != 0)
                throw new MissingMemberException("Native camera stack accessors changed");
            int uiOnly = Convert.ToInt32(Enum.Parse(state, "UiOnly", false));
            var method = new DynamicMethod("RTMaquetaXR_NativeUiOnly", typeof(bool), Type.EmptyTypes, typeof(NativeUiPresentationContract).Module, true);
            var il = method.GetILGenerator(); var missing = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, field); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Brfalse_S, missing);
            il.Emit(OpCodes.Callvirt, getter); il.Emit(OpCodes.Ldc_I4, uiOnly); il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ret);
            il.MarkLabel(missing); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            return (Func<bool>)method.CreateDelegate(typeof(Func<bool>));
        }
    }

}
