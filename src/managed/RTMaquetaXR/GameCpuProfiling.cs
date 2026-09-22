using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityModManagerNet;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        enum GameCpuPhase { GameTick, CameraRigUpdate, EventSystemUpdate, UiRaycastAll, CanvasLayoutAndGraphics, ModManagerOnGui, DiagnosticReport,
            GameTickInternal, GameModeTick, GameCommands, CharacterAtlas, FxPrewarm, LoadingProcess, SoundState, RealTimeTick, ControllerDispatch,
            StereoCull, StereoSubmit, StereoBuildAndExecute, PreparationVisualAdd, PreparationVisualRemove, TutorialShow, TutorialHide,
            TurnEnterTb, TurnBeginPreparation, SelectionUpdateUnits, ActionBarUnitChanged, ActionBarUpdateSlots,
            ActionBarAbilities, ActionBarConsumables, ActionBarWeapons, ActionBarMomentum, UiKeyboardBind, UiButtonClick, UiMultiButtonClick, UiWidgetCacheReset,
            TouchInput, TouchGroupInput, TouchGroupSend, TouchCameraFocus, CombatVisuals,
            RadialCatalogue, RadialAbilityInformation, RadialActorInformation, RadialNativeStatus,
            NativeInitiative, NativeTurnAdvance, NativeTurnSelection, NativeTurnValidation,
            NativeAbilityProcess, NativeUnitUiInitialize, NativeActionBarVisibility, NativeNetworkTurnUi }
        struct GameCpuScope { public int Frame, Phase; public long Started; }
        struct CpuPhaseTotals
        {
            public double GameTick, CameraRigUpdate, EventSystemUpdate, UiRaycastAll;
            public double CanvasLayoutAndGraphics, ModManagerOnGui, DiagnosticReport;
            public double GameTickInternal, GameModeTick, GameCommands, CharacterAtlas, FxPrewarm, LoadingProcess, SoundState, RealTimeTick, ControllerDispatch;
            public int TickInternalInvocations, ControllerInvocations;
            public string SlowestController;
            public double SlowestControllerMs, ControllerObservationCpuMs;
            public double StereoCull, StereoSubmit, StereoBuildAndExecute;
            public double PreparationVisualAdd, PreparationVisualRemove, TutorialShow, TutorialHide;
            public double TurnEnterTb, TurnBeginPreparation, SelectionUpdateUnits, ActionBarUnitChanged, ActionBarUpdateSlots;
            public double ActionBarAbilities, ActionBarConsumables, ActionBarWeapons, ActionBarMomentum, UiKeyboardBind, UiButtonClick, UiMultiButtonClick, UiWidgetCacheReset;
            public double TouchInput, TouchGroupInput, TouchGroupSend, TouchCameraFocus, CombatVisuals;
            public double RadialCatalogue, RadialAbilityInformation, RadialActorInformation, RadialNativeStatus;
            public double NativeInitiative, NativeTurnAdvance, NativeTurnSelection, NativeTurnValidation,
                NativeAbilityProcess, NativeUnitUiInitialize, NativeActionBarVisibility, NativeNetworkTurnUi;
        }
        struct PerformancePose
        {
            public bool Available;
            public float X, Y, Z, Qx, Qy, Qz, Qw;
        }
        struct PerformanceContext
        {
            public int Frame, OutputWidth, OutputHeight, RequestedNeuralMode, AppliedNeuralMode, Preset, EyeAa, OverlayPage;
            public long UtcTicks, SpatialContextRevision, SpaceCameraFrames;
            public ulong NeuralGeneration;
            public float RequestedInternalScale, AppliedInternalScale, WorldScale, DrawDistance;
            public bool Stereo, Focused, CombatKnown, InCombat, ModManagerOpen, VisibleCull, IndirectCull, PointerCache;
            public bool TutorialVisible, PreparationKnown, PreparationTurn, SpaceCombat, NavigationMap, SpaceCameraPending;
            public bool TouchGroupMoving, TouchGroupStarted, TouchCameraFollow, RadialVisible, NativeWindowOpen;
            public bool TurnOwnerKnown, EnemyTurn;
            public string Mode, SpatialDomain, NativeAreaMode, OverlayMenu, OverlayOption;
            public PerformancePose GameCamera, LeftEye;
        }
        struct PerformanceHitch
        {
            public double FrameIntervalMs, ThresholdMs, CurrentXrWaitMs, CurrentPreviousSubmissionWaitMs;
            public int Gen0CollectionsSincePreviousFrame, PreviousWorkFrame;
            public CpuPhaseTotals PreviousFrameCpuWallMs, CurrentFrameCpuWallMs;
            public ModStageFrames79.Evidence PreviousFrameModCpu79, CurrentFrameModCpu79;
            public PerformanceContext PreviousContext, CurrentContext;
            public EngineLoopFrameEvidence PreviousFrameEngineLoopCpu, CurrentFrameEngineLoopCpu;
            // Value copies of measurements already read in Runner.Update. They
            // retain serials so a runtime stall cannot be attributed by window
            // coincidence to a different Unity frame. No extra native polling.
            public XrBeginTiming CurrentNativeBegin;
            public XrRenderTiming LastCompletedNativeRender;
        }
        static Func<object,bool> _performanceEnemyUnit;
        struct PerformanceEvent
        {
            public string Cause;
            public PerformanceContext Before, After;
        }
        const int CpuPhaseCount = (int)GameCpuPhase.NativeNetworkTurnUi + 1, CpuFrameSlots = 4, MaxPerformanceEvents = 12;
        static readonly Dictionary<MethodBase, GameCpuPhase> _gameCpuMethods = new Dictionary<MethodBase, GameCpuPhase>();
        static readonly Dictionary<string, string> _gameCpuHookStatus = new Dictionary<string, string>();
        static readonly Measurement[] _gameCpuMeasurements = new Measurement[CpuPhaseCount];
        static readonly Measurement _cpuEvidenceCaptureMs = new Measurement();
        static readonly int[] _gameCpuFrames = { -1, -1, -1, -1 };
        static readonly double[,] _gameCpuTimes = new double[CpuFrameSlots, CpuPhaseCount];
        static readonly int[,] _gameCpuCalls = new int[CpuFrameSlots, CpuPhaseCount];
        static readonly Type[] _gameCpuSlowestControllers = new Type[CpuFrameSlots];
        static readonly double[] _gameCpuSlowestControllerMs = new double[CpuFrameSlots], _gameCpuControllerObservationMs = new double[CpuFrameSlots];
        static readonly Measurement _controllerObservationMs = new Measurement();
        static ControllerTickDispatch _gameControllerDispatch;
        static MethodInfo _gameControllerTickMethod;
        static bool _gameControllerDispatchHook;
        static readonly BoundedPerformanceSamples<PerformanceHitch> _performanceHitches = new BoundedPerformanceSamples<PerformanceHitch>(8);
        static readonly List<PerformanceEvent> _performanceEvents = new List<PerformanceEvent>(MaxPerformanceEvents);
        static int _gameCpuOwnerThread, _performanceHitchCount, _performanceEventCount, _performanceGen0;
        static bool _gameCpuFailed, _havePerformanceContext;
        static string _gameCpuError, _combatReadError;
        static float _performanceContextRetryAt;
        static PerformanceContext _previousPerformanceContext;
        static Func<object> _readPerformanceGame;
        static Func<bool> _hasPerformanceGame;
        static Func<object, object> _readPerformancePlayer;
        static Func<object, bool> _readPerformanceCombat;
        static Func<object, object> _readPerformanceTurn;
        static Func<object, bool> _readPerformancePreparation;

        static void InstallGameCpuProfiling()
        {
            _gameCpuOwnerThread = Thread.CurrentThread.ManagedThreadId;
            _gameCpuFailed = false; _gameCpuError = null; _performanceContextRetryAt = 0;
            var game = AccessTools.TypeByName("Kingmaker.Game");
            var rig = AccessTools.TypeByName("Kingmaker.View.CameraRig");
            InstallGameCpuMethod(game, "Tick", Type.EmptyTypes, GameCpuPhase.GameTick);
            InstallGameCpuMethod(rig, "Update", Type.EmptyTypes, GameCpuPhase.CameraRigUpdate);
            InstallGameCpuMethod(typeof(EventSystem), "Update", Type.EmptyTypes, GameCpuPhase.EventSystemUpdate);
            InstallGameCpuMethod(typeof(EventSystem), "RaycastAll", new[] { typeof(PointerEventData), typeof(List<RaycastResult>) }, GameCpuPhase.UiRaycastAll);
            InstallGameCpuMethod(typeof(CanvasUpdateRegistry), "PerformUpdate", Type.EmptyTypes, GameCpuPhase.CanvasLayoutAndGraphics);
            InstallGameCpuMethod(typeof(UnityModManager.UI), "OnGUI", Type.EmptyTypes, GameCpuPhase.ModManagerOnGui);
            InstallGameCpuMethod(game, "TickInternal", new[] { typeof(bool) }, GameCpuPhase.GameTickInternal);
            InstallGameCpuMethod(AccessTools.TypeByName("Kingmaker.GameModes.GameMode"), "Tick", Type.EmptyTypes, GameCpuPhase.GameModeTick);
            InstallGameCpuMethod(AccessTools.TypeByName("Kingmaker.GameCommands.GameCommandQueue"), "Tick", Type.EmptyTypes, GameCpuPhase.GameCommands);
            InstallGameCpuMethod(AccessTools.TypeByName("Kingmaker.Visual.CharacterSystem.CharacterAtlasService"), "Update", Type.EmptyTypes, GameCpuPhase.CharacterAtlas);
            InstallGameCpuMethod(AccessTools.TypeByName("Kingmaker.Visual.FX.FXPrewarmService"), "Update", Type.EmptyTypes, GameCpuPhase.FxPrewarm);
            InstallGameCpuMethod(AccessTools.TypeByName("Kingmaker.EntitySystem.Persistence.LoadingProcess"), "Tick", Type.EmptyTypes, GameCpuPhase.LoadingProcess);
            InstallGameCpuMethod(AccessTools.TypeByName("Kingmaker.Visual.Sound.SoundState"), "Update", Type.EmptyTypes, GameCpuPhase.SoundState);
            InstallGameCpuMethod(AccessTools.TypeByName("Kingmaker.Controllers.RealTimeController"), "Tick", Type.EmptyTypes, GameCpuPhase.RealTimeTick);
            // Cold lifecycle boundaries distinguish green preparation effects
            // and tutorial construction from the ordinary game/render tick.
            var turnType = AccessTools.TypeByName("Kingmaker.Controllers.TurnBased.TurnController");
            var tutorialType = AccessTools.TypeByName("Kingmaker.UI.MVVM.VM.Tutorial.TutorialVM");
            var tutorialData = AccessTools.TypeByName("Kingmaker.Tutorial.TutorialData");
            InstallGameCpuMethod(turnType,"AddPreparationTurnVisualEffect",Type.EmptyTypes,GameCpuPhase.PreparationVisualAdd);
            InstallGameCpuMethod(turnType,"RemovePreparationTurnVisualEffect",Type.EmptyTypes,GameCpuPhase.PreparationVisualRemove);
            InstallGameCpuMethod(tutorialType,"ShowTutorialInternal",new[]{tutorialData},GameCpuPhase.TutorialShow);
            InstallGameCpuMethod(tutorialType,"HideTutorial",new[]{tutorialData},GameCpuPhase.TutorialHide);
            // Five exact native boundaries identified in the 0.1.34 hitch trace.
            // Observe synchronous work only; no controller scheduling, UI
            // notifications, turn state, commands or gameplay are changed.
            InstallGameCpuMethod(turnType,"EnterTb",Type.EmptyTypes,GameCpuPhase.TurnEnterTb);
            InstallGameCpuMethod(turnType,"BeginPreparationTurn",new[]{typeof(bool)},GameCpuPhase.TurnBeginPreparation);
            InstallGameCpuMethod(AccessTools.TypeByName("Kingmaker.Controllers.SelectionCharacterController"),
                "UpdateSelectedUnits",new[]{typeof(bool)},GameCpuPhase.SelectionUpdateUnits);
            var actionBarType=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.ActionBar.Surface.SurfaceActionBarVM");
            InstallGameCpuMethod(actionBarType,"OnUnitChanged",Type.EmptyTypes,GameCpuPhase.ActionBarUnitChanged);
            InstallGameCpuMethod(actionBarType,"UpdateSlotsCommandHandler",new[]{typeof(bool)},GameCpuPhase.ActionBarUpdateSlots);
            // The original combat stalls predate our widgets. Attribute native
            // turn advancement, ability processing and unit-UI initialization
            // separately, without skipping or rescheduling any gameplay calls.
            InstallGameCpuMethod(turnType,"TryRollInitiative",Type.EmptyTypes,GameCpuPhase.NativeInitiative);
            InstallGameCpuMethod(turnType,"NextTurnTB",Type.EmptyTypes,GameCpuPhase.NativeTurnAdvance);
            InstallGameCpuMethod(turnType,"TrySelectCurrentUnitInUI",Type.EmptyTypes,GameCpuPhase.NativeTurnSelection);
            InstallGameCpuMethod(turnType,"HandleCurrentUnitUnableToAct",Type.EmptyTypes,GameCpuPhase.NativeTurnValidation);
            InstallGameCpuMethod(AccessTools.TypeByName("Kingmaker.Controllers.AbilityExecutionProcess"),"Tick",Type.EmptyTypes,GameCpuPhase.NativeAbilityProcess);
            InstallGameCpuMethod(AccessTools.TypeByName("Kingmaker.UI.Models.UnitSettings.PartUnitUISettings"),"TryToInitialize",Type.EmptyTypes,GameCpuPhase.NativeUnitUiInitialize);
            InstallGameCpuMethod(actionBarType,"UpdateVisibility",Type.EmptyTypes,GameCpuPhase.NativeActionBarVisibility);
            InstallGameCpuMethod(actionBarType,"CheckAnotherPlayerTurn",Type.EmptyTypes,GameCpuPhase.NativeNetworkTurnUi);
            try {
                var turnField=TouchSelectionCallFactory.ExactField(game,"TurnController",turnType);
                _readPerformanceTurn=TouchSelectionCallFactory.FieldGetter(turnField);
                _readPerformancePreparation=(Func<object,bool>)TouchSelectionCallFactory.Build(typeof(Func<object,bool>),
                    TouchSelectionCallFactory.ExactMethod(turnType,"get_IsPreparationTurn",typeof(bool),false));
            } catch { _readPerformanceTurn=null;_readPerformancePreparation=null; }
            InstallGameControllerDispatch();
            InstallNativeUiPerformance();
            // The previous trace cannot separate Touch dispatch, native group
            // submission and camera follow. These five exact boundaries retain
            // the existing bounded sampling and diagnostics OFF gate.
            _gameCpuHookStatus[GameCpuPhase.TouchInput.ToString()] = "EnsureTouchSample first sample per Unity frame, including radial/native menu callbacks; fast cached queries excluded";
            InstallNativeUiSpecialScope(typeof(Main).FullName, "TouchGroupBeforeFill", new[] { typeof(object) }, typeof(void), true, GameCpuPhase.TouchGroupInput);
            InstallNativeUiSpecialScope(typeof(Main).FullName, "SendTouchGroupMovement", new[] { typeof(object), typeof(TouchGroupMoveCommand) }, typeof(void), true, GameCpuPhase.TouchGroupSend);
            InstallNativeUiSpecialScope(typeof(Main).FullName, "UpdateTouchCameraFocus", new[] { typeof(XrFrame), typeof(Vector3), typeof(Quaternion), typeof(Camera) }, typeof(void), true, GameCpuPhase.TouchCameraFocus);
            InstallNativeUiSpecialScope(typeof(Main).FullName, "UpdateCombatVisuals", new[] { typeof(Camera), typeof(Camera) }, typeof(void), true, GameCpuPhase.CombatVisuals);
            InstallNativeUiSpecialScope(typeof(Main).FullName, "RebuildTouchRadialCatalogue", Type.EmptyTypes, typeof(void), true, GameCpuPhase.RadialCatalogue);
            InstallNativeUiSpecialScope(typeof(Main).FullName, "UpdateTouchRadialAbilityInformation", new[] { typeof(int) }, typeof(void), true, GameCpuPhase.RadialAbilityInformation);
            InstallNativeUiSpecialScope(typeof(Main).FullName, "UpdateTouchRadialCombatInformation", new[] { typeof(int) }, typeof(void), true, GameCpuPhase.RadialActorInformation);
            InstallNativeUiSpecialScope(typeof(Main).FullName, "UpdateRadialCombatStatus", Type.EmptyTypes, typeof(void), true, GameCpuPhase.RadialNativeStatus);
            try
            {
                var unitType=AccessTools.TypeByName("Kingmaker.EntitySystem.Entities.MechanicEntity");
                _performanceEnemyUnit=(Func<object,bool>)TouchSelectionCallFactory.Build(typeof(Func<object,bool>),
                    TouchSelectionCallFactory.ExactMethod(unitType,"get_IsPlayerEnemy",typeof(bool),false));
            }
            catch { _performanceEnemyUnit=null; }
            InstallEngineLoopProfiling();
            try
            {
                var instance = AccessTools.PropertyGetter(game, "Instance");
                var hasInstance = AccessTools.PropertyGetter(game, "HasInstance");
                var player = AccessTools.PropertyGetter(game, "Player");
                var combat = AccessTools.PropertyGetter(player?.ReturnType, "IsInCombat");
                var state = AccessTools.Field(game, "State");
                var playerState = state == null ? null : AccessTools.Field(state.FieldType, "PlayerState");
                if (instance == null || !instance.IsStatic || hasInstance == null || !hasInstance.IsStatic || hasInstance.ReturnType != typeof(bool) ||
                    player == null || combat?.ReturnType != typeof(bool) || state == null || playerState?.FieldType != player.ReturnType)
                    throw new MissingMemberException("Game.Instance/Player.IsInCombat");
                var method = new DynamicMethod("RTMaquetaXR_PerformanceGame", typeof(object), Type.EmptyTypes, typeof(Main).Module, true);
                var il = method.GetILGenerator(); il.Emit(OpCodes.Call, instance); il.Emit(OpCodes.Ret);
                _readPerformanceGame = (Func<object>)method.CreateDelegate(typeof(Func<object>));
                _hasPerformanceGame = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), hasInstance);
                // Game.Player dereferences State without a null guard. Loading can
                // legitimately have an instance before a usable player state.
                var readState = ReferenceGetter<object>(game, null, state);
                var readPlayerState = ReferenceGetter<object>(state.FieldType, null, playerState);
                _readPerformancePlayer = instanceValue => {
                    var stateValue = readState(instanceValue);
                    return stateValue == null ? null : readPlayerState(stateValue);
                };
                _readPerformanceCombat = ReferenceGetter<bool>(player.ReturnType, combat, null);
                _combatReadError = null;
            }
            catch (Exception error)
            { _readPerformanceGame = null; _combatReadError = error.Message; }
        }
        static void InstallGameCpuMethod(Type type, string name, Type[] parameters, GameCpuPhase phase)
        {
            try
            {
                var method = type == null ? null : AccessTools.DeclaredMethod(type, name, parameters);
                if (method == null || method.IsStatic || method.ReturnType != typeof(void) || method.GetMethodBody() == null)
                    throw new MissingMethodException((type?.FullName ?? "type missing") + "." + name);
                var prefix = AccessTools.Method(typeof(GameCpuObservationHooks), nameof(GameCpuObservationHooks.Prefix));
                var patches = Harmony.GetPatchInfo(method);
                bool installed = false;
                if (patches != null) foreach (var patch in patches.Prefixes)
                    if (patch.owner == _harmony.Id && patch.PatchMethod == prefix) { installed = true; break; }
                _gameCpuMethods[method] = phase;
                if (!installed) _harmony.Patch(method, prefix: new HarmonyMethod(prefix),
                    finalizer: new HarmonyMethod(typeof(GameCpuObservationHooks), nameof(GameCpuObservationHooks.Finalizer)));
                _gameCpuHookStatus[phase.ToString()] = method.DeclaringType.FullName + "." + method.Name;
            }
            catch (Exception error)
            { _gameCpuHookStatus[phase.ToString()] = "unavailable: " + error.Message; }
        }
        static bool GameCpuProfilingActive => DiagnosticsRecording && !_gameCpuFailed && _active && _cfg.detailedProfiling &&
            Thread.CurrentThread.ManagedThreadId == _gameCpuOwnerThread;
        static void InstallGameControllerDispatch()
        {
            if (_gameControllerDispatchHook) return;
            try
            {
                var contract = AccessTools.TypeByName("Kingmaker.Controllers.Interfaces.IControllerTick");
                _gameControllerTickMethod = AccessTools.Method(contract, "Tick", Type.EmptyTypes);
                _gameControllerDispatch = new ControllerTickDispatch(contract, Stopwatch.GetTimestamp, RecordGameController);
                var target = AccessTools.DeclaredMethod(AccessTools.TypeByName("Kingmaker.GameModes.GameMode"), "Tick", Type.EmptyTypes);
                if (target == null || target.IsStatic || target.ReturnType != typeof(void)) throw new MissingMethodException("GameMode.Tick");
                _harmony.Patch(target, transpiler: new HarmonyMethod(typeof(Main), nameof(GameControllerDispatchTranspiler)));
                _gameControllerDispatchHook = true;
                _gameCpuHookStatus[GameCpuPhase.ControllerDispatch.ToString()] = "GameMode.Tick single IControllerTick.Tick dispatch; no per-controller patches";
            }
            catch (Exception error) { _gameCpuHookStatus[GameCpuPhase.ControllerDispatch.ToString()] = "unavailable: " + error.Message; }
        }
        static IEnumerable<CodeInstruction> GameControllerDispatchTranspiler(IEnumerable<CodeInstruction> source)
        {
            int calls = 0;
            foreach (var instruction in source)
            {
                if (instruction.Calls(_gameControllerTickMethod))
                {
                    // Change only the receiver-compatible call. Labels, catches,
                    // finally blocks, CanTick and controller order remain intact.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(Main), nameof(ProfiledGameControllerTick));
                    ++calls;
                }
                yield return instruction;
            }
            if (calls != 1) throw new InvalidOperationException("Expected exactly one GameMode controller Tick dispatch");
        }
        static void ProfiledGameControllerTick(object controller)
        {
            bool enabled = GameCpuProfilingActive && _attached && !_modeFlat;
            _gameControllerDispatch.Invoke(controller, enabled, enabled ? Time.frameCount : 0);
        }
        static void RecordGameController(int frame, object controller, long started, long ended)
        {
            if (!DiagnosticsRecording) return;
            try
            {
                double elapsed = (ended - started) * 1000.0 / Stopwatch.Frequency;
                RecordGameCpuPhase(frame, (int)GameCpuPhase.ControllerDispatch, elapsed);
                int slot = frame & (CpuFrameSlots - 1);
                if (elapsed > _gameCpuSlowestControllerMs[slot])
                {
                    _gameCpuSlowestControllerMs[slot] = elapsed;
                    _gameCpuSlowestControllers[slot] = controller?.GetType();
                }
                double overhead = (Stopwatch.GetTimestamp() - ended) * 1000.0 / Stopwatch.Frequency;
                _gameCpuControllerObservationMs[slot] += overhead;
                if (_consecutiveStereoFrames > 16) _controllerObservationMs.Add(overhead);
            }
            catch (Exception error) { FailGameCpuProfiling(error); }
        }
        static void GameCpuPrefix(MethodBase __originalMethod, out GameCpuScope __state)
        {
            __state = default;
            if (!GameCpuProfilingActive || !_attached || _modeFlat) return;
            if (_gameCpuMethods.TryGetValue(__originalMethod, out var phase))
                __state = new GameCpuScope { Frame = Time.frameCount, Phase = (int)phase, Started = Stopwatch.GetTimestamp() };
        }
        static Exception GameCpuFinalizer(Exception __exception, GameCpuScope __state)
        {
            if (__state.Started != 0)
                RecordGameCpuPhase(__state.Frame, __state.Phase, (Stopwatch.GetTimestamp() - __state.Started) * 1000.0 / Stopwatch.Frequency);
            // A finalizer observes failures but preserves the original exception.
            return __exception;
        }
        static void RecordGameCpuPhase(int frame, int phase, double elapsedMs)
        {
            if (!DiagnosticsRecording) return;
            try
            {
                int slot = frame & (CpuFrameSlots - 1);
                if (_gameCpuFrames[slot] != frame)
                {
                    _gameCpuFrames[slot] = frame;
                    for (int i = 0; i < CpuPhaseCount; ++i) { _gameCpuTimes[slot, i] = 0; _gameCpuCalls[slot, i] = 0; }
                    _gameCpuSlowestControllers[slot] = null; _gameCpuSlowestControllerMs[slot] = _gameCpuControllerObservationMs[slot] = 0;
                }
                _gameCpuTimes[slot, phase] += elapsedMs;
                ++_gameCpuCalls[slot, phase];
                if (_consecutiveStereoFrames <= 16) return;
                var value = _gameCpuMeasurements[phase];
                if (value == null) _gameCpuMeasurements[phase] = value = new Measurement();
                value.Add(elapsedMs);
            }
            catch (Exception error) { FailGameCpuProfiling(error); }
        }
        // Measures only capture + bounded enqueue on the game thread. The writer
        // reports serialization/file I/O separately; never add its worker time to
        // GameTick, the main-thread measurement, or an individual GPU frame.
        internal static long BeginDiagnosticReportMeasurement() => GameCpuProfilingActive ? Stopwatch.GetTimestamp() : 0;
        internal static void EndDiagnosticReportMeasurement(long started)
        {
            if (started != 0) RecordGameCpuPhase(Time.frameCount, (int)GameCpuPhase.DiagnosticReport,
                (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
        }
        static CpuPhaseTotals GameCpuTotals(int frame)
        {
            int slot = frame & (CpuFrameSlots - 1);
            if (_gameCpuFrames[slot] != frame) return default;
            return new CpuPhaseTotals { GameTick = _gameCpuTimes[slot, 0], CameraRigUpdate = _gameCpuTimes[slot, 1],
                EventSystemUpdate = _gameCpuTimes[slot, 2], UiRaycastAll = _gameCpuTimes[slot, 3],
                CanvasLayoutAndGraphics = _gameCpuTimes[slot, 4], ModManagerOnGui = _gameCpuTimes[slot, 5], DiagnosticReport = _gameCpuTimes[slot, 6],
                GameTickInternal = _gameCpuTimes[slot, 7], GameModeTick = _gameCpuTimes[slot, 8], GameCommands = _gameCpuTimes[slot, 9],
                CharacterAtlas = _gameCpuTimes[slot, 10], FxPrewarm = _gameCpuTimes[slot, 11], LoadingProcess = _gameCpuTimes[slot, 12],
                SoundState = _gameCpuTimes[slot, 13], RealTimeTick = _gameCpuTimes[slot, 14], ControllerDispatch = _gameCpuTimes[slot, 15],
                TickInternalInvocations = _gameCpuCalls[slot, 7], ControllerInvocations = _gameCpuCalls[slot, 15],
                SlowestController = _gameCpuSlowestControllers[slot]?.FullName, SlowestControllerMs = _gameCpuSlowestControllerMs[slot],
                ControllerObservationCpuMs = _gameCpuControllerObservationMs[slot],
                StereoCull = _gameCpuTimes[slot, 16], StereoSubmit = _gameCpuTimes[slot, 17], StereoBuildAndExecute = _gameCpuTimes[slot, 18],
                PreparationVisualAdd=_gameCpuTimes[slot,19],PreparationVisualRemove=_gameCpuTimes[slot,20],
                TutorialShow=_gameCpuTimes[slot,21],TutorialHide=_gameCpuTimes[slot,22],
                TurnEnterTb=_gameCpuTimes[slot,23],TurnBeginPreparation=_gameCpuTimes[slot,24],
                SelectionUpdateUnits=_gameCpuTimes[slot,25],ActionBarUnitChanged=_gameCpuTimes[slot,26],ActionBarUpdateSlots=_gameCpuTimes[slot,27],
                ActionBarAbilities=_gameCpuTimes[slot,28],ActionBarConsumables=_gameCpuTimes[slot,29],ActionBarWeapons=_gameCpuTimes[slot,30],
                ActionBarMomentum=_gameCpuTimes[slot,31],UiKeyboardBind=_gameCpuTimes[slot,32],UiButtonClick=_gameCpuTimes[slot,33],
                UiMultiButtonClick=_gameCpuTimes[slot,34],UiWidgetCacheReset=_gameCpuTimes[slot,35],
                TouchInput=_gameCpuTimes[slot,36],TouchGroupInput=_gameCpuTimes[slot,37],TouchGroupSend=_gameCpuTimes[slot,38],
                TouchCameraFocus=_gameCpuTimes[slot,39],CombatVisuals=_gameCpuTimes[slot,40],
                RadialCatalogue=_gameCpuTimes[slot,41],RadialAbilityInformation=_gameCpuTimes[slot,42],
                RadialActorInformation=_gameCpuTimes[slot,43],RadialNativeStatus=_gameCpuTimes[slot,44],
                NativeInitiative=_gameCpuTimes[slot,45],NativeTurnAdvance=_gameCpuTimes[slot,46],
                NativeTurnSelection=_gameCpuTimes[slot,47],NativeTurnValidation=_gameCpuTimes[slot,48],
                NativeAbilityProcess=_gameCpuTimes[slot,49],NativeUnitUiInitialize=_gameCpuTimes[slot,50],
                NativeActionBarVisibility=_gameCpuTimes[slot,51],NativeNetworkTurnUi=_gameCpuTimes[slot,52] };
        }
        static PerformancePose PerformanceCameraPose(Camera camera)
        {
            if (camera == null) return default;
            var transform = camera.transform; var p = transform.position; var q = transform.rotation;
            return new PerformancePose { Available = true, X = p.x, Y = p.y, Z = p.z, Qx = q.x, Qy = q.y, Qz = q.z, Qw = q.w };
        }
        static PerformanceContext ReadPerformanceContext(bool stereo)
        {
            RefreshSpatialGameContext();
            bool spatial = InSpaceCombat, navigation = InNavigationMap;
            bool combatKnown = false, inCombat = false, preparationKnown=false,preparationTurn=false;
            bool turnOwnerKnown=false,enemyTurn=false;
            if (_readPerformanceGame != null && Time.unscaledTime >= _performanceContextRetryAt)
            {
                try
                {
                    var game = _hasPerformanceGame() ? _readPerformanceGame() : null;
                    var player = game == null ? null : _readPerformancePlayer(game);
                    if (player != null) { inCombat = _readPerformanceCombat(player); combatKnown = true; }
                    // TurnController.Data dereferences Game.Player while the
                    // main menu can legitimately have a game but no player.
                    if(player!=null && _readPerformanceTurn!=null && _readPerformancePreparation!=null) {
                        var turn=_readPerformanceTurn(game);
                        if(turn!=null)
                        {
                            preparationTurn=_readPerformancePreparation(turn);preparationKnown=true;
                            if(inCombat && _presentation!=null && _performanceEnemyUnit!=null)
                            {
                                var unit=_presentation.CurrentUnit(turn);
                                if(unit!=null) { enemyTurn=_performanceEnemyUnit(unit);turnOwnerKnown=true; }
                            }
                        }
                    }
                    _combatReadError = null;
                }
                catch (Exception error) { _combatReadError = error.Message; _performanceContextRetryAt = Time.unscaledTime + 1f; }
            }
            var manager = UnityModManager.UI.Instance;
            return new PerformanceContext {
                Frame = Time.frameCount, UtcTicks = DateTime.UtcNow.Ticks, Mode = _modeName, Stereo = stereo, Focused = Application.isFocused,
                CombatKnown = combatKnown, InCombat = inCombat, ModManagerOpen = manager != null && manager.Opened,
                TurnOwnerKnown=turnOwnerKnown, EnemyTurn=enemyTurn,
                TutorialVisible=_tutorialShowing,PreparationKnown=preparationKnown,PreparationTurn=preparationTurn,
                TouchGroupMoving=_touchGroupMove.Moving, TouchGroupStarted=_touchGroupStartFrame==Time.frameCount,
                TouchCameraFollow=!spatial && !navigation && _cfg.touchThirdPersonFollow, RadialVisible=_touchRadial.Visible, NativeWindowOpen=TouchMenuWindowVisible,
                SpatialContextRevision=_spatialGameRevision, SpaceCameraFrames=_touchSpaceFocusCount,
                SpaceCombat=spatial, NavigationMap=navigation, SpaceCameraPending=spatial && _touchSpaceFocusPending,
                SpatialDomain=spatial ? "SpaceCombat" : InGalacticMap ? "GalacticMap" : navigation ? "StarSystemMap" : "Terrestrial", NativeAreaMode=_spatialGameAreaMode,
                OutputWidth = _eyeW, OutputHeight = _eyeH, RequestedNeuralMode = _cfg.neuralMode, AppliedNeuralMode = _neuralAppliedMode,
                RequestedInternalScale = _cfg.neuralScale, AppliedInternalScale = _neuralAppliedScale,
                NeuralGeneration = NeuralTemporalHook.PerformanceGeneration, Preset = _neuralAppliedPreset,
                EyeAa = (int)EffectiveEyeAa, WorldScale = WorldScale, DrawDistance = _drawDistanceApplied ? _drawDistanceEffectiveFar : -1,
                VisibleCull = _cfg.visibleRegionCulling, IndirectCull = _cfg.indirectVisibleRegionCulling, PointerCache = _cfg.useGamePointerCache,
                OverlayMenu = _liveNavigation.Menu?.Title, OverlayOption = _liveNavigation.Current?.Label, OverlayPage = _liveNavigation.Index,
                GameCamera = PerformanceCameraPose(_attachedCam), LeftEye = PerformanceCameraPose(_runner?.GetEyeL()) };
        }
        static string PerformanceContextChange(PerformanceContext before, PerformanceContext after)
        {
            if(before.TurnOwnerKnown!=after.TurnOwnerKnown || before.EnemyTurn!=after.EnemyTurn) return "native-turn-owner";
            if (before.SpatialDomain != after.SpatialDomain || before.SpatialContextRevision != after.SpatialContextRevision)
                return "spatial-domain/area";
            if (before.SpaceCameraFrames != after.SpaceCameraFrames || before.SpaceCameraPending != after.SpaceCameraPending)
                return "space-camera-initial/explicit-frame";
            if (before.TouchGroupMoving != after.TouchGroupMoving || after.TouchGroupStarted)
                return "touch-group-movement-start/stop";
            if(before.TutorialVisible!=after.TutorialVisible || before.PreparationKnown!=after.PreparationKnown || before.PreparationTurn!=after.PreparationTurn)
                return "tutorial/preparation";
            if (before.Mode != after.Mode || before.Stereo != after.Stereo || before.CombatKnown != after.CombatKnown || before.InCombat != after.InCombat)
                return "game-mode/combat/stereo";
            if (before.OutputWidth != after.OutputWidth || before.OutputHeight != after.OutputHeight || before.NeuralGeneration != after.NeuralGeneration ||
                before.AppliedNeuralMode != after.AppliedNeuralMode || before.AppliedInternalScale != after.AppliedInternalScale || before.Preset != after.Preset ||
                before.EyeAa != after.EyeAa || before.DrawDistance != after.DrawDistance || before.WorldScale != after.WorldScale ||
                before.VisibleCull != after.VisibleCull || before.IndirectCull != after.IndirectCull || before.PointerCache != after.PointerCache)
                return "applied-image/distance/configuration";
            if (before.OverlayMenu != after.OverlayMenu || before.OverlayPage != after.OverlayPage || before.ModManagerOpen != after.ModManagerOpen || before.Focused != after.Focused)
                return "overlay/mod-manager/focus";
            return null;
        }
        static void RecordGameCpuFrameEvidence(bool stereo, bool steady, double seconds)
        {
            if (!GameCpuProfilingActive) { _havePerformanceContext = false; return; }
            long started = Stopwatch.GetTimestamp();
            try
            {
                var current = ReadPerformanceContext(stereo);
                int collections = GC.CollectionCount(0), collectionDelta = Math.Max(0, collections - _performanceGen0);
                if (_havePerformanceContext)
                {
                    string cause = PerformanceContextChange(_previousPerformanceContext, current);
                    if (cause != null)
                    {
                        ++_performanceEventCount;
                        if (_performanceEvents.Count < MaxPerformanceEvents)
                            _performanceEvents.Add(new PerformanceEvent { Cause = cause, Before = _previousPerformanceContext, After = current });
                    }
                }
                double interval = seconds * 1000, threshold = BoundedPerformanceSamples<PerformanceHitch>.HitchThreshold(_lastBeginTiming.predictedPeriodMs);
                if (steady && interval >= threshold && interval <= 60000 && _havePerformanceContext && _previousPerformanceContext.Frame == current.Frame - 1)
                {
                    ++_performanceHitchCount;
                    if (_performanceHitches.WouldKeep(interval))
                        _performanceHitches.Add(interval, new PerformanceHitch { FrameIntervalMs = interval, ThresholdMs = threshold,
                            PreviousWorkFrame = current.Frame - 1, PreviousFrameCpuWallMs = GameCpuTotals(current.Frame - 1),
                            CurrentFrameCpuWallMs = GameCpuTotals(current.Frame), PreviousContext = _previousPerformanceContext, CurrentContext = current,
                            PreviousFrameEngineLoopCpu = EngineLoopFrameSnapshot(current.Frame - 1), CurrentFrameEngineLoopCpu = EngineLoopFrameSnapshot(current.Frame),
                            PreviousFrameModCpu79 = _modStageFrames79.Snapshot(current.Frame - 1), CurrentFrameModCpu79 = _modStageFrames79.Snapshot(current.Frame),
                            Gen0CollectionsSincePreviousFrame = collectionDelta,
                            CurrentNativeBegin = _lastBeginTiming, LastCompletedNativeRender = _lastRenderTiming,
                            CurrentXrWaitMs = _lastBeginTiming.runtimeWaitMs, CurrentPreviousSubmissionWaitMs = _lastBeginTiming.queueWaitMs });
                }
                _previousPerformanceContext = current; _havePerformanceContext = true; _performanceGen0 = collections;
            }
            catch (Exception error) { FailGameCpuProfiling(error); }
            finally
            { if (steady) _cpuEvidenceCaptureMs.Add((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency); }
        }
        static void FailGameCpuProfiling(Exception error)
        {
            _gameCpuFailed = true; _gameCpuError = error.Message;
            _havePerformanceContext = false;
            // Do not emit a logger call from arbitrary instrumented methods.
            // Diagnostics exposes the failure; gameplay keeps its original path.
        }
        static object GameCpuEvidenceSnapshot()
        {
            var phases = new Dictionary<string, object>();
            for (int i = 0; i < CpuPhaseCount; ++i)
            {
                var measurement = _gameCpuMeasurements[i];
                phases[((GameCpuPhase)i).ToString()] = measurement == null ? null : DetailedValue(measurement);
            }
            return new { Enabled = _cfg.detailedProfiling, Failed = _gameCpuFailed, Error = _gameCpuError,
                Hooks = new Dictionary<string, string>(_gameCpuHookStatus), CombatReadError = _combatReadError,
                CpuWallMsPerInvocation = phases,
                EnginePlayerLoop = EngineLoopEvidenceSnapshot(),
                ControllerDispatchHook = _gameControllerDispatchHook,
                ControllerObservationCpuMsPerInvocation = DetailedValue(_controllerObservationMs),
                ControllerObservationScope = "aggregation after the original Tick; excludes gate, interface-dispatch and stopwatch-call overhead; included in GameModeTick",
                ControllerObservationStorageFrames = CpuFrameSlots,
                DiagnosticReportScope = "main-thread snapshot capture and enqueue; excludes worker serialization/file I/O",
                EvidenceCaptureCpuMsPerSteadyFrame = DetailedValue(_cpuEvidenceCaptureMs),
                ScopesMayOverlapDoNotSum = true, GameTickMayIncludeOtherMods = true,
                StereoFrameScopesSumBothViews = true,
                StereoFrameScopesAreCpuWallTime = true, StereoBuildAndExecuteIncludesNestedGraphExecution = true,
                CanvasScopeExcludesNativeBatching = true, MainThreadTimingIncludesPluginAndEngineWaits = true,
                FrameIntervalDescribesPreviousFrameWork = true, CurrentXrWaitIsNotSubtractedFromPreviousFrame = true,
                UnityGpuReadingsAreDelayedAndNotAssignedToHitchFrames = true,
                Gen0CountIsAnIndicatorNotGcDuration = true,
                HitchThreshold = "max(20 ms, 1.5 times the OpenXR display period)",
                HitchCount = _performanceHitchCount, MaximumRetainedHitches = 8, WorstHitches = _performanceHitches.Snapshot(),
                EventCount = _performanceEventCount, MaximumRetainedEvents = MaxPerformanceEvents, Events = _performanceEvents.ToArray(),
                LastContext = _havePerformanceContext ? (object)_previousPerformanceContext : null };
        }
        static void ResetGameCpuWindow()
        {
            ResetEngineLoopWindow();
            for (int i = 0; i < CpuPhaseCount; ++i)
            {
                var value = _gameCpuMeasurements[i];
                if (value != null) ClearDetailedValue(value);
            }
            _performanceHitches.Clear(); _performanceEvents.Clear();
            ClearDetailedValue(_cpuEvidenceCaptureMs);
            ClearDetailedValue(_controllerObservationMs);
            _performanceHitchCount = _performanceEventCount = 0;
        }
    }

    internal static partial class NeuralTemporalHook
    {
        // Read already-known managed state; no native query or allocation per frame.
        internal static ulong PerformanceGeneration => generation;
    }
}

