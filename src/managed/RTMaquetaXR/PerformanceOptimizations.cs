using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _fsrTargetHook, _taaExecutionHook;
        static readonly Dictionary<string, int> _taaExecutions = new Dictionary<string, int>();
        static readonly Dictionary<string, double> _modStageMilliseconds = new Dictionary<string, double>();
        static readonly Dictionary<string, int> _modStageSamples = new Dictionary<string, int>();
        static object _leftCameraBuffer, _rightCameraBuffer;

        static void InstallPerformanceHooks()
        {
            InstallEngineCadenceHooks();
            InstallEngineEffectOptimizations();
            InstallActionBarRefreshOptimizations();
            InstallActionBarSlotReuse();
            InstallEngineOptimizations58();
            InstallEyeSharpnessHook();
            InstallNativeUiWarnings();
            InstallNativeHintPlacement();
            InstallEyeAaHistoryHook();
            InstallForcedVisibilitySync();
            InstallPointerRaycastCache();
            InstallRenderStageHooks();
            InstallDetailedRendering();
            InstallIndirectRenderingHooks();
            if (!_fsrTargetHook)
            {
                try
                {
                    var target = AccessTools.Method(AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.WaaaghPipeline"), "InitializeStackedCameraData");
                    if (target == null) throw new MissingMethodException("InitializeStackedCameraData");
                    _harmony.Patch(target, transpiler: new HarmonyMethod(typeof(Main), nameof(EyeScalingTranspiler)));
                    _fsrTargetHook = true;
                    _log.Log("[performance] Enabled the game's scaling/FSR path for eye render textures");
                }
                catch (Exception e) { _log.Error("[performance] Eye scaling hook: " + e.Message); }
            }
            if (!_taaExecutionHook)
            {
                try
                {
                    var pass = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.Passes.PostProcess.PostProcessPass");
                    MethodInfo target = null;
                    foreach (var nested in pass.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                        foreach (var method in nested.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                            if (method.Name.Contains("<DoTemporalAntialiasing>") && method.GetParameters().Length == 2)
                            {
                                if (target != null) throw new InvalidOperationException("Ambiguous TAA render callback");
                                target = method;
                            }
                    if (target == null) throw new MissingMethodException("TAA render callback");
                    _harmony.Patch(target, postfix: new HarmonyMethod(typeof(Main), nameof(TaaRenderPostfix)));
                    _taaExecutionHook = true;
                }
                catch (Exception e) { _log.Error("[quality] TAA observation: " + e.Message); }
            }
            if (!_desktopMirrorHook)
            {
                RenderPipelineManager.endContextRendering += CopyEyeToDesktop;
                _desktopMirrorHook = true;
            }
            _desktopFallback = false;
            _desktopRecovery.Reset();
            _stereoPreparedFrame = _desktopSkippedFrame = _desktopHandledFrame = -1;
            StartTemporalInputProbe();
            StartNeuralIntegration();
        }

        // The first getter stores the real output texture in CameraData and MUST
        // remain unchanged. Only the second getter belongs to the null-target
        // guard that otherwise excludes every RenderTexture from the FSR path.
        static IEnumerable<CodeInstruction> EyeScalingTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(instructions);
            var getter = AccessTools.PropertyGetter(typeof(Camera), nameof(Camera.targetTexture));
            int count = 0, guard = -1;
            for (int i = 0; i < result.Count; ++i)
                if (Equals(result[i].operand, getter)) { ++count; guard = i; }
            if (count != 2 || guard + 2 >= result.Count || result[guard + 1].opcode != OpCodes.Ldnull ||
                !(result[guard + 2].operand is MethodInfo comparison) || comparison.Name != "op_Equality" ||
                comparison.DeclaringType != typeof(UnityEngine.Object))
                throw new InvalidOperationException("Unexpected eye scaling target guard");
            result[guard].opcode = OpCodes.Call;
            result[guard].operand = AccessTools.Method(typeof(Main), nameof(ScalingGuardTexture));
            return result;
        }
        static RenderTexture ScalingGuardTexture(Camera camera)
        {
            if (_active && GameFsrAllowedForEyes && IsEye(camera)) return null;
            return camera.targetTexture;
        }

        static void TaaRenderPostfix()
        {
            if (!DiagnosticsRecording || !_active || _modeFlat || !IsEye(_renderingCamera)) return;
            string name = _renderingCamera.name;
            _taaExecutions.TryGetValue(name, out int count);
            _taaExecutions[name] = count + 1;
        }

        static void StopPerformanceHooks()
        {
            StopFollowCollision81();
            ReleaseEngineCaches58();
            StopEngineEffectOptimizations();
            StopNativeUiWarnings();
            StopPreparationAura();
            StopNeuralIntegration();
            StopTemporalInputProbe();
            _consecutiveStereoFrames = 0; _stereoMeasurements.Clear(); _currentModStages.Clear();
            ResetRenderStageWindow();
            StopVisibleCulling();
            StopDetailedRendering();
            StopIndirectRendering();
            StopAaComparison();
            StopDrawDistanceComparison();
            _optimizationOptionsReady = false;
            StopForcedVisibility();
            InvalidatePointerRaycastCache();
            if (_desktopMirrorHook) RenderPipelineManager.endContextRendering -= CopyEyeToDesktop;
            _desktopMirrorHook = false;
            _desktopRecovery.Reset(); _desktopFallback = false;
            MonitorHeadsetRecovery81 = false;
            _monitorCopyProbe81=false;ResetMonitorEvidence81();
            _stereoPreparedFrame = _desktopSkippedFrame = _desktopHandledFrame = -1;
        }
        static readonly ModStageFrames79 _modStageFrames79 = new ModStageFrames79();
        internal static void RecordModStage(string name, long started)
        {
            if (started == 0 || !DiagnosticsRecording) return;
            double elapsed = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            _modStageMilliseconds.TryGetValue(name, out double ms);
            _modStageSamples.TryGetValue(name, out int count);
            _modStageMilliseconds[name] = ms + elapsed;
            _modStageSamples[name] = count + 1;
            if (_modStageFrame != Time.frameCount) { _currentModStages.Clear(); _modStageFrame = Time.frameCount; }
            _currentModStages.TryGetValue(name, out double frameMs);
            _currentModStages[name] = frameMs + elapsed;
            _modStageFrames79.Add(Time.frameCount, name, elapsed);
        }
        static object OptimizationSnapshot()
        {
            var stages = new Dictionary<string, double>();
            foreach (var pair in _modStageMilliseconds) stages[pair.Key] = Math.Round(pair.Value / _modStageSamples[pair.Key], 3);
            return new {
                EyeScalingHook = _fsrTargetHook, GameFsrAllowedForEyes = GameFsrAllowedForEyes,
                EyeAA = EyeAaOptions.Name(EffectiveEyeAa), TaaExecutionHook = _taaExecutionHook,
                EyeSharpnessHook = _eyeSharpnessHook, TaaSharpness = _cfg.taaSharpness,
                ForcedVisibility = ForcedVisibilitySnapshot(), PointerRaycast = PointerRaycastSnapshot(),
                Comparison = RuntimeOptimizationSnapshot(),
                DrawDistance = DrawDistanceSnapshot(),
                CombatVisuals = CombatVisualSnapshot(),
                EngineEffects = EngineEffectSnapshot(),
                EngineCadence = EngineCadenceSnapshot(),
                EngineBlocks = EngineOptimizationSnapshot58(),
                ActionBarRefresh = ActionBarRefreshSnapshot(),
                ActionBarSlotReuse = ActionBarSlotReuseSnapshot(),
                PartyHudVisibility = PcHudPartyVisibilitySnapshot(),
                OvertipBatches = OvertipBatchSnapshot(),
                TemporalInputs = TemporalInputProbeSnapshot(),
                MaxQueuedFrames = QualitySettings.maxQueuedFrames,
                Monitor81 = MonitorEvidenceSnapshot81(),
                CameraFocus = TouchCameraFocusSnapshot(),
                PcStartup = TouchPcStartupSnapshot(),
                LoadingStereoFrames = _loadingStereoFrames,
                TouchGuide = TouchGuideSnapshot(),
                NativeHudSelectionFix = NativeHudSelectionFixSnapshot(),
                NativeUiWarnings=NativeUiWarningsSnapshot(),
                NativePetPreparation = NativePetPreparationSnapshot(),
                WorldHud = WorldHudSnapshot(),
                WorldInformation = WorldInformationSnapshot70(),
                Neural = NeuralIntegrationSnapshot(),
                Hud = new {
                    FullResolutionRequested = _cfg.uiFullResolution, RasterMode = _cfg.uiRasterMode,
                    StableRenderSpace = _hudStableSpace, StableRenderFrames = _hudStableFrames, CapturePauses = _hudCapturePauses,
                    CanonicalScale=HudLayoutWorldScale,PhysicalToRenderRatio=_hudRenderRatio,
                    TransformWrites = _hudTransformWrites, TransformWritesAvoided = _hudTransformSkips,
                    RepairEvents = _hudRepairEvents, RepairModeWrites = _hudRepairModeWrites,
                    RepairCameraWrites = _hudRepairCameraWrites, RepairGeometryWrites = _hudRepairGeometryWrites,
                    RepairReports = _hudRepairReports,
                    WorldEventCameraRoutes = _hudWorldCameraRoutes, WorldCanvasCameraWrites = _hudWorldCanvasCameraWrites,
                    PopupBoundsQueries = _hudBoundsQueries, PopupForcedLayouts = _hudBoundsForcedUpdates, PopupLayoutsAvoided = _hudBoundsCoalescedUpdates,
                    PopupLocalLayouts = _hudBoundsLocalLayouts, PopupLocalLayoutFailed = _hudLocalLayoutFailed,
                    WindowWidth = Screen.width, WindowHeight = Screen.height,
                    CaptureWidth = _hudBlackTarget != null ? _hudBlackTarget.width : 0,
                    CaptureHeight = _hudBlackTarget != null ? _hudBlackTarget.height : 0,
                    CaptureFault = _hudCaptureFault, CaptureError = _hudCaptureError,
                    CaptureFar = _hudBlackCamera != null ? _hudBlackCamera.farClipPlane : 0,
                    HierarchyScans = _hudHierarchyCache.Scans, HierarchyInvalidations = _hudHierarchyCache.Invalidations,
                    PartialHierarchyRefreshes = _hudHierarchyCache.PartialRefreshes,
                    ReconciledBranches = _hudReconciledBranches, ReconciledNodes = _hudReconciledNodes,
                    RemovedHierarchyNodes = _hudRemovedNodes,
                    HierarchyTrackedNodes = _hudHierarchyCache.Count, HierarchyAuditNodes = _hudHierarchyAuditNodes,
                    ChildEdgeAudits = _hudChildEdgeAudits, ChildEdgesChecked = _hudChildEdgesChecked,
                    Size = _cfg.uiWidth, OffsetX = _cfg.uiOffsetX, OffsetY = _cfg.uiOffsetY
                },
                UiMetadataScansSinceStart = _uiMetadata.MetadataScans,
                VisibleRegionCulling = VisibleCullingSnapshot(),
                IndirectRendering = IndirectRenderingSnapshot(),
                TaaPassExecutions = new Dictionary<string, int>(_taaExecutions),
                SeparateEyeCameraBuffers = _leftCameraBuffer != null && _rightCameraBuffer != null && !ReferenceEquals(_leftCameraBuffer, _rightCameraBuffer),
                DesktopWorldSkips = _desktopWorldSkips, DesktopCopies = _desktopCopies, DesktopFallback = _desktopFallback,
                DesktopRecovery = new { _desktopRecovery.Reason, _desktopRecovery.Failures, _desktopRecovery.Recoveries,
                    _desktopRecovery.GoodFrames, _desktopRecovery.RetryFrame, CurrentFrameCopiesOnly = true },
                TexturePointerFetchesSinceStart = OpenXR.TexturePointerFetches,
                SrpBatcher = GraphicsSettings.useScriptableRenderPipelineBatching, ModCpuMs = stages
            };
        }
        static void ResetOptimizationWindow()
        {
            ResetForcedVisibilityWindow();
            ResetPointerRaycastWindow();
            _desktopWorldSkips = _desktopCopies = 0;
            _taaExecutions.Clear(); _modStageMilliseconds.Clear(); _modStageSamples.Clear();
        }
    }
}
