using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    [DefaultExecutionOrder(-31000)]
    public sealed class StartupVrGateRunner : MonoBehaviour
    {
        void Update() { Main.TickStartupVrGate(); }
    }

    public static partial class Main
    {
        static readonly StartupVrGatePolicy _startupVrGate = new StartupVrGatePolicy();
        static Harmony _startupVrGateHarmony;
        static GameObject _startupVrGateRunner, _startupVrGateCanvas;
        static Text _startupVrGateTitle, _startupVrGateBody;
        static float _startupVrGateNextPoll;
        static bool _startupVrGatePresented;
        static int _startupVrGateLanguage = -1;
        static string _startupVrGateStatus;

        internal static void InstallStartupVrGate()
        {
            if (_startupVrGateRunner != null) return;
            try
            {
                // Native entrypoint prefixes cover keyboard shortcuts and
                // programmatic menu clicks as well as our graphic ray blocker.
                var menu = AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.MainMenu.MainMenuVM");
                var saves = AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.SaveLoad.SaveLoadVM");
                if (menu == null || saves == null) throw new TypeLoadException("Native startup menu contract unavailable");
                var targets = new List<MethodInfo>();
                foreach (string name in new[] { "ShowNewGameSetup", "LoadLastGame", "LoadLastSave", "EnterGame", "ShowNetLobby", "ShowDlcManager", "OpenSettings", "ShowCredits", "ShowFeedback" })
                {
                    var method = AccessTools.Method(menu, name);
                    if (method == null || method.ReturnType != typeof(void)) throw new MissingMethodException(menu.FullName, name);
                    targets.Add(method);
                }
                foreach (string name in new[] { "RequestSaveOrLoad", "RequestLoad" })
                {
                    var method = AccessTools.Method(saves, name);
                    if (method == null || method.ReturnType != typeof(void)) throw new MissingMethodException(saves.FullName, name);
                    targets.Add(method);
                }
                _startupVrGateHarmony = new Harmony("RTMaquetaXR.StartupVrGate");
                foreach (var method in targets) _startupVrGateHarmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(Main), nameof(StartupVrGateBeforeMenuAction)) { priority = Priority.First });
                _startupVrGateRunner = new GameObject("RTMaquetaXR startup admission");
                UnityEngine.Object.DontDestroyOnLoad(_startupVrGateRunner);
                _startupVrGateRunner.AddComponent<StartupVrGateRunner>();
            }
            catch (Exception error)
            {
                StopStartupVrGate();
                _log.Error("[startup/gate] Native menu protection unavailable: " + error.Message);
            }
        }

        static bool StartupVrGateBeforeMenuAction()
        {
            TickStartupVrGate();
            return !_startupVrGate.Blocking;
        }

        internal static void TickStartupVrGate()
        {
            if (_startupVrGate.Completed || _startupVrGate.Bypassed || _appQuitting)
            {
                DestroyStartupVrGateCanvas();
                if (_startupVrGateRunner != null) UnityEngine.Object.Destroy(_startupVrGateRunner);
                _startupVrGateRunner = null;
                return;
            }
            try
            {
                float now = Time.realtimeSinceStartup;
                if (_active && !_startupVrGatePresented && now >= _startupVrGateNextPoll)
                {
                    _startupVrGateNextPoll = now + .1f;
                    // Initialize() alone is not proof of headset presentation:
                    // wait for the render thread to complete real XR content.
                    OpenXR.RTX_GetStats(out var stats);
                    _startupVrGatePresented = stats.state >= 5 && stats.state <= 6 &&
                        stats.lastSerial != 0 && stats.submittedPairs + stats.flatFrames > 0;
                }
                bool blocked = _startupVrGate.Update(_autoStartArmed || _active,
                    ResumingIntoMainMenu(), _startupVrGatePresented, _appQuitting, now);
                if (!blocked) { DestroyStartupVrGateCanvas(); return; }
                if (_startupVrGateCanvas == null) CreateStartupVrGateCanvas();
                string status = OpenXR.Status ?? "";
                if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.C))
                    GUIUtility.systemCopyBuffer = "RTMaquetaXR " + BuildTag + "\n" + OpenXrRuntime.Name + "\n" + status;
                if (_startupVrGateLanguage != ModLocalization.Revision || status != _startupVrGateStatus)
                {
                    _startupVrGateLanguage = ModLocalization.Revision; _startupVrGateStatus = status;
                    _startupVrGateTitle.text = ModLocalization.Text("PREPARING VR");
                    _startupVrGateBody.text = ModLocalization.Text(OpenXrRuntime.WaitingText) +
                        "\n\n" + ModLocalization.DiagnosticText(status) + "\n\n" + ModLocalization.Text("Ctrl+C: copy startup details");
                }
            }
            catch (Exception error)
            {
                // A failed gate UI must never create an invisible permanent
                // lock. Native VR initialization keeps its ordinary retries.
                _startupVrGate.Bypass(); DestroyStartupVrGateCanvas();
                _log.Error("[startup/gate] Protection released after UI failure: " + error.Message);
            }
        }

        static void CreateStartupVrGateCanvas()
        {
            _startupVrGateCanvas = new GameObject("RTMaquetaXR VR startup", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_startupVrGateCanvas);
            var canvas = _startupVrGateCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.overrideSorting = true; canvas.sortingOrder = 32760;
            var scaler = _startupVrGateCanvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var shade = new GameObject("Input shield", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)shade.transform; rect.SetParent(_startupVrGateCanvas.transform, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            shade.GetComponent<Image>().color = new Color(.015f, .022f, .025f, .98f);
            var font = _liveFont != null ? _liveFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _startupVrGateTitle = StartupGateText("Title", new Vector2(0, 130), new Vector2(1050, 70), 38, font);
            _startupVrGateTitle.color = new Color(.91f, .81f, .54f);
            _startupVrGateBody = StartupGateText("Status", new Vector2(0, 5), new Vector2(1050, 160), 25, font);
            // Admission is automatic after the first submitted XR content.
            // Native startup retries and the saved desktop-only option remain.
            _startupVrGateLanguage = -1;
        }
        static Text StartupGateText(string name, Vector2 position, Vector2 size, int pixels, Font font)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)go.transform; rect.SetParent(_startupVrGateCanvas.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = size;
            var text = go.GetComponent<Text>(); text.font = font; text.fontSize = pixels; text.color = new Color(.9f, .91f, .88f);
            text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 21; text.resizeTextMaxSize = pixels;
            return text;
        }
        static void DestroyStartupVrGateCanvas()
        {
            if (_startupVrGateCanvas == null) return;
            _startupVrGateCanvas.SetActive(false); UnityEngine.Object.Destroy(_startupVrGateCanvas); _startupVrGateCanvas = null;
        }
        internal static void StopStartupVrGate()
        {
            DestroyStartupVrGateCanvas();
            if (_startupVrGateRunner != null) UnityEngine.Object.Destroy(_startupVrGateRunner);
            _startupVrGateRunner = null;
            if (!_appQuitting) _startupVrGateHarmony?.UnpatchAll(_startupVrGateHarmony.Id);
            _startupVrGateHarmony = null;
        }
    }
}
