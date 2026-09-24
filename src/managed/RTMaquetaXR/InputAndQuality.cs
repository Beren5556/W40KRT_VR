using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Owlcat.Runtime.Visual.Waaagh;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _dragProjectionHook, _qualityHooks, _dragProjectionLogged, _smaaExecutionHook;
        static int _blurHooks;
        static bool _pacingOwned;
        static int _savedVsync, _savedFrameLimit;
        static float _nextPacingCheck;
        static Camera _renderingCamera;
        static FieldInfo _eyeAa, _eyeAaQuality, _eyeScaling, _eyeDithering;
        static object _eyeAaValue, _eyeTaaValue, _eyeNoAaValue, _eyeAaQualityValue;
        static readonly Dictionary<string, double> _renderMilliseconds = new Dictionary<string, double>();
        static readonly Dictionary<string, int> _renderSamples = new Dictionary<string, int>();
        static readonly Dictionary<string, string> _renderTargets = new Dictionary<string, string>();
        static readonly Dictionary<string, int> _smaaExecutions = new Dictionary<string, int>();
        static readonly FrameTiming[] _timings = new FrameTiming[1];
        static ulong _lastGpuTimestamp;
        static double _frameSeconds, _stereoSeconds, _waitMilliseconds, _gpuMilliseconds;
        static int _frameSamples, _stereoSamples, _waitSamples, _gpuSamples;
        static double _mainThreadMilliseconds, _renderThreadMilliseconds, _presentWaitMilliseconds;
        static int _mainThreadSamples, _renderThreadSamples, _presentWaitSamples;
        static float _nextTargetDiagnostic;

        static void InstallInputAndQualityHooks()
        {
            if (!_dragProjectionHook)
            {
                try
                {
                    var target = AccessTools.Method(AccessTools.TypeByName("Kingmaker.View.CameraRig"), "GetLocalPointerPosition");
                    if (target == null) throw new MissingMethodException("CameraRig.GetLocalPointerPosition");
                    _harmony.Patch(target, transpiler: new HarmonyMethod(typeof(Main), nameof(DragProjectionTranspiler)));
                    _dragProjectionHook = true;
                    _log.Log("[input] Camera drag uses the VR panel's matching projection; original buttons and sensitivity retained");
                }
                catch (Exception e) { _log.Error("[input] Drag projection hook: " + e.Message); }
            }
            if (_qualityHooks) return;
            _qualityHooks = true;
            foreach (string name in new[] { "DepthOfField", "MotionBlur" })
            {
                try
                {
                    var type = AccessTools.TypeByName("Owlcat.Runtime.Visual.Overrides." + name);
                    var target = AccessTools.Method(type, "IsActive");
                    if (target == null) throw new MissingMethodException(name + ".IsActive");
                    _harmony.Patch(target, prefix: new HarmonyMethod(typeof(Main), nameof(EyeBlurPrefix)));
                    ++_blurHooks;
                }
                catch (Exception e) { _log.Error("[quality] " + e.Message); }
            }
            _log.Log("[quality] Eye-only blur hooks=" + _blurHooks + "; monitor and flat panels retain their effects");
            try
            {
                var pass = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.Passes.PostProcess.PostProcessPass");
                MethodInfo target = null;
                foreach (var nested in pass.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                    foreach (var method in nested.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                        if (method.Name.Contains("<DoSubpixelMorphologicalAntialiasing>") && method.GetParameters().Length == 2)
                        {
                            if (target != null) throw new InvalidOperationException("Ambiguous SMAA render callback");
                            target = method;
                        }
                if (target == null) throw new MissingMethodException("SMAA render callback");
                _harmony.Patch(target, postfix: new HarmonyMethod(typeof(Main), nameof(SmaaRenderPostfix)));
                _smaaExecutionHook = true;
                _log.Log("[quality] Observing the SMAA render callback, including edge/blend/neighborhood draws");
            }
            catch (Exception e) { _log.Error("[quality] SMAA observation: " + e.Message); }
        }

        static IEnumerable<CodeInstruction> DragProjectionTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>();
            int replacements = 0;
            foreach (var instruction in instructions)
            {
                result.Add(instruction);
                var called = instruction.operand as MethodInfo;
                if (called == null || called.Name != "get_Instance" || called.DeclaringType.FullName != "Kingmaker.UI.UICamera") continue;
                result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Main), nameof(DragProjectionCamera))));
                ++replacements;
            }
            if (replacements != 1) throw new InvalidOperationException("Unexpected camera drag projection shape: " + replacements);
            return result;
        }

        static Camera DragProjectionCamera(Camera original)
        {
            if (!_active || !_attached || _modeFlat || _pickCam == null) return original;
            if (!_dragProjectionLogged)
            {
                _dragProjectionLogged = true;
                _log.Log("[input] First drag coordinate query redirected from UICamera to the VR pick camera");
            }
            return _pickCam;
        }

        static bool IsEye(Camera camera) => camera != null && _runner != null &&
            (camera == _runner.GetEyeL() || camera == _runner.GetEyeR());

        static bool EyeBlurPrefix(ref bool __result)
        {
            if (!_active || _modeFlat || !IsEye(_renderingCamera)) return true;
            __result = false; return false;
        }

        // This observes command recording in the actual render graph callback,
        // rather than inferring execution from the requested AA enum alone.
        static void SmaaRenderPostfix()
        {
            if (!DiagnosticsRecording || !_active || _modeFlat || !IsEye(_renderingCamera)) return;
            string name = _renderingCamera.name;
            _smaaExecutions.TryGetValue(name, out int count);
            _smaaExecutions[name] = count + 1;
        }

        internal static void ApplyEyeQuality(Camera camera)
        {
            if (_camDataType == null || camera == null) return;
            var data = camera.GetComponent(_camDataType);
            if (data == null) return;
            if (_eyeAa == null)
            {
                _eyeAa = AccessTools.Field(_camDataType, "m_Antialiasing");
                _eyeAaQuality = AccessTools.Field(_camDataType, "m_AntialiasingQuality");
                _eyeScaling = AccessTools.Field(_camDataType, "m_AllowRenderScaling");
                _eyeDithering = AccessTools.Field(_camDataType, "m_Dithering");
                if (_eyeAa != null) _eyeAaValue = Enum.Parse(_eyeAa.FieldType, "SubpixelMorphologicalAntiAliasing");
                if (_eyeAa != null) _eyeTaaValue = Enum.Parse(_eyeAa.FieldType, "TemporalAntialiasing");
                if (_eyeAa != null) _eyeNoAaValue = Enum.Parse(_eyeAa.FieldType, "None");
                if (_eyeAaQuality != null) _eyeAaQualityValue = Enum.Parse(_eyeAaQuality.FieldType, "High");
            }
            // Waaagh owns a separate history per camera. Preserve the full-size
            // OpenXR output, with optional internal FSR scaling selected in-game.
            _eyeAa?.SetValue(data, EffectiveEyeAa == EyeAaMode.Off ? _eyeNoAaValue : EffectiveEyeAa == EyeAaMode.Taa ? _eyeTaaValue : _eyeAaValue);
            _eyeAaQuality?.SetValue(data, _eyeAaQualityValue);
            _eyeScaling?.SetValue(data, (GameFsrAllowedForEyes && _fsrTargetHook) || NeuralTemporalHook.ShouldAllowEyeScaling());
            _eyeDithering?.SetValue(data, false);
        }

        internal static void ResetEyeQualityRefresh() { _camDataNextRefresh = 0; _renderTargets.Clear(); }

        static void ResetQualityDiagnostics()
        {
            _renderTargets.Clear(); _renderMilliseconds.Clear(); _renderSamples.Clear(); _smaaExecutions.Clear();
            _lastGpuTimestamp = 0; _nextTargetDiagnostic = 0;
            _leftCameraBuffer = _rightCameraBuffer = null;
            ResetPerformanceWindow();
        }

        internal static void MaintainVrPacing()
        {
            if (!_active || Time.realtimeSinceStartup < _nextPacingCheck) return;
            _nextPacingCheck = Time.realtimeSinceStartup + 1;
            if (!_pacingOwned)
            {
                _savedVsync = QualitySettings.vSyncCount;
                _savedFrameLimit = Application.targetFrameRate;
                _pacingOwned = true;
                _log.Log("[performance] OpenXR owns frame pacing; previous monitor vSync=" + _savedVsync + "; limit=" + _savedFrameLimit + "; engine queuedFrames retained=" + QualitySettings.maxQueuedFrames);
            }
            else
            {
                // Respect a settings change made during VR when restoring it.
                if (QualitySettings.vSyncCount != 0) _savedVsync = QualitySettings.vSyncCount;
                if (Application.targetFrameRate != -1) _savedFrameLimit = Application.targetFrameRate;
            }
            if (QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
            if (Application.targetFrameRate != -1) Application.targetFrameRate = -1;
            // OpenXR already controls submission pacing. Forcing this D3D11
            // queue to one in 0.1.30 can stall CPU/GPU overlap for every scene.
            // Retain the engine/user setting as before that regression.
        }

        static void RestoreFramePacing()
        {
            if (!_pacingOwned) return;
            if (QualitySettings.vSyncCount == 0) QualitySettings.vSyncCount = _savedVsync;
            if (Application.targetFrameRate == -1) Application.targetFrameRate = _savedFrameLimit;
            _pacingOwned = false; _nextPacingCheck = 0;
        }

        struct RenderScopeState { public Camera previous, camera; public long timestamp; public bool skipped; }
        static void BeginRenderScope(Camera camera, ref CameraData data, out RenderScopeState state)
        {
            state = new RenderScopeState { previous = _renderingCamera, camera = camera, timestamp = DiagnosticsRecording ? Stopwatch.GetTimestamp() : 0 };
            _renderingCamera = camera;
            if (!_active || camera == null) return;
            ReadVisibleCullConstraints(camera, ref data);
            // Culling constraints and the current camera are part of rendering,
            // not diagnostics. Keep them even when observations are suspended.
            if (!DiagnosticsRecording) return;
            // Refresh occasionally so a quality change in the game's menu is
            // reflected in diagnostics, without walking metadata every frame.
            if (Time.realtimeSinceStartup >= _nextTargetDiagnostic)
            { _renderTargets.Clear(); _nextTargetDiagnostic = Time.realtimeSinceStartup + 10; }
            if (_renderTargets.ContainsKey(camera.name)) return;
            // These diagnostic strings are refreshed once per ten seconds.
            // Only this cold path boxes metadata; the camera worker and culling
            // checks consume CameraData by reference on every rendered frame.
            object argument = data;
            var type = typeof(CameraData);
            var desc = data.CameraTargetDescriptor;
            string value = desc.width + "x" + desc.height + "; scale=" + AccessTools.Field(type, "RenderScale")?.GetValue(argument) +
                "; AA=" + data.Antialiasing + "; quality=" + data.AntialiasingQuality +
                "; postFX=" + data.PostProcessEnabled + "; TAAsharpness=" + data.TemporalAntialiasingSharpness +
                "; upscaler=" + AccessTools.Field(type, "UpscalingFilter")?.GetValue(argument);
            if (IsEye(camera))
            {
                if (camera == _runner.GetEyeL()) _leftCameraBuffer = data.CameraBuffer;
                else _rightCameraBuffer = data.CameraBuffer;
            }
            _renderTargets[camera.name] = value;
            _log.Log("[performance] " + camera.name + " actual target=" + value);
        }

        static Exception EndRenderScope(Exception __exception, RenderScopeState __state)
        {
            _renderingCamera = __state.previous;
            if (DiagnosticsRecording && _active && !__state.skipped && __state.camera != null && __state.timestamp != 0)
            {
                string name = __state.camera.name;
                _renderMilliseconds.TryGetValue(name, out double ms);
                _renderSamples.TryGetValue(name, out int samples);
                _renderMilliseconds[name] = ms + (Stopwatch.GetTimestamp() - __state.timestamp) * 1000.0 / Stopwatch.Frequency;
                _renderSamples[name] = samples + 1;
            }
            return __exception;
        }
        internal static void RecordFrameWait(long started)
        {
            if (started == 0 || !DiagnosticsRecording) return;
            _waitMilliseconds += (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            ++_waitSamples;
        }
        internal static void RecordFramePerformance(bool stereo)
        {
            if (!DiagnosticsRecording)
            {
                // Distance trial warmup uses this stability counter. Keep that
                // cheap state update, without CPU/GPU capture or frame timings.
                RecordSteadyStereo(stereo, 0); return;
            }
            double seconds = Time.unscaledDeltaTime;
            if (seconds > 0)
            {
                ++_frameSamples; _frameSeconds += seconds;
                if (stereo) { ++_stereoSamples; _stereoSeconds += seconds; }
            }
            bool steadyStereo = RecordSteadyStereo(stereo, seconds);
            ReadDetailedFrame(steadyStereo);
            if (!FrameTimingManager.IsFeatureEnabled()) return;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timings) == 0 || _timings[0].frameStartTimestamp == _lastGpuTimestamp) return;
            _lastGpuTimestamp = _timings[0].frameStartTimestamp;
            if (steadyStereo) RecordSteadyUnityTiming(_timings[0]);
            if (_timings[0].gpuFrameTime > 0) { _gpuMilliseconds += _timings[0].gpuFrameTime; ++_gpuSamples; }
            if (_timings[0].cpuMainThreadFrameTime > 0)
            { _mainThreadMilliseconds += _timings[0].cpuMainThreadFrameTime; ++_mainThreadSamples; }
            if (_timings[0].cpuRenderThreadFrameTime > 0)
            { _renderThreadMilliseconds += _timings[0].cpuRenderThreadFrameTime; ++_renderThreadSamples; }
            if (_timings[0].cpuMainThreadFrameTime > 0 && _timings[0].cpuMainThreadPresentWaitTime >= 0)
            { _presentWaitMilliseconds += _timings[0].cpuMainThreadPresentWaitTime; ++_presentWaitSamples; }
        }
        static object PerformanceSnapshot()
        {
            var cameras = new Dictionary<string, double>();
            foreach (var item in _renderMilliseconds) cameras[item.Key] = Math.Round(item.Value / _renderSamples[item.Key], 3);
            var value = new {
                SteadyStereoOnly = SteadyStereoSnapshot(),
                AntialiasingComparison = AaComparisonSnapshot(),
                EyeRenderBreakdown = RenderStageSnapshot(),
                DetailedRendering = DetailedRenderingSnapshot(),
                WindowIncludesFlatFrames = _frameSamples != _stereoSamples,
                Fps = _frameSeconds > 0 ? (double?)Math.Round(_frameSamples / _frameSeconds, 1) : null,
                StereoFps = _stereoSeconds > 0 ? (double?)Math.Round(_stereoSamples / _stereoSeconds, 1) : null,
                OpenXRWaitMs = _waitSamples > 0 ? (double?)Math.Round(_waitMilliseconds / _waitSamples, 3) : null,
                GpuFrameMs = _gpuSamples > 0 ? (double?)Math.Round(_gpuMilliseconds / _gpuSamples, 3) : null,
                UnityMainThreadMs = _mainThreadSamples > 0 ? (double?)Math.Round(_mainThreadMilliseconds / _mainThreadSamples, 3) : null,
                UnityRenderThreadMs = _renderThreadSamples > 0 ? (double?)Math.Round(_renderThreadMilliseconds / _renderThreadSamples, 3) : null,
                UnityPresentWaitMs = _presentWaitSamples > 0 ? (double?)Math.Round(_presentWaitMilliseconds / _presentWaitSamples, 3) : null,
                CameraCpuMs = cameras, ActualTargets = new Dictionary<string, string>(_renderTargets),
                Vsync = QualitySettings.vSyncCount, FrameLimit = Application.targetFrameRate,
                DragProjectionHook = _dragProjectionHook, EyeBlurHooks = _blurHooks,
                SmaaExecutionHook = _smaaExecutionHook, SmaaPassExecutions = new Dictionary<string, int>(_smaaExecutions),
                Optimizations = OptimizationSnapshot()
            };
            ResetPerformanceWindow();
            return value;
        }
        static void ResetPerformanceWindow()
        {
            ResetLiveMeasurements();
            _frameSeconds = _stereoSeconds = _waitMilliseconds = _gpuMilliseconds = 0;
            _frameSamples = _stereoSamples = _waitSamples = _gpuSamples = 0;
            _mainThreadMilliseconds = _renderThreadMilliseconds = _presentWaitMilliseconds = 0;
            _mainThreadSamples = _renderThreadSamples = _presentWaitSamples = 0;
            _renderMilliseconds.Clear(); _renderSamples.Clear();
            _smaaExecutions.Clear();
            ResetOptimizationWindow();
            _stereoMeasurements.Clear();
            ResetSteadyPoseWindow();
            ResetRenderStageWindow();
            ResetDetailedWindow();
        }
    }
}
