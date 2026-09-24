using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _hudViewportHooks;
        static bool _hudViewportAttaching;
        static readonly List<HudViewportMask> _hudViewportMasks = new List<HudViewportMask>();

        static void EnsureHudViewportMask(Canvas canvas)
        {
            if (canvas == null || !IsPcViewportRoot(canvas.name) || canvas.transform.parent != _uiRoot) return;
            // Never replace an existing game clipper or its padding/softness.
            if (canvas.GetComponent<RectMask2D>() != null) return;
            if (!_hudViewportHooks)
            {
                var add = AccessTools.Method(typeof(RectMask2D), "AddClippable", new[] { typeof(IClippable) });
                var remove = AccessTools.Method(typeof(RectMask2D), "RemoveClippable", new[] { typeof(IClippable) });
                var parent = AccessTools.Method(typeof(MaskUtilities), "GetRectMaskForClippable", new[] { typeof(IClippable) });
                var chain = AccessTools.Method(typeof(MaskUtilities), "GetRectMasksForClip", new[] { typeof(RectMask2D), typeof(List<RectMask2D>) });
                if (add == null || remove == null || parent == null || chain == null)
                    throw new MissingMethodException("HUD viewport clipping contract unavailable");
                _harmony.Patch(add, postfix: new HarmonyMethod(typeof(Main), nameof(HudViewportTargetAdded)));
                _harmony.Patch(remove, postfix: new HarmonyMethod(typeof(Main), nameof(HudViewportTargetRemoved)));
                _harmony.Patch(parent, postfix: new HarmonyMethod(typeof(Main), nameof(HudViewportClipParent)));
                _harmony.Patch(chain, postfix: new HarmonyMethod(typeof(Main), nameof(HudViewportClipChain)));
                _hudViewportHooks = true;
            }
            _hudViewportAttaching = true;
            try { _hudViewportMasks.Add(canvas.gameObject.AddComponent<HudViewportMask>()); }
            finally { _hudViewportAttaching = false; }
        }

        static void HudViewportTargetAdded(RectMask2D __instance, IClippable __0)
        { if (__instance is HudViewportMask viewport) viewport.Registered(__0); }
        static void HudViewportTargetRemoved(RectMask2D __instance, IClippable __0)
        { if (__instance is HudViewportMask viewport) viewport.Unregistered(__0); }

        static HudViewportMask HudViewportFor(Transform item)
        {
            // These native panels now have independent VR placement. The old
            // desktop edge must not slice them; their own scroll/text masks stay.
            if (IndependentGroupOwns81(item)) return null;
            // World indicators are physically outside the panel by design.
            // Exclude their branch before _overtipsRoot has even been resolved,
            // and keep map marker registration independent of panel layout.
            for (var parent = item; parent != null && parent != _uiRoot; parent = parent.parent)
            {
                if (parent.name == "OvertipsPCView" || IsMapContainer(parent.name)) return null;
                var viewport = parent.GetComponent<HudViewportMask>();
                if (viewport != null && viewport.isActiveAndEnabled) return viewport;
            }
            return null;
        }

        static void HudViewportClipParent(IClippable __0, ref RectMask2D __result)
        {
            if (_hudViewportMasks.Count == 0 && !_hudViewportAttaching) return;
            if (__0 == null || (__result != null && !(__result is HudViewportMask))) return;
            // Native overrideSorting can stop a mask search. It must not remove
            // the outer screen boundary that the desktop camera always imposed.
            var viewport = HudViewportFor(__0.gameObject.transform);
            if (__result is HudViewportMask) { if (viewport == null) __result = null; }
            else if (viewport != null && viewport.gameObject != __0.gameObject) __result = viewport;
        }

        static void HudViewportClipChain(RectMask2D __0, List<RectMask2D> __1)
        {
            if (_hudViewportMasks.Count == 0 && !_hudViewportAttaching) return;
            if (__0 == null || __1 == null) return;
            var viewport = HudViewportFor(__0.transform);
            // Intersect the existing scroll mask with our screen boundary; do
            // not remove, re-enable or replace any native game mask.
            for (int i = __1.Count - 1; i >= 0; --i)
                if (__1[i] is HudViewportMask && __1[i] != viewport) __1.RemoveAt(i);
            if (viewport != null && !__1.Contains(viewport)) __1.Add(viewport);
        }

        static void RestoreHudViewportMasks()
        {
            foreach (var mask in _hudViewportMasks)
                if (mask != null) { mask.enabled = false; UnityEngine.Object.Destroy(mask); }
            _hudViewportMasks.Clear();
        }
    }
}
