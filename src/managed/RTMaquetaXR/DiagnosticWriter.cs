using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace RTMaquetaXR
{
    // The producer owns capture: every node must be a detached value snapshot,
    // never a Unity object, live counter, lazy enumerable, or callback. The worker
    // only serializes that captured graph and writes files. No Unity API runs here.
    internal sealed class DiagnosticWriter
    {
        internal const int Capacity = 4;
        internal sealed class WorkItem
        {
            internal readonly object Snapshot;
            internal readonly string LatestPath, StereoPath, HistoryPath;
            internal readonly bool Stereo;
            internal WorkItem(object snapshot, string directory, string session, bool stereo)
            {
                Snapshot = snapshot; Stereo = stereo;
                LatestPath = Path.Combine(directory, "diagnostico-openxr.json");
                StereoPath = Path.Combine(directory, "diagnostico-ultimo-estereo.json");
                HistoryPath = Path.Combine(directory, "rendimiento-" + session + ".jsonl");
            }
        }
        readonly object gate = new object();
        static readonly SnapshotContractResolver contracts = new SnapshotContractResolver();
        readonly WorkItem[] pending = new WorkItem[Capacity];
        readonly Action<WorkItem> write;
        Thread worker;
        int count;
        bool enabled = false, stopRequested, writing;
        long enqueued, completed, dropped, failures, rejectedWhileDisabled;
        string lastError;
        double lastWriteMs, peakWriteMs;

        internal DiagnosticWriter() : this(null) { }
        // Injectable sink is for offline contention/failure tests. Production
        // always uses WriteSnapshot; no callback into Main is supplied.
        internal DiagnosticWriter(Action<WorkItem> sink) { write = sink ?? WriteSnapshot; }

        internal bool TryEnqueue(object snapshot, string directory, string session, bool stereo)
        {
            WorkItem item;
            try
            {
                if (snapshot == null || string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(session))
                    throw new ArgumentException("Diagnostic snapshot, directory and session are required");
                item = new WorkItem(snapshot, directory, session, stereo);
            }
            catch (Exception error) { RecordFailure(error); return false; }
            lock (gate)
            {
                if (!enabled) { ++rejectedWhileDisabled; return false; }
                // Re-enabling VR while an earlier stop is draining reuses the
                // same thread. At most one writer can touch these files.
                stopRequested = false;
                if (count == Capacity)
                {
                    int discard = 0;
                    if (!stereo && pending[0].Stereo)
                    {
                        bool anotherStereo = false;
                        for (int i = 1; i < count; ++i) anotherStereo |= pending[i].Stereo;
                        // Pausing must not evict the only unflushed gameplay
                        // record. Newest stereo replaces older stereo normally.
                        if (!anotherStereo) discard = 1;
                    }
                    for (int i = discard; i < count - 1; ++i) pending[i] = pending[i + 1];
                    pending[--count] = null; ++dropped;
                }
                pending[count++] = item; ++enqueued;
                if (worker == null)
                {
                    try
                    {
                        worker = new Thread(Run) { IsBackground = true, Name = "RTMaquetaXR diagnostics" };
                        worker.Start();
                    }
                    catch (Exception error)
                    {
                        worker = null; ++failures; lastError = error.GetType().Name + ": " + error.Message;
                        dropped += count; Array.Clear(pending, 0, count); count = 0; return false;
                    }
                }
                Monitor.Pulse(gate);
                return true;
            }
        }

        internal void RequestStop()
        {
            // Never join from a Unity lifecycle callback. Already captured
            // records drain in order; IsBackground cannot keep the game alive.
            lock (gate) { stopRequested = true; Monitor.Pulse(gate); }
        }

        internal void SetEnabled(bool value)
        {
            lock (gate)
            {
                enabled = value;
                if (!value)
                {
                    // Pausing discards queued reports. A file already being
                    // written may finish; never block Unity waiting for disk IO.
                    dropped += count; Array.Clear(pending, 0, count); count = 0;
                    stopRequested = true;
                }
                Monitor.Pulse(gate);
            }
        }

        internal object Snapshot()
        {
            lock (gate) return new {
                Enabled = enabled, Queued = count, QueueCapacity = Capacity, Writing = writing, Running = worker != null,
                StopRequested = stopRequested, Enqueued = enqueued, Completed = completed,
                Dropped = dropped, RejectedWhileDisabled = rejectedWhileDisabled, Failures = failures, LastError = lastError,
                LastWriteMs = Math.Round(lastWriteMs, 3), PeakWriteMs = Math.Round(peakWriteMs, 3),
                SerializationAndFileIoOnWorker = true, SnapshotCaptureOnMainThread = true,
                HistoryMayOmitOverloadedWindows = true, FinalFlushAtProcessExitGuaranteed = false
            };
        }

        void Run()
        {
            // Lower priority is a preference only; a runtime that rejects it
            // must still be able to save diagnostics.
            try { Thread.CurrentThread.Priority = ThreadPriority.BelowNormal; } catch { }
            while (true)
            {
                WorkItem item;
                lock (gate)
                {
                    while (count == 0 && !stopRequested) Monitor.Wait(gate);
                    if (count == 0) { worker = null; return; }
                    item = pending[0];
                    for (int i = 1; i < count; ++i) pending[i - 1] = pending[i];
                    pending[--count] = null; writing = true;
                }
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                try { write(item); lock (gate) ++completed; }
                catch (Exception error) { RecordFailure(error); }
                finally
                {
                    double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    lock (gate) { lastWriteMs = ms; peakWriteMs = Math.Max(peakWriteMs, ms); writing = false; }
                }
            }
        }

        void RecordFailure(Exception error)
        { lock (gate) { ++failures; lastError = error.GetType().Name + ": " + error.Message; } }

        internal string FailureMessage()
        { lock (gate) return failures > 0 ? lastError : null; }

        static void WriteSnapshot(WorkItem item)
        {
            // Explicit serializer ignores game-wide JsonConvert.DefaultSettings.
            // One compact serialization is reused for latest, stereo and history.
            // Detailed counters can cross the LOH threshold when pretty-printed.
            var serializer = JsonSerializer.Create(new JsonSerializerSettings {
                Formatting = Formatting.None, Culture = CultureInfo.InvariantCulture,
                TypeNameHandling = TypeNameHandling.None, ReferenceLoopHandling = ReferenceLoopHandling.Error,
                ContractResolver = contracts
            });
            string json;
            using (var text = new StringWriter(CultureInfo.InvariantCulture))
            using (var writer = new JsonTextWriter(text))
            { serializer.Serialize(writer, item.Snapshot); writer.Flush(); json = text.ToString(); }
            File.WriteAllText(item.LatestPath, json);
            if (item.Stereo) File.WriteAllText(item.StereoPath, json);
            File.AppendAllText(item.HistoryPath, json + Environment.NewLine);
        }

        // Cache the allowed metadata once per captured type. A mistakenly added
        // Camera, Measurement, lazy query or stateful getter is rejected BEFORE
        // its properties can run on the worker. Collection ownership is still a
        // producer contract: snapshot methods must allocate/copy their contents.
        sealed class SnapshotContractResolver : DefaultContractResolver
        {
            protected override JsonContract CreateContract(Type type)
            {
                if (!Allowed(type)) throw new JsonSerializationException("Not a detached diagnostic value: " + type.FullName);
                return base.CreateContract(type);
            }
            static bool Allowed(Type type)
            {
                if (type == typeof(object) || type == typeof(string) || type.IsPrimitive || type.IsEnum ||
                    type == typeof(decimal) || type == typeof(DateTime) || type == typeof(Guid)) return true;
                var nullable = Nullable.GetUnderlyingType(type);
                if (nullable != null) return Allowed(nullable);
                if (type.IsArray) return type.GetArrayRank() == 1 && Allowed(type.GetElementType());
                if (type.IsGenericType)
                {
                    var definition = type.GetGenericTypeDefinition(); var args = type.GetGenericArguments();
                    if (definition == typeof(List<>)) return Allowed(args[0]);
                    if (definition == typeof(Dictionary<,>)) return args[0] == typeof(string) && Allowed(args[1]);
                }
                if (type.Assembly != typeof(DiagnosticWriter).Assembly) return false;
                if (type.IsValueType)
                {
                    // CPU structs contain values/strings, plus the explicitly
                    // detached stage snapshot whose constructor copies its map.
                    foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        if (field.FieldType != typeof(string) &&
                            ((!field.FieldType.IsValueType && field.FieldType.FullName != "RTMaquetaXR.ModStageFrames79+Evidence") || !Allowed(field.FieldType))) return false;
                    return type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Length == 0;
                }
                bool stageSnapshot = type.FullName == "RTMaquetaXR.ModStageFrames79+Evidence";
                if (!type.IsSealed || (!stageSnapshot && (!type.Name.StartsWith("<>f__AnonymousType", StringComparison.Ordinal) ||
                    !type.IsDefined(typeof(CompilerGeneratedAttribute), false)))) return false;
                if (stageSnapshot && type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Length != 0) return false;
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (!field.IsInitOnly || !Allowed(field.FieldType)) return false;
                return true;
            }
        }
    }
}
