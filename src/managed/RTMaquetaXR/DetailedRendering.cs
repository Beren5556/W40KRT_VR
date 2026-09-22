using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        sealed class PassMeasurement
        {
            public string Name;
            public int Eye, LastFrame = -1, Invocations, CpuBlocks, GpuBlocks;
            public int NativeId, LastNativeFrame = -1;
            public ulong NativeRejected;
            public CustomSampler Sampler;
            public Recorder Recorder;
            public bool SamplerValid, Failed;
            public readonly Measurement RecordMs = new Measurement(), GpuMs = new Measurement(), RenderThreadMs = new Measurement();
            public readonly Measurement NativeGpuMs = new Measurement();
        }
        sealed class DrawCounter
        {
            public string Name, Error;
            public ProfilerRecorder Recorder;
            public readonly Measurement Values = new Measurement();
        }
        const int MaxPassMeasurements = 128;
        static readonly Dictionary<string, PassMeasurement>[] _detailedPasses = {
            new Dictionary<string, PassMeasurement>(), new Dictionary<string, PassMeasurement>()
        };
        static readonly List<DrawCounter> _drawCounters = new List<DrawCounter>();
        static readonly List<DrawCounter> _nativeUiCounters = new List<DrawCounter>();
        static readonly PassMeasurement[] _nativeGpuPasses = new PassMeasurement[MaxPassMeasurements];
        static int _nativeGpuPassCount;
        static MethodInfo _originalPassExecute;
        static Action<object, object> _invokePass;
        static Func<object, string> _passName;
        static Func<object, CommandBuffer> _passCommands;
        static bool _detailedPassHook, _detailedFailed, _detailedRunning, _gpuPassRecorderSupported;
        static int _detailedReadFrame = -1, _detailedReadFrames;
        static readonly GpuMarkerProbe _gpuMarkerProbe = new GpuMarkerProbe();
        static bool _gpuProbeAttached;
        static Camera _gpuProbeCamera;

        static Func<object, T> ReferenceGetter<T>(Type owner, MethodInfo getter, FieldInfo field)
        {
            var method = new DynamicMethod("RTMaquetaXR_PassMember", typeof(T), new[] { typeof(object) }, typeof(Main).Module, true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, owner);
            if (getter != null) il.Emit(OpCodes.Callvirt, getter); else il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);
            return (Func<object, T>)method.CreateDelegate(typeof(Func<object, T>));
        }
        static void InstallDetailedRendering()
        {
            if (!_detailedPassHook)
            {
                try
                {
                    var graph = typeof(UnityEngine.Rendering.RenderGraphModule.RenderGraph);
                    var pass = graph.Assembly.GetType("UnityEngine.Rendering.RenderGraphModule.RenderGraphPass", true);
                    var context = graph.Assembly.GetType("UnityEngine.Rendering.RenderGraphModule.InternalRenderGraphContext", true);
                    var name = AccessTools.PropertyGetter(pass, "name");
                    var commands = AccessTools.Field(context, "cmd");
                    _originalPassExecute = AccessTools.Method(pass, "Execute", new[] { context });
                    var compiled = AccessTools.Method(graph, "ExecuteCompiledPass");
                    if (name == null || commands?.FieldType != typeof(CommandBuffer) || compiled == null || context.IsValueType)
                        throw new MissingMemberException("Render graph pass metadata");
                    _invokePass = RenderPassInvoker.Create(_originalPassExecute);
                    _passName = ReferenceGetter<string>(pass, name, null);
                    _passCommands = ReferenceGetter<CommandBuffer>(context, null, commands);
                    _harmony.Patch(compiled, transpiler: new HarmonyMethod(typeof(Main), nameof(DetailedPassTranspiler)));
                    _detailedPassHook = true;
                    _log.Log("[diagnostic] Render graph pass recording and GPU markers installed; original passes retained");
                }
                catch (Exception e) { _log.Error("[diagnostic] Pass hook unavailable: " + e.Message); }
            }
            UpdateDetailedRecording();
        }
        static IEnumerable<CodeInstruction> DetailedPassTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(instructions);
            int replaced = 0;
            foreach (var instruction in result)
                if (instruction.opcode == OpCodes.Callvirt && Equals(instruction.operand, _originalPassExecute))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(Main), nameof(DetailedPassExecute));
                    ++replaced;
                }
            if (replaced != 1) throw new InvalidOperationException("Unexpected render pass execution shape");
            return result;
        }
        static void DetailedPassExecute(object pass, object context)
        {
            if (!DiagnosticsRecording) { _invokePass(pass, context); return; }
            int eye = RenderingEyeIndex();
            if (!_cfg.detailedProfiling || !_detailedRunning || _detailedFailed || eye < 0)
            { _invokePass(pass, context); return; }
            PassMeasurement value = null;
            CommandBuffer commands = null;
            bool begun = false;
            bool nativeBegun = false;
            try
            {
                string name = _passName(pass);
                if (!_detailedPasses[eye].TryGetValue(name, out value))
                {
                    if (_detailedPasses[0].Count + _detailedPasses[1].Count < MaxPassMeasurements)
                    {
                        value = new PassMeasurement { Name = name, Eye = eye, NativeId = _nativeGpuPassCount++ };
                        _nativeGpuPasses[value.NativeId] = value;
                        // The game's ProfilingScope is compiled out. Explicit
                        // CommandBuffer samples are required; validate support.
                        if (_gpuPassRecorderSupported)
                        {
                            value.Sampler = CustomSampler.Create("RTMaquetaXR/" + (eye == 0 ? "L/" : "R/") + name, true);
                            value.SamplerValid = value.Sampler != null && value.Sampler.isValid;
                            if (value.SamplerValid)
                            {
                                value.Recorder = value.Sampler.GetRecorder();
                                value.SamplerValid = value.Recorder != null && value.Recorder.isValid;
                                if (value.SamplerValid) value.Recorder.enabled = _gpuMarkerProbe.Active;
                            }
                        }
                        _detailedPasses[eye][name] = value;
                    }
                }
                if (value != null)
                {
                    value.LastFrame = Time.frameCount;
                    if (value.LastNativeFrame != Time.frameCount && NativeGpuPassDiagnostics.Select(value.NativeId, Time.frameCount, _nativeGpuPassCount) &&
                        NativeGpuPassDiagnostics.Event != IntPtr.Zero)
                    {
                        commands = _passCommands(context);
                        if (commands != null)
                        {
                            commands.IssuePluginEvent(NativeGpuPassDiagnostics.Event, value.NativeId * 2 + 1);
                            value.LastNativeFrame = Time.frameCount; nativeBegun = true;
                        }
                    }
                    if (_gpuMarkerProbe.Active && value.SamplerValid && !value.Failed)
                    {
                        commands = _passCommands(context);
                        if (commands != null) { commands.BeginSample(value.Sampler); begun = true; }
                    }
                }
            }
            catch (Exception e) { DetailedFailure(e); }
            long started = DiagnosticsRecording ? Stopwatch.GetTimestamp() : 0;
            try { _invokePass(pass, context); }
            finally
            {
                // Never catch/retry the original pass. Preserve exception identity
                // and execute it once, including outside our own eye cameras.
                double elapsed = started != 0 && DiagnosticsRecording ? (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency : 0;
                if (started != 0 && DiagnosticsRecording && value != null && _consecutiveStereoFrames > 16)
                { value.RecordMs.Add(elapsed); ++value.Invocations; }
                if (begun)
                {
                    try { commands.EndSample(value.Sampler); }
                    catch (Exception e) { value.Failed = true; DetailedFailure(e); }
                }
                if (nativeBegun)
                {
                    try { commands.IssuePluginEvent(NativeGpuPassDiagnostics.Event, value.NativeId * 2 + 2); }
                    catch (Exception e) { DetailedFailure(e); }
                }
            }
        }
        static void DetailedFailure(Exception e)
        {
            if (!_detailedFailed) _log.Error("[diagnostic] Detailed pass recording disabled: " + e.Message);
            _detailedFailed = true;
        }
        static void UpdateDetailedRecording()
        {
            bool wanted = DiagnosticsRecording && _active && _cfg.detailedProfiling;
            if (_detailedRunning == wanted) return;
            _detailedRunning = wanted;
            if (wanted)
            {
                _gpuPassRecorderSupported = SystemInfo.supportsGpuRecorder;
                _gpuMarkerProbe.Start(_gpuPassRecorderSupported);
            }
            else
            { _gpuMarkerProbe.Start(false); _gpuProbeAttached = false; _gpuProbeCamera = null; }
            foreach (var eye in _detailedPasses) foreach (var pass in eye.Values)
                if (pass.Recorder != null && pass.SamplerValid) pass.Recorder.enabled = wanted && _gpuMarkerProbe.Active && !pass.Failed;
            if (!wanted)
            {
                foreach (var counter in _drawCounters) counter.Recorder.Dispose();
                foreach (var counter in _nativeUiCounters) counter.Recorder.Dispose();
                _nativeUiCounters.Clear();
                _drawCounters.Clear(); return;
            }
            foreach (string name in new[] { "Draw Calls Count", "SetPass Calls Count", "Total Batches Count", "Triangles Count", "Vertices Count",
                "Shadow Casters Count", "Vertex Buffer Upload In Frame Bytes", "Index Buffer Upload In Frame Bytes", "Render Textures Changes Count" })
            {
                var counter = new DrawCounter { Name = name };
                try { counter.Recorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, name, 1); }
                catch (Exception e) { counter.Error = e.Message; }
                _drawCounters.Add(counter);
            }
            _log.Log("[diagnostic] Drawing counters active; SystemInfo.supportsGpuRecorder=" + _gpuPassRecorderSupported);
            // Existing native markers only: never insert work into Canvas or
            // force a layout to obtain a measurement. Retail players may omit
            // these markers; unavailable observations are reported as null.
            foreach (string name in new[] { "Canvas.BuildBatch", "Canvas.SendWillRenderCanvases", "UGUI.Rendering.UpdateBatches", "UI.Rendering.UpdateBatches" })
            {
                var counter = new DrawCounter { Name = name };
                try
                {
                    counter.Recorder = ProfilerRecorder.StartNew(ProfilerCategory.Gui, name, 1);
                    if (counter.Recorder.Valid && counter.Recorder.UnitType.ToString() != "TimeNanoseconds")
                    { counter.Error = "Not a time marker"; counter.Recorder.Dispose(); }
                }
                catch (Exception e) { counter.Error = e.Message; }
                _nativeUiCounters.Add(counter);
            }
        }
        static void ReadDetailedFrame(bool steadyStereo)
        {
            UpdateDetailedRecording();
            if (!DiagnosticsRecording) return;
            bool attached = _active && _attached && !_modeFlat;
            if (_detailedRunning && attached && (!_gpuProbeAttached || _gpuProbeCamera != _attachedCam))
            {
                _gpuMarkerProbe.Start(_gpuPassRecorderSupported);
                foreach (var eye in _detailedPasses) foreach (var pass in eye.Values)
                    if (pass.Recorder != null && pass.SamplerValid) pass.Recorder.enabled = _gpuMarkerProbe.Active && !pass.Failed;
            }
            _gpuProbeAttached = attached; _gpuProbeCamera = attached ? _attachedCam : null;
            if (!_detailedRunning || !steadyStereo || _detailedReadFrame == Time.frameCount) return;
            _detailedReadFrame = Time.frameCount; ++_detailedReadFrames;
            if (NativeGpuPassDiagnostics.Read())
                for (int i = 0; i < _nativeGpuPassCount; ++i)
                {
                    var reading = NativeGpuPassDiagnostics.Ready[i]; var pass = _nativeGpuPasses[i];
                    pass.NativeRejected += reading.Rejected;
                    pass.NativeGpuMs.Samples += (int)Math.Min(reading.Observations, (ulong)int.MaxValue);
                    pass.NativeGpuMs.Sum += reading.SumMs;
                    pass.NativeGpuMs.Peak = Math.Max(pass.NativeGpuMs.Peak, reading.PeakMs);
                }
            foreach (var counter in _drawCounters)
            {
                try
                {
                    if (counter.Error == null && counter.Recorder.Valid && counter.Recorder.Count > 0)
                        counter.Values.Add(counter.Recorder.LastValue);
                }
                catch (Exception e) { counter.Error = e.Message; }
            }
            foreach (var counter in _nativeUiCounters)
            {
                try
                {
                    if (counter.Error == null && counter.Recorder.Valid && counter.Recorder.Count > 0)
                        counter.Values.Add(counter.Recorder.LastValue / 1000000.0);
                }
                catch (Exception e) { counter.Error = e.Message; }
            }
            if (!_gpuMarkerProbe.Active) return;
            bool observedGpuSample = false;
            foreach (var eye in _detailedPasses) foreach (var pass in eye.Values)
            {
                if (!pass.SamplerValid || pass.Failed || Time.frameCount - pass.LastFrame > 8) continue;
                try
                {
                    int gpuCount = pass.Recorder.gpuSampleBlockCount;
                    long gpuNs = pass.Recorder.gpuElapsedNanoseconds;
                    if (gpuCount > 0 && gpuNs > 0)
                    { pass.GpuMs.Add(gpuNs / 1000000.0); pass.GpuBlocks += gpuCount; observedGpuSample = true; }
                    int cpuCount = pass.Recorder.sampleBlockCount;
                    long cpuNs = pass.Recorder.elapsedNanoseconds;
                    if (cpuCount > 0 && cpuNs > 0) { pass.RenderThreadMs.Add(cpuNs / 1000000.0); pass.CpuBlocks += cpuCount; }
                }
                catch (Exception e) { pass.Failed = true; DetailedFailure(e); }
            }
            if (!_gpuMarkerProbe.Observe(observedGpuSample))
            {
                foreach (var eye in _detailedPasses) foreach (var pass in eye.Values)
                    if (pass.Recorder != null && pass.SamplerValid) pass.Recorder.enabled = false;
                _log.Log("[diagnostic] GPU pass recorders returned no observations in 240 steady frames; empty marker writes/reads suspended until next attachment. FrameTiming GPU and CPU phases retained.");
            }
        }
        static object DetailedValue(Measurement value)
        {
            return new { Mean = value.Samples > 0 ? (double?)Math.Round(value.Sum / value.Samples, 3) : null,
                Peak = value.Samples > 0 ? (double?)Math.Round(value.Peak, 3) : null, Observations = value.Samples };
        }
        static object DetailedRenderingSnapshot()
        {
            var counters = new Dictionary<string, object>();
            foreach (var counter in _drawCounters)
                counters[counter.Name] = new { Available = counter.Recorder.Valid, counter.Error, PerFrame = DetailedValue(counter.Values) };
            var uiCounters = new Dictionary<string, object>();
            foreach (var counter in _nativeUiCounters)
                uiCounters[counter.Name] = new { Available = counter.Recorder.Valid && counter.Error == null,
                    counter.Error, MillisecondsPerObservedFrame = DetailedValue(counter.Values) };
            var passes = new List<object>();
            foreach (var eye in _detailedPasses) foreach (var pass in eye.Values)
                if (pass.Invocations > 0 || pass.GpuMs.Samples > 0)
                    passes.Add(new { Eye = pass.Eye == 0 ? "Left" : "Right", pass.Name, pass.Invocations, pass.SamplerValid, pass.Failed,
                        CpuRecordingMsPerInvocation = DetailedValue(pass.RecordMs),
                        GpuMsPerObservedFrame = DetailedValue(pass.GpuMs), pass.GpuBlocks,
                        D3d11GpuMsPerSampledInvocation = DetailedValue(pass.NativeGpuMs), pass.NativeRejected,
                        RenderThreadMarkerMsPerObservedFrame = DetailedValue(pass.RenderThreadMs), pass.CpuBlocks });
            Camera camera = _runner == null ? null : _runner.GetEyeL();
            object view = null;
            if (camera != null)
            {
                var position = camera.transform.position; var angles = camera.transform.eulerAngles;
                // Do not serialize Vector3's recursive normalized property.
                view = new { Position = new { position.x, position.y, position.z }, EulerAngles = new { angles.x, angles.y, angles.z } };
            }
            return new { Enabled = _cfg.detailedProfiling, PassHook = _detailedPassHook, Failed = _detailedFailed,
                GpuRecorderSupported = _gpuPassRecorderSupported, ReadFrames = _detailedReadFrames,
                GpuMarkerStatus = _gpuMarkerProbe.State.ToString(), GpuMarkerProbeFrames = _gpuMarkerProbe.Frames,
                GpuMarkerProbeLimit = GpuMarkerProbe.MaximumProbeFrames, MissingGpuObservationsAreUnavailableNotZero = true,
                D3d11GpuTimestamps = new { Backend = "D3D11 timestamp/disjoint queries", NativeGpuPassDiagnostics.Error,
                    RegisteredPasses = _nativeGpuPassCount, MaximumPassIntervalsPerFrame = 1,
                    QueryPoolSlots = 16, RotationUsesRegisteredPasses = true,
                    ReadsUseDoNotFlush = true, QueriesReadOnlyOnRenderThread = true, MinimumSubmissionDelay = 3,
                    DurationsCoverQueuedPassCommandsOnly = true, NoFrameTimeAttribution = true },
                CountersIncludeAllGameCameras = true, CounterReadingsAreDelayed = true, GpuReadingsDelayedByFrames = 3,
                CpuRecordingIsIncludedInGraphExecute = true, MarkersMayOverlapDoNotSumWithFrameTimes = true,
                LastLeftEyeView = view, Counters = counters, NativeUiMarkers = uiCounters, Passes = passes };
        }
        static void ClearDetailedValue(Measurement value) { value.Samples = 0; value.Sum = value.Peak = 0; }
        static void ResetDetailedWindow()
        {
            _detailedReadFrames = 0;
            foreach (var counter in _drawCounters) ClearDetailedValue(counter.Values);
            foreach (var counter in _nativeUiCounters) ClearDetailedValue(counter.Values);
            foreach (var eye in _detailedPasses) foreach (var pass in eye.Values)
            {
                pass.Invocations = pass.CpuBlocks = pass.GpuBlocks = 0;
                ClearDetailedValue(pass.RecordMs); ClearDetailedValue(pass.GpuMs); ClearDetailedValue(pass.RenderThreadMs);
                ClearDetailedValue(pass.NativeGpuMs); pass.NativeRejected = 0;
            }
        }
        static void StopDetailedRendering()
        {
            foreach (var eye in _detailedPasses) foreach (var pass in eye.Values)
            { if (pass.Recorder != null && pass.SamplerValid) pass.Recorder.enabled = false; pass.LastFrame = -1; }
            foreach (var counter in _drawCounters) counter.Recorder.Dispose();
            foreach (var counter in _nativeUiCounters) counter.Recorder.Dispose();
            _nativeUiCounters.Clear();
            _drawCounters.Clear(); _detailedRunning = false; _detailedReadFrame = -1;
            ResetDetailedWindow();
        }
    }
}
