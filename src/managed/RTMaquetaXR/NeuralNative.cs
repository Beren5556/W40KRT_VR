using System;
using System.Runtime.InteropServices;

namespace RTMaquetaXR
{
    internal static class NeuralNative
    {
        internal const string Library = "RTNeural.dll";
        internal const uint Abi = 2, EvaluateEvent = 1, MaintenanceEvent = 2;
        internal const uint Hdr = 1, ReversedDepth = 2, LowResolutionMotion = 4, AutoExposure = 16, ForcePresetK = 32, ColorSrgb = 64;
        internal const uint Reset = 1, Present = 2;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct Config
        {
            internal uint size, abi;
            internal ulong generation;
            internal uint mode, featureFlags;
            internal IntPtr featureDirectory, logPath;
            internal uint requestedPreset, reserved;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct Job
        {
            internal uint size, abi;
            internal ulong generation, frame, cameraId;
            internal uint eye, flags;
            internal IntPtr color, depth, motion, destination, fallback;
            internal uint renderWidth, renderHeight, outputWidth, outputHeight;
            internal uint colorX, colorY, depthX, depthY, motionX, motionY, outputX, outputY;
            internal float jitterX, jitterY, mvScaleX, mvScaleY;
            internal float preExposure, exposureScale, frameTimeMs, sharpness;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct JobStatus
        {
            internal uint size, abi;
            internal ulong ticket, generation, frame;
            internal uint eye, state, reason, ngxResult;
            internal double cpuMs;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct BackendStatus
        {
            internal uint size, abi, state, reason;
            internal ulong generation, lastFailureFrame, lastLeftFrame, lastRightFrame;
            internal uint pendingJobs, retiredBatches, runtimeMajor, runtimeMinor, runtimePatch, runtimeBuild;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct PresetStatus
        {
            internal uint size, abi;
            internal ulong generation;
            internal uint requestedPreset, identifiedLeft, identifiedRight, evidence;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
        internal struct RuntimeStatus
        {
            internal uint size, abi;
            internal ulong generation;
            internal uint identity, source, flags, lastErrorReason;
            internal uint versionMajor, versionMinor, versionPatch, versionBuild;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)] internal string path;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string message;
        }
        internal static void CheckAbi()
        {
            if (IntPtr.Size != 8 || Marshal.SizeOf(typeof(Config)) != 48 || Marshal.SizeOf(typeof(Job)) != 160 ||
                Marshal.SizeOf(typeof(JobStatus)) != 56 || Marshal.SizeOf(typeof(BackendStatus)) != 72 || Marshal.SizeOf(typeof(PresetStatus)) != 32 ||
                Marshal.SizeOf(typeof(RuntimeStatus)) != 2608)
                throw new InvalidOperationException("Neural ABI size mismatch");
        }

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int RTN_Configure(ref Config config);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int RTN_SubmitJob(ref Job job, out IntPtr eventData, out ulong ticket);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr RTN_GetRenderEventAndData();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int RTN_CancelJob(ulong ticket);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int RTN_GetJobStatus(ulong ticket, ref JobStatus status);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int RTN_GetBackendStatus(ref BackendStatus status);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int RTN_GetPresetStatus(ref PresetStatus status);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] internal static extern int RTN_GetRuntimeStatus(ref RuntimeStatus status);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void RTN_RequestShutdown();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void RTN_SetDiagnosticsEnabled(int enabled);
    }
}
