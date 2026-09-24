using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static XrTouchFrame _touchFrame, _touchSample;
        static int _touchSampleUnityFrame = -1;
        static ulong _touchLastSerial;
        static float _touchLastTime;
        static bool _touchSampleValid, _touchOverUi, _touchHasPoint, _touchRayReady, _touchClickFrozen;
        static bool _touchRawRightTrigger, _touchRawLeftTrigger;
        static bool _touchGameAxesArmed;
        static Ray _touchRay, _touchFrozenRay;
        static Vector2 _touchScreen = new Vector2(-4096, -4096), _touchFrozenScreen;
        static Vector3 _touchTargetPoint;
        static GameObject _touchTarget;
        static readonly TouchButtonLatch _touchPrimary = new TouchButtonLatch(), _touchSecondary = new TouchButtonLatch();
        static readonly TouchButtonLatch _touchGameCancel = new TouchButtonLatch(), _touchGamePause = new TouchButtonLatch();
        static readonly TouchButtonLatch _touchAnyButton = new TouchButtonLatch();
        static bool _touchFirstPersonExitConsumed, _touchMenuBackConsumed;
        static readonly TouchButtonLatch _touchA = new TouchButtonLatch(), _touchB = new TouchButtonLatch(),
            _touchX = new TouchButtonLatch(), _touchY = new TouchButtonLatch(), _touchMenu = new TouchButtonLatch(),
            _touchOverlayTrigger = new TouchButtonLatch();
        static readonly TouchAxisRepeat _touchNavVertical = new TouchAxisRepeat(), _touchNavHorizontal = new TouchAxisRepeat();
        static readonly System.Collections.Generic.List<RaycastResult> _touchUiHits = new System.Collections.Generic.List<RaycastResult>(32);
        static readonly System.Collections.Generic.List<Graphic> _touchWorldGraphics68 = new System.Collections.Generic.List<Graphic>(128);
        static bool _touchWorldHit68;
        static RaycastResult _touchWorldResult68;
        internal static bool TouchWorldUiHit68 => _touchWorldHit68;
        static PointerEventData _touchUiEvent;
        static EventSystem _touchEventSystem;
        static BaseInputModule _touchModule;
        static BaseInput _touchSavedInput;
        static TouchUiInput _touchUiInput;
        static long _touchSamples, _touchCancelledGestures, _touchSelections;
        internal static XrTouchFrame CurrentTouchFrame => _touchFrame;
        internal static bool TouchOverlayOpen => _liveNavigation.Visible;
        internal static bool TouchInputOwned => _active && _touchHooksReady;
        internal static bool TouchSampleValid { get { EnsureTouchSample(); return _touchSampleValid; } }
        internal static Vector2 TouchPointerScreen { get { EnsureTouchSample(); return _touchScreen; } }
        internal static Vector2 TouchScroll { get { EnsureTouchSample(); return TouchGameInputAllowed && (!InNavigationMap || TouchMenuWindowVisible) && !TouchLocalMapOwnsAxes && !TouchSelectionCaptured && !TouchDistanceReserved && !TouchGroupHoverScrollBlocked && _touchGameAxesArmed && (_modeFlat || _touchOverUi) ? new Vector2(0, _touchSample.right.stickY * 5f) : Vector2.zero; } }
        static bool TouchGameInputAllowed => _touchSampleValid && !TouchCameraFaulted && !TouchOverlayOpen && !TouchOverlayChordCaptured && !TouchRadialCaptured && !TouchProximityCaptured73 && !TouchManipulationRequested && !_touchExplorationMapFrame && !_touchMenuBackConsumed;
        static bool TouchManipulationRequested => !_modeFlat &&
            (((_touchSample.left.activeControls & 8) != 0 && _touchSample.left.squeeze > .5f) ||
             ((_touchSample.right.activeControls & 8) != 0 && _touchSample.right.squeeze > .5f) ||
             TouchStickCameraRequested(_touchSample));

        internal static void UpdateTouchInput(XrFrame frame)
        {
            if (!TouchInputOwned) return;
            if (OpenXR.GetTouch(out var input)) _touchFrame = input;
            // Button edges are sampled once at the first game/UI query of each
            // Unity frame. Reading a newer pose here never replays an edge.
        }

        static void EnsureTouchSample()
        {
            if (!TouchInputOwned || _touchSampleUnityFrame == Time.frameCount) return;
            // This is the real lazy input work (including native wheel/info
            // callbacks), not UpdateTouchInput's cheap pose poll. Measure once
            // per fresh sample, never on every native input getter.
            long inputStarted = GameCpuProfilingActive ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            int inputFrame = Time.frameCount;
            try
            {
                _touchSampleUnityFrame = Time.frameCount;
                RefreshSpatialGameContext();
                ProcessLiveOverlayKeyboard();
                if (OpenXR.GetTouch(out var newest)) _touchFrame = newest;
                _touchSample = _touchFrame;
                if (_touchSample.serial != _touchLastSerial)
                { _touchLastSerial = _touchSample.serial; _touchLastTime = Time.unscaledTime; }
                bool wasValid = _touchSampleValid;
                _touchSampleValid = _touchSample.ready != 0 && _touchSample.focused != 0 && Application.isFocused &&
                    _touchSample.serial != 0 && Time.unscaledTime - _touchLastTime < .3f &&
                    (_touchSample.right.aimFlags & 3) == 3 && (_touchSample.right.activeControls & 1) != 0;
                bool leftReady = _touchSampleValid && (_touchSample.left.activeControls & 1) != 0;
                ObserveTouchNavigationMapInput();
                _touchA.Step((_touchSample.right.buttons & TouchBindings.PrimaryButton) != 0, _touchSampleValid);
                _touchB.Step((_touchSample.right.buttons & TouchBindings.SecondaryButton) != 0, _touchSampleValid);
                _touchX.Step((_touchSample.left.buttons & TouchBindings.PrimaryButton) != 0, leftReady);
                _touchY.Step((_touchSample.left.buttons & TouchBindings.SecondaryButton) != 0, leftReady);
                _touchMenu.Step((_touchSample.left.buttons & TouchBindings.MenuButton) != 0, leftReady);
                _touchRawRightTrigger = (_touchSample.right.activeControls & 4) != 0 && TouchPointerMath.AnalogPressed(_touchSample.right.trigger, _touchRawRightTrigger);
                _touchRawLeftTrigger = (_touchSample.left.activeControls & 4) != 0 && TouchPointerMath.AnalogPressed(_touchSample.left.trigger, _touchRawLeftTrigger);
                _touchOverlayTrigger.Step(_touchRawRightTrigger, _touchSampleValid);
                bool overlayWasOpen = TouchOverlayOpen;
                try { ProcessTouchOverlayButtons(); }
                catch (Exception error)
                {
                    _liveMessage = "Could not apply setting"; _liveNextTextUpdate = 0;
                    _log.Error("[touch/overlay] " + error);
                }
                if (!_touchB.Held) _touchMenuBackConsumed = false;
                ProcessTouchRadials(overlayWasOpen);
                ProcessTouchLocalMap(overlayWasOpen);
                // A menu-closing B/A/trigger is consumed through release. It must not
                // also cancel a game action or press a control behind the overlay.
                ProcessTouchDrawDistance();
                ProcessTouchExplorationShortcuts(overlayWasOpen);
                if (!_touchB.Held) _touchFirstPersonExitConsumed = false;
                if (_touchB.Down && !overlayWasOpen && !TouchOverlayOpen && !TouchOverlayChordCaptured &&
                    !TouchRadialCaptured && !TouchLocalMapOwnsAxes && !TouchMenuWindowVisible && !_tutorialShowing &&
                    TouchGameInputAllowed && TryExitTouchFirstPerson()) _touchFirstPersonExitConsumed = true;
                bool allowed = TouchGameInputAllowed && !overlayWasOpen;
                // Bind the native source and cancel stale window/selection state
                // before this frame can produce a fresh pointer edge.
                EnsureTouchUiSource();
                ObserveTouchUiContext();
                ProcessTouchManagementBack(overlayWasOpen);
                allowed = TouchGameInputAllowed && !overlayWasOpen;
                ProcessTouchDeepTrigger(allowed);
                SampleTouchSelection(allowed && !TouchLocalMapCompactVisible);
                bool buttonsAllowed = allowed && !TouchSelectionCaptured;
                // Compact map uses explicit stick controls. Its independent spatial
                // plane must never feed an untransformed click into terrain or the
                // hidden service window behind it. B/menu remains native Cancel.
                bool compactMap = TouchLocalMapCompactVisible;
                bool pointerAllowed = buttonsAllowed && !compactMap;
                if (!buttonsAllowed || TouchDistanceReserved || TouchHelpClickOwnsAxes) _touchGameAxesArmed = false;
                else if (Mathf.Abs(_touchSample.right.stickY) < .2f && Mathf.Abs(_touchSample.right.stickX) < .2f) _touchGameAxesArmed = true;
                _touchGameCancel.Step((_touchB.Held && !_touchFirstPersonExitConsumed) || _touchMenu.Held, buttonsAllowed);
                _touchGamePause.Step(_touchX.Held, buttonsAllowed && !compactMap);
                _touchAnyButton.Step(compactMap ? _touchB.Held || _touchMenu.Held : _touchA.Held || (_touchB.Held && !_touchFirstPersonExitConsumed) || _touchX.Held || _touchMenu.Held || _touchRawRightTrigger || _touchRawLeftTrigger, buttonsAllowed);
                bool previouslyPressed = _touchPrimary.Held || _touchSecondary.Held;
                _touchPrimary.Step(TouchGameplayRightTrigger || _touchA.Held, pointerAllowed);
                _touchSecondary.Step(_touchRawLeftTrigger, pointerAllowed);
                if ((!allowed || compactMap) && (previouslyPressed || wasValid != _touchSampleValid))
                { CancelTouchGameGestures(); ++_touchCancelledGestures; _touchClickFrozen = false; }
                // No game hover/picking is needed while the menu or a table gesture
                // owns input. Keep the ordinary UI cursor outside its viewport and
                // avoid our extra RaycastAll until gameplay input is allowed again.
                if (!allowed || compactMap)
                { ResetTouchUiPress(); _touchRayReady = _touchHasPoint = _touchOverUi = false; _touchScreen = new Vector2(-4096, -4096); }
                else { PrepareTouchPointerSample(); ProcessTouchSelectionPointer(); ProcessTouchWeaponComparison(); }
                ++_touchSamples;
            }
            finally
            {
                if (inputStarted != 0) RecordGameCpuPhase(inputFrame, (int)GameCpuPhase.TouchInput,
                    (System.Diagnostics.Stopwatch.GetTimestamp() - inputStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            }
        }

        static void ProcessTouchManagementBack(bool overlayWasOpen)
        {
            if (!_touchB.Down || overlayWasOpen || !TouchGameInputAllowed || TouchLocalMapCompactVisible ||
                TouchMenuBlockingModal()) return;
            // Dispatch one native close, then swallow this B press until release.
            // Do not also feed Escape into the now-exposed parent or world.
            if (!TryCloseTouchMenuWindow()) return;
            _touchMenuBackConsumed = true;
            CancelTouchGameGestures();
            _touchGameCancel.Cancel(); _touchAnyButton.Cancel();
        }

        static void ProcessTouchOverlayButtons()
        {
            bool chordConsumed = ProcessTouchOverlayChord();
            if (!_touchSampleValid || !LiveOverlayVr) { ResetOverlayTouchPointer(); if (TouchOverlayOpen) HideLiveOverlay(); return; }
            if (chordConsumed) { ResetOverlayTouchPointer(); return; }
            bool changed = false;
            if (TouchOverlayOpen)
            {
                // A and right trigger share one gesture. Overlap cannot issue
                // two actions; both controls must be released after a change.
                _touchOverlayConfirm.Step(_touchRawRightTrigger || _touchA.Held, true);
                int vertical = _touchNavVertical.Step(_touchSample.right.stickY, Time.unscaledTime, true);
                int horizontal = _touchNavHorizontal.Step(_touchSample.right.stickX, Time.unscaledTime, true);
                bool navigationUsed = vertical != 0 || horizontal != 0 || _touchB.Down;
                if (vertical != 0) { changed |= _liveNavigation.Select((_livePage - vertical + _liveOptions.Count) % _liveOptions.Count); }
                else if (horizontal != 0) changed |= TurnConfirmationVisible ? _liveNavigation.Select(horizontal > 0 ? 1 : 0) : _liveNavigation.Change(horizontal);
                else if (_touchB.Down) { _liveNavigation.Back(); changed = true; }
                // Compact confirmation and Save have explicit focused buttons.
                // Their titles/blank margins must not swallow A. Other menus
                // retain their established pointed A/trigger interaction.
                bool focusedConfirm = !navigationUsed && (TurnConfirmationVisible || CurrentLiveOption()?.ActionButton == true) &&
                    _touchA.Down && _touchOverlayConfirm.Down;
                bool pointerChanged = false;
                bool pointerConsumed = focusedConfirm || ProcessTouchOverlayPointer(navigationUsed, out pointerChanged);
                if (focusedConfirm) { changed |= _liveNavigation.Execute(); ResetOverlayTouchPointer(); }
                changed |= pointerChanged;
                if (!navigationUsed && !pointerConsumed && _touchOverlayConfirm.Down) changed |= _liveNavigation.Execute();
            }
            else { _touchNavVertical.Clear(); _touchNavHorizontal.Clear(); ResetOverlayTouchPointer(); }
            if (changed) { _liveMessage = null; _liveNextTextUpdate = 0; LogLiveOverlayInput("Touch navigation"); }
        }

        static void EnsureTouchUiSource()
        {
            var es = EventSystem.current;
            var module = es != null ? es.currentInputModule : null;
            // An inactive PC module must receive its input source before its
            // ShouldActivateModule poll, otherwise Touch movement cannot wake it.
            if (module == null && es != null)
                foreach (var candidate in es.GetComponents<BaseInputModule>())
                    if (candidate.isActiveAndEnabled && candidate is StandaloneInputModule) { module = candidate; break; }
            if (module == _touchModule && es == _touchEventSystem) return;
            CancelTouchUiModule(_touchModule);
            if (_touchModule != null && _touchModule.inputOverride == _touchUiInput) _touchModule.inputOverride = _touchSavedInput;
            if (_touchUiInput != null) UnityEngine.Object.Destroy(_touchUiInput);
            _touchModule = module; _touchEventSystem = es; _touchUiEvent = es == null ? null : new PointerEventData(es);
            _touchUiInput = null; _touchSavedInput = null;
            if (module is StandaloneInputModule)
            {
                _touchSavedInput = module.inputOverride;
                _touchUiInput = module.gameObject.AddComponent<TouchUiInput>(); module.inputOverride = _touchUiInput;
                _log.Log("[touch/ui] Own input source attached to " + module.GetType().FullName);
            }
            else if (module != null)
                _log.Log("[touch/ui] Active module " + module.GetType().FullName +
                    (IsTouchRewiredModule(module) ? "; native Rewired mouse source routed to Touch." : "; unsupported input source."));
        }

        static void PrepareTouchPointerSample()
        {
            _touchWorldHit68=false;
            _touchHasPoint = _touchOverUi = false; _touchTarget = null;
            if (_modeFlat || !_attached)
            {
                _touchRayReady = false;
                _touchScreen = new Vector2(-4096, -4096);
                if (OpenXR.GetFlatPanel(out var panel) && panel.valid != 0)
                {
                    var inverse = Quaternion.Inverse(panel.pose.Rotation);
                    Vector3 origin = inverse * (_touchSample.right.aim.Position - panel.pose.Position);
                    Vector3 direction = inverse * (_touchSample.right.aim.Rotation * Vector3.forward);
                    if (TouchPointerMath.PanelPoint(ToPoint(origin), ToPoint(direction), panel.width, panel.height,
                        panel.flipVertical != 0, out float u, out float v))
                    {
                        _touchScreen = new Vector2(u * Screen.width, v * Screen.height);
                        if (ApplyTouchUiPress(true) && TouchPointerMath.InPanel(_touchScreen.x / Screen.width, _touchScreen.y / Screen.height))
                        {
                            _touchOverUi = _touchHasPoint = true;
                            FindTouchUiTarget(false);
                            BeginTouchUiPress(true);
                        }
                    }
                }
                if (!_touchHasPoint) CancelTouchPointerPress();
                return;
            }
            _touchRayReady = TryTouchWorldRay(false, out _touchRay);
            _touchScreen = new Vector2(-4096, -4096);
            if (!_touchRayReady) { CancelTouchPointerPress(); return; }
            // World selection has its own stable scene plane; neither HUD
            // dimensions nor the desktop viewport clip its drag coordinates.
            if (_touchBox.Pending || _touchBox.Active) return;
            if (!TouchSelectionCaptured && !_touchBox.Pending && _touchClickFrozen && (_touchPrimary.Held || _touchPrimary.Up))
            {
                _touchRay = _touchFrozenRay; _touchScreen = _touchFrozenScreen;
                // A world click keeps its route until release; an animated HUD
                // passing in front of the ray cannot steal its up event.
                return;
            }
            if (!_touchBox.Pending) _touchClickFrozen = false;
            Ray hudRay = HudWorldToRenderRay(_touchRay);
            if (_pickCam != null)
            {
                var panel = new Plane(_pickCam.transform.forward,
                    _pickCam.transform.position + _pickCam.transform.forward * HudPickPlaneDistance);
                if (panel.Raycast(hudRay, out float distance))
                {
                    var screen = _pickCam.WorldToScreenPoint(hudRay.GetPoint(distance));
                    if (screen.z > 0) _touchScreen = new Vector2(screen.x, screen.y);
                }
            }
            // Panel windows keep first refusal at the actual panel coordinate.
            if(_pickCam!=null&&IndependentPointer81(hudRay,out var independentPoint))
            {
                var screen=_pickCam.WorldToScreenPoint(independentPoint);
                if(screen.z>0)_touchScreen=new Vector2(screen.x,screen.y);
            }
            // Never retarget a window's captured down/up onto the world behind it.
            FindTouchUiTarget(true);
            bool panelHit=_touchOverUi&&_touchTarget!=null&&!IsWorldHudTransform(_touchTarget.transform);
            if(!panelHit&&(!_touchUiPress.Captured||(_touchUiPressTarget!=null&&IsWorldHudTransform(_touchUiPressTarget.transform)))&&
                !TouchMenuWindowVisible&&!_tutorialShowing&&!TouchOverlayOpen&&!TouchRadialCaptured)
                TryTouchWorldUi68(_touchRay);
            if (!ApplyTouchUiPress(false)) return;
            FindTouchUiTarget(true);
            BeginTouchUiPress(false);
            if (!TouchSelectionCaptured && !_touchBox.Pending && _touchPrimary.Down && !_touchOverUi)
            { _touchFrozenRay = _touchRay; _touchFrozenScreen = _touchScreen; _touchClickFrozen = true; }
            if (_touchClickFrozen && !_touchBox.Pending)
            {
                _touchRay = _touchFrozenRay; _touchScreen = _touchFrozenScreen;
                if (!_touchPrimary.Held && !_touchPrimary.Up) _touchClickFrozen = false;
            }
        }

        // A controller ray is not a camera ray through one fixed HUD plane.
        // Resolve world-overtip rectangles at their own depth first, then feed
        // the resulting native screen coordinate to the existing EventSystem.
        static bool TryTouchWorldUi68(Ray ray)
        {
            long started=DiagnosticTimestamp();
            try { return PickWorldInteraction72(ray); }
            finally { RecordModStage("WorldInteractionPicking70",started); }
        }
        static void TouchWorldRaycastResults68(PointerEventData __0,System.Collections.Generic.List<RaycastResult> __1)
        {
            if(!TouchInputOwned||_modeFlat||!_attached||__0==null||__1==null||(__0.position-_touchScreen).sqrMagnitude>.01f)return;
            // The same raycast result reaches both our hover probe and the
            // game's input module. No synthetic click or gameplay callback.
            // World hits from the fixed panel ray are never accepted.
            for(int i=__1.Count-1;i>=0;i--)
                if(__1[i].gameObject!=null&&IsWorldHudTransform(__1[i].gameObject.transform))__1.RemoveAt(i);
            if(!_touchWorldHit68||_touchWorldResult68.gameObject==null||!_touchWorldResult68.gameObject.activeInHierarchy||
                _touchWorldResult68.module==null||!_touchWorldResult68.module.isActiveAndEnabled||
                (__0.position-_touchScreen).sqrMagnitude>.01f)return;
            __1.Clear();__1.Add(_touchWorldResult68);
        }

        static void FindTouchUiTarget(bool world)
        {
            var es = EventSystem.current;
            if (es != null && (_touchWorldHit68 || (_touchScreen.x >= 0 && _touchScreen.y >= 0 && _touchScreen.x <= Screen.width && _touchScreen.y <= Screen.height)))
            {
                if (_touchUiEvent == null || es != _touchEventSystem) EnsureTouchUiSource();
                if (_touchUiEvent != null)
                {
                    _touchUiEvent.Reset(); _touchUiEvent.position = _touchScreen; _touchUiHits.Clear();
                    es.RaycastAll(_touchUiEvent, _touchUiHits);
                    foreach (var result in _touchUiHits)
                    {
                        if (result.gameObject == null || IsTouchOwnObject(result.gameObject)) continue;
                        _touchOverUi = _touchHasPoint = true; _touchTarget = result.gameObject;
                        if (!world) break;
                        if(_touchWorldHit68 && result.gameObject==_touchWorldResult68.gameObject)
                        { _touchTargetPoint=_touchWorldResult68.worldPosition; break; }
                        var rect = result.gameObject.transform as RectTransform;
                        bool worldHud = IsWorldHudTransform(rect);
                        Ray hitRay = worldHud ? _touchRay : HudWorldToRenderRay(_touchRay);
                        if (rect != null && new Plane(rect.forward, rect.position).Raycast(hitRay, out float d))
                            _touchTargetPoint = worldHud ? hitRay.GetPoint(d) : HudRenderToWorldPoint(hitRay.GetPoint(d));
                        else _touchTargetPoint = _touchRay.GetPoint(WorldScale);
                        break;
                    }
                }
            }
        }

        static Point3 ToPoint(Vector3 value) => new Point3(value.x, value.y, value.z);
        internal static bool TouchMouseHeld(int button) { EnsureTouchSample(); return button == 0 ? _touchPrimary.Held : button == 1 && _touchSecondary.Held; }
        internal static bool TouchMouseDown(int button) { EnsureTouchSample(); return button == 0 ? _touchPrimary.Down : button == 1 && _touchSecondary.Down; }
        internal static bool TouchMouseUp(int button) { EnsureTouchSample(); return button == 0 ? _touchPrimary.Up : button == 1 && _touchSecondary.Up; }
        static bool TouchMappedKey(KeyCode key, int edge)
        {
            EnsureTouchSample();
            if (!TouchGameInputAllowed) return false;
            if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6)
                return edge == 1 ? TouchMouseDown(key - KeyCode.Mouse0) : edge == 2 ? TouchMouseUp(key - KeyCode.Mouse0) : TouchMouseHeld(key - KeyCode.Mouse0);
            TouchButtonLatch latch = key == KeyCode.Space ? _touchGamePause : key == KeyCode.Escape ? _touchGameCancel : null;
            return latch != null && (edge == 1 ? latch.Down : edge == 2 ? latch.Up : latch.Held);
        }
        internal static object TouchInputSnapshot() => new {
            Owned = TouchInputOwned, Ready = _touchSampleValid, NativeReady = _touchFrame.ready, Focused = _touchFrame.focused,
            Serial = _touchFrame.serial, Samples = _touchSamples, UiInput = _touchModule?.GetType().FullName,
            UiHit = _touchOverUi, RayValid = _touchRayReady, Target = _touchTarget != null ? _touchTarget.name : null,
            UiAdapter = _touchModule is StandaloneInputModule ? "Unity BaseInput" : IsTouchRewiredModule(_touchModule) ? "Rewired UnityInputSource" : "unavailable",
            UiPressCaptured = _touchUiPress.Captured, UiPressDragging = _touchUiPress.Dragging,
            UiPresses = _touchUiPresses, UiDrags = _touchUiDrags, UiPressCancellations = _touchUiPressCancellations,
            GroupMovement = TouchGroupMovementSnapshot(),
            Radials = TouchRadialSnapshot(),
            ProximityInteractions = TouchProximitySnapshot73(),
            SpatialUi = SpatialUiSnapshot(), WorldInformation = WorldInformationSnapshot70(), LocalMap = TouchLocalMapSnapshot(),
            SpatialContext = SpatialGameContextSnapshot(), NavigationMaps = TouchNavigationMapSnapshot(), SpaceCamera = SpaceCombatCameraSnapshot(), SpaceRadials = TouchSpaceRadialDiagnostics(), CombatHeadView = TouchCombatHeadSnapshot(),
            MapShortcutOpens = _touchMapOpens, HighlightShortcutToggles = _touchHighlightToggles, HighlightShortcutOwned = _touchHighlightOwner != null,
            PersistentHudHidden = PcHudMinimal, EnlargedServiceWindow = TouchMenuWindowVisible, ServiceWindowTextScale = MenuWindowScale,
            DrawDistanceShortcut = _cfg.touchDrawDistanceShortcut, DrawDistanceReserved = TouchDistanceReserved,
            Flat = _modeFlat || !_attached, FlatPointerOnPanel = (_modeFlat || !_attached) && _touchHasPoint,
            MultipleSelection = _touchBox.Active, SelectionPending = _touchBox.Pending,
            SelectionGesture = "Right trigger click; drag from ground or controllable unit on a stable scene plane",
            MultipleSelectionsCompleted = _touchBoxCompletions, MultipleSelectionsCancelled = _touchBoxCancellations,
            SelectionCandidates = _touchBoxCandidateCount, LastSelectionCandidates = _touchBoxLastSelectedCount,
            SelectionUnitsSubmitted = _touchBoxUnitsSelected, EmptySelectionCompletions = _touchBoxEmptyCompletions,
            CancelledGestures = _touchCancelledGestures, Selections = _touchSelections, InputModel = "Touch only / exact game picking ray / PC UI actions"
        };
        static void CancelTouchPointerPress()
        {
            ResetTouchUiPress();
            CancelTouchSelection();
            if (_touchPrimary.Held || _touchPrimary.Up || _touchSecondary.Held || _touchSecondary.Up)
            { CancelTouchGameGestures(); ++_touchCancelledGestures; }
            _touchPrimary.Cancel(); _touchSecondary.Cancel(); _touchClickFrozen = false;
        }
    }
}
