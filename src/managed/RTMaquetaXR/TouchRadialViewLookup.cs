using System;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        // Opening a wheel used to search every loaded Unity object for each
        // view type. Use the exact live native HUD fields first, then a bounded
        // UI subtree. Do not retain model snapshots between actors or scenes.
        static Component[] FindTouchRadialViews(Type type)
        {
            if (_pcHud != null)
            {
                RefreshPcUiRoots();
                var paths = _pcHud.SurfaceType.IsInstanceOfType(_pcHudView) ? _pcHud.SurfacePanels :
                    _pcHud.SpaceType.IsInstanceOfType(_pcHudView) ? _pcHud.SpacePanels : null;
                if (paths != null) foreach (var path in paths)
                {
                    if (!type.IsAssignableFrom(path.ValueType)) continue;
                    var view = path.Read(_pcHudView) as Component;
                    if (view != null && view.gameObject.activeInHierarchy) return new[] { view };
                }
                var root = _pcHudView as Component;
                if (root != null)
                {
                    var scoped = root.GetComponentsInChildren(type, false);
                    if (scoped.Length != 0) return scoped;
                }
            }
            if (_uiRoot != null)
            {
                var scoped = _uiRoot.GetComponentsInChildren(type, false);
                if (scoped.Length != 0) return scoped;
            }
            // First boot / unsupported native layout: retain compatibility.
            var loaded = UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.None);
            var result = new Component[loaded.Length];
            for(int i=0;i<result.Length;i++) result[i]=loaded[i] as Component;
            return result;
        }
    }
}
