using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

namespace RTMaquetaXR.Diagnostics
{
    // Passive, opt-in metadata observer. Neither this type nor its static initializer patches anything.
    public static class WaaaghTemporalProbe
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        const int WarmupFrames = 64, FrameGap = 72, LimitPerEye = 8;
        static Harmony harmony;
        static Func<int> getEye;
        static Func<Camera> getCamera;
        static Action<string> log;
        static MethodInfo registrationMethod, callbackMethod;
        static Type renderingDataType, passDataType;
        static FieldInfo cameraDataField, cameraField, bufferField, bufferOwnerField, jitterField;
        static FieldInfo sourceField, destinationField, motionField, depthField;
        static bool installed, enabled, failed;
        static int epoch, armFrame, ownerThread;
        static readonly int[] attempts = new int[2], samples = new int[2], lastAttemptFrame = { -1, -1 };
        static ConditionalWeakTable<object, Registration> registrations = new ConditionalWeakTable<object, Registration>();

        // Contains values and owner references only. Never retain a TextureHandle, RTHandle or texture.
        sealed class Registration
        {
            public int Eye, Frame, Epoch, CameraId, BufferId;
            public string CameraName;
            public Camera Camera;
            public object Buffer;
            public Matrix4x4 Jitter;
        }

        public static bool Install(Harmony patcher, Func<int> eyeIndexGetter, Func<Camera> cameraGetter, Action<string> logger)
        {
            if (installed) return !failed;
            if (patcher == null || eyeIndexGetter == null || cameraGetter == null || logger == null)
                throw new ArgumentNullException("Probe dependencies cannot be null");
            harmony = patcher; getEye = eyeIndexGetter; getCamera = cameraGetter; log = logger;
            ownerThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            try
            {
                ResolveContract();
                // Patch callback first. Until the registration hook exists it is an inert lookup.
                harmony.Patch(callbackMethod, postfix: new HarmonyMethod(typeof(WaaaghTemporalProbe), nameof(ObserveExecution)));
                harmony.Patch(registrationMethod, transpiler: new HarmonyMethod(typeof(WaaaghTemporalProbe), nameof(ObserveRegistration)));
                installed = true; failed = false;
                Reset();
                SafeLog("[temporal-probe] installed; Unity descriptors only; 8 samples/eye; warmup=64 frames; interval=72 frames; no GPU readback");
                return true;
            }
            catch (Exception error)
            {
                Fail("install", error);
                RemoveOwnHooks();
                return false;
            }
        }

        // Explicitly arm a new bounded campaign after attach/recenter if desired. Not called each frame.
        public static void Reset()
        {
            if (!installed || failed) return;
            try
            {
                EnsureThread();
                ++epoch; armFrame = Time.frameCount;
                for (int i = 0; i != 2; ++i) { attempts[i] = samples[i] = 0; lastAttemptFrame[i] = -1; }
                registrations = new ConditionalWeakTable<object, Registration>();
                enabled = true;
            }
            catch (Exception error) { Fail("reset", error); }
        }

        // Invalidate pending records on detach; leaves hooks inert until an explicit Reset.
        public static void Detach()
        {
            enabled = false; ++epoch;
            registrations = new ConditionalWeakTable<object, Registration>();
        }

        // Call on the same Unity thread, outside rendering. Removes only these exact patch methods.
        public static void Stop()
        {
            Detach();
            RemoveOwnHooks();
            installed = false;
            getEye = null; getCamera = null; log = null;
        }

        static void RemoveOwnHooks()
        {
            if (harmony == null) return;
            try
            {
                if (registrationMethod != null)
                    harmony.Unpatch(registrationMethod, AccessTools.Method(typeof(WaaaghTemporalProbe), nameof(ObserveRegistration)));
                if (callbackMethod != null)
                    harmony.Unpatch(callbackMethod, AccessTools.Method(typeof(WaaaghTemporalProbe), nameof(ObserveExecution)));
            }
            catch (Exception error) { SafeLog("[temporal-probe] unpatch failed; observer remains disabled: " + error.Message); }
        }

