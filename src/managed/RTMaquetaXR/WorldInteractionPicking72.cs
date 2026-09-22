using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    internal sealed class WorldInteractionRaycaster72 : BaseRaycaster
    {
        public override Camera eventCamera => Main.WorldInteractionCamera72;
        // Results are injected once into the existing EventSystem from the
        // controller ray; never cast a second camera ray through a flat HUD.
        public override void Raycast(PointerEventData data, List<RaycastResult> results) { }
    }
    public static partial class Main
    {
        static WorldInteractionRaycaster72 _worldInteractionRaycaster72;
        static WorldInformationSource70 _worldInteractionStable72;
        static readonly List<Component> _worldInteractionFilters72 = new List<Component>(8);
        internal static Camera WorldInteractionCamera72 => HudWorldPickingCamera();

        static void RegisterInteractionTarget72(WorldInformationSource70 source)
        {
            source.InteractionButton72 = ReadField70(source.View, "m_Button") as Component;
            source.InteractionImage72 = ReadField70(source.View, "m_MainImage") as Graphic;
            source.InteractionRect72 = source.InteractionImage72 == null ? source.InteractionButton72?.transform as RectTransform : source.InteractionImage72.rectTransform;
            if(TouchProximityContractsReady73)
            {source.ProximityOnly73=ProximityOwnsNativeMarker74(source.View);source.ProximityClassified73=true;}
        }
        static bool InteractionAvailable72(WorldInformationSource70 source)
        {
            if (source?.View == null || source.ProximityOnly73 || source.InteractionButton72 == null || source.InteractionRect72 == null ||
                !source.View.gameObject.activeInHierarchy || !source.InteractionButton72.gameObject.activeInHierarchy) return false;
            if (!(TryGetProp(source.InteractionButton72, "Interactable") is bool allowed) || !allowed) return false;
            foreach (var graphic in source.InteractionGraphics)
                if (graphic != null && graphic.isActiveAndEnabled && graphic.color.a > .001f &&
                    graphic.canvasRenderer != null && graphic.canvasRenderer.GetInheritedAlpha() > .01f && !WorldGraphicSuppressed72(graphic)) return true;
            return false;
        }
        static bool InteractionFiltersAllow72(WorldInformationSource70 source, Vector2 screen, Camera camera)
        {
            bool ignoreGroups = false;
            for (Transform node = source.InteractionRect72; node != null; node = node.parent)
            {
                _worldInteractionFilters72.Clear(); node.GetComponents(_worldInteractionFilters72);
                foreach (var component in _worldInteractionFilters72)
                {
                    if (component is CanvasGroup group)
                    {
                        if (ignoreGroups) continue;
                        if (!group.interactable || !group.blocksRaycasts || group.alpha <= .001f) return false;
                        if (group.ignoreParentGroups) ignoreGroups = true;
                    }
                    else if ((component is Mask || component is RectMask2D) && component is ICanvasRaycastFilter filter &&
                        !filter.IsRaycastLocationValid(screen, camera)) return false;
                }
            }
            return true;
        }
        static bool InteractionHit72(WorldInformationSource70 source, Ray ray, Camera camera, bool retain,
            out Vector3 point, out float distance)
        {
            point = default(Vector3); distance = 0;
            if (!InteractionAvailable72(source)) return false;
            var rect = source.InteractionRect72;
            if (!new Plane(rect.forward, rect.position).Raycast(ray, out distance) || distance < 0) return false;
            point = ray.GetPoint(distance);
            Vector3 local = rect.InverseTransformPoint(point); Rect area = rect.rect;
            // Proportional tolerance is tied to the visible symbol, not HUD
            // resolution, graphic depth ordering, or an unrelated button plane.
            float margin = Mathf.Min(area.width, area.height) * (retain ? .14f : .05f);
            area.xMin -= margin; area.xMax += margin; area.yMin -= margin; area.yMax += margin;
            if (!area.Contains(new Vector2(local.x, local.y))) return false;
            Vector3 projected = camera.WorldToScreenPoint(point);
            return projected.z > 0 && InteractionFiltersAllow72(source, new Vector2(projected.x, projected.y), camera);
        }
        static bool PickWorldInteraction72(Ray ray)
        {
            if (!EffectiveWorldOvertips || _overtipsRoot == null) return false;
            var camera = HudWorldPickingCamera(); if (camera == null) return false;
            if (_touchUiPress.Captured && _touchUiPressTarget != null && _touchWorldResult68.gameObject == _touchUiPressTarget)
            {
                if (_worldInteractionStable72?.InteractionButton72 == null ||
                    _worldInteractionStable72.InteractionButton72.gameObject != _touchUiPressTarget || !InteractionAvailable72(_worldInteractionStable72))
                {
                    // A disappearing/disabled object must cancel the press,
                    // never transfer its release to a neighbour or the floor.
                    CancelTouchPointerPress(); _worldInteractionStable72 = null;
                    _touchWorldResult68 = default(RaycastResult); ClearStableInteractionTargetImmediately70();
                    return false;
                }
                _touchScreen = _touchWorldResult68.screenPosition; _touchTargetPoint = _touchWorldResult68.worldPosition;
                _touchTarget = _touchUiPressTarget; _touchWorldHit68 = _touchOverUi = _touchHasPoint = true; return true;
            }
            WorldInformationSource70 best = null; float nearest = float.PositiveInfinity; Vector3 hit = default(Vector3);
            if (InteractionHit72(_worldInteractionStable72, ray, camera, true, out var stablePoint, out float stableDistance))
            { best = _worldInteractionStable72; nearest = stableDistance; hit = stablePoint; }
            foreach (var source in _worldInformationSources70.Values)
            {
                if ((source.Family & WorldHudPolicy.InteractionFamily) == 0 || ReferenceEquals(source, best)) continue;
                if (!InteractionHit72(source, ray, camera, false, out var point, out float distance)) continue;
                if (best != null && distance >= nearest - Mathf.Max(.005f, WorldScale * .01f)) continue;
                best = source; nearest = distance; hit = point;
            }
            if (best == null) { _worldInteractionStable72 = null; ClearStableInteractionTargetImmediately70(); return false; }
            if (_worldInteractionRaycaster72 == null)
            {
                var obj = new GameObject("RTMaquetaXR world interaction raycaster", typeof(WorldInteractionRaycaster72));
                UnityEngine.Object.DontDestroyOnLoad(obj); _worldInteractionRaycaster72 = obj.GetComponent<WorldInteractionRaycaster72>();
            }
            _worldInteractionStable72 = best;
            var target = best.InteractionButton72.gameObject;
            if (target != _stableInteractionTarget70) { _stableInteractionTarget70 = target; ++_interactionTargetChanges70; }
            Vector3 screen = camera.WorldToScreenPoint(hit);
            _touchScreen = new Vector2(screen.x, screen.y); _touchTargetPoint = hit; _touchTarget = target;
            var canvas = best.InteractionImage72?.canvas;
            _touchWorldResult68 = new RaycastResult { gameObject = target, module = _worldInteractionRaycaster72, distance = nearest,
                worldPosition = hit, worldNormal = -best.InteractionRect72.forward, screenPosition = _touchScreen,
                depth = best.InteractionImage72 == null ? 0 : best.InteractionImage72.depth,
                sortingLayer = canvas == null ? 0 : canvas.sortingLayerID, sortingOrder = canvas == null ? 0 : canvas.sortingOrder, index = 0 };
            _touchWorldHit68 = _touchOverUi = _touchHasPoint = true; return true;
        }
        static void ReleaseWorldInteraction72()
        {
            _worldInteractionStable72 = null;
            if (_worldInteractionRaycaster72 != null) UnityEngine.Object.Destroy(_worldInteractionRaycaster72.gameObject);
            _worldInteractionRaycaster72 = null;
        }
    }
}
