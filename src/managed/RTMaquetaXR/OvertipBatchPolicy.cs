using System;
using System.Collections.Generic;

namespace RTMaquetaXR
{
    // Bounded ownership of optional UI batching components. Discovery reuses
    // the existing overtip walk; audit/release never scans the scene hierarchy.
    internal sealed class OvertipBatchPolicy<TKey, TValue> where TKey : class where TValue : class
    {
        internal const int Capacity = 96, CreatesPerFrame = 3, AuditsPerFrame = 8;
        readonly Func<TKey, TValue> create;
        readonly Func<TKey, TValue, bool> valid;
        readonly Action<TValue> release;
        readonly Dictionary<TKey, TValue> values = new Dictionary<TKey, TValue>();
        readonly List<TKey> keys = new List<TKey>(Capacity);
        int cursor, frame = -1, creates;
        internal bool Enabled { get; private set; }
        internal bool CapacityReached { get; private set; }
        internal int Count => keys.Count;
        internal long Created { get; private set; }
        internal long Released { get; private set; }
        internal long Audited { get; private set; }

        internal OvertipBatchPolicy(Func<TKey, TValue> create, Func<TKey, TValue, bool> valid, Action<TValue> release)
        { this.create = create; this.valid = valid; this.release = release; }

        internal void Begin(bool enabled, int nextFrame)
        {
            Enabled = enabled;
            if (!enabled) { Clear(); return; }
            if (frame == nextFrame) return;
            frame = nextFrame; creates = 0; CapacityReached = keys.Count >= Capacity;
            int count = Math.Min(AuditsPerFrame, keys.Count);
            for (int i = 0; i < count && keys.Count > 0; ++i)
            {
                if (cursor >= keys.Count) cursor = 0;
                TKey key = keys[cursor]; TValue value = values[key]; ++Audited;
                if (valid(key, value)) ++cursor;
                else { values.Remove(key); keys.RemoveAt(cursor); release(value); ++Released; }
            }
        }
        internal void Observe(TKey key)
        {
            if (!Enabled || key == null || values.ContainsKey(key) || creates >= CreatesPerFrame) return;
            if (keys.Count >= Capacity) { CapacityReached = true; return; }
            ++creates;
            TValue value = create(key);
            if (value == null) return;
            values.Add(key, value); keys.Add(key); ++Created; CapacityReached = keys.Count >= Capacity;
        }
        internal void Clear()
        {
            foreach (TKey key in keys) { release(values[key]); ++Released; }
            keys.Clear(); values.Clear(); cursor = 0; creates = 0; frame = -1;
            Enabled = CapacityReached = false;
        }
    }
}
