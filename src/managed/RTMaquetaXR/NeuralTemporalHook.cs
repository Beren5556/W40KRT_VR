using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace RTMaquetaXR
{
    // Neural cameras replace the complete TAA graph registration. NGX consumes raw scene
    // color; only a successful PRESENT job replaces a current-frame image without AA.
    internal static partial class NeuralTemporalHook
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        const int WarmupPairs = 8, MaxPointers = 64, MaxPending = 64;
        static Harmony harmony;
        static Func<int> getEye;
        static Func<Camera> getCamera;
        static Func<Camera, bool> ownsEye;
        static Action<string> log;
        static MethodInfo registrationMethod, callbackMethod, projectionFlippedMethod;
        static readonly List<MethodInfo> releaseMethods = new List<MethodInfo>();
        static Type renderingDataType, passDataType;
        static FieldInfo cameraDataField, cameraField, bufferField, jitterField, pixelSizeField, hdrField;
        static Func<object, Camera> bufferOwner;
        static Func<object, Matrix4x4> bufferJitter;
        static Func<object, Vector2Int> bufferPixels;
        static Func<object, TextureHandle> sourceHandle, destinationHandle, motionHandle, depthHandle;
        static bool installed, loaded, configured, faulted, stopping;
        static bool allDiagnosticsEnabled = false;
        internal static void SetAllDiagnosticsEnabled(bool enabled)
        {
            allDiagnosticsEnabled = enabled;
            if (loaded) ApplyNativeDiagnosticsSetting();
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ApplyNativeDiagnosticsSetting() => NeuralNative.RTN_SetDiagnosticsEnabled(allDiagnosticsEnabled ? 1 : 0);
        static int requestedMode, ownerThread, decisionFrame = -1, successfulPairs;
        static bool presentThisFrame;
        static ulong generation, historyEpoch;
        static uint configuredFlags;
        static string runtimeDirectory, nativeLogPath, lastError;
        static IntPtr renderEvent;
        static CommandBuffer maintenanceCommands;
        static readonly bool[] resetPending = { true, true };
        static readonly Camera[] previousCameras = new Camera[2];
        static readonly int[] lastQueuedFrame = { -1, -1 };
        static readonly Vector2Int[] lastInputSize = new Vector2Int[2];
        static readonly Vector2[] lastJitterPixels = new Vector2[2];
        static readonly ulong[] diagnosedGeneration = new ulong[2];
        static readonly List<Pending> pendingTickets = new List<Pending>();
        static readonly Dictionary<ulong, int> warmupFrames = new Dictionary<ulong, int>();
        static ConditionalWeakTable<object, Capture> captures = new ConditionalWeakTable<object, Capture>();
        static readonly Dictionary<RenderTexture, PointerEntry> pointers = new Dictionary<RenderTexture, PointerEntry>(new TextureIdentity());
        static long queuedJobs, shadowJobs, presentedJobs, fallbackJobs, pointerFetches, pointerInvalidations;
        static double metadataCpuMs, nativeCpuMs;
        static NeuralNative.BackendStatus backend;
        static NeuralNative.PresetStatus presetStatus;
        static readonly NeuralRuntimeInfo runtimeInfo = new NeuralRuntimeInfo(NeuralNative.RTN_GetRuntimeStatus, Stopwatch.Frequency);
        static float requestedScale = 1, requestedSharpness;
        static int requestedPreset;
        static Vector2Int outputSize;
        static readonly Vector2Int[] lastOutputSize = new Vector2Int[2];
        static readonly int[] lastPresentedFrame = { -100, -100 };
        static readonly Dictionary<ulong, int> presentedFrames = new Dictionary<ulong, int>();
        static int lastPresentedPair = -100;

        sealed class Capture
        {
            internal Camera Camera;
            internal object Buffer;
            internal int Eye, Frame;
            internal ulong Generation;
            internal Matrix4x4 Jitter;
            internal Vector2Int Pixels;
            internal bool Hdr, Present;
        }
        sealed class PointerEntry
        {
            internal IntPtr Pointer;
            internal RenderTextureDescriptor Descriptor;
        }
        struct Pending
        {
            internal ulong Ticket, Epoch;
        }
        sealed class TextureIdentity : IEqualityComparer<RenderTexture>
        {
            public bool Equals(RenderTexture a, RenderTexture b) { return ReferenceEquals(a, b); }
            public int GetHashCode(RenderTexture texture) { return RuntimeHelpers.GetHashCode(texture); }
        }
        struct Surface
        {
            internal RenderTexture Texture;
            internal RenderTextureDescriptor Descriptor;
            internal Vector2Int Viewport;
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);

        internal static bool Install(Harmony patcher, Func<int> eyeIndexGetter, Func<Camera> cameraGetter, Action<string> logger, Func<Camera, bool> eyeOwnership)
        {
            if (installed) return true;
            harmony = patcher; getEye = eyeIndexGetter; getCamera = cameraGetter; log = logger; ownsEye = eyeOwnership;
            ownerThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            try
            {
                if (harmony == null || getEye == null || getCamera == null || ownsEye == null) throw new ArgumentNullException("Neural hook dependency");
                NeuralNative.CheckAbi(); ResolveContract();
                PrepareRawTemporalContract();
                harmony.Patch(registrationMethod, transpiler: new HarmonyMethod(typeof(NeuralTemporalHook), nameof(RegisterTranspiler)));
                foreach (var method in releaseMethods)
                    harmony.Patch(method, transpiler: new HarmonyMethod(typeof(NeuralTemporalHook), nameof(ReleaseTranspiler)));
                InstallUpscaling();
                InstallRawHistoryHook();
                installed = true; faulted = false;
                Log("managed hooks ready; raw-image fallback; TAA/history bypass ready; neural mode is Off");
                return true;
            }
            catch (Exception error) { Fail("install", error); RemoveHooks(); return false; }
        }

        // Call outside rendering. Modes: 0 Off, 1 shadow DLAA, 2 DLAA, 3 DLSS;
        // visible neural modes start after WarmupPairs successful shadow pairs.
        // Native DLL must be beside the managed mod DLL; featureDirectory contains official NGX.
        internal static bool Configure(int mode, string featureDirectory, string logPath, float inputScale = 1, int preset = 0, float sharpness = 0)
        {
            try
            {
                EnsureThread();
                if (mode < 0 || mode > 3) throw new ArgumentOutOfRangeException(nameof(mode));
                if (mode == 0) { Detach(); return true; }
                requestedMode = mode; // Retain the request on failure: never silently select TAA.
                if (float.IsNaN(inputScale) || inputScale < 0.5f || inputScale > 1) throw new ArgumentOutOfRangeException(nameof(inputScale));
                if (preset != 0 && preset != 1 && preset != 11) throw new ArgumentOutOfRangeException(nameof(preset));
                if (mode == 3 && (outputSize.x <= 0 || outputSize.y <= 0)) throw new InvalidOperationException("DLSS output size is not set");
                if (!installed) throw new InvalidOperationException("Neural hooks are not installed");
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11) throw new InvalidOperationException("Neural path requires D3D11");
                string directory = Path.GetFullPath(featureDirectory);
                // NGX may select the configured DLL or an NVIDIA override. The
                // effective runtime is identified natively; file/version/source
                // differences alone are informational, never an admission gate.
                if (!loaded)
                {
                    string path = Path.Combine(Path.GetDirectoryName(typeof(NeuralTemporalHook).Assembly.Location), NeuralNative.Library);
                    if (LoadLibraryEx(path, IntPtr.Zero, 0x100 | 0x1000) == IntPtr.Zero)
                        throw new InvalidOperationException("Cannot load neural bridge; Win32=" + Marshal.GetLastWin32Error());
                    loaded = true;
                    NeuralNative.RTN_SetDiagnosticsEnabled(allDiagnosticsEnabled ? 1 : 0);
                    renderEvent = NeuralNative.RTN_GetRenderEventAndData();
                    if (renderEvent == IntPtr.Zero) throw new InvalidOperationException("Neural event callback is null");
                }
                // Configure is CPU-only, but a prior backend may still own GPU work.
                // Keep the new request pending until maintenance reports IDLE.
                if (configured || stopping || pendingTickets.Count != 0)
                { NeuralNative.RTN_RequestShutdown(); stopping = true; }
                requestedMode = mode; runtimeDirectory = directory; nativeLogPath = logPath;
                requestedScale = mode == 3 ? inputScale : 1; requestedPreset = preset == 0 ? 0 : 11;
                SetSharpness(sharpness);
                faulted = false; lastError = null; configured = false; ++generation;
                ResetHistory();
                Log("requested mode=" + mode + " scale=" + requestedScale + " preset=" + requestedPreset + "; " + WarmupPairs + "-pair shadow warmup; generation=" + generation);
                return true;
            }
            catch (Exception error) { Fail("configure", error); return false; }
        }

        internal static void ResetHistory()
        {
            ++historyEpoch;
            resetPending[0] = resetPending[1] = true;
            successfulPairs = 0; warmupFrames.Clear();
            previousCameras[0] = previousCameras[1] = null;
            lastQueuedFrame[0] = lastQueuedFrame[1] = -1;
            decisionFrame = -1; presentThisFrame = false;
            captures = new ConditionalWeakTable<object, Capture>();
            ResetUpscaling();
            lastPresentedFrame[0] = lastPresentedFrame[1] = -100;
            presentedFrames.Clear(); lastPresentedPair = -100;
            ClearPointers();
        }

        internal static void Detach()
        {
            requestedMode = 0; configured = false; ++generation; ResetHistory();
            if (loaded)
            {
                try { NeuralNative.RTN_RequestShutdown(); stopping = true; }
                catch (Exception error) { Fail("shutdown-request", error); }
            }
        }

        // Keep calling while true, also after Stop, until native retirement reaches IDLE.
        // Never unload the native DLL while an event or deferred GPU retirement may still use it.
        internal static bool PumpMaintenance()
        {
            if (!loaded) return false;
            try
            {
                EnsureThread(); Poll();
                if (!stopping) return false;
                if (backend.state == 0)
                {
                    stopping = false; pendingTickets.Clear();
                    if (maintenanceCommands != null) { maintenanceCommands.Release(); maintenanceCommands = null; }
                    return false;
                }
                if (maintenanceCommands == null) maintenanceCommands = new CommandBuffer { name = "RTMaquetaXR neural retirement" };
                maintenanceCommands.Clear();
                maintenanceCommands.IssuePluginEventAndData(renderEvent, (int)NeuralNative.MaintenanceEvent, IntPtr.Zero);
                Graphics.ExecuteCommandBuffer(maintenanceCommands);
                return true;
            }
            catch (Exception error) { Fail("maintenance", error); return stopping; }
        }

        internal static void Stop()
        {
            Detach(); RemoveHooks(); installed = false;
            // Keep the logger, event address and maintenance buffer until native cleanup completes.
        }

        static void RemoveHooks()
        {
            if (harmony == null) return;
            try
            {
                if (registrationMethod != null) harmony.Unpatch(registrationMethod, AccessTools.Method(typeof(NeuralTemporalHook), nameof(RegisterTranspiler)));
                RemoveRawHistoryHook();
                foreach (var method in releaseMethods) harmony.Unpatch(method, AccessTools.Method(typeof(NeuralTemporalHook), nameof(ReleaseTranspiler)));
                RemoveUpscalingHooks();
            }
            catch (Exception error) { Log("unpatch failed; evaluation remains disabled: " + error.Message); }
        }

        static FieldInfo Field(Type type, string name, Type expected)
        {
            var value = AccessTools.Field(type, name);
            if (value == null || value.IsStatic || (expected != null && value.FieldType != expected)) throw new MissingFieldException(type.FullName, name);
            return value;
        }
        static Func<object, T> Getter<T>(FieldInfo field)
        {
            var method = new DynamicMethod("NeuralRead_" + field.Name, typeof(T), new[] { typeof(object) }, typeof(NeuralTemporalHook).Module, true);
            var il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, field.DeclaringType); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
            return (Func<object, T>)method.CreateDelegate(typeof(Func<object, T>));
        }

        static void ResolveContract()
        {
            var pass = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.Passes.PostProcess.PostProcessPass");
            renderingDataType = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.RenderingData");
            if (pass == null || renderingDataType == null || !renderingDataType.IsValueType) throw new TypeLoadException("Waaagh metadata missing");
            registrationMethod = null; callbackMethod = null; releaseMethods.Clear();
            foreach (var method in pass.GetMethods(All)) if (method.Name == "DoTemporalAntialiasing")
            { if (registrationMethod != null) throw new AmbiguousMatchException("DoTemporalAntialiasing"); registrationMethod = method; }
            var args = registrationMethod == null ? null : registrationMethod.GetParameters();
            if (args == null || args.Length != 3 || registrationMethod.IsStatic || registrationMethod.ReturnType != typeof(void) ||
                args[0].ParameterType != renderingDataType.MakeByRefType() || args[1].ParameterType != typeof(TextureHandle) || args[2].ParameterType != typeof(TextureHandle))
                throw new MissingMethodException("TAA registration signature");
            passDataType = pass.GetNestedType("TaaPassData", BindingFlags.Public | BindingFlags.NonPublic);
            if (passDataType == null || passDataType.IsValueType) throw new TypeLoadException("TaaPassData");
            sourceHandle = Getter<TextureHandle>(Field(passDataType, "Source", typeof(TextureHandle)));
            destinationHandle = Getter<TextureHandle>(Field(passDataType, "Destination", typeof(TextureHandle)));
            motionHandle = Getter<TextureHandle>(Field(passDataType, "VelocityBuffer", typeof(TextureHandle)));
            depthHandle = Getter<TextureHandle>(Field(passDataType, "CameraDepthCopyRT", typeof(TextureHandle)));
            cameraDataField = Field(renderingDataType, "CameraData", null);
            if (!cameraDataField.FieldType.IsValueType) throw new TypeLoadException("CameraData is no longer a value type");
            cameraField = Field(cameraDataField.FieldType, "Camera", typeof(Camera));
            bufferField = Field(cameraDataField.FieldType, "CameraBuffer", null);
            if (bufferField.FieldType.IsValueType) throw new TypeLoadException("CameraBuffer is no longer a reference type");
            hdrField = Field(cameraDataField.FieldType, "IsHdrEnabled", typeof(bool));
            projectionFlippedMethod = AccessTools.Method(cameraDataField.FieldType, "IsCameraProjectionMatrixFlipped", Type.EmptyTypes);
            if (projectionFlippedMethod == null || projectionFlippedMethod.IsStatic || projectionFlippedMethod.ReturnType != typeof(bool))
                throw new MissingMethodException("CameraData.IsCameraProjectionMatrixFlipped");
            bufferOwner = Getter<Camera>(Field(bufferField.FieldType, "Camera", typeof(Camera)));
            jitterField = Field(bufferField.FieldType, "JitterMatrix", typeof(Matrix4x4));
            pixelSizeField = Field(bufferField.FieldType, "CameraRenderPixelSize", typeof(Vector2Int));
            bufferJitter = Getter<Matrix4x4>(jitterField);
            bufferPixels = Getter<Vector2Int>(pixelSizeField);
            foreach (var nested in pass.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) foreach (var method in nested.GetMethods(All))
            {
                var p = method.GetParameters();
                if (method.Name.Contains("<DoTemporalAntialiasing>") && !method.IsStatic && method.ReturnType == typeof(void) && p.Length == 2 && p[0].ParameterType == passDataType && p[1].ParameterType == typeof(RenderGraphContext))
                { if (callbackMethod != null) throw new AmbiguousMatchException("TAA callback"); callbackMethod = method; }
            }
            if (callbackMethod == null) throw new MissingMethodException("TAA callback");
            AddReleaseMethod("Resize", new[] { typeof(int), typeof(int), typeof(bool) });
            AddReleaseMethod("DemandResize", new[] { typeof(RTHandle) });
            AddReleaseMethod("SetHardwareDynamicResolutionState", new[] { typeof(bool) });
        }

        static void AddReleaseMethod(string name, Type[] args)
        {
            var method = AccessTools.Method(typeof(RTHandleSystem), name, args);
            if (method == null || method.IsStatic || method.ReturnType != typeof(void)) throw new MissingMethodException("RTHandleSystem." + name);
            releaseMethods.Add(method);
        }

        static IEnumerable<CodeInstruction> RegisterTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = new List<CodeInstruction>(instructions);
            if (code.Count == 0 || rawRegistrationBridge == null) throw new InvalidOperationException("Neural registration bridge missing");
            var original = generator.DefineLabel();
            code[0].labels.Add(original);
            var result = new List<CodeInstruction> {
                new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Ldflda, cameraDataField),
                new CodeInstruction(OpCodes.Ldfld, cameraField),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NeuralTemporalHook), nameof(ReplacesTaa))),
                new CodeInstruction(OpCodes.Brfalse, original),
                new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Ldarg_2), new CodeInstruction(OpCodes.Ldarg_3),
                new CodeInstruction(OpCodes.Call, rawRegistrationBridge), new CodeInstruction(OpCodes.Ret)
            };
            result.AddRange(code); return result;
        }

        static void LoadCameraDataField(List<CodeInstruction> code, FieldInfo field)
        {
            code.Add(new CodeInstruction(OpCodes.Ldarg_1)); code.Add(new CodeInstruction(OpCodes.Ldflda, cameraDataField)); code.Add(new CodeInstruction(OpCodes.Ldfld, field));
        }

        static IEnumerable<CodeInstruction> ReleaseTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions); int count = 0;
            var original = AccessTools.Method(typeof(RenderTexture), nameof(RenderTexture.Release), Type.EmptyTypes);
            var wrapper = AccessTools.Method(typeof(NeuralTemporalHook), nameof(ReleaseTrackedTexture));
            foreach (var instruction in code) if (Equals(instruction.operand, original))
            { instruction.opcode = OpCodes.Call; instruction.operand = wrapper; ++count; }
            if (count != 1) throw new InvalidOperationException("RTHandleSystem recreation IL changed");
            return code;
        }
        static void ReleaseTrackedTexture(RenderTexture texture)
        {
            // Preserve the engine call and its exception. Only invalidate our cached COM reference.
            try { ForgetPointer(texture); } catch (Exception error) { Fail("pointer-invalidation", error); }
            texture.Release();
        }

        static bool CaptureEnabled()
        {
            if (!installed || requestedMode == 0 || faulted || stopping) return false;
            try { EnsureThread(); int eye = getEye(); return eye == 0 || eye == 1; }
            catch (Exception error) { Fail("capture-gate", error); return false; }
        }

        // Typed field loads in the transpiler avoid boxing RenderingData/CameraData each frame.
        static void Record(object passData, Camera camera, object buffer, bool hdr, bool projectionFlipped)
        {
            try
            {
                if (!CaptureEnabled()) return;
                int eye = getEye(), frame = Time.frameCount;
                if (camera == null || camera != getCamera() || buffer == null || bufferOwner(buffer) != camera) throw new InvalidOperationException("Neural camera ownership mismatch");
                Matrix4x4 jitter = bufferJitter(buffer); Vector2Int pixels = bufferPixels(buffer);
                if (!projectionFlipped) throw new InvalidOperationException("D3D11 temporal projection flip contract changed");
                if (pixels.x <= 0 || pixels.y <= 0 || !ValidJitter(jitter)) throw new InvalidOperationException("Invalid temporal dimensions/jitter matrix");
                if (decisionFrame != frame)
                {
                    Poll(); decisionFrame = frame;
                    presentThisFrame = requestedMode >= 2 && successfulPairs >= WarmupPairs && !faulted;
                }
                if (faulted) return;
                Capture capture;
                if (!captures.TryGetValue(passData, out capture)) { capture = new Capture(); captures.Add(passData, capture); }
                capture.Camera = camera; capture.Buffer = buffer; capture.Eye = eye; capture.Frame = frame; capture.Generation = generation;
                capture.Jitter = jitter; capture.Pixels = pixels; capture.Hdr = hdr; capture.Present = presentThisFrame;
                if (requestedMode == 3) registeredRaw[eye] = passData as RawTemporalPassData;
            }
            catch (Exception error) { Fail("record", error); }
        }

        static bool ValidJitter(Matrix4x4 matrix)
        {
            for (int i = 0; i != 16; ++i) if (float.IsNaN(matrix[i]) || float.IsInfinity(matrix[i])) return false;
            var expected = Matrix4x4.Translate(new Vector3(matrix.m03, matrix.m13, 0));
            for (int i = 0; i != 16; ++i) if (Mathf.Abs(matrix[i] - expected[i]) > 0.00001f) return false;
            return true;
        }

        static void SubmitTemporal(Capture capture, TextureHandle colorHandle, TextureHandle outputHandle, TextureHandle inputDepth, TextureHandle inputMotion, Vector2Int outputPixels, RenderGraphContext context)
        {
            if (!installed || requestedMode == 0 || faulted || stopping) return;
            ulong ticket = 0; bool submitted = false, enqueued = false;
            long start = allDiagnosticsEnabled ? Stopwatch.GetTimestamp() : 0;
            try
            {
                EnsureThread();
                if (capture.Generation != generation || capture.Frame != Time.frameCount) return;
                // A pooled raw pass-data object may execute only once for this capture.
                capture.Generation = 0;
                if (capture.Camera == null || bufferOwner(capture.Buffer) != capture.Camera) throw new InvalidOperationException("Temporal owner expired");
                if (lastQueuedFrame[capture.Eye] == capture.Frame) return;
                if (pendingTickets.Count >= MaxPending) throw new InvalidOperationException("Neural pending queue exceeded bound");
                Surface color = Resolve(colorHandle, capture.Pixels, "color");
                Surface output = Resolve(outputHandle, outputPixels, "destination");
                Surface depth = Resolve(inputDepth, capture.Pixels, "depth");
                Surface motion = Resolve(inputMotion, capture.Pixels, "motion");
                ValidateSurfaces(color, output, depth, motion);
                // Native 0.1.17 converts the engine color into private linear RGBA16F.
                // HDR describes that NGX input, independently of the original LDR encoding.
                uint flags = NeuralNative.Hdr | NeuralNative.LowResolutionMotion | NeuralNative.AutoExposure;
                if (color.Descriptor.sRGB) flags |= NeuralNative.ColorSrgb;
                if (requestedPreset == 11) flags |= NeuralNative.ForcePresetK;
                if (SystemInfo.usesReversedZBuffer) flags |= NeuralNative.ReversedDepth;
                ConfigureNative(flags);
                bool reset = resetPending[capture.Eye] || previousCameras[capture.Eye] != capture.Camera;
                var job = new NeuralNative.Job {
                    size = 160, abi = NeuralNative.Abi, generation = generation, frame = (ulong)capture.Frame,
                    cameraId = unchecked((ulong)(long)capture.Camera.GetInstanceID()), eye = (uint)capture.Eye,
                    flags = (reset ? NeuralNative.Reset : 0) | (capture.Present ? NeuralNative.Present : 0),
                    color = Pointer(color), depth = Pointer(depth), motion = Pointer(motion), destination = Pointer(output),
                    renderWidth = (uint)capture.Pixels.x, renderHeight = (uint)capture.Pixels.y,
                    outputWidth = (uint)outputPixels.x, outputHeight = (uint)outputPixels.y,
                    // Installed DXBC: MV=(current-prev) UV; NGX wants current-to-previous pixels.
                    mvScaleX = -capture.Pixels.x, mvScaleY = -capture.Pixels.y,
                    jitterX = capture.Jitter.m03 * capture.Pixels.x * 0.5f,
                    jitterY = capture.Jitter.m13 * capture.Pixels.y * 0.5f,
                    preExposure = 1, exposureScale = 1, frameTimeMs = Mathf.Max(0.001f, Time.unscaledDeltaTime * 1000), sharpness = requestedSharpness };
                job.fallback = job.destination;
                if (Mathf.Abs(job.jitterX) > 0.501f || Mathf.Abs(job.jitterY) > 0.501f) throw new InvalidOperationException("Jitter outside installed Halton range");
                lastInputSize[capture.Eye] = capture.Pixels;
                lastOutputSize[capture.Eye] = outputPixels;
                lastJitterPixels[capture.Eye] = new Vector2(job.jitterX, job.jitterY);
                IntPtr eventData;
                if (NeuralNative.RTN_SubmitJob(ref job, out eventData, out ticket) != 1)
                    throw new InvalidOperationException(NativeFailureMessage("Native rejected temporal job"));
                submitted = true;
                if (eventData == IntPtr.Zero || ticket == 0) throw new InvalidOperationException("Native returned an invalid event ticket");
                context.cmd.IssuePluginEventAndData(renderEvent, (int)NeuralNative.EvaluateEvent, eventData);
                enqueued = true; pendingTickets.Add(new Pending { Ticket = ticket, Epoch = historyEpoch }); ++queuedJobs;
                resetPending[capture.Eye] = false; previousCameras[capture.Eye] = capture.Camera; lastQueuedFrame[capture.Eye] = capture.Frame;
                if (diagnosedGeneration[capture.Eye] != generation)
                {
                    diagnosedGeneration[capture.Eye] = generation;
                    Log("first-job-queued generation=" + generation + " eye=" + capture.Eye + " frame=" + capture.Frame +
                        " camera=" + job.cameraId + " input=" + capture.Pixels.x + "x" + capture.Pixels.y +
                        " output=" + job.outputWidth + "x" + job.outputHeight +
                        " color=" + Describe(color) + " destination=" + Describe(output) + " depth=" + Describe(depth) + " motion=" + Describe(motion) +
                        " featureFlags=" + flags + " jobFlags=" + job.flags + " jitter=" + job.jitterX.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                        "," + job.jitterY.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " mvScale=" + job.mvScaleX + "," + job.mvScaleY);
                }
            }
            catch (Exception error)
            {
                if (submitted && !enqueued && ticket != 0) { try { NeuralNative.RTN_CancelJob(ticket); } catch { } }
                Fail("temporal-event", error); // Raw current-frame output was already recorded; no TAA/history fallback.
            }
            finally { if (start != 0 && allDiagnosticsEnabled) metadataCpuMs += (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency; }
        }

        static Surface Resolve(TextureHandle handle, Vector2Int expected, string label)
        {
            if (!handle.IsValid()) throw new InvalidOperationException(label + " handle invalid");
            RTHandle rtHandle = handle;
            if (rtHandle == null || rtHandle.rt == null) throw new InvalidOperationException(label + " has no RenderTexture");
            RenderTexture texture = rtHandle.rt; var descriptor = texture.descriptor;
            Vector2Int viewport = rtHandle.useScaling ? rtHandle.GetScaledSize(rtHandle.rtHandleProperties.currentViewportSize) : new Vector2Int(descriptor.width, descriptor.height);
            // Legacy RenderGraph -> CoreUtils.SetViewport uses origin(0,0); reject unknown subrects.
            if (viewport != expected || descriptor.width < viewport.x || descriptor.height < viewport.y || !texture.IsCreated() ||
                descriptor.dimension != TextureDimension.Tex2D || descriptor.volumeDepth != 1 || descriptor.msaaSamples != 1 || descriptor.useDynamicScale)
                throw new InvalidOperationException(label + " viewport/physical texture contract mismatch: expected=" + expected.x + "x" + expected.y +
                    " viewport=" + viewport.x + "x" + viewport.y + " physical=" + descriptor.width + "x" + descriptor.height +
                    " format=" + descriptor.graphicsFormat + " depthFormat=" + descriptor.depthStencilFormat +
                    " useScaling=" + rtHandle.useScaling + " useDynamicScale=" + descriptor.useDynamicScale +
                    " dimension=" + descriptor.dimension + " slices=" + descriptor.volumeDepth + " MSAA=" + descriptor.msaaSamples + " created=" + texture.IsCreated());
            return new Surface { Texture = texture, Descriptor = descriptor, Viewport = viewport };
        }
        static string Describe(Surface surface) => surface.Descriptor.width + "x" + surface.Descriptor.height + "/" +
            surface.Descriptor.graphicsFormat + "/depth=" + surface.Descriptor.depthStencilFormat + "/sRGB=" + surface.Descriptor.sRGB;

        static void ValidateSurfaces(Surface color, Surface output, Surface depth, Surface motion)
        {
            if (ReferenceEquals(color.Texture, output.Texture) || ReferenceEquals(color.Texture, depth.Texture) || ReferenceEquals(color.Texture, motion.Texture) ||
                ReferenceEquals(output.Texture, depth.Texture) || ReferenceEquals(output.Texture, motion.Texture) || ReferenceEquals(depth.Texture, motion.Texture))
                throw new InvalidOperationException("Neural input/output resource alias");
            if (color.Descriptor.graphicsFormat != output.Descriptor.graphicsFormat || color.Descriptor.sRGB != output.Descriptor.sRGB ||
                motion.Descriptor.graphicsFormat != GraphicsFormat.R16G16_SFloat)
                throw new InvalidOperationException("Neural color/motion format mismatch");
            // Native checks actual D3D11 bind flags and depth SRV compatibility. No fabricated R32 assumption.
        }

        static IntPtr Pointer(Surface surface)
        {
            PointerEntry entry;
            if (pointers.TryGetValue(surface.Texture, out entry))
            {
                if (SameDescriptor(entry.Descriptor, surface.Descriptor)) return entry.Pointer;
                ForgetPointer(surface.Texture);
            }
            if (pointers.Count >= MaxPointers) ClearPointers();
            IntPtr pointer = surface.Texture.GetNativeTexturePtr(); ++pointerFetches;
            if (pointer == IntPtr.Zero) throw new InvalidOperationException("Texture native pointer unavailable");
            Marshal.AddRef(pointer); // Cache owns a COM reference; each accepted native job owns another.
            try { pointers.Add(surface.Texture, new PointerEntry { Pointer = pointer, Descriptor = surface.Descriptor }); }
            catch { Marshal.Release(pointer); throw; }
            return pointer;
        }
        static bool SameDescriptor(RenderTextureDescriptor a, RenderTextureDescriptor b)
        {
            // Compare native-allocation fields explicitly; ValueType.Equals would box both structs per eye/frame.
            return a.width == b.width && a.height == b.height && a.volumeDepth == b.volumeDepth &&
                a.graphicsFormat == b.graphicsFormat && a.depthStencilFormat == b.depthStencilFormat &&
                a.msaaSamples == b.msaaSamples && a.dimension == b.dimension && a.mipCount == b.mipCount &&
                a.sRGB == b.sRGB && a.useDynamicScale == b.useDynamicScale && a.enableRandomWrite == b.enableRandomWrite &&
                a.bindMS == b.bindMS && a.memoryless == b.memoryless && a.vrUsage == b.vrUsage;
        }
        static void ForgetPointer(RenderTexture texture)
        {
            if (ReferenceEquals(texture, null)) return;
            PointerEntry entry;
            if (pointers.TryGetValue(texture, out entry)) { pointers.Remove(texture); Marshal.Release(entry.Pointer); ++pointerInvalidations; }
        }
        static void ClearPointers()
        {
            foreach (var entry in pointers.Values) Marshal.Release(entry.Pointer);
            pointers.Clear(); ++pointerInvalidations;
        }

        static void ConfigureNative(uint flags)
        {
            if (configured)
            {
                if (configuredFlags != flags) throw new InvalidOperationException("Per-eye HDR/depth convention changed inside a neural generation");
                return;
            }
            IntPtr directory = Marshal.StringToHGlobalUni(runtimeDirectory), logPath = IntPtr.Zero;
            try
            {
                if (!string.IsNullOrEmpty(nativeLogPath)) logPath = Marshal.StringToHGlobalUni(nativeLogPath);
                uint nativeMode = requestedMode != 3 || requestedScale >= 0.999f ? 0u : requestedScale >= 0.63f ? 1u : requestedScale >= 0.56f ? 2u : 3u;
                var config = new NeuralNative.Config { size = 40, abi = NeuralNative.Abi, generation = generation, mode = nativeMode, featureFlags = flags, featureDirectory = directory, logPath = logPath };
                if (NeuralNative.RTN_Configure(ref config) != 1) throw new InvalidOperationException(NativeFailureMessage("Native configuration rejected"));
                configuredFlags = flags; configured = true;
            }
            finally { Marshal.FreeHGlobal(directory); if (logPath != IntPtr.Zero) Marshal.FreeHGlobal(logPath); }
        }

        static void Poll()
        {
            if (!loaded) return;
            backend = new NeuralNative.BackendStatus { size = 72, abi = NeuralNative.Abi };
            if (NeuralNative.RTN_GetBackendStatus(ref backend) != 1) throw new InvalidOperationException("Native backend status unavailable");
            runtimeInfo.Refresh(backend, Stopwatch.GetTimestamp());
            presetStatus = new NeuralNative.PresetStatus { size = 32, abi = NeuralNative.Abi, identifiedLeft = uint.MaxValue, identifiedRight = uint.MaxValue };
            NeuralNative.RTN_GetPresetStatus(ref presetStatus);
            if (backend.state == 3 && !stopping) throw new InvalidOperationException(NativeFailureMessage("Native neural failure at frame " + backend.lastFailureFrame));
            for (int i = pendingTickets.Count - 1; i >= 0; --i)
            {
                var status = new NeuralNative.JobStatus { size = 56, abi = NeuralNative.Abi };
                Pending pending = pendingTickets[i];
                if (NeuralNative.RTN_GetJobStatus(pending.Ticket, ref status) != 1) throw new InvalidOperationException("Neural job status expired before consumption");
                if (status.state == 1) continue;
                pendingTickets.RemoveAt(i); nativeCpuMs += status.cpuMs;
                if (status.state == 2) ++shadowJobs;
                else if (status.state == 3) ++presentedJobs;
                else ++fallbackJobs;
                if (status.generation != generation || pending.Epoch != historyEpoch || status.eye > 1 || (status.state != 2 && status.state != 3)) continue;
                if (status.state == 3)
                {
                    lastPresentedFrame[status.eye] = Math.Max(lastPresentedFrame[status.eye], (int)status.frame);
                    int presentedMask; presentedFrames.TryGetValue(status.frame, out presentedMask); presentedMask |= 1 << (int)status.eye;
                    if (presentedMask == 3) { presentedFrames.Remove(status.frame); lastPresentedPair = Math.Max(lastPresentedPair, (int)status.frame); }
                    else presentedFrames[status.frame] = presentedMask;
                    if (presentedFrames.Count > MaxPending) throw new InvalidOperationException("Unpaired neural presentation exceeded bound");
                }
                if (successfulPairs < WarmupPairs)
                {
                    int mask; warmupFrames.TryGetValue(status.frame, out mask); mask |= 1 << (int)status.eye;
                    if (mask == 3) { warmupFrames.Remove(status.frame); ++successfulPairs; }
                    else warmupFrames[status.frame] = mask;
                    if (warmupFrames.Count > MaxPending) throw new InvalidOperationException("Unpaired neural warmup exceeded bound");
                    if (successfulPairs == WarmupPairs) Log(WarmupPairs + " successful neural pairs; " + (requestedMode >= 2 ? "presentation eligible next frame" : "shadow mode remains active"));
                }
            }
        }

        static void EnsureThread()
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != ownerThread) throw new InvalidOperationException("Neural managed callback thread changed");
        }
        static string NativeFailureMessage(string fallback)
        {
            // Failure-only refresh happens before Fail requests shutdown. Do
            // not let optional diagnostic metadata cause a second failure.
            try
            {
                var current = new NeuralNative.BackendStatus { size = 72, abi = NeuralNative.Abi };
                if (NeuralNative.RTN_GetBackendStatus(ref current) == 1) backend = current;
                runtimeInfo.Refresh(backend, Stopwatch.GetTimestamp(), true);
            }
            catch { }
            return runtimeInfo.Failure(fallback, backend, generation);
        }
        static void Log(string message) { try { if (log != null) log("[neural] " + message); } catch { } }
        static void Fail(string stage, Exception error)
        {
            if (!faulted) { lastError = stage + ": " + error.Message; Log("disabled; " + lastError + "; current-frame image without antialiasing retained"); }
            faulted = true; presentThisFrame = false;
            captures = new ConditionalWeakTable<object, Capture>();
            if (loaded && !stopping) { try { NeuralNative.RTN_RequestShutdown(); stopping = true; } catch { } }
        }

        internal static bool IsStopping => stopping;
        internal static bool IsFaulted => faulted;
        internal static string RuntimeStatusText() => loaded ? runtimeInfo.PanelText(backend) : ModLocalization.Format("No effective runtime loaded\nRecommended: {0}", NeuralRuntimeInfo.RecommendedVersion);
        internal static string RuntimeNoticeText() => loaded && requestedMode != 0 ? runtimeInfo.Notice(backend) : "";
        internal static bool EffectivePresentedRecent => requestedMode >= 2 && !faulted && !stopping && lastPresentedPair >= 0 && Time.frameCount - lastPresentedPair <= 8;
        internal static string InternalSizeText() => lastInputSize[0].x + "x" + lastInputSize[0].y;
        internal static void SetOutputSize(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException("Neural output size");
            outputSize = new Vector2Int(width, height);
        }
        internal static void SetSharpness(float sharpness)
        {
            if (float.IsNaN(sharpness) || float.IsInfinity(sharpness)) throw new ArgumentOutOfRangeException(nameof(sharpness));
            requestedSharpness = Mathf.Clamp01(sharpness);
        }
        internal static string StatusLine()
        {
            if (faulted)
            {
                string detail = ModLocalization.DiagnosticText(lastError);
                return ModLocalization.Format("No AA fallback: {0}", detail != null && detail.Length > 130 ? detail.Substring(0, 130) + "..." : detail);
            }
            if (requestedMode == 0) return ModLocalization.Text(stopping ? "Off (releasing resources)" : "Off");
            string mode = requestedMode == 3 ? "DLSS" : "DLAA";
            if (stopping) return ModLocalization.Format("No AA (waiting to switch to {0})", mode);
            if (successfulPairs < WarmupPairs) return ModLocalization.Format("{0}: warmup {1}/{2} (no AA visible)", mode, successfulPairs, WarmupPairs);
            if (requestedMode == 1) return ModLocalization.Text("DLAA diagnostic (no AA displayed)");
            if (!EffectivePresentedRecent) return ModLocalization.Format("{0}: waiting for confirmed presentation", mode);
            string identified = presetStatus.generation == generation && presetStatus.evidence != 0 ?
                PresetName(presetStatus.identifiedLeft) + "/" + PresetName(presetStatus.identifiedRight) : ModLocalization.Text("unidentified");
            return ModLocalization.Format("{0} active {1}x{2} -> {3}x{4} | {5} -> {6}", mode, lastInputSize[0].x, lastInputSize[0].y, lastOutputSize[0].x, lastOutputSize[0].y, requestedPreset == 0 ? "Auto" : "K", identified);
        }
        static string PresetName(uint value) => value == 11 ? "K" : value == 13 ? "M" : value == uint.MaxValue ? "?" : value.ToString();

        internal static object Snapshot() => new {
            Installed = installed, RequestedMode = requestedMode, Faulted = faulted, LastError = lastError,
            Generation = generation, Configured = configured, FeatureFlags = configuredFlags,
            WarmupPairs = successfulPairs, RequiredWarmupPairs = WarmupPairs, PresentationEligible = requestedMode >= 2 && successfulPairs >= WarmupPairs && !faulted,
            EffectivePresentedRecent = EffectivePresentedRecent, LastPresentedPairFrame = lastPresentedPair, LastPresentedLeftFrame = lastPresentedFrame[0], LastPresentedRightFrame = lastPresentedFrame[1],
            InputScale = requestedScale, Sharpness = requestedSharpness, RequestedPreset = requestedPreset,
            PresetGeneration = presetStatus.generation, PresetEvidence = presetStatus.evidence, IdentifiedLeftPreset = presetStatus.identifiedLeft, IdentifiedRightPreset = presetStatus.identifiedRight,
            OutputWidth = outputSize.x, OutputHeight = outputSize.y,
            PendingJobs = pendingTickets.Count, NativeState = backend.state, NativeReason = backend.reason, Stopping = stopping,
            NativePendingJobs = backend.pendingJobs, NativeRetiredBatches = backend.retiredBatches,
            NativeLastLeftFrame = backend.lastLeftFrame, NativeLastRightFrame = backend.lastRightFrame, NativeLastFailureFrame = backend.lastFailureFrame,
            RuntimeVersion = backend.runtimeMajor + "." + backend.runtimeMinor + "." + backend.runtimePatch + "." + backend.runtimeBuild,
            EffectiveRuntime = new { Version = runtimeInfo.Version(backend), Source = runtimeInfo.Source, Identity = runtimeInfo.Identity,
                Path = runtimeInfo.HasStatus ? runtimeInfo.Current.path : null, Generation = runtimeInfo.HasStatus ? runtimeInfo.Current.generation : 0,
                Recommended = NeuralRuntimeInfo.RecommendedVersion, MatchesRecommended = runtimeInfo.IsRecommended(backend),
                Flags = runtimeInfo.HasStatus ? runtimeInfo.Current.flags : 0, IdentityAvailable = runtimeInfo.HasIdentity,
                MetadataError = runtimeInfo.ReadError, MissingOptionalExport = runtimeInfo.MissingExport, MetadataReads = runtimeInfo.Reads,
                LastNativeReason = runtimeInfo.HasStatus ? runtimeInfo.Current.lastErrorReason : 0, LastNativeMessage = runtimeInfo.HasStatus ? runtimeInfo.Current.message : null },
            QueuedJobs = queuedJobs, ShadowJobs = shadowJobs, PresentedJobs = presentedJobs, FallbackJobs = fallbackJobs,
            PointerFetches = pointerFetches, PointerInvalidations = pointerInvalidations, CachedPointers = pointers.Count,
            LeftInputWidth = lastInputSize[0].x, LeftInputHeight = lastInputSize[0].y,
            RightInputWidth = lastInputSize[1].x, RightInputHeight = lastInputSize[1].y,
            LeftOutputWidth = lastOutputSize[0].x, LeftOutputHeight = lastOutputSize[0].y, RightOutputWidth = lastOutputSize[1].x, RightOutputHeight = lastOutputSize[1].y,
            LeftJitterX = lastJitterPixels[0].x, LeftJitterY = lastJitterPixels[0].y,
            RightJitterX = lastJitterPixels[1].x, RightJitterY = lastJitterPixels[1].y,
            ManagedMetadataCpuMs = metadataCpuMs, NativeCpuMs = nativeCpuMs,
            InputConvention = "native UV current-minus-previous; NGX scale=-renderWidth,-renderHeight; nonjittered motion",
            OriginalTaaAndHistoryPreserved = false, RawPasses = rawPasses, RawPromotedCopiesAvoided = rawPromotedSkips, RawFallbackCopies = rawFallbackCopies, OriginalTaaPassesBypassed = originalTaaPassesBypassed, SameFrameGpuCompletionNotProvenByManagedCallback = true
        };
    }
}
