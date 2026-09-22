using System;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static TouchRadialInformationContracts _touchRadialInformationContracts;
        static readonly TouchRadialInformationLease _touchRadialInformationLease = new TouchRadialInformationLease();
        static TouchRadialEntry _touchRadialInformationEntry;
        static Component _touchRadialInformationView;
        static object _touchRadialInformationModel;
        static string _touchRadialInformationFault;
        static float _touchRadialInformationNext;
        internal static bool TouchRadialInformationHoverActive => TouchRadialInfoGripHeld && (TouchRadialInformationIsAbility || (_touchRadial.Visible &&
            TouchRadialModePolicy.IsCharacters(_touchRadialLeftMode) &&
            _touchRadialInformationEntry != null && _touchRadialInformationLease.OwnsCurrent));
        internal static Component TouchRadialInformationWindow => TouchRadialInformationIsAbility ? _touchRadialAbilityInfoLease.Window as Component :
            TouchRadialInformationHoverActive && _touchRadialInformationView != null ?
            _touchRadialInformationContracts.InfoWindow(_touchRadialInformationView) as Component : null;
        internal static int TouchRadialInformationRevision => _touchRadialInformationLease.Revision + _touchRadialAbilityInfoLease.Revision;
        internal static Component TouchRadialInformationContent
        {
            get
            {
                var window=TouchRadialInformationWindow;if(window==null)return null;
                // Both original PC windows put their logic on a fullscreen
                // root, while HeaderContainer belongs to the fitted Window
                // child (the surface prefab offsets it by x=-631 pixels).
                // Move the real card, retaining the native root and callbacks.
                var header=(TouchRadialInformationIsAbility ? _touchRadialAbilityInfoContracts.Header(window) :
                    _touchRadialInformationContracts.Header(window)) as RectTransform;
                return header!=null ? header.parent as RectTransform : null;
            }
        }
        // The original InspectPCView owns all information content and layout.
        static string _touchRadialInformationText => null;

        static void InstallTouchRadialCombatInformation()
        {
            try { _touchRadialInformationContracts = TouchRadialInformationContracts.Create(AccessTools.TypeByName); _touchRadialInformationFault = null; }
            catch (Exception error) { _touchRadialInformationContracts = null; ReportTouchRadialInformationFault(error); }
        }

        static void BeginTouchRadialCombatInformation()
        {
            ClearTouchRadialCombatInformation(false);
            var c = _touchRadialInformationContracts;
            if (c == null) return;
            // Exact UI field path; no scene scan, prefab cloning, new VM,
            // subscriptions or tooltip construction by the mod.
            _touchRadialInformationView = c.ReadView() as Component;
            _touchRadialInformationModel = _touchRadialInformationView == null ? null : c.Model(_touchRadialInformationView);
            if (_touchRadialInformationModel != null && !c.InGameModel.IsInstanceOfType(_touchRadialInformationModel))
            { _touchRadialInformationView = null; _touchRadialInformationModel = null; }
        }

        static void UpdateTouchRadialCombatInformation(int index)
        {
            if (!TouchRadialInfoGripHeld || !TouchRadialModePolicy.IsCharacters(_touchRadialLeftMode))
            { ClearTouchRadialCombatInformation(false); return; }
            var entry = index >= 0 && index < _touchRadialEntries.Count ? _touchRadialEntries[index] : null;
            if (entry == null || !entry.Character)
            { ClearTouchRadialCombatInformation(false); return; }
            if (!ReferenceEquals(entry, _touchRadialInformationEntry))
            {
                _touchRadialInformationLease.Clear(false);
                _touchRadialInformationEntry = entry;
                // Stick or a fresh opposite trigger requested this actor.
                // Immediate replacement; steady selection never rebuilds it.
                _touchRadialInformationNext = Time.unscaledTime;
            }
            if (Time.unscaledTime < _touchRadialInformationNext) return;
            ShowTouchRadialCombatInformation(entry);
            _touchRadialInformationNext = Time.unscaledTime + .15f;
        }

        static bool ShowTouchRadialCombatInformation(TouchRadialEntry entry)
        {
            var c = _touchRadialInformationContracts;
            if (c == null || entry == null || !entry.Character || entry.Mechanic == null ||
                _touchRadialInformationView == null || _touchRadialInformationModel == null) return false;
            try
            {
                if (!ReferenceEquals(c.ReadView(), _touchRadialInformationView) ||
                    !ReferenceEquals(c.Model(_touchRadialInformationView), _touchRadialInformationModel) ||
                    !ReferenceEquals(entry.Party ? _touchRadialPartyContracts.Unit(entry.Model) :
                        _touchRadialCharacterContracts.Unit.GetValue(entry.Model, null), entry.Mechanic))
                { ClearTouchRadialCombatInformation(false); return false; }
                if (!_touchRadialInformationLease.Show(c, _touchRadialInformationView, _touchRadialInformationModel, entry.Mechanic, false)) return false;
                // A held portrait preview must not enter management-window
                // sizing, add a Close footer, or acquire ordinary UI pointing.
                // Presentation centres the original view in the headset.
                return true;
            }
            catch (Exception error) { ClearTouchRadialCombatInformation(false); ReportTouchRadialInformationFault(error); return false; }
        }

        static void CommitTouchRadialCombatInformation(TouchRadialEntry entry)
        {
            // Retained as a harmless compatibility entry point. Battle portrait
            // confirmation is no longer an action; previews never outlive hold.
        }

        static TouchRadialEntry _touchDeploymentSelection;
        static void UpdateTouchRadialDeploymentSelection(int index)
        {
            var entry = index >= 0 && index < _touchRadialEntries.Count ? _touchRadialEntries[index] : null;
            if (entry == null) { _touchDeploymentSelection = null; return; }
            if (ReferenceEquals(entry, _touchDeploymentSelection)) return;
            _touchDeploymentSelection = entry;
            if (!InSpaceCombat && TouchGroundPreparationNow() && entry.Character && !entry.Party && entry.Mechanic != null &&
                _touchRadialCharacterContracts != null)
                _touchRadialCharacterContracts.Commit(entry.Mechanic);
        }

        static void ClearTouchRadialCombatInformation(bool force)
        {
            _touchRadialInformationEntry = null;
            try { _touchRadialInformationLease.Clear(force); }
            catch (Exception error) { ReportTouchRadialInformationFault(error); }
        }

        static void ReportTouchRadialInformationFault(Exception error)
        {
            if (_touchRadialInformationFault == error.Message) return;
            _touchRadialInformationFault = error.Message;
            _log.Error("[touch/radial] Native battle information: " + error.Message);
        }
    }
}
