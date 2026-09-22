using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly TouchButtonLatch _touchMapStick = new TouchButtonLatch(), _touchHighlightStick = new TouchButtonLatch();
        static object _touchHighlightOwner;
        static bool _touchExplorationMapFrame;
        static long _touchMapOpens, _touchHighlightToggles;

        static void InstallTouchExplorationShortcuts()
        { InstallTouchLootHighlight65();InstallTouchProximityInteractions73(); }
        static void ProcessTouchExplorationShortcuts(bool overlayWasOpen)
        {
            _touchExplorationMapFrame = false;
            bool left = (_touchSample.left.activeControls & (uint)XrTouchControl.StickClick) != 0 &&
                (_touchSample.left.buttons & (uint)XrTouchButton.StickClick) != 0;
            bool right = (_touchSample.right.activeControls & (uint)XrTouchControl.StickClick) != 0 &&
                (_touchSample.right.buttons & (uint)XrTouchButton.StickClick) != 0;
            try
            {
                ProcessTouchProximityInteractions73(overlayWasOpen);
                ProcessTouchHelpShortcut(right, overlayWasOpen);
                // No native scene query or UI search on the ordinary idle path.
                bool allowed = !InSpaceCombat && !InNavigationMap && _touchSampleValid && !overlayWasOpen && TouchGameInputAllowed && !TouchDistanceReserved &&
                    !TouchSelectionCaptured && !_touchBox.Pending && !_touchUiPress.Captured &&
                    !_touchRawRightTrigger && !_touchRawLeftTrigger &&
                    (_touchSample.left.aimFlags & 3) == 3;
                if (allowed && (left || right || _touchHighlightOwner != null))
                    allowed = _presentation != null && TouchRadialSceneAllowed && !TouchRadialCombatNow(out _) &&
                        _touchGamePointer != null && _touchBoxPointerMode(_touchGamePointer) == 0;
                _touchMapStick.Step(left, allowed);

                if (_touchMapStick.Down)
                {
                    RestoreTouchExplorationHighlight();
                    if (OpenTouchExplorationMap())
                    { _touchExplorationMapFrame = true; StopTouchGroupMovement(); ++_touchMapOpens; }
                }

            }
            catch (Exception error)
            {
                RestoreTouchExplorationHighlight(); _touchMapStick.Cancel(); _touchHighlightStick.Cancel();
                _log.Error("[touch/shortcuts] Native action failed: " + error.Message);
            }
        }
        static bool OpenTouchExplorationMap()
        {
            var c = _touchRadialContracts; var es = EventSystem.current;
            if (c == null || es == null) return false;
            int index = Array.IndexOf(TouchRadialContracts.MenuFields, "m_Map");
            if (index < 0) return false;
            // An explicit click is rare. Resolve the live native PC view once,
            // preserving its permission/availability and original click handler.
            foreach (var candidate in UnityEngine.Object.FindObjectsByType(c.MenuView, FindObjectsSortMode.None))
            {
                var view = candidate as Component;
                if (view == null || !view.gameObject.activeInHierarchy || c.MenuModel.GetValue(view, null) == null) continue;
                var button = c.MenuButtons[index].GetValue(view) as Component;
                if (button == null || !button.gameObject.activeInHierarchy || !(bool)c.Interactable.GetValue(button, null)) return false;
                var target = button.gameObject;
                var data = new PointerEventData(es) { button = PointerEventData.InputButton.Left, clickCount = 1, eligibleForClick = true,
                    pointerPress = target, rawPointerPress = target, pointerEnter = target };
                if (target.transform is RectTransform rect)
                    data.position = RectTransformUtility.WorldToScreenPoint(_pickCam, rect.TransformPoint(rect.rect.center));
                data.pressPosition = data.position;
                try
                {
                    ExecuteEvents.Execute(target, data, ExecuteEvents.pointerEnterHandler);
                    ExecuteEvents.Execute(target, data, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.Execute(target, data, ExecuteEvents.pointerUpHandler);
                    if (target == null || !button.gameObject.activeInHierarchy || !(bool)c.Interactable.GetValue(button, null)) return false;
                    bool sent = ExecuteEvents.Execute(target, data, ExecuteEvents.pointerClickHandler);
                    if (sent) NotifyTouchRadialMenuOpened(view, button);
                    return sent;
                }
                finally { if (target != null) ExecuteEvents.Execute(target, data, ExecuteEvents.pointerExitHandler); }
            }
            return false;
        }
        static void RestoreTouchExplorationHighlight()
        {
            _touchHighlightOwner = null;
            ReleaseTouchLootHighlight65();
        }
        static void StopTouchExplorationShortcuts()
        {
            _touchHelpClick.Step(false, false, 0);
            RestoreTouchExplorationHighlight(); _touchMapStick.Cancel(); _touchHighlightStick.Cancel(); _touchExplorationMapFrame = false;
            StopTouchProximityInteractions73();_touchLootContracts65 = null;
        }
    }
}
