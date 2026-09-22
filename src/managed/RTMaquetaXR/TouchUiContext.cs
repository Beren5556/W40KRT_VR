using UnityEngine;
using UnityEngine.EventSystems;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly TouchUiContextGuard _touchUiContext = new TouchUiContextGuard();

        static void ObserveTouchUiContext()
        {
            // Walk the already resolved, bounded native paths. No hierarchy
            // scan, synthetic Submit, direct onClick or second activation path.
            FindTouchMenuWindow(out var window, out var view, out var model);
            var message = _touchMenuWindows?.MessageBox?.Read(_pcHudCommon) as Component;
            object modal = message != null && message.gameObject.activeInHierarchy ? _touchMenuWindows.MessageModel(message) : null;
            var current = new TouchUiContextIdentity {
                EventSystem = EventSystem.current, Module = _touchModule, Root = _uiRoot,
                Window = view, Model = model, Modal = modal,
                TutorialWindow = _tutorialWindowIdentity,
                Flat = _modeFlat || !_attached, Tutorial = _tutorialShowing,
                Width = Screen.width, Height = Screen.height, Area = SpatialGameContextRevision
            };
            if (!_touchUiContext.Observe(current)) return;
            CancelTouchPointerPress();
            _touchTarget = null; _touchOverUi = _touchHasPoint = false;
            var es = EventSystem.current;
            var selected = es == null ? null : es.currentSelectedGameObject;
            Transform owner = modal != null ? message.transform : view == null ? null : view.transform;
            if (selected != null && (!selected.activeInHierarchy ||
                (owner != null && selected.transform != owner && !selected.transform.IsChildOf(owner))))
                es.SetSelectedGameObject(null);
        }
    }
}
