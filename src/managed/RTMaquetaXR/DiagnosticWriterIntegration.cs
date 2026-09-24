namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly DiagnosticWriter _diagnosticWriter = new DiagnosticWriter();
        static readonly DiagnosticCollectionPolicy _diagnosticCollection = new DiagnosticCollectionPolicy();
        static string _diagnosticRecordingChangedUtc;
        static bool _diagnosticFailureReported;

        internal static bool AllDiagnosticsEnabled => _cfg.allDiagnosticsEnabled;
        internal static bool DiagnosticsRecording => AllDiagnosticsEnabled && _diagnosticCollection.Recording;
        internal static long DiagnosticTimestamp() => DiagnosticsRecording ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        static bool? _appliedAllDiagnostics;
        internal static void SetAllDiagnosticsEnabled(bool enabled)
        {
            _cfg.allDiagnosticsEnabled = enabled;
            _cfg.detailedProfiling = enabled;
            _diagnosticCollection.Set(enabled);
            ApplyAllDiagnosticsSetting();
            MarkSettingsDirty();
        }
        internal static void ApplyAllDiagnosticsSetting()
        {
            if (_appliedAllDiagnostics == AllDiagnosticsEnabled) return;
            _appliedAllDiagnostics = AllDiagnosticsEnabled;
            _cfg.detailedProfiling = AllDiagnosticsEnabled;
            _diagnosticCollection.Set(AllDiagnosticsEnabled);
            ModDiagnosticLog.Enabled = AllDiagnosticsEnabled;
            OpenXR.SetAllDiagnosticsEnabled(AllDiagnosticsEnabled);
            NeuralTemporalHook.SetAllDiagnosticsEnabled(AllDiagnosticsEnabled);
            ApplyOptionalDiagnostics77(AllDiagnosticsEnabled);
            ApplyDiagnosticCollectionState();
        }
        internal static void SetDiagnosticsRecording(bool enabled)
        {
            SetAllDiagnosticsEnabled(enabled);
        }
        static void ApplyDiagnosticCollectionState()
        {
            _diagnosticRecordingChangedUtc = System.DateTime.UtcNow.ToString("o");
            _diagnosticWriter.SetEnabled(DiagnosticsRecording);
            OpenXR.SetDiagnosticsRecording(DiagnosticsRecording);
            // Discard partial windows across OFF/ON, including delayed Unity
            // samples. This changes no game/mod image preference or camera state.
            ResetPerformanceWindow(); _consecutiveStereoFrames = 0; _lastGpuTimestamp = 0;
            _renderTargets.Clear(); _nextTargetDiagnostic = 0;
            _modStageFrames79.Clear();
            ResetMonitorEvidence81();
            _currentModStages.Clear(); _modStageFrame = -1; _havePerformanceContext = false;
            _hbBegins.Clear(); _hbEnds.Clear(); _hbNames.Clear();
            _restoreChecks.Clear(); _restoreCheckDue = 0;
            UpdateDetailedRecording();
        }

        // WriteDiagnostics builds the complete detached graph on the main thread
        // before this hand-off. Snapshot methods must copy every mutable counter
        // and collection; the verifier audits that graph in the delivered DLL.
        internal static void EnqueueDiagnosticSnapshot(object snapshot, bool haveStereoMeasurements)
        {
            if (DiagnosticsRecording)
            {
                // Report a writer failure once through the game's independent
                // logger. Putting it only in the rejected report hid the fault.
                if (!_diagnosticFailureReported)
                {
                    string failure = _diagnosticWriter.FailureMessage();
                    if (failure != null)
                    {
                        _diagnosticFailureReported = true;
                        _log.Error("[diagnostic/writer] A performance report could not be saved: " + failure);
                    }
                }
                _diagnosticWriter.TryEnqueue(snapshot, _modFolder, _diagnosticSession, haveStereoMeasurements);
            }
        }
        internal static object DiagnosticWriterSnapshot() => new {
            Recording = DiagnosticsRecording, Revision = _diagnosticCollection.Revision,
            LastChangeUtc = _diagnosticRecordingChangedUtc, StatePersistsToSettings = true,
            MasterEnabled = AllDiagnosticsEnabled, MasterPersistsToSettings = true,
            Writer = _diagnosticWriter.Snapshot()
        };
        internal static void StopDiagnosticWriter() => _diagnosticWriter.RequestStop();
    }
}
