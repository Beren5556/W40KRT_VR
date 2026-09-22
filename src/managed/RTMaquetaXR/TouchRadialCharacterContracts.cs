using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RTMaquetaXR
{
    // Independent optional binder: a changed initiative panel cannot disable
    // the action wheels. It is the PC vertical tracker, not the bottom party bar.
    internal sealed class TouchRadialCharacterContracts
    {
        internal Type TrackerView, UnitModel, VirtualUnitData, BaseUnit;
        internal PropertyInfo TrackerModel, ViewModel, DataModel, BoundView, Unit, DisplayName, PlayerFaction;
        internal FieldInfo Entries, CurrentView, Prefab, UnitState, Selected, Current, Enemy, Neutral, Unable, WillNotTurn, LostControl;
        internal FieldInfo Wrapper, Subtype, PortraitZone, SubtypeZone, Picture, PortraitSize;
        internal PropertyInfo[] Portraits;
        internal PropertyInfo BoolValue;
        internal Func<object> Game;
        internal Func<object, object> Selection, Camera, Follower, AbilityHandler, Ability;
        internal Action<object, object, bool, bool> Select;
        internal Action<object, object> Focus;
        internal Func<object, Vector3> Position;
        internal Func<object, object> Health;
        internal Func<object, string> HpText;
        internal FieldInfo Player;
        internal Func<object, int> Order;
        internal static TouchRadialCharacterContracts Create(Func<string, Type> resolve)
        {
            Func<string, Type> type = n => resolve(n) ?? throw new TypeLoadException(n);
            var c = CreateNavigation(resolve);
            c.TrackerView = type("Kingmaker.Code.UI.MVVM.View.SurfaceCombat.PC.InitiativeTrackerVerticalPCView");
            var view = type("Kingmaker.Code.UI.MVVM.View.SurfaceCombat.SurfaceCombatUnitOrderView");
            c.UnitModel = type("Kingmaker.Code.UI.MVVM.VM.SurfaceCombat.InitiativeTrackerUnitVM");
            c.VirtualUnitData = type("Kingmaker.Code.UI.MVVM.View.SurfaceCombat.InitiativeTrackerView+TurnVirtualUnitData");
            c.TrackerModel = P(c.TrackerView, "ViewModel"); c.ViewModel = P(view, "ViewModel");
            c.Entries = F(c.TrackerView, "VirtualEntries"); c.CurrentView = F(c.TrackerView, "m_CurrentUnit");
            c.Prefab = F(c.TrackerView, "CombatUnitPrefab");
            c.DataModel = P(c.VirtualUnitData, "ViewModel"); c.BoundView = P(c.VirtualUnitData, "BoundView");
            c.Unit = P(c.UnitModel, "Unit"); c.DisplayName = P(c.UnitModel, "DisplayName");
            c.UnitState = F(c.UnitModel, "UnitState"); c.Selected = F(c.UnitState.FieldType, "IsSelected");
            c.Current = F(c.UnitModel, "IsCurrent"); c.Enemy = F(c.UnitModel, "IsEnemy"); c.Neutral = F(c.UnitModel, "IsNeutral");
            c.Player = F(c.UnitModel, "IsPlayer");
            c.Order = TouchRadialPartyContracts.Reactive<int>(c.UnitModel, "OrderIndex");
            c.Unable = F(c.UnitModel, "IsUnableToAct"); c.WillNotTurn = F(c.UnitModel, "WillNotTakeTurn"); c.LostControl = F(c.UnitModel, "HasControlLossEffects");
            c.BoolValue = P(c.Current.FieldType, "Value");
            c.Wrapper = F(c.UnitModel, "UnitUIWrapper"); c.Subtype = F(c.UnitModel, "UsedSubtypeIcon");
            c.PortraitZone = F(view, "m_CharacetrPortraitZone"); c.SubtypeZone = F(view, "m_NoPortraitZone");
            c.Picture = F(c.PortraitZone.FieldType, "m_Picture"); c.PortraitSize = F(c.PortraitZone.FieldType, "m_Size");
            c.Portraits = new[] { P(c.Wrapper.FieldType, "Icon"), P(c.Wrapper.FieldType, "SmallPortrait"), P(c.Wrapper.FieldType, "MiddlePortrait") };
            var healthField = F(c.UnitModel, "UnitHealthPartVM"); var healthReceiver = TouchSelectionCallFactory.FieldGetter(healthField);
            var healthValue = Getter(P(healthField.FieldType, "Value"));
            c.Health = model => { var value = model == null ? null : healthReceiver(model); return value == null ? null : healthValue(value); };
            c.HpText = TouchRadialPartyContracts.Reactive<string>(type("Kingmaker.Code.UI.MVVM.VM.Party.UnitHealthPartVM"), "HpText");
            return c;
        }
        // Party and initiative are independent UI contracts. Their safe native
        // selection/navigation is identical and does not require either panel.
        internal static TouchRadialCharacterContracts CreateNavigation(Func<string, Type> resolve)
        {
            Func<string, Type> type = n => resolve(n) ?? throw new TypeLoadException(n);
            var c = new TouchRadialCharacterContracts();
            var game = type("Kingmaker.Game"); c.BaseUnit = type("Kingmaker.EntitySystem.Entities.BaseUnitEntity");
            var mechanic = type("Kingmaker.EntitySystem.Entities.MechanicEntity"); c.PlayerFaction = P(mechanic, "IsPlayerFaction");
            c.Position = (Func<object, Vector3>)Call(typeof(Func<object, Vector3>), mechanic, "get_Position", typeof(Vector3), false);
            c.Game = (Func<object>)Call(typeof(Func<object>), game, "get_Instance", game, true);
            var selection = F(game, "SelectionCharacter"); c.Selection = TouchSelectionCallFactory.FieldGetter(selection);
            c.Select = (Action<object, object, bool, bool>)Call(typeof(Action<object, object, bool, bool>), selection.FieldType,
                "SetSelected", typeof(void), false, c.BaseUnit, typeof(bool), typeof(bool));
            var camera = P(game, "CameraController"); c.Camera = Getter(camera);
            var follower = F(camera.PropertyType, "Follower"); c.Follower = TouchSelectionCallFactory.FieldGetter(follower);
            c.Focus = (Action<object, object>)Call(typeof(Action<object, object>), follower.FieldType, "ScrollTo", typeof(void), false, mechanic);
            var handler = P(game, "SelectedAbilityHandler"); c.AbilityHandler = Getter(handler);
            c.Ability = Getter(P(handler.PropertyType, "Ability"));
            return c;
        }
        static FieldInfo F(Type t, string n) => TouchRadialContracts.Field(t, n);
        static PropertyInfo P(Type t, string n) => TouchRadialContracts.Property(t, n);
        static Delegate Call(Type d, Type t, string n, Type r, bool s, params Type[] a) =>
            TouchSelectionCallFactory.Build(d, TouchSelectionCallFactory.ExactMethod(t, n, r, s, a));
        static Func<object, object> Getter(PropertyInfo p) => (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>), p.GetGetMethod(true));
        internal bool Flag(FieldInfo field, object receiver)
        { var value = receiver == null ? null : field.GetValue(receiver); return value != null && (bool)BoolValue.GetValue(value, null); }

        // Use the live filtered/virtualized list (including its native squad
        // visibility), never the full battle roster or currently realized views.
        internal void CopyModels(IList entries, object current, List<object> output)
        {
            output.Clear();
            if (entries != null) foreach (var data in entries)
            {
                if (data == null || !VirtualUnitData.IsInstanceOfType(data)) continue;
                var model = DataModel.GetValue(data, null);
                if (model != null && UnitModel.IsInstanceOfType(model)) output.Add(model);
            }
            if (current != null && UnitModel.IsInstanceOfType(current) && !output.Contains(current)) output.Add(current);
            // Stable native turn order: duplicate future-round entries with the
            // same index retain their original relative position.
            for(int i=1;i<output.Count;i++)
            {
                var value=output[i];int order=Order(value),j=i-1;
                while(j>=0 && Order(output[j])>order){output[j+1]=output[j];j--;}
                output[j+1]=value;
            }
        }
        internal bool SameEntries(IList entries, List<object> snapshot)
        {
            if (entries == null || entries.Count != snapshot.Count) return false;
            for (int i = 0; i < snapshot.Count; ++i) if (!ReferenceEquals(entries[i], snapshot[i])) return false;
            return true;
        }
        internal void Commit(object unit)
        {
            if (unit == null) return;
            var game = Game(); if (game == null) return;
            var handler = AbilityHandler(game);
            bool targeting = handler != null && Ability(handler) != null;
            // Reproduce the native single-click selection path, bypassing ONLY
            // its ability-target dispatcher. Armed skills remain armed; in that
            // case the wheel changes neither selection nor camera.
            if (!targeting && BaseUnit.IsInstanceOfType(unit) && (bool)PlayerFaction.GetValue(unit, null))
            {
                var selection = Selection(game); if (selection != null) Select(selection, unit, false, false);
            }
            // Camera framing belongs exclusively to the explicit double-click route.
        }
    }
}
