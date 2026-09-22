using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static MethodInfo _nativeTutorialMenuGetter;
        static Func<object, bool> _nativeTutorialOriginalMenuGetter;
        static bool _nativeTutorialMenuGuard;

        static void InstallNativeTutorialMenuGuard()
        {
            if (_nativeTutorialMenuGuard) return;
            try
            {
                var root = AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.RootUIContext");
                var tutorial = AccessTools.TypeByName("Kingmaker.UI.MVVM.VM.Tutorial.TutorialVM");
                var mode = AccessTools.TypeByName("Kingmaker.GameModes.GameModeType");
                _nativeTutorialMenuGetter = TouchSelectionCallFactory.ExactMethod(root, "get_IsIngameMenuShown", typeof(bool), false);
                _nativeTutorialOriginalMenuGetter = (Func<object, bool>)TouchSelectionCallFactory.Build(typeof(Func<object, bool>), _nativeTutorialMenuGetter);
                var canShow = TouchSelectionCallFactory.ExactMethod(tutorial, "CanShowBigWindow", typeof(bool), true, mode);
                // Only this tutorial query changes. Cinematic/dialog mode
                // guards, pending tutorial data and game callbacks stay native.
                _harmony.Patch(canShow, transpiler: new HarmonyMethod(typeof(Main), nameof(GuardNativeTutorialMenuRead)));
                _nativeTutorialMenuGuard = true;
            }
            catch (Exception error) { _log.Error("[tutorial/input] Native menu-query guard unavailable: " + error.Message); }
        }

        static IEnumerable<CodeInstruction> GuardNativeTutorialMenuRead(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions); int matches = 0;
            foreach (var instruction in list)
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                    Equals(instruction.operand, _nativeTutorialMenuGetter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(Main), nameof(NativeTutorialMenuShown)); ++matches;
                }
            if (matches != 1) throw new InvalidOperationException("Expected one native tutorial menu query; found " + matches);
            return list;
        }

        static bool NativeTutorialMenuShown(object root)
        {
            if (!_active || _presentation == null) return _nativeTutorialOriginalMenuGetter(root);
            // Native get_IsIngameMenuShown dereferences SurfaceVM even when
            // SpaceVM owns the scene. Two logged crashes occurred here. Missing
            // root means UI is transitioning: let native code queue the tutorial.
            if (root == null) return true;
            return NativeUiOnlyPresentation() || TouchMenuWindowVisible || _presentation.IngameMenu(root);
        }
    }
}
