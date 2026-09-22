using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly TouchUiPressPolicy _touchUiPress = new TouchUiPressPolicy();
        static GameObject _touchUiPressTarget;
        static int _touchUiPressButton;
        static bool _touchUiPressFlat, _touchUiPressHasTarget;
        static long _touchUiPresses, _touchUiDrags, _touchUiPressCancellations;

        static void ResetTouchUiPress()
        {
            if (_touchUiPress.Captured) ++_touchUiPressCancellations;
            ClearTouchUiPress();
        }
        static void ClearTouchUiPress()
        { _touchUiPress.Clear(); _touchUiPressTarget = null; _touchUiPressHasTarget = false; }

        // Called before the UI raycast, not after it: the game's event system,
        // its click-on-up controls and our visible reticle use one coordinate.
        static bool ApplyTouchUiPress(bool flat)
        {
            if (!_touchUiPress.Captured) return true;
            var button = _touchUiPressButton == 0 ? _touchPrimary : _touchSecondary;
            if (!button.Held && !button.Up) { ClearTouchUiPress(); return true; }
            if (_touchUiPressFlat != flat ||
                (_touchUiPressHasTarget && (_touchUiPressTarget == null || !_touchUiPressTarget.activeInHierarchy)))
            { CancelTouchPointerPress(); return false; }
            bool wasDragging = _touchUiPress.Dragging;
            if (!_touchUiPress.Apply(_touchScreen.x, _touchScreen.y, Screen.width, Screen.height, out float x, out float y))
            { CancelTouchPointerPress(); return false; }
            _touchScreen = new Vector2(x, y);
            if (!wasDragging && _touchUiPress.Dragging) ++_touchUiDrags;
            return true;
        }

        static void BeginTouchUiPress(bool flat)
        {
            if (_touchUiPress.Captured || !_touchOverUi || !_touchHasPoint ||
                (!_touchPrimary.Down && !_touchSecondary.Down)) return;
            if (!_touchUiPress.Begin(_touchScreen.x, _touchScreen.y, Screen.width, Screen.height)) return;
            _touchUiPressButton = _touchPrimary.Down ? 0 : 1;
            _touchUiPressFlat = flat;
            // Both projections remember the actual native graphic. Closing
            // a flat window while squeezing must cancel just like stereo UI.
            // The native module still owns eligibility and event dispatch.
            _touchUiPressTarget = _touchTarget;
            _touchUiPressHasTarget = _touchTarget != null;
            ++_touchUiPresses;
        }
    }
}
