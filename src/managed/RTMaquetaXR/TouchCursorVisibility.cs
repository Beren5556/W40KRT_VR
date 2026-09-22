using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        // Owlcat's PC controls use OS cursor visibility as an input-availability
        // guard. Touch supplies a visible VR pointer while the OS cursor remains
        // hidden. Replace that guard only at the nine actual UI read sites.
        static void InstallTouchCursorVisibility()
        {
            const string selectable = "Owlcat.Runtime.UI.Controls.Selectable.OwlcatSelectable";
            const string tooltip = "Kingmaker.Code.UI.MVVM.VM.Tooltip.Utils.TooltipHandler";
            const string hints = "Kingmaker.Code.UI.MVVM.VM.Tooltip.Utils.TooltipHelper+<>c__DisplayClass34_0";
            foreach (string name in new[] { "OnPointerEnter", "OnPointerExit", "OnPointerDown", "OnPointerUp" })
                PatchTouchCursorGuard(selectable, name, new[] { typeof(PointerEventData) });
            foreach (string name in new[] { "EnterAction", "ExitAction", "RightClickAction" })
                PatchTouchCursorGuard(tooltip, name, Type.EmptyTypes);
            PatchTouchCursorGuard(hints, "<SetHint>b__0", new[] { typeof(PointerEventData) });
            PatchTouchCursorGuard(hints, "<SetHint>b__1", Type.EmptyTypes);
            _log.Log("[touch/ui] Nine scoped Owlcat pointer/tooltip visibility guards use the VR pointer; OS cursor is unchanged.");
        }
        static void PatchTouchCursorGuard(string typeName, string name, Type[] parameters)
        {
            var type = AccessTools.TypeByName(typeName) ?? throw new TypeLoadException(typeName);
            var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, parameters, null);
            if (method == null || method.ReturnType != typeof(void) || method.ContainsGenericParameters)
                throw new MissingMethodException(typeName, name);
            _touchHarmony.Patch(method, transpiler: new HarmonyMethod(typeof(Main), nameof(TouchCursorVisibilityTranspiler)));
        }
        static bool TouchCursorVisible() => TouchInputOwned || Cursor.visible;
        static IEnumerable<CodeInstruction> TouchCursorVisibilityTranspiler(IEnumerable<CodeInstruction> source, MethodBase __originalMethod)
        {
            int count = 0;
            var original = AccessTools.PropertyGetter(typeof(Cursor), nameof(Cursor.visible));
            var replacement = AccessTools.Method(typeof(Main), nameof(TouchCursorVisible));
            foreach (var instruction in source)
            {
                if (instruction.Calls(original)) { instruction.operand = replacement; ++count; }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Unexpected Owlcat UI cursor guard count: " + count + " in " + __originalMethod);
        }
    }
}
