using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        sealed class IndirectEyeInfo
        {
            public int Applied, Fallback;
            public string LastStatus = "not-called";
            public readonly Measurement InputInstances = new Measurement();
        }
        sealed class IndirectDrawCount
        {
            public string Pass;
            public int Eye, Invocations, FramesSeen, LastFrame = -1;
            public long Commands;
        }
        static readonly IndirectEyeInfo[] _indirectEyes = { new IndirectEyeInfo(), new IndirectEyeInfo() };
        static readonly Dictionary<string, IndirectDrawCount>[] _indirectDraws = {
            new Dictionary<string, IndirectDrawCount>(), new Dictionary<string, IndirectDrawCount>()
        };
        static IndirectDrawCount _currentIndirectDraw;
        static bool _indirectCullHook, _indirectDrawHook, _indirectFailed;

        static void InstallIndirectRenderingHooks()
        {
            var type = AccessTools.TypeByName("Owlcat.Runtime.Visual.IndirectRendering.IndirectRenderingSystem");
            if (!_indirectCullHook)
            {
                try
                {
                    var method = type == null ? null : AccessTools.Method(type, "Cull", new[] { typeof(ScriptableRenderContext), typeof(Camera) });
                    if (method == null) throw new MissingMethodException("IndirectRenderingSystem.Cull");
                    _harmony.Patch(method, transpiler: new HarmonyMethod(typeof(Main), nameof(IndirectCullTranspiler)));
                    _indirectCullHook = true;
                    _log.Log("[performance] Indirect culling uses the validated visible region for each eye; drawing projection retained");
                }
                catch (Exception e) { _log.Error("[performance] Indirect culling hook unavailable: " + e.Message); }
            }
            if (!_indirectDrawHook)
            {
                try
                {
                    var method = type == null ? null : AccessTools.Method(type, "DrawPassInternal",
                        new[] { typeof(CommandBuffer), typeof(string), typeof(RenderQueueRange), typeof(bool) });
                    if (method == null) throw new MissingMethodException("IndirectRenderingSystem.DrawPassInternal");
                    _harmony.Patch(method, prefix: new HarmonyMethod(typeof(Main), nameof(IndirectDrawPrefix)),
                        transpiler: new HarmonyMethod(typeof(Main), nameof(IndirectDrawTranspiler)),
                        finalizer: new HarmonyMethod(typeof(Main), nameof(IndirectDrawFinalizer)));
                    _indirectDrawHook = true;
                }
                catch (Exception e) { _log.Error("[diagnostic] Indirect draw counter unavailable: " + e.Message); }
            }
        }

        static IEnumerable<CodeInstruction> IndirectCullTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(instructions);
            var getter = AccessTools.PropertyGetter(typeof(Camera), "projectionMatrix");
            var viewGetter = AccessTools.PropertyGetter(typeof(Camera), "worldToCameraMatrix");
            var setMatrix = AccessTools.Method(typeof(CommandBuffer), nameof(CommandBuffer.SetComputeMatrixParam),
                new[] { typeof(ComputeShader), typeof(int), typeof(Matrix4x4) });
            var setInt = AccessTools.Method(typeof(CommandBuffer), nameof(CommandBuffer.SetComputeIntParam),
                new[] { typeof(ComputeShader), typeof(int), typeof(int) });
            int projections = 0, views = 0, matrixWrites = 0, instanceCounts = 0;
            for (int i = 0; i < result.Count; ++i)
            {
                var instruction = result[i];
                if (instruction.opcode != OpCodes.Callvirt) continue;
                if (Equals(instruction.operand, getter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(Main), nameof(IndirectCullingProjection)); ++projections;
                }
                else if (Equals(instruction.operand, viewGetter)) ++views;
                else if (Equals(instruction.operand, setMatrix)) ++matrixWrites;
                else if (Equals(instruction.operand, setInt) && i >= 2 && result[i - 2].opcode == OpCodes.Ldsfld &&
                    result[i - 2].operand is FieldInfo field && field.Name == "_TotalInstanceCount" &&
                    field.DeclaringType.FullName == "Owlcat.Runtime.Visual.IndirectRendering.ShaderPropertyId")
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(Main), nameof(RecordIndirectInstanceCount)); ++instanceCounts;
                }
            }
            if (projections != 1 || views != 1 || matrixWrites != 1 || instanceCounts != 1)
                throw new InvalidOperationException("Unexpected indirect culling call sites");
            return result;
        }

        static bool SameCullMatrix(Matrix4x4 current, Matrix4x4 expected, float jitterX = 0, float jitterY = 0)
        {
            for (int row = 0; row < 4; ++row) for (int column = 0; column < 4; ++column)
            {
                float a = current[row, column], b = expected[row, column];
                if (!VisibleFrustum.Finite(a) || !VisibleFrustum.Finite(b)) return false;
                float tolerance = .00001f * Math.Max(1, Math.Abs(b));
                if (row == 0 && column == 2) tolerance += jitterX;
                if (row == 1 && column == 2) tolerance += jitterY;
                if (Math.Abs(a - b) > tolerance) return false;
            }
            return true;
        }

        static Matrix4x4 IndirectCullingProjection(Camera camera)
        {
            // Preserve the original getter and its exception. The effective
            // camera may be the game's DebugCamera, which must remain intact.
            var original = camera.projectionMatrix;
            int eye = RenderingEyeIndex();
            if (eye < 0) return original;
            var info = _indirectEyes[eye];
            var state = _visibleCull[eye];
            string reason = null;
            try
            {
                if (!_cfg.indirectVisibleRegionCulling || !_cfg.visibleRegionCulling) reason = "disabled";
                else if (_indirectFailed || _visibleCullFailed) reason = "exception-disabled";
                else if (camera != _renderingCamera || state.Camera != camera) reason = "different-camera";
                else if (!OpenXR.FlipEyes || !state.EffectsSafe) reason = "effects-or-orientation";
                else if (state.PreparedFrame != Time.frameCount || state.CheckedFrame != Time.frameCount ||
                    state.AppliedFrame != Time.frameCount) reason = "no-applied-frustum-this-frame";
                else if (!SameCullMatrix(camera.worldToCameraMatrix, state.AppliedView)) reason = "changed-view";
                else if (!SameCullMatrix(original, state.Projection,
                    4 / (state.Width * state.RenderScale), 4 / (state.Height * state.RenderScale))) reason = "changed-projection";
                if (reason == null)
                {
                    var xy = state.AppliedProjectionXY;
                    var candidate = original;
                    candidate.m00 = xy.M00; candidate.m02 = xy.M02;
                    candidate.m11 = xy.M11; candidate.m12 = xy.M12;
                    // Z/W and every other element remain exactly original.
                    // No GL/GPU conversion here: Cull originally uses CPU P*V.
                    ++info.Applied; info.LastStatus = "applied";
                    return candidate;
                }
            }
            catch (Exception e)
            {
                if (!_indirectFailed) _log.Error("[performance] Indirect visible culling disabled: " + e.Message);
                _indirectFailed = true; reason = "exception";
            }
            ++info.Fallback; info.LastStatus = reason;
            return original;
        }

        static void RecordIndirectInstanceCount(CommandBuffer command, ComputeShader shader, int property, int count)
        {
            command.SetComputeIntParam(shader, property, count);
            int eye = RenderingEyeIndex();
            if (eye >= 0 && _cfg.detailedProfiling && _consecutiveStereoFrames > 16 && count >= 0)
                _indirectEyes[eye].InputInstances.Add(count);
        }

        static MethodInfo IndirectDrawMethod() => AccessTools.Method(typeof(CommandBuffer), nameof(CommandBuffer.DrawMeshInstancedIndirect),
            new[] { typeof(Mesh), typeof(int), typeof(Material), typeof(int), typeof(ComputeBuffer), typeof(int), typeof(MaterialPropertyBlock) });

        static IEnumerable<CodeInstruction> IndirectDrawTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(instructions);
            var original = IndirectDrawMethod(); int replaced = 0;
            foreach (var instruction in result)
                if (instruction.opcode == OpCodes.Callvirt && Equals(instruction.operand, original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(Main), nameof(CountIndirectDraw)); ++replaced;
                }
            if (replaced != 1) throw new InvalidOperationException("Unexpected indirect drawing call sites");
            return result;
        }
        static void IndirectDrawPrefix(string __1, out IndirectDrawCount __state)
        {
            __state = _currentIndirectDraw; _currentIndirectDraw = null;
            int eye = RenderingEyeIndex();
            if (eye < 0 || !_cfg.detailedProfiling || _consecutiveStereoFrames <= 16 || __1 == null) return;
            if (!_indirectDraws[eye].TryGetValue(__1, out var counter))
            {
                if (_indirectDraws[eye].Count >= 32) return;
                counter = new IndirectDrawCount { Eye = eye, Pass = __1 };
                _indirectDraws[eye][__1] = counter;
            }
            ++counter.Invocations;
            if (counter.LastFrame != Time.frameCount) { counter.LastFrame = Time.frameCount; ++counter.FramesSeen; }
            _currentIndirectDraw = counter;
        }
        static void CountIndirectDraw(CommandBuffer command, Mesh mesh, int submesh, Material material,
            int shaderPass, ComputeBuffer args, int offset, MaterialPropertyBlock properties)
        {
            command.DrawMeshInstancedIndirect(mesh, submesh, material, shaderPass, args, offset, properties);
            // Constant cost, no camera lookup, material scan or GPU readback.
            if (_currentIndirectDraw != null) ++_currentIndirectDraw.Commands;
        }
        static Exception IndirectDrawFinalizer(Exception __exception, IndirectDrawCount __state)
        { _currentIndirectDraw = __state; return __exception; }

        static object IndirectRenderingSnapshot()
        {
            var eyes = new object[2]; var draws = new List<object>();
            for (int eye = 0; eye < 2; ++eye)
            {
                var info = _indirectEyes[eye];
                eyes[eye] = new { Eye = eye == 0 ? "Left" : "Right", info.Applied, info.Fallback, info.LastStatus,
                    InputInstancesPerCull = DetailedValue(info.InputInstances) };
                foreach (var count in _indirectDraws[eye].Values)
                    if (count.Invocations > 0) draws.Add(new { Eye = eye == 0 ? "Left" : "Right", count.Pass,
                        count.Commands, count.Invocations, count.FramesSeen,
                        CommandsPerInvokedFrame = count.FramesSeen > 0 ? (double?)Math.Round((double)count.Commands / count.FramesSeen, 3) : null });
            }
            return new { Enabled = _cfg.indirectVisibleRegionCulling, CullHook = _indirectCullHook, DrawCounterHook = _indirectDrawHook,
                Failed = _indirectFailed, DrawCountersEnabled = _cfg.detailedProfiling,
                CountersAreCpuRequestsNotGpuVisibleInstances = true, Eyes = eyes, DrawCommands = draws };
        }
        static void ResetIndirectRenderingWindow()
        {
            foreach (var info in _indirectEyes) { info.Applied = info.Fallback = 0; ClearDetailedValue(info.InputInstances); }
            foreach (var eye in _indirectDraws) foreach (var count in eye.Values)
            { count.Commands = count.Invocations = count.FramesSeen = 0; count.LastFrame = -1; }
        }
        static void StopIndirectRendering()
        {
            _currentIndirectDraw = null; _indirectFailed = false;
            ResetIndirectRenderingWindow();
            foreach (var info in _indirectEyes) info.LastStatus = "stopped";
        }
    }
}
