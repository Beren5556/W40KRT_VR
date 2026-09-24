using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace RTMaquetaXR
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct XrPose
    {
        public float qx, qy, qz, qw, x, y, z;
        ViewPose UnityPose => ViewPose.FromOpenXR(x,y,z,qx,qy,qz,qw);
        public Vector3 Position { get { var p=UnityPose.position; return new Vector3(p.x,p.y,p.z); } }
        public Quaternion Rotation { get { var q=UnityPose.rotation; return new Quaternion(q.x,q.y,q.z,q.w); } }
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct XrFov { public float left, right, up, down; }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct XrView { public XrPose pose; public XrFov fov; }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct XrFrame
    {
        public int width, height, state, shouldRender, valid;
        public ulong serial;
        public XrPose head;
        public XrView left, right;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct XrCrop
    {
        public float left, top, right, bottom;
        public static XrCrop Full => new XrCrop { right = 1, bottom = 1 };
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct XrStats
    {
        public ulong begun, submittedPairs, flatFrames, emptyFrames, failedFrames;
        public ulong lastSerial, lastLeftSerial, lastRightSerial;
        public int state, queued;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct XrBeginTiming
    {
        public ulong serial;
        public double queueWaitMs, runtimeWaitMs, beginLocateMs, predictedPeriodMs;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct XrRenderTiming
    {
        public ulong serial;
        public int images, flushes, sourceViewsCreated, completed;
        public double acquireWaitMs, drawFlushMs, releaseMs, endFrameMs, totalMs;
    }
    [Flags]
    internal enum XrTouchControl : uint
    {
        Aim = 1, Grip = 2, Trigger = 4, Squeeze = 8, Stick = 16,
        Primary = 32, Secondary = 64, Menu = 128, StickClick = 256, Thumbrest = 512
    }
    [Flags]
    internal enum XrTouchButton : uint { Primary = 1, Secondary = 2, Menu = 4, StickClick = 8, Thumbrest = 16 }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct XrTouchHand
    {
        public XrPose aim, grip;
        public uint aimFlags, gripFlags, activeControls, buttons;
        public float trigger, squeeze, stickX, stickY;
        public bool Active => activeControls != 0;
        public bool AimValid => (activeControls & (uint)XrTouchControl.Aim) != 0 && (aimFlags & 3u) == 3u;
        public bool GripValid => (activeControls & (uint)XrTouchControl.Grip) != 0 && (gripFlags & 3u) == 3u;
        public bool AimTracked => AimValid && (aimFlags & 12u) == 12u;
        public bool GripTracked => GripValid && (gripFlags & 12u) == 12u;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct XrTouchFrame
    {
        public ulong serial;
        public long predictedDisplayTime;
        public int ready, focused, state, lastResult;
        public XrTouchHand left, right;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct XrFlatPanel
    {
        public ulong serial;
        public XrPose pose;
        public float width, height;
        public int valid, flipVertical;
    }
    internal static class OpenXR
    {
        const string Library = "RTMaquetaBridge.dll";
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal struct MonitorGpuTiming81 { public ulong Serial; public int Kind, Valid; public double Milliseconds; }
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr RTX_GetMonitorEvent81();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int RTX_ReadMonitorTimings81([Out] MonitorGpuTiming81[] samples, int capacity);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int RTX_SelectRuntime(int runtime);
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryEx(string path, IntPtr reserved, uint flags);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        static extern int RTX_Init(IntPtr texture, float scale, string logPath);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int RTX_Begin(out XrFrame frame);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern int RTX_Queue(ulong serial, IntPtr left, IntPtr right, IntPtr flat, XrCrop lc, XrCrop rc,
                                   int flipEyes, int flipFlat, float distance, int recenter);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void RTX_SetFlatPanelWidth(float widthRatio);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void RTX_SetFlatPanelOffset(float x, float y);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void RTX_SetFlatPanelAspect(float aspect);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int RTX_GetHudStatus();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int RTX_SetHudFrame(ulong serial,
            IntPtr black, IntPtr white, float width, float height, float distance, float x, float y, int linear);
        internal static bool HudCompositorAvailable => initialized && RTX_GetHudStatus() >= 0;
        [DllImport(Library,CallingConvention=CallingConvention.Cdecl)] static extern int RTX_GetUiLimits81(out int layers,out int width,out int height);
        internal static bool GetUiLimits81(out int layers,out int width,out int height)=>RTX_GetUiLimits81(out layers,out width,out height)==1;
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int RTX_GetSpatialStatus();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int RTX_SetSpatialFrame(ulong serial,
            IntPtr black, IntPtr white, XrPose pose, float width, float height, int linear);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int RTX_SetSpatialInformation(ulong serial, XrPose pose, float size, int visible);
        internal static bool SpatialCompositorAvailable => initialized && RTX_GetSpatialStatus() >= 0;
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern ulong RTX_GetTrackingOriginRevision();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr RTX_GetRenderEvent();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int RTX_Shutdown();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void RTX_GetStats(out XrStats stats);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void RTX_GetBeginTiming(out XrBeginTiming timing);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int RTX_GetRenderTiming(out XrRenderTiming timing);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void RTX_SetDiagnosticsEnabled(int enabled);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void RTX_SetLogEnabled(int enabled);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int RTX_GetTouch(out XrTouchFrame touch);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int RTX_GetFlatPanel(out XrFlatPanel panel);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void RTX_GetSize(out int width, out int height);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        static extern void RTX_GetResolutionInfo(out int recommendedWidth, out int recommendedHeight, out float scale);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int RTX_PreviewSize(float scale, out int width, out int height);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int RTX_TryResize(float scale);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        static extern int RTX_GetError(StringBuilder text, int capacity);
        static bool loaded, initialized, pendingShutdown;
        static bool diagnosticsRecording = false;
        static bool allDiagnosticsEnabled = false;
        static readonly FlatPanelSnapshotPolicy flatPanelPolicy = new FlatPanelSnapshotPolicy();
        static XrFlatPanel cachedFlatPanel;
        static IntPtr renderEvent;
        static readonly NativeTextureCache<RenderTexture> nativeTextures = new NativeTextureCache<RenderTexture>();
        static readonly Func<RenderTexture, IntPtr> fetchNativeTexture = texture => texture.GetNativeTexturePtr();
        public static int TexturePointerFetches => nativeTextures.Fetches;
        public static int Width, Height;
        public static int RecommendedWidth, RecommendedHeight;
        public static float AppliedRenderScale;
        public static bool FlipEyes = true, FlipFlat = false;
        public static string Status = "Waiting for the OpenXR headset";
        public static bool Initialize(string folder, float scale)
        {
            ResetFlatPanelCache();
            if (pendingShutdown && !Shutdown()) return false;
            if (initialized) return true;
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Direct3D11)
                throw new InvalidOperationException("El prototipo necesita Direct3D 11; dispositivo actual: " + SystemInfo.graphicsDeviceType);
            try { OpenXrRuntime.PrepareProcess(); }
            catch (Exception error) { Status = error.Message; throw; }
            if (!loaded)
            {
                if (Marshal.SizeOf(typeof(XrFrame)) != 144 || Marshal.SizeOf(typeof(XrStats)) != 72 || Marshal.SizeOf(typeof(XrBeginTiming)) != 40 || Marshal.SizeOf(typeof(XrRenderTiming)) != 64 ||
                    Marshal.SizeOf(typeof(XrTouchHand)) != 88 || Marshal.SizeOf(typeof(XrTouchFrame)) != 208 || Marshal.SizeOf(typeof(XrFlatPanel)) != 52)
                    throw new InvalidOperationException("OpenXR ABI size mismatch");
                string path = Path.Combine(folder, Library);
                if (LoadLibraryEx(path, IntPtr.Zero, 0x100 | 0x1000) == IntPtr.Zero)
                    throw new InvalidOperationException("No se pudo cargar " + path + "; Win32=" + Marshal.GetLastWin32Error());
                loaded = true;
                RTX_SetLogEnabled(allDiagnosticsEnabled ? 1 : 0);
                ApplyDiagnosticsRecording();
                renderEvent = RTX_GetRenderEvent();
            }
            if (RTX_SelectRuntime(OpenXrRuntime.Applied) != 1)
                throw new InvalidOperationException("OpenXR runtime change requires restarting the game");
            var probe = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                probe.Create();
                int result = RTX_Init(probe.GetNativeTexturePtr(), scale, Path.Combine(folder, "openxr-native.log"));
                if (result != 1) { Status = result == 0 ? "Headset unavailable; retrying automatically" : Error(); return false; }
                RTX_GetSize(out Width, out Height);
                RTX_GetResolutionInfo(out RecommendedWidth, out RecommendedHeight, out AppliedRenderScale);
                initialized = true; Status = ModLocalization.Format("OpenXR / {0}: session created", OpenXrRuntime.Name);
                ApplyDiagnosticsRecording();
                return true;
            }
            finally { probe.Release(); UnityEngine.Object.Destroy(probe); }
        }
        public static string Error()
        {
            var text = new StringBuilder(2048); RTX_GetError(text, text.Capacity); return text.ToString();
        }
        public static bool Shutdown()
        {
            ResetFlatPanelCache();
            if (!loaded) return true;
            pendingShutdown = RTX_Shutdown() == 0;
            if (!pendingShutdown) { initialized = false; nativeTextures.Clear(); Status = "VR stopped"; }
            return !pendingShutdown;
        }
        public static void PumpShutdown() { if (pendingShutdown) Shutdown(); }
        internal static void SetDiagnosticsRecording(bool enabled)
        {
            diagnosticsRecording = enabled;
            if (loaded) ApplyDiagnosticsRecording();
        }
        // Keep the P/Invoke out of the pre-load path, including Mono JIT inlining.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ApplyDiagnosticsRecording() => RTX_SetDiagnosticsEnabled(diagnosticsRecording ? 1 : 0);
        internal static void SetAllDiagnosticsEnabled(bool enabled)
        {
            allDiagnosticsEnabled = enabled;
            if (loaded) ApplyLogSetting();
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ApplyLogSetting() => RTX_SetLogEnabled(allDiagnosticsEnabled ? 1 : 0);
        internal static bool IsStopping => pendingShutdown;
        internal static bool GetTouch(out XrTouchFrame touch)
        {
            touch = default(XrTouchFrame);
            return initialized && !pendingShutdown && RTX_GetTouch(out touch) == 1;
        }
        internal static bool GetFlatPanel(out XrFlatPanel panel)
        {
            panel = default(XrFlatPanel);
            if (!initialized || pendingShutdown) { ResetFlatPanelCache(); return false; }
            int result = RTX_GetFlatPanel(out panel);
            if (!flatPanelPolicy.Accept(result, panel.valid != 0, panel.serial,
                System.Diagnostics.Stopwatch.GetTimestamp(), System.Diagnostics.Stopwatch.Frequency, out bool useCached))
            { cachedFlatPanel = panel = default(XrFlatPanel); return false; }
            if (useCached) panel = cachedFlatPanel;
            else cachedFlatPanel = panel;
            return true;
        }
        static void ResetFlatPanelCache() { flatPanelPolicy.Reset(); cachedFlatPanel = default(XrFlatPanel); }
        static void InvalidateFlatPanelCache(ulong serial) { flatPanelPolicy.Invalidate(serial); cachedFlatPanel = default(XrFlatPanel); }
        internal static int TryResize(float scale)
        {
            if (!initialized || pendingShutdown) return -1;
            int result = RTX_TryResize(scale);
            if (result == 1)
            {
                RTX_GetSize(out Width, out Height);
                RTX_GetResolutionInfo(out RecommendedWidth, out RecommendedHeight, out AppliedRenderScale);
                Status = "Output applied: " + Width + " × " + Height;
            }
            return result;
        }
        public static void Submit(XrFrame frame, RenderTexture left, RenderTexture right, RenderTexture flat,
                                  XrCrop lc, XrCrop rc, float distance, bool recenter)
        {
            if (!initialized || pendingShutdown) return;
            if (recenter || flat == null) InvalidateFlatPanelCache(frame.serial);
            if (flat != null)
            {
                RTX_SetFlatPanelWidth(Main.FlatPanelWidthRatio);
                RTX_SetFlatPanelOffset(Main.FlatPanelOffsetX, Main.FlatPanelOffsetY);
                RTX_SetFlatPanelAspect(Main.FlatPanelAspect);
            }
            else if (Main.GetHudCapture(out RenderTexture hudBlack, out RenderTexture hudWhite, out HudPanelGeometry hud))
            {
                if (RTX_SetHudFrame(frame.serial, Native(hudBlack), Native(hudWhite), hud.Width, hud.Height,
                    hud.Distance, hud.CentreX, hud.CentreY, QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0) != 1)
                    Main.FailHudCapture("El compositor rechazó la captura del HUD");
            }
            if (flat == null && left != null && right != null && Main.GetSpatialUi(out RenderTexture spatialBlack, out RenderTexture spatialWhite,
                out XrPose spatialPose, out float spatialWidth, out float spatialHeight))
            {
                bool showInformation = Main.GetSpatialInformation(out XrPose informationPose, out float informationSize);
                if (RTX_SetSpatialFrame(frame.serial, Native(spatialBlack), Native(spatialWhite), spatialPose,
                    spatialWidth, spatialHeight, QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0) != 1)
                    Main.FailSpatialUi("Spatial compositor rejected the capture");
                else if (RTX_SetSpatialInformation(frame.serial, informationPose, informationSize, showInformation ? 1 : 0) != 1)
                    Main.FailSpatialUi("Spatial compositor rejected the information pose");
            }
            int result = RTX_Queue(frame.serial, Native(left), Native(right), Native(flat), lc, rc,
                                   FlipEyes ? 1 : 0, FlipFlat ? 1 : 0, distance, recenter ? 1 : 0);
            if (result == 1) GL.IssuePluginEvent(renderEvent, 1);
            else Main.Log.Log("[OpenXR] Frame rejected before GPU queue: " + frame.serial);
        }
        internal static void PrepareTexture(RenderTexture texture) { Native(texture); }
        internal static void ForgetTexture(RenderTexture texture) { nativeTextures.Forget(texture); }
        static IntPtr Native(RenderTexture texture)
        {
            if (texture != null && texture.IsCreated()) return nativeTextures.Get(texture, fetchNativeTexture);
            nativeTextures.Forget(texture);
            return IntPtr.Zero;
        }
    }
}
