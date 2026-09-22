using System;
using System.IO;
using UnityEngine;
using UnityModManagerNet;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static string[] _drawDistanceLabels => new[] { ModLocalization.Text("Original"), ModLocalization.Text("100 units"), ModLocalization.Text("60 units"), ModLocalization.Text("Custom") };
        static readonly DrawDistanceTrial _drawDistanceTrial = new DrawDistanceTrial();
        static readonly DrawDistanceStartGate _drawDistanceStartGate = new DrawDistanceStartGate();
        static int _drawDistanceStartFrame = -1;
        static bool _drawDistanceApplied;
        static int _drawDistanceMode, _drawDistanceSegment, _drawDistanceChangedFrame = -1;
        static int _drawDistanceBoundary, _drawDistanceAppliedBoundary = -1, _drawDistanceTimerFrame = -1;
        static string _drawDistanceChangedUtc, _drawDistanceReason = "initial-selection";
        static string _drawDistanceLastEvent = "not-applied";
        static float _drawDistanceNear, _drawDistanceOriginalFar, _drawDistanceEffectiveFar;
        static float _drawDistanceAppliedCustom = 100f, _drawTrialCustom;
        static EyeAaMode _drawTrialAa;
        static int _drawTrialWidth, _drawTrialHeight, _drawTrialNeuralMode;
        static float _drawTrialScale, _drawTrialSharpness, _drawTrialWorldScale, _drawTrialIpdScale;
        static float _drawTrialNeuralScale, _drawTrialNeuralSharpness;
        static int _drawTrialNeuralPreset;
        static bool _drawTrialGameFsr, _drawTrialSkipDesktop, _drawTrialVisibleCull, _drawTrialIndirectCull;
        static bool _drawTrialForcedVisibility, _drawTrialPointerCache, _drawTrialFlipEyes;
        static int RequestedDrawDistance => _drawDistanceTrial.Running ? _drawDistanceTrial.Mode : DrawDistanceOptions.Normalize(_cfg.drawDistanceMode);
        static string RequestedDistanceName(int mode) => DrawDistanceOptions.Name(mode, _cfg.drawDistanceCustom);
        static string AppliedDistanceName => DrawDistanceOptions.Name(_drawDistanceMode, _drawDistanceAppliedCustom);

        static string DrawDistanceDisplayName(int mode, float custom) => mode == 3 ?
            ModLocalization.Format("{0} units (custom)", DrawDistanceOptions.SanitizeCustom(custom).ToString("0.##", ModLocalization.Culture)) :
            ModLocalization.Text(DrawDistanceOptions.Name(mode, custom));

        static bool DrawDistanceVrReady() => !InSpaceCombat && !InNavigationMap && _active && _attached && !_modeFlat && !FlatWanted && _attachedCam != null;
        static bool DrawDistancePanelOpen()
        {
            var panel = UnityModManager.UI.Instance;
            return panel == null || panel.Opened;
        }

        static void DrawDrawDistanceSelector()
        {
            GUILayout.Label(ModLocalization.Text("VR draw distance · live changes"));
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && !_drawDistanceTrial.Running && !_drawDistanceStartGate.Armed;
            int selected = GUILayout.SelectionGrid(DrawDistanceOptions.Normalize(_cfg.drawDistanceMode), _drawDistanceLabels, 4);
            GUI.enabled = previousEnabled;
            if (!_drawDistanceTrial.Running && !_drawDistanceStartGate.Armed && selected != DrawDistanceOptions.Normalize(_cfg.drawDistanceMode)) SetDrawDistanceMode(selected);
            GUI.enabled = previousEnabled && !_drawDistanceTrial.Running && !_drawDistanceStartGate.Armed;
            float custom = Slider(ModLocalization.Text("Custom distance"), _cfg.drawDistanceCustom, 20f, 400f, "0.0");
            // Opening the panel must not round a stored fractional value or
            // activate Custom unless the slider actually moved.
            if (!Mathf.Approximately(custom, _cfg.drawDistanceCustom)) SetCustomDrawDistance(Mathf.Round(custom));
            GUI.enabled = previousEnabled;
            GUILayout.Label(ModLocalization.Text("Move the slider to apply a custom distance. Saved for both eyes without restarting VR."));
            string actual = _drawDistanceApplied ? DrawDistanceDisplayName(_drawDistanceMode, _drawDistanceAppliedCustom) : ModLocalization.Text("waiting for VR");
            GUILayout.Label(ModLocalization.Text("Applied: ") + actual + (_drawDistanceApplied
                ? ModLocalization.Text(" · effective limit ") + _drawDistanceEffectiveFar.ToString("0.##") + ModLocalization.Text(" units") : ""));
            if (_stereoMeasurements.TryGetValue("FrameInterval", out var recent) && recent.Samples >= 30 && recent.Sum > 0)
                GUILayout.Label(ModLocalization.Text("Recent FPS: ") + (1000d * recent.Samples / recent.Sum).ToString("0.0"));
            else GUILayout.Label(ModLocalization.Text("Recent FPS: waiting for a stable sample."));
            GUILayout.Label(ModLocalization.Text("Resolution and antialiasing remain unchanged. Short distances can clip the background."));
            if (_drawDistanceTrial.Running)
            {
                string warmup = _consecutiveStereoFrames <= 16 ? ModLocalization.Text(" · stabilizing image") : "";
                GUILayout.Label(ModLocalization.Text("Trial ") + (_drawDistanceTrial.Phase + 1) + "/4 · " + DrawDistanceDisplayName(_drawDistanceTrial.Mode, _cfg.drawDistanceCustom) +
                    ModLocalization.Text(" · remaining ") + Math.Max(0, DrawDistanceTrial.PhaseSeconds - _drawDistanceTrial.UsefulSeconds).ToString("0.0") + ModLocalization.Text(" useful s") + warmup);
                GUILayout.Label(ModLocalization.Text("Keep the same view during comparison."));
                if (GUILayout.Button(ModLocalization.Text("Cancel trial and restore ") + DrawDistanceDisplayName(_drawDistanceTrial.PreviousMode, _cfg.drawDistanceCustom)))
                    CancelDrawDistanceTrial("user-cancelled");
            }
            else if (_drawDistanceStartGate.Armed)
            {
                GUILayout.Label(ModLocalization.Text("Trial armed. Close this panel and return to gameplay."));
                GUILayout.Label(ModLocalization.Text("Starts after 5 seconds of stable VR with this panel closed."));
                if (GUILayout.Button(ModLocalization.Text("Cancel pending trial"))) CancelDrawDistanceStart("user-cancelled");
            }
            else
            {
                GUI.enabled = previousEnabled && _active;
                if (GUILayout.Button(ModLocalization.Text("Prepare automatic comparison"))) ArmDrawDistanceTrial();
                GUI.enabled = previousEnabled;
                GUILayout.Label(ModLocalization.Text("Original → 100 → 60 → Original: four phases of 15 useful seconds. Restores your selection at the end."));
                GUILayout.Label(ModLocalization.Text("Open this panel to cancel a running comparison."));
                if (!_active) GUILayout.Label(ModLocalization.Text("Enable VR to prepare the comparison."));
            }
        }

        // No keyboard polling here: the former shortcuts also trigger game actions.
        static void UpdateDrawDistanceControl()
        {
            if (_drawDistanceTrial.Running && (!DrawDistanceVrReady() || DrawDistancePanelOpen()))
                CancelDrawDistanceTrial("panel-opened-or-left-vr");
            if (_drawDistanceStartGate.Armed && (!DrawDistanceVrReady() || DrawDistancePanelOpen()))
            { _drawDistanceStartGate.ResetDelay(); _drawDistanceStartFrame = -1; }
        }

        static void ArmDrawDistanceTrial()
        {
            if (!_active || _drawDistanceTrial.Running || _drawDistanceStartGate.Armed) return;
            _drawDistanceStartGate.Arm(); _drawDistanceStartFrame = -1;
            WriteDrawDistanceEvent("TrialArmed", "panel-button", -1, RequestedDrawDistance, 0);
        }

        static void CancelDrawDistanceStart(string reason)
        {
            if (!_drawDistanceStartGate.Armed) return;
            _drawDistanceStartGate.Cancel(); _drawDistanceStartFrame = -1;
            WriteDrawDistanceEvent("StartCancelled", reason, -1, RequestedDrawDistance, 0);
        }

        static void SetDrawDistanceMode(int mode)
        {
            mode = DrawDistanceOptions.Normalize(mode);
            if (_drawDistanceTrial.Running || _drawDistanceStartGate.Armed || mode == DrawDistanceOptions.Normalize(_cfg.drawDistanceMode)) return;
            _cfg.drawDistanceMode = mode; MarkSettingsDirty();
            ++_drawDistanceBoundary; _drawDistanceReason = "manual-selection";
            _log.Log("[distance] Selected " + RequestedDistanceName(mode) + "; queued for both eyes.");
        }

        static void SetCustomDrawDistance(float units, string reason = "custom-slider")
        {
            if (_drawDistanceTrial.Running || _drawDistanceStartGate.Armed) return;
            units = DrawDistanceOptions.SanitizeCustom(units);
            if (_cfg.drawDistanceMode == 3 && _cfg.drawDistanceCustom == units) return;
            _cfg.drawDistanceCustom = units; _cfg.drawDistanceMode = 3;
            MarkSettingsDirty(); ++_drawDistanceBoundary; _drawDistanceReason = reason;
        }

        static void StartDrawDistanceTrial()
        {
            if (!DrawDistanceVrReady() || DrawDistancePanelOpen() || !_drawDistanceTrial.Start(_cfg.drawDistanceMode)) return;
            _drawTrialAa = RequestedEyeAa; _drawTrialWidth = OpenXR.Width; _drawTrialHeight = OpenXR.Height;
            _drawTrialNeuralMode = _cfg.neuralMode;
            _drawTrialNeuralScale = _cfg.neuralScale; _drawTrialNeuralSharpness = _cfg.neuralSharpness; _drawTrialNeuralPreset = _cfg.neuralPreset;
            _drawTrialScale = _cfg.renderScale; _drawTrialSharpness = _cfg.taaSharpness; _drawTrialGameFsr = _cfg.allowGameFsr;
            _drawTrialWorldScale = _cfg.worldScale; _drawTrialIpdScale = _cfg.ipdScale;
            _drawTrialSkipDesktop = _cfg.skipDesktopWorld; _drawTrialVisibleCull = _cfg.visibleRegionCulling;
            _drawTrialIndirectCull = _cfg.indirectVisibleRegionCulling; _drawTrialForcedVisibility = _cfg.syncForcedVisibility;
            _drawTrialPointerCache = _cfg.useGamePointerCache; _drawTrialFlipEyes = OpenXR.FlipEyes;
            _drawTrialCustom = _cfg.drawDistanceCustom;
            ++_drawDistanceBoundary; _drawDistanceTimerFrame = -1; _drawDistanceReason = "automatic-phase";
            // The four temporary modes never enter _cfg or call MarkSettingsDirty/SaveSettings.
            _log.Log("[distance] Automatic comparison requested: Original, 100, 60, Original; 15 useful VR seconds per phase.");
        }

        static void CancelDrawDistanceTrial(string reason)
        {
            if (!_drawDistanceTrial.Running) return;
            int phase = _drawDistanceTrial.Phase, mode = _drawDistanceTrial.Mode;
            double useful = _drawDistanceTrial.UsefulSeconds;
            WriteDrawDistanceEvent("PhaseEnd", reason, phase, mode, useful);
            int restored = _drawDistanceTrial.Cancel();
            // Restore the captured manual selection, never one of the automatic phase modes.
            _cfg.drawDistanceMode = restored;
            if (restored == 3) _cfg.drawDistanceCustom = _drawTrialCustom;
            ++_drawDistanceBoundary; _drawDistanceTimerFrame = -1; _drawDistanceReason = reason;
            WriteDrawDistanceEvent("TrialCancelled", reason, -1, restored, useful);
            _log.Log("[distance] Automatic comparison cancelled; restoring " + RequestedDistanceName(restored) + ".");
        }

        internal static void ApplyPendingDrawDistance()
        {
            if (!DrawDistanceVrReady())
            {
                if (_drawDistanceTrial.Running) CancelDrawDistanceTrial("left-vr");
                _drawDistanceStartGate.ResetDelay(); _drawDistanceStartFrame = -1;
                _drawDistanceTimerFrame = -1;
                return;
            }
            int frame = Time.frameCount;
            bool panelOpen = (_drawDistanceStartGate.Armed || _drawDistanceTrial.Running) && DrawDistancePanelOpen();
            if (_drawDistanceTrial.Running && panelOpen) CancelDrawDistanceTrial("panel-opened");
            if (_drawDistanceStartGate.Armed)
            {
                bool ready = _drawDistanceStartFrame == frame - 1 && _consecutiveStereoFrames > 16;
                bool start = _drawDistanceStartGate.Tick(Time.unscaledDeltaTime, ready, panelOpen);
                _drawDistanceStartFrame = frame;
                if (start) StartDrawDistanceTrial();
            }
            if (_drawDistanceTrial.Running)
            {
                if (_drawTrialAa != RequestedEyeAa || _drawTrialWidth != OpenXR.Width || _drawTrialHeight != OpenXR.Height ||
                    _drawTrialScale != _cfg.renderScale || _drawTrialSharpness != _cfg.taaSharpness || _drawTrialGameFsr != _cfg.allowGameFsr || _drawTrialNeuralMode != _cfg.neuralMode ||
                    _drawTrialNeuralScale != _cfg.neuralScale || _drawTrialNeuralSharpness != _cfg.neuralSharpness || _drawTrialNeuralPreset != _cfg.neuralPreset || _outputPending ||
                    _drawTrialWorldScale != _cfg.worldScale || _drawTrialIpdScale != _cfg.ipdScale ||
                    _drawTrialSkipDesktop != _cfg.skipDesktopWorld || _drawTrialVisibleCull != _cfg.visibleRegionCulling ||
                    _drawTrialIndirectCull != _cfg.indirectVisibleRegionCulling || _drawTrialForcedVisibility != _cfg.syncForcedVisibility ||
                    _drawTrialPointerCache != _cfg.useGamePointerCache || _drawTrialFlipEyes != OpenXR.FlipEyes ||
                    _drawTrialCustom != _cfg.drawDistanceCustom ||
                    _drawDistanceTrial.PreviousMode != DrawDistanceOptions.Normalize(_cfg.drawDistanceMode))
                    CancelDrawDistanceTrial("comparison-settings-changed");
                else
                {
                    int phase = _drawDistanceTrial.Phase, mode = _drawDistanceTrial.Mode;
                    bool eligible = _drawDistanceApplied && _drawDistanceAppliedBoundary == _drawDistanceBoundary &&
                        _drawDistanceTimerFrame == frame - 1 && _consecutiveStereoFrames > 16;
                    var step = _drawDistanceTrial.Tick(Time.unscaledDeltaTime, eligible);
                    if (step != DrawDistanceTrialStep.None)
                    {
                        WriteDrawDistanceEvent("PhaseEnd", "completed", phase, mode, _drawDistanceTrial.FinishedPhaseSeconds);
                        ++_drawDistanceBoundary;
                        if (step == DrawDistanceTrialStep.Completed)
                        {
                            _cfg.drawDistanceMode = _drawDistanceTrial.PreviousMode;
                            if (_cfg.drawDistanceMode == 3) _cfg.drawDistanceCustom = _drawTrialCustom;
                            _drawDistanceReason = "automatic-completed";
                            WriteDrawDistanceEvent("TrialCompleted", _drawDistanceReason, -1, _cfg.drawDistanceMode, _drawDistanceTrial.TotalUsefulSeconds);
                            _log.Log("[distance] Automatic comparison completed; restoring " + RequestedDistanceName(_cfg.drawDistanceMode) + ".");
                        }
                        else _drawDistanceReason = "automatic-phase";
                    }
                }
            }
            _drawDistanceTimerFrame = frame;
            int requested = RequestedDrawDistance;
            if (_drawDistanceApplied && _drawDistanceMode == requested && _drawDistanceAppliedBoundary == _drawDistanceBoundary &&
                (requested != 3 || _drawDistanceAppliedCustom == _cfg.drawDistanceCustom)) return;
            _drawDistanceMode = requested; _drawDistanceApplied = true; _drawDistanceAppliedBoundary = _drawDistanceBoundary;
            _drawDistanceAppliedCustom = DrawDistanceOptions.SanitizeCustom(_cfg.drawDistanceCustom);
            ++_drawDistanceSegment; _drawDistanceChangedFrame = frame; _drawDistanceChangedUtc = DateTime.UtcNow.ToString("o");
            UpdateDrawDistanceEffective(_attachedCam);
            ResetPerformanceWindow(); _consecutiveStereoFrames = 0; _lastGpuTimestamp = 0;
            if (EffectiveEyeAa == EyeAaMode.Taa) RequestEyeAaHistoryReset();
            WriteDrawDistanceEvent(_drawDistanceTrial.Running ? "PhaseStart" : "SelectionApplied", _drawDistanceReason,
                _drawDistanceTrial.Running ? _drawDistanceTrial.Phase : -1, requested, 0);
            _log.Log("[distance] Segment " + _drawDistanceSegment + ": " + AppliedDistanceName +
                "; far=" + _drawDistanceEffectiveFar.ToString("0.##") + "; both eyes, unchanged AA/resolution.");
        }

        // Called for each eye after a single shared ApplyPendingDrawDistance in Runner.LateUpdate.
        // It never observes GUI/config changes that arrive after that boundary.
        internal static float EyeFarClip(float near, float originalFar)
        {
            int mode = !InSpaceCombat && !InNavigationMap && _drawDistanceApplied ? _drawDistanceMode : 0;
            float far = DrawDistanceOptions.FarClip(mode, near, originalFar, _drawDistanceAppliedCustom);
            far = Mathf.Max(DrawDistanceOptions.SanitizeNear(near) + .01f, ApplyAdaptiveFar(far));
            _drawDistanceNear = DrawDistanceOptions.SanitizeNear(near);
            _drawDistanceOriginalFar = originalFar; _drawDistanceEffectiveFar = far;
            return far;
        }
        static void UpdateDrawDistanceEffective(Camera source)
        {
            if (source == null) return;
            float near = DrawDistanceOptions.SanitizeNear(Mathf.Min(source.nearClipPlane, Mathf.Max(0.01f, WorldScale * 0.02f)));
            EyeFarClip(near, source.farClipPlane);
        }

        static void WriteDrawDistanceEvent(string eventName, string reason, int phase, int mode, double usefulSeconds)
        {
            if (!DiagnosticsRecording) return;
            _drawDistanceLastEvent = eventName;
            try
            {
                var record = new { Session = _diagnosticSession, Version = BuildTag, Utc = DateTime.UtcNow.ToString("o"),
                    Event = eventName, Reason = reason, Segment = _drawDistanceSegment, UnityFrame = Time.frameCount,
                    Run = _drawDistanceTrial.Run, Phase = phase < 0 ? (int?)null : phase + 1,
                    Mode = RequestedDistanceName(mode), ModeId = mode,
                    AppliedMode = _drawDistanceApplied ? AppliedDistanceName : null,
                    CustomSelection = _cfg.drawDistanceCustom,
                    RequestedFarClip = DrawDistanceOptions.Cap(mode, _cfg.drawDistanceCustom) > 0 ? (float?)DrawDistanceOptions.Cap(mode, _cfg.drawDistanceCustom) : null,
                    OriginalFarClip = DrawDistanceOptions.Finite(_drawDistanceOriginalFar) ? (float?)_drawDistanceOriginalFar : null,
                    EffectiveFarClip = _drawDistanceApplied ? (float?)_drawDistanceEffectiveFar : null,
                    EyeNearClip = _drawDistanceApplied ? (float?)_drawDistanceNear : null,
                    UsefulSeconds = Math.Round(usefulSeconds, 3), PhaseUsefulSeconds = DrawDistanceTrial.PhaseSeconds,
                    TotalUsefulSeconds = Math.Round(_drawDistanceTrial.TotalUsefulSeconds, 3),
                    PreviousSelection = RequestedDistanceName(_drawDistanceTrial.PreviousMode),
                    Antialiasing = EyeAaOptions.Name(EffectiveEyeAa), Width = OpenXR.Width, Height = OpenXR.Height,
                    WarmupFrames = 16, TemporaryPhasesPersisted = false };
                File.AppendAllText(Path.Combine(_modFolder, "cambios-distancia-" + _diagnosticSession + ".jsonl"),
                    Newtonsoft.Json.JsonConvert.SerializeObject(record) + Environment.NewLine);
            }
            catch (Exception e)
            {
                // Diagnostics must never interrupt preparing or submitting the two eye views.
                try { _log.Error("[distance] Change log unavailable: " + e.Message); } catch { }
            }
        }

        static object DrawDistanceSnapshot() => new {
            Adaptive = AdaptiveDistanceSnapshot(),
            Segment = _drawDistanceSegment, Applied = _drawDistanceApplied,
            Mode = _drawDistanceApplied ? AppliedDistanceName : null,
            Requested = RequestedDistanceName(RequestedDrawDistance), ManualSelection = RequestedDistanceName(_cfg.drawDistanceMode),
            CustomSelection = _cfg.drawDistanceCustom, AppliedCustomSelection = _drawDistanceAppliedCustom,
            AppliedFrame = _drawDistanceChangedFrame, AppliedUtc = _drawDistanceChangedUtc,
            RequestedFarClip = _drawDistanceApplied && DrawDistanceOptions.Cap(_drawDistanceMode, _drawDistanceAppliedCustom) > 0
                ? (float?)DrawDistanceOptions.Cap(_drawDistanceMode, _drawDistanceAppliedCustom) : null,
            EffectiveFarClip = _drawDistanceApplied ? (float?)_drawDistanceEffectiveFar : null,
            OriginalFarClip = DrawDistanceOptions.Finite(_drawDistanceOriginalFar) ? (float?)_drawDistanceOriginalFar : null,
            EyeNearClip = _drawDistanceApplied ? (float?)_drawDistanceNear : null,
            Automatic = _drawDistanceTrial.Running, Run = _drawDistanceTrial.Run,
            Phase = _drawDistanceTrial.Running ? (int?)(_drawDistanceTrial.Phase + 1) : null,
            UsefulPhaseSeconds = Math.Round(_drawDistanceTrial.UsefulSeconds, 3), PhaseTargetSeconds = DrawDistanceTrial.PhaseSeconds,
            TotalUsefulSeconds = Math.Round(_drawDistanceTrial.TotalUsefulSeconds, 3),
            LastEvent = _drawDistanceLastEvent, StartArmed = _drawDistanceStartGate.Armed,
            StartDelayRemainingSeconds = Math.Round(_drawDistanceStartGate.RemainingSeconds, 3), Activation = "UMM panel button",
            WarmupFrames = 16, PartialWindowDiscardedOnChange = true, TemporaryPhasesPersisted = false
        };

        static void StopDrawDistanceComparison(bool preservePendingStart = false)
        {
            CancelDrawDistanceTrial("lifecycle-stop");
            if (!preservePendingStart) CancelDrawDistanceStart("vr-stopped");
            _drawDistanceStartGate.ResetDelay(); _drawDistanceStartFrame = -1;
            _drawDistanceApplied = false; _drawDistanceChangedFrame = -1; _drawDistanceTimerFrame = -1;
            ++_drawDistanceBoundary; _drawDistanceReason = "attachment-selection";
        }
    }
}
