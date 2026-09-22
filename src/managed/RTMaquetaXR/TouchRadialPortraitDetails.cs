using System;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    internal sealed class TouchRadialPortraitContracts
    {
        internal Type BaseUnit;
        internal Func<object, object> View;
        internal Action<object, bool> Hover;
        internal static TouchRadialPortraitContracts Create(Func<string, Type> resolve)
        {
            var c = new TouchRadialPortraitContracts();
            c.BaseUnit = resolve("Kingmaker.EntitySystem.Entities.BaseUnitEntity") ?? throw new TypeLoadException("BaseUnitEntity");
            var view = TouchRadialContracts.Property(c.BaseUnit, "View");
            c.View = (Func<object, object>)TouchSelectionCallFactory.Build(typeof(Func<object, object>), view.GetGetMethod(true));
            c.Hover = (Action<object, bool>)TouchSelectionCallFactory.Build(typeof(Action<object, bool>),
                TouchSelectionCallFactory.ExactMethod(view.PropertyType, "HandleHoverChange", typeof(void), false, typeof(bool)));
            return c;
        }
    }
    internal static class TouchRadialPortraitText
    {
        // Native HpText may carry TextMeshPro colour markup. The wheel Text
        // does not enable rich text, so preserve content while dropping tags.
        internal static string PlainHealth(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            bool needsCleaning = text.IndexOf('<') >= 0 || text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0;
            if (!needsCleaning) return text;
            var result = new StringBuilder(text.Length); bool tag = false;
            foreach (char c in text)
            {
                if (c == '<') { tag = true; continue; }
                if (c == '>' && tag) { tag = false; continue; }
                if (!tag) result.Append(c == '\r' || c == '\n' ? ' ' : c);
            }
            return result.ToString().Trim();
        }
    }
    public static partial class Main
    {
        static TouchRadialPortraitContracts _touchRadialPortraitContracts;
        static TouchRadialEntry _touchRadialPortraitHovered;
        static Component _touchRadialPortraitHoveredView;
        static string _touchRadialPortraitFault;
        static void InstallTouchRadialPortraitDetails()
        {
            try { _touchRadialPortraitContracts = TouchRadialPortraitContracts.Create(AccessTools.TypeByName); _touchRadialPortraitFault = null; }
            catch (Exception error) { _touchRadialPortraitContracts = null; _touchRadialPortraitFault = error.Message; _log.Error("[touch/radial] Native portrait highlight unavailable: " + error.Message); }
        }
        static void SetTouchRadialPortraitText(TouchRadialEntry entry, string status, string nativeHp)
        {
            if (entry.PortraitStatus == status && entry.NativeHp == nativeHp && entry.StateLabel != null) return;
            entry.PortraitStatus = status; entry.NativeHp = nativeHp;
            string hp = TouchRadialPortraitText.PlainHealth(nativeHp);
            entry.StateLabel = string.IsNullOrEmpty(hp) ? status : status + "\nHP " + hp;
        }
        static void UpdateTouchRadialPortraitHover(int index)
        {
            var entry = index >= 0 && index < _touchRadialEntries.Count ? _touchRadialEntries[index] : null;
            // Battle portraits are read-only information: do not change native
            // hover/cursor state. UnitEntityView hover is distinct from the
            // PartyCharacterHover event; no ability-target fix is implied here.
            // Exploration Party keeps its established highlight behavior.
            if (entry != null && (!entry.Character || !entry.Party)) entry = null;
            if (ReferenceEquals(entry, _touchRadialPortraitHovered)) return;
            ClearTouchRadialPortraitHover();
            var c = _touchRadialPortraitContracts;
            if (entry == null || c == null || entry.Mechanic == null || !c.BaseUnit.IsInstanceOfType(entry.Mechanic)) return;
            var view = c.View(entry.Mechanic) as Component;
            if (view == null || !view.gameObject.activeInHierarchy) return;
            // The initiative panel's pointer-enter/exit invokes exactly this
            // native view method. Party hover highlights normal overtips;
            // it does not commit selection, inspection or a button click.
            _touchRadialPortraitHovered = entry; _touchRadialPortraitHoveredView = view;
            c.Hover(view, true);
        }
        static void ClearTouchRadialPortraitHover()
        {
            var view = _touchRadialPortraitHoveredView;
            _touchRadialPortraitHovered = null; _touchRadialPortraitHoveredView = null;
            if (view == null || _touchRadialPortraitContracts == null) return;
            try { _touchRadialPortraitContracts.Hover(view, false); }
            catch (Exception error)
            {
                if (_touchRadialPortraitFault == error.Message) return;
                _touchRadialPortraitFault = error.Message; _log.Error("[touch/radial] Native hover cleanup: " + error.Message);
            }
        }
    }
}
