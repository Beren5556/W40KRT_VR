using System;

namespace RTMaquetaXR
{
    // Collection state follows the persistent master; it never changes image/input
    // preferences. Repeating the current value must not reset a live window.
    internal sealed class DiagnosticCollectionPolicy
    {
        internal bool Recording { get; private set; } = false;
        internal int Revision { get; private set; }
        internal bool Set(bool value)
        {
            if (Recording == value) return false;
            Recording = value; ++Revision; return true;
        }
    }
    // Fixed capacity: monitoring a bad scene never grows the retained frame set.
    internal sealed class BoundedPerformanceSamples<T>
    {
        readonly T[] values;
        readonly double[] scores;
        internal int Count { get; private set; }
        internal BoundedPerformanceSamples(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            values = new T[capacity]; scores = new double[capacity];
        }
        internal bool WouldKeep(double score) => score > 0 && !double.IsNaN(score) && !double.IsInfinity(score) &&
            (Count < values.Length || score > scores[Count - 1]);
        internal bool Add(double score, T value)
        {
            if (!WouldKeep(score)) return false;
            int index = Count < values.Length ? Count++ : Count - 1;
            while (index > 0 && score > scores[index - 1])
            { scores[index] = scores[index - 1]; values[index] = values[index - 1]; --index; }
            scores[index] = score; values[index] = value; return true;
        }
        internal T[] Snapshot()
        { var result = new T[Count]; Array.Copy(values, result, Count); return result; }
        internal void Clear()
        { Array.Clear(values, 0, values.Length); Array.Clear(scores, 0, scores.Length); Count = 0; }
        internal static double HitchThreshold(double displayPeriodMs) =>
            Math.Max(20, displayPeriodMs > 0 && !double.IsNaN(displayPeriodMs) && !double.IsInfinity(displayPeriodMs)
                ? displayPeriodMs * 1.5 : 20);
    }

    internal enum GpuMarkerProbeState { Unsupported, Probing, Available, UnavailableNoSamples }

    // SystemInfo advertised GPU recorder support in 0.1.19, but 69,541 steady
    // samples produced no per-pass observations. Probe again on attachment;
    // do not keep issuing/read-polling empty markers indefinitely.
    internal sealed class GpuMarkerProbe
    {
        internal const int MaximumProbeFrames = 240;
        internal GpuMarkerProbeState State { get; private set; }
        internal int Frames { get; private set; }
        internal bool Active => State == GpuMarkerProbeState.Probing || State == GpuMarkerProbeState.Available;
        internal void Start(bool supported)
        { Frames = 0; State = supported ? GpuMarkerProbeState.Probing : GpuMarkerProbeState.Unsupported; }
        internal bool Observe(bool haveGpuSample)
        {
            if (!Active) return false;
            if (State == GpuMarkerProbeState.Available) return true;
            ++Frames;
            if (haveGpuSample) State = GpuMarkerProbeState.Available;
            else if (Frames >= MaximumProbeFrames) State = GpuMarkerProbeState.UnavailableNoSamples;
            return Active;
        }
    }
}
