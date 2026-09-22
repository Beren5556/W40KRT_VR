using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        sealed class ForcedVisibilityGroup
        {
            public object Service;
            public CullingGroup Group;
            public Camera OriginalCamera;
            public bool Retargeted, GroupEnabled, CheatDisabled;
            public int TotalCount, LiveTargets, CulledTargets, RendererReferences, UniqueRenderers;
            public int Events, VisibleEvents, SyncedEvents;
        }

        static readonly List<ForcedVisibilityGroup> _forcedVisibilityGroups = new List<ForcedVisibilityGroup>();
        static readonly Point3[] _forcedVisibilityCorners = new Point3[16];
        static readonly Vector3[] _forcedVisibilityWorldCorners = new Vector3[16];
        static readonly Measurement _forcedVisibilityCullMs = new Measurement();
        static Func<object, CullingGroup> _forcedVisibilityGetGroup;
        static Func<object, int> _forcedVisibilityGetCount;
        static Func<object, bool> _forcedVisibilityGetCheatDisabled;
        static FieldInfo _forcedVisibilityTargets, _forcedVisibilityCulled, _forcedVisibilityRenderers;
        static bool _forcedVisibilityHook, _forcedVisibilityEventHook, _forcedVisibilityDisposeHook, _forcedVisibilityFailed;
        static Camera _forcedVisibilityCamera, _forcedVisibilitySource;
        static StereoVisibilityBounds _forcedVisibilityBounds;
        static int _forcedVisibilityPreparedFrame = -1, _forcedVisibilityCulledFrame = -1;
        static int _forcedVisibilityCullCalls, _forcedVisibilityEmptyFrames;
        static long _forcedVisibilityCullCallsTotal;
        static float _forcedVisibilityNextInventory;
        static string _forcedVisibilityStatus = "not-installed", _forcedVisibilityError;
        enum ForcedMaskProbeState { Unstarted, Baseline, EmptyMask, Passed, Fallback }
        const int ForcedMaskProbeAttemptLimit = 8;
        const float ForcedMaskProbeTimeoutSeconds = 5;
        static ForcedMaskProbeState _forcedMaskProbeState;
        static Camera _forcedMaskProbeCamera;
        static CullingGroup _forcedMaskProbeGroup;
        static readonly BoundingSphere[] _forcedMaskProbeSpheres = new BoundingSphere[2];
        static readonly Measurement _forcedMaskProbeMs = new Measurement();
        static int _forcedMaskProbeLastCullFrame = -1, _forcedMaskProbeLastReadFrame = -1;
        static int _forcedMaskProbePhaseCulls, _forcedMaskProbeTotalCulls, _forcedMaskProbeTransitionBits;
        static float _forcedMaskProbeStarted;
        static uint _forcedMaskProbeSourceMask, _forcedVisibilityLastMask;
        static bool? _forcedMaskProbeBaseline0, _forcedMaskProbeBaseline1, _forcedMaskProbeEmpty0, _forcedMaskProbeEmpty1;
        static string _forcedMaskProbeError;

        static void InstallForcedVisibilitySync()
        {
            // A new VR run may retry a previous failure only after restoring any
            // camera targets retained by the preceding run.
            if (!RestoreForcedVisibilityGroups()) return;
            _forcedVisibilityFailed = false;
            _forcedVisibilityPreparedFrame = _forcedVisibilityCulledFrame = -1;
            _forcedVisibilityNextInventory = 0;
            ResetForcedMaskProbe();
            try
            {
                if (!_forcedVisibilityHook)
                {
                    var service = AccessTools.TypeByName("Kingmaker.Visual.Particles.ForcedCulling.ForcedCullingService");
                    var target = AccessTools.TypeByName("Kingmaker.Visual.Particles.ForcedCulling.ForcedCullingRadius");
                    if (service == null || target == null) throw new TypeLoadException("ForcedCulling types");
                    var group = AccessTools.Field(service, "m_CullingGroup");
                    var count = AccessTools.Field(service, "m_TotalCount");
                    var cheat = AccessTools.Field(service, "m_CheatDisabled");
                    _forcedVisibilityTargets = AccessTools.Field(service, "m_SphereToObject");
                    _forcedVisibilityCulled = AccessTools.Field(target, "m_IsCulled");
                    _forcedVisibilityRenderers = AccessTools.Field(target, "m_Renderers");
                    if (group == null || group.FieldType != typeof(CullingGroup) || count == null || count.FieldType != typeof(int) ||
                        cheat == null || cheat.FieldType != typeof(bool) ||
                        _forcedVisibilityTargets == null || !_forcedVisibilityTargets.FieldType.IsArray ||
                        _forcedVisibilityTargets.FieldType.GetElementType() != target ||
                        _forcedVisibilityCulled == null || _forcedVisibilityCulled.FieldType != typeof(bool) ||
                        _forcedVisibilityRenderers == null || !typeof(IList).IsAssignableFrom(_forcedVisibilityRenderers.FieldType))
                        throw new MissingFieldException("Unexpected ForcedCulling fields");
                    _forcedVisibilityGetGroup = ForcedVisibilityFieldGetter<CullingGroup>(group);
                    _forcedVisibilityGetCount = ForcedVisibilityFieldGetter<int>(count);
                    _forcedVisibilityGetCheatDisabled = ForcedVisibilityFieldGetter<bool>(cheat);
                    var update = AccessTools.DeclaredMethod(service, "DoUpdate", Type.EmptyTypes);
                    var changed = AccessTools.DeclaredMethod(service, "OnCullingStateChange", new[] { typeof(CullingGroupEvent) });
                    if (update == null || update.IsStatic || update.ReturnType != typeof(void) ||
                        changed == null || changed.IsStatic || changed.ReturnType != typeof(void))
                        throw new MissingMethodException("Unexpected ForcedCulling callbacks");
                    _harmony.Patch(update, postfix: new HarmonyMethod(typeof(Main), nameof(ForcedVisibilityUpdatePostfix)));
                    _forcedVisibilityHook = true;
                }
                var serviceType = AccessTools.TypeByName("Kingmaker.Visual.Particles.ForcedCulling.ForcedCullingService");
                if (!_forcedVisibilityEventHook)
                {
                    var changed = AccessTools.DeclaredMethod(serviceType, "OnCullingStateChange", new[] { typeof(CullingGroupEvent) });
                    _harmony.Patch(changed, postfix: new HarmonyMethod(typeof(Main), nameof(ForcedVisibilityEventPostfix)));
                    _forcedVisibilityEventHook = true;
                }
                if (!_forcedVisibilityDisposeHook)
                {
                    var dispose = AccessTools.DeclaredMethod(serviceType, "Dispose", Type.EmptyTypes);
                    if (dispose == null || dispose.IsStatic || dispose.ReturnType != typeof(void))
                        throw new MissingMethodException("Unexpected ForcedCullingService.Dispose");
                    _harmony.Patch(dispose, prefix: new HarmonyMethod(typeof(Main), nameof(ForcedVisibilityDisposePrefix)),
                        postfix: new HarmonyMethod(typeof(Main), nameof(ForcedVisibilityDisposePostfix)));
                    _forcedVisibilityDisposeHook = true;
                }
                _forcedVisibilityStatus = "waiting-for-service";
                _log.Log("[visibility] ForcedCulling synchronization available; helper performs culling only for the union of both eye frusta");
            }
            catch (Exception e) { FailForcedVisibility("install", e); }
        }

        static Func<object, T> ForcedVisibilityFieldGetter<T>(FieldInfo field)
        {
            var method = new DynamicMethod("RTMaquetaXR_Visibility_" + field.Name, typeof(T), new[] { typeof(object) }, typeof(Main), true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, field.DeclaringType); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
            return (Func<object, T>)method.CreateDelegate(typeof(Func<object, T>));
        }

        static bool ForcedVisibilityEnabled => _active && _attached && !_modeFlat && _cfg.syncForcedVisibility && _cfg.skipDesktopWorld &&
            _desktopMirrorHook && !_desktopFallback && _forcedVisibilityHook && _forcedVisibilityDisposeHook && !_forcedVisibilityFailed;

        static void ForcedVisibilityUpdatePostfix(object __instance)
        {
            if (!ForcedVisibilityEnabled) return;
            try
            {
                var group = _forcedVisibilityGetGroup(__instance);
                for (int i = 0; i < _forcedVisibilityGroups.Count; ++i)
                {
                    var item = _forcedVisibilityGroups[i];
                    if (!ReferenceEquals(item.Service, __instance)) continue;
                    if (ReferenceEquals(item.Group, group)) return;
                    if (!RestoreForcedVisibilityGroup(item)) return;
                    _forcedVisibilityGroups.RemoveAt(i);
                    break;
                }
                if (group == null) return;
                _forcedVisibilityGroups.Add(new ForcedVisibilityGroup {
                    Service = __instance, Group = group, OriginalCamera = group.targetCamera
                });
                _forcedVisibilityNextInventory = 0;
            }
            catch (Exception e) { FailForcedVisibility("capture-service", e); }
        }

        static void ForcedVisibilityEventPostfix(object __instance, CullingGroupEvent __0)
        {
            // No reflection, allocations or scene changes in the native culling callback.
            if (!ForcedVisibilityEnabled) return;
            for (int i = 0; i < _forcedVisibilityGroups.Count; ++i)
            {
                var item = _forcedVisibilityGroups[i];
                if (!ReferenceEquals(item.Service, __instance)) continue;
                ++item.Events;
                if (__0.isVisible) ++item.VisibleEvents;
                if (item.Retargeted) ++item.SyncedEvents;
                return;
            }
        }

        static void ForcedVisibilityDisposePrefix(object __instance)
        {
            // This also runs while VR is stopped: restore before the native group
            // is disposed, so no stale target needs to be read after disposal.
            foreach (var item in _forcedVisibilityGroups)
                if (ReferenceEquals(item.Service, __instance)) RestoreForcedVisibilityGroup(item);
        }

        static void ForcedVisibilityDisposePostfix(object __instance)
        {
            // The original completed its native disposal. If it throws, Harmony
            // leaves the record available for the normal restoration path.
            for (int i = _forcedVisibilityGroups.Count - 1; i >= 0; --i)
                if (ReferenceEquals(_forcedVisibilityGroups[i].Service, __instance)) _forcedVisibilityGroups.RemoveAt(i);
        }

        internal static void PrepareForcedVisibility(Camera source, Camera left, Camera right)
        {
            _forcedVisibilityPreparedFrame = -1;
            if (!ForcedVisibilityEnabled)
            {
                SuspendForcedMaskProbe();
                RestoreForcedVisibilityGroups();
                if (!_forcedVisibilityFailed)
                    _forcedVisibilityStatus = !_cfg.syncForcedVisibility ? "disabled-by-setting" :
                        !_cfg.skipDesktopWorld || _desktopFallback ? "desktop-rendering" : "inactive";
                return;
            }
            if (source == null || source != _attachedCam || source.targetTexture != null || left == null || right == null ||
                source == left || source == right || left == right)
            {
                SuspendForcedMaskProbe();
                RestoreForcedVisibilityGroups();
                if (!_forcedVisibilityFailed) _forcedVisibilityStatus = "source-ineligible";
                return;
            }
            try
            {
                // Results are read in the following Prepare, before any new
                // culling. The probe camera itself keeps a fixed pose throughout.
                AdvanceForcedMaskProbe();
                int eligible = 0;
                foreach (var item in _forcedVisibilityGroups)
                {
                    var current = item.Group.targetCamera;
                    if (current != _forcedVisibilityCamera || _forcedVisibilityCamera == null)
                    {
                        // A game update or another mod owns this newer choice.
                        item.OriginalCamera = current;
                        item.Retargeted = false;
                    }
                    item.TotalCount = _forcedVisibilityGetCount(item.Service);
                    item.GroupEnabled = item.Group.enabled;
                    item.CheatDisabled = _forcedVisibilityGetCheatDisabled(item.Service);
                    if (item.OriginalCamera != source || item.TotalCount <= 0 || !item.GroupEnabled || item.CheatDisabled)
                    {
                        if (!RestoreForcedVisibilityGroup(item)) return;
                        continue;
                    }
                    ++eligible;
                }
                RefreshForcedVisibilityInventory();
                if (eligible == 0)
                {
                    SuspendForcedMaskProbe();
                    ++_forcedVisibilityEmptyFrames;
                    _forcedVisibilityStatus = "no-source-targets";
                    return;
                }

                var center = (left.transform.position + right.transform.position) * .5f;
                var rotation = Quaternion.Slerp(left.transform.rotation, right.transform.rotation, .5f);
                var inverse = Quaternion.Inverse(rotation);
                float shift = Mathf.Max(Vector3.Distance(left.transform.position, right.transform.position), .01f);
                if (!FillForcedVisibilityCorners(left, center, inverse, 0) || !FillForcedVisibilityCorners(right, center, inverse, 8) ||
                    !StereoVisibilityVolume.TryFit(_forcedVisibilityCorners, shift, out var bounds) || !ForcedVisibilityContainsCorners(bounds, shift))
                    throw new InvalidOperationException("Cannot conservatively enclose both eye frusta");
                if (_forcedVisibilityCamera == null)
                {
                    var go = new GameObject("RTMaquetaXR.VisibilityCull") { hideFlags = HideFlags.HideAndDontSave };
                    _forcedVisibilityCamera = go.AddComponent<Camera>();
                    _forcedVisibilityCamera.hideFlags = HideFlags.HideAndDontSave;
                    _forcedVisibilityCamera.enabled = false;
                }
                var helper = _forcedVisibilityCamera;
                helper.enabled = false;
                helper.targetTexture = null;
                helper.orthographic = false;
                helper.useOcclusionCulling = false;
                helper.allowHDR = helper.allowMSAA = helper.allowDynamicResolution = false;
                helper.cullingMask = source.cullingMask;
                helper.transform.SetPositionAndRotation(center - rotation * Vector3.forward * shift, rotation);
                helper.nearClipPlane = bounds.Near;
                helper.farClipPlane = bounds.Far;
                var projection = Matrix4x4.Frustum(bounds.Left * bounds.Near, bounds.Right * bounds.Near,
                    bounds.Bottom * bounds.Near, bounds.Top * bounds.Near, bounds.Near, bounds.Far);
                var view = helper.worldToCameraMatrix;
                var culling = projection * view;
                if (!ForcedVisibilityFinite(projection) || !ForcedVisibilityFinite(view) || !ForcedVisibilityFinite(culling))
                    throw new InvalidOperationException("Non-finite visibility helper matrices");
                helper.projectionMatrix = helper.nonJitteredProjectionMatrix = projection;
                helper.cullingMatrix = culling;
                _forcedVisibilityBounds = bounds;
                if (_forcedMaskProbeState == ForcedMaskProbeState.Unstarted)
                    BeginForcedMaskProbe(helper);
                foreach (var item in _forcedVisibilityGroups)
                {
                    if (item.OriginalCamera != source || item.TotalCount <= 0 || !item.GroupEnabled || item.CheatDisabled) continue;
                    item.Retargeted = true;
                    item.Group.targetCamera = helper;
                }
                _forcedVisibilitySource = source;
                _forcedVisibilityPreparedFrame = Time.frameCount;
                _forcedVisibilityStatus = "prepared";
            }
            catch (Exception e) { FailForcedVisibility("prepare", e); }
        }

        static bool FillForcedVisibilityCorners(Camera eye, Vector3 center, Quaternion inverse, int offset)
        {
            var p = eye.projectionMatrix;
            float near = eye.nearClipPlane, far = eye.farClipPlane;
            // Only the ordinary perspective matrices installed by Runner.Configure.
            if (!(p.m00 > 0 && p.m11 > 0 && near > 0 && far > near) ||
                Mathf.Abs(p.m01) > 1e-6f || Mathf.Abs(p.m10) > 1e-6f || Mathf.Abs(p.m03) > 1e-6f || Mathf.Abs(p.m13) > 1e-6f ||
                Mathf.Abs(p.m20) > 1e-6f || Mathf.Abs(p.m21) > 1e-6f || Mathf.Abs(p.m30) > 1e-6f || Mathf.Abs(p.m31) > 1e-6f ||
                Mathf.Abs(p.m32 + 1) > 1e-6f || Mathf.Abs(p.m33) > 1e-6f) return false;
            var toWorld = eye.cameraToWorldMatrix;
            for (int depthIndex = 0; depthIndex < 2; ++depthIndex)
            {
                float depth = depthIndex == 0 ? near : far;
                for (int y = 0; y < 2; ++y)
                    for (int x = 0; x < 2; ++x)
                    {
                        // Unity's view space looks down -Z; the fitting helper uses +Z.
                        var local = new Vector3(((x == 0 ? -1 : 1) + p.m02) * depth / p.m00,
                            ((y == 0 ? -1 : 1) + p.m12) * depth / p.m11, -depth);
                        var world = toWorld.MultiplyPoint3x4(local);
                        _forcedVisibilityWorldCorners[offset] = world;
                        var point = inverse * (world - center);
                        _forcedVisibilityCorners[offset++] = new Point3(point.x, point.y, point.z);
                    }
            }
            return true;
        }

        static bool ForcedVisibilityFinite(Matrix4x4 matrix)
        {
            for (int i = 0; i < 16; ++i)
                if (float.IsNaN(matrix[i]) || float.IsInfinity(matrix[i])) return false;
            return true;
        }

        static bool ForcedVisibilityParametersEncloseEyes(ref ScriptableCullingParameters parameters)
        {
            if (parameters.cullingPlaneCount != 6 || !ForcedVisibilityFinite(parameters.cullingMatrix)) return false;
            float tolerance = Mathf.Max(.0001f, _forcedVisibilityBounds.Far * .00001f);
            for (int planeIndex = 0; planeIndex < parameters.cullingPlaneCount; ++planeIndex)
            {
                var plane = parameters.GetCullingPlane(planeIndex);
                float magnitude = plane.normal.magnitude;
                if (!(magnitude > .5f && magnitude < 2)) return false;
                foreach (var world in _forcedVisibilityWorldCorners)
                    if (!(plane.GetDistanceToPoint(world) >= -tolerance)) return false;
            }
            return true;
        }

        static bool ForcedVisibilityContainsCorners(StereoVisibilityBounds bounds, float shift)
        {
            foreach (var point in _forcedVisibilityCorners)
            {
                double z = (double)point.z + shift;
                if (!(z >= bounds.Near && z <= bounds.Far && point.x >= bounds.Left * z && point.x <= bounds.Right * z &&
                    point.y >= bounds.Bottom * z && point.y <= bounds.Top * z)) return false;
            }
            return true;
        }

        static void CullForcedVisibility(ref ScriptableRenderContext context, Camera camera)
        {
            if (!ForcedVisibilityEnabled || _forcedVisibilityPreparedFrame != Time.frameCount ||
                _forcedVisibilityCulledFrame == Time.frameCount || _forcedVisibilityCamera == null) return;
            try
            {
                if (camera != _forcedVisibilitySource) return;
                bool hasTargets = false;
                foreach (var item in _forcedVisibilityGroups)
                    if (item.Retargeted && item.TotalCount > 0 && item.Group.enabled && !item.CheatDisabled &&
                        item.Group.targetCamera == _forcedVisibilityCamera) { hasTargets = true; break; }
                if (!hasTargets) return;
                if (!_forcedVisibilityCamera.TryGetCullingParameters(false, out var parameters))
                    throw new InvalidOperationException("Visibility helper has no culling parameters");
                if (!ForcedVisibilityParametersEncloseEyes(ref parameters))
                    throw new InvalidOperationException("Native visibility planes do not enclose both eye frusta");
                // The helper is disabled and never rendered. Spheres need frustum
                // visibility only: no lighting/probe pairing, shadow casters or Umbra.
                parameters.cullingOptions = CullingOptions.ForceEvenIfCameraIsNotActive | CullingOptions.DisablePerObjectCulling;
                parameters.shadowDistance = 0;
                // The isolated native check leaves all real groups on the
                // original mask until both phases have passed.
                CullForcedMaskProbe(ref context);
                if (_forcedMaskProbeState == ForcedMaskProbeState.Passed) parameters.cullingMask = 0;
                _forcedVisibilityLastMask = parameters.cullingMask;
                _forcedVisibilityCulledFrame = Time.frameCount;
                long started = DiagnosticTimestamp();
                try { context.Cull(ref parameters); }
                finally { if (started != 0 && DiagnosticsRecording) _forcedVisibilityCullMs.Add((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency); }
                ++_forcedVisibilityCullCalls; ++_forcedVisibilityCullCallsTotal;
                _forcedVisibilityStatus = _forcedMaskProbeState == ForcedMaskProbeState.Passed ? "culled-union-empty-mask" : "culled-union";
            }
            catch (Exception e) { FailForcedVisibility("cull", e); }
        }

        static bool ForcedMaskProbeRunning => _forcedMaskProbeState == ForcedMaskProbeState.Baseline ||
            _forcedMaskProbeState == ForcedMaskProbeState.EmptyMask;

        static void BeginForcedMaskProbe(Camera helper)
        {
            try
            {
                if (_forcedMaskProbeCamera != null || _forcedMaskProbeGroup != null)
                    throw new InvalidOperationException("Previous native probe resources still exist");
                var go = new GameObject("RTMaquetaXR.VisibilityMaskProbe") { hideFlags = HideFlags.HideAndDontSave };
                _forcedMaskProbeCamera = go.AddComponent<Camera>();
                var camera = _forcedMaskProbeCamera;
                camera.enabled = false;
                camera.hideFlags = HideFlags.HideAndDontSave;
                camera.targetTexture = null;
                camera.orthographic = false;
                camera.useOcclusionCulling = false;
                camera.allowHDR = camera.allowMSAA = camera.allowDynamicResolution = false;
                camera.cullingMask = helper.cullingMask;
                camera.nearClipPlane = helper.nearClipPlane;
                camera.farClipPlane = helper.farClipPlane;
                camera.transform.SetPositionAndRotation(helper.transform.position, helper.transform.rotation);
                camera.projectionMatrix = camera.nonJitteredProjectionMatrix = helper.projectionMatrix;
                camera.cullingMatrix = camera.projectionMatrix * camera.worldToCameraMatrix;
                _forcedMaskProbeSourceMask = unchecked((uint)helper.cullingMask);
                if (!camera.TryGetCullingParameters(false, out var parameters) || parameters.cullingPlaneCount != 6 ||
                    !ForcedVisibilityFinite(parameters.cullingMatrix))
                    throw new InvalidOperationException("Native probe has invalid culling planes");
                var bounds = _forcedVisibilityBounds;
                float depth = bounds.Near + (bounds.Far - bounds.Near) * .5f;
                var localInside = new Vector3((float)(((double)bounds.Left + bounds.Right) * .5 * depth),
                    (float)(((double)bounds.Bottom + bounds.Top) * .5 * depth), -depth);
                var inside = camera.cameraToWorldMatrix.MultiplyPoint3x4(localInside);
                var outside = camera.transform.position - camera.transform.forward * Mathf.Max(1, depth);
                if (!VisibleFrustum.Finite(inside.x) || !VisibleFrustum.Finite(inside.y) || !VisibleFrustum.Finite(inside.z) ||
                    !VisibleFrustum.Finite(outside.x) || !VisibleFrustum.Finite(outside.y) || !VisibleFrustum.Finite(outside.z))
                    throw new InvalidOperationException("Non-finite synthetic sphere positions");
                float insideMargin = float.PositiveInfinity, outsideMargin = float.PositiveInfinity;
                for (int i = 0; i < 6; ++i)
                {
                    var plane = parameters.GetCullingPlane(i);
                    float normalSize = plane.normal.sqrMagnitude;
                    if (!VisibleFrustum.Finite(plane.distance) || !VisibleFrustum.Finite(plane.normal.x) ||
                        !VisibleFrustum.Finite(plane.normal.y) || !VisibleFrustum.Finite(plane.normal.z) || !(normalSize > .5f && normalSize < 2))
                        throw new InvalidOperationException("Invalid synthetic visibility plane");
                    insideMargin = Mathf.Min(insideMargin, plane.GetDistanceToPoint(inside));
                    outsideMargin = Mathf.Min(outsideMargin, plane.GetDistanceToPoint(outside));
                }
                if (!(insideMargin > .0001f && insideMargin < float.PositiveInfinity && outsideMargin < -.0001f && outsideMargin > float.NegativeInfinity))
                    throw new InvalidOperationException("Probe spheres are not unambiguously inside and behind the camera");
                float radius = Mathf.Min(.1f, Mathf.Min(insideMargin, -outsideMargin) * .25f);
                _forcedMaskProbeSpheres[0] = new BoundingSphere(inside, radius);
                _forcedMaskProbeSpheres[1] = new BoundingSphere(outside, radius);
                _forcedMaskProbeGroup = new CullingGroup();
                _forcedMaskProbeGroup.targetCamera = camera;
                _forcedMaskProbeGroup.SetBoundingSpheres(_forcedMaskProbeSpheres);
                _forcedMaskProbeGroup.SetBoundingSphereCount(2);
                // Explicitly exclude distance rejection in this synthetic test.
                // No distance settings of the game's groups are changed.
                _forcedMaskProbeGroup.SetBoundingDistances(new[] { float.PositiveInfinity });
                _forcedMaskProbeGroup.onStateChanged = ForcedMaskProbeChanged;
                _forcedMaskProbeStarted = Time.unscaledTime;
                _forcedMaskProbePhaseCulls = 0;
                _forcedMaskProbeLastCullFrame = _forcedMaskProbeLastReadFrame = -1;
                _forcedMaskProbeTransitionBits = 0;
                _forcedMaskProbeBaseline0 = _forcedMaskProbeBaseline1 = _forcedMaskProbeEmpty0 = _forcedMaskProbeEmpty1 = null;
                _forcedMaskProbeError = null;
                _forcedMaskProbeState = ForcedMaskProbeState.Baseline;
            }
            catch (Exception e) { FailForcedMaskProbe("create", e.Message); }
        }

        static void ForcedMaskProbeChanged(CullingGroupEvent change)
        {
            // Only our two synthetic spheres reach this callback. It never
            // forwards visibility changes to the game's service or renderers.
            if (_forcedMaskProbeState != ForcedMaskProbeState.EmptyMask) return;
            if (change.index == 0 && change.hasBecomeInvisible) _forcedMaskProbeTransitionBits |= 1;
            if (change.index == 1 && change.hasBecomeVisible) _forcedMaskProbeTransitionBits |= 2;
        }

        static void AdvanceForcedMaskProbe()
        {
            if (!ForcedMaskProbeRunning) return;
            try
            {
                if (Time.unscaledTime - _forcedMaskProbeStarted > ForcedMaskProbeTimeoutSeconds)
                { FailForcedMaskProbe("timeout", "Native probe did not complete within five seconds"); return; }
                if (_forcedMaskProbeLastCullFrame < 0 || _forcedMaskProbeLastCullFrame >= Time.frameCount ||
                    _forcedMaskProbeLastReadFrame == _forcedMaskProbeLastCullFrame) return;
                _forcedMaskProbeLastReadFrame = _forcedMaskProbeLastCullFrame;
                bool first = _forcedMaskProbeGroup.IsVisible(0), second = _forcedMaskProbeGroup.IsVisible(1);
                if (_forcedMaskProbeState == ForcedMaskProbeState.Baseline)
                {
                    _forcedMaskProbeBaseline0 = first; _forcedMaskProbeBaseline1 = second;
                    if (first && !second)
                    {
                        // Preserve the same native group and index history. Swapping
                        // the two spheres must reverse both visibility states.
                        var sphere = _forcedMaskProbeSpheres[0];
                        _forcedMaskProbeSpheres[0] = _forcedMaskProbeSpheres[1];
                        _forcedMaskProbeSpheres[1] = sphere;
                        _forcedMaskProbeTransitionBits = 0;
                        _forcedMaskProbePhaseCulls = 0;
                        _forcedMaskProbeLastCullFrame = _forcedMaskProbeLastReadFrame = -1;
                        _forcedMaskProbeState = ForcedMaskProbeState.EmptyMask;
                        return;
                    }
                }
                else
                {
                    _forcedMaskProbeEmpty0 = first; _forcedMaskProbeEmpty1 = second;
                    if (!first && second && _forcedMaskProbeTransitionBits == 3)
                    {
                        if (!ReleaseForcedMaskProbe())
                        { FailForcedMaskProbe("dispose", "Native probe cleanup failed"); return; }
                        _forcedMaskProbeState = ForcedMaskProbeState.Passed;
                        _log.Log("[visibility] Native empty-mask probe passed both directions; helper now excludes scene renderers from its culling results");
                        return;
                    }
                }
                if (_forcedMaskProbePhaseCulls >= ForcedMaskProbeAttemptLimit)
                    FailForcedMaskProbe("no-transition", "Native visibility states did not match the requested phase");
            }
            catch (Exception e) { FailForcedMaskProbe("read", e.Message); }
        }

        static void CullForcedMaskProbe(ref ScriptableRenderContext context)
        {
            if (!ForcedMaskProbeRunning || _forcedMaskProbeLastCullFrame == Time.frameCount) return;
            try
            {
                if (Time.unscaledTime - _forcedMaskProbeStarted > ForcedMaskProbeTimeoutSeconds ||
                    _forcedMaskProbePhaseCulls >= ForcedMaskProbeAttemptLimit)
                { FailForcedMaskProbe("timeout", "Native probe exhausted its bounded attempts"); return; }
                if (!_forcedMaskProbeCamera.TryGetCullingParameters(false, out var parameters))
                    throw new InvalidOperationException("Probe culling parameters unavailable");
                parameters.cullingOptions = CullingOptions.ForceEvenIfCameraIsNotActive | CullingOptions.DisablePerObjectCulling;
                parameters.shadowDistance = 0;
                parameters.cullingMask = _forcedMaskProbeState == ForcedMaskProbeState.Baseline ? _forcedMaskProbeSourceMask : 0;
                _forcedMaskProbeLastCullFrame = Time.frameCount;
                ++_forcedMaskProbePhaseCulls; ++_forcedMaskProbeTotalCulls;
                long started = DiagnosticTimestamp();
                try { context.Cull(ref parameters); }
                finally { if (started != 0 && DiagnosticsRecording) _forcedMaskProbeMs.Add((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency); }
            }
            catch (Exception e) { FailForcedMaskProbe("cull", e.Message); }
        }

        static bool ReleaseForcedMaskProbe()
        {
            try
            {
                if (_forcedMaskProbeGroup != null)
                {
                    _forcedMaskProbeGroup.onStateChanged = null;
                    _forcedMaskProbeGroup.Dispose();
                    _forcedMaskProbeGroup = null;
                }
                // Disposal must precede destroying the camera targeted by the group.
                if (_forcedMaskProbeCamera != null) UnityEngine.Object.Destroy(_forcedMaskProbeCamera.gameObject);
                _forcedMaskProbeCamera = null;
                return true;
            }
            catch (Exception e) { _forcedMaskProbeError = "cleanup: " + e.Message; return false; }
        }

        static void FailForcedMaskProbe(string stage, string message)
        {
            _forcedMaskProbeState = ForcedMaskProbeState.Fallback;
            _forcedMaskProbeError = stage + ": " + message;
            ReleaseForcedMaskProbe();
            _log.Log("[visibility] Keeping the original helper mask; empty-mask probe fallback: " + _forcedMaskProbeError);
        }

        static void SuspendForcedMaskProbe()
        {
            if (!ForcedMaskProbeRunning) return;
            if (ReleaseForcedMaskProbe()) _forcedMaskProbeState = ForcedMaskProbeState.Unstarted;
            else FailForcedMaskProbe("suspend", "Could not release the isolated native probe");
        }

        static void ResetForcedMaskProbe()
        {
            if (!ReleaseForcedMaskProbe())
            { _forcedMaskProbeState = ForcedMaskProbeState.Fallback; return; }
            _forcedMaskProbeState = ForcedMaskProbeState.Unstarted;
            _forcedMaskProbeLastCullFrame = _forcedMaskProbeLastReadFrame = -1;
            _forcedMaskProbePhaseCulls = _forcedMaskProbeTotalCulls = _forcedMaskProbeTransitionBits = 0;
            _forcedMaskProbeBaseline0 = _forcedMaskProbeBaseline1 = _forcedMaskProbeEmpty0 = _forcedMaskProbeEmpty1 = null;
            _forcedMaskProbeSourceMask = 0;
            _forcedMaskProbeError = null;
            ClearDetailedValue(_forcedMaskProbeMs);
        }

        static void RefreshForcedVisibilityInventory()
        {
            if (Time.unscaledTime < _forcedVisibilityNextInventory) return;
            _forcedVisibilityNextInventory = Time.unscaledTime + 5;
            foreach (var item in _forcedVisibilityGroups)
            {
                item.LiveTargets = item.CulledTargets = item.RendererReferences = item.UniqueRenderers = 0;
                var targets = _forcedVisibilityTargets.GetValue(item.Service) as Array;
                if (targets == null) continue;
                var renderers = new HashSet<Renderer>();
                for (int i = 0, count = Math.Min(Math.Max(0, item.TotalCount), targets.Length); i < count; ++i)
                {
                    var target = targets.GetValue(i);
                    if (target == null || target is UnityEngine.Object obj && obj == null) continue;
                    ++item.LiveTargets;
                    if ((bool)_forcedVisibilityCulled.GetValue(target)) ++item.CulledTargets;
                    var list = _forcedVisibilityRenderers.GetValue(target) as IList;
                    if (list == null) continue;
                    foreach (var entry in list)
                    {
                        var renderer = entry as Renderer;
                        if (renderer == null) continue;
                        ++item.RendererReferences;
                        renderers.Add(renderer);
                    }
                }
                item.UniqueRenderers = renderers.Count;
            }
        }

        static bool RestoreForcedVisibilityGroup(ForcedVisibilityGroup item)
        {
            if (!item.Retargeted) return true;
            try
            {
                // Never replace a newer target assigned by the game or another mod.
                if (_forcedVisibilityCamera != null && item.Group.targetCamera == _forcedVisibilityCamera)
                    item.Group.targetCamera = item.OriginalCamera;
                item.Retargeted = false;
                return true;
            }
            catch (Exception e)
            {
                bool firstFailure = !_forcedVisibilityFailed;
                _forcedVisibilityFailed = true;
                _forcedVisibilityError = "restore: " + e.Message;
                _forcedVisibilityStatus = "restore-failed";
                if (firstFailure) _log.Error("[visibility] Camera target restoration failed; helper retained and synchronization disabled: " + e.Message);
                return false;
            }
        }

        static bool RestoreForcedVisibilityGroups()
        {
            bool restored = true;
            foreach (var item in _forcedVisibilityGroups) if (!RestoreForcedVisibilityGroup(item)) restored = false;
            return restored;
        }

        static void FailForcedVisibility(string stage, Exception error)
        {
            SuspendForcedMaskProbe();
            _forcedVisibilityFailed = true;
            _forcedVisibilityPreparedFrame = -1;
            _forcedVisibilityError = stage + ": " + error.Message;
            _forcedVisibilityStatus = "disabled-after-error";
            RestoreForcedVisibilityGroups();
            _log.Error("[visibility] ForcedCulling synchronization stopped; original camera targets restored where available: " + _forcedVisibilityError);
        }

        static void StopForcedVisibility()
        {
            SuspendForcedMaskProbe();
            ReleaseForcedMaskProbe();
            _forcedVisibilityPreparedFrame = _forcedVisibilityCulledFrame = -1;
            _forcedVisibilitySource = null;
            if (!RestoreForcedVisibilityGroups())
            {
                // Keep the disabled helper alive until retained targets can be restored.
                _log.Error("[visibility] Keeping disabled helper until camera targets can be restored: " + _forcedVisibilityError);
                return;
            }
            _forcedVisibilityGroups.Clear();
            if (_forcedVisibilityCamera != null) UnityEngine.Object.Destroy(_forcedVisibilityCamera.gameObject);
            _forcedVisibilityCamera = null;
            _forcedVisibilityNextInventory = 0;
            _forcedVisibilityStatus = _forcedVisibilityFailed ? "stopped-after-error" : "stopped";
        }

        static object ForcedVisibilitySnapshot()
        {
            var groups = new List<object>();
            foreach (var item in _forcedVisibilityGroups)
                groups.Add(new { OriginalCamera = item.OriginalCamera == null ? null : item.OriginalCamera.name,
                    item.Retargeted, item.GroupEnabled, item.CheatDisabled, item.TotalCount, item.LiveTargets, item.CulledTargets,
                    item.RendererReferences, item.UniqueRenderers, item.Events, item.VisibleEvents, item.SyncedEvents });
            var b = _forcedVisibilityBounds;
            return new { Enabled = _cfg.syncForcedVisibility, Hook = _forcedVisibilityHook, EventHook = _forcedVisibilityEventHook,
                DisposeHook = _forcedVisibilityDisposeHook,
                Failed = _forcedVisibilityFailed, Status = _forcedVisibilityStatus, LastError = _forcedVisibilityError,
                HelperExists = _forcedVisibilityCamera != null, PreparedFrame = _forcedVisibilityPreparedFrame,
                LastCullFrame = _forcedVisibilityCulledFrame, CullCalls = _forcedVisibilityCullCalls,
                CullCallsSinceStart = _forcedVisibilityCullCallsTotal, NoTargetFrames = _forcedVisibilityEmptyFrames,
                EffectiveCullMask = _forcedVisibilityCulledFrame >= 0 ? (uint?)_forcedVisibilityLastMask : null,
                EmptyMaskProbe = new { State = _forcedMaskProbeState.ToString(), Verified = _forcedMaskProbeState == ForcedMaskProbeState.Passed,
                    ResourcesAlive = _forcedMaskProbeCamera != null || _forcedMaskProbeGroup != null,
                    SourceMask = _forcedMaskProbeSourceMask, BaselineSphere0Visible = _forcedMaskProbeBaseline0,
                    BaselineSphere1Visible = _forcedMaskProbeBaseline1, EmptyMaskSphere0Visible = _forcedMaskProbeEmpty0,
                    EmptyMaskSphere1Visible = _forcedMaskProbeEmpty1, TransitionBits = _forcedMaskProbeTransitionBits,
                    PhaseCulls = _forcedMaskProbePhaseCulls, TotalCulls = _forcedMaskProbeTotalCulls,
                    AttemptLimitPerPhase = ForcedMaskProbeAttemptLimit, TimeoutSeconds = ForcedMaskProbeTimeoutSeconds,
                    LastError = _forcedMaskProbeError, InitializationCullCpuMs = DetailedValue(_forcedMaskProbeMs) },
                CullCpuMs = DetailedValue(_forcedVisibilityCullMs), InventoryIntervalSeconds = 5,
                Volume = new { b.Left, b.Right, b.Bottom, b.Top, b.Near, b.Far }, Groups = groups };
        }

        static void ResetForcedVisibilityWindow()
        {
            _forcedVisibilityCullCalls = _forcedVisibilityEmptyFrames = 0;
            ClearDetailedValue(_forcedVisibilityCullMs);
            foreach (var item in _forcedVisibilityGroups) item.Events = item.VisibleEvents = item.SyncedEvents = 0;
        }
    }
}
