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
        static Func<bool> _nativeManagement81;
        static bool _nativeManagementAttempted81;
        internal static bool NativeManagementOpen81()
        {
            if(!_nativeManagementAttempted81) {
                _nativeManagementAttempted81=true;
                try { _nativeManagement81=NativeUiPresentationContract.CreateManagement81(AccessTools.TypeByName); }
                catch(Exception error) { _log.Error("[ui/management] Native ownership unavailable: " + error.Message); }
            }
            return _nativeManagement81 != null && _nativeManagement81();
        }
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
