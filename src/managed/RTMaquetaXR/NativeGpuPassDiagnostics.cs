using System;
using System.Runtime.InteropServices;

namespace RTMaquetaXR
{
    // D3D11 timestamps do not depend on Unity's retail-player GPU profiler.
    // Only native render events touch queries; this class copies ready totals.
    internal static class NativeGpuPassDiagnostics
    {
        internal const int Capacity = 128, SampleInterval = 4;
        static int _sampleFrame = -1, _samplePass = -1, _nextPass;
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal struct Timing
        {
            public ulong Observations, Rejected;
            public double SumMs, PeakMs;
        }
        [DllImport("RTMaquetaBridge.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr RTX_GetGpuPassEvent();
        [DllImport("RTMaquetaBridge.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int RTX_ReadGpuPassTimings([Out] Timing[] timings, int capacity);
        internal static readonly Timing[] Ready = new Timing[Capacity];
        static IntPtr _event;
        internal static string Error { get; private set; }
        internal static IntPtr Event
        {
            get
            {
                if (Error != null) return IntPtr.Zero;
                if (_event == IntPtr.Zero && Error == null)
                    try { _event = RTX_GetGpuPassEvent(); if (_event == IntPtr.Zero) Error = "Missing timestamp event"; }
                    catch (Exception e) { Error = e.GetType().Name + ": " + e.Message; }
                return _event;
            }
        }
        internal static bool Read()
        {
            if (Event == IntPtr.Zero) return false;
            try { return RTX_ReadGpuPassTimings(Ready, Ready.Length) != 0; }
            catch (Exception e) { Error = e.GetType().Name + ": " + e.Message; return false; }
        }
        internal static bool Select(int pass, int frame, int registeredPasses)
        {
            if (pass < 0 || pass >= Capacity || registeredPasses <= 0) return false;
            // Freeze the choice for the frame even while discovering passes;
            // Microsoft specifies at most one timestamp-disjoint per frame.
            if (_sampleFrame != frame)
            {
                _sampleFrame = frame;
                _samplePass = frame % SampleInterval == 0 ? _nextPass % Math.Min(registeredPasses, Capacity) : -1;
                if (_samplePass >= 0) _nextPass = _samplePass + 1;
            }
            return pass == _samplePass;
        }
    }
}
