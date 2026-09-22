using System;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _liveInputReady;
        static readonly LiveOverlayKeyboardPolicy _liveKeyboard = new LiveOverlayKeyboardPolicy();
        static int _liveKeyboardFrame = -1;

        // Touch is shared with the game adapter; it also owns gesture cancellation.
        // Flat menus retain the same navigation, even before a world camera exists.
        static bool LiveOverlayVr => _liveStarted && _liveInputReady && TouchInputOwned && Application.isFocused;

        static bool InstallLiveOverlayInput()
        {
            _liveInputReady = _touchHooksReady;
            if (!_liveInputReady)
                _log.Error("[overlay] Touch input contract unavailable; panel disabled");
            else
                _log.Log("[overlay] Touch: both triggers + both grips held 2s opens/closes; release all four to rearm; right stick navigates/adjusts; A/right trigger enters/executes; B returns; available in stereo and flat menus");
            return _liveInputReady;
        }

        static void TickLiveOverlayInput()
        {
            ProcessLiveOverlayKeyboard();
            EnsureTouchSample();
            if (!LiveOverlayVr && _liveNavigation.Visible) HideLiveOverlay();
        }

        // Called before both UMM update and the first game/UI input query. The
        // game binding routes already consume keyboard keys in Touch mode;
        // our own original Input read gives F1 priority regardless of keybinds.
        static void ProcessLiveOverlayKeyboard()
        {
            if (_liveKeyboardFrame == Time.frameCount) return;
            _liveKeyboardFrame = Time.frameCount;
            if (!_liveKeyboard.Step(Input.GetKey(KeyCode.F1), LiveOverlayVr)) return;
            CancelTouchGameGestures();
            _touchPrimary.Cancel(); _touchSecondary.Cancel(); _touchOverlayTrigger.Cancel();
            _touchA.Cancel(); _touchB.Cancel(); _touchGameCancel.Cancel(); _touchGamePause.Cancel();
            _touchGameAxesArmed = false; _touchClickFrozen = false;
            _touchNavVertical.Clear(); _touchNavHorizontal.Clear(); _touchTabletop.Cancel(true);
            ResetOverlayTouchPointer();
            if (TouchOverlayOpen) HideLiveOverlay(); else _liveNavigation.Cycle();
            _liveMessage = null; _liveNextTextUpdate = 0;
            LogLiveOverlayInput("F1: toggle VR settings");
        }

        // One bounded line per actual navigation/change, never per render frame.
        // These observations do not establish that either eye drew the panel.
        static void LogLiveOverlayInput(string action)
        {
            try
            {
                var option = CurrentLiveOption();
                string value = option?.Value == null ? "" : option.Value();
                if (value != null && value.Length > 240) value = value.Substring(0, 240);
                _log.Log("[overlay/input] frame=" + Time.frameCount + " " + action +
                    "; started=" + _liveStarted + "; touch=" + _liveInputReady + "; active=" + _active +
                    "; attached=" + _attached + "; flat=" + _modeFlat + "; focus=" + Application.isFocused +
                    "; page=" + _livePage + "/" + (_liveOptions == null ? 0 : _liveOptions.Count) +
                    "; menu=" + (_liveNavigation.Menu?.Title ?? "hidden") + "; depth=" + _liveNavigation.Depth +
                    "; canvasEnabled=" + (_liveCanvas != null && _liveCanvas.enabled) +
                    "; option=" + (option?.Label ?? "hidden") + "; value=" + value);
            }
            catch { } // Diagnostics cannot prevent opening or adjusting the panel.
        }

        static void RemoveLiveOverlayInput() { _liveInputReady = false; _liveKeyboard.Reset(); _liveKeyboardFrame = -1; }
    }
}
