using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    internal sealed class TouchRadialWeaponContracts
    {
        internal Func<object, object> Converted, Sets;
        internal Func<object, object> MainHand, OffHand, ItemIcon, ItemIdentity;
        internal Func<object, string> ItemName;
        internal Func<object, bool> CanSwitch;
        internal Func<object, bool> TwoHanded;
        internal Func<object, int> Index;
        internal Action<object> Switch, CloseConvert;
        internal FieldInfo VariantSlots;
        internal static TouchRadialWeaponContracts Create(TouchRadialContracts bar)
        {
            var c = new TouchRadialWeaponContracts();
            var slot = bar.SlotModel.PropertyType; var weapons = bar.BarWeapons.FieldType;
            var set = bar.WeaponSetValue.PropertyType;
            c.MainHand = TouchRadialPartyContracts.Reactive<object>(set, "MainHandWeapon");
            c.OffHand = TouchRadialPartyContracts.Reactive<object>(set, "OffHandWeapon");
            c.TwoHanded = PcUiPath.BooleanField(PcUiPath.Field(set,"IsTwoHanded"));
            var item = PcUiPath.Property(PcUiPath.Field(set, "MainHandWeapon").FieldType, "Value").PropertyType;
            c.ItemIcon = TouchRadialPartyContracts.Reactive<object>(item, "Icon");
            c.ItemIdentity = TouchRadialPartyContracts.Reactive<object>(item, "Item");
            c.ItemName = TouchRadialPartyContracts.Reactive<string>(item, "DisplayName");
            c.Converted = TouchRadialPartyContracts.Reactive<object>(slot, "ConvertedVm");
            c.VariantSlots = PcUiPath.Field(PcUiPath.Property(PcUiPath.Field(slot, "ConvertedVm").FieldType, "Value").PropertyType, "Slots");
            c.Sets = TouchSelectionCallFactory.FieldGetter(PcUiPath.Field(weapons, "Sets"));
            c.CanSwitch = TouchRadialPartyContracts.Reactive<bool>(weapons, "CanSwitchSets");
            var index = PcUiPath.Field(set, "Index");
            c.Index = item => (int)index.GetValue(item);
            c.Switch = (Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>),
                TouchSelectionCallFactory.ExactMethod(set, "SwitchWeapon", typeof(void), false));
            c.CloseConvert = (Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>),
                TouchSelectionCallFactory.ExactMethod(slot, "CloseConvert", typeof(void), false));
            return c;
        }
    }
    public static partial class Main
    {
        static TouchRadialWeaponContracts _radialWeapons;
        static readonly List<TouchRadialEntry> _radialConversions = new List<TouchRadialEntry>(4);
        static int _radialCatalogueFingerprint;
        static Sprite _radialActiveWeaponMain, _radialActiveWeaponOff;
        static string _radialActiveWeaponName;
        static int _radialActiveWeaponSet;
        static void InstallTouchRadialWeapons()
        {
            try { _radialWeapons = _touchRadialContracts == null ? null : TouchRadialWeaponContracts.Create(_touchRadialContracts); }
            catch (Exception error) { _radialWeapons = null; _log.Error("[touch/radial] Native weapon variants: " + error.Message); }
        }
        static bool RadialContains(object list, object value)
        {
            if (ReferenceEquals(list, value)) return true;
            if (list is IEnumerable items) foreach (var item in items) if (ReferenceEquals(item, value)) return true;
            return false;
        }
        static void AddTouchRadialWeaponSets()
        {
            var c = _touchRadialContracts; var w = _radialWeapons;
            if (w == null || _touchRadialBarModel == null) return;
            var weapons = c.BarWeapons.GetValue(_touchRadialBarModel);
            if (weapons == null || !(w.Sets(weapons) is IEnumerable sets)) return;
            // Native item-slot VMs exist even for an EMPTY loadout. Inspect the
            // actual Item, not the wrapper/icon (which may be an empty-slot art).
            int equippedSets = 0;
            foreach (var set in sets) if (TouchRadialWeaponSetEquipped(set)) ++equippedSets;
            if (equippedSets < 2) return;
            var current = TouchRadialPart(2);
            foreach (var set in sets)
            {
                if (!TouchRadialWeaponSetEquipped(set)) continue;
                var main = TouchRadialEquippedWeapon(w.MainHand(set));
                var off = w.TwoHanded(set) ? null : TouchRadialEquippedWeapon(w.OffHand(set));
                Sprite icon = main == null ? null : w.ItemIcon(main) as Sprite;
                if (icon == null && off != null) icon = w.ItemIcon(off) as Sprite;
                _touchRadialEntries.Add(new TouchRadialEntry { View = _touchRadialBar, Model = set, Part = weapons,
                    WeaponSet = true, WeaponSetNumber = w.Index(set) + 1, Selected = ReferenceEquals(current, set),
                    Icon = icon, WeaponName = TouchRadialWeaponNames(main,off), Label = "Switch weapon", OwnLabel = true });
            }
        }
        static object TouchRadialEquippedWeapon(object itemView) =>
            itemView != null && _radialWeapons.ItemIdentity(itemView) != null ? itemView : null;
        static bool TouchRadialWeaponSetEquipped(object set)
        {
            var w = _radialWeapons;
            return w != null && set != null && (TouchRadialEquippedWeapon(w.MainHand(set)) != null ||
                !w.TwoHanded(set) && TouchRadialEquippedWeapon(w.OffHand(set)) != null);
        }
        static string TouchRadialWeaponNames(object main, object off)
        {
            string first = main == null ? null : _radialWeapons.ItemName(main);
            string second = off == null ? null : _radialWeapons.ItemName(off);
            return string.IsNullOrEmpty(first) ? second ?? "" : string.IsNullOrEmpty(second) ? first : first + " / " + second;
        }
        static void RefreshTouchRadialWeaponIdentity()
        {
            _radialActiveWeaponMain = _radialActiveWeaponOff = null; _radialActiveWeaponName = null; _radialActiveWeaponSet = 0;
            if (_touchRadialSpace && _touchRadial.Side == 0 && !TouchRadialInformationOnly) { RefreshTouchSpaceRadialWeaponIdentity(); return; }
            if (_radialWeapons == null || _touchRadial.Side != 0 || TouchRadialModePolicy.IsCharacters(_touchRadialLeftMode) || _touchRadialBarModel == null) return;
            var set = TouchRadialPart(2); if (set == null) return;
            var main = TouchRadialEquippedWeapon(_radialWeapons.MainHand(set)); var off = _radialWeapons.TwoHanded(set) ? null : TouchRadialEquippedWeapon(_radialWeapons.OffHand(set));
            _radialActiveWeaponMain = main == null ? null : _radialWeapons.ItemIcon(main) as Sprite;
            _radialActiveWeaponOff = off == null ? null : _radialWeapons.ItemIcon(off) as Sprite;
            _radialActiveWeaponName = TouchRadialWeaponNames(main, off);
            _radialActiveWeaponSet = _radialWeapons.Index(set) + 1;
        }
        static void RefreshTouchSpaceRadialWeaponIdentity()
        {
            var c = _touchSpaceRadialContracts; if (c == null || _touchRadial.Side != 0) return;
            int preview = _touchRadial.PreviewIndex;
            TouchRadialEntry entry = preview >= 0 && preview < _touchRadialEntries.Count ? _touchRadialEntries[preview] : null;
            while (entry?.VariantParent != null) entry = entry.VariantParent;
            if (entry == null || entry.Mechanic == null || !c.ShipWeaponMechanic.IsInstanceOfType(entry.Mechanic))
            {
                entry = null;
                foreach (var candidate in _touchRadialEntries)
                {
                    var original = candidate; while (original.VariantParent != null) original = original.VariantParent;
                    if (original.Mechanic != null && c.ShipWeaponMechanic.IsInstanceOfType(original.Mechanic)) { entry = original; break; }
                }
            }
            if (entry == null) return;
            var slot = c.ShipWeaponSlot(entry.Mechanic); var weapon = slot == null ? null : c.ActiveWeapon(slot);
            if (weapon == null) return;
            _radialActiveWeaponMain = c.WeaponIcon(weapon) as Sprite;
            _radialActiveWeaponName = c.WeaponName(weapon);
        }
        static bool TouchRadialWeaponSetAvailable(TouchRadialEntry entry)
        {
            var w = _radialWeapons;
            return w != null && _touchRadialBarModel != null &&
                ReferenceEquals(_touchRadialContracts.BarWeapons.GetValue(_touchRadialBarModel), entry.Part) &&
                !ReferenceEquals(TouchRadialPart(2), entry.Model) && w.CanSwitch(entry.Part) &&
                RadialContains(w.Sets(entry.Part), entry.Model) && TouchRadialWeaponSetEquipped(entry.Model) &&
                (!_touchRadialCombat || _touchRadialCombatContracts != null && _touchRadialCombatContracts.Available(_touchRadialBarModel));
        }
        static void SwitchTouchRadialWeaponSet(TouchRadialEntry entry)
        {
            _radialWeapons.Switch(entry.Model);
            if (TouchRadialContextValid()) { RebuildTouchRadialCatalogue(); _touchRadial.ContinueCatalogue(); }
        }
        static bool TouchRadialVariantBound(TouchRadialEntry entry)
        {
            if (entry?.VariantParent == null || _radialWeapons == null) return false;
            var parent = entry.VariantParent;
            bool ownerBound = parent.VariantParent != null ? TouchRadialVariantBound(parent) : parent.Space != null ?
                TouchSpaceRadialSlotBound(parent) : ReferenceEquals(TouchRadialPart(parent.PartKind), parent.Part) &&
                RadialContains(parent.PartSlots.GetValue(parent.Part), parent.Model) &&
                ReferenceEquals(_touchRadialContracts.MechanicSlot.GetValue(parent.Model,null),parent.Mechanic);
            return ownerBound && ReferenceEquals(_radialWeapons.Converted(parent.Model), entry.Part) &&
                RadialContains(entry.PartSlots.GetValue(entry.Part), entry.Model) &&
                ReferenceEquals(_touchRadialContracts.MechanicSlot.GetValue(entry.Model, null), entry.Mechanic);
        }
        static bool TouchRadialVariantAvailable(TouchRadialEntry entry)
        {
            if (!TouchRadialVariantBound(entry)) return false;
            var c = _touchRadialContracts;
            if (_touchRadialSpace && !ReadTouchSpaceRadialContext().CanAct ||
                !_touchRadialSpace && _touchRadialCombat && !_touchRadialCombatContracts.Available(_touchRadialBarModel)) return false;
            return !(bool)c.EmptyValue.GetValue(c.SlotEmpty.GetValue(entry.Model), null) &&
                !(bool)c.CharScreen.GetValue(entry.Model) && ((bool)c.EmptyValue.GetValue(c.SlotPossible.GetValue(entry.Model), null) ||
                (bool)c.EmptyValue.GetValue(c.SlotConverts.GetValue(entry.Model), null));
        }
        static bool OpenTouchRadialVariants(TouchRadialEntry entry)
        {
            if (_radialWeapons == null || !entry.Ability || _radialConversions.Count >= 8) return false;
            var converted = _radialWeapons.Converted(entry.Model);
            if (converted == null || !(_radialWeapons.VariantSlots.GetValue(converted) is IEnumerable slots)) return false;
            _radialConversions.Add(entry); _touchRadialEntries.Clear();
            foreach (var slot in slots)
            {
                int before = _touchRadialEntries.Count;
                AddTouchRadialSlot(slot, converted, _radialWeapons.VariantSlots, 3);
                if (_touchRadialEntries.Count > before)
                {
                    var variant = _touchRadialEntries[_touchRadialEntries.Count - 1];
                    variant.View = entry.View; variant.VariantParent = entry;
                }
            }
            if (_touchRadialEntries.Count == 0) { ClearTouchRadialVariants(); return false; }
            _touchRadialMessage = "WEAPON / ABILITY VARIANTS";
            _touchRadial.ContinueCatalogue();
            foreach (var variant in _touchRadialEntries) variant.Enabled = TouchRadialEntryAvailable(variant);
            _radialCatalogueFingerprint = TouchRadialCatalogueFingerprint();
            BuildTouchRadialRings();
            RebuildTouchRadialVisual(); return true;
        }
        static void ClearTouchRadialVariants()
        {
            for (int i = _radialConversions.Count - 1; i >= 0; --i)
            {
                try { var entry = _radialConversions[i]; if (_radialWeapons?.Converted(entry.Model) != null) _radialWeapons.CloseConvert(entry.Model); }
                catch (Exception error) { _log.Error("[touch/radial] Closing native variants: " + error.Message); }
            }
            _radialConversions.Clear();
        }
        static int RadialHash(int hash, object value) => unchecked(hash * 31 + (value == null ? 0 : RuntimeHelpers.GetHashCode(value)));
        static int RadialSlotsHash(int hash, object list)
        {
            if (list is IEnumerable items) foreach (var item in items)
            {
                hash = RadialHash(hash, item);
                if (item != null)
                {
                    var c = _touchRadialContracts;
                    hash = RadialHash(hash, c.MechanicSlot.GetValue(item, null));
                    hash = RadialHash(hash, c.IconValue.GetValue(c.SlotIcon.GetValue(item), null));
                    hash = unchecked(hash * 31 + ((bool)c.EmptyValue.GetValue(c.SlotEmpty.GetValue(item),null) ? 1 : 0));
                }
            }
            else
            {
                hash = RadialHash(hash, list);
                if(list!=null&&_touchRadialContracts.MechanicSlot.DeclaringType.IsInstanceOfType(list))
                    hash=RadialHash(hash,_touchRadialContracts.MechanicSlot.GetValue(list,null));
            }
            return hash;
        }
        static int TouchRadialCatalogueFingerprint()
        {
            if (_touchRadialContracts == null) return 0;
            if (_radialConversions.Count > 0)
            {
                var parent=_radialConversions[_radialConversions.Count-1];
                var converted=_radialWeapons.Converted(parent.Model);
                return converted==null?0:RadialSlotsHash(RadialHash(17,converted),_radialWeapons.VariantSlots.GetValue(converted));
            }
            int hash = 17;
            if (_touchRadialSpace && _touchRadial.Side == 0 && !TouchRadialInformationOnly)
            {
                var c = _touchSpaceRadialContracts; var s = _touchSpaceRadialContext;
                if (c == null || !s.Ready) return 0;
                if (_touchRadial.Side == 0 && s.Weapons != null)
                {
                    if (c.WeaponGroups(s.Weapons) is IDictionary groups) foreach (DictionaryEntry pair in groups)
                        if (pair.Value != null) hash = RadialSlotsHash(hash, c.GroupSlots(pair.Value));
                    var own = c.OwnGroup(s.Weapons); if (own != null) hash = RadialSlotsHash(hash, c.GroupSlots(own));
                }
                if (s.Posts != null && c.PostList(s.Posts) is IEnumerable posts)
                    foreach (var post in posts) { var group = post == null ? null : c.PostGroup(post); if (group != null) hash = RadialSlotsHash(hash, c.GroupSlots(group)); }
                return hash;
            }
            if (_touchRadialBarModel == null) return 0;
            var bar = _touchRadialContracts;
            for (int kind = 0; kind <= 2; ++kind)
            {
                var part = TouchRadialPart(kind); hash = RadialHash(hash, part); if (part == null) continue;
                if (kind == 2) foreach (var field in bar.WeaponSlots) hash = RadialSlotsHash(hash, field.GetValue(part));
                else hash = RadialSlotsHash(hash, (kind == 0 ? bar.AbilitySlots : bar.ConsumableSlots).GetValue(part));
                if(kind==0)hash=RadialSlotsHash(hash,bar.OverdriveSlot.GetValue(part));
            }
            var weapons = bar.BarWeapons.GetValue(_touchRadialBarModel);
            if (_radialWeapons != null && weapons != null && _radialWeapons.Sets(weapons) is IEnumerable sets)
                foreach (var set in sets)
                {
                    hash = RadialHash(hash, set);
                    if (set == null) continue;
                    var main = _radialWeapons.MainHand(set); var off = _radialWeapons.OffHand(set);
                    hash = RadialHash(hash, main == null ? null : _radialWeapons.ItemIdentity(main));
                    hash = RadialHash(hash, off == null ? null : _radialWeapons.ItemIdentity(off));
                }
            return hash;
        }
        static void RefreshTouchRadialCatalogue()
        {
            RefreshTouchRadialWeaponIdentity();
            if (_radialConversions.Count > 0)
            {
                if (_touchRadialEntries.Count == 0 || !TouchRadialVariantBound(_touchRadialEntries[0]))
                { RebuildTouchRadialCatalogue(); _touchRadial.ContinueCatalogue(); }
                else if(TouchRadialCatalogueFingerprint()!=_radialCatalogueFingerprint)
                {
                    var parent=_radialConversions[_radialConversions.Count-1];
                    _radialConversions.RemoveAt(_radialConversions.Count-1);
                    ClearTouchRadialAbilityInformation();
                    if(!OpenTouchRadialVariants(parent)){RebuildTouchRadialCatalogue();_touchRadial.ContinueCatalogue();}
                }
                return;
            }
            int next = TouchRadialCatalogueFingerprint();
            if (next == _radialCatalogueFingerprint) return;
            RebuildTouchRadialCatalogue(); _touchRadial.ContinueCatalogue();
        }
    }
}
