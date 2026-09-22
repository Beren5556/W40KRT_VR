using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        // Narrow PC gameplay/UI routes, checked against the installed metadata.
        // Debug tools, settings rebinding and inactive console modules are excluded.
        static readonly string[] TouchInputRoutes = {
            "Kingmaker.UI.Legacy.MainMenuUI.SplashScreenController|Update",
            "Kingmaker.Code.UI.MVVM.View.SurfaceCombat.PC.SurfaceHUDPCView|InternalUpdate",
            "Kingmaker.Code.UI.MVVM.View.LoadingScreen.LoadingScreenBaseView|<ShowUserInputWaiting>b__88_0",
            "Kingmaker.UI.Selection.SelectionManagerBase|SwitchSelectionUnitInGroup",
            "Kingmaker.UI.Pointer.BaseCursor|get_Position",
            "Kingmaker.UI.Pointer.CursorController|get_CursorPosition",
            "Kingmaker.UI.InputSystems.KeyboardAccess|IsInputFieldSelected",
            "Kingmaker.UI.InputSystems.KeyboardAccess|AnyKeyHold",
            "Kingmaker.UI.InputSystems.KeyboardAccess|AnyKeyDown",
            "Kingmaker.UI.InputSystems.KeyboardAccess|AnyKeyUp",
            "Kingmaker.UI.InputSystems.KeyboardAccess|IsAltHold",
            "Kingmaker.UI.InputSystems.KeyboardAccess|IsCtrlHold",
            "Kingmaker.UI.InputSystems.KeyboardAccess|IsShiftHold",
            "Kingmaker.UI.InputSystems.KeyboardAccess|IsAltDown",
            "Kingmaker.UI.InputSystems.KeyboardAccess|IsCtrlDown",
            "Kingmaker.UI.InputSystems.KeyboardAccess|IsShiftDown",
            "Kingmaker.UI.InputSystems.KeyboardAccess|IsAltUp",
            "Kingmaker.UI.InputSystems.KeyboardAccess|IsCtrlUp",
            "Kingmaker.UI.InputSystems.KeyboardAccess|IsShiftUp",
            "Kingmaker.UI.InputSystems.KeyboardAccess+Binding|IsKeyTriggered",
            "Kingmaker.UI.Common.UIUtility|IsAnyKeyboardKeyDown",
            "Kingmaker.Networking.PingNetManager|CheckPingCoop",
            "Kingmaker.Controllers.Clicks.PointerController|Tick",
            "Kingmaker.Controllers.Clicks.PointerController|Switch2MouseUITickHandler",
            "Kingmaker.Controllers.Clicks.Handlers.ClickGroundHandler|GetPriorityInternal",
            "Kingmaker.Controllers.Clicks.Handlers.ClickWithSelectedAbilityHandler|GetPriorityInternal",
            "Kingmaker.Code.UI.MVVM.VM.Tooltip.Utils.TooltipHandler|RightClickAction",
            "Kingmaker.Code.UI.MVVM.View.ContextMenu.PC.ContextMenuPCView|OnUpdate",
            "Kingmaker.Code.UI.MVVM.View.Common.InputField.OwlcatInputField|TrySubmitInputField",
            "Kingmaker.Code.UI.MVVM.View.ChoseControllerMode.GamepadDisconnectedInGamepadModeWindowView|OnLateUpdate",
            "Kingmaker.Settings.Entities.KeyBindingData|get_IsPressed",
            "Kingmaker.Settings.Entities.KeyBindingData|get_IsDown",
        };
        static IEnumerable<MethodInfo> ResolveTouchInputRoutes()
        {
            foreach (string entry in TouchInputRoutes)
            {
                int separator = entry.IndexOf('|');
                string typeName = entry.Substring(0, separator), methodName = entry.Substring(separator + 1);
                var type = AccessTools.TypeByName(typeName) ?? throw new TypeLoadException(typeName);
                MethodInfo match = null;
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    if (method.Name == methodName)
                    {
                        if (match != null) throw new AmbiguousMatchException(entry);
                        match = method;
                    }
                yield return match ?? throw new MissingMethodException(entry);
            }
            // Patch a closed instantiation, never an open generic definition.
            var pc = AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.View.VariativeInteraction.VariativeInteractionPCView");
            var closed = pc?.BaseType?.GetMethod("OnUpdateHandler", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (closed == null || closed.ContainsGenericParameters) throw new MissingMethodException("VariativeInteractionPCView.OnUpdateHandler closed route");
            yield return closed;
        }
    }
}
