using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static TouchRadialCharacterContracts _touchRadialCharacterContracts;
        static Component _touchRadialTracker, _touchRadialTrackerCurrentView;
        static object _touchRadialTrackerModel, _touchRadialTrackerCurrent;
        static readonly List<object> _touchRadialTrackerEntries = new List<object>(32);
        static readonly List<object> _touchRadialTrackerModels = new List<object>(32);
        static string _touchRadialCharacterFault;
        static void InstallTouchRadialCharacters()
        {
            try { _touchRadialCharacterContracts = TouchRadialCharacterContracts.Create(AccessTools.TypeByName); _touchRadialCharacterFault = null; }
            catch (Exception error)
            {
                _touchRadialCharacterContracts = null; _touchRadialCharacterFault = error.Message;
                _log.Error("[touch/radial] Battle portraits unavailable; other wheels unchanged: " + error.Message);
            }
        }
        static void ClearTouchRadialCharacters()
        {
            ClearTouchRadialCombatInformation(false);
            ClearTouchRadialAbilityInformation();
            _touchRadialInformationView = null; _touchRadialInformationModel = null;
            _touchRadialTracker = _touchRadialTrackerCurrentView = null;
            _touchRadialTrackerModel = _touchRadialTrackerCurrent = null;
            _touchRadialTrackerEntries.Clear(); _touchRadialTrackerModels.Clear();
        }
        static void OpenTouchRadialCharacters()
        {
            BeginTouchRadialCombatInformation();
            var c = _touchRadialCharacterContracts;
            if (c == null) { _touchRadialMessage = "Battle portrait panel unavailable"; return; }
            foreach (var item in FindTouchRadialViews(c.TrackerView))
            {
                var view = item as Component; if (view == null || !view.gameObject.activeInHierarchy) continue;
                var model = c.TrackerModel.GetValue(view, null); if (model == null) continue;
                _touchRadialTracker = view; _touchRadialTrackerModel = model; break;
            }
            if (_touchRadialTracker == null) { _touchRadialMessage = "Battle portrait panel not visible"; return; }
            var entries = c.Entries.GetValue(_touchRadialTracker) as IList;
            if (entries == null) { _touchRadialMessage = "Battle portraits are updating"; return; }
            foreach (var data in entries) _touchRadialTrackerEntries.Add(data);
            _touchRadialTrackerCurrentView = c.CurrentView.GetValue(_touchRadialTracker) as Component;
            _touchRadialTrackerCurrent = _touchRadialTrackerCurrentView == null ? null : c.ViewModel.GetValue(_touchRadialTrackerCurrentView, null);
            c.CopyModels(entries, _touchRadialTrackerCurrent, _touchRadialTrackerModels);
            var prefab = c.Prefab.GetValue(_touchRadialTracker) as Component;
            foreach (var model in _touchRadialTrackerModels)
            {
                var unit = c.Unit.GetValue(model, null); if (unit == null) continue;
                Component bound = ReferenceEquals(model, _touchRadialTrackerCurrent) ? _touchRadialTrackerCurrentView : null;
                if (bound == null) foreach (var data in entries)
                {
                    if (data == null || !c.VirtualUnitData.IsInstanceOfType(data) || !ReferenceEquals(c.DataModel.GetValue(data, null), model)) continue;
                    var candidate = c.BoundView.GetValue(data, null) as Component;
                    if (candidate != null && ReferenceEquals(c.ViewModel.GetValue(candidate, null), model)) bound = candidate;
                    break;
                }
                Sprite icon = null; Color tint = Color.white;
                // The original prefab declares Icon/Small/Middle portrait.
                // Offscreen pooled entries use its same wrapper getter, never
                // clone a Unit or invent a generic portrait texture.
                var source = bound != null ? bound : prefab;
                if (source != null)
                {
                    var zone = ((bool)c.Subtype.GetValue(model) ? c.SubtypeZone : c.PortraitZone).GetValue(source);
                    if (zone != null)
                    {
                        var picture = c.Picture.GetValue(zone) as Image;
                        if (picture != null) { tint = picture.color; if (bound != null) icon = picture.sprite; }
                        if (icon == null)
                        {
                            int size = Convert.ToInt32(c.PortraitSize.GetValue(zone)); if (size < 0 || size > 2) size = 1;
                            icon = c.Portraits[size].GetValue(c.Wrapper.GetValue(model), null) as Sprite;
                        }
                    }
                }
                // The tracker often uses its tiny Icon portrait. The same
                // native wrapper supplies the full artwork; retain the native
                // fallback for ships/custom portraits without a larger image.
                var wrapper=c.Wrapper.GetValue(model);
                if(wrapper!=null)
                    for(int size=c.Portraits.Length-1;size>=0;size--)
                    { var detailed=c.Portraits[size].GetValue(wrapper,null) as Sprite;
                      if(detailed!=null && (icon==null || detailed.rect.width*detailed.rect.height>icon.rect.width*icon.rect.height)) {icon=detailed;break;} }
                string nativeName = c.DisplayName.GetValue(model, null) as string;
                var entry = new TouchRadialEntry { Character = true, View = _touchRadialTracker, Model = model,
                    Mechanic = unit, Icon = icon, IconColor = tint, Label = nativeName ?? "Character", OwnLabel = nativeName == null };
                entry.Enabled = TouchRadialCharacterAvailable(entry); _touchRadialEntries.Add(entry);
            }
            if (_touchRadialEntries.Count == 0) _touchRadialMessage = "No battle portraits available";
        }
        static bool TouchRadialCharactersContextValid()
        {
            var c = _touchRadialCharacterContracts;
            if (c == null || _touchRadialTracker == null || !_touchRadialTracker.gameObject.activeInHierarchy) return _touchRadialEntries.Count == 0;
            if (!ReferenceEquals(c.TrackerModel.GetValue(_touchRadialTracker, null), _touchRadialTrackerModel)) return false;
            if (!c.SameEntries(c.Entries.GetValue(_touchRadialTracker) as IList, _touchRadialTrackerEntries)) return false;
            var currentView = c.CurrentView.GetValue(_touchRadialTracker) as Component;
            return currentView == _touchRadialTrackerCurrentView && ReferenceEquals(currentView == null ? null : c.ViewModel.GetValue(currentView, null), _touchRadialTrackerCurrent);
        }
        static bool TouchRadialCharacterAvailable(TouchRadialEntry entry)
        {
            var c = _touchRadialCharacterContracts;
            if (c == null || !ReferenceEquals(c.Unit.GetValue(entry.Model, null), entry.Mechanic) || entry.Mechanic == null) return false;
            if (entry.StateLabel != null && Time.unscaledTime < entry.CharacterRefreshAt) return true;
            entry.CharacterRefreshAt = Time.unscaledTime + .15f;
            entry.Selected = c.Flag(c.Selected, c.UnitState.GetValue(entry.Model)); entry.Current = c.Flag(c.Current, entry.Model);
            entry.Player = c.Flag(c.Player, entry.Model); entry.NativeOrder = c.Order(entry.Model);
            entry.Enemy = c.Flag(c.Enemy, entry.Model); entry.Neutral = c.Flag(c.Neutral, entry.Model);
            bool losesTurn = c.Flag(c.WillNotTurn, entry.Model), control = c.Flag(c.LostControl, entry.Model);
            entry.UnableToAct = c.Flag(c.Unable, entry.Model) || losesTurn || control;
            string state = entry.Current ? "CURRENT TURN" : entry.Selected ? "SELECTED" : entry.Enemy ? "ENEMY" : entry.Neutral ? "NEUTRAL" : "ALLY";
            if (entry.UnableToAct) state += losesTurn ? " · SKIPS TURN" : control ? " · CONTROL LOST" : " · CANNOT ACT";
            SetTouchRadialPortraitText(entry, state, c.HpText(c.Health(entry.Model)));
            // Every live portrait is informational, including enemies and
            // units unable to act. Turn eligibility never greys this catalogue.
            return true;
        }
        static void ExecuteTouchRadialCharacter(TouchRadialEntry entry)
        {
            // Deliberately no action: combat portraits are consulted only while
            // pointed at. Future callback paths must not commit or close them.
        }
        static void FocusTouchRadialCharacter(object unit)
        {
            if (!_touchTabletop.Initialized || unit == null) return;
            var navigation = _touchRadialCharacterContracts ?? _touchRadialPartyContracts?.Navigation;
            if (navigation == null) return;
            var position = navigation.Position(unit);
            if (!TouchTabletopState.Finite(TablePoint(position))) return;
            // Owlcat's ScrollTo moves its source rig; VR owns its own tabletop.
            // Mirror the chosen navigation in that mapping, retaining all zoom,
            // yaw and tilt. Never infer a double click or enter the actor's head.
            LeaveTouchHeadView(); ClearTouchCameraPendingFocus();
            TouchRadialFocusPolicy.Center(_touchTabletop, TablePoint(position));
            _combatFocus.Cancel(true); _touchFollowReady = _touchFollowWasMoving = false;
            _touchThirdPersonFollower.Reset(); ++_touchFocuses;
        }
    }
}
