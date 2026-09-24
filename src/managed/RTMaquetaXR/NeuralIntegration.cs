using System;
using System.IO;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static string[] _neuralLabels => new[] { ModLocalization.Text("Off"), ModLocalization.Text("NVIDIA diagnostic"), "DLAA", "DLSS" };
        static bool _neuralHooksInstalled;
        static int _neuralAppliedMode = -1;
        static float _neuralAppliedScale = -1, _neuralAppliedSharpness = -1;
        static int _neuralAppliedPreset = -1;
        static bool _neuralResetPending, _neuralRawFallback;
        static string _neuralLogPath;
        static bool GameFsrAllowedForEyes => _cfg.allowGameFsr && _cfg.neuralMode == 0 && _neuralAppliedMode <= 0;

        static void StartNeuralIntegration()
        {
            _neuralAppliedMode = -1; _neuralRawFallback = false;
            _neuralLogPath = Path.Combine(_modFolder, "neural-" + _diagnosticSession + ".log");
            _neuralHooksInstalled = NeuralTemporalHook.Install(_harmony,
                () => _active && _attached && !_modeFlat ? RenderingEyeIndex() : -1,
                () => _renderingCamera, message => _log.Log(message),
                camera => _active && _attached && !_modeFlat && IsEye(camera));
        }

        static void DrawNeuralSelector()
        {
            GUILayout.Label(ModLocalization.Text("NVIDIA · DLSS / DLAA · recommended runtime ") + NeuralRuntimeInfo.RecommendedVersion);
            int requested = Mathf.Clamp(_cfg.neuralMode, 0, 3);
            int selected = GUILayout.SelectionGrid(requested, _neuralLabels, 4);
            if (selected != requested)
            {
                _cfg.neuralMode = selected;
                if (selected != 0) SetEyeAa(EyeAaMode.Taa);
                MarkSettingsDirty();
            }
            if (_cfg.neuralMode == 3)
            {
                float scale = Slider(ModLocalization.Text("DLSS internal resolution"), _cfg.neuralScale, .5f, 1f, "0.00");
                if (!Mathf.Approximately(scale, _cfg.neuralScale)) { _cfg.neuralScale = scale; MarkSettingsDirty(); }
            }
            if (_cfg.neuralMode != 0)
            {
                int preset = NeuralPresets81.Values[GUILayout.SelectionGrid(NeuralPresets81.Index(_cfg.neuralPreset), NeuralPresets81.Labels(), 4)];
                if (preset != _cfg.neuralPreset) { _cfg.neuralPreset = preset; MarkSettingsDirty(); }
                float sharpness = Slider(ModLocalization.Text("NVIDIA sharpness"), _cfg.neuralSharpness, 0f, 1f, "0.00");
                if (!Mathf.Approximately(sharpness, _cfg.neuralSharpness)) { _cfg.neuralSharpness = sharpness; MarkSettingsDirty(); }
            }
            GUILayout.Label(ModLocalization.Text("DLAA uses native resolution; DLSS reconstructs output from a lower internal resolution. Diagnostic only evaluates NVIDIA."));
            if (_cfg.neuralMode != 0)
                GUILayout.Label(ModLocalization.Text("FSR is suspended in VR while NVIDIA is selected. If NVIDIA fails, the current image is shown without antialiasing."));
            if (_active && _neuralHooksInstalled)
            { GUILayout.Label(NeuralTemporalHook.StatusLine()); GUILayout.Label(NeuralTemporalHook.RuntimeStatusText()); }
            if (_active && _neuralAppliedMode != _cfg.neuralMode)
                GUILayout.Label(ModLocalization.Text("The selection applies to both eyes when gameplay resumes."));
        }

        // Called once before configuring either camera; OnGUI only requests changes.
        internal static void ApplyPendingNeural()
        {
            int requested = Mathf.Clamp(_cfg.neuralMode, 0, 3);
            if (requested != 0 && RequestedEyeAa != EyeAaMode.Taa)
            {
                requested = _cfg.neuralMode = 0;
                MarkSettingsDirty();
            }
            float scale = requested == 3 ? _cfg.neuralScale : 1f;
            int preset = NeuralPresets81.Normalize(_cfg.neuralPreset);
            bool configurationChanged = requested != _neuralAppliedMode ||
                (requested != 0 && (scale != _neuralAppliedScale || preset != _neuralAppliedPreset));
            if (_neuralHooksInstalled && configurationChanged)
            {
                // A previous scene/mode may still own GPU work. PumpShutdown runs
                // from Update; retry here after retirement, never between eyes.
                if (!NeuralTemporalHook.PumpMaintenance())
                {
                    NeuralTemporalHook.SetOutputSize(_eyeW, _eyeH);
                    bool accepted = NeuralTemporalHook.Configure(requested,
                        Path.Combine(_modFolder, "NVIDIA"), _neuralLogPath, scale, preset, _cfg.neuralSharpness);
                    _neuralAppliedMode = requested; // Applied request; actual backend success is reported separately.
                    _neuralAppliedScale = scale; _neuralAppliedPreset = preset;
                    _neuralAppliedSharpness = _cfg.neuralSharpness;
                    if (!accepted) _log.Error("[neural] NVIDIA unavailable; raw image without antialiasing. Selection retained for an explicit retry.");
                    _camDataNextRefresh = 0; _renderTargets.Clear();
                    RequestEyeAaHistoryReset();
                    ResetPerformanceWindow(); _consecutiveStereoFrames = 0; _lastGpuTimestamp = 0;
                    _log.Log("[neural] Applied selection=" + _neuralAppliedMode + "; both eyes; FSR=" + GameFsrAllowedForEyes);
                }
            }
            // Latch fallback once BEFORE configuring either eye, never in the middle of a pair.
            bool rawFallback = requested != 0 && (!_neuralHooksInstalled || NeuralTemporalHook.IsFaulted || NeuralTemporalHook.IsStopping);
            if (rawFallback != _neuralRawFallback)
            {
                _neuralRawFallback = rawFallback; ResetEyeQualityRefresh();
                _leftCameraBuffer = _rightCameraBuffer = null;
                ResetPerformanceWindow(); _consecutiveStereoFrames = 0; _lastGpuTimestamp = 0;
            }
            if (_neuralHooksInstalled && _neuralAppliedSharpness != _cfg.neuralSharpness)
            {
                NeuralTemporalHook.SetSharpness(_cfg.neuralSharpness);
                _neuralAppliedSharpness = _cfg.neuralSharpness;
                ResetPerformanceWindow();
            }
            if (_neuralResetPending)
            {
                _neuralResetPending = false;
                NeuralTemporalHook.ResetHistory();
            }
        }

        internal static void RequestNeuralHistoryReset() { _neuralResetPending = true; }

        static void DetachNeuralIntegration()
        {
            if (_neuralHooksInstalled) NeuralTemporalHook.Detach();
            _neuralAppliedMode = -1;
            _neuralResetPending = true;
        }

        static void StopNeuralIntegration()
        {
            NeuralTemporalHook.Stop();
            // UMM stops calling OnUpdate after the mod is disabled. Retirement
            // must outlive Runner and ModEntry.Active without blocking the UI.
            if (!_appQuitting && NeuralTemporalHook.IsStopping) NeuralRetirementPump.EnsureRunning();
            _neuralHooksInstalled = false;
            _neuralAppliedMode = -1;
        }

        static object NeuralIntegrationSnapshot() => new {
            RequestedMode = _cfg.neuralMode, AppliedMode = _neuralAppliedMode,
            Installed = _neuralHooksInstalled, Hook = NeuralTemporalHook.Snapshot(),
            InputScale = _neuralAppliedScale, RequestedInputScale = _cfg.neuralScale,
            RequestedPreset = NeuralPresets81.Name(_cfg.neuralPreset), Sharpness = _cfg.neuralSharpness,
            OriginalTaaComputedForFallback = false, RawFallbackActive = _neuralRawFallback, FallbackAntialiasing = "None",
            SameFrameGeometryStereo = true
        };
    }

    internal sealed class NeuralRetirementPump : MonoBehaviour
    {
        static NeuralRetirementPump instance;
        internal static void EnsureRunning()
        {
            if (instance != null) return;
            var obj = new GameObject("RTMaquetaXR_NeuralRetirement");
            DontDestroyOnLoad(obj);
            instance = obj.AddComponent<NeuralRetirementPump>();
        }
        void Update()
        {
            OpenXR.PumpShutdown();
            bool neuralBusy = NeuralTemporalHook.PumpMaintenance();
            if (!neuralBusy && !OpenXR.IsStopping) Destroy(gameObject);
        }
        void OnDestroy() { if (instance == this) instance = null; }
    }
}
