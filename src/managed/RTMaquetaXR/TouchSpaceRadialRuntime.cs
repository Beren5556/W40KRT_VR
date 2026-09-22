using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    internal sealed class TouchSpaceRadialBinding
    {
        internal object Group, Post, WeaponKey;
        internal int Kind; // 0 native weapon mount, 1 own abilities, 2 ship post
    }
    public static partial class Main
    {
        static TouchSpaceRadialContracts _touchSpaceRadialContracts;
        static TouchSpaceRadialContext _touchSpaceRadialContext;
        static bool _touchRadialSpace;
        static string _touchSpaceRadialFault;
        static long _touchSpaceRadialCatalogues, _touchSpaceRadialInvalidations;
        static void InstallTouchSpaceRadials()
        {
            try { _touchSpaceRadialContracts = TouchSpaceRadialContracts.Create(AccessTools.TypeByName); }
            catch (Exception error) { _touchSpaceRadialContracts = null; _touchSpaceRadialFault = error.Message;
                _log.Error("[touch/space-radial] Native ship actions unavailable; original HUD retained: " + error.Message); }
        }
        static TouchSpaceRadialContext ReadTouchSpaceRadialContext()
        {
            var c = _touchSpaceRadialContracts; if (c == null || !InSpaceCombat) return default(TouchSpaceRadialContext);
            var g = c.Game(); if (g == null) return default(TouchSpaceRadialContext);
            var view = c.View(g) as Component;
            if (view == null || !view.gameObject.activeInHierarchy || view is Behaviour b && !b.isActiveAndEnabled)
                return default(TouchSpaceRadialContext);
            var model = c.ViewModel(view); var turn = c.Turn(g);
            if (model == null || turn == null) return default(TouchSpaceRadialContext);
            var weapons = c.Weapons(model); var posts = c.Posts(model); var player = c.Player(g); var input = c.Input(g); var exit = c.Exit(model);
            return new TouchSpaceRadialContext { View = view, Model = model, Turn = turn, Unit = c.TurnUnit(turn), Area = c.Area(g),
                Weapons = weapons, Posts = posts, SelectedUnit = weapons == null ? null : c.WeaponUnit(weapons),
                PlayerShip = player == null ? null : c.PlayerShip(player), Round = c.Round(turn), PlayerTurn = c.PlayerTurn(turn),
                Ending = c.EndingTurn(turn), Locked = input == null || c.InputLocked(input), Exit = exit != null && c.ExitActive(exit) };
        }
        static void OpenTouchSpaceRadial()
        {
            if (_touchRadial.Side != 0) return; // Right hand always belongs to native menus.
            ++_touchSpaceRadialCatalogues;
            _touchRadialMessage = "SPACE WEAPONS";
            _touchSpaceRadialContext = ReadTouchSpaceRadialContext(); var snapshot = _touchSpaceRadialContext; var c = _touchSpaceRadialContracts;
            if (c == null || !snapshot.Ready) { _touchRadialMessage = "Native ship actions unavailable"; return; }
            if (!ReferenceEquals(snapshot.Unit, snapshot.SelectedUnit)) { _touchRadialMessage = "Select the unit whose turn it is"; return; }
            _touchRadialUnit = snapshot.Unit;
            if (_touchRadial.Side == 0)
            {
                // Dictionary insertion order is not a UI contract. Native mount
                // enum order gives each opening the same grouping and positions.
                if (snapshot.Weapons != null && c.WeaponGroups(snapshot.Weapons) is IDictionary mounts)
                {
                    var keys = new List<object>(); foreach (DictionaryEntry pair in mounts) keys.Add(pair.Key);
                    keys.Sort();
                    foreach (var key in keys) AddTouchSpaceRadialGroup(mounts[key], new TouchSpaceRadialBinding { Kind = 0, WeaponKey = key });
                }
                if (snapshot.Weapons != null) AddTouchSpaceRadialGroup(c.OwnGroup(snapshot.Weapons), new TouchSpaceRadialBinding { Kind = 1 });
            }
            if (snapshot.MainShip && snapshot.Posts != null && c.PostList(snapshot.Posts) is IEnumerable posts)
            {
                foreach (var post in posts) if (post != null)
                    AddTouchSpaceRadialGroup(c.PostGroup(post), new TouchSpaceRadialBinding { Kind = 2, Post = post });
            }
            // An auxiliary has only the own actions the installed native panel
            // actually publishes. It never inherits the main ship's post VMs.
            if (_touchRadialEntries.Count == 0) _touchRadialMessage = _touchRadial.Side == 1 && !snapshot.MainShip ?
                "No ship-post actions for this unit" : "No actions available here";
        }
        static void AddTouchSpaceRadialGroup(object group, TouchSpaceRadialBinding binding)
        {
            var c = _touchSpaceRadialContracts; var snapshot = _touchSpaceRadialContext;
            if (group == null || !(c.GroupSlots(group) is IEnumerable slots)) return;
            binding.Group = group;
            var nativeGroup = c.GroupLabel(group);
            foreach (var slot in slots)
            {
                if (slot == null || c.Empty(slot) || c.CharScreen(slot)) continue;
                var mechanic = c.SlotMechanic(slot);
                // Native slot lists are updated asynchronously after selection.
                // Their own mechanic owner is the final source of identity.
                if (mechanic == null || !ReferenceEquals(c.SlotOwner(mechanic), snapshot.Unit)) continue;
                bool duplicate = false; foreach (var old in _touchRadialEntries) if (ReferenceEquals(old.Model, slot)) { duplicate = true; break; }
                if (duplicate) continue;
                var title = c.SlotTitle(mechanic);
                _touchRadialEntries.Add(new TouchRadialEntry { View = snapshot.View as Component, Model = slot, Mechanic = mechanic,
                    Ability = true, Space = binding, Icon = c.SlotIcon(slot) as Sprite,
                    Label = title == null ? "Ability" : string.IsNullOrEmpty(nativeGroup) ? title : nativeGroup + " · " + title,
                    OwnLabel = title == null });
            }
        }
        static bool TouchSpaceRadialContextValid()
        {
            if (_touchSpaceRadialContracts == null) return _touchRadialEntries.Count == 0;
            bool valid = _touchSpaceRadialContext.Same(ReadTouchSpaceRadialContext());
            if (!valid) ++_touchSpaceRadialInvalidations;
            return valid;
        }
        static bool TouchSpaceRadialEntryAvailable(TouchRadialEntry entry)
        {
            var c = _touchSpaceRadialContracts; var binding = entry.Space;
            if (c == null || binding == null) return false;
            var snapshot = ReadTouchSpaceRadialContext();
            if (!_touchSpaceRadialContext.Same(snapshot) || !snapshot.CanAct || !c.SlotAvailable(entry.Model, entry.Mechanic, snapshot.Unit) || !c.Contains(binding.Group, entry.Model)) return false;
            if (binding.Kind < 2)
            {
                // IsActive on these VMs belongs only to expanded console
                // panels. PC actions use turn/slot permission, not that flag.
                if (snapshot.Weapons == null || !c.WeaponsTurn(snapshot.Weapons)) return false;
                if (binding.Kind == 1) return ReferenceEquals(c.OwnGroup(snapshot.Weapons), binding.Group);
                var mounts = c.WeaponGroups(snapshot.Weapons) as IDictionary;
                return mounts != null && mounts.Contains(binding.WeaponKey) && ReferenceEquals(mounts[binding.WeaponKey], binding.Group);
            }
            if (!snapshot.MainShip || snapshot.Posts == null || !c.PostsTurn(snapshot.Posts) ||
                binding.Post == null || c.PostBlocked(binding.Post) || !ReferenceEquals(c.PostGroup(binding.Post), binding.Group)) return false;
            if (c.PostList(snapshot.Posts) is IEnumerable posts) foreach (var post in posts) if (ReferenceEquals(post, binding.Post)) return true;
            return false;
        }
        static bool TouchSpaceRadialSlotBound(TouchRadialEntry entry)
        {
            var c = _touchSpaceRadialContracts;
            if (c == null || entry?.Space == null || entry.Model == null || entry.Mechanic == null ||
                !ReferenceEquals(c.SlotMechanic(entry.Model), entry.Mechanic) || !ReferenceEquals(c.SlotOwner(entry.Mechanic), _touchSpaceRadialContext.Unit) ||
                !c.Contains(entry.Space.Group, entry.Model)) return false;
            var snapshot = ReadTouchSpaceRadialContext(); var binding = entry.Space;
            if (!_touchSpaceRadialContext.Same(snapshot)) return false;
            if (binding.Kind == 1) return snapshot.Weapons != null && ReferenceEquals(c.OwnGroup(snapshot.Weapons), binding.Group);
            if (binding.Kind == 0) { var mounts = snapshot.Weapons == null ? null : c.WeaponGroups(snapshot.Weapons) as IDictionary;
                return mounts != null && mounts.Contains(binding.WeaponKey) && ReferenceEquals(mounts[binding.WeaponKey], binding.Group); }
            if (!snapshot.MainShip || snapshot.Posts == null || binding.Post == null || !ReferenceEquals(c.PostGroup(binding.Post), binding.Group)) return false;
            if (c.PostList(snapshot.Posts) is IEnumerable posts) foreach (var post in posts) if (ReferenceEquals(post, binding.Post)) return true;
            return false;
        }
        internal static object TouchSpaceRadialDiagnostics() => new { Ready = _touchSpaceRadialContracts != null, Fault = _touchSpaceRadialFault,
            Active = _touchRadialSpace && _touchRadial.Visible, Catalogues = _touchSpaceRadialCatalogues, Invalidations = _touchSpaceRadialInvalidations,
            NativeEndTurnRetained = true, SlotOwnerValidated = true, BothWheelsExplicitConfirm = true, ThirdPartyActionsInherited = false };
    }
}
