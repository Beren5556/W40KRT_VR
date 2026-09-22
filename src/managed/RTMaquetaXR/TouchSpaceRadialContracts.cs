using System;
using System.Collections;
using System.Reflection;

namespace RTMaquetaXR
{
    // Resolve the installed PC view and the original slot VMs once. All live
    // reads below are cached delegates, with no scene search or type scan.
    internal sealed class TouchSpaceRadialContracts
    {
        internal Func<object> Game;
        internal Func<object, object> Root, Ui, ViewModel, Weapons, Posts, WeaponGroups, OwnGroup, PostList, PostGroup;
        internal Func<object, object> GroupSlots, WeaponUnit, Turn, TurnUnit, Area, Player, PlayerShip, Input, Exit;
        internal Func<object, object> SlotMechanic, SlotOwner, SlotIcon;
        internal Func<object, object> PostPortrait;
        internal Func<object, bool> SlotCooling;
        internal Func<object, string> SlotCooldown, PostDuration;
        internal Type ShipWeaponMechanic;
        internal Func<object, object> ShipWeaponSlot, ActiveWeapon, WeaponIcon;
        internal Func<object, string> WeaponName;
        internal Func<object, string> GroupLabel, SlotTitle;
        internal Func<object, bool> PlayerTurn, EndingTurn, InputLocked, WeaponsTurn, PostsTurn, PostBlocked;
        internal Func<object, bool> ExitActive, Empty, CharScreen, Possible, Convert, MechanicPossible, MechanicConvert, MechanicLocked;
        internal Func<object, int> Round;
        internal Action<object> Click;
        internal PcUiPath ViewPath;

