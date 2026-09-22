using System;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static TouchMenuWindowContracts _touchMenuWindows;
        static TouchMenuWindowContract _touchMenuWindow;
        static Component _touchMenuWindowView;
        static object _touchMenuWindowModel;
        static bool _touchMenuWindowFault;
        static float _touchMenuReadWarningAt;

        // Temporary presentation only. The native viewport, panel and picking
        // projection consume the same values; saved user HUD settings survive.
        internal static bool TouchMenuWindowVisible => _touchMenuWindowView != null && _touchMenuWindowModel != null;
        // Query native views before choosing the render surface; do not wait for
        // the 200 ms chrome refresh, which exposed a scene frame on opening.
        internal static bool ManagementWindowOpen66()
        {
            if(_touchMenuWindows==null){SetManagementBackdrop66(null);return false;}
            RefreshPcUiRoots();
            bool open=FindTouchMenuWindow(out var window,out var view,out var model);
            SetManagementBackdrop66(_active && open ? view : null);
            return open;
        }
        internal static bool ManagementPresentation => InNavigationMap || TouchMenuWindowVisible || NativeUiOnlyPresentation();
        internal static float MenuWindowWidth => Mathf.Clamp(_cfg.uiMenuWidth, .45f, 1f);
        internal static void SetMenuWindowWidth(float value) { _cfg.uiMenuWidth = Mathf.Clamp(value, .45f, 1f); MarkSettingsDirty(); }
        internal static float HudPresentationWidth => InNavigationMap ? NavigationPanel.Width : ManagementPresentation ? MenuWindowWidth : _cfg.uiWidth;
        internal static float HudPresentationDistance => InNavigationMap ? NavigationPanel.Distance : ManagementPresentation ? _cfg.uiMenuDistance : _cfg.uiDistance;
        internal static float HudPresentationOffsetX => InNavigationMap ? NavigationPanel.OffsetX : ManagementPresentation ? _cfg.uiMenuOffsetX : _cfg.uiOffsetX;
        internal static float HudPresentationOffsetY => InNavigationMap ? NavigationPanel.OffsetY : ManagementPresentation ? _cfg.uiMenuOffsetY : _cfg.uiOffsetY;
        internal static float ManagementPresentationWidth => InNavigationMap ? NavigationPanel.Width : MenuWindowWidth;
        internal static float ManagementPresentationAspect => InNavigationMap ? NavigationPanel.Aspect : MenuWindowAspect;
        internal static float MenuWindowAspect => _cfg.uiMenuAspect;
        internal static void SetMenuWindowDistance(float value) { _cfg.uiMenuDistance = Mathf.Clamp(value, .5f, 3f); MarkSettingsDirty(); }
        internal static void SetMenuWindowOffsetX(float value) { _cfg.uiMenuOffsetX = Mathf.Clamp(value, -.65f, .65f); MarkSettingsDirty(); }
        internal static void SetMenuWindowOffsetY(float value) { _cfg.uiMenuOffsetY = Mathf.Clamp(value, -.65f, .65f); MarkSettingsDirty(); }
        internal static void CycleMenuWindowAspect(int direction)
        {
            float next = _cfg.uiMenuAspect <= 0 ? (direction < 0 ? 2.4f : .8f) : Mathf.Round((_cfg.uiMenuAspect + direction * .1f) * 10) / 10;
            _cfg.uiMenuAspect = next < .79f || next > 2.41f ? 0 : next; MarkSettingsDirty();
        }
        internal static float HudPresentationElementScale => InNavigationMap ? 1 : ManagementPresentation ? MenuWindowScale : _cfg.uiElementScale;
        internal static float MenuWindowScale => Mathf.Clamp(_cfg.uiMenuScale, .65f, 1.5f);
        internal static void SetMenuWindowScale(float value) { _cfg.uiMenuScale = Mathf.Clamp(value, .65f, 1.5f); MarkSettingsDirty(); }

        internal static void NotifyTouchMenuOpened()
        {
            _pcHudNext = 0;
        }
        internal static void NotifyTouchRadialMenuOpened(Component view, Component button) { NotifyTouchMenuOpened(); }

        static void InstallTouchMenuWindows()
        {
            if (_touchMenuWindows != null || _touchMenuWindowFault) return;
            try { _touchMenuWindows = TouchMenuWindowContracts.Create(_pcHud); }
            catch (Exception error) { _touchMenuWindowFault = true; _log.Error("[ui/menu-window] Keeping native windows unchanged: " + error); }
        }

        static bool FindTouchMenuWindow(out TouchMenuWindowContract found, out Component view, out object model)
        {
            found = null; view = null; model = null;
            if (_touchMenuWindows == null) return false;
            foreach (var candidate in _touchMenuWindows.Windows)
            {
                try
                {
                var root = candidate.Common ? _pcHudCommon : _pcHudView;
                var current = candidate.View.Read(root) as Component;
                if (current != null && (current == TouchRadialInformationWindow || current == TouchLocalMapCompactService || IsSpatialNode(current.transform))) continue;
                // These are exact native PC-window fields, not a scene search. Their
                // native root owns discovery while our transient VR canvas can be
                // null/recreated during a management or map transition. IsChildOf
                // with that null canvas used to throw on EVERY input sample.
                if (current == null || !current.gameObject.activeInHierarchy) continue;
                var bound = candidate.ReadModel(root);
                if (bound == null || !candidate.CanClose(bound, bound)) continue;
                found = candidate; view = current; model = bound; return true;
                }
                catch (Exception error)
                {
                    // A native pooled/disposed view must not abort EnsureTouchSample
                    // and trap every other menu. Retry next sample; rate-limit the
                    // diagnosis without permanently disabling the window contract.
                    if (Time.unscaledTime >= _touchMenuReadWarningAt)
                    {
                        _touchMenuReadWarningAt = Time.unscaledTime + 5;
                        _log.Error("[ui/menu-window] Native window temporarily unavailable (" + candidate.Name + "): " + error.Message);
                    }
                }
            }
            return false;
        }

        static void UpdateTouchMenuWindow()
        {
            TouchMenuWindowContract found; Component view; object model;
            bool open = FindTouchMenuWindow(out found, out view, out model);
            // Explicit menu notification accelerates discovery, but native
            // mouse/keyboard openings receive the same usable window layout.
            if (!open) { if (TouchMenuWindowVisible) { StopTouchMenuWindow(); RequestTouchCameraGroupReturn(); } return; }
            if (ReferenceEquals(found, _touchMenuWindow) && view == _touchMenuWindowView && ReferenceEquals(model, _touchMenuWindowModel))
            { return; }
            StopTouchMenuWindow();
            _touchMenuWindow = found; _touchMenuWindowView = view; _touchMenuWindowModel = model;
            _log.Log("[ui/menu-window] Native " + found.Name + " enlarged to binocular fit; B invokes its native close action.");
        }

        static bool TouchMenuBlockingModal()
        {
            if (NativeTutorialInputBlocked) return true;
            var message = _touchMenuWindows?.MessageBox?.Read(_pcHudCommon) as Component;
            return message != null && message.gameObject.activeInHierarchy && _touchMenuWindows.MessageModel(message) != null;
        }
        internal static void CloseTouchMenuWindow() => TryCloseTouchMenuWindow();

        internal static bool TryCloseTouchMenuWindow()
        {
            try
            {
                // Native Close remains usable while the stereo canvas is rebuilding
                // or the game temporarily presents this window on a flat layer.
                if (_pcHud == null) return false;
                RefreshPcUiRoots();
                if (TouchMenuBlockingModal()) return false;
                TouchMenuWindowContract current; Component view; object model;
                if (!FindTouchMenuWindow(out current, out view, out model) || !current.CanClose(model, model)) return false;
                // Game retains unsaved-settings confirmation, asynchronous
                // opening guard, custom completion actions and VM disposal.
                current.Close(model); _pcHudNext = 0;
                return true;
            }
            catch (Exception error) { _log.Error("[ui/menu-window] Native close failed: " + error); return false; }
        }

        static void StopTouchMenuWindow()
        {
            _touchMenuWindow = null; _touchMenuWindowView = null; _touchMenuWindowModel = null;
        }
    }
}
