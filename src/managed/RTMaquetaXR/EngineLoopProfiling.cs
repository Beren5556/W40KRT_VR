using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.LowLevel;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly EngineLoopSamples _engineLoopSamples = new EngineLoopSamples(EngineLoopTree.PhaseNames.Length);
        static EngineLoopProbe[] _engineLoopProbes;
        static int[] _engineLoopPhaseCounts;
        static bool _engineLoopInstalled, _engineLoopFailed;
        static int _engineLoopPhaseCount;
        static string _engineLoopError;
        static bool EngineLoopProfilingActive => _engineLoopInstalled && !_engineLoopFailed && GameCpuProfilingActive && _attached && !_modeFlat;

        static void InstallEngineLoopProfiling()
        {
            if (_engineLoopInstalled) return;
            try
            {
                var current = PlayerLoop.GetCurrentPlayerLoop();
                var clean = EngineLoopTree.Remove(current, out int oldBoundaries);
                _engineLoopPhaseCounts = EngineLoopTree.Count(clean);
                _engineLoopProbes = new EngineLoopProbe[EngineLoopTree.PhaseNames.Length];
                for (int i = 0; i < _engineLoopProbes.Length; ++i)
                    _engineLoopProbes[i] = new EngineLoopProbe(i, () => EngineLoopProfilingActive,
                        () => Time.frameCount, () => _diagnosticCollection.Revision, Stopwatch.GetTimestamp, RecordEngineLoopPhase);
                var instrumented = EngineLoopTree.Insert(clean, _engineLoopProbes, out int count);
                if (count == 0) throw new InvalidOperationException("No recognized unambiguous PlayerLoop phase");
                _engineLoopSamples.ResetAll();
                _engineLoopFailed = false; _engineLoopError = null; _engineLoopPhaseCount = count;
                PlayerLoop.SetPlayerLoop(instrumented);
                _engineLoopInstalled = true;
                _log.Log("[diagnostic/playerloop] " + count + " original engine phases bracketed; native delegates/order retained; clocks disabled with diagnostics/detail OFF");
            }
            catch (Exception error)
            {
                _engineLoopInstalled = false; _engineLoopFailed = true; _engineLoopError = error.Message;
                _log.Error("[diagnostic/playerloop] Unavailable; original engine loop retained: " + error.Message);
            }
        }
        internal static void StopEngineLoopProfiling()
        {
            // Disable first: even if another mod changed/wrapped a boundary,
            // a preserved callback is inert and never touches clocks or samples.
            _engineLoopInstalled = false;
            if (_engineLoopProbes != null) foreach (var probe in _engineLoopProbes) probe.Cancel();
            try
            {
                if (_appQuitting) return;
                var current = PlayerLoop.GetCurrentPlayerLoop();
                var clean = EngineLoopTree.Remove(current, out int removed);
                if (removed != 0) PlayerLoop.SetPlayerLoop(clean);
            }
            catch (Exception error) { _engineLoopError = "Inactive observer could not be removed: " + error.Message; }
        }
        static void RecordEngineLoopPhase(int phase, int frame, long started, long ended)
        {
            if (!EngineLoopProfilingActive) return;
            _engineLoopSamples.Add(frame, phase, (ended - started) * 1000.0 / Stopwatch.Frequency, _consecutiveStereoFrames > 16);
        }
        static EngineLoopFrameEvidence EngineLoopFrameSnapshot(int frame) => new EngineLoopFrameEvidence(_engineLoopSamples, frame);
        static string EngineLoopLabel(int phase) => EngineLoopTree.PhaseNames[phase].Replace("UnityEngine.PlayerLoop.", "").Replace('+', '.');
        static object EngineLoopEvidenceSnapshot()
        {
            var phases = new Dictionary<string, object>();
            for (int i = 0; i < EngineLoopTree.PhaseNames.Length; ++i)
            {
                int count = _engineLoopSamples.Count[i], occurrences = _engineLoopPhaseCounts == null ? 0 : _engineLoopPhaseCounts[i];
                var probe = _engineLoopProbes == null ? null : _engineLoopProbes[i];
                phases[EngineLoopLabel(i)] = new {
                    PresentAtInstall = occurrences, Observed = count > 0, Available = _engineLoopInstalled && occurrences == 1 && probe != null && !probe.Failed,
                    Failure = probe?.Error,
                    MeanMsPerInvocation = count > 0 ? (double?)Math.Round(_engineLoopSamples.Sum[i] / count, 4) : null,
                    PeakMsPerInvocation = count > 0 ? (double?)Math.Round(_engineLoopSamples.Peak[i], 4) : null,
                    Invocations = count };
            }
            return new {
                Installed = _engineLoopInstalled, Enabled = EngineLoopProfilingActive, Failed = _engineLoopFailed, Error = _engineLoopError,
                BracketedPhases = _engineLoopPhaseCount, MaximumPhases = EngineLoopTree.PhaseNames.Length, StorageFrames = EngineLoopSamples.FrameSlots,
                Phases = phases, Source = "Current PlayerLoop begin/end siblings around untouched native or managed systems",
                CpuWallTimeIncludesEngineAndDriverWaits = true, IndependentGpuTimers = false,
                FinishFrameRenderingIncludesRenderingCallbacksAndWaits = true,
                PlayerUpdateCanvasesMayIncludeManagedCanvasRegistry = true,
                ScopesOverlapExistingGameTickRenderAndCanvasTimingsDoNotSum = true,
                EndCallbackOverheadNotSeparatelyMeasured = true, CurrentFrameSnapshotMayBeIncomplete = true,
                AbsentOrAmbiguousPhasesAreUnavailableNotZero = true, OffReadsClockOrFrameCounter = false };
        }
        static void ResetEngineLoopWindow() => _engineLoopSamples.ResetWindow();
    }
}
