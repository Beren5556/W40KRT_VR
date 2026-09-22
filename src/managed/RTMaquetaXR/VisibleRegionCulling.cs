using System;
using System.Reflection;
using System.Reflection.Emit;
using Owlcat.Runtime.Visual.Waaagh;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        const float VisibleCullGuardPixels = 64;
        sealed class VisibleCullState
        {
            public Camera Camera;
            public Matrix4x4 Projection, AppliedView;
            public CullingProjectionXY AppliedProjectionXY;
            public XrCrop Crop;
            public float Near, Far, RenderScale, AreaFraction = 1;
            public int Width, Height, PreparedFrame = -1, CheckedFrame = -1, AppliedFrame = -1;
            public bool EffectsSafe;
            public string Constraint = "not-checked", LastStatus = "not-rendered";
            public int Applied, Fallback;
            public readonly Plane[] Planes = new Plane[6];
        }
        static readonly VisibleCullState[] _visibleCull = { new VisibleCullState(), new VisibleCullState() };
        delegate float VisibleCullScaleReader(ref CameraData data);
        static Type _visibleCullBloom, _visibleCullHbao;
        static VisibleCullScaleReader _visibleCullScale;
        static Func<object, bool> _visibleCullBloomActive, _visibleCullHbaoActive;
        static bool _visibleCullMetadataAttempted, _visibleCullMetadataReady, _visibleCullFailed, _visibleCullLogged;

        internal static void PrepareVisibleCulling(Camera camera, Matrix4x4 projection, XrCrop crop,
            float near, float far, int width, int height)
        {
            if (!IsEye(camera)) return;
            var state = _visibleCull[camera == _runner.GetEyeL() ? 0 : 1];
            state.Camera = camera; state.Projection = projection; state.Crop = crop;
            state.Near = near; state.Far = far; state.Width = width; state.Height = height;
            state.PreparedFrame = Time.frameCount; state.CheckedFrame = state.AppliedFrame = -1;
        }

        static void EnsureVisibleCullReaders()
        {
            if (_visibleCullMetadataAttempted) return;
            _visibleCullMetadataAttempted = true;
            var scale = AccessTools.Field(typeof(CameraData), "RenderScale");
            _visibleCullBloom = AccessTools.TypeByName("Owlcat.Runtime.Visual.Overrides.Bloom");
            _visibleCullHbao = AccessTools.TypeByName("Owlcat.Runtime.Visual.Overrides.HBAO.Hbao");
            var bloom = _visibleCullBloom == null ? null : AccessTools.Method(_visibleCullBloom, "IsActive");
            var hbao = _visibleCullHbao == null ? null : AccessTools.Method(_visibleCullHbao, "IsActive");
            if (scale == null || scale.IsStatic || scale.FieldType != typeof(float) ||
                bloom == null || bloom.IsStatic || bloom.ReturnType != typeof(bool) || bloom.GetParameters().Length != 0 ||
                hbao == null || hbao.IsStatic || hbao.ReturnType != typeof(bool) || hbao.GetParameters().Length != 0) return;
            // RenderScale is internal in the installed game. A by-ref reader
            // preserves the exact value without boxing the large CameraData.
            var getter = new DynamicMethod("RTMaquetaXR_CullRenderScale", typeof(float),
                new[] { typeof(CameraData).MakeByRefType() }, typeof(Main).Module, true);
            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, scale); il.Emit(OpCodes.Ret);
            _visibleCullScale = (VisibleCullScaleReader)getter.CreateDelegate(typeof(VisibleCullScaleReader));
            _visibleCullBloomActive = ReferenceGetter<bool>(_visibleCullBloom, bloom, null);
            _visibleCullHbaoActive = ReferenceGetter<bool>(_visibleCullHbao, hbao, null);
            _visibleCullMetadataReady = true;
        }

        static void ReadVisibleCullConstraints(Camera camera, ref CameraData data)
        {
            if (!_cfg.visibleRegionCulling || !IsEye(camera) || _modeFlat || _visibleCullFailed) return;
            var state = _visibleCull[camera == _runner.GetEyeL() ? 0 : 1];
            state.CheckedFrame = Time.frameCount; state.EffectsSafe = false; state.Constraint = "camera-data-unavailable";
            try
            {
                EnsureVisibleCullReaders();
                if (!_visibleCullMetadataReady) { state.Constraint = "effect-metadata-unavailable"; return; }
                state.RenderScale = _visibleCullScale(ref data);
                if (data.IsSSREnabled || data.IsSSREnabledInStack)
                { state.Constraint = "screen-space-reflections"; return; }
                if (data.PostProcessEnabled)
                {
                    if (_blurHooks != 2) { state.Constraint = "blur-hooks-unavailable"; return; }
                    var stack = VolumeManager.instance.stack;
                    if (stack == null) { state.Constraint = "volume-stack-unavailable"; return; }
                    // Same current native volume values and IsActive methods.
                    // Delegates eliminate reflection/boxed booleans, not checks.
                    var bloom = stack.GetComponent(_visibleCullBloom);
                    if (bloom != null && _visibleCullBloomActive(bloom))
                    { state.Constraint = "bloom"; return; }
                    var hbao = stack.GetComponent(_visibleCullHbao);
                    if (hbao != null && _visibleCullHbaoActive(hbao))
                    { state.Constraint = "ambient-occlusion"; return; }
                }
                state.EffectsSafe = true; state.Constraint = "ready";
            }
            catch (Exception e) { FailVisibleCulling(e); state.Constraint = "exception"; }
        }

        static void ApplyVisibleCulling(int eye, ref ScriptableCullingParameters parameters)
        {
            if (eye < 0) return;
            var state = _visibleCull[eye];
            state.AppliedFrame = -1;
            string reason = null;
            if (!_cfg.visibleRegionCulling) reason = "disabled";
            else if (_visibleCullFailed) reason = "exception-disabled";
            else if (!OpenXR.FlipEyes) reason = "nonstandard-texture-orientation";
            else if (state.Camera != _renderingCamera || state.PreparedFrame != Time.frameCount || state.CheckedFrame != Time.frameCount)
                reason = "stale-camera-state";
            else if (!state.EffectsSafe) reason = state.Constraint;
            else if (parameters.cullingPlaneCount != 6 || parameters.isOrthographic) reason = "custom-culling-planes";
            if (reason != null) { VisibleCullFallback(state, reason); return; }
            try
            {
                var crop = state.Crop;
                if (!VisibleFrustum.Finite(state.Near) || !VisibleFrustum.Finite(state.Far) || state.Near <= 0 || state.Far <= state.Near ||
                    !VisibleFrustum.TryBounds(1 / state.Projection.m00, 1 / state.Projection.m11,
                        crop.left, crop.top, crop.right, crop.bottom, state.Width, state.Height,
                        state.RenderScale, VisibleCullGuardPixels, out var bounds))
                { VisibleCullFallback(state, "invalid-frustum"); return; }
                if (bounds.AreaFraction >= .9999f) { VisibleCullFallback(state, "full-frustum"); return; }
                if (!VisibleFrustum.TryProjection(bounds, out var projectionXY))
                { VisibleCullFallback(state, "invalid-projection"); return; }
                var view = state.Camera.worldToCameraMatrix;
                var matrix = Matrix4x4.Frustum(bounds.Left * state.Near, bounds.Right * state.Near,
                    bounds.Bottom * state.Near, bounds.Top * state.Near, state.Near, state.Far) * view;
                GeometryUtility.CalculateFrustumPlanes(matrix, state.Planes);
                // Modify a value copy first. If validation or Unity throws, the
                // caller retains every original culling parameter. Keep LOD,
                // shadows, lights, probe settings and per-eye ownership intact.
                var candidate = parameters;
                candidate.cullingMatrix = matrix;
                candidate.cullingPlaneCount = 6;
                for (int i = 0; i < 6; ++i)
                {
                    var plane = state.Planes[i];
                    if (!VisibleFrustum.Finite(plane.distance) || !VisibleFrustum.Finite(plane.normal.x) ||
                        !VisibleFrustum.Finite(plane.normal.y) || !VisibleFrustum.Finite(plane.normal.z) || plane.normal.sqrMagnitude < .5f)
                        throw new InvalidOperationException("Invalid visible-region plane");
                    candidate.SetCullingPlane(i, plane);
                }
                parameters = candidate;
                state.AppliedProjectionXY = projectionXY; state.AppliedView = view; state.AppliedFrame = Time.frameCount;
                ++state.Applied; state.LastStatus = "applied"; state.AreaFraction = bounds.AreaFraction;
                if (!_visibleCullLogged)
                {
                    _visibleCullLogged = true;
                    _log.Log("[performance] Visible-region culling active; guard=64 internal pixels; projection and texture resolution retained");
                }
            }
            catch (Exception e) { FailVisibleCulling(e); VisibleCullFallback(state, "exception"); }
        }
        static void VisibleCullFallback(VisibleCullState state, string reason)
        { ++state.Fallback; state.AppliedFrame = -1; state.LastStatus = reason; state.AreaFraction = 1; }
        static void FailVisibleCulling(Exception e)
        {
            if (!_visibleCullFailed) _log.Error("[performance] Visible-region culling disabled; original cull retained: " + e.Message);
            _visibleCullFailed = true;
        }
        static object VisibleCullingSnapshot()
        {
            var eyes = new object[2];
            for (int i = 0; i < 2; ++i)
            {
                var state = _visibleCull[i];
                eyes[i] = new { Eye = i == 0 ? "Left" : "Right", state.Applied, state.Fallback,
                    state.LastStatus, state.AreaFraction, state.RenderScale };
            }
            return new { Enabled = _cfg.visibleRegionCulling, CullHook = _eyeCullSubmitHook,
                MetadataReady = _visibleCullMetadataReady, Failed = _visibleCullFailed,
                GuardInternalPixels = VisibleCullGuardPixels, Eyes = eyes };
        }
        static void ResetVisibleCullWindow()
        { foreach (var state in _visibleCull) state.Applied = state.Fallback = 0; }
        static void StopVisibleCulling()
        {
            foreach (var state in _visibleCull)
            {
                state.Camera = null; state.PreparedFrame = state.CheckedFrame = state.AppliedFrame = -1;
                state.EffectsSafe = false; state.LastStatus = "stopped"; state.AreaFraction = 1;
            }
            _visibleCullFailed = _visibleCullLogged = false;
        }
    }
}
