using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

namespace RTMaquetaXR
{
    internal static class SpatialPresentationPolicy
    {
        internal const int LogicalWidth = 1600, LogicalHeight = 2400, RasterWidth = 2732, RasterHeight = 4096;
        internal const float WheelY = -600, InformationY = 600, IslandDistance = 4096;
        internal static int FreeLayer(uint used, uint named, int hud, int world)
        {
            for (int candidate = 31; candidate >= 8; --candidate)
                if (candidate != hud && candidate != world && ((used | named) & (1u << candidate)) == 0) return candidate;
            return hud;
        }
        internal static bool MaskOutsideCapture(bool shared, bool hudCamera, bool spatialCamera) =>
            !spatialCamera && !(shared && hudCamera);
    }
    public static partial class Main
    {
        // One atlas/two camera passes: bottom = physical wheel, top = original
        // native information. The compositor gives each half its own pose.
        // Unit-scale canonical geometry avoids precision loss at scene coordinates.
        const float SpatialPixel = 1f;
        const int SpatialWidth = SpatialPresentationPolicy.LogicalWidth, SpatialHeight = SpatialPresentationPolicy.LogicalHeight;
        static GameObject _spatialRoot;
        static Camera _spatialBlackCamera, _spatialWhiteCamera;
        static RenderTexture _spatialBlack, _spatialWhite;
        static int _spatialLayer = -1, _spatialFrame = -1, _spatialScanFrame, _spatialInformationRevision = -1;
        static bool _spatialSharedHudLayer, _spatialInformationShown;
        static string _spatialFault;
        static int _spatialRecoveryAttempts;
        static float _spatialRetryAt;
        static long _spatialRecoveryRevision=-1;
        static readonly List<Transform> _spatialNodes = new List<Transform>();
        static SpatialNativeLease _spatialNative;
        static Vector3 _spatialWorldCentre, _spatialCaptureOrigin;
        static Quaternion _spatialWorldRotation;
        static float _spatialWorldPixel, _spatialInformationWidth;
        static XrPose _spatialPose, _spatialInformationPose;
        static long _spatialFrames, _spatialMissedFrames;
        static readonly Dictionary<Camera, int> _spatialCameraMasks = new Dictionary<Camera, int>();
        internal static bool IsSpatialCaptureCamera(Camera camera) => camera != null &&
            (camera == _spatialBlackCamera || camera == _spatialWhiteCamera);
        internal static bool IsSpatialNode(Transform node) => node != null && _spatialRoot != null &&
            (node == _spatialRoot.transform || node.IsChildOf(_spatialRoot.transform));
        internal static void ReleaseSpatialDepartedNode(Transform node) => _spatialNative?.ReleaseDepartedNode(node);
        internal static object SpatialUiSnapshot() => new { Active = _spatialFrame == Time.frameCount,
            Fault = _spatialFault, Width = _spatialBlack == null ? 0 : _spatialBlack.width,
            Height = _spatialBlack == null ? 0 : _spatialBlack.height, Frames = _spatialFrames,
            MissedFrames = _spatialMissedFrames, Layer = _spatialLayer, SharedHudLayer = _spatialSharedHudLayer,
            InformationVisible = _spatialInformationShown, InformationAnchor = "Original native window at fixed screen centre, slightly right; 75% of 0.1.58 size",
            VolumeIsolation = HudVolumeSnapshot(),
            NativeCardLayout = new { Requests = NativeCardLayoutBatch.Requests, Coalesced = NativeCardLayoutBatch.Coalesced,
                Rebuilds = NativeCardLayoutBatch.Rebuilds, MeasuredBuilds76=NativeCardLayoutBatch.MeasuredBuilds76, MeasuredBuildMs76=NativeCardLayoutBatch.BuildTicks76*1000d/System.Diagnostics.Stopwatch.Frequency, MeasuredLayoutMs76 = NativeCardLayoutBatch.LayoutTicks76*1000d/System.Diagnostics.Stopwatch.Frequency, DeferredSections = _nativeCardDeferredSections, HierarchyScans = _spatialNative?.HierarchyScans,
                BoundsMeasurements = _spatialNative?.BoundsMeasurements, MaskQueries = _spatialNative?.MaskQueries },
            NativeCombatIndicators = RadialCombatStatusSnapshot(),
            InformationPhysicalWidth = SpatialInformationLayout.FixedWidth,
            Path = "Independent spatial atlas; physical wheel and head-centred original native information; no temporal resampling" };

