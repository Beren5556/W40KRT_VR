using System;
using System.Diagnostics;
using System.IO;
using RTMaquetaXR.Diagnostics;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _temporalProbeInstalled, _temporalProbeLogFailed;
        static int _temporalProbeRows;
        static string _temporalProbePath;
        static string _temporalProbeStatus = "not-started";

        static void StartTemporalInputProbe()
        {
            _temporalProbeRows = 0;
            _temporalProbeLogFailed = false;
            _temporalProbeStatus = "installing";
            try
            {
                _temporalProbePath = Path.Combine(_modFolder, "diagnostico-temporal-" + _diagnosticSession + ".jsonl");
                _temporalProbeInstalled = WaaaghTemporalProbe.Install(_harmony,
                    () => DiagnosticsRecording && _active && _attached && !_modeFlat ? RenderingEyeIndex() : -1,
                    () => _renderingCamera, LogTemporalInputProbe);
            }
            catch (Exception error)
            {
                _temporalProbeInstalled = false;
                _temporalProbeStatus = "install-failed";
                _log.Error("[temporal-probe] unavailable; TAA retained: " + error.Message);
            }
        }

        static void LogTemporalInputProbe(string message)
        {
            if (!DiagnosticsRecording) return;
            const string prefix = "[temporal-probe] ";
            if (message == null) return;
            if (!message.StartsWith(prefix + "{", StringComparison.Ordinal))
            { _temporalProbeStatus = message; _log.Log(message); return; }
            if (_temporalProbeLogFailed) return;
            long started = Stopwatch.GetTimestamp();
            try
            {
                // Session and build are generated identifiers. Data is JSON emitted by the observer.
                string record = "{\"Session\":\"" + _diagnosticSession + "\",\"Build\":\"" + BuildTag +
                    "\",\"Data\":" + message.Substring(prefix.Length) + "}";
                File.AppendAllText(_temporalProbePath, record + Environment.NewLine);
                ++_temporalProbeRows;
            }
            catch (Exception error)
            {
                _temporalProbeLogFailed = true;
                _temporalProbeStatus = "log-failed";
                WaaaghTemporalProbe.Detach();
                _log.Error("[temporal-probe] log unavailable; observation stopped: " + error.Message);
            }
            finally { RecordModStage("TemporalProbeLog", started); }
        }

        static void StopTemporalInputProbe()
        {
            WaaaghTemporalProbe.Stop();
            _temporalProbeInstalled = false;
            _temporalProbeStatus = "stopped";
        }

        static object TemporalInputProbeSnapshot() => new {
            Installed = _temporalProbeInstalled,
            Status = _temporalProbeStatus,
            LogFailed = _temporalProbeLogFailed,
            RowsWritten = _temporalProbeRows,
            LimitPerEye = 8,
            MetadataOnly = true,
            GpuReadback = false,
            DlssEnabled = false
        };
    }
}