        internal static TouchSpaceRadialContracts Create(Func<string, Type> resolve)
        {
            Func<string, Type> type = n => resolve(n) ?? throw new TypeLoadException(n);
            var c = new TouchSpaceRadialContracts();
            var game = type("Kingmaker.Game");
            c.Game = (Func<object>)TouchSelectionCallFactory.Build(typeof(Func<object>), TouchSelectionCallFactory.ExactMethod(game, "get_Instance", game, true));
            var root = PcUiPath.Property(game, "RootUiContext"); c.Root = PcUiPath.Getter(root);
            c.Ui = Field(root.PropertyType, "m_UIView");
            c.ViewPath = PcUiPath.Create(type("Kingmaker.Code.UI.MVVM.View.Space.PC.SpacePCView"), "m_StaticPartPCView", "m_SpaceCombatPCView");
            var model = PcUiPath.Property(c.ViewPath.ValueType, "ViewModel"); c.ViewModel = PcUiPath.Getter(model);
            var vm = model.PropertyType;
            c.Weapons = Field(vm, "ShipWeaponsPanelVM"); c.Posts = Field(vm, "ShipPostsPanelVM"); c.Exit = Field(vm, "ExitBattlePopupVM");
            c.ExitActive = Reactive<bool>(PcUiPath.Field(vm, "ExitBattlePopupVM").FieldType, "IsActive");
            var weapons = PcUiPath.Field(vm, "ShipWeaponsPanelVM").FieldType;
            c.WeaponGroups = Field(weapons, "WeaponAbilitiesGroups"); c.OwnGroup = Field(weapons, "AbilitiesGroup");
            c.WeaponUnit = Property(weapons, "Unit"); c.WeaponsTurn = Reactive<bool>(weapons, "IsPlayerTurn");
            var posts = PcUiPath.Field(vm, "ShipPostsPanelVM").FieldType;
            c.PostList = Field(posts, "Posts"); c.PostsTurn = Reactive<bool>(posts, "IsPlayerTurn");
            var post = type("Kingmaker.Code.UI.MVVM.VM.SpaceCombat.Components.ShipPostVM");
            c.PostGroup = Field(post, "AbilitiesGroup"); c.PostBlocked = Reactive<bool>(post, "IsPostBlocked");
            c.PostPortrait = Field(post,"Portrait"); c.PostDuration = Reactive<string>(post,"BlockDuration");
            var group = type("Kingmaker.Code.UI.MVVM.VM.SpaceCombat.Components.AbilitiesGroupVM");
            c.GroupSlots = Field(group, "Slots");
            var label = Field(group, "GroupLabel"); c.GroupLabel = value => label(value) as string;
            var turn = PcUiPath.Field(game, "TurnController"); c.Turn = TouchSelectionCallFactory.FieldGetter(turn);
            c.TurnUnit = Property(turn.FieldType, "CurrentUnit"); c.PlayerTurn = Boolean(turn.FieldType, "IsPlayerTurn"); c.EndingTurn = Boolean(turn.FieldType, "EndingTurn");
            c.Round = (Func<object, int>)TouchSelectionCallFactory.Build(typeof(Func<object, int>), PcUiPath.Property(turn.FieldType, "CombatRound").GetGetMethod(true));
            c.Area = Property(game, "CurrentlyLoadedArea");
            var player = PcUiPath.Property(game, "Player"); c.Player = PcUiPath.Getter(player); c.PlayerShip = Property(player.PropertyType, "PlayerShip");
            var input = PcUiPath.Field(game, "PlayerInputInCombatController"); c.Input = TouchSelectionCallFactory.FieldGetter(input); c.InputLocked = Boolean(input.FieldType, "IsLocked");
            var slot = type("Kingmaker.Code.UI.MVVM.VM.ActionBar.ActionBarSlotVM");
            c.SlotCooling=Reactive<bool>(slot,"IsOnCooldown");c.SlotCooldown=Reactive<string>(slot,"CooldownText");
            var mechanic = PcUiPath.Property(slot, "MechanicActionBarSlot"); c.SlotMechanic = PcUiPath.Getter(mechanic);
            c.SlotOwner = Property(mechanic.PropertyType, "Unit"); c.SlotIcon = Reactive<object>(slot, "Icon");
            c.Empty = Reactive<bool>(slot, "IsEmpty"); c.Possible = Reactive<bool>(slot, "IsPossibleActive"); c.Convert = Reactive<bool>(slot, "HasAvailableConvert");
            c.CharScreen = PcUiPath.BooleanField(PcUiPath.Field(slot, "IsInCharScreen"));
            c.MechanicPossible = Boolean(mechanic.PropertyType, "IsPossibleActive"); c.MechanicConvert = Boolean(mechanic.PropertyType, "IsPossibleToConvert");
            c.MechanicLocked = Boolean(mechanic.PropertyType, "IsPlayerInputLocked");
            c.SlotTitle = (Func<object, string>)TouchSelectionCallFactory.Build(typeof(Func<object, string>), TouchSelectionCallFactory.ExactMethod(mechanic.PropertyType, "GetTitle", typeof(string), false));
            c.Click = (Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>), TouchSelectionCallFactory.ExactMethod(slot, "OnMainClick", typeof(void), false));
            c.ShipWeaponMechanic = type("Kingmaker.UI.Models.UnitSettings.MechanicActionBarShipWeaponSlot");
            var weaponSlot = PcUiPath.Field(c.ShipWeaponMechanic,"WeaponSlot");
            c.ShipWeaponSlot = TouchSelectionCallFactory.FieldGetter(weaponSlot);
            c.ActiveWeapon = Property(weaponSlot.FieldType,"Weapon");
            var item = type("Kingmaker.Items.ItemEntity");
            c.WeaponIcon = Property(item,"Icon");
            c.WeaponName = (Func<object,string>)TouchSelectionCallFactory.Build(typeof(Func<object,string>),PcUiPath.Property(item,"Name").GetGetMethod(true));
            return c;
        }
        internal object View(object game) { var root = game == null ? null : Root(game); return ViewPath.Read(root == null ? null : Ui(root)); }
        internal bool Contains(object group, object slot)
        {
            if (group == null || slot == null || !(GroupSlots(group) is IEnumerable items)) return false;
            foreach (var item in items) if (ReferenceEquals(item, slot)) return true;
            return false;
        }
        internal bool SlotAvailable(object slot, object mechanic, object unit)
        {
            return slot != null && mechanic != null && unit != null && !Empty(slot) && !CharScreen(slot) &&
                ReferenceEquals(SlotMechanic(slot), mechanic) && ReferenceEquals(SlotOwner(mechanic), unit) &&
                !MechanicLocked(mechanic) && (Possible(slot) || Convert(slot)) && (MechanicPossible(mechanic) || MechanicConvert(mechanic));
        }
        static Func<object, object> Field(Type t, string n) => TouchSelectionCallFactory.FieldGetter(PcUiPath.Field(t, n));
        static Func<object, object> Property(Type t, string n) => PcUiPath.Getter(PcUiPath.Property(t, n));
        static Func<object, bool> Boolean(Type t, string n) => (Func<object, bool>)TouchSelectionCallFactory.Build(typeof(Func<object, bool>), PcUiPath.Property(t, n).GetGetMethod(true));
        static Func<object, T> Reactive<T>(Type t, string n) => TouchRadialPartyContracts.Reactive<T>(t, n);
    }
}
