using System;
using HarmonyLib;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Harmony _touchPcStartupHarmony;
        static TouchPcStartupContracts _touchPcStartup;
        static object _touchPcStartupCompleted, _touchPcStartupFailed;
        static int _touchPcStartupSelections;

        // Installed at mod load, before OpenXR: the game's controller question
        // precedes VR session creation. The normal session input hook is too late.
        internal static void InstallTouchPcStartup()
        {
            if (_touchPcStartupHarmony != null) return;
            try
            {
                _touchPcStartup = TouchPcStartupContracts.Create(AccessTools.TypeByName);
                _touchPcStartupHarmony = new Harmony("RTMaquetaXR.TouchPcStartup");
                _touchPcStartupHarmony.Patch(_touchPcStartup.Tick,
                    prefix: new HarmonyMethod(typeof(Main), nameof(TouchPcStartupTick)) { priority = Priority.First });
            }
            catch (Exception error)
            {
                _touchPcStartupHarmony?.UnpatchAll(_touchPcStartupHarmony.Id); _touchPcStartupHarmony = null;
                _log.Error("[touch/startup] Automatic PC selection unavailable: " + error.Message);
            }
        }
        static bool TouchPcStartupTick(object __instance)
        {
            if (!TouchPcStartupPolicy.Owned(_active, _autoStartArmed, _appQuitting) || _touchPcStartup == null) return true;
            var vm = _touchPcStartup.ViewModel(__instance);
            if (vm == null) return true;
            if (ReferenceEquals(vm, _touchPcStartupFailed)) return true;
            if (ReferenceEquals(vm, _touchPcStartupCompleted)) return false;
            // Run from the native late-update callback, after all Bind
            // subscriptions exist. This is precisely Return's native action;
            // it both selects Mouse and continues the startup routine.
            _touchPcStartupCompleted = vm;
            try { _touchPcStartup.Keyboard(vm); ++_touchPcStartupSelections; }
            catch (Exception error)
            {
                _touchPcStartupCompleted = null; _touchPcStartupFailed = vm;
                _log.Error("[touch/startup] Native PC confirmation failed: " + error.Message);
                return true; // Keep the game's ordinary Return escape available.
            }
            return false;
        }
        internal static void StopTouchPcStartup()
        {
            if (!_appQuitting) _touchPcStartupHarmony?.UnpatchAll(_touchPcStartupHarmony.Id);
            _touchPcStartupHarmony = null; _touchPcStartup = null; _touchPcStartupCompleted = _touchPcStartupFailed = null;
        }
        internal static object TouchPcStartupSnapshot() => new {
            Installed = _touchPcStartupHarmony != null, KeyboardSelections = _touchPcStartupSelections,
            Armed = TouchPcStartupPolicy.Owned(_active, _autoStartArmed, _appQuitting)
        };
    }
}
