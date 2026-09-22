using System;
using System.Reflection;

namespace RTMaquetaXR
{
    internal sealed class TouchRadialCombatContracts
    {
        internal FieldInfo CurrentCombatUnit;
        internal PropertyInfo CurrentCombatValue, CombatActor, Visible, VisibleValue;
        internal static TouchRadialCombatContracts Create(TouchRadialContracts actions)
        {
            var c = new TouchRadialCombatContracts();
            c.CurrentCombatUnit = TouchRadialContracts.Field(actions.BarModel.PropertyType, "CurrentCombatUnit");
            c.CurrentCombatValue = TouchRadialContracts.Property(c.CurrentCombatUnit.FieldType, "Value");
            c.CombatActor = TouchRadialContracts.Property(c.CurrentCombatValue.PropertyType, "Unit");
            c.Visible = TouchRadialContracts.Property(actions.BarModel.PropertyType, "IsVisible");
            c.VisibleValue = TouchRadialContracts.Property(c.Visible.PropertyType, "Value");
            return c;
        }
        internal object Actor(object bar)
        {
            var current = bar == null ? null : CurrentCombatUnit.GetValue(bar);
            var model = current == null ? null : CurrentCombatValue.GetValue(current, null);
            return model == null ? null : CombatActor.GetValue(model, null);
        }
        internal bool Available(object bar)
        {
            var value = bar == null ? null : Visible.GetValue(bar, null);
            return value != null && (bool)VisibleValue.GetValue(value, null);
        }
        internal bool Matches(object bar, object turnUnit) => turnUnit != null && ReferenceEquals(Actor(bar), turnUnit);
    }
}
