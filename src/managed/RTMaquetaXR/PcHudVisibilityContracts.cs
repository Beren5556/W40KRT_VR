using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace RTMaquetaXR
{
    internal enum PcHudReplacement { Menus, Party, Actions, Combatants, EndTurn }
    // Explicit native field paths: never classify UI by names of scene objects.
    internal sealed class PcUiPath
    {
        internal Type RootType, ValueType;
        internal string Description;
        internal PcHudReplacement Replacement;
        internal bool RequiresTouch;
        internal Func<object, object>[] Steps;
        internal object Read(object root)
        {
            if (root == null || !RootType.IsInstanceOfType(root)) return null;
            foreach (var step in Steps) { root = step(root); if (root == null) return null; }
            return root;
        }
        internal static PcUiPath Create(Type root, params string[] fields)
        {
            var result = new PcUiPath { RootType = root, Description = root.Name + "." + string.Join(".", fields) };
            var steps = new List<Func<object, object>>();
            foreach (var name in fields)
            {
                var field = Field(root, name); steps.Add(TouchSelectionCallFactory.FieldGetter(field)); root = field.FieldType;
            }
            result.Steps = steps.ToArray(); result.ValueType = root; return result;
        }
        internal static FieldInfo Field(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            throw new MissingFieldException(type.FullName, name);
        }
        internal static PropertyInfo Property(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (p?.GetGetMethod(true) != null) return p;
            }
            throw new MissingMemberException(type.FullName, name);
        }
        internal static Func<object, object> Getter(PropertyInfo property) =>
            (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>), property.GetGetMethod(true));
        internal static Func<object, bool> BooleanField(FieldInfo field)
        {
            if (field.IsStatic || field.FieldType != typeof(bool)) throw new InvalidOperationException("Native boolean field changed: " + field);
            var method = new DynamicMethod("RTMaquetaXR_UI_" + field.Name, typeof(bool), new[] { typeof(object) }, typeof(PcUiPath).Module, true);
            var il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, field.DeclaringType);
            il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
            return (Func<object, bool>)method.CreateDelegate(typeof(Func<object, bool>));
        }
    }

    internal sealed class PcHudVisibilityContracts
    {
        internal Func<object> Game;
        internal Func<object, object> Root, View, Common;
        internal PcUiPath[] SurfacePanels, SpacePanels, NavigationMenus;
        internal PcUiPath EndTurn;
        internal Type SurfaceType, SpaceType, CommonType;
        internal static PcHudVisibilityContracts Create(Func<string, Type> resolve)
        {
            Func<string, Type> type = n => resolve(n) ?? throw new TypeLoadException(n);
            var c = new PcHudVisibilityContracts(); var game = type("Kingmaker.Game");
            c.Game = (Func<object>)TouchSelectionCallFactory.Build(typeof(Func<object>),
                TouchSelectionCallFactory.ExactMethod(game, "get_Instance", game, true));
            var root = PcUiPath.Property(game, "RootUiContext"); c.Root = PcUiPath.Getter(root);
            c.View = TouchSelectionCallFactory.FieldGetter(PcUiPath.Field(root.PropertyType, "m_UIView"));
            c.Common = TouchSelectionCallFactory.FieldGetter(PcUiPath.Field(root.PropertyType, "m_CommonView"));
            c.SurfaceType = type("Kingmaker.Code.UI.MVVM.View.Surface.PC.SurfacePCView");
            c.SpaceType = type("Kingmaker.Code.UI.MVVM.View.Space.PC.SpacePCView");
            c.CommonType = type("Kingmaker.Code.UI.MVVM.View.Common.PC.CommonPCView");
            var surface = new List<PcUiPath>();
            foreach (var field in new[] { "m_PartyPCView", "m_ActionBarView", "m_InitiativeTrackerView", "m_SurfaceCombatCurrentUnitView",
                "m_IngameMenuPCView", "m_IngameMenuSettingsButtonPCView", "m_CombatLogPCView" })
            {
                var path = PcUiPath.Create(c.SurfaceType, "m_StaticPartPCView", "SurfaceHUDView", field);
                path.Replacement = field == "m_PartyPCView" ? PcHudReplacement.Party :
                    field == "m_InitiativeTrackerView" ? PcHudReplacement.Combatants :
                    field == "m_ActionBarView" || field == "m_SurfaceCombatCurrentUnitView" ? PcHudReplacement.Actions : PcHudReplacement.Menus;
                surface.Add(path);
            }
            c.EndTurn = PcUiPath.Create(c.SurfaceType, "m_StaticPartPCView", "SurfaceHUDView", "m_EndTurnButton");
            c.EndTurn.Replacement = PcHudReplacement.EndTurn;
            surface.Add(c.EndTurn); c.SurfacePanels = surface.ToArray();
            var space = new List<PcUiPath>();
            // Ship navigation/combat, deployment, Inspect and popups remain native.
            foreach (var field in new[] { "m_IngameMenuPCView", "m_IngameMenuSettingsButtonPCView", "m_PartyPCView", "m_CombatLogPCView" })
            {
                var path = PcUiPath.Create(c.SpaceType, "m_StaticPartPCView", field);
                path.Replacement = field == "m_PartyPCView" ? PcHudReplacement.Party : PcHudReplacement.Menus;
                space.Add(path);
            }
            foreach (var field in new[] { "m_ShipWeaponsPanelPCView", "m_ShipPostsPanelPCView", "m_SpaceCombatServicePanelPCView", "m_SpaceCombatCircleArcsView" })
                space.Add(PcUiPath.Create(c.SpaceType,"m_StaticPartPCView","m_SpaceCombatPCView",field));
            // The native prefab puts the second own-abilities group in the
            // TorpedoPanel, a SIBLING of the weapons/posts and service panels.
            // ShipWeaponsPanelPCView binds this duplicate to the same abilities
            // VM (including Restore Shields). Hiding the main bars therefore
            // never hid this floating slot. Lease the exact native roots, keep
            // the VM/action alive and let the radial continue to use it.
            space.Add(PcUiPath.Create(c.SpaceType,"m_StaticPartPCView","m_SpaceCombatPCView",
                "m_ShipWeaponsPanelPCView","m_SecondAbilitiesGroup"));
            space.Add(PcUiPath.Create(c.SpaceType,"m_StaticPartPCView","m_SpaceCombatPCView",
                "m_SpaceCombatServicePanelPCView","m_SpaceCombatTorpedoPanelPCView"));
            foreach (var field in new[] { "m_ShipHealthAndRepairPCView", "m_ShipPositionRulersView", "m_SystemMapSpaceResourcesPCView" })
                space.Add(PcUiPath.Create(c.SpaceType,"m_StaticPartPCView",field));
            // These native dynamic branches are outside the side/bottom bars:
            // ship overtips, floating ability/point markers and system markers.
            // Lease visual alpha only; preserve targeting models and board grid.
            foreach (var field in new[] { "m_SpaceOvertipsView", "m_SpaceCombatPointMarkersPCView" })
                space.Add(PcUiPath.Create(c.SpaceType,"m_DynamicPartPCView",field));
            // Native BaseCursor has a separate floating ability image. It is
            // not part of the overtip/marker roots and our skull owns pointing.
            // Hide its visual transform only, never the cursor controller.
            var spaceCursor=PcUiPath.Create(c.SpaceType,"m_DynamicPartPCView","m_Cursor","m_CursorTransform");
            spaceCursor.RequiresTouch=true;
            space.Add(spaceCursor);
            space.Add(PcUiPath.Create(c.SpaceType,"m_StaticPartPCView","m_SpacePointMarkersPCView"));
            c.SpacePanels = space.ToArray();
            c.NavigationMenus = new[] {
                PcUiPath.Create(c.SpaceType,"m_StaticPartPCView","m_IngameMenuPCView"),
                PcUiPath.Create(c.SpaceType,"m_StaticPartPCView","m_IngameMenuSettingsButtonPCView")
            };
            return c;
        }
    }
}