        static FieldInfo RequiredField(Type type, string name, Type expected)
        {
            var field = AccessTools.Field(type, name);
            if (field == null || field.IsStatic || (expected != null && field.FieldType != expected))
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        static void ResolveContract()
        {
            var pass = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.Passes.PostProcess.PostProcessPass");
            renderingDataType = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.RenderingData");
            if (pass == null || renderingDataType == null || !renderingDataType.IsValueType)
                throw new TypeLoadException("Waaagh PostProcessPass/RenderingData contract missing");
            registrationMethod = null;
            foreach (var method in pass.GetMethods(All))
                if (method.Name == "DoTemporalAntialiasing")
                {
                    if (registrationMethod != null) throw new AmbiguousMatchException("DoTemporalAntialiasing");
                    registrationMethod = method;
                }
            var args = registrationMethod == null ? null : registrationMethod.GetParameters();
            if (args == null || args.Length != 3 || registrationMethod.IsStatic || registrationMethod.ReturnType != typeof(void) ||
                args[0].ParameterType != renderingDataType.MakeByRefType() || args[1].ParameterType != typeof(TextureHandle) || args[2].ParameterType != typeof(TextureHandle))
                throw new MissingMethodException("DoTemporalAntialiasing(ref RenderingData, TextureHandle, TextureHandle)");
            passDataType = pass.GetNestedType("TaaPassData", BindingFlags.NonPublic | BindingFlags.Public);
            if (passDataType == null || passDataType.IsValueType) throw new TypeLoadException("TaaPassData must be a reference type");
            sourceField = RequiredField(passDataType, "Source", typeof(TextureHandle));
            destinationField = RequiredField(passDataType, "Destination", typeof(TextureHandle));
            motionField = RequiredField(passDataType, "VelocityBuffer", typeof(TextureHandle));
            depthField = RequiredField(passDataType, "CameraDepthCopyRT", typeof(TextureHandle));
            cameraDataField = RequiredField(renderingDataType, "CameraData", null);
            cameraField = RequiredField(cameraDataField.FieldType, "Camera", typeof(Camera));
            bufferField = RequiredField(cameraDataField.FieldType, "CameraBuffer", null);
            bufferOwnerField = RequiredField(bufferField.FieldType, "Camera", typeof(Camera));
            jitterField = RequiredField(bufferField.FieldType, "JitterMatrix", typeof(Matrix4x4));
            callbackMethod = null;
            foreach (var nested in pass.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (var method in nested.GetMethods(All))
                {
                    var p = method.GetParameters();
                    if (method.Name.Contains("<DoTemporalAntialiasing>") && method.ReturnType == typeof(void) &&
                        !method.IsStatic && p.Length == 2 && p[0].ParameterType == passDataType && p[1].ParameterType == typeof(RenderGraphContext))
                    {
                        if (callbackMethod != null) throw new AmbiguousMatchException("TAA callback");
                        callbackMethod = method;
                    }
                }
            if (callbackMethod == null) throw new MissingMethodException("TAA callback(TaaPassData, RenderGraphContext)");
        }

        static IEnumerable<CodeInstruction> ObserveRegistration(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = new List<CodeInstruction>(instructions);
            int found = -1; object passLocal = null;
            for (int i = 0; i < code.Count; ++i)
            {
                var method = code[i].operand as MethodInfo;
                if (method == null || !method.IsGenericMethod || method.Name != "AddRenderPass" ||
                    method.DeclaringType != typeof(RenderGraph) || method.GetGenericArguments().Length != 1 ||
                    method.GetGenericArguments()[0] != passDataType) continue;
                if (found >= 0 || i < 4 || i + 1 >= code.Count || !code[i + 1].opcode.Name.StartsWith("stloc"))
                    throw new InvalidOperationException("Unexpected/ambiguous TAA AddRenderPass");
                // Installed overload: (string, out TaaPassData, string file, int line).
                var p = method.GetParameters();
                if (p.Length != 4 || p[0].ParameterType != typeof(string) || p[1].ParameterType != passDataType.MakeByRefType() ||
                    p[2].ParameterType != typeof(string) || p[3].ParameterType != typeof(int) ||
                    (code[i - 3].opcode != OpCodes.Ldloca && code[i - 3].opcode != OpCodes.Ldloca_S))
                    throw new InvalidOperationException("TAA AddRenderPass out-local contract changed");
                passLocal = code[i - 3].operand; found = i + 2;
            }
            if (found < 0 || passLocal == null) throw new InvalidOperationException("TAA pass registration site missing");
            var skip = generator.DefineLabel();
            var end = new CodeInstruction(OpCodes.Nop); end.labels.Add(skip);
            // Nothing is boxed when the sample is not due. Original ref data is never written.
            code.InsertRange(found, new[] {
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WaaaghTemporalProbe), nameof(SampleDue))),
                new CodeInstruction(OpCodes.Brfalse, skip),
                new CodeInstruction(OpCodes.Ldloc, passLocal),
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldobj, renderingDataType),
                new CodeInstruction(OpCodes.Box, renderingDataType),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WaaaghTemporalProbe), nameof(CaptureRegistration))), end });
            return code;
        }

        static void EnsureThread()
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Unexpected thread for Unity graph observation");
        }

        static bool SampleDue()
        {
            if (!enabled || failed) return false;
            try
            {
                EnsureThread();
                int frame = Time.frameCount;
                // Both graphs may register before either callback executes. Give final records
                // their whole frame, then expire a campaign whose final passes never executed.
                if (attempts[0] >= LimitPerEye && attempts[1] >= LimitPerEye &&
                    frame > Math.Max(lastAttemptFrame[0], lastAttemptFrame[1]) + 2)
                { FinishCampaign(); return false; }
                int eye = getEye();
                if (eye < 0 || eye > 1 || attempts[eye] >= LimitPerEye || frame < armFrame || frame - armFrame < WarmupFrames)
                    return false;
                return lastAttemptFrame[eye] < 0 || (frame >= lastAttemptFrame[eye] && frame - lastAttemptFrame[eye] >= FrameGap);
            }
            catch (Exception error) { Fail("sample-gate", error); return false; }
        }

        static void CaptureRegistration(object passData, object renderingData)
        {
            if (!enabled || failed) return;
            try
            {
                EnsureThread();
                int eye = getEye(), frame = Time.frameCount;
                if (eye < 0 || eye > 1 || !SampleDue()) return;
                Camera expected = getCamera();
                if (expected == null || passData == null || passData.GetType() != passDataType || renderingData == null || renderingData.GetType() != renderingDataType)
                    throw new InvalidOperationException("Invalid capture owner/data");
                object cameraData = cameraDataField.GetValue(renderingData);
                Camera camera = (Camera)cameraField.GetValue(cameraData);
                object buffer = bufferField.GetValue(cameraData);
                if (camera != expected || buffer == null || (Camera)bufferOwnerField.GetValue(buffer) != expected)
                    throw new InvalidOperationException("Camera/CameraBuffer ownership mismatch");
                Matrix4x4 jitter = (Matrix4x4)jitterField.GetValue(buffer);
                for (int i = 0; i != 16; ++i)
                    if (float.IsNaN(jitter[i]) || float.IsInfinity(jitter[i])) throw new InvalidOperationException("Non-finite JitterMatrix");
                var record = new Registration { Eye = eye, Frame = frame, Epoch = epoch, Camera = camera, Buffer = buffer,
                    CameraId = camera.GetInstanceID(), CameraName = camera.name, BufferId = RuntimeHelpers.GetHashCode(buffer), Jitter = jitter };
                registrations.Remove(passData); registrations.Add(passData, record);
                ++attempts[eye]; lastAttemptFrame[eye] = frame;
            }
            catch (Exception error) { Fail("registration", error); }
        }

        // object __0 is the reference-type pass data. No __args array / RenderGraphContext boxing.
        // Postfix only: never skips the original; original exceptions propagate without modification.
        static void ObserveExecution(object __0)
        {
            if (!enabled || failed || __0 == null) return;
            try
            {
                EnsureThread();
                Registration capture;
                if (!registrations.TryGetValue(__0, out capture)) return;
                registrations.Remove(__0); // Remove before reading anything; pooled pass data cannot replay a sample.
                if (capture.Epoch != epoch || capture.Frame != Time.frameCount) return;
                if (capture.Camera == null || (Camera)bufferOwnerField.GetValue(capture.Buffer) != capture.Camera)
                    throw new InvalidOperationException("CameraBuffer owner changed before execution");
                var clock = Stopwatch.StartNew();
                var text = new StringBuilder(1400);
                text.Append("{\"Schema\":\"waaagh-temporal-metadata/v1\",\"Eye\":").Append(capture.Eye)
                    .Append(",\"Frame\":").Append(capture.Frame).Append(",\"Epoch\":").Append(capture.Epoch)
                    .Append(",\"CameraId\":").Append(capture.CameraId).Append(",\"CameraName\":");
                String(text, capture.CameraName);
                text.Append(",\"CameraBufferId\":").Append(capture.BufferId)
                    .Append(",\"OwnerVerified\":true,\"CallbackCompleted\":true,\"GpuExecutionVerified\":false,\"ReversedZ\":")
                    .Append(SystemInfo.usesReversedZBuffer ? "true" : "false").Append(",\"JitterMatrix\":[");
                for (int i = 0; i != 16; ++i) { if (i != 0) text.Append(','); Number(text, capture.Jitter[i]); }
                text.Append("],\"JitterMatrixOrder\":\"Unity linear index (column major)\",\"Textures\":{");
                Describe(text, "Source", sourceField, __0); text.Append(',');
                Describe(text, "Destination", destinationField, __0); text.Append(',');
                Describe(text, "Motion", motionField, __0); text.Append(',');
                Describe(text, "DepthCopy", depthField, __0);
                text.Append("},\"MetadataCpuMsExcludingLog\":"); Number(text, clock.Elapsed.TotalMilliseconds);
                text.Append('}');
                ++samples[capture.Eye];
                SafeLog("[temporal-probe] " + text);
                if (samples[0] >= LimitPerEye && samples[1] >= LimitPerEye) FinishCampaign();
            }
            catch (Exception error) { Fail("execution", error); }
        }

        static void Describe(StringBuilder text, string label, FieldInfo field, object passData)
        {
            // Resolve and discard within the live graph callback. No native pointer or pixel access.
            TextureHandle handle = (TextureHandle)field.GetValue(passData);
            if (!handle.IsValid()) throw new InvalidOperationException(label + " handle is invalid");
            RenderTexture texture = handle;
            if (texture == null) throw new InvalidOperationException(label + " is not a concrete RenderTexture");
            var descriptor = texture.descriptor;
            if (descriptor.width <= 0 || descriptor.height <= 0) throw new InvalidOperationException(label + " has invalid size");
            String(text, label); text.Append(":{\"Id\":").Append(texture.GetInstanceID()).Append(",\"Name\":"); String(text, texture.name);
            text.Append(",\"Width\":").Append(descriptor.width).Append(",\"Height\":").Append(descriptor.height)
                .Append(",\"VolumeDepth\":").Append(descriptor.volumeDepth).Append(",\"MsaaSamples\":").Append(descriptor.msaaSamples)
                .Append(",\"GraphicsFormat\":"); String(text, descriptor.graphicsFormat.ToString());
            text.Append(",\"DepthStencilFormat\":"); String(text, descriptor.depthStencilFormat.ToString());
            text.Append(",\"Dimension\":"); String(text, descriptor.dimension.ToString());
            text.Append(",\"Srgb\":").Append(descriptor.sRGB ? "true" : "false")
                .Append(",\"RandomWrite\":").Append(descriptor.enableRandomWrite ? "true" : "false")
                .Append(",\"DynamicScale\":").Append(descriptor.useDynamicScale ? "true" : "false")
                .Append(",\"Created\":").Append(texture.IsCreated() ? "true" : "false").Append('}');
        }

        static void Number(StringBuilder text, double value) { text.Append(value.ToString("R", CultureInfo.InvariantCulture)); }
        static void FinishCampaign()
        {
            enabled = false;
            registrations = new ConditionalWeakTable<object, Registration>();
            SafeLog("[temporal-probe] campaign complete; left=" + samples[0] + "; right=" + samples[1] +
                "; attemptedLeft=" + attempts[0] + "; attemptedRight=" + attempts[1] + "; observer disabled");
        }
        static void String(StringBuilder text, string value)
        {
            text.Append('"');
            foreach (char c in value ?? "")
                if (c == '"' || c == '\\') { text.Append('\\'); text.Append(c); }
                else if (c < 32) text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else text.Append(c);
            text.Append('"');
        }
        static void SafeLog(string message) { try { if (log != null) log(message); } catch { } }
        static void Fail(string stage, Exception error)
        {
            enabled = false; registrations = new ConditionalWeakTable<object, Registration>();
            if (!failed) { failed = true; SafeLog("[temporal-probe] disabled at " + stage + ": " + error.GetType().Name + ": " + error.Message); }
        }
    }
}
