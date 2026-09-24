using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _hudProjectionHooksAttempted;
        static MethodInfo _hudOriginalUiCameraGetter;
        static Func<Camera> _hudOriginalUiCamera;
        struct HudPopupLayoutState { internal bool Active, Updated; }
        static HudPopupLayoutState _hudPopupLayoutState;
        static readonly Vector3[] _hudBoundsCorners = new Vector3[4];
        static long _hudBoundsQueries, _hudBoundsForcedUpdates, _hudBoundsCoalescedUpdates;
        static long _hudBoundsLocalLayouts;
        static bool _hudLocalLayoutFailed;
        static readonly List<Component> _hudPopupLayoutComponents = new List<Component>();

        static void EnsureHudPcProjectionHooks()
        {
            if (_hudProjectionHooksAttempted || _harmony == null) return;
            _hudProjectionHooksAttempted = true;
            try
            {
                var utility = AccessTools.TypeByName("Kingmaker.UI.Common.UIUtility");
                var uiCamera = AccessTools.TypeByName("Kingmaker.UI.UICamera");
                _hudOriginalUiCameraGetter = AccessTools.PropertyGetter(uiCamera, "Instance");
                _hudOriginalUiCamera = (Func<Camera>)Delegate.CreateDelegate(typeof(Func<Camera>), _hudOriginalUiCameraGetter);
                var popup = AccessTools.Method(utility, "SetPopupWindowPosition");
                var inScreen = AccessTools.Method(utility, "IsTransformInScreen", new[] { typeof(Transform) });
                var localPoint = AccessTools.Method(typeof(RectTransformUtility), "ScreenPointToLocalPointInRectangle",
                    new[] { typeof(RectTransform), typeof(Vector2), typeof(Camera), typeof(Vector2).MakeByRefType() });
                var raycast = AccessTools.Method(typeof(GraphicRaycaster), "Raycast",
                    new[] { typeof(PointerEventData), typeof(List<RaycastResult>) });
                var eventCamera = AccessTools.PropertyGetter(typeof(GraphicRaycaster), "eventCamera");
                if (popup == null || !popup.IsStatic || popup.GetParameters().Length != 4 ||
                    popup.GetParameters()[1].ParameterType != typeof(RectTransform) || inScreen == null || localPoint == null || raycast == null ||
                    eventCamera == null || eventCamera.ReturnType != typeof(Camera) || eventCamera.GetParameters().Length != 0 || !eventCamera.IsVirtual)
                    throw new MissingMethodException("PC popup projection contract changed");
                _harmony.Patch(popup, prefix: new HarmonyMethod(typeof(Main), nameof(HudPopupLayoutBegin)),
                    transpiler: new HarmonyMethod(typeof(Main), nameof(HudPopupCameraTranspiler)),
                    finalizer: new HarmonyMethod(typeof(Main), nameof(HudPopupLayoutEnd)));
                _harmony.Patch(inScreen, prefix: new HarmonyMethod(typeof(Main), nameof(HudInScreenPrefix)));
                _harmony.Patch(localPoint, prefix: new HarmonyMethod(typeof(Main), nameof(HudLocalPointCameraPrefix)));
                _harmony.Patch(raycast, prefix: new HarmonyMethod(typeof(Main), nameof(HudGraphicRaycastPrefix)));
                _harmony.Patch(eventCamera, postfix: new HarmonyMethod(typeof(Main), nameof(HudWorldEventCameraPostfix)));
                var nativeRaycaster=AccessTools.TypeByName("Kingmaker.UI.Pointer.KingmakerGraphicRaycaster");
                var nativeEventCamera=AccessTools.PropertyGetter(nativeRaycaster,"eventCamera");
                if(nativeEventCamera==null)throw new MissingMethodException("KingmakerGraphicRaycaster.eventCamera");
                _harmony.Patch(nativeEventCamera,postfix:new HarmonyMethod(typeof(Main),nameof(HudNativeWorldEventCamera68)));
                _harmony.Patch(AccessTools.Method(typeof(EventSystem),"RaycastAll"),
                    postfix:new HarmonyMethod(typeof(Main),nameof(TouchWorldRaycastResults68)));
                _harmony.Patch(raycast,
                    prefix: new HarmonyMethod(typeof(HudRaycastUnitsHooks), nameof(HudRaycastUnitsHooks.Prefix)),
                    postfix: new HarmonyMethod(typeof(HudRaycastUnitsHooks), nameof(HudRaycastUnitsHooks.Postfix)),
                    finalizer: new HarmonyMethod(typeof(HudRaycastUnitsHooks), nameof(HudRaycastUnitsHooks.Finalizer)));
                _log.Log("[ui/pc-projection] PC popup anchors, screen bounds and screen-to-local conversions use the panel camera only for our moved canvases.");
            }
            catch (Exception error) { _log.Error("[ui/pc-projection] " + error); }
        }

        static bool IsHudPanelTransform(Transform transform) => _attached && _pickCam != null &&
            transform != null && ((_uiRoot != null && transform.IsChildOf(_uiRoot)) || IndependentGroupOwns81(transform));

        static bool HudCanvasShown(string name) => _cfg.uiEnabled || name == "FadeCanvas";

        static bool HudGraphicRaycastPrefix(GraphicRaycaster __instance)
        {
            if (_cfg.uiEnabled || !IsHudPanelTransform(__instance.transform)) return true;
            var root = __instance.transform;
            while (root.parent != null && root.parent != _uiRoot) root = root.parent;
            // Disabling Canvas rendering alone does not guarantee that a nested
            // GraphicRaycaster cannot return a button. Exclude hidden HUD trees
            // at their original raycaster; preserve the game's fade blocker.
            return HudCanvasShown(root.name);
        }

        static Camera HudPopupCamera(RectTransform source) =>
            IsHudPanelTransform(source) ? (IsWorldHudTransform(source) ? HudWorldPickingCamera() : _pickCam) : _hudOriginalUiCamera();

        // A popup tries several pivots synchronously. After its first layout,
        // only anchors/pivot/position change; size and content remain the same.
        // Never share this permission across calls, frames or nested popups.
        static void HudPopupLayoutBegin(RectTransform __0, out HudPopupLayoutState __state)
        {
            __state = _hudPopupLayoutState;
            _hudPopupLayoutState = new HudPopupLayoutState { Active = _cfg.coalesceHudLayout && IsHudPanelTransform(__0) };
        }
        static void HudPopupLayoutEnd(HudPopupLayoutState __state) { _hudPopupLayoutState = __state; }

        static IEnumerable<CodeInstruction> HudPopupCameraTranspiler(IEnumerable<CodeInstruction> source)
        {
            int replaced = 0;
            foreach (var instruction in source)
            {
                if (instruction.Calls(_hudOriginalUiCameraGetter))
                {
                    // Preserve the original game pivot choices, anchors, size,
                    // fallback and offset logic; change only the camera used to
                    // project a moved source rect into PC viewport coordinates.
                    var argument = new CodeInstruction(OpCodes.Ldarg_1);
                    argument.labels.AddRange(instruction.labels);
                    argument.blocks.AddRange(instruction.blocks);
                    yield return argument;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Main), nameof(HudPopupCamera)));
                    ++replaced;
                }
                else yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException("Expected one UICamera getter in PC popup placement; found " + replaced);
        }

        static bool HudInScreenPrefix(Transform __0, ref bool __result)
        {
            if (!IsHudPanelTransform(__0)) return true;
            var rect = __0 as RectTransform;
            if (rect == null) return true;
            ++_hudBoundsQueries;
            if (!_hudPopupLayoutState.Active || !_hudPopupLayoutState.Updated)
            {
                _hudPopupLayoutState.Updated = true;
                ++_hudBoundsForcedUpdates;
                if (!TryRebuildHudPopupLayout(rect)) Canvas.ForceUpdateCanvases();
            }
            else ++_hudBoundsCoalescedUpdates;
            var corners = _hudBoundsCorners;
            rect.GetWorldCorners(corners);
            var projectionCamera = HudPopupCamera(rect);
            __result = true;
            foreach (var corner in corners)
            {
                var projected = projectionCamera.WorldToViewportPoint(corner);
                if (projected.z <= 0 || projected.x < 0 || projected.x > 1 || projected.y < 0 || projected.y > 1)
                { __result = false; break; }
            }
            return false;
        }

        static bool TryRebuildHudPopupLayout(RectTransform rect)
        {
            if (!_cfg.coalesceHudLayout || _hudLocalLayoutFailed || rect == null || rect == _uiRoot) return false;
            try
            {
                // Match the installed MarkLayoutForRebuild root search. A plain
                // Canvas/RectTransform does not traverse its children in Unity's
                // PerformLayoutCalculation/Control, so rebuilding the top panel
                // can silently leave a nested popup's fitters and bounds stale.
                // Only contiguous active parent ILayoutGroups propagate layout.
                var root = rect;
                var parent = rect.parent as RectTransform;
                while (parent != null && parent != _uiRoot)
                {
                    parent.GetComponents(typeof(ILayoutGroup), _hudPopupLayoutComponents);
                    bool activeGroup = false;
                    foreach (var component in _hudPopupLayoutComponents)
                        if (component is Behaviour behaviour && behaviour.isActiveAndEnabled)
                        { activeGroup = true; break; }
                    if (!activeGroup) break;
                    root = parent;
                    parent = parent.parent as RectTransform;
                }
                // A generic wrapper needs its independent descendant fitters
                // rebuilt individually, rather than a no-op pass at the wrapper.
                root.GetComponents(typeof(ILayoutController), _hudPopupLayoutComponents);
                bool activeController = false;
                foreach (var component in _hudPopupLayoutComponents)
                    if (component is Behaviour behaviour && behaviour.isActiveAndEnabled)
                    { activeController = true; break; }
                if (!activeController) return TryRebuildHudPopupChildren(rect);
                // The reused selection list is no longer needed when native
                // layout invokes callbacks, which can synchronously place a
                // nested popup. Each invocation keeps its own local root.
                _hudPopupLayoutComponents.Clear();
                LayoutRebuilder.ForceRebuildLayoutImmediate(root);
                ++_hudBoundsLocalLayouts;
                return true;
            }
            catch (Exception error)
            {
                _hudLocalLayoutFailed = true;
                _log.Error("[ui/layout] Local popup layout unavailable; retaining native global update: " + error.Message);
                return false;
            }
            finally { _hudPopupLayoutComponents.Clear(); }
        }

        static void HudLocalPointCameraPrefix(RectTransform __0, ref Camera __2)
        {
            if (IsHudPanelTransform(__0)) __2 = HudPopupCamera(__0);
        }

        static long _hudPopupSubtreeLayouts;
        static bool TryRebuildHudPopupChildren(RectTransform rect)
        {
            // Some native parchment/popup roots are plain wrappers. A global
            // ForceUpdateCanvases here rebuilds every unrelated window and
            // radial in the middle of input. Rebuild the popup's independent
            // layout islands only, deepest first; normal PreRender still runs.
            // Local lists are intentional: a fitter may synchronously place a
            // nested popup, so scratch state cannot be shared across calls.
            var nodes=rect.GetComponentsInChildren<RectTransform>(false);
            if(nodes.Length>512)return false;
            var controllers=new List<Component>(4);
            bool found=false;
            for(int i=nodes.Length-1;i>=0;i--)
            {
                var node=nodes[i]; controllers.Clear(); node.GetComponents(typeof(ILayoutController),controllers);
                bool active=false;
                foreach(var c in controllers)if(c is Behaviour b && b.isActiveAndEnabled){active=true;break;}
                if(!active)continue;
                bool grouped=false;
                if(node.parent!=null && node.parent!=rect)
                {
                    controllers.Clear();node.parent.GetComponents(typeof(ILayoutGroup),controllers);
                    foreach(var c in controllers)if(c is Behaviour b && b.isActiveAndEnabled){grouped=true;break;}
                }
                if(grouped)continue;
                LayoutRebuilder.ForceRebuildLayoutImmediate(node);found=true;++_hudPopupSubtreeLayouts;
            }
            // A popup consisting solely of fixed rectangles needs no layout
            // pass at all. Its Graphic meshes will rebuild at normal PreRender.
            return found || nodes.Length!=0;
        }
    }
}
