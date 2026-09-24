using System;
using System.Reflection;
using System.Reflection.Emit;
namespace RTMaquetaXR
{
    // A cached, allocation-free read of the game's actual camera-stack intent.
    // Camera.main == null alone is not a licence to flatten a 3D scene.
    internal static class NativeUiPresentationContract
    {
        // Exact native fullscreen ownership also covers windows whose menu bar
        // is hidden or whose contents live in a different UI scene. Read only:
        // native close guards, animations and child layouts remain authoritative.
        internal static readonly string[] ManagementNames81 = {
            "EscapeMenu", "SaveLoad", "LevelUp", "Inventory", "CharacterScreen", "Journal",
            "Vendor", "Settings", "Encyclopedia", "ShipCustomization", "ColonyManagement",
            "CargoManagement", "Chargen", "GroupChanger", "Loot", "Augmentations"
        };
        internal static Func<bool> CreateManagement81(Func<string, Type> find)
        {
            var game = find("Kingmaker.Game") ?? throw new MissingMemberException("Game unavailable");
            var root = find("Kingmaker.Code.UI.MVVM.RootUIContext") ?? throw new MissingMemberException("UI context unavailable");
            var kind = find("Kingmaker.UI.Models.FullScreenUIType") ?? throw new MissingMemberException("UI type unavailable");
            var instance = game.GetProperty("Instance")?.GetGetMethod();
            var context = game.GetProperty("RootUiContext")?.GetGetMethod();
            var current = root.GetProperty("FullScreenUIType")?.GetGetMethod();
            if(instance == null || context?.ReturnType != root || current?.ReturnType != kind || !kind.IsEnum || Enum.GetUnderlyingType(kind) != typeof(int))
                throw new MissingMemberException("Native management ownership changed");
            var method = new DynamicMethod("RTMaquetaXR_Management81", typeof(bool), Type.EmptyTypes, typeof(NativeUiPresentationContract).Module, true);
            var il = method.GetILGenerator(); var missing = il.DefineLabel(); var yes = il.DefineLabel(); var value = il.DeclareLocal(typeof(int));
            il.Emit(OpCodes.Call, instance); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Brfalse, missing);
            il.Emit(OpCodes.Callvirt, context); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Brfalse, missing);
            il.Emit(OpCodes.Callvirt, current); il.Emit(OpCodes.Stloc, value);
            foreach(var name in ManagementNames81) {
                il.Emit(OpCodes.Ldloc, value); il.Emit(OpCodes.Ldc_I4, Convert.ToInt32(Enum.Parse(kind, name, false))); il.Emit(OpCodes.Beq, yes);
            }
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            il.MarkLabel(missing); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            il.MarkLabel(yes); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
            return (Func<bool>)method.CreateDelegate(typeof(Func<bool>));
        }
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
