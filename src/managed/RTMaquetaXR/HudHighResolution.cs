using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Camera _hudBlackCamera, _hudWhiteCamera;
        static RenderTexture _hudBlackTarget, _hudWhiteTarget;
        static readonly Dictionary<GameObject, int> _hudLayers = new Dictionary<GameObject, int>();
        static readonly List<Transform> _hudNodes = new List<Transform>(1024);
        static readonly List<GameObject> _hudDeadNodes = new List<GameObject>();
        static readonly List<Transform> _hudWorldNodes = new List<Transform>();
        static readonly HashSet<Transform> _hudWorldNodeSet = new HashSet<Transform>();
        static readonly List<Canvas> _hudAddedOvertipCanvases = new List<Canvas>();
        static readonly List<GraphicRaycaster> _hudAddedOvertipRaycasters = new List<GraphicRaycaster>();
        static int _hudLayer = -1, _hudPreparedFrame = -1, _hudLayerCleanupFrame;
        static bool _hudCaptureFault;
        static float _hudRetryAt;
        static int _hudRecoveryAttempts;
        static long _hudRecoveryRevision=-1;
        static bool _hudPointerWasIsolated;
        static string _hudCaptureError;
        static HudPanelGeometry _hudCaptureGeometry;
        static FieldInfo[] _hudDataFields;

        internal static bool IsHudCaptureCamera(Camera camera) => camera != null &&
            (camera == _hudBlackCamera || camera == _hudWhiteCamera);
        internal static bool HudCaptureActive => _hudPreparedFrame == Time.frameCount && _hudLayer >= 0;
        internal static string HudCaptureDescription => !_cfg.uiFullResolution && !_worldInformationVisible70 ? ModLocalization.Text("Earlier path · rendered inside each eye") :
            _hudCaptureFault ? ModLocalization.Format("Capture suspended: {0}", ModLocalization.DiagnosticText(_hudCaptureError)) :
            _hudBlackTarget == null ? ModLocalization.Text("Preparing independent HUD") :
            ModLocalization.Format("HUD {0} × {1} · independent of DLSS", _hudBlackTarget.width, _hudBlackTarget.height);

        // Called at the END of tabletop LateUpdate, after overtips and helper poses.
        // Two backgrounds preserve the game's original blend/mask/stencil behavior.
        // They are ordinary UI-only SRP cameras, not recursive Camera.Render calls.
        internal static void PrepareHudCapture(Camera left, Camera right, Vector3 head, Quaternion orientation)
        {
            if(_hudRecoveryRevision!=SpatialGameContextRevision)
            {_hudRecoveryRevision=SpatialGameContextRevision;_hudRecoveryAttempts=0;_hudRetryAt=0;}
            bool fullHud = _cfg.uiFullResolution;
            bool informationOnly = !fullHud && _worldInformationVisible70 && _worldInformationRoot70 != null;
            if ((!fullHud && !informationOnly) || !_active || !_attached || _modeFlat || (fullHud && _uiRoot == null) ||
                left == null || right == null)
            { StopHudCapture(); return; }
            if (_hudCaptureFault)
            {
                if (_hudRecoveryAttempts >= 3 || Time.unscaledTime < _hudRetryAt) return;
                ++_hudRecoveryAttempts; _hudCaptureFault = false;
                _log.Log("[ui/full-resolution] Retrying capture after transition, attempt " + _hudRecoveryAttempts);
            }
            long started = DiagnosticTimestamp();
            try
            {
                if (!OpenXR.HudCompositorAvailable) throw new InvalidOperationException("HUD compositor unavailable");
                EnsureHudRenderIsolation();
                EnsureHudHierarchyHooks();
                if (_hudLayer < 0) ChooseHudLayer();
                if (_hudBlackCamera == null)
                {
                    Camera original = FindUICamera();
                    if (original == null || _camDataType == null) throw new InvalidOperationException("UICamera not ready");
                    _hudBlackCamera = CreateHudCamera(original, "RTMaquetaXR HUD black", Color.black);
                    _hudWhiteCamera = CreateHudCamera(original, "RTMaquetaXR HUD white", Color.white);
                }
                float far = HudRenderLength(Mathf.Min(left.farClipPlane, right.farClipPlane));
                _hudCaptureGeometry = HudPanelLayout.CoverViews(HudPresentationDistance, HudLayoutWorldScale, far,
                    _hudNativeReferenceSize.x, _hudViewPlanes);
                int width = HudRasterPolicy.Width(_cfg.uiRasterMode);
                // Small sub-pixel pose changes must not replace GPU targets.
                int height = Mathf.Max(256, Mathf.CeilToInt(width / _hudCaptureGeometry.Aspect / 64) * 64);
                if (_hudBlackTarget != null && _hudBlackTarget.width == width &&
                    Mathf.Abs(_hudBlackTarget.height - height) <= 64) height = _hudBlackTarget.height;
                int maximum = Mathf.Min(4096, SystemInfo.maxTextureSize);
                if (width > maximum || height > maximum)
                {
                    float factor = Mathf.Min((float)maximum / width, (float)maximum / height);
                    width = Mathf.Max(256, Mathf.RoundToInt(width * factor));
                    height = Mathf.Max(256, Mathf.RoundToInt(height * factor));
                }
                EnsureHudTarget(ref _hudBlackTarget, width, height, "RTMaquetaXR HUD black raster");
                EnsureHudTarget(ref _hudWhiteTarget, width, height, "RTMaquetaXR HUD white raster");
                bool refresh = HudHierarchyNeedsRefresh();
                if (refresh)
                {
                    BeginHudHierarchyRefresh();
                    if (fullHud)
                    {
                        IsolateHudRoot(_uiRoot);
                        IsolateHudRoot(_touchGestureRoot == null ? null : _touchGestureRoot.transform);
                        IsolateHudRoot(_liveRoot == null ? null : _liveRoot.transform);
                        IsolateHudRoot(_contextHintRoot == null ? null : _contextHintRoot.transform);
                        if (_spatialFault != null) IsolateHudRoot(_touchRadialRoot == null ? null : _touchRadialRoot.transform);
                    }
                    IsolateHudRoot(_worldInformationRoot70 == null ? null : _worldInformationRoot70.transform);
                }
                // A world aim marker keeps its actual stereo depth. Only a
                // reticle over PC chrome belongs to the composited UI plane.
                if (fullHud && _touchOverUi&&!_touchWorldHit68)
                {
                    IsolateHudRoot(_touchPointerRoot == null ? null : _touchPointerRoot.transform);
                    _hudPointerWasIsolated = _touchPointerRoot != null;
                }
                else if (_hudPointerWasIsolated)
                {
                    RestoreHudRoot(_touchPointerRoot == null ? null : _touchPointerRoot.transform);
                    _hudPointerWasIsolated = false;
                }
                if (refresh) EndHudHierarchyRefresh();
                float helperDepth = 0;
                Vector3 captureHead = _hudStableSpace ? Vector3.zero : head;
                Quaternion captureRotation = _hudStableSpace ? Quaternion.identity : orientation;
                Vector3 forward = captureRotation * Vector3.forward;
                if (fullHud)
                {
                    IncludeHudHelperDepth(ref helperDepth, _liveRoot, CurrentLiveOption() != null, captureHead, forward);
                    IncludeHudHelperDepth(ref helperDepth, _touchGestureRoot, _touchGestureShown, captureHead, forward);
                    IncludeHudHelperDepth(ref helperDepth, _contextHintRoot, _contextHintVisible, captureHead, forward);
                    IncludeHudHelperDepth(ref helperDepth, _touchPointerRoot, _touchOverUi && !_touchWorldHit68 && _touchReticleShown, captureHead, forward);
                    IncludeHudHelperDepth(ref helperDepth, _touchRadialRoot, _touchRadialShown, captureHead, forward);
                    IncludeTouchRadialBoundsDepth(ref helperDepth, captureHead, forward);
                }
                IncludeHudHelperDepth(ref helperDepth, _worldInformationRoot70, _worldInformationVisible70, captureHead, forward);
                float captureFar = HudCapturePolicy.FarClip(_hudCaptureGeometry.Distance,
                    HudPresentationDistance * HudLayoutWorldScale, helperDepth);
                ConfigureHudCamera(_hudBlackCamera, _hudBlackTarget, captureHead, captureRotation, left.depth + 1000, captureFar);
                ConfigureHudCamera(_hudWhiteCamera, _hudWhiteTarget, captureHead, captureRotation, left.depth + 1001, captureFar);
                // Setting is applied after Camera.CopyFrom for both game eyes.
                // UI enters only the independent capture, never DLSS/FSR/TAA.
                left.cullingMask &= ~(1 << _hudLayer); right.cullingMask &= ~(1 << _hudLayer);
                _hudPreparedFrame = Time.frameCount;
                if (Time.frameCount >= _hudLayerCleanupFrame)
                {
                    _hudLayerCleanupFrame = Time.frameCount + 300; _hudDeadNodes.Clear();
                    foreach (var pair in _hudLayers) if (pair.Key == null) _hudDeadNodes.Add(pair.Key);
                    foreach (var item in _hudDeadNodes) _hudLayers.Remove(item);
                }
            }
            catch (Exception error)
            {
                StopHudCapture(); _hudCaptureFault = true; _hudCaptureError = error.Message;
                _hudRetryAt = Time.unscaledTime + 1f;
                _log.Error("[ui/full-resolution] Capture unavailable; original stereo HUD retained: " + error.Message);
            }
            finally { RecordModStage("FullResolutionHudPreparation", started); }
        }

        static void ChooseHudLayer()
        {
            if (UiLayerOwnership.Hud >= 0) { _hudLayer = UiLayerOwnership.Hud; return; }
            uint used = 0, named = 0;
            // Once per attachment, including inactive scene objects. Assets do not
            // count as scene occupants. Existing colliders/renderers are untouched.
            foreach (var item in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (item != null) used |= 1u << item.gameObject.layer;
            for (int candidate = 24; candidate < 32; ++candidate)
                if (!string.IsNullOrEmpty(LayerMask.LayerToName(candidate))) named |= 1u << candidate;
            int slot = UiLayerOwnership.Find(used, named, 24);
            if (slot >= 0) { _hudLayer = UiLayerOwnership.Hud = slot; return; }
            throw new InvalidOperationException("No free layer for HUD isolation");
        }

        static Camera CreateHudCamera(Camera original, string name, Color clear)
        {
            var root = new GameObject(name, typeof(Camera));
            try
            {
            UnityEngine.Object.DontDestroyOnLoad(root);
            var camera = root.GetComponent<Camera>(); camera.enabled = false;
            camera.CopyFrom(original);
            camera.enabled = false; camera.targetTexture = null;
            var sourceData = original.GetComponent(_camDataType);
            if (sourceData == null) throw new InvalidOperationException("UICamera data unavailable");
            var data = root.AddComponent(_camDataType);
            if (_hudDataFields == null)
                _hudDataFields = _camDataType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var field in _hudDataFields)
            {
                if (!field.Name.StartsWith("m_") || field.Name == "m_Camera" || field.Name == "m_Cameras" ||
                    field.Name == "m_VolumeStack" || field.Name == "m_TargetDepthTexture") continue;
                field.SetValue(data, field.GetValue(sourceData));
            }
            SetHudCameraField(data, "m_CameraType", 0);
            SetHudCameraField(data, "m_RenderPostProcessing", false);
            SetHudCameraField(data, "m_Antialiasing", 0);
            SetHudCameraField(data, "m_IsLightingEnabled", false);
            SetHudCameraField(data, "m_RenderShadows", false);
            SetHudCameraField(data, "m_AllowIndirectRendering", false);
            SetHudCameraField(data, "m_AllowRenderScaling", false);
            SetHudCameraField(data, "m_Dithering", false);
            CreateHudVolumeState(camera, data);
            PrepareHudRendererPasses(data);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = clear;
            camera.allowHDR = false; camera.allowMSAA = false; camera.allowDynamicResolution = false;
            camera.useOcclusionCulling = false; camera.stereoTargetEye = StereoTargetEyeMask.None;
            return camera;
            }
            catch { ReleaseHudVolumeState(root.GetComponent<Camera>()); UnityEngine.Object.Destroy(root); throw; }
        }

        static void SetHudCameraField(Component data, string name, object value)
        {
            var field = _camDataType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException("HUD camera contract: " + name);
            field.SetValue(data, field.FieldType.IsEnum ? Enum.ToObject(field.FieldType, value) : value);
        }

        static void EnsureHudTarget(ref RenderTexture target, int width, int height, string name)
        {
            if (target != null && target.width == width && target.height == height && target.IsCreated()) return;
            ReleaseHudTarget(ref target);
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) {
                name = name, antiAliasing = 1, useMipMap = false, autoGenerateMips = false, filterMode = FilterMode.Bilinear
            };
            if (!target.Create()) throw new InvalidOperationException("No se pudo crear superficie HUD");
            OpenXR.PrepareTexture(target);
        }

        static void IncludeHudHelperDepth(ref float depth, GameObject root, bool visible, Vector3 head, Vector3 forward)
        {
            if (!visible || root == null) return;
            depth = HudCapturePolicy.IncludeHelper(depth, ToPoint(root.transform.position), ToPoint(head), ToPoint(forward), visible);
        }

        static void ConfigureHudCamera(Camera camera, RenderTexture target, Vector3 head, Quaternion rotation, float depth, float far)
        {
            var plane = _hudCaptureGeometry;
            bool poseChanged = camera.transform.position != head || camera.transform.rotation != rotation;
            if (poseChanged) camera.transform.SetPositionAndRotation(head, rotation);
            bool targetChanged = camera.targetTexture != target;
            if (targetChanged) camera.targetTexture = target;
            if (camera.depth != depth) camera.depth = depth;
            if (camera.cullingMask != (1 << _hudLayer)) camera.cullingMask = 1 << _hudLayer;
            Rect viewport = new Rect(0, 0, 1, 1);
            if (camera.rect != viewport) camera.rect = viewport;
            if (camera.orthographic) camera.orthographic = false;
            if (camera.aspect != plane.Aspect) camera.aspect = plane.Aspect;
            float near = Mathf.Max(.005f, HudLayoutWorldScale * .005f);
            if (camera.nearClipPlane != near) camera.nearClipPlane = near;
            if (camera.farClipPlane != far) camera.farClipPlane = far;
            if (camera.fieldOfView != plane.VerticalFov) camera.fieldOfView = plane.VerticalFov;
            float ratio = camera.nearClipPlane / plane.Distance;
            var projection = Matrix4x4.Frustum(
                (plane.CentreX - plane.Width * .5f) * ratio, (plane.CentreX + plane.Width * .5f) * ratio,
                (plane.CentreY - plane.Height * .5f) * ratio, (plane.CentreY + plane.Height * .5f) * ratio,
                camera.nearClipPlane, camera.farClipPlane);
            bool projectionChanged = !camera.projectionMatrix.Equals(projection);
            if (projectionChanged) camera.projectionMatrix = projection;
            // Preserve the existing automatic culling contract, including any
            // custom matrix inherited from the original native UI camera.
            camera.ResetCullingMatrix();
            if (!camera.enabled) camera.enabled = true;
        }

        static void IsolateHudRoot(Transform root)
        {
            if (root == null) return;
            _hudNodes.Clear(); root.GetComponentsInChildren(true, _hudNodes);
            IsolateHudNodes();
        }

        static void IsolateHudNodes()
        {
            foreach (var node in _hudNodes)
            {
                if (node == null) continue;
                if (IsSpatialNode(node)) continue;
                var go = node.gameObject;
                bool worldOvertip = _hudWorldNodeSet.Contains(node);
                if (worldOvertip)
                {
                    _hudCurrentMembers.Remove(go);
                    if (_hudLayers.TryGetValue(go, out int original)) { go.layer = original; _hudLayers.Remove(go); }
                    if (node == _overtipsRoot)
                    {
                        var canvas=node.GetComponent<Canvas>();
                        if(canvas==null)
                        {
                            canvas = go.AddComponent<Canvas>();_hudAddedOvertipCanvases.Add(canvas);
                            canvas.renderMode = RenderMode.WorldSpace;
                            // Physical overtip picking is routed by eventCamera;
                            // do not change the camera shared with DynamicCanvas.
                            canvas.worldCamera = _pickCam; canvas.overrideSorting = true; canvas.sortingOrder = 32760;
                        }
                        // Moving native Graphics below a new root Canvas also
                        // moves them out of the parent GraphicRegistry. Give
                        // that Canvas its own native EventSystem surface.
                        if(go.GetComponent<UnityEngine.EventSystems.BaseRaycaster>()==null)
                        {
                            var raycaster=go.AddComponent<GraphicRaycaster>();
                            raycaster.ignoreReversedGraphics=true;
                            _hudAddedOvertipRaycasters.Add(raycaster);
                        }
                    }
                    TrackHudHierarchyNode(node);
                    continue;
                }
                // A pooled world marker may become a PC panel. Its original
                // layer must be restored before entering the other ledger.
                RemoveWorldHudLayer(go);
                int layer = go.layer;
                _hudCurrentMembers.Add(go);
                if (!_hudLayers.ContainsKey(go))
                {
                    int ancestor = 5;
                    for (Transform parent = node.parent; parent != null; parent = parent.parent)
                        if (_hudLayers.TryGetValue(parent.gameObject, out int known)) { ancestor = known; break; }
                    _hudLayers.Add(go, UiLayerOwnership.NativeLayer(layer, _hudLayer, ancestor));
                }
                if (layer != _hudLayer) go.layer = _hudLayer;
                TrackHudHierarchyNode(node);
            }
        }

        static void RestoreHudRoot(Transform root)
        {
            if (root == null) return;
            _hudNodes.Clear(); root.GetComponentsInChildren(true, _hudNodes);
            foreach (var node in _hudNodes)
                if (node != null && _hudLayers.TryGetValue(node.gameObject, out int layer))
                { node.gameObject.layer = layer; _hudLayers.Remove(node.gameObject); _hudCurrentMembers.Remove(node.gameObject); }
        }

        internal static void FailHudCapture(string reason)
        {
            StopHudCapture(); _hudCaptureFault = true; _hudCaptureError = reason;
            _hudRetryAt = Time.unscaledTime + 1f;
            _log.Error("[ui/full-resolution] Original HUD restored: " + reason);
        }

        internal static bool GetHudCapture(out RenderTexture black, out RenderTexture white, out HudPanelGeometry geometry)
        {
            black = white = null; geometry = default(HudPanelGeometry);
            if (!HudCaptureActive || _hudBlackCamera == null || _hudWhiteCamera == null) return false;
            if (_hudRenderFailurePending)
            { FailHudCapture("HUD renderer changed during the frame"); return false; }
            if (!_renderedFrames.TryGetValue(_hudBlackCamera.GetInstanceID(), out int b) || b != Time.frameCount ||
                !_renderedFrames.TryGetValue(_hudWhiteCamera.GetInstanceID(), out int w) || w != Time.frameCount)
            {
                // Never reuse a HUD from another frame while the game underneath moves.
                StopHudCapture(); _hudCaptureFault = true; _hudCaptureError = "par de capturas UI incompleto";
                _hudRetryAt = Time.unscaledTime + 1f;
                _log.Error("[ui/full-resolution] Incomplete same-frame background pair; restoring the ordinary HUD");
                return false;
            }
            black = _hudBlackTarget; white = _hudWhiteTarget; geometry = _hudCaptureGeometry;
            _hudRecoveryAttempts = 0;
            geometry.Distance /= HudLayoutWorldScale; geometry.Width /= HudLayoutWorldScale; geometry.Height /= HudLayoutWorldScale;
            geometry.CentreX /= HudLayoutWorldScale; geometry.CentreY /= HudLayoutWorldScale;
            return true;
        }

        static void ReleaseHudTarget(ref RenderTexture target)
        {
            if (target == null) return;
            // Unity destroys cameras at the end of the frame. A destroyed or
            // resized capture may still reference this RT until then; releasing
            // it first leaves Camera.targetTexture pointing at a dead surface.
            DetachHudTarget(_hudBlackCamera, target); DetachHudTarget(_hudWhiteCamera, target);
            DetachHudTarget(_spatialBlackCamera, target); DetachHudTarget(_spatialWhiteCamera, target);
            OpenXR.ForgetTexture(target); target.Release(); UnityEngine.Object.Destroy(target); target = null;
        }
        static void DetachHudTarget(Camera camera, RenderTexture target)
        {
            if (camera != null && camera.targetTexture == target) camera.targetTexture = null;
        }

        internal static void StopHudCapture()
        {
            RestoreWorldHudResolution();
            ClearHudStableSpace();
            _hudPreparedFrame = -1;
            _hudCaptureFault = false; _hudCaptureError = null;
            _hudPointerWasIsolated = false;
            ClearHudHierarchyCache();
            _hudWorldNodes.Clear(); _hudWorldNodeSet.Clear();
            _hudRenderFailurePending = false;
            // Spatial wheels keep using their UI-only renderer when the user
            // disables full-resolution game HUD; retain its pass identities.
            if (_spatialBlackCamera == null && _spatialWhiteCamera == null) _hudUiPasses.Clear();
            if (_hudBlackCamera != null) { _hudBlackCamera.enabled = false; _hudBlackCamera.targetTexture = null; ReleaseHudVolumeState(_hudBlackCamera); UnityEngine.Object.Destroy(_hudBlackCamera.gameObject); }
            if (_hudWhiteCamera != null) { _hudWhiteCamera.enabled = false; _hudWhiteCamera.targetTexture = null; ReleaseHudVolumeState(_hudWhiteCamera); UnityEngine.Object.Destroy(_hudWhiteCamera.gameObject); }
            _hudBlackCamera = _hudWhiteCamera = null;
            foreach (var item in _hudLayers) if (item.Key != null && item.Key.layer == _hudLayer) item.Key.layer = item.Value;
            _hudLayers.Clear(); _hudLayer = _spatialSharedHudLayer && _spatialRoot != null ? _spatialLayer : -1; _hudNodes.Clear(); _hudDeadNodes.Clear();
            foreach (var raycaster in _hudAddedOvertipRaycasters) if (raycaster != null) UnityEngine.Object.Destroy(raycaster);
            _hudAddedOvertipRaycasters.Clear();
            foreach (var canvas in _hudAddedOvertipCanvases) if (canvas != null) UnityEngine.Object.Destroy(canvas);
            _hudAddedOvertipCanvases.Clear();
            ReleaseHudTarget(ref _hudBlackTarget); ReleaseHudTarget(ref _hudWhiteTarget);
        }
    }
}
