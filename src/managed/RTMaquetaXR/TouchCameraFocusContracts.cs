using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RTMaquetaXR
{
    internal sealed class TouchCameraFocusContracts
    {
        internal MethodInfo Click, GroundClick, SelectAll, AreaActivated;
        internal Func<object, object> Player, Party;
        internal Func<object, bool> Dead, InGame, SceneLoaded;
        internal Func<object, object> HoldingState;
        internal static TouchCameraFocusContracts Create(Func<string, Type> lookup)
        {
            var game = lookup("Kingmaker.Game");
            var player = lookup("Kingmaker.Player");
            var unit = lookup("Kingmaker.EntitySystem.Entities.BaseUnitEntity");
            var mechanic = lookup("Kingmaker.EntitySystem.Entities.MechanicEntity");
            var click = lookup("Kingmaker.Controllers.Clicks.Handlers.ClickUnitHandler");
            var ground = lookup("Kingmaker.Controllers.Clicks.Handlers.ClickGroundHandler");
            var selection = lookup("Kingmaker.UI.Selection.SelectionManagerBase");
            var entity = lookup("Kingmaker.EntitySystem.Entities.Base.Entity");
            var scene = lookup("Kingmaker.EntitySystem.SceneEntitiesState");
            return new TouchCameraFocusContracts {
                Click = TouchSelectionCallFactory.ExactMethod(click, "OnClick", typeof(bool), false,
                    typeof(GameObject), typeof(Vector3), typeof(int), typeof(bool), typeof(bool)),
                GroundClick = TouchSelectionCallFactory.ExactMethod(ground, "OnClick", typeof(bool), false,
                    typeof(GameObject), typeof(Vector3), typeof(int), typeof(bool), typeof(bool)),
                SelectAll = TouchSelectionCallFactory.ExactMethod(selection, "SelectAll", typeof(void), false, typeof(IEnumerable<>).MakeGenericType(unit)),
                AreaActivated = TouchSelectionCallFactory.ExactMethod(game, "HandleActiveAreaChanged", typeof(void), false, typeof(bool)),
                Player = (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>), TouchSelectionCallFactory.ExactMethod(game, "get_Player", player, false)),
                Party = (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>), TouchSelectionCallFactory.ExactMethod(player, "get_Party", typeof(List<>).MakeGenericType(unit), false)),
                Dead = (Func<object, bool>)TouchSelectionCallFactory.Build(typeof(Func<object, bool>), TouchSelectionCallFactory.ExactMethod(mechanic, "get_IsDeadOrUnconscious", typeof(bool), false)),
                InGame = (Func<object, bool>)TouchSelectionCallFactory.Build(typeof(Func<object, bool>), TouchSelectionCallFactory.ExactMethod(entity, "get_IsInGame", typeof(bool), false)),
                HoldingState = (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>), TouchSelectionCallFactory.ExactMethod(entity, "get_HoldingState", scene, false)),
                SceneLoaded = (Func<object, bool>)TouchSelectionCallFactory.Build(typeof(Func<object, bool>), TouchSelectionCallFactory.ExactMethod(scene, "get_IsSceneLoaded", typeof(bool), false))
            };
        }
    }
}
