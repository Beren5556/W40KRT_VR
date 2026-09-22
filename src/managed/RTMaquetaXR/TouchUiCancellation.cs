using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RTMaquetaXR
{
    // A cancelled pointer is not an ordinary mouse release. Some Owlcat
    // PointerUp/EndDrag handlers execute clicks or inventory transfers directly,
    // ignoring eligibleForClick. Only audited state-cleanup handlers run here.
    internal static class TouchUiCancellation
    {
        const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Dictionary<Type, Plan> plans = new Dictionary<Type, Plan>();
        static readonly List<Component> components = new List<Component>(16);
        static readonly object[] argument = new object[1];
        static readonly HashSet<string> reportedErrors = new HashSet<string>();
        static MethodInfo dragManagerInstance, dragManagerCancel;
        static Action<string> reportError;
        static bool cancelling;
        static readonly List<PointerEventData> rewiredPointers = new List<PointerEventData>(6);

        sealed class Plan
        {
            internal MethodInfo Up, End, Reset;
            internal FieldInfo LinkDown, LinkHover;
            internal bool GameDrag;
        }

        internal static void Configure(Type manager, Action<string> error)
        {
            reportError = error;
            dragManagerInstance = manager?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetGetMethod(true);
            dragManagerCancel = manager?.GetMethod("CancelDrag", InstanceFlags, null, Type.EmptyTypes, null);
            if (dragManagerInstance == null || dragManagerCancel == null)
                throw new MissingMethodException("DragNDropManager.Instance / CancelDrag");
        }

        internal static void Cancel(PointerEventData pointer)
        {
            if (pointer == null || cancelling) return;
            var press = pointer.pointerPress;
            var drag = pointer.pointerDrag;
            bool wasDragging = pointer.dragging;
            Clear(pointer);
            if (press == null && drag == null) return;
            cancelling = true;
            argument[0] = pointer;
            try
            {
                Cleanup(press, false, wasDragging);
                Cleanup(drag, true, wasDragging);
            }
            finally
            {
                // Callbacks cannot rearm this event, even if another mod mutates it.
                Clear(pointer);
                components.Clear(); argument[0] = null; cancelling = false;
            }
        }

        internal static void CancelRewiredMousePointers(object playerData)
        {
            if (cancelling || !(playerData is System.Collections.IDictionary players)) return;
            rewiredPointers.Clear();
            // Actual Rewired shape: player id -> mouse-index array -> pointer id
            // dictionary. Snapshot before callbacks: cancellation can close UI.
            foreach (System.Collections.DictionaryEntry player in players)
                if (player.Value is Array mice)
                    foreach (object mouse in mice)
                        if (mouse is System.Collections.IDictionary pointers)
                            for (int id = -1; id >= -3; --id)
                                if (pointers.Contains(id) && pointers[id] is PointerEventData pointer && !rewiredPointers.Contains(pointer))
                                    rewiredPointers.Add(pointer);
            try { for (int i = 0; i < rewiredPointers.Count; ++i) Cancel(rewiredPointers[i]); }
            finally { rewiredPointers.Clear(); }
        }

        static void Clear(PointerEventData pointer)
        {
            pointer.eligibleForClick = false; pointer.dragging = false;
            pointer.pointerPress = null; pointer.rawPointerPress = null;
            pointer.pointerClick = null; pointer.pointerDrag = null; pointer.clickCount = 0;
        }

        static void Cleanup(GameObject owner, bool end, bool wasDragging)
        {
            if (owner == null) return;
            components.Clear(); owner.GetComponents(components);
            for (int i = 0; i < components.Count; ++i)
            {
                var component = components[i];
                if (component == null) continue;
                try
                {
                    Type type = component.GetType();
                    if (!plans.TryGetValue(type, out var plan)) { plan = BuildPlan(type); plans.Add(type, plan); }
                    if (!end)
                    {
                        if (plan.LinkDown != null) plan.LinkDown.SetValue(component, -1);
                        if (plan.LinkHover != null) plan.LinkHover.SetValue(component, -1);
                        plan.Reset?.Invoke(component, null);
                        plan.Up?.Invoke(component, argument);
                    }
                    else if (wasDragging)
                    {
                        if (plan.GameDrag) CancelGameDrag();
                        else plan.End?.Invoke(component, argument);
                    }
                }
                catch (Exception error)
                {
                    string text = component.GetType().FullName + ": " + (error.InnerException ?? error).Message;
                    if (reportedErrors.Add(text)) reportError?.Invoke("[touch/cancel] " + text);
                }
            }
        }

        static void CancelGameDrag()
        {
            // This is the game's Escape cancellation route. EndDrag can call
            // TryDropItem/MoveCharacter even when no IDrop event was dispatched.
            var manager = dragManagerInstance.Invoke(null, null);
            if (manager is UnityEngine.Object unity && unity == null) return;
            if (manager != null) dragManagerCancel.Invoke(manager, null);
        }

        static Plan BuildPlan(Type type)
        {
            var plan = new Plan();
            MethodInfo up = InterfaceMethod(type, typeof(IPointerUpHandler));
            string upOwner = up?.DeclaringType?.FullName;
            switch (upOwner)
            {
                case "UnityEngine.UI.Selectable":
                case "UnityEngine.UI.Scrollbar":
                case "Kingmaker.UI.Common.DraggbleWindow":
                case "Kingmaker.UI.Common.TextMeshButton":
                    plan.Up = up; break;
                case "Owlcat.Runtime.UI.Controls.Selectable.OwlcatSelectable":
                    // Parameterless overload only clears IsPressed; the event
                    // overload unnecessarily depends on OS-cursor visibility.
                    plan.Reset = up.DeclaringType.GetMethod("OnPointerUp", InstanceFlags, null, Type.EmptyTypes, null); break;
                case "Kingmaker.UI.TMPExtention.TMPLinkHandler":
                    plan.LinkDown = up.DeclaringType.GetField("m_DownIndex", InstanceFlags);
                    plan.LinkHover = up.DeclaringType.GetField("m_HoverdLink", InstanceFlags); break;
                case "Kingmaker.UI.Common.DoubleClickableElement":
                    plan.Reset = up.DeclaringType.GetMethod("Reset", InstanceFlags, null, Type.EmptyTypes, null); break;
            }
            MethodInfo end = InterfaceMethod(type, typeof(IEndDragHandler));
            switch (end?.DeclaringType?.FullName)
            {
                case "UnityEngine.UI.ScrollRect":
                case "UnityEngine.UI.InputField":
                case "TMPro.TMP_InputField":
                case "Owlcat.Runtime.UI.VirtualListSystem.DragTracker":
                case "Kingmaker.UI.Common.ScrollRectExtended":
                case "Kingmaker.UI.DollRoom.DollRoomTargetController":
                case "Kingmaker.Code.UI.MVVM.View.Party.PC.PartyCharacterPCView":
                case "Kingmaker.Code.UI.MVVM.View.Party.Console.PartyCharacterConsoleView":
                    plan.End = end; break;
                case "Kingmaker.UI.DragNDrop.DragHandler":
                case "Kingmaker.UI.DragNDrop.DragNDropHandler":
                case "Kingmaker.UI.DragNDrop.DragNDropManager":
                case "Kingmaker.Code.UI.MVVM.View.Slots.ItemSlotPCView":
                case "Kingmaker.UI.MVVM.View.NetRoles.PC.NetRolesPlayerCharacterPCView":
                    plan.GameDrag = true; break;
            }
            // Unknown EventTrigger/observable callbacks deliberately receive no
            // synthetic release: their listeners may submit arbitrary game work.
            return plan;
        }

        static MethodInfo InterfaceMethod(Type type, Type contract)
        {
            if (!contract.IsAssignableFrom(type)) return null;
            var map = type.GetInterfaceMap(contract);
            return map.TargetMethods.Length == 1 ? map.TargetMethods[0] : null;
        }
    }
}
