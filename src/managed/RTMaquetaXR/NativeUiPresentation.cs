using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Func<bool> _nativeUiOnly;
        static bool _nativeUiPresentationAttempted;
        internal static bool NativeUiOnlyPresentation()
        {
            if (!_nativeUiPresentationAttempted)
            {
                _nativeUiPresentationAttempted = true;
                try { _nativeUiOnly = NativeUiPresentationContract.Create(AccessTools.TypeByName); }
                catch (Exception error) { _log.Log("[presentation/ui-stack] Explicit UI scene fallback: " + error.Message); }
            }
            if (_nativeUiOnly != null) return _nativeUiOnly();
            // Compatibility fallback is restricted to the known native service
            // scene. Ordinary world-camera replacement still waits for stereo.
            var scene = SceneManager.GetActiveScene();
            return scene.IsValid() && scene.isLoaded && scene.name == "UI_Surface_Scene" && Camera.main == null;
        }
    }
}