        static void PositionTouchRadialPlane()
        {
            UpdateTouchRadialTrackingPlane();
            if (_radialFlatDrawing)
            {
                EnsureSpatialRoot();
                var wheel = _touchRadialRoot.transform;
                RestoreHudRoot(wheel); wheel.SetParent(null, true);
                wheel.SetPositionAndRotation(_touchRadialWorldCenter, _touchRadialWorldRotation);
                wheel.localScale = Vector3.one * _touchRadialWorldPixel;
                return;
            }
            if (_spatialFault != null)
            {
                PositionHudHelper(_touchRadialRoot.transform, _touchRadialWorldCenter,
                    _touchRadialWorldRotation, _touchRadialWorldPixel);
                return;
            }
            EnsureSpatialRoot();
            var node = _touchRadialRoot.transform;
            if (node.parent != _spatialRoot.transform) { RestoreHudRoot(node); node.SetParent(_spatialRoot.transform, false); _spatialScanFrame = 0; }
            // The physical pose is applied by the compositor. This retained
            // atlas geometry does not move when a controller or head moves.
            // Avoid re-dirtying its whole Canvas hierarchy with identical values.
            Vector3 atlasPosition = new Vector3(0, SpatialPresentationPolicy.WheelY, 0);
            if (node.localPosition != atlasPosition) node.localPosition = atlasPosition;
            if (node.localRotation != Quaternion.identity) node.localRotation = Quaternion.identity;
            if (node.localScale != Vector3.one) node.localScale = Vector3.one;
        }
        static void EnsureSpatialRoot()
        {
            if (_spatialRoot != null) return;
            uint used = 0, named = 0;
            foreach (var node in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (node != null) used |= 1u << node.gameObject.layer;
            for (int layer = 8; layer < 32; ++layer)
                if (!string.IsNullOrEmpty(LayerMask.LayerToName(layer))) named |= 1u << layer;
            _spatialLayer = UiLayerOwnership.Spatial >= 0 ? UiLayerOwnership.Spatial : UiLayerOwnership.Find(used, named, 8);
            if (_spatialLayer < 0)
            {
                // The shipped game has only two free private layers, consumed
                // by HUD and world indicators. Reserve the HUD slot if needed,
                // then share it with a spatially disjoint capture island.
                ChooseHudLayer(); _spatialLayer = _hudLayer;
            }
            UiLayerOwnership.Spatial = _spatialLayer;
            _spatialSharedHudLayer = _spatialLayer == UiLayerOwnership.Hud;
            if (_spatialSharedHudLayer) _hudLayer = _spatialLayer;
            _spatialRoot = new GameObject("RTMaquetaXR spatial UI", typeof(RectTransform), typeof(Canvas));
            UnityEngine.Object.DontDestroyOnLoad(_spatialRoot); _spatialRoot.layer = _spatialLayer;
            var rect = (RectTransform)_spatialRoot.transform; rect.sizeDelta = new Vector2(SpatialWidth, SpatialHeight);
            rect.localScale = Vector3.one * SpatialPixel;
            rect.position = Vector3.back * SpatialPresentationPolicy.IslandDistance;
            var canvas = _spatialRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true; canvas.sortingOrder = 32762;
            RenderPipelineManager.beginCameraRendering += SpatialCameraBegin;
            RenderPipelineManager.endCameraRendering += SpatialCameraEnd;
        }
        static void SpatialCameraBegin(ScriptableRenderContext context, Camera camera)
        {
            if ((_spatialFrame != Time.frameCount && !FlatRadialOwnsCanvas79) || _spatialLayer < 0 || camera == null ||
                !SpatialPresentationPolicy.MaskOutsideCapture(_spatialSharedHudLayer, IsHudCaptureCamera(camera), IsSpatialCaptureCamera(camera))) return;
            int mask = camera.cullingMask;
            if ((mask & (1 << _spatialLayer)) == 0) return;
            _spatialCameraMasks[camera] = mask; camera.cullingMask = mask & ~(1 << _spatialLayer);
        }
        static void SpatialCameraEnd(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || !_spatialCameraMasks.TryGetValue(camera, out int mask)) return;
            if (camera.cullingMask == (mask & ~(1 << _spatialLayer))) camera.cullingMask = mask;
            _spatialCameraMasks.Remove(camera);
        }
        internal static void PrepareSpatialUi(XrFrame frame, Camera left, Camera right, Vector3 physicalHead, Quaternion physicalRotation)
        {
            // Invalidate publication immediately, while leaving a consecutive
            // frame's owned cameras registered. Every incomplete path disables
            // both cameras in finally; only a completely prepared frame remains.
            _spatialFrame = -1; _spatialInformationShown = false;
            try
            {
                if (left == null || right == null || !_attached || _modeFlat) return;
                if(_spatialRecoveryRevision!=SpatialGameContextRevision)
                {_spatialRecoveryRevision=SpatialGameContextRevision;_spatialRecoveryAttempts=0;_spatialRetryAt=0;}
                bool wheel = _touchRadialShown && _touchRadialPlaneReady;
                Component native = wheel ? TouchRadialInformationContent : null;
                if (!wheel) { ReleaseSpatialNative(); return; }
                if(_spatialFault!=null)
                {
                    if(_spatialRecoveryAttempts>=3||Time.unscaledTime<_spatialRetryAt)return;
                    ++_spatialRecoveryAttempts;_spatialFault=null;InvalidateHudHierarchy();
                }
                EnsureSpatialRoot();
                if (!OpenXR.SpatialCompositorAvailable) throw new InvalidOperationException("Spatial compositor unavailable");
                EnsureHudRenderIsolation(); EnsureHudHierarchyHooks();
                if (_spatialBlackCamera == null)
                {
                    Camera original = FindUICamera();
                    if (original == null) return;
                    _spatialBlackCamera = CreateHudCamera(original, "RTMaquetaXR spatial black", Color.black);
                    _spatialWhiteCamera = CreateHudCamera(original, "RTMaquetaXR spatial white", Color.white);
                    EnsureHudTarget(ref _spatialBlack, SpatialPresentationPolicy.RasterWidth, SpatialPresentationPolicy.RasterHeight, "RTMaquetaXR spatial black raster");
                    EnsureHudTarget(ref _spatialWhite, SpatialPresentationPolicy.RasterWidth, SpatialPresentationPolicy.RasterHeight, "RTMaquetaXR spatial white raster");
                }
                _spatialWorldCentre = _touchRadialWorldCenter;
                _spatialWorldRotation = _touchRadialWorldRotation;
                _spatialWorldPixel = _touchRadialWorldPixel;
                // Always behind the ordinary HUD capture. Even when a layer is
                // shared, the two capture frusta cannot contain each other's UI.
                Vector3 hudHead = _hudStableSpace ? Vector3.zero : physicalHead;
                Quaternion hudRotation = _hudStableSpace ? Quaternion.identity : physicalRotation;
                _spatialCaptureOrigin = hudHead - hudRotation * Vector3.forward * SpatialPresentationPolicy.IslandDistance;
                // Both capture cameras and the retained atlas share an integer
                // origin. Head motion cannot move glyphs across subpixel float
                // positions inside this distant capture island.
                _spatialCaptureOrigin = new Vector3(Mathf.Round(_spatialCaptureOrigin.x),Mathf.Round(_spatialCaptureOrigin.y),Mathf.Round(_spatialCaptureOrigin.z));
                if (_spatialRoot.transform.position != _spatialCaptureOrigin) _spatialRoot.transform.position = _spatialCaptureOrigin;
                if (native != null)
                {
                    if (_spatialNative == null || _spatialNative.View != native)
                    {
                        ReleaseSpatialNative(); RestoreHudRoot(native.transform);
                        _spatialNative = new SpatialNativeLease(native, (RectTransform)_spatialRoot.transform, FindUICamera());
                        _spatialScanFrame = 0;
                    }
                    _spatialNative.Place(new Vector2(0, SpatialPresentationPolicy.InformationY), new Vector2(1100, 950), _spatialBlackCamera);
                }
                else ReleaseSpatialNative();
                _spatialInformationShown = native != null;
                UpdateSpatialInformationHint(false); // Native information remains clean; A/trigger still confirm.
                if (_spatialInformationRevision != TouchRadialInformationRevision)
                { _spatialInformationRevision = TouchRadialInformationRevision; _spatialScanFrame = 0; }
                if (Time.frameCount >= _spatialScanFrame || _touchRadialLayoutDirty)
                {
                    _spatialNative?.CaptureNewNodes();
                    _spatialNodes.Clear(); _spatialRoot.GetComponentsInChildren(true, _spatialNodes);
                    foreach (var node in _spatialNodes) if (node != null && node.gameObject.layer != _spatialLayer) node.gameObject.layer = _spatialLayer;
                    _spatialScanFrame = Time.frameCount + 12;
                }
                if (!_spatialSharedHudLayer)
                {
                    if (_hudBlackCamera != null) _hudBlackCamera.cullingMask &= ~(1 << _spatialLayer);
                    if (_hudWhiteCamera != null) _hudWhiteCamera.cullingMask &= ~(1 << _spatialLayer);
                }
                ConfigureSpatialCamera(_spatialBlackCamera, _spatialBlack, left.depth + 1100);
                ConfigureSpatialCamera(_spatialWhiteCamera, _spatialWhite, left.depth + 1101);
                left.cullingMask &= ~(1 << _spatialLayer); right.cullingMask &= ~(1 << _spatialLayer);
                var spatialCanvas = _spatialRoot.GetComponent<Canvas>();
                if (spatialCanvas.worldCamera != _spatialBlackCamera) spatialCanvas.worldCamera = _spatialBlackCamera;
                if (_touchRadialCanvas != null && _touchRadialCanvas.worldCamera != _spatialBlackCamera) _touchRadialCanvas.worldCamera = _spatialBlackCamera;
                if (_touchProximityCanvas73 != null && _touchProximityCanvas73.worldCamera != _spatialBlackCamera) _touchProximityCanvas73.worldCamera = _spatialBlackCamera;
                Quaternion toTracking = frame.head.Rotation * Quaternion.Inverse(physicalRotation);
                Vector3 position = frame.head.Position + toTracking * ((_spatialWorldCentre - physicalHead) / WorldScale);
                // Publish the retained OpenXR pose directly. A round-trip
                // through scaled scene coordinates needlessly quantized it
                // when large camera translations changed during combat.
                _spatialPose = _touchRadialTrackingReady ? SpatialPackPose(_touchRadialTrackingCentre, _touchRadialTrackingRotation) :
                    SpatialPackPose(position, toTracking * _spatialWorldRotation);
                // Original game information is a fixed central HUD layer, slightly right. The wheel
                // keeps its own physical pose and never drags/scales this card.
                Vector3 information = SpatialInformationLayout.Place(frame.head.Position, frame.head.Rotation, out _spatialInformationWidth, TouchRadialInformationIsAbility);
                // Wider transparent atlas preserves native card content scale.
                _spatialInformationWidth *= (float)SpatialWidth / (SpatialHeight / 2);
                _spatialInformationPose = SpatialPackPose(information, frame.head.Rotation);
                _spatialFrame = Time.frameCount; ++_spatialFrames;
            }
            catch (Exception error) { FailSpatialUi(error.Message); }
            finally { if (_spatialFrame != Time.frameCount) PauseSpatialUi(); }
        }
        static XrPose SpatialPackPose(Vector3 position, Quaternion rotation) => new XrPose {
            x=position.x,y=position.y,z=-position.z,qx=-rotation.x,qy=-rotation.y,qz=rotation.z,qw=rotation.w };
        static void ConfigureSpatialCamera(Camera camera, RenderTexture target, float depth)
        {
            Vector3 position = _spatialCaptureOrigin + Vector3.back * 2000;
            if (camera.transform.position != position || camera.transform.rotation != Quaternion.identity)
                camera.transform.SetPositionAndRotation(position, Quaternion.identity);
            Rect viewport = new Rect(0,0,1,1);
            float size = SpatialHeight * SpatialPixel * .5f, aspect = (float)SpatialWidth / SpatialHeight;
            // A newly copied camera has no target yet, so its potentially
            // custom projection/culling matrices are reset on the first frame.
            bool projectionChanged = camera.targetTexture != target || camera.rect != viewport ||
                camera.nearClipPlane != 1 || camera.farClipPlane != 4000 || !camera.orthographic ||
                camera.orthographicSize != size || camera.aspect != aspect;
            if (camera.targetTexture != target) camera.targetTexture = target;
            if (camera.depth != depth) camera.depth = depth;
            if (camera.cullingMask != (1 << _spatialLayer)) camera.cullingMask = 1 << _spatialLayer;
            if (camera.rect != viewport) camera.rect = viewport;
            if (camera.nearClipPlane != 1) camera.nearClipPlane = 1;
            if (camera.farClipPlane != 4000) camera.farClipPlane = 4000;
            if (!camera.orthographic) camera.orthographic = true;
            if (camera.orthographicSize != size) camera.orthographicSize = size;
            if (camera.aspect != aspect) camera.aspect = aspect;
            if (projectionChanged) { camera.ResetProjectionMatrix(); camera.ResetCullingMatrix();
                camera.nonJitteredProjectionMatrix=camera.projectionMatrix; }
            if (camera.useJitteredProjectionMatrixForTransparentRendering)
                camera.useJitteredProjectionMatrixForTransparentRendering=false;
            if (!camera.enabled) camera.enabled = true;
        }
        internal static bool GetSpatialUi(out RenderTexture black, out RenderTexture white, out XrPose pose, out float width, out float height)
        {
            black=white=null;pose=_spatialPose;width=height=0;
            if (_spatialFrame != Time.frameCount || _spatialBlackCamera == null || _spatialWhiteCamera == null) return false;
            if (_hudRenderFailurePending) { FailSpatialUi("UI renderer changed during the frame"); return false; }
            if (!_renderedFrames.TryGetValue(_spatialBlackCamera.GetInstanceID(),out int b) || b!=Time.frameCount ||
                !_renderedFrames.TryGetValue(_spatialWhiteCamera.GetInstanceID(),out int w) || w!=Time.frameCount)
            { ++_spatialMissedFrames; return false; }
            black=_spatialBlack;white=_spatialWhite;
            _spatialRecoveryAttempts=0;
            width=SpatialWidth * _spatialWorldPixel / WorldScale;
            height=(SpatialHeight / 2) * _spatialWorldPixel / WorldScale;
            return true;
        }
        internal static bool GetSpatialInformation(out XrPose pose, out float size)
        { pose=_spatialInformationPose;size=_spatialInformationWidth;return _spatialInformationShown && _spatialFrame==Time.frameCount; }
        internal static void PauseSpatialUi()
        {
            _spatialFrame=-1;_spatialInformationShown=false;
            if (_spatialBlackCamera!=null && _spatialBlackCamera.enabled)_spatialBlackCamera.enabled=false;
            if (_spatialWhiteCamera!=null && _spatialWhiteCamera.enabled)_spatialWhiteCamera.enabled=false;
        }
        static void ReleaseSpatialNative()
        { _spatialNative?.Dispose();_spatialNative=null;_spatialInformationShown=false;ResetSpatialInformationScroll();UpdateSpatialInformationHint(false); }
        internal static void FailSpatialUi(string reason)
        {
            if (_spatialFault!=null)return;
            StopSpatialUi();_spatialFault=reason;InvalidateHudHierarchy();
            _spatialRetryAt=Time.unscaledTime+1f;
            _log.Error("[ui/spatial] Retaining VR and restoring ordinary HUD wheel: "+reason);
        }
        internal static void StopSpatialUi()
        {
            PauseSpatialUi();ReleaseSpatialNative();
            RenderPipelineManager.beginCameraRendering-=SpatialCameraBegin;
            RenderPipelineManager.endCameraRendering-=SpatialCameraEnd;
            foreach(var pair in _spatialCameraMasks)
                if(pair.Key!=null&&pair.Key.cullingMask==(pair.Value&~(1<<_spatialLayer)))pair.Key.cullingMask=pair.Value;
            _spatialCameraMasks.Clear();
            if(_touchRadialRoot!=null&&IsSpatialNode(_touchRadialRoot.transform))
            {
                _touchRadialRoot.transform.SetParent(null,true);
                _spatialNodes.Clear();_touchRadialRoot.GetComponentsInChildren(true,_spatialNodes);
                foreach(var node in _spatialNodes)if(node!=null&&node.gameObject.layer==_spatialLayer)node.gameObject.layer=5;
            }
            if(_spatialBlackCamera!=null){_spatialBlackCamera.enabled=false;_spatialBlackCamera.targetTexture=null;ReleaseHudVolumeState(_spatialBlackCamera);UnityEngine.Object.Destroy(_spatialBlackCamera.gameObject);}
            if(_spatialWhiteCamera!=null){_spatialWhiteCamera.enabled=false;_spatialWhiteCamera.targetTexture=null;ReleaseHudVolumeState(_spatialWhiteCamera);UnityEngine.Object.Destroy(_spatialWhiteCamera.gameObject);}
            _spatialBlackCamera=_spatialWhiteCamera=null;
            if(_hudBlackCamera==null&&_hudWhiteCamera==null){_hudUiPasses.Clear();if(_spatialSharedHudLayer)_hudLayer=-1;}
            ReleaseHudTarget(ref _spatialBlack);ReleaseHudTarget(ref _spatialWhite);
            DestroySpatialInformationHint();
            if(_spatialRoot!=null)UnityEngine.Object.Destroy(_spatialRoot);
            _spatialRoot=null;_spatialLayer=-1;_spatialScanFrame=0;_spatialSharedHudLayer=false;
        }
    }
}
