using System;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _outputPending, _outputDraining;
        static float _requestedOutputScale = 1f, _outputStarted;
        static RenderTexture _nextOutputLeft, _nextOutputRight;
        static string _outputStatus = "";
        static string OutputStatusText => ModLocalization.DiagnosticText(_outputStatus);
        static float RequestedOutputScale => _outputPending ? _requestedOutputScale : _cfg.renderScale;

        static void RequestOutputScale(float scale)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale)) return;
            scale = Mathf.Clamp(scale, .25f, 1.5f);
            if (!_active) { _cfg.renderScale = scale; MarkSettingsDirty(); return; }
            if (!_outputPending && Mathf.Approximately(scale, _cfg.renderScale)) return;
            _requestedOutputScale = scale;
            if (!_outputPending) _outputStarted = Time.realtimeSinceStartup;
            _outputPending = true; _outputStatus = "Resolution requested; waiting for the stereo pair";
        }

        // Before RTX_Begin and before rendering either eye. Rejected changes
        // retain both the old Unity targets and the old OpenXR swapchains.
        internal static bool ApplyPendingOutputResolution()
        {
            if (!_outputPending) return true;
            bool committed = false;
            try
            {
                if (Time.realtimeSinceStartup - _outputStarted > 10)
                    throw new InvalidOperationException("Could not finish releasing resources");
                if (!_outputDraining)
                {
                    _outputDraining = true;
                    DetachNeuralIntegration();
                }
                if (NeuralTemporalHook.PumpMaintenance()) return false;
                OpenXR.RTX_GetStats(out var stats);
                if (stats.queued != 0) return false;
                if (OpenXR.RTX_PreviewSize(_requestedOutputScale, out int width, out int height) != 1)
                    throw new InvalidOperationException("OpenXR runtime rejected this resolution");
                if (_nextOutputLeft != null && (_nextOutputLeft.width != width || _nextOutputLeft.height != height)) ReleaseStagedOutputs();
                if (_nextOutputLeft == null) _nextOutputLeft = NewRt(width, height);
                if (_nextOutputRight == null) _nextOutputRight = NewRt(width, height);
                int result = OpenXR.TryResize(_requestedOutputScale);
                if (result == 0) return false;
                if (result != 1) throw new InvalidOperationException(OpenXR.Error());
                committed = true;
                var oldLeft = _rtL; var oldRight = _rtR;
                _rtL = _nextOutputLeft; _rtR = _nextOutputRight;
                _nextOutputLeft = _nextOutputRight = null;
                _eyeW = width; _eyeH = height;
                _cfg.renderScale = OpenXR.AppliedRenderScale;
                _outputPending = _outputDraining = false;
                // Native swapchains have committed; never claim rollback after this point.
                try { _runner?.RefreshEyeTargets(); }
                finally { ReleaseOutput(oldLeft); ReleaseOutput(oldRight); }
                MarkSettingsDirty();
                _outputStatus = "Output applied: " + width + " × " + height;
                _log.Log("[image] " + _outputStatus + "; preserved session and head reference");
                ResetEyeQualityRefresh(); RequestEyeAaHistoryReset();
                _leftCameraBuffer = _rightCameraBuffer = null;
                ResetPerformanceWindow(); _consecutiveStereoFrames = 0; _lastGpuTimestamp = 0;
                _outputPending = _outputDraining = false;
                return true;
            }
            catch (Exception error)
            {
                ReleaseStagedOutputs();
                _outputPending = _outputDraining = false;
                _outputStatus = (committed ? "Output changed; restoring cameras: " : "Previous resolution retained: ") + error.Message;
                _log.Error("[image] " + _outputStatus);
                if (committed) { MarkSettingsDirty(); SuspendForTransition("output-camera-recovery"); }
                return true;
            }
        }
        static void ReleaseOutput(RenderTexture texture)
        {
            if (texture == null) return;
            try { OpenXR.ForgetTexture(texture); texture.Release(); }
            catch (Exception error) { _log.Error("[image] Retirada de textura: " + error.Message); }
            finally { UnityEngine.Object.Destroy(texture); }
        }
        static void ReleaseStagedOutputs()
        {
            ReleaseOutput(_nextOutputLeft); ReleaseOutput(_nextOutputRight);
            _nextOutputLeft = _nextOutputRight = null;
        }
        static void StopLiveResolution()
        {
            ReleaseStagedOutputs(); _outputPending = _outputDraining = false;
        }
    }
}
