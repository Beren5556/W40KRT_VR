using System;
using System.Reflection;
using System.Reflection.Emit;

namespace RTMaquetaXR
{
    internal sealed class TouchRadialContracts
    {
        internal const string SlotViewName = "Kingmaker.Code.UI.MVVM.View.ActionBar.PC.ActionBarSlotPCView";
        internal const string BarViewName = "Kingmaker.Code.UI.MVVM.View.ActionBar.PC.SurfaceActionBarPCView";
        internal const string MenuViewName = "Kingmaker.Code.UI.MVVM.View.IngameMenu.PC.IngameMenuNewPCView";
        internal const string SettingsViewName = "Kingmaker.Code.UI.MVVM.View.IngameMenu.PC.IngameMenuSettingsButtonPCView";
        internal static readonly string[] MenuFields = { "m_Inventory", "m_Character", "m_Augmentations", "m_Journal", "m_Map", "m_Encyclopedia", "m_ShipCustomization", "m_ColonyManagement", "m_CargoManagement", "m_Formation" };
        internal static readonly string[] MenuLabels = { "Inventory", "Character", "Augmentations", "Journal", "Map", "Encyclopedia", "Voidship", "Colonies", "Cargo", "Formation" };
        internal static readonly string[] SettingsFields = { "m_Settings", "m_Pause", "m_NetRoles" };
        internal static readonly string[] SettingsLabels = { "Game menu", "Pause", "Co-op roles" };
        internal Type SlotView, BarView, MenuView, SettingsView;
        internal PropertyInfo SlotModel, BarModel, MenuModel, SettingsModel, CurrentUnit, MechanicSlot, IconValue, EmptyValue, Interactable;
        internal FieldInfo SlotIcon, SlotEmpty, SlotPossible, SlotConverts, SlotButton, AbilitiesView, WeaponsView;
        internal FieldInfo[] MenuButtons, SettingsButtons;
        internal FieldInfo BarAbilities, BarConsumables, BarWeapons, AbilitySlots, ConsumableSlots, OverdriveSlot, WeaponCurrentSet, CharScreen;
        internal FieldInfo[] WeaponSlots;
        internal PropertyInfo WeaponSetValue;
        internal MethodInfo MainClick, ModelClick, Title;
        Func<object> game;
        Func<object, object> player, colonies;
        Func<object, bool> servicesBlocked, inventoryBlocked, characterBlocked, augmentationsBlocked, starshipAvailable, colonizationBlocked;
        internal static TouchRadialContracts Create(Func<string, Type> resolve)
        {
            var c = new TouchRadialContracts();
            c.SlotView = RequireType(resolve, SlotViewName); c.BarView = RequireType(resolve, BarViewName);
            c.MenuView = RequireType(resolve, MenuViewName); c.SettingsView = RequireType(resolve, SettingsViewName);
            c.SlotModel = Property(c.SlotView, "ViewModel"); c.BarModel = Property(c.BarView, "ViewModel");
            c.MenuModel = Property(c.MenuView, "ViewModel"); c.SettingsModel = Property(c.SettingsView, "ViewModel");
            c.CurrentUnit = Property(c.BarModel.PropertyType, "CurrentUnit");
            c.MechanicSlot = Property(c.SlotModel.PropertyType, "MechanicActionBarSlot");
            c.SlotIcon = Field(c.SlotModel.PropertyType, "Icon"); c.IconValue = Property(c.SlotIcon.FieldType, "Value");
            c.SlotEmpty = Field(c.SlotModel.PropertyType, "IsEmpty"); c.EmptyValue = Property(c.SlotEmpty.FieldType, "Value");
            c.SlotPossible = Field(c.SlotModel.PropertyType, "IsPossibleActive"); c.SlotConverts = Field(c.SlotModel.PropertyType, "HasAvailableConvert");
            if (c.SlotPossible.FieldType != c.SlotEmpty.FieldType || c.SlotConverts.FieldType != c.SlotEmpty.FieldType)
                throw new InvalidOperationException("Native ability availability property changed");
            c.SlotButton = Field(c.SlotView, "m_MainButton");
            c.Interactable = Property(c.SlotButton.FieldType, "Interactable");
            c.AbilitiesView = Field(c.BarView, "m_AbilitiesView"); c.WeaponsView = Field(c.BarView, "m_WeaponsView");
            c.MainClick = Method(c.SlotView, "OnMainClick", typeof(void));
            c.ModelClick = Method(c.SlotModel.PropertyType, "OnMainClick", typeof(void));
            c.CharScreen = Field(c.SlotModel.PropertyType, "IsInCharScreen");
            c.Title = Method(c.MechanicSlot.PropertyType, "GetTitle", typeof(string));
            c.BarAbilities = Field(c.BarModel.PropertyType, "Abilities"); c.AbilitySlots = Field(c.BarAbilities.FieldType, "Slots");
            c.OverdriveSlot = Field(c.BarAbilities.FieldType, "OverdriveSlotVM");
            c.BarConsumables = Field(c.BarModel.PropertyType, "Consumables"); c.ConsumableSlots = Field(c.BarConsumables.FieldType, "Slots");
            c.BarWeapons = Field(c.BarModel.PropertyType, "Weapons"); c.WeaponCurrentSet = Field(c.BarWeapons.FieldType, "CurrentSet");
            c.WeaponSetValue = Property(c.WeaponCurrentSet.FieldType, "Value");
            c.WeaponSlots = Fields(c.WeaponSetValue.PropertyType, new[] { "MainHandSlots", "OffHandSlots", "ComboHandsSlots" });
            c.MenuButtons = Fields(c.MenuView, MenuFields); c.SettingsButtons = Fields(c.SettingsView, SettingsFields);
            var gameType = RequireType(resolve, "Kingmaker.Game");
            var getGame = TouchSelectionCallFactory.ExactMethod(gameType, "get_Instance", gameType, true);
            var getPlayer = Property(gameType, "Player");
            c.game = (Func<object>)TouchSelectionCallFactory.Build(typeof(Func<object>), getGame);
            c.player = (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>), getPlayer.GetGetMethod(true));
            var playerType = getPlayer.PropertyType;
            c.servicesBlocked = PlayerFlag(playerType, "ServiceWindowsBlocked");
            c.inventoryBlocked = PlayerFlag(playerType, "InventoryWindowBlocked");
            c.characterBlocked = PlayerFlag(playerType, "CharacterInfoWindowBlocked");
            c.augmentationsBlocked = PlayerFlag(playerType, "AugmentationsWindowBlocked");
            c.starshipAvailable = PlayerFlag(playerType, "CanAccessStarshipInventory");
            var colonyProperty = Property(playerType, "ColoniesState");
            c.colonies = (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>), colonyProperty.GetGetMethod(true));
            c.colonizationBlocked = PlayerFlag(colonyProperty.PropertyType, "ForbidColonization");
            return c;
        }
        // Read the native facts even when a hidden toolbar missed a refresh.
        // These are the exact flags consulted by CheckServiceWindowsBlocked /
        // CheckEnabledColonizationButton; no native SetActive calls are issued.
        internal bool MenuAvailable(object view, string label)
        {
            if (!MenuView.IsInstanceOfType(view)) return SettingsView.IsInstanceOfType(view);
            var g = game(); var p = g == null ? null : player(g); if (p == null) return false;
            switch (label)
            {
                case "Inventory": return !servicesBlocked(p) && !inventoryBlocked(p);
                case "Character": return !servicesBlocked(p) && !characterBlocked(p);
                case "Augmentations": return !servicesBlocked(p) && !augmentationsBlocked(p);
                case "Cargo": return !servicesBlocked(p);
                case "Voidship": return !servicesBlocked(p) && starshipAvailable(p);
                case "Colonies": var state = colonies(p); return starshipAvailable(p) && state != null && !colonizationBlocked(state);
                default: return true;
            }
        }
        static Func<object, bool> PlayerFlag(Type type, string name)
        {
            var field = Field(type, name);
            var dynamic = new DynamicMethod("RTMaquetaXR_RadialFlag_" + name, typeof(bool), new[] { typeof(object) }, typeof(TouchRadialContracts).Module, true);
            var il = dynamic.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, field.DeclaringType); il.Emit(OpCodes.Ldfld, field);
            if (field.FieldType != typeof(bool))
                il.Emit(OpCodes.Call, TouchSelectionCallFactory.ExactMethod(field.FieldType, "op_Implicit", typeof(bool), true, field.FieldType));
            il.Emit(OpCodes.Ret); return (Func<object, bool>)dynamic.CreateDelegate(typeof(Func<object, bool>));
        }
        static FieldInfo[] Fields(Type type, string[] names)
        { var result = new FieldInfo[names.Length]; for (int i = 0; i < names.Length; ++i) result[i] = Field(type, names[i]); return result; }
        static Type RequireType(Func<string, Type> resolve, string name) => resolve(name) ?? throw new TypeLoadException(name);
        internal static FieldInfo Field(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            { var field = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); if (field != null) return field; }
            throw new MissingFieldException(type.FullName, name);
        }
        internal static PropertyInfo Property(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            { var property = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); if (property?.GetGetMethod(true) != null) return property; }
            throw new MissingMemberException(type.FullName, name);
        }
        static MethodInfo Method(Type type, string name, Type returns)
        {
            var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != returns || method.ContainsGenericParameters) throw new MissingMethodException(type.FullName, name);
            return method;
        }
    }
}
