using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Harmony _touchHarmony;
        static bool _touchHooksReady;
        static object _touchGamePointer;
        static bool _touchWorldTickBlocked;
        static PropertyInfo _touchControllerModeProperty;
        static object _touchControllerOwner, _touchPreviousControllerMode, _touchMouseControllerMode;
        static FieldInfo _touchMouseDownField, _touchMouseDragField, _touchMouseOnField, _touchMouseHandlerField, _touchDragFramesField;
        static FieldInfo _touchPointerDataField, _touchPressedDataField;
        static readonly Dictionary<MethodInfo, MethodInfo> _touchInputReplacements = new Dictionary<MethodInfo, MethodInfo>();

        static void InstallTouchGameInput()
        {
            StopTouchGameInput();
            _touchHarmony = new Harmony("RTMaquetaXR.TouchInput");
            try
            {
                AddTouchReplacement("get_mousePosition", nameof(TouchUnityPosition), Type.EmptyTypes);
                AddTouchReplacement("get_mouseScrollDelta", nameof(TouchUnityScroll), Type.EmptyTypes);
                AddTouchReplacement("get_anyKey", nameof(TouchAnyKey), Type.EmptyTypes);
                AddTouchReplacement("get_anyKeyDown", nameof(TouchAnyKeyDown), Type.EmptyTypes);
                AddTouchReplacement("GetMouseButton", nameof(TouchUnityMouseHeld), new[] { typeof(int) });
                AddTouchReplacement("GetMouseButtonDown", nameof(TouchUnityMouseDown), new[] { typeof(int) });
                AddTouchReplacement("GetMouseButtonUp", nameof(TouchUnityMouseUp), new[] { typeof(int) });
                foreach (string suffix in new[] { "", "Down", "Up" })
                {
                    AddTouchReplacement("GetKey" + suffix, "TouchUnityKey" + suffix, new[] { typeof(KeyCode) });
                    AddTouchReplacement("GetKey" + suffix, "TouchUnityKeyString" + suffix, new[] { typeof(string) });
                }
                int routes = 0;
                foreach (var target in ResolveTouchInputRoutes())
                {
                    if (target.ContainsGenericParameters) throw new InvalidOperationException("Open generic input route: " + target);
                    _touchHarmony.Patch(target, transpiler: new HarmonyMethod(typeof(Main), nameof(TouchGameInputTranspiler))); ++routes;
                }
                var pointer = AccessTools.TypeByName("Kingmaker.Controllers.Clicks.PointerController");
                _touchMouseDownField = TouchField(pointer, "m_MouseDown"); _touchMouseDragField = TouchField(pointer, "m_MouseDrag");
                _touchMouseOnField = TouchField(pointer, "m_MouseDownOn"); _touchMouseHandlerField = TouchField(pointer, "m_MouseDownHandler");
                _touchDragFramesField = TouchField(pointer, "m_DragFrames");
                _touchPointerDataField = TouchField(typeof(PointerInputModule), "m_PointerData");
                _touchPressedDataField = TouchField(typeof(StandaloneInputModule), "m_InputPointerEvents");
                TouchUiCancellation.Configure(AccessTools.TypeByName("Kingmaker.UI.DragNDrop.DragNDropManager"), TouchUiCancellationError);
                var select = AccessTools.Method(pointer, "SelectClickObject");
                if (select == null || select.GetParameters().Length != 4) throw new MissingMethodException("PointerController.SelectClickObject");
                _touchHarmony.Patch(select, transpiler: new HarmonyMethod(typeof(Main), nameof(TouchSelectionRayTranspiler)),
                    postfix: new HarmonyMethod(typeof(Main), nameof(TouchSelectionResult)));
                PatchTouchPrefix(pointer, "Tick", nameof(TouchPointerTick));
                PatchTouchPrefix(pointer, "get_InGui", nameof(TouchInGui));
                PatchTouchPrefix(typeof(StandaloneInputModule), "Process", nameof(TouchUiProcess));
                PatchTouchPrefix(typeof(EventSystem), "Update", nameof(TouchUiProcess));
                InstallTouchRewiredInput();
                InstallTouchSelection();
                InstallTouchGroupMovement();
                InstallTouchCameraFocus();
                InstallTouchRadials();
                InstallTouchExplorationShortcuts();
                InstallNativeHudSelectionFix();
                InstallNativePetPreparationDiagnostics();
                PatchTouchPrefix(AccessTools.TypeByName("Kingmaker.UI.Pointer.CursorController"), "get_CursorPosition", nameof(TouchCursorPosition));
                var game = AccessTools.TypeByName("Kingmaker.Game");
                PatchTouchPrefix(game, "get_IsControllerMouse", nameof(TouchMouseMode));
                PatchTouchPrefix(game, "get_IsControllerGamepad", nameof(TouchGamepadMode));
                var controllerSetter = AccessTools.PropertySetter(game, "ControllerMode");
                if (controllerSetter == null || !controllerSetter.GetParameters()[0].ParameterType.IsEnum) throw new MissingMethodException("Game.ControllerMode");
                _touchHarmony.Patch(controllerSetter, prefix: new HarmonyMethod(typeof(Main), nameof(TouchControllerModePrefixFactory)) { priority = Priority.First });
                InstallTouchCursorVisibility();
                _touchHooksReady = true;
                InstallTouchLocalMap();
                InstallTouchNavigationMaps();
                _touchControllerModeProperty = AccessTools.Property(game, "ControllerMode");
                _touchMouseControllerMode = Enum.Parse(controllerSetter.GetParameters()[0].ParameterType, "Mouse");
                var hasGame = AccessTools.PropertyGetter(game, "HasInstance");
                if (controllerSetter.IsStatic || (hasGame != null && (bool)hasGame.Invoke(null, null)))
                {
                    _touchControllerOwner = controllerSetter.IsStatic ? null : AccessTools.PropertyGetter(game, "Instance").Invoke(null, null);
                    _touchPreviousControllerMode = _touchControllerModeProperty.GetValue(_touchControllerOwner, null);
                    // Run the original setter and its PC-view transition once.
                    // Overriding only the two mode predicates cannot rebuild UI.
                    if (!Equals(_touchPreviousControllerMode, _touchMouseControllerMode))
                        _touchControllerModeProperty.SetValue(_touchControllerOwner, _touchMouseControllerMode, null);
                }
                _log.Log("[touch/input] " + routes + " scoped input routes; real game ray picker; Unity UI input override; Touch only while VR is active.");
            }
            catch (Exception error)
            {
                StopTouchGameInput();
                throw new InvalidOperationException("No se pudo preparar la entrada Touch del juego", error);
            }
        }
        static FieldInfo TouchField(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type?.FullName, name);
        static void PatchTouchPrefix(Type type, string name, string prefix)
        {
            var target = AccessTools.Method(type, name, Type.EmptyTypes) ?? throw new MissingMethodException(type?.FullName, name);
            _touchHarmony.Patch(target, prefix: new HarmonyMethod(typeof(Main), prefix) { priority = Priority.First });
        }
        static DynamicMethod TouchControllerModePrefixFactory(MethodBase original) => BuildTouchControllerModePrefix(original.GetParameters()[0].ParameterType);
        static DynamicMethod BuildTouchControllerModePrefix(Type mode)
        {
            int mouse = Convert.ToInt32(Enum.Parse(mode, "Mouse"));
            var prefix = new DynamicMethod("RTMaquetaXR_TouchMouseUiMode", typeof(void), new[] { mode.MakeByRefType() }, typeof(Main).Module, true);
            prefix.DefineParameter(1, ParameterAttributes.None, "__0");
            var il = prefix.GetILGenerator(); var done = il.DefineLabel();
            il.Emit(OpCodes.Call, AccessTools.PropertyGetter(typeof(Main), nameof(TouchInputOwned)));
            il.Emit(OpCodes.Brfalse_S, done); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4, mouse); il.Emit(OpCodes.Stobj, mode);
            il.MarkLabel(done); il.Emit(OpCodes.Ret); return prefix;
        }
        static void AddTouchReplacement(string original, string replacement, Type[] args)
        {
            var from = AccessTools.Method(typeof(Input), original, args);
            var to = AccessTools.Method(typeof(Main), replacement, args);
            if (from != null && to != null) _touchInputReplacements[from] = to;
        }
        static IEnumerable<CodeInstruction> TouchGameInputTranspiler(IEnumerable<CodeInstruction> source)
        {
            int count = 0;
            foreach (var instruction in source)
            {
                if (instruction.operand is MethodInfo method && _touchInputReplacements.TryGetValue(method, out var replacement))
                { instruction.operand = replacement; ++count; }
                yield return instruction;
            }
            if (count == 0) throw new InvalidOperationException("No matching game Input reads for Touch");
        }
        static IEnumerable<CodeInstruction> TouchSelectionRayTranspiler(IEnumerable<CodeInstruction> source)
        {
            int count = 0;
            var method = AccessTools.Method(typeof(Camera), "ScreenPointToRay", new[] { typeof(Vector3) });
            foreach (var instruction in source)
            {
                if (instruction.Calls(method)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(Main), nameof(TouchSelectionRay)); ++count; }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Unexpected selection ray call count: " + count);
        }
        static Ray TouchSelectionRay(Camera camera, Vector3 position)
        { EnsureTouchSample(); return TouchInputOwned && !InNavigationMap && _touchRayReady ? _touchRay : camera.ScreenPointToRay(position); }
        static void TouchSelectionResult(object __instance, GameObject __1, Vector3 __2, object __3)
        {
            if (!TouchInputOwned || _touchOverUi || !_touchRayReady) return;
            _touchTarget = __1; _touchTargetPoint = __2;
            _touchHasPoint = (__1 != null || __3 != null) && TouchPointerMath.Finite(__2.x) && TouchPointerMath.Finite(__2.y) && TouchPointerMath.Finite(__2.z);
            if (_touchPrimary.Down)
            {
                ++_touchSelections;
                ArmTouchDeepTrigger(__1, __3);
                try { TryStartTouchSelection(__instance, __1, __2, __3); }
                catch (Exception error) { CancelTouchSelection(); _log.Error("[touch/selection-start] " + error.Message); }
            }
        }
        static bool TouchPointerTick(object __instance)
        {
            if (!TouchInputOwned) return true;
            _touchGamePointer = __instance; EnsureTouchSample();
            if (TouchSelectionCaptured) { _touchWorldTickBlocked = true; return false; }
            if (AllowTouchNavigationMapPointer()) { _touchWorldTickBlocked = false; return true; }
            if (!TouchLocalMapOwnsAxes && TouchGameInputAllowed && _touchRayReady && !_modeFlat) { _touchWorldTickBlocked = false; return true; }
            if (!_touchWorldTickBlocked) { CancelTouchWorldGesture(); _touchWorldTickBlocked = true; }
            return false;
        }
        static bool TouchInGui(ref bool __result)
        {
            if (!TouchInputOwned) return true;
            EnsureTouchSample();
            // A map is a flat image with real native objects behind it. The
            // panel intersection is not itself a UI hit: the game's EventSystem
            // decides whether an actual button/list/modal covers this pixel.
            if (TouchNavigationMapPointerAllowed) return true;
            __result = !TouchGameInputAllowed || TouchLocalMapOwnsAxes || _touchOverUi || _modeFlat;
            return false;
        }
        static bool TouchUiProcess()
        {
            if (TouchInputOwned) { EnsureTouchSample(); EnsureTouchUiSource(); }
            return true;
        }
        static bool TouchCursorPosition(ref Vector2 __result)
        { if (!TouchInputOwned) return true; __result = TouchPointerScreen; return false; }
        static bool TouchMouseMode(ref bool __result) { if (!TouchInputOwned) return true; __result = true; return false; }
        static bool TouchGamepadMode(ref bool __result) { if (!TouchInputOwned) return true; __result = false; return false; }

        static Vector3 TouchUnityPosition() => TouchInputOwned ? (Vector3)TouchPointerScreen : Input.mousePosition;
        static Vector2 TouchUnityScroll() => TouchInputOwned ? TouchScroll : Input.mouseScrollDelta;
        static bool TouchAnyKey() { if (!TouchInputOwned) return Input.anyKey; EnsureTouchSample(); return TouchGameInputAllowed && _touchAnyButton.Held; }
        static bool TouchAnyKeyDown() { if (!TouchInputOwned) return Input.anyKeyDown; EnsureTouchSample(); return TouchGameInputAllowed && _touchAnyButton.Down; }
        static bool TouchUnityMouseHeld(int button) => TouchInputOwned ? TouchMouseHeld(button) : Input.GetMouseButton(button);
        static bool TouchUnityMouseDown(int button) => TouchInputOwned ? TouchMouseDown(button) : Input.GetMouseButtonDown(button);
        static bool TouchUnityMouseUp(int button) => TouchInputOwned ? TouchMouseUp(button) : Input.GetMouseButtonUp(button);
        static bool TouchUnityKey(KeyCode key) => TouchInputOwned ? TouchMappedKey(key, 0) : Input.GetKey(key);
        static bool TouchUnityKeyDown(KeyCode key) => TouchInputOwned ? TouchMappedKey(key, 1) : Input.GetKeyDown(key);
        static bool TouchUnityKeyUp(KeyCode key) => TouchInputOwned ? TouchMappedKey(key, 2) : Input.GetKeyUp(key);
        static bool TouchStringKey(string key, int edge) => Enum.TryParse(key, true, out KeyCode code) && TouchMappedKey(code, edge);
        static bool TouchUnityKeyString(string key) => TouchInputOwned ? TouchStringKey(key, 0) : Input.GetKey(key);
        static bool TouchUnityKeyStringDown(string key) => TouchInputOwned ? TouchStringKey(key, 1) : Input.GetKeyDown(key);
        static bool TouchUnityKeyStringUp(string key) => TouchInputOwned ? TouchStringKey(key, 2) : Input.GetKeyUp(key);

        static void CancelTouchGameGestures()
        {
            ClearTouchCameraPendingFocus();
            StopTouchGroupMovement();
            CancelTouchWorldGesture();
            CancelTouchUiModule(_touchModule);
        }
        static void TouchUiCancellationError(string message) => _log.Error(message);
        static void CancelTouchUiModule(BaseInputModule module)
        {
            ResetTouchUiPress();
            if (module == null || _touchPointerDataField == null || _touchPressedDataField == null) return;
            if (module is StandaloneInputModule)
            {
                ClearTouchUiDictionary(_touchPointerDataField.GetValue(module) as Dictionary<int, PointerEventData>);
                ClearTouchUiDictionary(_touchPressedDataField.GetValue(module) as Dictionary<int, PointerEventData>);
            }
            else if (IsTouchRewiredModule(module))
                TouchUiCancellation.CancelRewiredMousePointers(_touchRewiredPointerData.GetValue(module));
        }
        static void CancelTouchWorldGesture()
        {
            CancelTouchSelection();
            ClearTouchWorldPointerPress();
        }
        static void ClearTouchWorldPointerPress()
        {
            if (_touchGamePointer != null && _touchMouseDownField != null)
            {
                _touchMouseDownField.SetValue(_touchGamePointer, false); _touchMouseDragField.SetValue(_touchGamePointer, false);
                _touchMouseOnField.SetValue(_touchGamePointer, null); _touchMouseHandlerField.SetValue(_touchGamePointer, null);
                _touchDragFramesField.SetValue(_touchGamePointer, 0);
            }
        }
        static void ClearTouchUiDictionary(Dictionary<int, PointerEventData> data)
        {
            if (data == null) return;
            for (int id = -1; id >= -3; --id)
                if (data.TryGetValue(id, out var pointer))
                    TouchUiCancellation.Cancel(pointer);
        }
        static void StopTouchGameInput()
        {
            _touchUiContext.Reset();
            StopTouchExplorationShortcuts();
            StopSpatialUi();
            StopTouchLocalMap();
            StopTouchNavigationMaps();
            StopTouchRadials();
            ResetTouchCameraFocus();
            StopNativePetPreparationDiagnostics();
            ResetTouchDrawDistance();
            ResetOverlayTouchPointer();
            StopTouchGroupMovement();
            ResetTouchOverlayChord();
            CancelTouchGameGestures(); _touchHooksReady = false;
            if (!_appQuitting && _touchPreviousControllerMode != null && _touchControllerModeProperty != null)
            {
                try
                {
                    if (Equals(_touchControllerModeProperty.GetValue(_touchControllerOwner, null), _touchMouseControllerMode))
                        _touchControllerModeProperty.SetValue(_touchControllerOwner, _touchPreviousControllerMode, null);
                }
                catch (Exception error) { _log.Error("[touch/input] Restore controller UI mode: " + error.Message); }
            }
            _touchControllerModeProperty = null; _touchControllerOwner = _touchPreviousControllerMode = _touchMouseControllerMode = null;
            _touchPrimary.Cancel(); _touchSecondary.Cancel(); _touchA.Cancel(); _touchB.Cancel();
            _touchGameCancel.Cancel(); _touchGamePause.Cancel(); _touchWorldTickBlocked = false;
            _touchAnyButton.Cancel();
            _touchBox.Reset();
            DestroyTouchSelectionVisuals();
            _touchGameAxesArmed = false;
            _touchX.Cancel(); _touchY.Cancel(); _touchMenu.Cancel(); _touchOverlayTrigger.Cancel();
            _touchFrame = _touchSample = default; _touchSampleUnityFrame = -1;
            _touchSampleValid = _touchRayReady = _touchHasPoint = _touchClickFrozen = false;
            if (_touchModule != null && _touchModule.inputOverride == _touchUiInput) _touchModule.inputOverride = _touchSavedInput;
            if (_touchUiInput != null) UnityEngine.Object.Destroy(_touchUiInput);
            _touchModule = null; _touchUiInput = null; _touchGamePointer = null; _touchUiEvent = null; _touchEventSystem = null;
            if (_touchHarmony != null && !_appQuitting) _touchHarmony.UnpatchAll(_touchHarmony.Id);
            _touchHarmony = null; _touchInputReplacements.Clear(); DestroyTouchPointer(); DestroyTouchHands();
        }
    }
}
