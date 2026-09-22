using System;

namespace RTMaquetaXR
{
    // Only the observer is executed here. The engine's native/managed callback
    // stays in its original PlayerLoopSystem and is never invoked by this type.
    internal sealed class EngineLoopProbe
    {
        readonly int phase;
        readonly Func<bool> enabled;
        readonly Func<int> frame, revision;
        readonly Func<long> clock;
        readonly Action<int, int, long, long> record;
        bool pending;
        int startedFrame, startedRevision;
        long started;
        internal bool Failed { get; private set; }
        internal string Error { get; private set; }
        internal EngineLoopProbe(int phase, Func<bool> enabled, Func<int> frame, Func<int> revision,
            Func<long> clock, Action<int, int, long, long> record)
        {
            this.phase = phase; this.enabled = enabled; this.frame = frame; this.revision = revision;
            this.clock = clock; this.record = record;
        }
        internal void Begin()
        {
            pending = false;
            try
            {
                if (Failed || !enabled()) return;
                startedFrame = frame(); startedRevision = revision(); started = clock(); pending = true;
            }
            catch (Exception error) { Fail(error); }
        }
        internal void End()
        {
            bool hadBegin = pending; pending = false;
            try
            {
                if (!hadBegin || Failed || !enabled() || frame() != startedFrame || revision() != startedRevision) return;
                long ended = clock();
                if (ended >= started) record(phase, startedFrame, started, ended);
            }
            catch (Exception error) { Fail(error); }
        }
        internal void Cancel() { pending = false; }
        void Fail(Exception error) { pending = false; Failed = true; Error = error.GetType().Name + ": " + error.Message; }
    }

    internal sealed class EngineLoopSamples
    {
        internal const int FrameSlots = 4;
        readonly int phases;
        readonly int[] frames = { -1, -1, -1, -1 };
        readonly double[,] times;
        readonly int[,] calls;
        internal readonly double[] Sum, Peak;
        internal readonly int[] Count;
        internal EngineLoopSamples(int phases)
        {
            if (phases < 1 || phases > 32) throw new ArgumentOutOfRangeException(nameof(phases));
            this.phases = phases; times = new double[FrameSlots, phases]; calls = new int[FrameSlots, phases];
            Sum = new double[phases]; Peak = new double[phases]; Count = new int[phases];
        }
        internal bool Add(int frame, int phase, double milliseconds, bool aggregate)
        {
            if (frame < 0 || phase < 0 || phase >= phases || double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) || milliseconds < 0 || milliseconds > 60000) return false;
            int slot = frame & (FrameSlots - 1);
            if (frames[slot] != frame)
            {
                frames[slot] = frame;
                for (int i = 0; i < phases; ++i) { times[slot, i] = 0; calls[slot, i] = 0; }
            }
            times[slot, phase] += milliseconds; ++calls[slot, phase];
            if (aggregate) { Sum[phase] += milliseconds; Peak[phase] = Math.Max(Peak[phase], milliseconds); ++Count[phase]; }
            return true;
        }
        internal bool HasFrame(int frame) => frame >= 0 && frames[frame & (FrameSlots - 1)] == frame;
        internal double Time(int frame, int phase) => HasFrame(frame) ? times[frame & (FrameSlots - 1), phase] : 0;
        internal int Calls(int frame, int phase) => HasFrame(frame) ? calls[frame & (FrameSlots - 1), phase] : 0;
        internal void ResetWindow() { Array.Clear(Sum, 0, phases); Array.Clear(Peak, 0, phases); Array.Clear(Count, 0, phases); }
        internal void ResetAll()
        {
            ResetWindow(); for (int i = 0; i < frames.Length; ++i) frames[i] = -1;
            Array.Clear(times, 0, times.Length); Array.Clear(calls, 0, calls.Length);
        }
    }
}
