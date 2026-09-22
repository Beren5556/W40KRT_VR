using System;
using System.IO;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _optimizationOptionsReady, _lastForcedOption, _lastPointerOption;
        static int _optimizationSegment;
        static string _optimizationChangedUtc;

        internal static void ApplyRuntimeOptimizationOptions()
        {
            if (_optimizationOptionsReady && _lastForcedOption == _cfg.syncForcedVisibility &&
                _lastPointerOption == _cfg.useGamePointerCache) return;
            _optimizationOptionsReady = true;
            _lastForcedOption = _cfg.syncForcedVisibility; _lastPointerOption = _cfg.useGamePointerCache;
            ++_optimizationSegment; _optimizationChangedUtc = DateTime.UtcNow.ToString("o");
            ResetPerformanceWindow(); _consecutiveStereoFrames = 0; _lastGpuTimestamp = 0;
            if (!DiagnosticsRecording) return;
            try
            {
                var record = new { Session = _diagnosticSession, Version = BuildTag, Utc = _optimizationChangedUtc,
                    Segment = _optimizationSegment, UnityFrame = Time.frameCount,
                    ForcedVisibility = _lastForcedOption, PointerCache = _lastPointerOption, WarmupFrames = 16 };
                File.AppendAllText(Path.Combine(_modFolder, "cambios-optimizacion-" + _diagnosticSession + ".jsonl"),
                    Newtonsoft.Json.JsonConvert.SerializeObject(record) + Environment.NewLine);
            }
            catch (Exception e) { _log.Error("[performance] Option change log unavailable: " + e.Message); }
        }

        static object RuntimeOptimizationSnapshot() => new {
            Segment = _optimizationSegment, Applied = _optimizationOptionsReady, Utc = _optimizationChangedUtc,
            ForcedVisibility = _lastForcedOption, PointerCache = _lastPointerOption,
            PartialWindowDiscardedOnChange = true, WarmupFrames = 16
        };
    }
}
