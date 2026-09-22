using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    internal sealed class TouchRadialEntry
    {
        internal Component View, Button;
        internal object Model, Mechanic;
        internal TouchSpaceRadialBinding Space;
        internal object Part;
        internal FieldInfo PartSlots;
        internal int PartKind;
        internal PropertyInfo ModelProperty;
        internal Sprite Icon, EndTurnGlow;
        internal Color IconColor = Color.white;
        internal string Label;
        // Native character/ability names retain the game's language. Only
        // captions and fallbacks authored by this mod use its own language.
        internal bool OwnLabel;
        internal string DisplayLabel => WeaponSet ? ModLocalization.Text("Switch weapon") + " · " + WeaponSetNumber + (string.IsNullOrEmpty(WeaponName) ? "" : "\n" + WeaponName) : OwnLabel ? ModLocalization.Text(Label) : Label;
        internal bool WeaponSet;
        internal int WeaponSetNumber;
        internal int NavigationDelta;
        internal string WeaponName;
        internal TouchRadialEntry VariantParent;
        internal bool Ability, Enabled, EndTurn, SpaceEndTurn, StartBattle;
        internal bool Character, Party, Selected, Current, Player, Enemy, Neutral, UnableToAct;
        internal bool LevelUp;
        internal Sprite LevelUpIcon;
        internal Color LevelUpColor = Color.white;
        internal int NativeOrder = -1;
        internal string StateLabel;
        internal string PortraitStatus, NativeHp;
        internal float CharacterRefreshAt;
    }
    public static partial class Main
    {
        static readonly TouchRadialPolicy _touchRadial = new TouchRadialPolicy();
        static readonly TouchRadialInfoGripPolicy _touchRadialInfoGrip = new TouchRadialInfoGripPolicy();
        internal static bool TouchRadialInfoGripHeld => _touchRadial.Visible && _touchRadial.Side == 0 && _overlayRightGripPressed;
        static readonly List<TouchRadialEntry> _touchRadialEntries = new List<TouchRadialEntry>(48);
        static TouchRadialContracts _touchRadialContracts;
        static TouchRadialCombatContracts _touchRadialCombatContracts;
        static TouchRadialLeftMode _touchRadialLeftMode;
        internal static TouchRadialLeftMode TouchRadialLeftMode => _touchRadialLeftMode;
        internal static bool TouchRadialLeftCanSwitch => _touchRadial.Visible && _touchRadial.Side == 0 ;
        static Component _touchRadialBar;
        static object _touchRadialBarModel, _touchRadialUnit;
        static bool _touchRadialCombat, _touchRadialConsumedFrame;
        static float _touchRadialRefreshAt;
        static string _touchRadialMessage, _touchRadialFault;
        static string _touchRadialLastAction;
        static int _touchRadialLastActionFrame;
        static string _touchRadialCombatActionsFault;
        static long _touchRadialOpens, _touchRadialActions, _touchRadialPointerActions;
        static long _touchRadialModeSwitches;
        internal static bool TouchRadialCaptured => TouchInputOwned && (_touchRadial.Captured || _touchRadialConsumedFrame);
        internal static double TouchRadialDwellProgress => _touchRadial.DwellProgress;
        internal static bool TouchRadialInformationOnly => _touchRadial.Side == 0 && (_touchRadialCombat || _touchRadialSpace) &&
            TouchRadialModePolicy.IsCharacters(_touchRadialLeftMode);
        internal static bool TouchRadialMenusAvailable => _touchRadialContracts != null;
        internal static bool TouchRadialPartyAvailableForHud => _touchRadialPartyContracts != null;
        internal static bool TouchRadialActionsAvailableForHud => _touchRadialContracts != null && _touchRadialCombatContracts != null;
        internal static bool TouchRadialCombatantsAvailableForHud => _touchRadialCharacterContracts != null;
        internal static bool TouchRadialChordPressed(XrTouchFrame sample)
        {
            const uint required = (uint)(XrTouchControl.Trigger | XrTouchControl.Squeeze);
            return ((sample.left.activeControls & required) == required && sample.left.trigger >= .45f && sample.left.squeeze >= .45f) ||
                ((sample.right.activeControls & required) == required && sample.right.trigger >= .45f && sample.right.squeeze >= .45f);
        }
        static void InstallTouchRadials()
        {
            try { _touchRadialContracts = TouchRadialContracts.Create(AccessTools.TypeByName); _touchRadialFault = null; }
            catch (Exception error) { _touchRadialContracts = null; _touchRadialFault = error.Message; _log.Error("[touch/radial] Native UI contract unavailable: " + error.Message); }
            InstallTouchRadialCharacters();
            InstallTouchRadialParty();
            InstallTouchRadialPortraitDetails();
            InstallTouchRadialCombatInformation();
            InstallTouchRadialAbilityInformation();
            InstallRadialCombatStatus();
            InstallTouchRadialEndTurn();
            InstallTouchRadialWeapons();
            InstallTouchWeaponComparison();
            InstallTouchSpaceRadials();
            try { _touchRadialCombatContracts = _touchRadialContracts == null ? null : TouchRadialCombatContracts.Create(_touchRadialContracts); _touchRadialCombatActionsFault = null; }
            catch (Exception error) { _touchRadialCombatContracts = null; _touchRadialCombatActionsFault = error.Message; _log.Error("[touch/radial] Left battle action context unavailable: " + error.Message); }
        }
        static bool TouchRadialCombatNow(out object unit)
        {
            unit = null; if (_presentation == null) return false;
            var game = _presentation.Game(); var turn = game == null ? null : _presentation.Turn(game);
            if (turn == null || !_presentation.InCombat(turn)) return false;
            unit = _presentation.CurrentUnit(turn); return true;
        }
        static bool TouchRadialSceneAllowed => LiveOverlayVr && (_attached || _modeFlat) && !NativeTutorialInputBlocked &&
            !CinematicWanted && !ObservedNativeLoading && _modeName != "MainMenu" && _modeName != "Loading";


        static void ProcessTouchRadials(bool overlayWasOpen)
        {
            bool capturedBefore = _touchRadial.Captured;
            _touchRadialConsumedFrame = false;
            var left = _touchSample.left; var right = _touchSample.right;
            bool overlayOwns = overlayWasOpen || TouchOverlayOpen || TouchOverlayChordCaptured;
            bool requested = (!overlayOwns && TouchRadialChordPressed(_touchSample)) || capturedBefore;
            try
            {
                // The ordinary idle path never searches for UI, reads game
                // selection, or allocates a list. Only a physical chord does.
                bool allowed = !requested || TouchRadialSceneAllowed;
                bool cancelB = requested && (right.buttons & (uint)XrTouchButton.Secondary) != 0;
                bool blocked = overlayOwns || !allowed || cancelB;
                if (_touchRadial.Visible && !TouchRadialContextValid())
                    blocked = overlayOwns || !allowed || cancelB || !RefreshTouchRadialInformationContext();
                int hit = _touchRadial.Visible && !blocked ? TouchRadialPointerHit() : -1;
                const uint controls = (uint)(XrTouchControl.Trigger | XrTouchControl.Squeeze);
                bool controlsValid = !requested || ((left.activeControls & controls) == controls && (right.activeControls & controls) == controls);
                int owner = _touchRadial.Side >= 0 ? _touchRadial.Side : _touchRawLeftTrigger && _overlayLeftGripPressed ? 0 : 1;
                if (requested) controlsValid &= owner == 0 ? left.AimValid : right.AimValid;
                int stick = _touchRadial.Visible ? TouchRadialPolicy.PickStable(owner == 0 ? left.stickX : right.stickX,
                    owner == 0 ? left.stickY : right.stickY, _touchRadialEntries.Count, _touchRadial.Selected, _touchRadialInnerCount) : -1;
                // Keep validating the native end-turn entry while an explicit
                // confirmation is held and the owner relaxes the selecting
                // stick. The current ray/empty stick is not its action target.
                int stickTarget = stick >= 0 ? stick : _touchRadial.HeldStickConfirmation;
                bool stickAvailable = stickTarget >= 0 && stickTarget < _touchRadialEntries.Count &&
                    TouchRadialEntryAvailable(_touchRadialEntries[stickTarget]);
                bool pointerAvailable = hit >= 0 && hit < _touchRadialEntries.Count &&
                    (hit == stick ? stickAvailable : TouchRadialEntryAvailable(_touchRadialEntries[hit]));
                int holdAction = -1;
                for (int i=0;i<_touchRadialEntries.Count;i++) if (_touchRadialEntries[i].EndTurn) { holdAction=i; break; }
                var result = _touchRadial.Step(_touchRawLeftTrigger, _overlayLeftGripPressed,
                    _touchRadialSettingsChord.Deferring ? _touchRadialSettingsChord.Pulse : _touchRawRightTrigger,
                    !_touchRadialSettingsChord.Deferring && _overlayRightGripPressed, _touchSampleValid && controlsValid, blocked, Time.unscaledTime,
                    left.stickX, left.stickY, right.stickX, right.stickY, _touchRadialEntries.Count, hit,
                    (left.activeControls & (uint)XrTouchControl.StickClick) != 0 && (left.buttons & (uint)XrTouchButton.StickClick) != 0,
                    stickAvailable, pointerAvailable, TouchRadialInformationOnly, TouchRadialAbilityMode,
                    (right.activeControls & (uint)XrTouchControl.Primary) != 0 && (right.buttons & (uint)XrTouchButton.Primary) != 0, -1, _touchRadialPointerObserved, _touchRadialInnerCount, holdAction);
                _touchRadialConsumedFrame = capturedBefore || _touchRadial.Captured || result != TouchRadialEvent.None;
                if (_touchRadialConsumedFrame)
                {
                    if (!capturedBefore) { CancelTouchPointerPress(); StopTouchGroupMovement(); _touchTabletop.Cancel(true); }
                    _touchPrimary.Cancel(); _touchSecondary.Cancel(); _touchOverlayTrigger.Cancel();
                    _touchGameCancel.Cancel(); _touchGamePause.Cancel(); _touchAnyButton.Cancel();
                    _touchGameAxesArmed = false; _touchClickFrozen = false;
                }
                if (result == TouchRadialEvent.Open) OpenTouchRadial();
                else if (result == TouchRadialEvent.Switch)
                {
                    _touchRadialLeftMode = TouchRadialModePolicy.Other(_touchRadialLeftMode); ++_touchRadialModeSwitches;
                    RebuildTouchRadialCatalogue();
                }
                else if (result == TouchRadialEvent.Execute)
                {
                    ClearTouchRadialAbilityInformation();
                    int selected = _touchRadial.Selected;
                    if ((!TouchRadialInformationOnly || selected >= 0 && selected < _touchRadialEntries.Count && _touchRadialEntries[selected].NavigationDelta != 0) && !TouchOverlayChordCaptured && !TouchOverlayChordPressed(_touchSample) && TouchRadialContextValid() &&
                        selected >= 0 && selected < _touchRadialEntries.Count && TouchRadialEntryAvailable(_touchRadialEntries[selected]))
                    {
                        ExecuteTouchRadial(_touchRadialEntries[selected]); ++_touchRadialActions;
                        if (_touchRadial.PointerCommitted) ++_touchRadialPointerActions;
                    }
                    if (!_touchRadial.Visible) { ClearTouchRadialVariants(); CloseTouchRadialVisual(); }
                }
                else if (result == TouchRadialEvent.Cancel) { ClearTouchRadialVariants(); CloseTouchRadialVisual(); }
                if (_touchRadial.Visible && Time.unscaledTime >= _touchRadialRefreshAt)
                {
                    _touchRadialRefreshAt = Time.unscaledTime + .15f;
                    RefreshTouchRadialCatalogue();
                    foreach (var entry in _touchRadialEntries) entry.Enabled = TouchRadialEntryAvailable(entry);
                }
                UpdateTouchRadialPortraitHover(_touchRadial.Visible ? (_touchRadial.Hovered >= 0 ? _touchRadial.Hovered : _touchRadial.Selected) : -1);
                // Selection and inspection are separate. A grip-held card must
                // not select a deployment actor merely because it is inspected.
                UpdateTouchRadialDeploymentSelection(_touchRadial.Visible ? _touchRadial.InspectIndex : -1);
                int information = _touchRadialInfoGrip.StepStable(_touchRadial.Visible && !blocked, _touchRadial.Side == 0,
                    _overlayRightGripPressed, _touchRadial.PreviewIndex, _touchRadialEntries.Count, Time.unscaledTime);
                UpdateTouchRadialCombatInformation(information);
                UpdateTouchRadialAbilityInformation(information);
                if (!blocked && _touchRadial.Visible && _touchRadial.Side == 0) ScrollSpatialInformation(right.stickY);
                else ResetSpatialInformationScroll();
            }
            catch (Exception error)
            {
                _touchRadial.Cancel(); _touchRadialConsumedFrame = true; ClearTouchRadialVariants(); CloseTouchRadialVisual();
                ClearTouchRadialPortraitHover();
                _touchRadialFault = error.Message; _log.Error("[touch/radial] Wheel cancelled; game input preserved after release: " + error.Message);
            }
        }
        static void OpenTouchRadial()
        {
            ++_touchRadialOpens;
            _touchRadialInnerCount = -1;
            if (_touchRadial.Side == 0)
            {
                bool combat = TouchRadialCombatNow(out var ignored);
                if (InSpaceCombat) combat = true;
                // Exploration is the only default that depends on selection.
                var units = combat ? null : TouchCameraSelectedUnits();
                _touchRadialLeftMode = TouchRadialModePolicy.InitialForPhase(combat, InSpaceCombat, TouchGroundPreparationNow(), units == null ? 0 : units.Count);
            }
            RebuildTouchRadialCatalogue();
        }
        static bool RefreshTouchRadialInformationContext()
        {
            // Initiative can repool/reorder native portrait views during a
            // hover. Replace that read-only catalogue instead of cancelling
            // the physical wheel. Action catalogues retain strict cancellation.
            if(_touchRadialSpace&&_touchRadial.Side==0&&InSpaceCombat)
            {
                RebuildTouchRadialCatalogue();_touchRadial.ContinueCatalogue();
                return TouchRadialContextValid();
            }
            if (_touchRadial.Side != 0 || !TouchRadialModePolicy.IsCharacters(_touchRadialLeftMode) ||
                (!TouchRadialCombatNow(out var ignored) && !TouchGroundPreparationNow())) return false;
            RebuildTouchRadialCatalogue();
            _touchRadial.ContinueCatalogue();
            return TouchRadialContextValid();
        }
        static void RebuildTouchRadialCatalogue()
        {
            _touchRadialInfoGrip.Reset();
            _touchRadial.ResetHoldConfirmation();
            ClearTouchRadialVariants();
            ClearTouchRadialPortraitHover();
            ClearTouchRadialCombatInformation(false);
            ClearTouchRadialAbilityInformation();
            _touchRadialEntries.Clear(); _touchRadialBar = null; _touchRadialBarModel = _touchRadialUnit = null;
            ClearTouchRadialCharacters();
            ClearTouchRadialParty();
            _touchRadialCombat = TouchRadialCombatNow(out var turnUnit);
            _touchRadialSpace = InSpaceCombat;
            bool portraits = _touchRadial.Side == 0 && TouchRadialModePolicy.IsCharacters(_touchRadialLeftMode);
            _touchRadialMessage = _touchRadial.Side == 0 ? (portraits ? (_touchRadialCombat ? "BATTLE PORTRAITS" : "PARTY") : "CHARACTER ACTIONS") : "COMMAND DECK";
            try
            {
            var c = _touchRadialContracts;
            if (portraits && _touchRadialSpace) { OpenTouchRadialCharacters(); return; }
            if (_touchRadialSpace && _touchRadial.Side == 0) { OpenTouchSpaceRadial(); return; }
            if (portraits) { if (_touchRadialCombat) OpenTouchRadialCharacters(); else OpenTouchRadialParty(); return; }
            if (c == null) { _touchRadialMessage = "Native action bar unavailable"; return; }
            if (_touchRadial.Side == 0)
            {
                bool needsSelection = TouchRadialModePolicy.RequiresSingleSelection(_touchRadial.Side, _touchRadialCombat);
                if (needsSelection)
                {
                    var units = TouchCameraSelectedUnits();
                    if (units == null || units.Count != 1) { _touchRadialMessage = _touchRadial.Side == 0 ? "Select one character · click left stick for portraits" : "Select one character"; return; }
                    _touchRadialUnit = units[0];
                }
                else if (_touchRadialCombatContracts == null) { _touchRadialMessage = "Native battle action context unavailable"; return; }
                foreach (var item in FindTouchRadialViews(c.BarView))
                {
                    var bar = item as Component; if (bar == null || !bar.gameObject.activeInHierarchy) continue;
                    object model = c.BarModel.GetValue(bar, null);
                    if (model == null) continue;
                    object actor = c.CurrentUnit.GetValue(model, null);
                    if (needsSelection ? !ReferenceEquals(actor, _touchRadialUnit) : actor == null || !_touchRadialCombatContracts.Matches(model, turnUnit)) continue;
                    if (!needsSelection) _touchRadialUnit = actor;
                    _touchRadialBar = bar; _touchRadialBarModel = model; break;
                }
                if (_touchRadialBar != null)
                {
                    object weaponSet = TouchRadialPart(2);
                    foreach (var slots in c.WeaponSlots) AddTouchRadialSlots(weaponSet, slots, 2);
                    AddTouchRadialWeaponSets();
                    if (_touchRadial.Side == 0) AddTouchRadialSlots(TouchRadialPart(1), c.ConsumableSlots, 1);
                    object abilities = TouchRadialPart(0);
                    AddTouchRadialSlots(abilities, c.AbilitySlots, 0);
                    if (abilities != null) AddTouchRadialSlot(c.OverdriveSlot.GetValue(abilities), abilities, c.OverdriveSlot, 0);
                }
            }
            else
            {
                AddTouchRadialMenus(c.MenuView, c.MenuModel, c.MenuButtons, TouchRadialContracts.MenuLabels);
                AddTouchRadialMenus(c.SettingsView, c.SettingsModel, c.SettingsButtons, TouchRadialContracts.SettingsLabels);
            }
            foreach (var entry in _touchRadialEntries) entry.Enabled = TouchRadialEntryAvailable(entry);
            if (_touchRadialEntries.Count == 0) _touchRadialMessage = "No actions available here";
            }
            finally
            {
            AddTouchRadialEndTurn();
            foreach (var entry in _touchRadialEntries) entry.Enabled = TouchRadialEntryAvailable(entry);
            // Every early exit clears old icon geometry and shows its reason.
            // Empty informational wheels cannot commit a stale character slot.
            if (_touchRadialEntries.Count == 0) { _touchRadialUnit = _touchRadialBarModel = null; _touchRadialBar = null; }
            _touchRadialRefreshAt = Time.unscaledTime + .15f;
            _radialCatalogueFingerprint = TouchRadialCatalogueFingerprint();
            RefreshTouchRadialWeaponIdentity();
            BuildTouchRadialRings();
            RebuildTouchRadialVisual();
            }
        }
        static object TouchRadialPart(int kind)
        {
            var c = _touchRadialContracts;
            if (_touchRadialBarModel == null) return null;
            if (kind == 0) return c.BarAbilities.GetValue(_touchRadialBarModel);
            if (kind == 1) return c.BarConsumables.GetValue(_touchRadialBarModel);
            object weapons = c.BarWeapons.GetValue(_touchRadialBarModel);
            return weapons == null ? null : c.WeaponSetValue.GetValue(c.WeaponCurrentSet.GetValue(weapons), null);
        }
        static void AddTouchRadialSlots(object part, FieldInfo slots, int kind)
        {
            if (part == null || !(slots.GetValue(part) is IEnumerable items)) return;
            foreach (var model in items) AddTouchRadialSlot(model, part, slots, kind);
        }
        static void AddTouchRadialSlot(object model, object part, FieldInfo slots, int kind)
        {
            var c = _touchRadialContracts;
            if (model == null || (bool)c.EmptyValue.GetValue(c.SlotEmpty.GetValue(model), null) || (bool)c.CharScreen.GetValue(model)) return;
            foreach (var existing in _touchRadialEntries) if (ReferenceEquals(existing.Model, model)) return;
            object mechanic = c.MechanicSlot.GetValue(model, null); if (mechanic == null) return;
            var icon = c.IconValue.GetValue(c.SlotIcon.GetValue(model), null) as Sprite;
            string nativeTitle = c.Title.Invoke(mechanic, null) as string;
            _touchRadialEntries.Add(new TouchRadialEntry { View = _touchRadialBar, Model = model,
                Part = part, PartSlots = slots, PartKind = kind, Icon = icon, Mechanic = mechanic, Ability = true,
                Label = nativeTitle ?? "Ability", OwnLabel = nativeTitle == null });
        }
        static void AddTouchRadialMenus(Type type, PropertyInfo modelProperty, FieldInfo[] buttons, string[] labels)
        {
            foreach (var item in FindTouchRadialViews(type))
            {
                var view = item as Component; if (view == null || !view.gameObject.activeInHierarchy) continue;
                object model = modelProperty.GetValue(view, null); if (model == null) continue;
                for (int i = 0; i < buttons.Length; ++i)
                {
                    var button = buttons[i].GetValue(view) as Component;
                    // Native hidden controls (for example co-op roles in a
                    // solo game) stay absent, rather than creating an extra
                    // ring made only of unavailable options.
                    if (button == null || !button.gameObject.activeInHierarchy) continue;
                    Sprite icon = null; Color iconColor = Color.white; int score = -1;
                    foreach (var image in button.GetComponentsInChildren<Image>(false))
                    {
                        if (image.sprite == null) continue;
                        string name = image.name.ToLowerInvariant();
                        int next = name.Contains("icon") ? 10 : name.Contains("mark") || name.Contains("frame") || name.Contains("background") ? 0 : 2;
                        if(labels[i]=="End turn" && (image.sprite.name.ToLowerInvariant().Contains("skull") || name.Contains("icon")))next+=20;
                        if (next > score) { icon = image.sprite; iconColor = image.color; score = next; }
                    }
                    if (score < 2) icon = null; // A frame/background is not an action icon.
                    Sprite turnGlow=null;
                    if(labels[i]=="End turn") { ReadNativeEndTurnArt(button,out icon,out turnGlow); iconColor=Color.white; }
                    _touchRadialEntries.Add(new TouchRadialEntry { View = view, Model = model, ModelProperty = modelProperty,
                        Button = button, Icon = icon, EndTurnGlow=turnGlow, IconColor = iconColor, Label = labels[i], OwnLabel = true });
                }
                break; // Exactly the live PC bar; never duplicate pooled views.
            }
        }
        static bool TouchRadialContextValid()
        {
            if (!TouchRadialSceneAllowed) return false;
            if (_touchRadialSpace != InSpaceCombat) return false;
            if (_touchRadialSpace && _touchRadial.Side == 0) return TouchRadialModePolicy.IsCharacters(_touchRadialLeftMode) ? TouchRadialCharactersContextValid() : TouchSpaceRadialContextValid();
            bool combat = TouchRadialCombatNow(out var unit);
            if (combat != _touchRadialCombat) return false;
            if (_touchRadial.Side == 0 && TouchRadialModePolicy.IsCharacters(_touchRadialLeftMode))
                return combat ? TouchRadialCharactersContextValid() : TouchRadialPartyContextValid();
            if (_touchRadialContracts == null) return _touchRadial.Side == 0 && _touchRadialEntries.Count == 0;
            if (_touchRadialUnit != null)
            {
                if (TouchRadialModePolicy.RequiresSingleSelection(_touchRadial.Side, combat))
                {
                    var selected = TouchCameraSelectedUnits();
                    if (selected == null || selected.Count != 1 || !ReferenceEquals(selected[0], _touchRadialUnit) ||
                        _touchRadial.Side == 1 && combat && !ReferenceEquals(unit, _touchRadialUnit)) return false;
                }
                else if (_touchRadialCombatContracts == null || !_touchRadialCombatContracts.Matches(_touchRadialBarModel, unit)) return false;
                if (_touchRadialBar == null || !ReferenceEquals(_touchRadialContracts.BarModel.GetValue(_touchRadialBar, null), _touchRadialBarModel) ||
                    !ReferenceEquals(_touchRadialContracts.CurrentUnit.GetValue(_touchRadialBarModel, null), _touchRadialUnit)) return false;
            }
            return true;
        }
        static bool TouchRadialEntryAvailable(TouchRadialEntry entry)
        {
            if (entry != null && entry.NavigationDelta != 0) return false;
            if (entry == null || entry.View == null || !entry.View.gameObject.activeInHierarchy ||
                entry.View is Behaviour viewBehaviour && !viewBehaviour.isActiveAndEnabled) return false;
            if (entry.Party) return TouchRadialPartyAvailable(entry);
            if (entry.Character) return TouchRadialCharacterAvailable(entry);
            if (entry.WeaponSet) return TouchRadialWeaponSetAvailable(entry);
            if (entry.VariantParent != null) return TouchRadialVariantAvailable(entry);
            if (entry.Space != null) return TouchSpaceRadialEntryAvailable(entry);
            if (entry.StartBattle) return TouchNativeStartBattleAvailable(entry);
            if(entry.SpaceEndTurn&&!TouchSpaceRadialContextValid())return false;
            if (entry.EndTurn && (entry.SpaceEndTurn ? _spaceCanEndTurn==null||!_spaceCanEndTurn(entry.Model)||!(_spacePlayerTurn(entry.Model)||(_spaceTorpedoTurn!=null&&_spaceTorpedoTurn(entry.Model))) :
                _touchRadialEndTurnContracts == null || !_touchRadialEndTurnContracts.Available(entry.Model))) return false;
            if (entry.Ability)
            {
                var c = _touchRadialContracts;
                if (_touchRadial.Side == 0 && _touchRadialCombat && (_touchRadialCombatContracts == null || !_touchRadialCombatContracts.Available(_touchRadialBarModel))) return false;
                if ((bool)c.EmptyValue.GetValue(c.SlotEmpty.GetValue(entry.Model), null) ||
                    (bool)c.CharScreen.GetValue(entry.Model) || !ReferenceEquals(TouchRadialPart(entry.PartKind), entry.Part) ||
                    !ReferenceEquals(c.MechanicSlot.GetValue(entry.Model, null), entry.Mechanic) ||
                    (!(bool)c.EmptyValue.GetValue(c.SlotPossible.GetValue(entry.Model), null) &&
                     !(bool)c.EmptyValue.GetValue(c.SlotConverts.GetValue(entry.Model), null))) return false;
                object slots = entry.PartSlots.GetValue(entry.Part);
                if (ReferenceEquals(slots, entry.Model)) return true;
                if (slots is IEnumerable items) foreach (object item in items) if (ReferenceEquals(item, entry.Model)) return true;
                return false;
            }
            return entry.Button != null && entry.Button.gameObject.activeInHierarchy &&
                (!(entry.Button is Behaviour buttonBehaviour) || buttonBehaviour.isActiveAndEnabled) &&
                ReferenceEquals(entry.ModelProperty.GetValue(entry.View, null), entry.Model) &&
                (entry.EndTurn || _touchRadialContracts.MenuAvailable(entry.View, entry.Label)) &&
                (bool)_touchRadialContracts.Interactable.GetValue(entry.Button, null);
        }
        static void ExecuteTouchRadial(TouchRadialEntry entry)
        {
            if (entry.EndTurn && !_touchRadial.LongPressCommitted) return;
            if (entry != null && entry.NavigationDelta != 0) return;
            // Recheck at the last boundary as well: native menu flags, selected
            // actor or turn can change during the dwell and pointer callbacks.
            if (entry != null && entry.Character && !entry.Party) return;
            if (!TouchRadialContextValid() || !TouchRadialEntryAvailable(entry)) return;
            _touchRadialLastAction = entry.Label; _touchRadialLastActionFrame = Time.frameCount;
            if (entry.WeaponSet)
            {
                SwitchTouchRadialWeaponSet(entry);
                return; // Preserve the same physical plane throughout the switch.
            }
            // Hide before native callbacks. They may open a modal, destroy this
            // bar, change turns, or enter ability-targeting; none is simulated.
            CloseTouchRadialVisual();
            ClearTouchRadialPortraitHover();
            // The real PC view's guards (one selected unit, not character-sheet,
            // live bound slot; battle-left uses the native turn actor instead)
            // were checked above. Dispatch its same VM entry so
            // folded rows work without activating or cloning native UI views.
            if (entry.Party) ExecuteTouchRadialParty(entry);
            else if (entry.Character) ExecuteTouchRadialCharacter(entry);
            else if (entry.Ability)
            {
                if (entry.Space != null) _touchSpaceRadialContracts.Click(entry.Model);
                else _touchRadialContracts.ModelClick.Invoke(entry.Model, null);
                OpenTouchRadialVariants(entry);
            }
            else
            {
                var es = EventSystem.current; if (es == null) return;
                var target = entry.Button.gameObject;
                var data = new PointerEventData(es) { button = PointerEventData.InputButton.Left, clickCount = 1,
                    eligibleForClick = true, pointerPress = target, rawPointerPress = target, pointerEnter = target };
                if (target.transform is RectTransform rect)
                    data.position = RectTransformUtility.WorldToScreenPoint(_pickCam, rect.TransformPoint(rect.rect.center));
                data.pressPosition = data.position;
                try
                {
                    ExecuteEvents.Execute(target, data, ExecuteEvents.pointerEnterHandler);
                    ExecuteEvents.Execute(target, data, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.Execute(target, data, ExecuteEvents.pointerUpHandler);
                    if (target != null && TouchRadialEntryAvailable(entry))
                    {
                        bool confirmedBefore = _endTurnConfirmed;
                        if (entry.EndTurn) _endTurnConfirmed = true;
                        try { ExecuteEvents.Execute(target, data, ExecuteEvents.pointerClickHandler); }
                        finally { _endTurnConfirmed = confirmedBefore; }
                        if (!entry.EndTurn) NotifyTouchRadialMenuOpened(entry.View, entry.Button);
                    }
                }
                finally { if (target != null) ExecuteEvents.Execute(target, data, ExecuteEvents.pointerExitHandler); }
            }
        }
        static void StopTouchRadials()
        {
            ClearTouchRadialVariants(); _radialWeapons = null;
            _touchRadial.Reset(); _touchRadialConsumedFrame = false; _touchRadialEntries.Clear();
            ClearTouchRadialCatalogueState();
            _touchRadialBar = null; _touchRadialBarModel = _touchRadialUnit = null; _touchRadialContracts = null;
            ClearTouchRadialCharacters(); _touchRadialCharacterContracts = null;
            ClearTouchRadialParty(); ClearTouchRadialPortraitHover(); _touchRadialCombatContracts = null; _touchRadialPartyContracts = null;
            DestroyTouchRadialVisual();
        }
        internal static object TouchRadialSnapshot() => new { Ready = _touchRadialContracts != null, Fault = _touchRadialFault,
            BattlePortraitsReady = _touchRadialCharacterContracts != null, BattlePortraitsFault = _touchRadialCharacterFault,
            LeftMode = _touchRadialLeftMode.ToString(), ModeSwitches = _touchRadialModeSwitches, BattleActionsFault = _touchRadialCombatActionsFault,
            Visible = _touchRadial.Visible, Captured = TouchRadialCaptured, Side = _touchRadial.Side, Selected = _touchRadial.Selected,
            Hovered = _touchRadial.Hovered, Count = _touchRadialEntries.Count, Opens = _touchRadialOpens, Actions = _touchRadialActions,
            SingleRing=TouchRadialPolicy.Rings(_touchRadialEntries.Count,_touchRadialInnerCount)==1, CatalogueCount=_touchRadialCatalogue.Count, Pagination=false,
            InnerCount=TouchRadialPolicy.Split(_touchRadialEntries.Count,_touchRadialInnerCount), PointerWaitingExit=_touchRadial.PointerWaitingExit,
            StickDeadzone=TouchRadialPolicy.Deadzone, StickReleaseDeadzone=TouchRadialPolicy.ReleaseDeadzone,
            ActiveWeaponName=_radialActiveWeaponName, ActiveWeaponSet=_radialActiveWeaponSet, WeaponSwitchKeepsWheelOpen=true,
            LastAction=_touchRadialLastAction, LastActionFrame=_touchRadialLastActionFrame,
            OppositePointerActions = _touchRadialPointerActions, NativeActions = true, LayoutBuilds = _touchRadialLayoutBuilds,
            OpenHoldSeconds=TouchRadialPolicy.HoldSeconds, MetresPerPixel=TouchRadialProjection.MetresPerPixel,
            StickDwellSeconds=TouchRadialPolicy.DwellSeconds, DwellProgress=_touchRadial.DwellProgress, OwnerReleaseExecutes=false,
            EdgeCoverageAA=true, TextRasterScale=2, InformationWindowNative=true,
            InformationFault=_touchRadialInformationFault, InformationOnlyBattlePortraits=true,
            BattlePortraitsHoverOnly=false, BattlePortraitsConfirm=false, BattlePortraitInformationLatched=false,
            InformationHeldByRightGrip=true, EndTurnHoldSeconds=TouchRadialHoldPolicy.Seconds, EndTurnStickOnly=true,
            AbilityConfirmAOrOppositeTrigger=true, AbilityDwellExecutes=false, AbilityInformationFault=_touchRadialAbilityInfoFault,
            ImmediateInformationHide=_touchRadialInformationContracts != null && _touchRadialInformationContracts.ImmediateCloseAvailable,
            EndTurnReady=TouchRadialEndTurnAvailable, DisabledArtFault=_touchRadialDisabledArt?.Fault,
            SectorVertices = _touchRadialMeshVertices, NativeIcons = _touchRadialNativeIcons, HudCameraPasses = _touchRadialHudCameras };
    }
}
