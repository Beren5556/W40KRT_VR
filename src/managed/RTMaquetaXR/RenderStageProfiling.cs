using System;
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
        enum EyeRenderStage { Cull, InitializeData, Setup, BuildAndExecute, GraphExecute, Submit }
        struct EyeStageScope { public long Started; public int Eye; public EyeRenderStage Stage; }
        struct EyeCullInfo
        {
            public bool Observed, OcclusionRequested;
            public int VisibleLights, PeakVisibleLights;
            public long VisibleLightSum;
            public int LightSamples;
            public CullingOptions Options;
            public float ShadowDistance;
        }
        static readonly Measurement[,] _eyeStages = new Measurement[2, 6];
        static readonly EyeCullInfo[] _eyeCullInfo = new EyeCullInfo[2];
        static readonly Dictionary<MethodBase, EyeRenderStage> _stageHookMethods = new Dictionary<MethodBase, EyeRenderStage>();
        static bool _eyeCullSubmitHook;

        static int RenderingEyeIndex()
        {
            if (!_active || _modeFlat || _runner == null || _renderingCamera == null) return -1;
            if (_renderingCamera == _runner.GetEyeL()) return 0;
            if (_renderingCamera == _runner.GetEyeR()) return 1;
            return -1;
        }
        static EyeStageScope BeginEyeStage(EyeRenderStage stage)
        {
            int eye = RenderingEyeIndex();
            // Eye identity is still needed by visible-region culling when
            // diagnostics are OFF. Only the stopwatch/aggregation is disabled.
            return new EyeStageScope { Eye = eye, Stage = stage, Started = eye < 0 || !DiagnosticsRecording ? 0 : Stopwatch.GetTimestamp() };
        }
        static void EndEyeStage(EyeStageScope scope)
        {
            if (scope.Started == 0 || !DiagnosticsRecording) return;
            var value = _eyeStages[scope.Eye, (int)scope.Stage];
            if (value == null) _eyeStages[scope.Eye, (int)scope.Stage] = value = new Measurement();
            double elapsed = (Stopwatch.GetTimestamp() - scope.Started) * 1000.0 / Stopwatch.Frequency;
            value.Add(elapsed);
            // These synchronous main-thread scopes finish in this Unity frame.
            // Sum both eyes into the same four-frame ring as Game.Tick, so the
            // next interval's hitch can identify its actual previous-frame work.
            // GraphExecute is already nested in BuildAndExecute: never add it.
            if (!GameCpuProfilingActive) return;
            if (scope.Stage == EyeRenderStage.Cull)
                RecordGameCpuPhase(Time.frameCount, (int)GameCpuPhase.StereoCull, elapsed);
            else if (scope.Stage == EyeRenderStage.Submit)
                RecordGameCpuPhase(Time.frameCount, (int)GameCpuPhase.StereoSubmit, elapsed);
            else if (scope.Stage == EyeRenderStage.BuildAndExecute)
                RecordGameCpuPhase(Time.frameCount, (int)GameCpuPhase.StereoBuildAndExecute, elapsed);
        }

        static void InstallRenderStageHooks()
        {
            InstallGameCpuProfiling();
            var pipeline = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.WaaaghPipeline");
            var renderer = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.ScriptableRenderer");
            var graph = typeof(UnityEngine.Rendering.RenderGraphModule.RenderGraph);
            InstallStageMethod(pipeline, "InitializeRenderingData", EyeRenderStage.InitializeData);
            InstallStageMethod(renderer, "SetupInternal", EyeRenderStage.Setup);
            InstallStageMethod(renderer, "Execute", EyeRenderStage.BuildAndExecute);
            InstallStageMethod(graph, "EndRecordingAndExecute", EyeRenderStage.GraphExecute);
            if (_eyeCullSubmitHook) return;
            try
            {
                MethodInfo target = null;
                foreach (var method in pipeline.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    if (method.Name == "RenderSingleCamera" && method.GetParameters().Length == 2 &&
                        method.GetParameters()[1].ParameterType.IsByRef && method.GetParameters()[1].ParameterType.GetElementType().Name == "CameraData")
                    {
                        if (target != null) throw new InvalidOperationException("Ambiguous RenderSingleCamera");
                        target = method;
                    }
                if (target == null) throw new MissingMethodException("RenderSingleCamera with CameraData");
                _harmony.Patch(target, transpiler: new HarmonyMethod(typeof(Main), nameof(RenderStageTranspiler)));
                _eyeCullSubmitHook = true;
                _log.Log("[performance] Eye render stages: cull, data, setup, graph and submit; all original operations retained");
            }
            catch (Exception e) { _log.Error("[performance] Cull/submit measurement: " + e.Message); }
        }
        static void InstallStageMethod(Type type, string name, EyeRenderStage stage)
        {
            try
            {
                var method = AccessTools.Method(type, name);
                if (method == null) throw new MissingMethodException(name);
                if (_stageHookMethods.ContainsKey(method)) return;
                _stageHookMethods[method] = stage;
                try
                {
                    _harmony.Patch(method,
                        prefix: new HarmonyMethod(typeof(Main), nameof(RenderStagePrefix)),
                        finalizer: new HarmonyMethod(typeof(Main), nameof(RenderStageFinalizer)));
                }
                catch { _stageHookMethods.Remove(method); throw; }
            }
            catch (Exception e) { _log.Error("[performance] " + name + " measurement: " + e.Message); }
        }
        static void RenderStagePrefix(MethodBase __originalMethod, out EyeStageScope __state)
        {
            if (!DiagnosticsRecording) { __state = default; return; }
            __state = _stageHookMethods.TryGetValue(__originalMethod, out var stage) ? BeginEyeStage(stage) : default;
        }
        static Exception RenderStageFinalizer(Exception __exception, EyeStageScope __state)
        {
            EndEyeStage(__state); return __exception;
        }
        static IEnumerable<CodeInstruction> RenderStageTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(instructions);
            var cull = AccessTools.Method(typeof(ScriptableRenderContext), nameof(ScriptableRenderContext.Cull),
                new[] { typeof(ScriptableCullingParameters).MakeByRefType() });
            var submit = AccessTools.Method(typeof(ScriptableRenderContext), nameof(ScriptableRenderContext.Submit), Type.EmptyTypes);
            int culls = 0, submits = 0;
            foreach (var instruction in result)
            {
                if (instruction.opcode != OpCodes.Call) continue;
                if (Equals(instruction.operand, cull))
                { ++culls; instruction.operand = AccessTools.Method(typeof(Main), nameof(ProfiledEyeCull)); }
                else if (Equals(instruction.operand, submit))
                { ++submits; instruction.operand = AccessTools.Method(typeof(Main), nameof(ProfiledEyeSubmit)); }
            }
            // A value-type instance call already has a by-ref context on the
            // stack. The static wrappers accept that same context and parameters.
            if (culls != 1 || submits != 1) throw new InvalidOperationException("Unexpected cull/submit call sites");
            return result;
        }
        static CullingResults ProfiledEyeCull(ref ScriptableRenderContext context, ref ScriptableCullingParameters parameters)
        {
            var scope = BeginEyeStage(EyeRenderStage.Cull);
            CullingResults result;
            try { ApplyVisibleCulling(scope.Eye, ref parameters); result = context.Cull(ref parameters); }
            finally { EndEyeStage(scope); }
            if (scope.Eye >= 0 && DiagnosticsRecording)
            {
                var info = _eyeCullInfo[scope.Eye];
                info.Observed = true;
                info.VisibleLights = result.visibleLights.Length;
                info.PeakVisibleLights = Math.Max(info.PeakVisibleLights, info.VisibleLights);
                info.VisibleLightSum += info.VisibleLights; ++info.LightSamples;
                info.Options = parameters.cullingOptions; info.ShadowDistance = parameters.shadowDistance;
                info.OcclusionRequested = _renderingCamera.useOcclusionCulling;
                _eyeCullInfo[scope.Eye] = info;
            }
            return result;
        }
        static void ProfiledEyeSubmit(ref ScriptableRenderContext context)
        {
            var scope = BeginEyeStage(EyeRenderStage.Submit);
            try { context.Submit(); }
            finally { EndEyeStage(scope); }
        }
        static object RenderStageSnapshot()
        {
            var eyes = new Dictionary<string, object>();
            for (int eye = 0; eye < 2; ++eye)
            {
                var stages = new Dictionary<string, object>();
                for (int stage = 0; stage < 6; ++stage)
                {
                    var value = _eyeStages[eye, stage];
                    if (value == null || value.Samples == 0) continue;
                    stages[((EyeRenderStage)stage).ToString()] = new {
                        MeanMs = Math.Round(value.Sum / value.Samples, 3), PeakMs = Math.Round(value.Peak, 3), Samples = value.Samples
                    };
                }
                var cull = _eyeCullInfo[eye];
                eyes[eye == 0 ? "Left" : "Right"] = new {
                    CpuStages = stages,
                    Culling = new { cull.Observed, cull.VisibleLights, cull.PeakVisibleLights,
                        MeanVisibleLights = cull.LightSamples > 0 ? (double?)Math.Round((double)cull.VisibleLightSum / cull.LightSamples, 2) : null,
                        cull.LightSamples, Options = cull.Options.ToString(), cull.ShadowDistance, cull.OcclusionRequested }
                };
            }
            return new {
                CullSubmitHook = _eyeCullSubmitHook, ManagedStageHooks = _stageHookMethods.Count,
                GraphExecuteIsIncludedInBuildAndExecute = true, TimingsAreCpuWallTime = true,
                RenderingThreadingMode = SystemInfo.renderingThreadingMode.ToString(), Eyes = eyes
            };
        }
        static void ResetRenderStageWindow()
        {
            ResetGameCpuWindow();
            ResetVisibleCullWindow();
            ResetIndirectRenderingWindow();
            for (int eye = 0; eye < 2; ++eye)
            {
                _eyeCullInfo[eye] = default;
                for (int stage = 0; stage < 6; ++stage)
                {
                    var value = _eyeStages[eye, stage];
                    if (value != null) { value.Samples = 0; value.Sum = value.Peak = 0; }
                }
            }
        }
    }
}
