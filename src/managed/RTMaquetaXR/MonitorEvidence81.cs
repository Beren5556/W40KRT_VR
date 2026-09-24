using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        internal struct MonitorFrame81
        {
            public ulong Serial;
            public int UnityFrame;
            public bool Image, CopyProbe, Stereo, Prepared, WorldSkipped, EyeCopied, Cleared, FlatCaptured, Recovery;
            public string Reason;
            public string Context;
            public double Realtime;
            public double UnityDeltaMs,CopyCpuMs,ClearCpuMs;
        }
        static readonly MonitorFrame81[] _monitorFrames81 = new MonitorFrame81[2048];
        static int _monitorCount81, _monitorNext81;
        static string _monitorOutputError81;
        static IntPtr _monitorGpuEvent81;
        // Session-only diagnostic comparison: keep world-render policy, omit just
        // the eye copy. The regular monitor switch remains the user's preference.
        static bool _monitorCopyProbe81;
        static double _monitorCopyCpu81;
        static void ResetMonitorEvidence81() { _monitorCount81=_monitorNext81=0; }
        static readonly OpenXR.MonitorGpuTiming81[] _monitorGpuRead81 = new OpenXR.MonitorGpuTiming81[512];
        static void MonitorGpuMark81(CommandBuffer commands, ulong serial, int mark)
        {
            if (!DiagnosticsRecording || serial == 0) return;
            try
            {
                if (_monitorGpuEvent81 == IntPtr.Zero) _monitorGpuEvent81 = OpenXR.RTX_GetMonitorEvent81();
                if (_monitorGpuEvent81 != IntPtr.Zero) commands.IssuePluginEventAndData(_monitorGpuEvent81, mark, new IntPtr(unchecked((long)serial)));
            }
            catch (Exception) { /* Missing diagnostics must not break presentation. */ }
        }
        internal static void CompleteMonitorOutput81(ulong serial, bool stereo, bool flatCaptured)
        {
            double clearCpu=0;
            MonitorHeadsetRecovery81 = !stereo && !_modeFlat && _stereoPreparedFrame == Time.frameCount;
            if (_active && (!_cfg.monitorImage || _monitorCopyProbe81))
            {
                long started = DiagnosticTimestamp();
                var previous = RenderTexture.active;
                try
                {
                    // Explicit camera target bypasses Graphics.Blit's Camera.main
                    // redirect; always clear the actual window, after flat capture.
                    using (var commands = new CommandBuffer { name = "RTVR monitor black" })
                    {
                        commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                        MonitorGpuMark81(commands, serial, 3);
                        commands.ClearRenderTarget(false, true, Color.black);
                        MonitorGpuMark81(commands, serial, 4);
                        Graphics.ExecuteCommandBuffer(commands);
                    }
                    _desktopClearFrame81 = Time.frameCount; _monitorOutputError81 = null;
                }
                catch (Exception error)
                {
                    if (_monitorOutputError81 != error.Message) _log.Error("[monitor81] Clear failed: " + error.Message);
                    _monitorOutputError81 = error.Message;
                }
                finally { RenderTexture.active = previous; RecordModStage("MonitorClear", started);if(started!=0)clearCpu=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency; }
            }
            if (!DiagnosticsRecording) return;
            _monitorFrames81[_monitorNext81] = new MonitorFrame81 {
                Serial = serial, UnityFrame = Time.frameCount, Realtime = Time.realtimeSinceStartup,
                UnityDeltaMs=Time.unscaledDeltaTime*1000.0,CopyCpuMs=_desktopCopyFrame81==Time.frameCount?_monitorCopyCpu81:0,ClearCpuMs=clearCpu,
                Image = _cfg.monitorImage, CopyProbe = _monitorCopyProbe81, Stereo = stereo, Prepared = _stereoPreparedFrame == Time.frameCount,
                WorldSkipped = _desktopSkippedFrame == Time.frameCount, EyeCopied = _desktopCopyFrame81 == Time.frameCount,
                Cleared = _desktopClearFrame81 == Time.frameCount, FlatCaptured = flatCaptured, Recovery = _desktopFallback,
                Reason = _monitorOutputError81 ?? (MonitorHeadsetRecovery81 ? "Incomplete eyes: desktop capture retained for headset recovery" : _desktopRecovery.Reason), Context = _modeName
            };
            _monitorNext81 = (_monitorNext81 + 1) % _monitorFrames81.Length;
            _monitorCount81 = Math.Min(_monitorCount81 + 1, _monitorFrames81.Length);
        }
        static object MonitorEvidenceSnapshot81()
        {
            var frames = new MonitorFrame81[_monitorCount81];
            for (int i = 0; i < frames.Length; ++i) frames[i] = _monitorFrames81[(_monitorNext81 - _monitorCount81 + i + _monitorFrames81.Length) % _monitorFrames81.Length];
            int count = 0;
            try { count = OpenXR.RTX_ReadMonitorTimings81(_monitorGpuRead81, _monitorGpuRead81.Length); } catch (Exception) { }
            var gpu = new OpenXR.MonitorGpuTiming81[count]; Array.Copy(_monitorGpuRead81, gpu, count);
            return new { Enabled = _cfg.monitorImage, CpuTiming = "MonitorCopy/MonitorClear are CPU wall times, not GPU durations",
                GpuTiming = "Delayed nonblocking D3D11 timestamps; Kind 1 copy, 2 clear. Missing/rejected samples are unavailable, never zero. Match Serial, not the readback frame.", Frames = frames, Gpu = gpu };
        }
    }
}
