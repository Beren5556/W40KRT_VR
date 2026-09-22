using System;
using System.Collections.Generic;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        sealed class Measurement
        {
            public int Samples;
            public double Sum, Peak;
            public void Add(double value) { ++Samples; Sum += value; Peak = Math.Max(Peak, value); }
        }
        static readonly Dictionary<string, Measurement> _stereoMeasurements = new Dictionary<string, Measurement>();
        static readonly Dictionary<string, double> _currentModStages = new Dictionary<string, double>();
        static int _modStageFrame = -1, _consecutiveStereoFrames;
        static XrBeginTiming _lastBeginTiming;
        static XrRenderTiming _lastRenderTiming;
        static ulong _recordedRenderSerial;
        static int _nativeRenderSamples, _nativeImages, _nativeFlushes, _nativeSourceViews;
        static int _poseWindowSamples;
        static Vector3 _poseGameStart, _poseEyeStart;
        static Quaternion _poseGameRotationStart, _poseEyeRotationStart;
        static float _poseGameMaxShift, _poseEyeMaxShift, _poseGameMaxAngle, _poseEyeMaxAngle;
        static readonly string _diagnosticSession = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        static int _invalidUnityTimingSamples;
        static double _overlayFrameSum, _overlayGpuSum, _overlayCpuSum;
        static int _overlayFrames, _overlayGpuSamples, _overlayCpuSamples;
        static double _overlayFps, _overlayGpu, _overlayCpu;
        static float _overlayMeasurementAt;
        internal static void RecordBeginTiming(XrBeginTiming timing) { _lastBeginTiming = timing; }
        internal static void RecordRenderTiming(XrRenderTiming timing)
        {
            if (timing.completed == 1 && timing.serial != 0 && ValidUnityTiming(timing.totalMs)) _lastRenderTiming = timing;
        }

        static void AddStereoMeasurement(string name, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) return;
            if (!_stereoMeasurements.TryGetValue(name, out var measurement))
                _stereoMeasurements[name] = measurement = new Measurement();
            measurement.Add(value);
        }

        static bool RecordSteadyStereo(bool stereo, double seconds)
        {
            _consecutiveStereoFrames = stereo ? _consecutiveStereoFrames + 1 : 0;
            if (!DiagnosticsRecording) return false;
            // FrameTimingManager returns delayed data. Exclude transition warmup
            // so menu/pause timings cannot masquerade as steady stereo work.
            bool steady = _consecutiveStereoFrames > 16 && seconds > 0 && _lastBeginTiming.serial != 0;
            RecordGameCpuFrameEvidence(stereo, steady, seconds);
            if (!steady) return false;
            RecordSteadyPose();
            AddStereoMeasurement("FrameInterval", seconds * 1000.0);
            _overlayFrameSum += seconds; ++_overlayFrames;
            if (_overlayFrameSum >= .5)
            {
                _overlayFps = _overlayFrames / _overlayFrameSum;
                _overlayGpu = _overlayGpuSamples > 0 ? _overlayGpuSum / _overlayGpuSamples : 0;
                _overlayCpu = _overlayCpuSamples > 0 ? _overlayCpuSum / _overlayCpuSamples : 0;
                _overlayMeasurementAt = Time.realtimeSinceStartup;
                _overlayFrameSum = _overlayGpuSum = _overlayCpuSum = 0;
                _overlayFrames = _overlayGpuSamples = _overlayCpuSamples = 0;
            }
            AddStereoMeasurement("PreviousSubmissionWait", _lastBeginTiming.queueWaitMs);
            AddStereoMeasurement("XrWaitFrame", _lastBeginTiming.runtimeWaitMs);
            AddStereoMeasurement("XrBeginAndLocate", _lastBeginTiming.beginLocateMs);
            AddStereoMeasurement("RuntimeDisplayPeriod", _lastBeginTiming.predictedPeriodMs);
            if (_lastRenderTiming.serial != 0 && _lastRenderTiming.serial != _recordedRenderSerial &&
                _lastRenderTiming.serial + 1 == _lastBeginTiming.serial)
            {
                _recordedRenderSerial = _lastRenderTiming.serial;
                AddStereoMeasurement("NativePreviousRenderEvent", _lastRenderTiming.totalMs);
                AddStereoMeasurement("NativeAcquireAndWaitImages", _lastRenderTiming.acquireWaitMs);
                AddStereoMeasurement("NativeDrawAndFlush", _lastRenderTiming.drawFlushMs);
                AddStereoMeasurement("NativeReleaseImages", _lastRenderTiming.releaseMs);
                AddStereoMeasurement("NativeEndFrame", _lastRenderTiming.endFrameMs);
                ++_nativeRenderSamples; _nativeImages += _lastRenderTiming.images;
                _nativeFlushes += _lastRenderTiming.flushes; _nativeSourceViews += _lastRenderTiming.sourceViewsCreated;
            }
            if (_modStageFrame == Time.frameCount)
                foreach (var stage in _currentModStages) AddStereoMeasurement(stage.Key, stage.Value);
            return true;
        }

        static void RecordSteadyUnityTiming(FrameTiming timing)
        {
            if (!DiagnosticsRecording) return;
            if (ValidUnityTiming(timing.gpuFrameTime)) { AddStereoMeasurement("UnityGpu", timing.gpuFrameTime); _overlayGpuSum += timing.gpuFrameTime; ++_overlayGpuSamples; }
            if (ValidUnityTiming(timing.cpuMainThreadFrameTime)) { AddStereoMeasurement("UnityMainThreadIncludingPluginWaits", timing.cpuMainThreadFrameTime); _overlayCpuSum += timing.cpuMainThreadFrameTime; ++_overlayCpuSamples; }
            if (ValidUnityTiming(timing.cpuRenderThreadFrameTime)) AddStereoMeasurement("UnityRenderThread", timing.cpuRenderThreadFrameTime);
            if (ValidUnityTiming(timing.cpuMainThreadPresentWaitTime)) AddStereoMeasurement("UnityPresentWait", timing.cpuMainThreadPresentWaitTime);
        }
        static bool ValidUnityTiming(double ms)
        {
            // A captured driver sample was 1.8e12 ms while VR ran at 72 FPS.
            // Retain real hitches; reject only missing/nonfinite/impossible data.
            if (ms == 0) return false;
            if (double.IsNaN(ms) || double.IsInfinity(ms) || ms < 0 || ms > 60000) { ++_invalidUnityTimingSamples; return false; }
            return true;
        }
        static void ResetLiveMeasurements()
        {
            _overlayFrameSum = _overlayGpuSum = _overlayCpuSum = 0;
            _overlayFrames = _overlayGpuSamples = _overlayCpuSamples = 0;
            _overlayMeasurementAt = -100;
        }
        static string LiveMeasurementLine() => !DiagnosticsRecording ? ModLocalization.Text("Diagnostics paused · read FPS in Virtual Desktop") :
            Time.realtimeSinceStartup - _overlayMeasurementAt < 2 && _consecutiveStereoFrames > 16
            ? "FPS " + _overlayFps.ToString("0.0", ModLocalization.Culture) + "  |  GPU " + (_overlayGpu > 0 ? _overlayGpu.ToString("0.0", ModLocalization.Culture) + " ms" : ModLocalization.Text("n/a")) +
                "  |  CPU " + (_overlayCpu > 0 ? _overlayCpu.ToString("0.0", ModLocalization.Culture) + ModLocalization.Text(" ms (includes waits)") : ModLocalization.Text("n/a"))
            : ModLocalization.Text("Waiting for a stable stereo sample");

        static object SteadyStereoSnapshot()
        {
            var values = new Dictionary<string, object>();
            foreach (var pair in _stereoMeasurements)
                values[pair.Key] = new {
                    MeanMs = Math.Round(pair.Value.Sum / pair.Value.Samples, 3),
                    PeakMs = Math.Round(pair.Value.Peak, 3), Samples = pair.Value.Samples
                };
            _stereoMeasurements.TryGetValue("FrameInterval", out var frames);
            return new {
                Samples = frames?.Samples ?? 0,
                Fps = frames != null && frames.Sum > 0 ? (double?)Math.Round(1000.0 * frames.Samples / frames.Sum, 1) : null,
                ExcludedTransitionFrames = 16, UnityTimingsAreDelayed = true, InvalidUnityTimingSamples = _invalidUnityTimingSamples,
                CpuAndHitches = GameCpuEvidenceSnapshot(),
                NativeRenderEvent = new { Samples = _nativeRenderSamples, Images = _nativeImages, Flushes = _nativeFlushes,
                    SourceViewsCreated = _nativeSourceViews, LastCompleted = _lastRenderTiming,
                    Timing = "CPU wall time from previous completed OpenXR serial; not GPU time; total includes subphases" },
                PoseMovement = new { Samples = _poseWindowSamples,
                    GameStart = PosePoint(_poseGameStart), EyeStart = PosePoint(_poseEyeStart),
                    GameMaxDistanceFromStart = _poseGameMaxShift, EyeMaxDistanceFromStart = _poseEyeMaxShift,
                    GameMaxAngleFromStart = _poseGameMaxAngle, EyeMaxAngleFromStart = _poseEyeMaxAngle,
                    Units = "game-world units and degrees; first valid pose of this window" },
                Measurements = values
            };
        }

        static object PosePoint(Vector3 value) => new { value.x, value.y, value.z };
        static void RecordSteadyPose()
        {
            var game = _attachedCam;
            var eye = _runner != null ? _runner.GetEyeL() : null;
            if (game == null || eye == null) return;
            var gp = game.transform.position; var gr = game.transform.rotation;
            var ep = eye.transform.position; var er = eye.transform.rotation;
            if (_poseWindowSamples++ == 0)
            {
                _poseGameStart = gp; _poseEyeStart = ep;
                _poseGameRotationStart = gr; _poseEyeRotationStart = er;
            }
            _poseGameMaxShift = Mathf.Max(_poseGameMaxShift, Vector3.Distance(gp, _poseGameStart));
            _poseEyeMaxShift = Mathf.Max(_poseEyeMaxShift, Vector3.Distance(ep, _poseEyeStart));
            _poseGameMaxAngle = Mathf.Max(_poseGameMaxAngle, Quaternion.Angle(gr, _poseGameRotationStart));
            _poseEyeMaxAngle = Mathf.Max(_poseEyeMaxAngle, Quaternion.Angle(er, _poseEyeRotationStart));
        }
        static void ResetSteadyPoseWindow()
        {
            _nativeRenderSamples = _nativeImages = _nativeFlushes = _nativeSourceViews = 0;
            _poseWindowSamples = 0;
            _poseGameMaxShift = _poseEyeMaxShift = _poseGameMaxAngle = _poseEyeMaxAngle = 0;
            _poseGameStart = _poseEyeStart = Vector3.zero;
        }
    }
}
