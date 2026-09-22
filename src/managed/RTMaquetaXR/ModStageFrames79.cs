using System.Collections.Generic;

namespace RTMaquetaXR
{
    // Four reusable frame slots. Only a retained hitch receives a detached
    // copy; recording OFF never calls Add. Nested stage times must not be summed.
    internal sealed class ModStageFrames79
    {
        internal sealed class Evidence
        {
            public readonly int Frame;
            public readonly bool Available;
            public readonly Dictionary<string, double> CpuWallMs;
            internal Evidence(int frame, bool available, Dictionary<string, double> values)
            { Frame = frame; Available = available; CpuWallMs = values == null ? new Dictionary<string, double>() : new Dictionary<string, double>(values); }
        }
        readonly int[] frames = { -1, -1, -1, -1 };
        readonly Dictionary<string, double>[] values = {
            new Dictionary<string, double>(), new Dictionary<string, double>(),
            new Dictionary<string, double>(), new Dictionary<string, double>() };
        internal void Add(int frame, string name, double milliseconds)
        {
            if (frame < 0 || string.IsNullOrEmpty(name) || milliseconds < 0 || double.IsNaN(milliseconds) || double.IsInfinity(milliseconds)) return;
            int slot = frame % frames.Length;
            if (frames[slot] != frame) { values[slot].Clear(); frames[slot] = frame; }
            values[slot].TryGetValue(name, out double previous);
            values[slot][name] = previous + milliseconds;
        }
        internal Evidence Snapshot(int frame)
        {
            bool found = frame >= 0 && frames[frame % frames.Length] == frame;
            return new Evidence(frame, found, found ? values[frame % frames.Length] : null);
        }
        internal void Clear()
        {
            for (int i = 0; i < frames.Length; ++i) { frames[i] = -1; values[i].Clear(); }
        }
    }
}
