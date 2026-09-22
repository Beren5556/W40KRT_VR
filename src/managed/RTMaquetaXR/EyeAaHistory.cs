using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        delegate void EyeHistoryCopy(object instance, object renderingData, TextureHandle source);
        static EyeHistoryCopy _eyeHistoryCopy;
        static Type _eyeHistoryRenderingData;
        static FieldInfo _eyeHistoryCameraData, _eyeHistoryCamera, _eyeHistoryBuffer, _eyeHistoryBufferCamera, _eyeHistoryRenderer;
        static MethodInfo _eyeHistoryResources, _eyeHistoryColor;
        static bool _eyeHistoryHook, _eyeHistoryFailed;
        static readonly bool[] _eyeHistoryPending = { true, true };
        static readonly Camera[] _eyeHistorySeededCamera = new Camera[2];
        static readonly int[] _eyeHistoryCopies = new int[2];
        static string _eyeHistoryStatus = "not-installed";
        static string _eyeHistoryLastError;

        static void InstallEyeAaHistoryHook()
        {
            if (_eyeHistoryHook) return;
            try
            {
                var type = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.Passes.PostProcess.PostProcessPass");
                if (type == null) throw new MissingMemberException("PostProcessPass");
                MethodInfo taa = null;
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (method.Name == "DoTemporalAntialiasing")
                    {
                        if (taa != null) throw new InvalidOperationException("Ambiguous DoTemporalAntialiasing");
                        taa = method;
                    }
                if (taa == null || taa.ReturnType != typeof(void) || taa.IsStatic || taa.ContainsGenericParameters)
                    throw new MissingMethodException("DoTemporalAntialiasing");
                var parameters = taa.GetParameters();
                if (parameters.Length != 3 || !parameters[0].ParameterType.IsByRef ||
                    parameters[1].ParameterType != typeof(TextureHandle) || parameters[2].ParameterType != typeof(TextureHandle))
                    throw new InvalidOperationException("Unexpected TAA arguments");
                _eyeHistoryRenderingData = parameters[0].ParameterType.GetElementType();
                if (!_eyeHistoryRenderingData.IsValueType || _eyeHistoryRenderingData.FullName != "Owlcat.Runtime.Visual.Waaagh.RenderingData")
                    throw new InvalidOperationException("Unexpected RenderingData");
                var copy = AccessTools.Method(type, "DoCopyHistory", new[] { parameters[0].ParameterType, typeof(TextureHandle) });
                if (copy == null || copy.IsStatic || copy.ReturnType != typeof(void) || copy.ContainsGenericParameters)
                    throw new MissingMethodException("DoCopyHistory(ref RenderingData, TextureHandle)");

                _eyeHistoryCameraData = AccessTools.Field(_eyeHistoryRenderingData, "CameraData");
                var cameraData = _eyeHistoryCameraData == null ? null : _eyeHistoryCameraData.FieldType;
                if (cameraData == null || !cameraData.IsValueType || cameraData.FullName != "Owlcat.Runtime.Visual.Waaagh.CameraData")
                    throw new MissingFieldException("RenderingData.CameraData");
                _eyeHistoryCamera = AccessTools.Field(cameraData, "Camera");
                _eyeHistoryBuffer = AccessTools.Field(cameraData, "CameraBuffer");
                _eyeHistoryRenderer = AccessTools.Field(cameraData, "Renderer");
                if (_eyeHistoryCamera == null || _eyeHistoryCamera.FieldType != typeof(Camera) ||
                    _eyeHistoryBuffer == null || _eyeHistoryRenderer == null)
                    throw new MissingFieldException("CameraData camera/buffer/renderer");
                _eyeHistoryBufferCamera = AccessTools.Field(_eyeHistoryBuffer.FieldType, "Camera");
                _eyeHistoryResources = AccessTools.PropertyGetter(_eyeHistoryRenderer.FieldType, "RenderGraphResources");
                if (_eyeHistoryBufferCamera == null || _eyeHistoryBufferCamera.FieldType != typeof(Camera) ||
                    _eyeHistoryResources == null || _eyeHistoryResources.IsStatic || _eyeHistoryResources.GetParameters().Length != 0)
                    throw new MissingMemberException("CameraBuffer.Camera / Renderer.RenderGraphResources");
                _eyeHistoryColor = AccessTools.PropertyGetter(_eyeHistoryResources.ReturnType, "CameraHistoryColorBuffer");
                if (_eyeHistoryColor == null || _eyeHistoryColor.IsStatic || _eyeHistoryColor.GetParameters().Length != 0 ||
                    _eyeHistoryColor.ReturnType != typeof(TextureHandle))
                    throw new MissingMemberException("RenderGraphResources.CameraHistoryColorBuffer");

                // unbox gives DoCopyHistory the address of the boxed value, not
                // a temporary copy. Our entry hook writes it back to the original
                // ref RenderingData after the call, preserving any changes.
                var invoke = new DynamicMethod("RTMaquetaXR_CopyEyeHistory", typeof(void),
                    new[] { typeof(object), typeof(object), typeof(TextureHandle) }, typeof(Main).Module, true);
                var il = invoke.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, copy.DeclaringType);
                il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Unbox, _eyeHistoryRenderingData);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(copy.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, copy); il.Emit(OpCodes.Ret);
                _eyeHistoryCopy = (EyeHistoryCopy)invoke.CreateDelegate(typeof(EyeHistoryCopy));
                _harmony.Patch(taa, transpiler: new HarmonyMethod(typeof(Main), nameof(EyeAaHistoryTranspiler)));
                _eyeHistoryHook = true; _eyeHistoryFailed = false; _eyeHistoryStatus = "ready"; _eyeHistoryLastError = null;
                _log.Log("[quality] Eye TAA history can be seeded from its current source without releasing render textures");
            }
            catch (Exception e)
            {
                _eyeHistoryCopy = null; _eyeHistoryStatus = "hook-unavailable";
                _eyeHistoryLastError = e.Message;
                _log.Error("[quality] Eye history initialization unavailable; original TAA retained: " + e.Message);
            }
        }

        static void RequestEyeAaHistoryReset()
        {
            RequestNeuralHistoryReset();
            _eyeHistoryPending[0] = _eyeHistoryPending[1] = true;
            if (_eyeHistoryHook && !_eyeHistoryFailed) _eyeHistoryStatus = "pending";
        }

        static IEnumerable<CodeInstruction> EyeAaHistoryTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var original = new List<CodeInstruction>(instructions);
            if (original.Count == 0 || _eyeHistoryRenderingData == null || _eyeHistoryCopy == null)
                throw new InvalidOperationException("TAA history hook was not prepared");
            var proceed = generator.DefineLabel();
            original[0].labels.Add(proceed);
            var result = new List<CodeInstruction> {
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Main), nameof(NeedsEyeAaHistory))),
                new CodeInstruction(OpCodes.Brfalse, proceed),
                // Keep the original ref as the destination for copy-back.
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldobj, _eyeHistoryRenderingData),
                new CodeInstruction(OpCodes.Box, _eyeHistoryRenderingData),
                new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Main), nameof(SeedEyeAaHistory))),
                new CodeInstruction(OpCodes.Unbox_Any, _eyeHistoryRenderingData),
                new CodeInstruction(OpCodes.Stobj, _eyeHistoryRenderingData)
            };
            result.AddRange(original);
            return result;
        }

        static bool NeedsEyeAaHistory()
        {
            int eye = RenderingEyeIndex();
            return eye >= 0 && !NeuralTemporalHook.ReplacesTaa(_renderingCamera) && _eyeHistoryHook && !_eyeHistoryFailed && EffectiveEyeAa == EyeAaMode.Taa &&
                (_eyeHistoryPending[eye] || _eyeHistorySeededCamera[eye] != _renderingCamera);
        }

        static object SeedEyeAaHistory(object instance, object renderingData, TextureHandle source)
        {
            int eye = RenderingEyeIndex();
            if (eye < 0 || !NeedsEyeAaHistory()) return renderingData;
            var camera = _renderingCamera;
            try
            {
                if (renderingData == null || renderingData.GetType() != _eyeHistoryRenderingData)
                    throw new InvalidOperationException("Unexpected TAA call data");
                object cameraData = _eyeHistoryCameraData.GetValue(renderingData);
                object buffer = _eyeHistoryBuffer.GetValue(cameraData);
                if ((Camera)_eyeHistoryCamera.GetValue(cameraData) != camera || buffer == null ||
                    (Camera)_eyeHistoryBufferCamera.GetValue(buffer) != camera)
                { _eyeHistoryStatus = "camera-ownership-mismatch"; return renderingData; }
                object renderer = _eyeHistoryRenderer.GetValue(cameraData);
                if (renderer == null) { _eyeHistoryStatus = "renderer-unavailable"; return renderingData; }
                object resources = _eyeHistoryResources.Invoke(renderer, null);
                if (resources == null) { _eyeHistoryStatus = "resources-unavailable"; return renderingData; }
                var history = (TextureHandle)_eyeHistoryColor.Invoke(resources, null);
                // These are the same resources DoTemporalAntialiasing will use.
                // Never initialize another camera's history or self-blit a handle.
                if (!source.IsValid() || !history.IsValid() || source.Equals(history))
                { _eyeHistoryStatus = "invalid-or-identical-handles"; return renderingData; }
            }
            catch (Exception e)
            {
                FailEyeAaHistory("validation", e);
                return renderingData; // No render graph mutation has happened yet.
            }

            try
            {
                // The game's copy schedules pass 4 of its TAA material, which
                // is also the TAA history-write pass. The graph's write followed
                // by TAA's read/write orders initialization before accumulation.
                _eyeHistoryCopy(instance, renderingData, source);
            }
            catch (Exception e)
            {
                FailEyeAaHistory("copy", e);
                // Copy may already have modified the graph. Do not continue TAA
                // with a partial graph or wrap/suppress the original exception.
                throw;
            }
            _eyeHistoryPending[eye] = false; _eyeHistorySeededCamera[eye] = camera;
            ++_eyeHistoryCopies[eye]; _eyeHistoryStatus = "seeded";
            _log.Log("[quality] TAA history seeded for " + camera.name + "; frame=" + Time.frameCount);
            return renderingData;
        }

        static void FailEyeAaHistory(string stage, Exception error)
        {
            if (!_eyeHistoryFailed) _log.Error("[quality] Eye history initialization disabled at " + stage + ": " + error.Message);
            _eyeHistoryFailed = true; _eyeHistoryStatus = stage + "-failed";
            _eyeHistoryLastError = error.Message;
        }

        static object EyeAaHistorySnapshot() => new {
            Hook = _eyeHistoryHook, Failed = _eyeHistoryFailed, Status = _eyeHistoryStatus, LastError = _eyeHistoryLastError,
            LeftPending = _eyeHistoryPending[0], RightPending = _eyeHistoryPending[1],
            LeftCopiesSinceStart = _eyeHistoryCopies[0], RightCopiesSinceStart = _eyeHistoryCopies[1],
            CountsAreSuccessfullyRecordedCopyPasses = true
        };

        static void StopEyeAaHistory()
        {
            _eyeHistoryPending[0] = _eyeHistoryPending[1] = false;
            _eyeHistorySeededCamera[0] = _eyeHistorySeededCamera[1] = null;
            _eyeHistoryFailed = false; _eyeHistoryStatus = "stopped";
        }
    }
}
