using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        // A process-start choice. Editing saved settings or restarting just VR
        // cannot change it; a disabled launch never installs these patches.
        static EngineCadenceSchedule _engineCadence;
        static bool _engineCadenceCaptured, _engineCadenceAttempted, _engineCadenceInstalled;
        static string _engineCadenceFault;
        static int _engineCadenceOwnerThread;
        static int _engineCadenceSlotSerial;
        static long _engineCadenceInvalidations;
        static readonly Dictionary<MethodBase, EngineCadenceTarget> _engineCadenceTargets = new Dictionary<MethodBase, EngineCadenceTarget>();
        sealed class EngineCadenceTarget
        {
            internal string Name;
            internal readonly ConditionalWeakTable<object, EngineCadenceSlot> Slots = new ConditionalWeakTable<object, EngineCadenceSlot>();
            internal long Eligible, Executed, Skipped, Original, Timed;
            internal double ObservedMs;
        }
        struct EngineCadenceCall
        {
            internal EngineCadenceTarget Target;
            internal long Started;
        }
        static readonly ConditionalWeakTable<object, EngineCadenceSlot>.CreateValueCallback CreateEngineCadenceSlot = _ => new EngineCadenceSlot(unchecked(_engineCadenceSlotSerial++) & 1);

        static void CaptureEngineCadenceStartup()
        {
            if (_engineCadenceCaptured) return;
            _engineCadenceCaptured = true;
            _engineCadence = new EngineCadenceSchedule(_cfg.engineCadenceEnabled, _cfg.engineCadenceMode);
            _engineCadenceOwnerThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }
        internal static bool EngineCadenceTimingNeeded => _engineCadence != null && _engineCadence.Enabled && _engineCadenceFault == null;
        static bool EngineCadenceRestartPending => _engineCadenceCaptured &&
            (_engineCadence.Enabled != _cfg.engineCadenceEnabled || _engineCadence.Mode != EngineCadenceSchedule.NormalizeMode(_cfg.engineCadenceMode));

        static void SetEngineCadenceEnabled(bool value)
        {
            if (_cfg.engineCadenceEnabled == value) return;
            _cfg.engineCadenceEnabled = value;
            MarkSettingsDirty();
            SaveSettings(); // Persist before the user closes the game to restart.
        }
        static void SetEngineCadenceMode(int value)
        {
            value = EngineCadenceSchedule.NormalizeMode(value);
            if (_cfg.engineCadenceMode == value) return;
            _cfg.engineCadenceMode = value;
            MarkSettingsDirty();
            SaveSettings();
        }

        static void InstallEngineCadenceHooks()
        {
            if (_engineCadence == null || !_engineCadence.Enabled || _engineCadenceAttempted) return;
            _engineCadenceAttempted = true;
            try
            {
                var contracts = EngineCadenceContracts.Create(AccessTools.TypeByName);
                // Validate the complete audited dependency set before patching.
                foreach (var method in contracts.Targets)
                    _engineCadenceTargets.Add(method, new EngineCadenceTarget { Name = method.DeclaringType.FullName });
                foreach (var method in contracts.Invalidators)
                    _harmony.Patch(method, postfix: new HarmonyMethod(typeof(Main), nameof(EngineCadenceMaterialsChanged)));
                foreach (var method in contracts.Targets)
                    _harmony.Patch(method, prefix: new HarmonyMethod(typeof(Main), nameof(EngineCadencePrefix)),
                        finalizer: new HarmonyMethod(typeof(Main), nameof(EngineCadenceFinalizer)));
                _engineCadenceInstalled = true;
                _log.Log("[cadence] Experimental visual cadence installed: " + _engineCadence.VrHz + "/" + _engineCadence.VisualHz +
                    "; five audited visual Update methods; simulation, input and stereo rendering unchanged; game restart required for changes");
            }
            catch (Exception error) { FailEngineCadence(error); }
        }
        static void FailEngineCadence(Exception error)
        {
            if (_engineCadenceFault != null) return;
            _engineCadenceFault = error.GetType().Name + ": " + error.Message;
            _engineCadence?.ResetTracking();
            try { _log?.Error("[cadence] Original visual updates retained: " + _engineCadenceFault); }
            catch { /* Logging must not replace a native exception or prevent fallback. */ }
        }
        static void EngineCadenceMaterialsChanged()
        {
            if (!_engineCadenceInstalled || _engineCadenceFault != null || !_active ||
                System.Threading.Thread.CurrentThread.ManagedThreadId != _engineCadenceOwnerThread) return;
            _engineCadence.InvalidateTargets();
            if (DiagnosticsRecording) ++_engineCadenceInvalidations;
        }

        internal static void ObserveEngineCadenceFrame(XrFrame xrFrame, XrBeginTiming timing, int result)
        {
            if (!EngineCadenceTimingNeeded) return;
            try
            {
                double now = Time.realtimeSinceStartupAsDouble;
                _engineCadence.Observe(timing.serial, timing.predictedPeriodMs, now,
                    result == 1 && xrFrame.valid != 0 && xrFrame.shouldRender != 0 && timing.serial == xrFrame.serial);
                PrepareEngineCadenceFrame(now);
            }
            catch (Exception error) { FailEngineCadence(error); }
        }
        static void PrepareEngineCadenceFrame(double now)
        {
            bool session = _active && _runner != null;
            bool scene = session && _attached && !_modeFlat && !_suspendRequested && !TouchCameraFaulted;
            long revision = scene ? SpatialGameContextRevision : 0;
            _engineCadence.BeginFrame(Time.frameCount, now, _engineCadenceInstalled && _engineCadenceFault == null, session, scene, revision);
        }
        static bool EngineCadencePrefix(object __instance, MethodBase __originalMethod, out EngineCadenceCall __state)
        {
            __state = default;
            // This first branch also makes any partially installed hooks inert.
            if (!_engineCadenceInstalled || _engineCadenceFault != null ||
                System.Threading.Thread.CurrentThread.ManagedThreadId != _engineCadenceOwnerThread) return true;
            try
            {
                PrepareEngineCadenceFrame(Time.realtimeSinceStartupAsDouble);
                if (__instance == null || !_engineCadenceTargets.TryGetValue(__originalMethod, out var target)) return true;
                bool eligible = _engineCadence.State == EngineCadenceState.Active;
                bool run = !eligible || _engineCadence.ShouldRun(target.Slots.GetValue(__instance, CreateEngineCadenceSlot));
                if (DiagnosticsRecording)
                {
                    if (eligible) { ++target.Eligible; if (run) ++target.Executed; else ++target.Skipped; }
                    else ++target.Original;
                    if (run) __state = new EngineCadenceCall { Target = target, Started = Stopwatch.GetTimestamp() };
                }
                return run;
            }
            catch (Exception error) { FailEngineCadence(error); return true; }
        }
        static Exception EngineCadenceFinalizer(Exception __exception, EngineCadenceCall __state)
        {
            if (__state.Target != null && __state.Started != 0 && DiagnosticsRecording)
            {
                ++__state.Target.Timed;
                __state.Target.ObservedMs += (Stopwatch.GetTimestamp() - __state.Started) * 1000.0 / Stopwatch.Frequency;
            }
            // Never hide or replace an exception from the original game method.
            if (__exception != null) FailEngineCadence(__exception);
            return __exception;
        }
        static string EngineCadenceStatusText()
        {
            string state;
            if (_engineCadence == null || !_engineCadence.Enabled) state = "Off";
            else if (_engineCadenceFault != null) state = "Unavailable";
            else if (!_active) state = "Waiting for VR";
            else switch (_engineCadence.State)
            {
                case EngineCadenceState.Active: state = "Active"; break;
                case EngineCadenceState.WaitingForVr: state = "Waiting for VR"; break;
                case EngineCadenceState.WaitingForTiming: state = "Waiting for frame timing"; break;
                case EngineCadenceState.TimingMismatch: state = "Frame timing mismatch"; break;
                case EngineCadenceState.Unavailable: state = "Unavailable"; break;
                default: state = "Suspended"; break;
            }
            string current = ModLocalization.Text(state);
            if (_engineCadence != null && _engineCadence.Enabled)
                current += " · " + _engineCadence.VrHz + "/" + _engineCadence.VisualHz;
            return EngineCadenceRestartPending ? ModLocalization.Format("Restart required · current: {0}", current) : current;
        }
        static object EngineCadenceSnapshot()
        {
            var targets = new List<object>(_engineCadenceTargets.Count);
            foreach (var target in _engineCadenceTargets.Values)
                targets.Add(new { Type = target.Name, Eligible = target.Eligible, Executed = target.Executed,
                    Skipped = target.Skipped, Original = target.Original, TimedExecutions = target.Timed,
                    ObservedExecutionMs = Math.Round(target.ObservedMs, 4) });
            return new { Requested = _cfg.engineCadenceEnabled, RequestedMode = _cfg.engineCadenceMode,
                StartupEnabled = _engineCadence?.Enabled ?? false, StartupMode = _engineCadence?.Mode ?? 0,
                RestartRequired = EngineCadenceRestartPending, Installed = _engineCadenceInstalled,
                State = _engineCadenceFault != null ? "Unavailable" : _engineCadence?.State.ToString() ?? "Off",
                RuntimeReportedCadenceHz = _engineCadence?.ObservedHz ?? 0,
                TargetVrHz = _engineCadence?.VrHz ?? 72, TargetVisualHz = _engineCadence?.VisualHz ?? 36,
                MaterialInvalidations = _engineCadenceInvalidations, Error = _engineCadenceFault, Targets = targets.ToArray() };
        }
    }
}
