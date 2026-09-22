using System;
using System.Collections.Generic;
using UnityEngine;

namespace RTMaquetaXR
{
    // This is the actual startup binder, also executed against the installed
    // game assemblies by the regression test. It resolves/builds delegates only;
    // it never runs game methods, initializes a scene or calls the Unity engine.
    internal sealed class TouchSelectionContracts
    {
        internal Func<object> Instance, Game;
        internal Func<object, bool> Allowed, Controllable;
        internal Action<object> Commit, Cancel, ClearMembers;
        internal Func<object, object> Members, State, Awake, UnitView, ViewEntity, SoftCollider;
        internal Func<object, Vector3> UnitPosition;
        internal Func<object, object, bool> AddMember;
        internal Func<object, int> PointerMode;
        internal Func<GameObject, bool> IsGround;
        internal Type GroundHandlerType, UnitHandlerType, EntityViewType, UnitViewType;

        internal static TouchSelectionContracts Create(Func<string, Type> lookup)
        {
            Func<string, Type> require = name => lookup(name) ?? throw new TypeLoadException(name);
            var box = require("Kingmaker.UI.Selection.MultiplySelection");
            var pointer = require("Kingmaker.Controllers.Clicks.PointerController");
            var pointerMode = require("Kingmaker.Controllers.Clicks.PointerMode");
            var game = require("Kingmaker.Game");
            var state = require("Kingmaker.EntitySystem.PersistentState");
            var unit = require("Kingmaker.EntitySystem.Entities.BaseUnitEntity");
            var mechanic = require("Kingmaker.EntitySystem.Entities.MechanicEntity");
            var network = require("Kingmaker.UI.Common.UINetUtility");
            var c = new TouchSelectionContracts {
                GroundHandlerType = require("Kingmaker.Controllers.Clicks.Handlers.ClickGroundHandler"),
                UnitHandlerType = require("Kingmaker.Controllers.Clicks.Handlers.ClickUnitHandler"),
                EntityViewType = require("Kingmaker.View.EntityViewBase"),
                UnitViewType = require("Kingmaker.View.UnitEntityView")
            };
            c.Instance = (Func<object>)TouchSelectionCallFactory.Build(typeof(Func<object>), TouchSelectionCallFactory.ExactMethod(box, "get_Instance", box, true));
            c.Allowed = (Func<object, bool>)TouchSelectionCallFactory.Build(typeof(Func<object, bool>), TouchSelectionCallFactory.ExactMethod(box, "get_ShouldMultiSelect", typeof(bool), false));
            c.Commit = (Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>), TouchSelectionCallFactory.ExactMethod(box, "SelectEntities", typeof(void), false));
            c.Cancel = (Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>), TouchSelectionCallFactory.ExactMethod(box, "Cancel", typeof(void), false));
            c.PointerMode = (Func<object, int>)TouchSelectionCallFactory.Build(typeof(Func<object, int>), TouchSelectionCallFactory.ExactMethod(pointer, "get_Mode", pointerMode, false));
            c.IsGround = (Func<GameObject, bool>)TouchSelectionCallFactory.Build(typeof(Func<GameObject, bool>), TouchSelectionCallFactory.ExactMethod(pointer, "IsGround", typeof(bool), true, typeof(GameObject)));
            c.Controllable = (Func<object, bool>)TouchSelectionCallFactory.Build(typeof(Func<object, bool>), TouchSelectionCallFactory.ExactMethod(network, "IsDirectlyControllable", typeof(bool), true, mechanic));
            c.UnitView = (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>), TouchSelectionCallFactory.ExactMethod(unit, "get_View", c.UnitViewType, false));
            c.UnitPosition = (Func<object, Vector3>)TouchSelectionCallFactory.Build(typeof(Func<object, Vector3>), TouchSelectionCallFactory.ExactMethod(unit, "get_Position", typeof(Vector3), false));
            c.ViewEntity = (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>), TouchSelectionCallFactory.ExactMethod(c.UnitViewType, "get_EntityData", unit, false));
            c.Game = (Func<object>)TouchSelectionCallFactory.Build(typeof(Func<object>), TouchSelectionCallFactory.ExactMethod(game, "get_Instance", game, true));
            c.State = TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(game, "State", state));
            c.Awake = TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(state, "AllBaseAwakeUnits", typeof(List<>).MakeGenericType(unit)));
            c.SoftCollider = TouchSelectionCallFactory.FieldGetter(TouchSelectionCallFactory.ExactField(c.UnitViewType, "m_SoftCollider", typeof(CapsuleCollider)));
            var members = TouchSelectionCallFactory.ExactField(box, "m_UnitsInFrame", typeof(HashSet<>).MakeGenericType(c.UnitViewType));
            c.Members = TouchSelectionCallFactory.FieldGetter(members);
            c.ClearMembers = (Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>), TouchSelectionCallFactory.ExactMethod(members.FieldType, "Clear", typeof(void), false));
            c.AddMember = (Func<object, object, bool>)TouchSelectionCallFactory.Build(typeof(Func<object, object, bool>), TouchSelectionCallFactory.ExactMethod(members.FieldType, "Add", typeof(bool), false, c.UnitViewType));
            return c;
        }
    }
}
