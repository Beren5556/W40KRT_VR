using System;
using System.IO;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static string[] _aaLabels => new[] { "TAA", "SMAA", ModLocalization.Text("No AA") };
        static EyeAaMode _appliedEyeAa;
        static bool _aaHasApplied;
        static int _aaSegment, _aaChangedFrame = -1;
        static string _aaChangedUtc;
        static EyeAaMode RequestedEyeAa => EyeAaOptions.Get(_cfg.temporalAA, _cfg.disableAA);
        static EyeAaMode EffectiveEyeAa => _neuralRawFallback ? EyeAaMode.Off : _aaHasApplied ? _appliedEyeAa : RequestedEyeAa;

        static void DrawAaSelector()
        {
            GUILayout.Label(ModLocalization.Text("Antialiasing · live changes"));
            var requested = RequestedEyeAa;
            int selected = GUILayout.SelectionGrid((int)requested, _aaLabels, 3);
            if (selected != (int)requested) SetEyeAa((EyeAaMode)selected);
            GUILayout.Label(ModLocalization.Text("Ctrl+Alt+A cycles TAA → SMAA → No AA. No VR restart required."));
            if (_active && _aaHasApplied && RequestedEyeAa != _appliedEyeAa)
                GUILayout.Label(ModLocalization.Text("The change applies when gameplay resumes."));
        }
        static void SetEyeAa(EyeAaMode selected)
        {
            if ((int)selected < 0 || (int)selected > 2) return;
            if (selected == RequestedEyeAa) return;
            EyeAaOptions.Apply(selected, out _cfg.temporalAA, out _cfg.disableAA);
            MarkSettingsDirty();
            // Queue selection until the next preparation of BOTH eyes. OnGUI
            // can run after camera rendering; never change modes between eyes.
            _log.Log("[quality] Selected eye AA=" + EyeAaOptions.Name(selected));
        }
        internal static void ApplyPendingEyeAa()
        {
            var requested = RequestedEyeAa;
            if (_aaHasApplied && requested == _appliedEyeAa) return;
            string previous = _aaHasApplied ? EyeAaOptions.Name(_appliedEyeAa) : null;
            _appliedEyeAa = requested; _aaHasApplied = true;
            ++_aaSegment; _aaChangedFrame = Time.frameCount; _aaChangedUtc = DateTime.UtcNow.ToString("o");
            _camDataNextRefresh = 0; _renderTargets.Clear();
            _leftCameraBuffer = _rightCameraBuffer = null;
            if (requested == EyeAaMode.Taa) RequestEyeAaHistoryReset();
            else StopEyeAaHistory();
            // Discard partial windows instead of labelling mixed AA timings
            // as one mode. Existing 16-frame warmup also excludes delayed GPU
            // readings and initial TAA/history convergence after the switch.
            ResetPerformanceWindow(); _consecutiveStereoFrames = 0; _lastGpuTimestamp = 0;
            if (!DiagnosticsRecording) return;
            try
            {
                var record = new { Session = _diagnosticSession, Version = BuildTag, Utc = _aaChangedUtc,
                    Event = "EyeAntialiasingApplied", Segment = _aaSegment, Previous = previous,
                    Mode = EyeAaOptions.Name(requested), UnityFrame = _aaChangedFrame,
                    Width = OpenXR.Width, Height = OpenXR.Height, WarmupFrames = 16 };
                File.AppendAllText(Path.Combine(_modFolder, "cambios-AA-" + _diagnosticSession + ".jsonl"),
                    Newtonsoft.Json.JsonConvert.SerializeObject(record) + Environment.NewLine);
                _log.Log("[quality] AA segment=" + _aaSegment + "; applied=" + EyeAaOptions.Name(requested) + "; both eyes; same resolution");
            }
            catch (Exception e) { _log.Error("[quality] AA change log unavailable: " + e.Message); }
        }
        static object AaComparisonSnapshot() => new {
            Segment = _aaSegment, Applied = _aaHasApplied ? EyeAaOptions.Name(_appliedEyeAa) : null,
            Requested = EyeAaOptions.Name(RequestedEyeAa), AppliedFrame = _aaChangedFrame, AppliedUtc = _aaChangedUtc,
            WarmupFrames = 16, PartialWindowDiscardedOnChange = true, History = EyeAaHistorySnapshot()
        };
        static void StopAaComparison() { _aaHasApplied = false; _aaChangedFrame = -1; StopEyeAaHistory(); }
    }
}
