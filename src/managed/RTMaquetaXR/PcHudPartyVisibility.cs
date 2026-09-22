using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Type _pcHudPartyType;
        static Action<object, bool> _pcHudPartyHide;
        static Action<object> _pcHudPartyCheck;
        static Func<object, object> _pcHudPartyModel;
        static readonly HashSet<Component> _pcHudHiddenParties = new HashSet<Component>();
        static bool _pcHudPartyInstalled;
        static string _pcHudPartyFailure;
        static long _pcHudPartyShowPrevented, _pcHudPartyHidden, _pcHudPartyRestored;

        static void InstallPcHudPartyVisibility()
        {
            if (_pcHudPartyInstalled || _pcHudPartyFailure != null) return;
            try
            {
                _pcHudPartyType = AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.View.Party.PC.PartyPCView")
                    ?? throw new TypeLoadException("PartyPCView");
                var hide = TouchSelectionCallFactory.ExactMethod(_pcHudPartyType, "HideAnimation", typeof(void), false, typeof(bool));
                var check = TouchSelectionCallFactory.ExactMethod(_pcHudPartyType, "CheckVisible", typeof(void), false);
                _pcHudPartyHide = (Action<object, bool>)TouchSelectionCallFactory.Build(typeof(Action<object, bool>), hide);
                _pcHudPartyCheck = (Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>), check);
                _pcHudPartyModel = PcUiPath.Getter(PcUiPath.Property(_pcHudPartyType, "ViewModel"));
                // This exact native entry point only controls a bar's fade and
                // slide. Unit models, actions, bindings and availability remain
                // untouched. Native mode changes cannot schedule a show tween
                // while our radial replacement owns the bar's presentation.
                _harmony.Patch(hide, prefix: new HarmonyMethod(typeof(Main), nameof(PcHudPartyHidePrefix)));
                _pcHudPartyInstalled = true;
            }
            catch (Exception error)
            {
                _pcHudPartyFailure = error.Message;
                _log?.Error("[ui/clean-hud] Native party visibility unavailable; visual leases retained: " + error.Message);
            }
        }

        static void PcHudPartyHidePrefix(Component __instance, ref bool __0)
        {
            if (__0 || !_active || !_attached || !PcHudMinimal || __instance == null || !_pcHudHiddenParties.Contains(__instance)) return;
            __0 = true; ++_pcHudPartyShowPrevented;
        }

        static void UpdatePcHudPartyVisibility(PcHudVisualLease lease, Component view, bool hide)
        {
            if (!_pcHudPartyInstalled || view == null || !_pcHudPartyType.IsInstanceOfType(view)) return;
            if (!hide) { ReleasePcHudPartyVisibility(lease); return; }
            if (lease.PartyView == view) return;
            ReleasePcHudPartyVisibility(lease);
            lease.PartyView = view;
            if (_pcHudHiddenParties.Add(view))
                try { _pcHudPartyHide(view, true); ++_pcHudPartyHidden; }
                catch (Exception error) { PartyVisibilityFailure(error); }
        }

        internal static void ReleasePcHudPartyVisibility(PcHudVisualLease lease)
        {
            var view = lease.PartyView; lease.PartyView = null;
            if (ReferenceEquals(view, null) || !_pcHudHiddenParties.Remove(view) || view == null) return;
            // Let native mode/fullscreen/map rules choose visibility on release.
            // A disposed/pool-bound view has no live model to inspect.
            try
            {
                if (view.gameObject.activeInHierarchy && _pcHudPartyModel(view) != null)
                { _pcHudPartyCheck(view); ++_pcHudPartyRestored; }
            }
            catch (Exception error) { PartyVisibilityFailure(error); }
        }

        static void PartyVisibilityFailure(Exception error)
        {
            if (_pcHudPartyFailure == null) _log?.Error("[ui/clean-hud] Native party visibility: " + error.Message);
            _pcHudPartyFailure = error.Message;
        }

        static object PcHudPartyVisibilitySnapshot() => new {
            Ready = _pcHudPartyInstalled, Failure = _pcHudPartyFailure,
            HiddenViews = _pcHudHiddenParties.Count, NativeHideRequests = _pcHudPartyHidden,
            NativeShowRequestsPrevented = _pcHudPartyShowPrevented, NativeVisibilityRestored = _pcHudPartyRestored,
            Scope = "Exact original PartyPCView.HideAnimation only while its radial replacement owns a hidden bar; models remain live."
        };
    }
}
