using System;
using System.Reflection;
using System.Reflection.Emit;

namespace RTMaquetaXR
{
    internal sealed class TouchCombatFirstPersonContracts
    {
        internal MethodInfo RunCommand, TurnChanged;
        internal Func<object, object> Turn, CurrentUnit, Handle, Unit;
        internal Func<object, bool> Active, PlayerTurn, Preparation;
        internal static TouchCombatFirstPersonContracts Create(Func<string, Type> lookup)
        {
            var game = lookup("Kingmaker.Game");
            var turn = lookup("Kingmaker.Controllers.TurnBased.TurnController");
            var order = lookup("Kingmaker.Controllers.TurnBased.TurnOrderQueue");
            var unit = lookup("Kingmaker.EntitySystem.Entities.MechanicEntity");
            var move = lookup("Kingmaker.Controllers.Units.VirtualMoveCommand");
            var handle = lookup("Kingmaker.UnitLogic.Commands.Base.UnitCommandHandle");
            return new TouchCombatFirstPersonContracts {
                RunCommand = TouchSelectionCallFactory.ExactMethod(move, "RunCommand", typeof(void), false),
                TurnChanged = TouchSelectionCallFactory.ExactMethod(order, "set_CurrentUnit", typeof(void), false, unit),
                Turn = TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(game, "TurnController", turn)),
                CurrentUnit = GetObject(turn, "get_CurrentUnit", unit),
                Handle = GetObject(move, "get_CmdHandle", handle),
                Unit = MoveUnit(move, lookup),
                Active = GetBool(turn, "get_TurnBasedModeActive"),
                PlayerTurn = GetBool(turn, "get_IsPlayerTurn"),
                Preparation = GetBool(turn, "get_IsPreparationTurn")
            };
        }
        static Func<object, object> MoveUnit(Type move, Func<string, Type> lookup)
        {
            var reference = lookup("Kingmaker.EntitySystem.Entities.UnitReference");
            var field = TouchSelectionCallFactory.ExactField(move, "Unit", reference);
            var resolve = TouchSelectionCallFactory.ExactMethod(lookup("Kingmaker.Mechanics.Entities.UnitReferenceExtensions"),
                "ToBaseUnitEntity", lookup("Kingmaker.EntitySystem.Entities.BaseUnitEntity"), true, reference);
            var getter = new DynamicMethod("RTMaquetaXR_CombatMoveUnit", typeof(object), new[] { typeof(object) }, typeof(TouchCombatFirstPersonContracts).Module, true);
            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, move); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Call, resolve); il.Emit(OpCodes.Ret);
            return (Func<object, object>)getter.CreateDelegate(typeof(Func<object, object>));
        }
        static Func<object, object> GetObject(Type type, string name, Type result) =>
            (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>), TouchSelectionCallFactory.ExactMethod(type, name, result, false));
        static Func<object, bool> GetBool(Type type, string name) =>
            (Func<object, bool>)TouchSelectionCallFactory.Build(typeof(Func<object, bool>), TouchSelectionCallFactory.ExactMethod(type, name, typeof(bool), false));
    }
}
