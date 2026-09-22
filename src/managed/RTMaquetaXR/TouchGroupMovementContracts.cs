using System;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace RTMaquetaXR
{
    internal sealed class TouchGroupMovementContracts
    {
        internal Func<bool> Allowed;
        internal Func<object> Game;
        internal Func<object, object> Controller, Selection, SelectedProperty, SelectedValue, SelectedUnits, SentUnits;
        internal Func<object, bool> Controllable, CanMove, UiBlocked;
        internal Action<object, object, Vector2, float> Push;
        internal Action<object, object> RestoreSentUnits;
        internal MethodInfo Fill;

        internal static TouchGroupMovementContracts Create(Func<string, Type> lookup)
        {
            var game = lookup("Kingmaker.Game");
            var controller = lookup("Kingmaker.Controllers.Net.SynchronizedDataController");
            var gamepad = lookup("Kingmaker.Controllers.GamepadInputController");
            var selection = lookup("Kingmaker.Controllers.SelectionCharacterController");
            var unit = lookup("Kingmaker.EntitySystem.Entities.BaseUnitEntity");
            var mechanic = lookup("Kingmaker.EntitySystem.Entities.MechanicEntity");
            var reference = lookup("Kingmaker.EntitySystem.Entities.UnitReference");
            var utility = lookup("Kingmaker.UI.Common.UINetUtility");
            var data = lookup("Kingmaker.Controllers.Net.SynchronizedData");
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var selectedProperty = selection.GetField("SelectedUnit", flags) ?? throw new MissingFieldException(selection.FullName, "SelectedUnit");
            if (!selectedProperty.FieldType.IsGenericType || selectedProperty.FieldType.GetGenericArguments()[0] != unit)
                throw new InvalidOperationException("SelectedUnit must retain its exact unit type");
            var unitsGetter = selection.GetMethod("get_SelectedUnits", flags, null, Type.EmptyTypes, null)
                ?? throw new MissingMethodException(selection.FullName, "get_SelectedUnits");
            var sent = TouchSelectionCallFactory.ExactField(controller, "m_SelectedUnits", reference.MakeArrayType());
            return new TouchGroupMovementContracts {
                Allowed = (Func<bool>)TouchSelectionCallFactory.Build(typeof(Func<bool>), TouchSelectionCallFactory.ExactMethod(gamepad, "get_CanProcessInput", typeof(bool), true)),
                Game = (Func<object>)TouchSelectionCallFactory.Build(typeof(Func<object>), TouchSelectionCallFactory.ExactMethod(game, "get_Instance", game, true)),
                Controller = TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(game, "SynchronizedDataController", controller)),
                Selection = TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(game, "SelectionCharacter", selection)),
                SelectedProperty = TouchSelectionCallFactory.FieldGetter(selectedProperty),
                SelectedValue = (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>),
                    TouchSelectionCallFactory.ExactMethod(selectedProperty.FieldType, "get_Value", unit, false)),
                SelectedUnits = (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>), unitsGetter),
                SentUnits = TouchSelectionCallFactory.FieldGetter(sent), RestoreSentUnits = ReferenceSetter(sent),
                Controllable = (Func<object, bool>)TouchSelectionCallFactory.Build(typeof(Func<object, bool>),
                    TouchSelectionCallFactory.ExactMethod(utility, "IsDirectlyControllable", typeof(bool), true, mechanic)),
                CanMove = (Func<object, bool>)TouchSelectionCallFactory.Build(typeof(Func<object, bool>),
                    TouchSelectionCallFactory.ExactMethod(mechanic, "get_CanMove", typeof(bool), false)),
                UiBlocked = BuildUiBlocker(lookup, game),
                Push = (Action<object, object, Vector2, float>)TouchSelectionCallFactory.Build(typeof(Action<object, object, Vector2, float>),
                    TouchSelectionCallFactory.ExactMethod(controller, "PushLeftStickMovement", typeof(void), false, unit, typeof(Vector2), typeof(float))),
                Fill = TouchSelectionCallFactory.ExactMethod(controller, "FillLeftStickData", data, false, data)
            };
        }
        static Func<object, bool> BuildUiBlocker(Func<string, Type> lookup, Type game)
        {
            // Read the actual open-window state. Pointer hover says nothing
            // about whether inventory, a modal or a message box owns gameplay.
            var ui = lookup("Kingmaker.Code.UI.MVVM.RootUIContext");
            var common = lookup("Kingmaker.Code.UI.MVVM.VM.Common.CommonVM");
            var full = lookup("Kingmaker.UI.Models.FullScreenUIType");
            var modal = lookup("Kingmaker.UI.Models.ModalWindowUIType");
            var message = lookup("Kingmaker.Code.UI.MVVM.VM.MessageBox.MessageBoxVM");
            if (Convert.ToInt32(Enum.Parse(full, "Unknown")) != 0 || Convert.ToInt32(Enum.Parse(modal, "Unknown")) != 0)
                throw new InvalidOperationException("Native empty-window enum contract changed");
            var rootGetter = TouchSelectionCallFactory.ExactMethod(game, "get_RootUiContext", ui, false);
            var fullGetter = TouchSelectionCallFactory.ExactMethod(ui, "get_FullScreenUIType", full, false);
            var modalField = TouchSelectionCallFactory.ExactField(ui, "m_ModalWindowUIType", modal);
            var commonGetter = TouchSelectionCallFactory.ExactMethod(ui, "get_CommonVM", common, false);
            var messageField = common.GetField("MessageBoxVM", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (messageField == null || !messageField.FieldType.IsGenericType || messageField.FieldType.GetGenericArguments()[0] != message)
                throw new MissingFieldException("Native common message-box property changed");
            var valueGetter = TouchSelectionCallFactory.ExactMethod(messageField.FieldType, "get_Value", message, false);
            var method = new DynamicMethod("RTMaquetaXR_MovementUiBlocked", typeof(bool), new[] { typeof(object) }, typeof(TouchGroupMovementContracts).Module, true);
            var il = method.GetILGenerator();
            var root = il.DeclareLocal(ui); var current = il.DeclareLocal(common); var property = il.DeclareLocal(messageField.FieldType);
            var blocked = il.DefineLabel(); var clear = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Brfalse, blocked);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, game); il.Emit(OpCodes.Callvirt, rootGetter); il.Emit(OpCodes.Stloc, root);
            il.Emit(OpCodes.Ldloc, root); il.Emit(OpCodes.Brfalse, blocked);
            il.Emit(OpCodes.Ldloc, root); il.Emit(OpCodes.Callvirt, fullGetter); il.Emit(OpCodes.Brtrue, blocked);
            il.Emit(OpCodes.Ldloc, root); il.Emit(OpCodes.Ldfld, modalField); il.Emit(OpCodes.Brtrue, blocked);
            il.Emit(OpCodes.Ldloc, root); il.Emit(OpCodes.Callvirt, commonGetter); il.Emit(OpCodes.Stloc, current);
            il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Brfalse, blocked);
            il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Ldfld, messageField); il.Emit(OpCodes.Stloc, property);
            il.Emit(OpCodes.Ldloc, property); il.Emit(OpCodes.Brfalse, clear);
            il.Emit(OpCodes.Ldloc, property); il.Emit(OpCodes.Callvirt, valueGetter); il.Emit(OpCodes.Brtrue, blocked);
            il.MarkLabel(clear); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            il.MarkLabel(blocked); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
            return (Func<object, bool>)method.CreateDelegate(typeof(Func<object, bool>));
        }
        static Action<object, object> ReferenceSetter(FieldInfo field)
        {
            if (field.IsStatic || field.FieldType.IsValueType || field.IsInitOnly) throw new InvalidOperationException("Movement group snapshot field changed");
            var method = new DynamicMethod("RTMaquetaXR_RestoreMovementGroup", typeof(void), new[] { typeof(object), typeof(object) }, typeof(TouchGroupMovementContracts).Module, true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, field.DeclaringType);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Castclass, field.FieldType); il.Emit(OpCodes.Stfld, field); il.Emit(OpCodes.Ret);
            return (Action<object, object>)method.CreateDelegate(typeof(Action<object, object>));
        }
    }
}
