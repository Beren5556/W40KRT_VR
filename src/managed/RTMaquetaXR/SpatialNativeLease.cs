using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    // Temporarily relocates the original native view, including its original
    // text, art and subscriptions. Restoration never recreates native content.
    internal sealed class SpatialNativeLease : IDisposable
    {
        internal readonly Component View;
        readonly RectTransform rect;
        readonly Transform parent, destination;
        readonly bool hadParent;
        readonly int sibling;
        readonly Vector2 anchorMin, anchorMax, pivot, size;
        readonly Vector3 anchored, scale;
        readonly Quaternion rotation;
        readonly Dictionary<GameObject, int> layers = new Dictionary<GameObject, int>();
        readonly Dictionary<Canvas, Camera> cameras = new Dictionary<Canvas, Camera>();
        readonly List<GameObject> departedLayers = new List<GameObject>();
        readonly List<Canvas> departedCameras = new List<Canvas>();
        readonly List<Transform> scanNodes = new List<Transform>(64);
        readonly List<Canvas> scanCanvases = new List<Canvas>(8);
        readonly List<Graphic> scanGraphics = new List<Graphic>(32);
        struct ClipBounds { internal bool Clipped; internal Rect Bounds; }
        readonly Dictionary<Transform, ClipBounds> clipBounds = new Dictionary<Transform, ClipBounds>();
        internal long HierarchyScans { get; private set; }
        internal long BoundsMeasurements { get; private set; }
        internal long MaskQueries { get; private set; }
        readonly Vector3[] corners = new Vector3[4];
        Rect visibleBounds;
        Vector2 measuredLayoutSize;
        readonly int nativeLayer, spatialLayer;
        readonly Camera nativeCamera;
        bool attached;
        NativeInformationScroll scroll;
        readonly Dictionary<Behaviour, NativeInformationScroll> scrollAdapters = new Dictionary<Behaviour, NativeInformationScroll>();
        readonly List<NativeInformationScroll> scrolls = new List<NativeInformationScroll>(4);
        readonly List<Component> scanScrollComponents = new List<Component>(64);
        internal Vector2 PlacedSize { get; private set; }
        RectTransform referenceNode;
        Transform referenceParent;
        Vector2 referenceSize;
        Vector3 referencePosition, referenceScale;
        Rect referenceBounds;
        Rect StableReferenceBounds(RectTransform reference)
        {
            if(referenceNode!=reference || referenceParent!=reference.parent ||
                referenceSize.x!=reference.rect.width || referenceSize.y!=reference.rect.height ||
                referencePosition!=reference.anchoredPosition3D || referenceScale!=reference.localScale)
            {
                referenceNode=reference;referenceParent=reference.parent;referenceSize=reference.rect.size;
                referencePosition=reference.anchoredPosition3D;referenceScale=reference.localScale;
                referenceBounds=LocalBounds(reference);
            }
            return referenceBounds;
        }
        internal bool CanScroll
        {
            get
            {
                if (scroll != null && scroll.Available) return true;
                // Inspect/ability windows include several inactive scrolls.
                // Choosing the first component could permanently select one
                // with no content while the visible native card did overflow.
                foreach (var candidate in scrolls)
                    if (candidate.Available) { scroll=candidate; return true; }
                return false;
            }
        }
        internal SpatialNativeLease(Component view, RectTransform destination, Camera fallbackCamera)
        {
            View = view; rect = view.transform as RectTransform;
            if (rect == null) throw new InvalidOperationException("Native spatial view has no RectTransform");
            this.destination = destination;
            spatialLayer = destination.gameObject.layer;
            nativeLayer = UiLayerOwnership.NativeLayer(rect.gameObject.layer, spatialLayer); nativeCamera = fallbackCamera;
            parent = rect.parent; hadParent = parent != null; sibling = rect.GetSiblingIndex(); anchorMin = rect.anchorMin; anchorMax = rect.anchorMax;
            pivot = rect.pivot; size = rect.sizeDelta; anchored = rect.anchoredPosition3D; scale = rect.localScale; rotation = rect.localRotation;
            CaptureNewNodes();
            Vector2 actual = rect.rect.size;
            rect.SetParent(destination, false); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = actual; rect.localRotation = Quaternion.identity;
            attached = true;
            MeasureBounds(); // Same native hierarchy; only its pivot coordinates changed.
        }
        internal void CaptureNewNodes()
        {
            if (rect == null) return;
            ReleaseDepartedNodes();
            if (attached && rect.parent != destination) return;
            ++HierarchyScans;
            scanNodes.Clear(); rect.GetComponentsInChildren(true, scanNodes);
            scroll = null; scrolls.Clear();
            scanScrollComponents.Clear(); rect.GetComponentsInChildren(true, scanScrollComponents);
            foreach (var component in scanScrollComponents)
                if (NativeInformationScroll.Supports(component)) {
                    var behaviour = (Behaviour)component;
                    if (!scrollAdapters.TryGetValue(behaviour, out var adapter))
                        scrollAdapters.Add(behaviour, adapter = new NativeInformationScroll(behaviour));
                    scrolls.Add(adapter);
                }
            foreach (var node in scanNodes)
            {
                if (!layers.ContainsKey(node.gameObject)) layers[node.gameObject] = node.gameObject.layer == spatialLayer ? nativeLayer : node.gameObject.layer;
            }
            scanCanvases.Clear(); rect.GetComponentsInChildren(true, scanCanvases);
            foreach (var canvas in scanCanvases)
                if (!cameras.ContainsKey(canvas)) cameras[canvas] = Main.IsSpatialCaptureCamera(canvas.worldCamera) ? nativeCamera : canvas.worldCamera;
            scanGraphics.Clear(); rect.GetComponentsInChildren(false, scanGraphics);
            MeasureBounds();
        }
        void MeasureBounds()
        {
            ++BoundsMeasurements;
            clipBounds.Clear();
            // Keep the original card rectangle and visible parchment. A long
            // scroll's children can be thousands of pixels beyond its viewport;
            // those clipped pixels must not shrink the entire information card.
            visibleBounds = rect.rect;
            measuredLayoutSize = visibleBounds.size;
            foreach (var graphic in scanGraphics)
            {
                if (graphic == null || !graphic.isActiveAndEnabled || !InNativeView(graphic.transform)) continue;
                Rect shown = LocalBounds(graphic.rectTransform);
                ClipBounds clip = graphic.transform == rect ? default(ClipBounds) : ClipFor(graphic.transform.parent);
                if (clip.Clipped) shown = Intersect(shown, clip.Bounds);
                if (shown.size.x <= 0 || shown.size.y <= 0) continue;
                visibleBounds = Rect.MinMaxRect(Mathf.Min(visibleBounds.xMin,shown.xMin),Mathf.Min(visibleBounds.yMin,shown.yMin),
                    Mathf.Max(visibleBounds.xMax,shown.xMax),Mathf.Max(visibleBounds.yMax,shown.yMax));
            }
        }
        ClipBounds ClipFor(Transform node)
        {
            if (node == null) return default(ClipBounds);
            if (clipBounds.TryGetValue(node, out var found)) return found;
            ClipBounds result = node == rect ? default(ClipBounds) : ClipFor(node.parent);
            var rectangle = node as RectTransform;
            var mask2D = node.GetComponent<RectMask2D>();
            var mask = node.GetComponent<Mask>();
            ++MaskQueries;
            if (rectangle != null && ((mask2D != null && mask2D.isActiveAndEnabled) || (mask != null && mask.isActiveAndEnabled)))
            {
                Rect bounds = LocalBounds(rectangle);
                result.Bounds = result.Clipped ? Intersect(result.Bounds, bounds) : bounds;
                result.Clipped = true;
            }
            clipBounds.Add(node, result);
            return result;
        }
        Rect LocalBounds(RectTransform node)
        {
            node.GetWorldCorners(corners);
            Vector3 first = rect.InverseTransformPoint(corners[0]);
            Rect bounds = Rect.MinMaxRect(first.x, first.y, first.x, first.y);
            for (int i = 1; i < corners.Length; ++i)
            {
                Vector3 p = rect.InverseTransformPoint(corners[i]);
                bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin,p.x),Mathf.Min(bounds.yMin,p.y),Mathf.Max(bounds.xMax,p.x),Mathf.Max(bounds.yMax,p.y));
            }
            return bounds;
        }
        static Rect Intersect(Rect value, Rect clip) => Rect.MinMaxRect(Mathf.Max(value.xMin,clip.xMin),
            Mathf.Max(value.yMin,clip.yMin),Mathf.Min(value.xMax,clip.xMax),Mathf.Min(value.yMax,clip.yMax));
        internal bool Scroll(float axis, float seconds)
        {
            if (!CanScroll || float.IsNaN(axis) || float.IsInfinity(axis) || float.IsNaN(seconds) || float.IsInfinity(seconds)) return false;
            // ScrollRect's own setter moves the original content and scrollbar.
            // No cloned text, masked content resize or simulated game click.
            return scroll.Scroll(axis,seconds);
        }
        bool InNativeView(Transform node) => node != null && (node == rect || node.IsChildOf(rect));
        internal void ReleaseDepartedNode(Transform node)
        {
            if (node == null || (rect != null && rect.parent == destination && InNativeView(node))) return;
            // Canvas registration can occur before the next hierarchy scan.
            // Return this node's real layer before the normal HUD records it.
            var item = node.gameObject;
            if (layers.TryGetValue(item, out int layer))
            {
                if (item.layer == spatialLayer) item.layer = layer;
                layers.Remove(item);
            }
            var canvas = node.GetComponent<Canvas>();
            if (canvas != null && cameras.TryGetValue(canvas, out Camera camera))
            {
                if (Main.IsSpatialCaptureCamera(canvas.worldCamera)) canvas.worldCamera = camera;
                cameras.Remove(canvas);
            }
        }
        void ReleaseDepartedNodes()
        {
            // Native pools can reparent a marker/tooltip into another live
            // window while our lease is still open. Relinquish it immediately;
            // restore only temporary values still owned by this capture.
            bool rootMoved = attached && rect.parent != destination;
            departedLayers.Clear();
            foreach (var pair in layers)
                if (pair.Key == null || rootMoved || !InNativeView(pair.Key.transform))
                {
                    if (pair.Key != null && pair.Key.layer == spatialLayer) pair.Key.layer = pair.Value;
                    departedLayers.Add(pair.Key);
                }
            foreach (var node in departedLayers) layers.Remove(node);
            departedCameras.Clear();
            foreach (var pair in cameras)
                if (pair.Key == null || rootMoved || !InNativeView(pair.Key.transform))
                {
                    if (pair.Key != null && Main.IsSpatialCaptureCamera(pair.Key.worldCamera)) pair.Key.worldCamera = pair.Value;
                    departedCameras.Add(pair.Key);
                }
            foreach (var canvas in departedCameras) cameras.Remove(canvas);
        }
        internal void Place(Vector2 centre, Vector2 bounds, Camera camera) => Place(centre, bounds, camera, false);
        internal float ReferenceWidth(RectTransform reference) => rect != null && reference != null ?
            StableReferenceBounds(reference).width * Mathf.Abs(rect.localScale.x) : 0;
        internal void AlignReference(RectTransform reference, float visibleWidth, Vector2 centre)
        {
            if (rect == null || rect.parent != destination || reference == null || visibleWidth <= 0) return;
            Rect art = StableReferenceBounds(reference);
            if (art.width <= 0) return;
            float factor = visibleWidth / art.width;
            Vector3 scale = Vector3.one * factor;
            Vector3 position = new Vector3(centre.x-art.center.x*factor, centre.y-art.center.y*factor, -.01f);
            if (rect.localScale != scale) rect.localScale = scale;
            if (rect.anchoredPosition3D != position) rect.anchoredPosition3D = position;
            PlacedSize = visibleBounds.size * factor;
        }
        internal void SetCamera(Camera camera)
        {
            foreach (var pair in cameras)
                if (pair.Key != null && InNativeView(pair.Key.transform) && pair.Key.worldCamera != camera) pair.Key.worldCamera = camera;
        }
        internal void Place(Vector2 centre, Vector2 bounds, Camera camera, bool fill)
        {
            if (rect == null || rect.parent != destination) return;
            Vector2 currentLayoutSize = rect.rect.size;
            // Native fitters update height while opening a card. That changes
            // bounds, not ownership/scroll component discovery. New children
            // are still collected by registration and the periodic audit.
            if(currentLayoutSize.x != measuredLayoutSize.x || currentLayoutSize.y != measuredLayoutSize.y) MeasureBounds();
            // Native layout may change as the inspected actor changes. Keep its
            // own aspect and fit its complete rectangle, never stretch text.
            Vector2 actual = visibleBounds.size;
            // Retain native pixel scale for all ordinary cards. Shorter text
            // no longer balloons to fill the envelope. Only oversized native
            // windows are fitted, uniformly, to keep their entire art visible.
            float factor = Mathf.Min(fill ? 4 : 1, Mathf.Min(bounds.x / Mathf.Max(1, actual.x), bounds.y / Mathf.Max(1, actual.y)));
            PlacedSize = actual * factor;
            Vector3 targetScale = Vector3.one * factor;
            if (rect.localScale != targetScale) rect.localScale = targetScale;
            Vector3 target = new Vector3(centre.x-visibleBounds.center.x*factor, centre.y-visibleBounds.center.y*factor, -.01f);
            if (rect.anchoredPosition3D != target) rect.anchoredPosition3D = target;
            SetCamera(camera);
        }
        public void Dispose()
        {
            if (rect != null && rect.parent == destination)
            {
                // A scene may unload while an original widget is borrowed by
                // the persistent VR canvas. Do not orphan that old scene view.
                if (hadParent && parent == null)
                { rect.gameObject.SetActive(false); UnityEngine.Object.Destroy(rect.gameObject); }
                rect.SetParent(parent != null ? parent : null, false); rect.SetSiblingIndex(sibling); rect.anchorMin = anchorMin; rect.anchorMax = anchorMax;
                rect.pivot = pivot; rect.sizeDelta = size; rect.anchoredPosition3D = anchored; rect.localScale = scale; rect.localRotation = rotation;
            }
            foreach (var pair in layers) if (pair.Key != null && pair.Key.layer == spatialLayer) pair.Key.layer = pair.Value;
            foreach (var pair in cameras) if (pair.Key != null && Main.IsSpatialCaptureCamera(pair.Key.worldCamera)) pair.Key.worldCamera = pair.Value;
            layers.Clear(); cameras.Clear(); departedLayers.Clear(); departedCameras.Clear();
            scanNodes.Clear(); scanCanvases.Clear(); scanGraphics.Clear(); clipBounds.Clear();
        }
    }
}
