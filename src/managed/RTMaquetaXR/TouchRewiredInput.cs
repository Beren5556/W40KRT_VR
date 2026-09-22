using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine.EventSystems;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Type _touchRewiredModule;
        static FieldInfo _touchRewiredPointerData;
        static bool IsTouchRewiredModule(BaseInputModule module) =>
            module != null && _touchRewiredModule != null && _touchRewiredModule.IsInstanceOfType(module);

        static void InstallTouchRewiredInput()
        {
            // The shipped Rewired PC module is NOT a StandaloneInputModule and
            // never reads BaseInput.inputOverride. Keep its full event pipeline;
            // replace only its default desktop-mouse source while VR owns input.
            _touchRewiredModule = AccessTools.TypeByName("Rewired.Integration.UnityUI.RewiredStandaloneInputModule")
                ?? throw new TypeLoadException("RewiredStandaloneInputModule");
            var pointer = _touchRewiredModule.BaseType;
            var source = pointer.GetNestedType("UnityInputSource", BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new TypeLoadException("RewiredPointerInputModule.UnityInputSource");
            _touchRewiredPointerData = TouchField(pointer, "m_PlayerPointerData");
            foreach (string method in new[] { "GetMouseInputSource", "GetMouseInputSourceCount" })
            {
                var target = AccessTools.Method(pointer, method) ?? throw new MissingMethodException(pointer.FullName, method);
                _touchHarmony.Patch(target, prefix: new HarmonyMethod(typeof(Main), nameof(TouchRewiredSourcePrefixFactory)));
            }
            foreach (string method in new[] {
                "TryUpdate", "Rewired.UI.IMouseInputSource.get_screenPosition", "Rewired.UI.IMouseInputSource.get_wheelDelta",
                "Rewired.UI.IMouseInputSource.GetButtonDown", "Rewired.UI.IMouseInputSource.GetButtonUp", "Rewired.UI.IMouseInputSource.GetButton" })
            {
                var target = AccessTools.Method(source, method) ?? throw new MissingMethodException(source.FullName, method);
                _touchHarmony.Patch(target, transpiler: new HarmonyMethod(typeof(Main), nameof(TouchGameInputTranspiler)));
            }
            PatchTouchPrefix(source, "Rewired.UI.IMouseInputSource.get_enabled", nameof(TouchRewiredEnabled));
            PatchTouchPrefix(source, "Rewired.UI.IMouseInputSource.get_locked", nameof(TouchRewiredUnlocked));
            PatchTouchPrefix(source, "Rewired.UI.ITouchInputSource.get_touchSupported", nameof(TouchRewiredUnlocked));
            PatchTouchPrefix(source, "Rewired.UI.ITouchInputSource.get_touchCount", nameof(TouchRewiredNoTouchscreen));
            PatchTouchPrefix(_touchRewiredModule, "Process", nameof(TouchUiProcess));
            PatchTouchPrefix(_touchRewiredModule, "get_isMouseSupported", nameof(TouchRewiredEnabled));
            PatchTouchPrefix(_touchRewiredModule, "get_isTouchAllowed", nameof(TouchRewiredUnlocked));
            _log.Log("[touch/ui] Rewired PC module retains click/drag processing; its desktop mouse source now reads Touch.");
        }

        static DynamicMethod TouchRewiredSourcePrefixFactory(MethodBase original) =>
            TouchRewiredSourceFactory.Build((MethodInfo)original, AccessTools.PropertyGetter(typeof(Main), nameof(TouchInputOwned)));
        static bool TouchRewiredEnabled(ref bool __result)
        { if (!TouchInputOwned) return true; __result = true; return false; }
        static bool TouchRewiredUnlocked(ref bool __result)
        { if (!TouchInputOwned) return true; __result = false; return false; }
        static bool TouchRewiredNoTouchscreen(ref int __result)
        { if (!TouchInputOwned) return true; __result = 0; return false; }
    }
}

