using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace RTMaquetaXR
{
    internal static partial class NeuralTemporalHook
    {
        static MethodInfo rawRegistrationBridge, rawHistoryMethod, rawResolutionMethod;
        static Func<object, int> sharedHistoryCount;
        static long rawPasses, rawFallbackCopies, rawPromotedSkips, originalTaaPassesBypassed;

        sealed class RawTemporalPassData
        {
            internal TextureHandle Color, Output, Depth, Motion;
            internal Vector2Int Pixels;
            internal bool EvaluateAtNativeSize, Promoted;
        }
        sealed class SharedHistoryPassData
        {
            internal TextureHandle Color, History;
            internal Vector2Int Pixels;
        }

        // This gate deliberately stays true after an NGX failure. Falling through to
        // the original TAA pass on failure would violate the user's no-AA fallback.
        internal static bool ReplacesTaa(Camera camera)
        {
            if (!installed || requestedMode == 0 || camera == null) return false;
            try { return ownsEye != null && ownsEye(camera); }
            catch (Exception error) { Fail("raw-owner", error); return false; }
        }
        static bool ReplacesCurrentEyeTaa() => getCamera != null && ReplacesTaa(getCamera());

        static void PrepareRawTemporalContract()
        {
            sharedHistoryCount = Getter<int>(Field(bufferField.FieldType, "m_HistoryColorFramesCount", typeof(int)));
            var graphField = Field(renderingDataType, "RenderGraph", typeof(RenderGraph));
            var renderer = Field(cameraDataField.FieldType, "Renderer", null);
            var resources = UniqueMethod(renderer.FieldType, "get_RenderGraphResources");
            var depth = Field(resources.ReturnType, "CameraDepthCopyRT", typeof(TextureHandle));
            var motion = Field(resources.ReturnType, "CameraMotionVectorsRT", typeof(TextureHandle));
            var history = UniqueMethod(resources.ReturnType, "get_CameraHistoryColorBuffer");
            var method = new DynamicMethod("NeuralRegisterRaw", typeof(void),
                new[] { renderingDataType.MakeByRefType(), typeof(TextureHandle), typeof(TextureHandle) },
                typeof(NeuralTemporalHook).Module, true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, graphField);
            foreach (var field in new[] { cameraField, bufferField, hdrField })
            { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldflda, cameraDataField); il.Emit(OpCodes.Ldfld, field); }
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldflda, cameraDataField); il.Emit(OpCodes.Call, projectionFlippedMethod);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2);
            foreach (var field in new[] { depth, motion })
            {
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldflda, cameraDataField);
                il.Emit(OpCodes.Ldfld, renderer); il.Emit(OpCodes.Callvirt, resources); il.Emit(OpCodes.Ldfld, field);
            }
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldflda, cameraDataField);
            il.Emit(OpCodes.Ldfld, renderer); il.Emit(OpCodes.Callvirt, resources); il.Emit(OpCodes.Callvirt, history);
            il.Emit(OpCodes.Call, AccessTools.Method(typeof(NeuralTemporalHook), nameof(RegisterRawTemporal)));
            il.Emit(OpCodes.Ret); rawRegistrationBridge = method;
        }

        static void RegisterRawTemporal(RenderGraph graph, Camera camera, object buffer, bool hdr, bool flipped,
            TextureHandle source, TextureHandle destination, TextureHandle depth, TextureHandle motion, TextureHandle sharedHistory)
        {
            // Register a plain current-frame copy first. It remains valid even when metadata
            // or native configuration fails. No TAA pass or history dependency is registered.
            using (var builder = graph.AddRenderPass<RawTemporalPassData>("RTMaquetaXR raw scene / DLAA", out var data))
            {
                data.Color = builder.ReadTexture(source); data.Output = builder.WriteTexture(destination);
                data.Depth = builder.ReadTexture(depth); data.Motion = builder.ReadTexture(motion);
                data.Pixels = bufferPixels(buffer); data.EvaluateAtNativeSize = requestedMode != 3;
                // RenderGraph pools pass-data instances across eyes, frames AND modes.
                // A DLSS-promoted instance must execute normally when reused for DLAA.
                data.Promoted = false;
                builder.SetRenderFunc<RawTemporalPassData>(RenderRawTemporal);
                // For DLSS, promotion reads raw Color directly. Its unused low copy can be
                // culled; when promotion fails, ordinary post-processing consumes this copy.
                builder.AllowPassCulling(true);
                Record(data, camera, buffer, hdr, flipped);
                ++originalTaaPassesBypassed;
            }
            // EnsureCamera still allocates this history when SSR or VFX actually need it.
            // TAA-only history is absent. Keep the independent consumers fed with raw color.
            // ImportTexture(null) can still have a valid graph handle in this Core version.
            // Test actual buffer ownership, not just the handle token.
            if (sharedHistoryCount(buffer) > 0 && sharedHistory.IsValid())
                using (var builder = graph.AddRenderPass<SharedHistoryPassData>("RTMaquetaXR shared SSR/VFX history", out var data))
                {
                    data.Color = builder.ReadTexture(source); data.History = builder.WriteTexture(sharedHistory);
                    data.Pixels = bufferPixels(buffer);
                    builder.SetRenderFunc<SharedHistoryPassData>(RenderSharedHistory);
                }
        }

        static void RenderSharedHistory(SharedHistoryPassData data, RenderGraphContext context) =>
            CopyRawImage(data.Color, data.History, data.Pixels, data.Pixels, context);

        static void RenderRawTemporal(RawTemporalPassData data, RenderGraphContext context)
        {
            if (data.Promoted) { ++rawPromotedSkips; return; }
            ++rawPasses;
            CopyRawImage(data.Color, data.Output, data.Pixels, data.Pixels, context);
            if (!data.EvaluateAtNativeSize) return;
            Capture capture;
            if (captures.TryGetValue(data, out capture))
                SubmitTemporal(capture, data.Color, data.Output, data.Depth, data.Motion, data.Pixels, context);
        }

        static void CopyRawImage(TextureHandle source, TextureHandle destination,
            Vector2Int inputPixels, Vector2Int outputPixels, RenderGraphContext context)
        {
            RTHandle input = source, output = destination;
            if (input == null || output == null || input.rt == null || output.rt == null)
                throw new InvalidOperationException("Raw fallback graph texture unavailable");
            if (inputPixels == outputPixels && input.rt.graphicsFormat == output.rt.graphicsFormat)
            {
                // Exact current-frame pixels; no filtering, history, AA or sharpening.
                // Active rect only: pooled RTHandles can contain a larger physical texture.
                context.cmd.CopyTexture(input.rt, 0, 0, 0, 0, inputPixels.x, inputPixels.y, output.rt, 0, 0, 0, 0);
            }
            else
            {
                // Explicit-size SR targets and the engine's history allocations are exact.
                // Never stretch into pooled padding if that native contract changes.
                if (output.rt.width != outputPixels.x || output.rt.height != outputPixels.y)
                    throw new InvalidOperationException("Raw fallback destination active viewport changed");
                Vector2 scale = new Vector2((float)inputPixels.x / input.rt.width, (float)inputPixels.y / input.rt.height);
                context.cmd.Blit(input.nameID, output.nameID, scale, Vector2.zero);
            }
            ++rawFallbackCopies;
        }

        static bool NeedsOriginalTaaHistory(bool originalTaa, Camera camera) => originalTaa && !ReplacesTaa(camera);

        static void InstallRawHistoryHook()
        {
            var buffers = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.WaaaghCameraBuffers");
            rawHistoryMethod = UniqueMethod(buffers, "EnsureCamera");
            var p = rawHistoryMethod.GetParameters();
            if (!rawHistoryMethod.IsStatic || p.Length != 1 || p[0].ParameterType != cameraDataField.FieldType.MakeByRefType())
                throw new MissingMethodException("Waaagh history requirements changed");
            harmony.Patch(rawHistoryMethod, transpiler: new HarmonyMethod(typeof(NeuralTemporalHook), nameof(RawHistoryTranspiler)));
            rawResolutionMethod = UniqueMethod(bufferField.FieldType, "CheckResolution");
            if (rawResolutionMethod.ReturnType != typeof(bool) || rawResolutionMethod.GetParameters().Length != 1 ||
                rawResolutionMethod.GetParameters()[0].ParameterType != typeof(Vector2Int))
                throw new MissingMethodException("Waaagh buffer resolution contract changed");
            harmony.Patch(rawResolutionMethod, postfix: new HarmonyMethod(typeof(NeuralTemporalHook), nameof(RawResolutionPostfix)));
        }

        static void RawResolutionPostfix(object __instance, Vector2Int __0, ref bool __result)
        {
            // Native CheckResolution compares history textures only, and returns true
            // unconditionally when none exist. Raw neural mode intentionally has no
            // TAA history: still invalidate its pixel size and jitter on a scale change.
            // Preserve native rejection and its recreation/lifetime path.
            if (__result && __instance != null && ReplacesTaa(bufferOwner(__instance)))
                __result = bufferPixels(__instance) == __0;
        }

        static IEnumerable<CodeInstruction> RawHistoryTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions); int count = 0;
            // Audited EnsureCamera computes color history = SSR || VFX color || TAA.
            // Remove only the TAA contribution, before both cache comparison and allocation.
            // Retain the original TAA flag for jitter and all other consumers (depth/SSR/VFX).
            for (int i = 0; i < code.Count; ++i)
            {
                yield return code[i];
                if (code[i].opcode != OpCodes.Ldloc_0 || i + 1 >= code.Count || code[i + 1].opcode != OpCodes.Or) continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0); yield return new CodeInstruction(OpCodes.Ldfld, cameraField);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NeuralTemporalHook), nameof(NeedsOriginalTaaHistory)));
                ++count;
            }
            if (count != 1) throw new InvalidOperationException("TAA-only history requirement IL changed");
        }

        static void RemoveRawHistoryHook()
        {
            if (rawHistoryMethod != null) harmony.Unpatch(rawHistoryMethod, AccessTools.Method(typeof(NeuralTemporalHook), nameof(RawHistoryTranspiler)));
            if (rawResolutionMethod != null) harmony.Unpatch(rawResolutionMethod, AccessTools.Method(typeof(NeuralTemporalHook), nameof(RawResolutionPostfix)));
        }
    }
}
