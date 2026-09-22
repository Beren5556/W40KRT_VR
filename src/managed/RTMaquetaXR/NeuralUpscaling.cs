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
        // Scene/depth/MV stay at input resolution. Promotion replaces the raw low pass
        // and before bloom/Uber. Both PP ping-pong targets AND its final destination are high.
        static readonly RawTemporalPassData[] registeredRaw = new RawTemporalPassData[2];
        static readonly Camera[] promotedCamera = new Camera[2];
        static readonly int[] promotedFrame = { -1, -1 };
        static readonly Vector2Int[] promotedInput = new Vector2Int[2];
        static readonly List<KeyValuePair<MethodInfo, string>> upscaleHooks = new List<KeyValuePair<MethodInfo, string>>();
        static FieldInfo scaleField, scalingModeField, upscaleFilterField, fsrSharpnessField, targetTypeField, descriptorField, postDescField;
        static FieldInfo closureSource, closureDestination, closureDesc, closureGraph, rendererField;
        static MethodInfo resourcesGetter, promoteBridge, scaleBridge;
        static Action<object, TextureHandle> setCameraColor;
        static Action<object, TextureDesc> setPostDesc;
        static Func<object, bool> unsafeDepthEffects;
        static readonly int screenParamsId = Shader.PropertyToID("_ScreenParams");
        static readonly int scaledScreenParamsId = Shader.PropertyToID("_ScaledScreenParams");
        static readonly int screenSizeId = Shader.PropertyToID("_ScreenSize");
        static readonly int mipBiasId = Shader.PropertyToID("_GlobalMipBias");

        sealed class UpscalePassData
        {
            internal Capture Capture;
            internal TextureHandle Color, Depth, Motion, Output;
            internal Vector2Int OutputPixels;
        }
        sealed class ScreenRestorePassData
        {
            internal Vector2Int Pixels;
            internal float Scale;
        }

        internal static bool ShouldAllowEyeScaling() => installed && requestedMode == 3 && !faulted && !stopping;
        static bool ScaleCamera(Camera camera)
        {
            if (!ShouldAllowEyeScaling() || camera == null) return false;
            // CameraData initializes in RenderCameraStack BEFORE RenderSingleCamera opens
            // the rendering-eye scope. Use stable Runner ownership here, not getEye/getCamera.
            try { return ownsEye != null && ownsEye(camera); }
            catch (Exception error) { Fail("scale-camera", error); return false; }
        }
        static float InputScale() => requestedScale;
        static int InputScalingMode() => requestedScale < 0.999f ? 1 : 0;
        static int InputTargetType(int original, Camera camera) => ScaleCamera(camera) ? 0 : original;
        static Vector2Int InputViewport(Vector2Int original, Camera camera)
        {
            if (!ScaleCamera(camera)) return original;
            // Round upward to even input pixels; 50% must never fall below the NGX minimum.
            return new Vector2Int(RoundInput(outputSize.x, requestedScale), RoundInput(outputSize.y, requestedScale));
        }
        static int RoundInput(int output, float scale) => scale >= 0.999f ? output : Math.Min(output, (int)Math.Ceiling(output * (double)scale / 2) * 2);
        static int PromotedEye(Camera camera)
        {
            for (int eye = 0; eye < 2; ++eye)
                if (camera != null && promotedCamera[eye] == camera && promotedFrame[eye] == Time.frameCount) return eye;
            return -1;
        }
        static int FinalTargetType(int original, Camera camera) => PromotedEye(camera) >= 0 ? 1 : original;
        static RenderTextureDescriptor FinalDescriptor(RenderTextureDescriptor original, Camera camera)
        {
            if (PromotedEye(camera) >= 0) { original.width = outputSize.x; original.height = outputSize.y; }
            return original;
        }
        static TextureDesc PostDescriptor(TextureDesc original, Camera camera)
        {
            if (PromotedEye(camera) >= 0) { original.width = outputSize.x; original.height = outputSize.y; }
            return original;
        }
        static void RcasPostfix(ref bool __result)
        {
            // No original TAA sharpening in any neural path, including warmup/failure.
            if (ReplacesCurrentEyeTaa()) __result = false;
        }
        static void ResetUpscaling()
        {
            for (int i = 0; i < 2; ++i) { registeredRaw[i] = null; promotedCamera[i] = null; promotedFrame[i] = -1; }
        }

        static MethodInfo UniqueMethod(Type type, string name)
        {
            MethodInfo found = null;
            foreach (var method in type.GetMethods(All)) if (method.Name == name)
            { if (found != null) throw new AmbiguousMatchException(type.FullName + "." + name); found = method; }
            if (found == null) throw new MissingMethodException(type.FullName, name);
            return found;
        }
        static void PatchUpscale(MethodInfo target, string patch, bool postfix = false)
        {
            upscaleHooks.Add(new KeyValuePair<MethodInfo, string>(target, patch));
            var hook = new HarmonyMethod(typeof(NeuralTemporalHook), patch);
            if (postfix) harmony.Patch(target, postfix: hook); else harmony.Patch(target, transpiler: hook);
        }
        static void InstallUpscaling()
        {
            Type cd = cameraDataField.FieldType;
            var pipeline = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.WaaaghPipeline");
            var post = registrationMethod.DeclaringType;
            var final = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.Passes.FinalBlitPass");
            if (final == null) final = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.Passes.Base.FinalBlitPass");
            if (pipeline == null || final == null) throw new TypeLoadException("Waaagh scaling pass contract");
            scaleField = Field(cd, "RenderScale", typeof(float)); scalingModeField = Field(cd, "ScalingMode", null);
            upscaleFilterField = Field(cd, "UpscalingFilter", null); fsrSharpnessField = Field(cd, "FsrSharpness", typeof(float));
            targetTypeField = Field(cd, "CameraRenderTargetBufferType", null); descriptorField = Field(cd, "CameraTargetDescriptor", typeof(RenderTextureDescriptor));
            rendererField = Field(cd, "Renderer", null);
            resourcesGetter = UniqueMethod(rendererField.FieldType, "get_RenderGraphResources");
            setCameraColor = Setter<TextureHandle>(Field(resourcesGetter.ReturnType, "CameraColorBuffer", typeof(TextureHandle)));
            postDescField = Field(post, "m_Desc", typeof(TextureDesc)); setPostDesc = Setter<TextureDesc>(postDescField);
            var closure = post.GetNestedType("<>c__DisplayClass55_0", BindingFlags.Public | BindingFlags.NonPublic);
            if (closure == null || !closure.IsValueType) throw new TypeLoadException("Post-process closure changed");
            closureSource = Field(closure, "source", typeof(TextureHandle)); closureDestination = Field(closure, "destination", typeof(TextureHandle));
            closureDesc = Field(closure, "desc", typeof(TextureDesc)); closureGraph = Field(closure, "renderGraph", typeof(RenderGraph));
            unsafeDepthEffects = MakeDepthEffectGuard(post);
            scaleBridge = BuildScaleBridge(cd); promoteBridge = BuildPromoteBridge(closure);
            PatchUpscale(UniqueMethod(pipeline, "InitializeStackedCameraData"), nameof(StackScaleTranspiler));
            PatchUpscale(UniqueMethod(pipeline, "InitializeAdditionalCameraData"), nameof(TargetScaleTranspiler));
            PatchUpscale(UniqueMethod(cd, "get_ScaledCameraTargetViewportSize"), nameof(ViewportTranspiler));
            PatchUpscale(UniqueMethod(post, "RenderPostProcess"), nameof(PromoteTranspiler));
            PatchUpscale(UniqueMethod(post, "RecordRenderGraph"), nameof(PostDescriptorTranspiler));
            PatchUpscale(UniqueMethod(post, "RenderFinalPostProcess"), nameof(FinalPostGlobalsTranspiler));
            var custom = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.RendererFeatures.CustomPostProcess.Passes.CustomPostProcessPass");
            if (custom == null) throw new TypeLoadException("Waaagh custom post-process contract");
            PatchUpscale(UniqueMethod(custom, "RecordRenderGraph"), nameof(CustomPostTranspiler));
            PatchUpscale(UniqueMethod(final, "Setup"), nameof(FinalBlitTranspiler));
            PatchUpscale(UniqueMethod(post, "get_ApplyTaaRcas"), nameof(RcasPostfix), true);
        }
        static void RemoveUpscalingHooks()
        {
            foreach (var hook in upscaleHooks) harmony.Unpatch(hook.Key, AccessTools.Method(typeof(NeuralTemporalHook), hook.Value));
            upscaleHooks.Clear(); ResetUpscaling();
        }
        static Action<object, T> Setter<T>(FieldInfo field)
        {
            var method = new DynamicMethod("NeuralWrite_" + field.Name, typeof(void), new[] { typeof(object), typeof(T) }, typeof(NeuralTemporalHook).Module, true);
            var il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, field.DeclaringType); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Stfld, field); il.Emit(OpCodes.Ret);
            return (Action<object, T>)method.CreateDelegate(typeof(Action<object, T>));
        }
        static Func<object, bool> MakeDepthEffectGuard(Type post)
        {
            var method = new DynamicMethod("NeuralDepthEffectGuard", typeof(bool), new[] { typeof(object) }, typeof(NeuralTemporalHook).Module, true);
            var il = method.GetILGenerator(); var active = il.DefineLabel();
            foreach (string name in new[] { "m_DepthOfField", "m_MotionBlur" })
            {
                var field = Field(post, name, null); var isActive = UniqueMethod(field.FieldType, "IsActive");
                if (isActive.ReturnType != typeof(bool) || isActive.GetParameters().Length != 0) throw new MissingMethodException("Depth effect IsActive");
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, post); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Callvirt, isActive); il.Emit(OpCodes.Brtrue, active);
            }
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); il.MarkLabel(active); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
            return (Func<object, bool>)method.CreateDelegate(typeof(Func<object, bool>));
        }
        static MethodInfo BuildScaleBridge(Type cd)
        {
            var method = new DynamicMethod("NeuralSetCameraScale", typeof(void), new[] { cd.MakeByRefType(), typeof(Camera) }, typeof(NeuralTemporalHook).Module, true);
            var il = method.GetILGenerator(); var done = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, AccessTools.Method(typeof(NeuralTemporalHook), nameof(ScaleCamera))); il.Emit(OpCodes.Brfalse, done);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, AccessTools.Method(typeof(NeuralTemporalHook), nameof(InputScale))); il.Emit(OpCodes.Stfld, scaleField);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, AccessTools.Method(typeof(NeuralTemporalHook), nameof(InputScalingMode))); il.Emit(OpCodes.Stfld, scalingModeField);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stfld, upscaleFilterField);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_R4, 0f); il.Emit(OpCodes.Stfld, fsrSharpnessField);
            il.MarkLabel(done); il.Emit(OpCodes.Ret); return method;
        }
        static MethodInfo BuildPromoteBridge(Type closure)
        {
            var method = new DynamicMethod("NeuralPromotePostProcess", typeof(void), new[] { typeof(object), renderingDataType.MakeByRefType(), closure.MakeByRefType() }, typeof(NeuralTemporalHook).Module, true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldfld, closureGraph);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldflda, cameraDataField); il.Emit(OpCodes.Ldfld, rendererField); il.Emit(OpCodes.Callvirt, resourcesGetter);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldflda, cameraDataField); il.Emit(OpCodes.Ldfld, cameraField);
            il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldflda, closureDesc);
            il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldflda, closureSource);
            il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldflda, closureDestination);
            il.Emit(OpCodes.Call, AccessTools.Method(typeof(NeuralTemporalHook), nameof(Promote))); il.Emit(OpCodes.Ret); return method;
        }
        static IEnumerable<CodeInstruction> StackScaleTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var p = __originalMethod.GetParameters();
            if (__originalMethod.IsStatic || p.Length != 4 || p[0].ParameterType != typeof(Camera) || p[3].ParameterType != cameraDataField.FieldType.MakeByRefType()) throw new InvalidOperationException("Stack camera initialization signature changed");
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ret)
                {
                    var first = new CodeInstruction(OpCodes.Ldarg_S, (byte)4); first.MoveLabelsFrom(instruction); yield return first;
                    yield return new CodeInstruction(OpCodes.Ldarg_1); yield return new CodeInstruction(OpCodes.Call, scaleBridge); ++count;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Stack camera initialization return changed");
        }
        static IEnumerable<CodeInstruction> TargetScaleTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var p = __originalMethod.GetParameters();
            if (!__originalMethod.IsStatic || p.Length != 5 || p[0].ParameterType != typeof(Camera) || p[4].ParameterType != cameraDataField.FieldType.MakeByRefType()) throw new InvalidOperationException("Additional camera initialization signature changed");
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, targetTypeField))
                {
                    var first = new CodeInstruction(OpCodes.Ldarg_0); first.MoveLabelsFrom(instruction); yield return first;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NeuralTemporalHook), nameof(InputTargetType))); ++count;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Additional camera scaled target assignment changed");
        }
        static IEnumerable<CodeInstruction> ViewportTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ret)
                {
                    var first = new CodeInstruction(OpCodes.Ldarg_0); first.MoveLabelsFrom(instruction); yield return first;
                    yield return new CodeInstruction(OpCodes.Ldfld, cameraField);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NeuralTemporalHook), nameof(InputViewport))); ++count;
                }
                yield return instruction;
            }
            if (count == 0) throw new InvalidOperationException("Scaled viewport getter changed");
        }
        static IEnumerable<CodeInstruction> PromoteTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions); int site = -1;
            for (int i = 0; i < code.Count; ++i)
                if (Equals(code[i].operand, registrationMethod))
                {
                    if (site >= 0 || i + 2 >= code.Count || (code[i + 1].opcode != OpCodes.Ldloca && code[i + 1].opcode != OpCodes.Ldloca_S) ||
                        !(code[i + 2].operand is MethodInfo) || !((MethodInfo)code[i + 2].operand).Name.Contains("g__Swap")) throw new InvalidOperationException("Post-TAA swap IL changed");
                    site = i + 3;
                }
            if (site < 0) throw new InvalidOperationException("Post-TAA promotion site missing");
            code.InsertRange(site, new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(code[site - 2].opcode, code[site - 2].operand), new CodeInstruction(OpCodes.Call, promoteBridge) });
            // Restore low scene dimensions after the entire high PP chain, before late masks.
            var graph = Field(renderingDataType, "RenderGraph", typeof(RenderGraph)); int returns = 0;
            for (int i = code.Count - 1; i >= 0; --i) if (code[i].opcode == OpCodes.Ret)
            {
                var first = new CodeInstruction(OpCodes.Ldarg_1); first.MoveLabelsFrom(code[i]);
                code.InsertRange(i, new[] { first, new CodeInstruction(OpCodes.Ldfld, graph), new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Ldflda, cameraDataField), new CodeInstruction(OpCodes.Ldfld, cameraField),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NeuralTemporalHook), nameof(RegisterScreenRestore))) }); ++returns;
            }
            if (returns != 1) throw new InvalidOperationException("Post-process return changed"); return code;
        }
        static IEnumerable<CodeInstruction> PostDescriptorTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, postDescField))
                {
                    var first = new CodeInstruction(OpCodes.Ldarg_1); first.MoveLabelsFrom(instruction); yield return first;
                    yield return new CodeInstruction(OpCodes.Ldflda, cameraDataField); yield return new CodeInstruction(OpCodes.Ldfld, cameraField);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NeuralTemporalHook), nameof(PostDescriptor))); ++count;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Post-process descriptor assignment changed");
        }
        static IEnumerable<CodeInstruction> FinalBlitTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            int targets = 0, descriptors = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode != OpCodes.Ldfld) continue;
                string name = Equals(instruction.operand, targetTypeField) ? nameof(FinalTargetType) : Equals(instruction.operand, descriptorField) ? nameof(FinalDescriptor) : null;
                if (name == null) continue;
                yield return new CodeInstruction(OpCodes.Ldarg_3); yield return new CodeInstruction(OpCodes.Ldflda, cameraDataField);
                yield return new CodeInstruction(OpCodes.Ldfld, cameraField); yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NeuralTemporalHook), name));
                if (name == nameof(FinalTargetType)) ++targets; else ++descriptors;
            }
            if (targets != 1 || descriptors != 1) throw new InvalidOperationException("Final blit viewport/descriptor IL changed");
        }
        static IEnumerable<CodeInstruction> FinalPostGlobalsTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var graph = Field(renderingDataType, "RenderGraph", typeof(RenderGraph));
            var entry = ScreenCall(graph, nameof(RegisterScreenHigh)); entry[0].MoveLabelsFrom(code[0]);
            code.InsertRange(0, entry);
            int count = 0;
            for (int i = code.Count - 1; i >= 0; --i) if (code[i].opcode == OpCodes.Ret)
            {
                var exit = ScreenCall(graph, nameof(RegisterScreenRestore)); exit[0].MoveLabelsFrom(code[i]); code.InsertRange(i, exit); ++count;
            }
            if (count != 1) throw new InvalidOperationException("Final post-process return changed");
            return code;
        }
        static IEnumerable<CodeInstruction> CustomPostTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(); int count = 0;
            foreach (var instruction in instructions)
            {
                code.Add(instruction);
                if (instruction.opcode != OpCodes.Ldfld || !Equals(instruction.operand, descriptorField)) continue;
                code.Add(new CodeInstruction(OpCodes.Ldarg_1)); code.Add(new CodeInstruction(OpCodes.Ldflda, cameraDataField));
                code.Add(new CodeInstruction(OpCodes.Ldfld, cameraField));
                code.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NeuralTemporalHook), nameof(FinalDescriptor)))); ++count;
            }
            if (count != 1) throw new InvalidOperationException("Custom post-process descriptor IL changed");
            return FinalPostGlobalsTranspiler(code);
        }
        static List<CodeInstruction> ScreenCall(FieldInfo graph, string name) => new List<CodeInstruction> {
            new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Ldfld, graph),
            new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Ldflda, cameraDataField), new CodeInstruction(OpCodes.Ldfld, cameraField),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NeuralTemporalHook), name)) };

        static void Promote(object post, RenderGraph graph, object resources, Camera camera, ref TextureDesc desc, ref TextureHandle source, ref TextureHandle destination)
        {
            if (!ScaleCamera(camera) || !CaptureEnabled()) return;
            try
            {
                int eye = getEye();
                if (camera != getCamera() || (eye != 0 && eye != 1)) throw new InvalidOperationException("DLSS promotion outside its rendering-eye scope");
                RawTemporalPassData raw = registeredRaw[eye]; registeredRaw[eye] = null;
                Capture capture;
                if (raw == null || !captures.TryGetValue(raw, out capture) || capture.Generation != generation || capture.Frame != Time.frameCount || capture.Camera != camera)
                    throw new InvalidOperationException("DLSS registration owner/frame missing");
                if (unsafeDepthEffects(post)) throw new InvalidOperationException("DLSS high post-process requires depth of field and motion blur disabled");
                if (camera.allowDynamicResolution || camera.rect != new Rect(0, 0, 1, 1)) throw new InvalidOperationException("DLSS requires full eye viewport and no hardware DRS");
                if (camera.pixelWidth != outputSize.x || camera.pixelHeight != outputSize.y) throw new InvalidOperationException("DLSS camera output does not match the latched XR size");
                if (capture.Pixels != InputViewport(capture.Pixels, camera)) throw new InvalidOperationException("DLSS input viewport does not match requested scale");
                TextureHandle color = raw.Color, depth = raw.Depth, motion = raw.Motion;
                // The verified immediate Swap assigns our raw low destination to this source.
                // Do not use ValueType.Equals on TextureHandle here (boxes transient handles).
                if (!color.IsValid() || !depth.IsValid() || !motion.IsValid() || !source.IsValid()) throw new InvalidOperationException("DLSS graph source/fallback contract changed");
                TextureDesc high = desc; high.width = outputSize.x; high.height = outputSize.y; high.depthBufferBits = DepthBits.None;
                high.name = "RTMaquetaXR Neural High";
                TextureHandle output = graph.CreateTexture(high);
                high.name = "RTMaquetaXR Post Process High";
                TextureHandle final = graph.CreateTexture(high);
                using (var builder = graph.AddRenderPass<UpscalePassData>("RTMaquetaXR DLSS before post process", out var data))
                {
                    data.Capture = capture; data.OutputPixels = outputSize;
                    data.Color = builder.ReadTexture(color);
                    data.Depth = builder.ReadTexture(depth); data.Motion = builder.ReadTexture(motion);
                    data.Output = builder.WriteTexture(output);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc<UpscalePassData>(RenderUpscale);
                }
                // No camera descriptor/depth promotion: late highlight masks retain low DSV sizes.
                setCameraColor(resources, final); setPostDesc(post, high);
                desc = high; source = output; destination = TextureHandle.nullHandle;
                // Imported destinations keep the low copy alive even when no
                // consumer remains. Suppress it only after successful promotion;
                // the high pass still prefills its own no-AA failure image.
                raw.Promoted = true;
                promotedCamera[eye] = camera; promotedFrame[eye] = Time.frameCount; promotedInput[eye] = capture.Pixels;
            }
            catch (Exception error) { Fail("SR-promotion", error); }
        }
        static void RenderUpscale(UpscalePassData data, RenderGraphContext context)
        {
            // Initialize high output with current raw scene color, even if NGX is disabled
            // after graph registration. A native failure therefore cannot publish stale pixels.
            try
            {
                CopyRawImage(data.Color, data.Output, data.Capture.Pixels, data.OutputPixels, context);
                SubmitTemporal(data.Capture, data.Color, data.Output, data.Depth, data.Motion, data.OutputPixels, context);
            }
            catch (Exception error) { Fail("SR-callback", error); }
            finally { SetScreenDimensions(context.cmd, data.OutputPixels, 1); data.Capture = null; }
        }
        static void RegisterScreenRestore(RenderGraph graph, Camera camera)
        {
            RegisterScreenPass(graph, camera, false);
        }
        static void RegisterScreenHigh(RenderGraph graph, Camera camera)
        {
            RegisterScreenPass(graph, camera, true);
        }
        static void RegisterScreenPass(RenderGraph graph, Camera camera, bool high)
        {
            int eye = PromotedEye(camera); if (eye < 0) return;
            try
            {
                using (var builder = graph.AddRenderPass<ScreenRestorePassData>(high ? "RTMaquetaXR high final dimensions" : "RTMaquetaXR restore scene dimensions", out var data))
                {
                    data.Pixels = high ? outputSize : promotedInput[eye]; data.Scale = high ? 1 : requestedScale; builder.AllowPassCulling(false);
                    builder.SetRenderFunc<ScreenRestorePassData>((pass, context) => SetScreenDimensions(context.cmd, pass.Pixels, pass.Scale));
                }
            }
            catch (Exception error) { Fail("SR-restore-registration", error); }
        }
        static void SetScreenDimensions(CommandBuffer cmd, Vector2Int pixels, float scale)
        {
            // Exact installed SetCameraShaderVariablesPass formulas; DRS is guarded off.
            float w = pixels.x, h = pixels.y;
            var parameters = new Vector4(w, h, 1 + 1 / w, 1 + 1 / h);
            cmd.SetGlobalVector(screenParamsId, parameters); cmd.SetGlobalVector(scaledScreenParamsId, parameters);
            cmd.SetGlobalVector(screenSizeId, new Vector4(w, h, 1 / w, 1 / h));
            float bias = Math.Min((float)Math.Log(scale, 2), 0);
            cmd.SetGlobalVector(mipBiasId, new Vector4(bias, Mathf.Pow(2, bias), 0, 0));
        }
    }
}
