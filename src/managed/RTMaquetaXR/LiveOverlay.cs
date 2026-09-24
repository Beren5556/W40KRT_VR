using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _liveStarted;
        static readonly LiveOverlayNavigation _liveNavigation = new LiveOverlayNavigation();
        static int _livePage => _liveNavigation.Index;
        static List<OverlayOption> _liveOptions => _liveNavigation.Menu?.Options;
        static Func<string> _liveStatus;
        static GameObject _liveRoot;
        static Canvas _liveCanvas;
        static Camera _liveLeft, _liveRight;
        static Text _liveTitle, _liveLabel, _liveValue, _liveState, _liveHelp, _liveDescription, _liveActionText;
        static readonly Text[] _liveRows = new Text[LiveOverlayLayout.VisibleRows];
        static RectTransform _liveSelection;
        static RectTransform _liveSliderFill;
        static GameObject _liveSlider;
        static Font _liveFont, _liveHeaderFont;
        static Text _liveDecreaseText, _liveIncreaseText, _livePreviousText, _liveNextText;
        static Text _liveCloseHint, _liveDescriptionHeading;
        static readonly System.Collections.Generic.List<ControlIconRun> _liveFlatMeasureRuns=new System.Collections.Generic.List<ControlIconRun>();
        static readonly GUIContent _liveFlatMeasureContent=new GUIContent();
        static int _liveLanguageRevision = -1;
        static GameObject _liveDecreaseFrame, _liveIncreaseFrame;
        static Image _livePrimaryActionFrame;
        static Text _livePrimaryActionText;
        static int _liveFontAttempts;
        static bool _liveFontRetryOnOpen = true;
        static readonly Color LiveBackground = new Color(.025f, .035f, .023f, .98f);
        static readonly Color LiveFrame = new Color(.42f, .36f, .22f, 1f);
        static readonly Color LiveAccent = new Color(.57f, .68f, .35f, 1f);
        static readonly Color LiveInk = new Color(.86f, .87f, .74f, 1f);
        static readonly Color LiveMuted = new Color(.64f, .69f, .55f, 1f);
        static readonly Color LiveSelection = new Color(.10f, .17f, .075f, .76f);
        static Material _liveImageMaterial, _liveFontMaterial;
        static float _liveNextTextUpdate;
        static string _liveMessage;
        static int _liveCreateAttempts, _liveBuiltRevision = -1, _liveCameraLogBudget;
        static float _liveNextCreate;
        static string _liveVisualStatus;
        static int _liveLeftCameraFrame = -1, _liveRightCameraFrame = -1, _liveLabelVertices;
        static int _liveFlatFrame = -1;
        static string _liveFlatError;
        static GUIStyle _liveFlatStyle;
        static string _liveDisplayTitle, _liveDisplayLabel, _liveDisplayValue, _liveDisplayDescription,
            _liveDisplayState, _liveDisplayHelp, _liveDisplayAction;
        static readonly string[] _liveDisplayRows = new string[LiveOverlayLayout.VisibleRows];
        static readonly bool[] _liveDisplayRowEnabled = new bool[LiveOverlayLayout.VisibleRows];
        static bool _liveDisplayEnabled, _liveDisplaySlider;
        static float _liveDisplayFraction;
        static int _liveDisplayValueSize = 36;
        const float LivePanelWidth = LiveOverlayLayout.Width, LivePanelHeight = LiveOverlayLayout.Height;
        static float CurrentLivePanelWidth => TurnConfirmationVisible ? TurnConfirmationLayout.Width : TouchQuickGuideVisible ? TouchQuickGuideLayout.Width : LivePanelWidth;
        static float CurrentLivePanelHeight => TurnConfirmationVisible ? TurnConfirmationLayout.Height : TouchQuickGuideVisible ? TouchQuickGuideLayout.Height : LivePanelHeight;
        const float LiveSliderWidth = LiveOverlayLayout.SliderWidth;
        static bool TryCurrentLiveFlatPlacement(float width, float height, out float x, out float y, out float scale) =>
            TurnConfirmationVisible ? PlaceFlatTurnConfirmation(width,height,out x,out y,out scale) :
            TouchQuickGuideVisible ? TouchQuickGuideLayout.TryFlatPlacement(width, height, out x, out y, out scale) :
                LiveOverlayLayout.TryFlatPlacement(width, height, out x, out y, out scale);

        internal static void StartLiveOverlay(OverlayMenu menu, Func<string> status)
        {
            StopLiveOverlay();
            _liveNavigation.Reset(menu); _liveStatus = status;
            if (menu == null || menu.Options.Count == 0 || !InstallLiveOverlayInput()) return;
            _liveStarted = true;
            _liveFontAttempts = 0; _liveFontRetryOnOpen = true;
            _liveCreateAttempts = 0; _liveNextCreate = _liveNextTextUpdate = 0;
            RenderPipelineManager.beginCameraRendering += LiveOverlayBeginCamera;
            EnsureLiveOverlayVisuals();
        }

        static void EnsureLiveOverlayVisuals()
        {
            if (!_liveStarted || _liveCanvas != null || _liveCreateAttempts >= 8 || Time.unscaledTime < _liveNextCreate) return;
            ++_liveCreateAttempts; _liveNextCreate = Time.unscaledTime + 2;
            try
            {
                CreateLiveOverlay();
                _liveVisualStatus = "created";
                _liveNextTextUpdate = 0;
                _log.Log("[overlay] Ready; both triggers + both grips held 2s opens/closes; stick navigates; A/right trigger enters; B returns; shader=" + _liveImageMaterial.shader.name +
                    "; text shader=" + _liveFontMaterial.shader.name + "; font=" + _liveFont.name);
            }
            catch (Exception error)
            {
                _liveVisualStatus = "creation failed: " + error.Message;
                DestroyLiveOverlayVisuals();
                // Retain Touch navigation while UI resources arrive during loading.
                _log.Error("[overlay] Creation attempt " + _liveCreateAttempts + "/8: " + error.Message);
            }
        }

        static OverlayOption CurrentLiveOption() => _liveNavigation.Current;

        internal static void HideLiveOverlay()
        {
            ResetOverlayTouchPointer();
            HideTouchGuideVisual();
            HideTouchQuickGuide();
            if (_liveNavigation.Visible) _liveNavigation.Close();
            _liveBuiltRevision = -1; _liveNextTextUpdate = 0;
            if (_liveCanvas != null) _liveCanvas.enabled = false;
        }

        internal static void PositionLiveOverlay(Camera left, Camera right)
        {
            if (!_liveStarted) return;
            EnsureLiveOverlayVisuals();
            if (_liveCanvas == null) return;
            if (!LiveOverlayVr) { HideLiveOverlay(); return; }
            if (_modeFlat || !_attached || left == null || right == null) { _liveCanvas.enabled = false; return; }
            _liveLeft = left; _liveRight = right;
            if (CurrentLiveOption() == null) { _liveFontRetryOnOpen = true; _liveCanvas.enabled = false; return; }
            var rotation = Quaternion.Slerp(left.transform.rotation, right.transform.rotation, 0.5f);
            var center = (left.transform.position + right.transform.position) * 0.5f;
            float near = Mathf.Max(left.nearClipPlane, right.nearClipPlane);
            float far = Mathf.Min(left.farClipPlane, right.farClipPlane);
            float distance;
            if (!LiveOverlayPolicy.TryDistance81(near, far, WorldScale, TurnConfirmationVisible ? .75f : _cfg.modMenuDistance81, out distance)) { HideLiveOverlay(); return; }
            var panelRect = (RectTransform)_liveRoot.transform;
            var panelSize = new Vector2(CurrentLivePanelWidth, CurrentLivePanelHeight);
            if (panelRect.sizeDelta != panelSize) panelRect.sizeDelta = panelSize;
            PlaceModMenu80(left,right,center,rotation,distance);
            _livePlacementLimited80|=Mathf.Abs(distance-(TurnConfirmationVisible?.75f:_cfg.modMenuDistance81)*WorldScale)>.001f;
            if (_liveCanvas.worldCamera != left) _liveCanvas.worldCamera = left;
            // Present the canvas to Unity's normal UI rebuild before camera culling.
            // Camera callbacks then limit its rendering to the two VR eyes.
            if (!_liveCanvas.enabled) _liveCanvas.enabled = true;
            RefreshLiveOverlayIfDue();
            if (_liveBuiltRevision != _liveNavigation.Revision)
            {
                _liveBuiltRevision = _liveNavigation.Revision; _liveCameraLogBudget = 3;
                // The canvas is already enabled before the normal UI update.
                // Do not flush all native layout/graphics queues on menu changes.
                try
                {
                    var mesh = _liveLabel.canvasRenderer.GetMesh(); // Owned by CanvasRenderer; never destroy it.
                    _liveLabelVertices = mesh == null ? 0 : mesh.vertexCount;
                    // Existing snapshot fields retain layout diagnostics;
                    // avoid synchronous file output on each menu transition.
                }
                catch (Exception error) { _log.Error("[overlay] Layout diagnostic: " + error.Message); }
            }
        }

        static void LiveOverlayBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (_liveCanvas == null) return;
            bool hudCapture = IsHudCaptureCamera(camera);
            bool active = LiveOverlayVr && !_modeFlat && _attached && CurrentLiveOption() != null;
            bool visible = active && (camera == _liveLeft || camera == _liveRight || hudCapture);
            bool isolated = camera != null && HudCaptureActive && _liveRoot != null && _liveRoot.layer == _hudLayer;
            bool retain = OverlayCanvasPolicy.Retain(active, visible, isolated, _hudLayer, camera == null ? -1 : camera.cullingMask);
            if (_liveCanvas.enabled != retain) _liveCanvas.enabled = retain;
            if (!visible) return;
            int eyeBit = hudCapture ? 3 : camera == _liveLeft ? 1 : 2;
            if (hudCapture) _liveLeftCameraFrame = _liveRightCameraFrame = Time.frameCount;
            if (eyeBit == 1) _liveLeftCameraFrame = Time.frameCount; else _liveRightCameraFrame = Time.frameCount;
            if ((_liveCameraLogBudget & eyeBit) != 0)
            {
                _liveCameraLogBudget &= ~eyeBit;
                // Both-eye completion remains recorded above without logging
                // strings or writing files from a render-camera callback.
            }
        }

        internal static bool IsLiveOverlayCanvas(Canvas canvas) => canvas != null &&
            (canvas == _contextHintCanvas || canvas == _touchPointerCanvas || canvas == _touchGestureCanvas || canvas == _liveCanvas || IsTouchRadialCanvas(canvas) || IsTouchProximityCanvas73(canvas) || IsSpatialNode(canvas.transform) || canvas.name == "RTMaquetaXR VR startup" || (_liveRoot != null && canvas.transform.IsChildOf(_liveRoot.transform)));

        static void CreateLiveOverlay()
        {
            _liveRoot = new GameObject("RTMaquetaXR_LiveOverlay", typeof(RectTransform), typeof(Canvas));
            _liveRoot.layer = 5;
            UnityEngine.Object.DontDestroyOnLoad(_liveRoot);
            var rect = (RectTransform)_liveRoot.transform; rect.sizeDelta = new Vector2(LivePanelWidth, LivePanelHeight);
            _liveCanvas = _liveRoot.GetComponent<Canvas>();
            _liveCanvas.renderMode = RenderMode.WorldSpace; _liveCanvas.sortingOrder = 32761;
            _liveCanvas.overrideSorting = true; _liveCanvas.enabled = false;
            _liveFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_liveFont == null) throw new InvalidOperationException("Built-in overlay font unavailable");
            _liveHeaderFont = _liveFont;
            TryLiveGameFonts();
            _liveImageMaterial = CreateLiveUiMaterial("RTMaquetaXR Overlay Images");
            // Unity Text supplies its font atlas through mainTexture. Use the same
            // pipeline-compatible UI shader as Image, not a legacy font shader.
            _liveFontMaterial = CreateLiveUiMaterial("RTMaquetaXR Overlay Text");
            LiveImage("Background", rect, LiveBackground, 0, 0, LivePanelWidth, LivePanelHeight);
            LiveTopImage("HeaderBand", rect, LiveSelection, 18, 18, 1284, 58);
            _liveTitle = LiveText("Heading", rect, 28, FontStyle.Normal, LiveInk, 36, -24, 740, 48);
            _liveTitle.font = _liveHeaderFont;
            LiveTopImage("Divider", rect, LiveFrame, 469, 85, 1, 420);
            _liveSelection = LiveTopImage("Selection", rect, LiveSelection, 34, 91, 404, 36).rectTransform;
            for (int i = 0; i < _liveRows.Length; ++i)
            {
                _liveRows[i] = LiveText("MenuRow" + i, rect, 24, FontStyle.Normal, LiveInk, 42, -92 - i * 36, 398, 34);
                _liveRows[i].resizeTextForBestFit = true;
                _liveRows[i].resizeTextMinSize = 20; _liveRows[i].resizeTextMaxSize = 24;
            }
            _liveLabel = LiveText("Option", rect, 30, FontStyle.Normal, LiveInk, 500, -86, 770, 70);
            _liveValue = LiveText("Value", rect, 36, FontStyle.Bold, LiveAccent, 500, -166, 770, 108);
            FitLiveText(_liveTitle, 24, 28); FitLiveText(_liveLabel, 25, 30); FitLiveText(_liveValue, 24, 36);
            _livePrimaryActionFrame = LiveTopImage("Save button", rect, LiveSelection, 630, 172, 500, 96);
            _livePrimaryActionText = LiveText("Save label",rect,36,FontStyle.Bold,LiveInk,640,-182,480,76);
            _livePrimaryActionText.alignment=TextAnchor.MiddleCenter;
            FitLiveText(_livePrimaryActionText,28,36);
            _livePrimaryActionFrame.gameObject.SetActive(false);_livePrimaryActionText.gameObject.SetActive(false);
            var track = LiveTopImage("Slider", rect, LiveFrame, LiveOverlayLayout.SliderX, 306, LiveSliderWidth, 12);
            _liveSlider = track.gameObject;
            var fill = LiveImage("Fill", track.rectTransform, LiveAccent, 0, 0, LiveSliderWidth, 12);
            _liveSliderFill = fill.rectTransform;
            _liveSliderFill.anchorMin = _liveSliderFill.anchorMax = new Vector2(0, 0.5f);
            _liveSliderFill.pivot = new Vector2(0, 0.5f); _liveSliderFill.anchoredPosition = Vector2.zero;
            _liveDecreaseFrame = LiveTopImage("DecreaseFrame", rect, LiveSelection, 500, 284, 56, 50).gameObject;
            _liveIncreaseFrame = LiveTopImage("IncreaseFrame", rect, LiveSelection, 1214, 284, 56, 50).gameObject;
            _liveDecreaseText = LiveText("Decrease", rect, 30, FontStyle.Bold, LiveAccent, 518, -287, 38, 42); _liveDecreaseText.text = "-";
            _liveIncreaseText = LiveText("Increase", rect, 30, FontStyle.Bold, LiveAccent, 1227, -287, 38, 42); _liveIncreaseText.text = "+";
            LiveTopImage("ActionFrame", rect, LiveSelection, 500, 336, 770, 96);
            _liveActionText = LiveText("Interaction", rect, 23, FontStyle.Bold, LiveAccent, 516, -340, 738, 88);
            FitLiveText(_liveActionText, 18, 23);
            LiveTopImage("PreviousFrame", rect, LiveSelection, 34, 468, 192, 34);
            LiveTopImage("NextFrame", rect, LiveSelection, 246, 468, 192, 34);
            _livePreviousText = LiveText("PreviousPage", rect, 20, FontStyle.Normal, LiveMuted, 48, -471, 170, 30);
            _liveNextText = LiveText("NextPage", rect, 20, FontStyle.Normal, LiveMuted, 260, -471, 170, 30);
            _liveCloseHint = LiveText("CloseHint", rect, 28, FontStyle.Normal, LiveMuted, 804, -16, 488, 78);
            _liveState = LiveText("EffectiveState", rect, 20, FontStyle.Normal, LiveMuted, 500, -438, 770, 126);
            FitLiveText(_liveState, 17, 20);
            LiveTopImage("DescriptionDivider", rect, LiveFrame, 36, 576, 1248, 1);
            _liveDescriptionHeading = LiveText("DescriptionHeading", rect, 20, FontStyle.Bold, LiveAccent, 36, -590, 1248, 26);
            _liveDescription = LiveText("Description", rect, 23, FontStyle.Normal, LiveInk, 36, -628, 1248, 260);
            _liveHelp = LiveText("TouchHelp", rect, 21, FontStyle.Normal, LiveMuted, 36, -906, 1248, 96);
            FitLiveText(_liveDescription, 20, 23); FitLiveText(_liveHelp, 17, 21);
            // Match the wheel's proven local supersampling and restrained
            // outline while keeping this menu's physical geometry and hit map.
            // Each owned Text is sharpened exactly once at construction. Control
            // artwork keeps the wheel's approved 40 logical-pixel body after the
            // 2x/.5 supersampling transform; combinations reserve multiple slots.
            foreach (var text in _liveRoot.GetComponentsInChildren<Text>(true))
            {
                SharpenRadialText(text); OutlineRadialText(text);
            }
            // Optional Touch clicks use private panel coordinates. No raycaster,
            // selectable or event handler is added to the game's EventSystem.
        }

        static void TryLiveGameFonts()
        {
            TryLiveGameTheme();
            // settingspcview uses ScreenFont (TT Supermolot Neue Medium) and
            // HeaderFont (Inquisitor). At most three creation/opening attempts
            // allow for late-loaded menu assets; never scan resources per frame.
            if (_liveFontAttempts >= 3) return;
            ++_liveFontAttempts;
            foreach (var font in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (font == null) continue;
                if (font.name == "TTSupermolotNeue-Medium") _liveFont = font;
                else if (font.name == "Inquisitor") _liveHeaderFont = font;
            }
            if (_liveRoot != null)
                foreach (var text in _liveRoot.GetComponentsInChildren<Text>(true))
                    text.font = text == _liveTitle || text.name == "QuickHeading" ? _liveHeaderFont : _liveFont;
            if (_liveFont != null && _liveFont.name == "TTSupermolotNeue-Medium" && _liveHeaderFont != null && _liveHeaderFont.name == "Inquisitor") _liveFontAttempts = 3;
        }

        static Material CreateLiveUiMaterial(string name)
        {
            // This is the same engine material used by our working Image reticle.
            // UI/Default was stripped in the real build: Shader.Find of that name is not a valid requirement.
            var basis = Graphic.defaultGraphicMaterial;
            Material material;
            if (basis != null && basis.shader != null && basis.shader.isSupported && basis.shader.name != "Hidden/InternalErrorShader")
                material = new Material(basis);
            else
            {
                var shader = Shader.Find("Owlcat/UI/Default"); // Verified in this game's globalgamemanagers.assets.
                if (shader == null || !shader.isSupported) throw new InvalidOperationException("Game UI material not ready");
                material = new Material(shader);
            }
            material.name = name;
            // Modify only our clone; leave game materials and global shader state intact.
            material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            SetLiveMaterialInt(material, "_ZTest", (int)CompareFunction.Always);
            SetLiveMaterialInt(material, "_ZTestMode", (int)CompareFunction.Always);
            SetLiveMaterialInt(material, "_StencilComp", (int)CompareFunction.Always);
            SetLiveMaterialInt(material, "_Stencil", 0); SetLiveMaterialInt(material, "_StencilOp", 0);
            SetLiveMaterialInt(material, "_StencilWriteMask", 0); SetLiveMaterialInt(material, "_StencilReadMask", 255);
            SetLiveMaterialInt(material, "_ColorMask", 15); SetLiveMaterialInt(material, "_UseUIAlphaClip", 0);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            material.DisableKeyword("UNITY_UI_CLIP_RECT"); material.DisableKeyword("UNITY_UI_ALPHACLIP");
            return material;
        }

        static void SetLiveMaterialInt(Material material, string name, int value) { if (material.HasProperty(name)) material.SetInt(name, value); }

        static object LiveOverlaySnapshot() => new {
            Started = _liveStarted, InputReady = _liveInputReady, Created = _liveCanvas != null,
            Status = _liveVisualStatus, CreateAttempts = _liveCreateAttempts,
            Page = _livePage, Options = _liveOptions == null ? 0 : _liveOptions.Count,
            Menu = _liveNavigation.Menu?.Title, MenuDepth = _liveNavigation.Depth, MenuRevision = _liveNavigation.Revision,
            Description = CurrentLiveOption()?.Description, Input = "Touch", TouchOwned = TouchInputOwned,
            Focused = Application.isFocused, VrGate = LiveOverlayVr,
            LastLeftCameraFrame = _liveLeftCameraFrame, LastRightCameraFrame = _liveRightCameraFrame,
            LastFlatRepaintFrame = _liveFlatFrame,
            LabelVerticesAtPageChange = _liveLabelVertices, NativeMonitor = _liveNativeMonitor != null,
            NativeButtons = _liveNativeButton != null, ThemeAttempts = _liveThemeAttempts,
            VisualPixelsVerified = false
        };

        static Image LiveImage(string name, RectTransform parent, Color color, float x, float y, float width, float height)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)); obj.layer = 5;
            var rect = (RectTransform)obj.transform; rect.SetParent(parent, false); rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = new Vector2(x, y);
            var image = obj.GetComponent<Image>(); image.color = color; image.raycastTarget = false; image.material = _liveImageMaterial;
            RegisterLiveGameTheme(image); return image;
        }

        static Image LiveTopImage(string name, RectTransform parent, Color color, float x, float y, float width, float height) =>
            LiveImage(name, parent, color, x + width / 2 - parent.rect.width / 2, parent.rect.height / 2 - y - height / 2, width, height);

        static Text LiveText(string name, RectTransform parent, int size, FontStyle style, Color color, float x, float y, float width, float height)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(OverlayControlText72)); obj.layer = 5;
            var rect = (RectTransform)obj.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
            var text = obj.GetComponent<Text>(); text.font = _liveFont; text.fontSize = size; text.fontStyle = style; text.color = color;
            ((OverlayControlText72)text).IconBodySize=ControlIconSizing65.OverlayBody;
            text.raycastTarget = false; text.supportRichText = false; text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            text.material = _liveFontMaterial; return text;
        }

        static void SetLiveText(Text target, string value) { value = value ?? ""; if (target != null && target.text != value) target.text = value; }
        static void FitLiveText(Text target, int minimum, int maximum)
        {
            target.resizeTextForBestFit = true;
            target.resizeTextMinSize = minimum; target.resizeTextMaxSize = maximum;
        }
        internal static int LiveOverlayPointerHit(float x, float y)
        {
            if (TurnConfirmationVisible) return TurnConfirmationLayout.Hit(x,y);
            if (TouchQuickGuideVisible) return TouchQuickGuideLayout.PointerHit(x, y);
            var option = CurrentLiveOption();
            if (option == null) return -1;
            bool enabled = option.Enabled == null || option.Enabled();
            bool execute = option.Menu != null || option.Action != null || option.Command != OverlayCommand.None;
            return LiveOverlayLayout.PointerHit(x, y, _livePage, _liveOptions.Count, option.Change != null, execute, enabled, option.ActionButton);
        }
        static void RefreshLiveOverlayIfDue()
        {
            if (_liveLanguageRevision == ModLocalization.Revision && Time.unscaledTime < _liveNextTextUpdate) return;
            _liveLanguageRevision = ModLocalization.Revision;
            RefreshLiveOverlay(); _liveNextTextUpdate = Time.unscaledTime + 0.12f;
        }
        static void RefreshLiveOverlay()
        {
            var option = CurrentLiveOption(); if (option == null) return;
            try
            {
                if (_liveFontRetryOnOpen) { _liveFontRetryOnOpen = false; TryLiveGameFonts(); }
                if (TurnConfirmationVisible) { HideTouchQuickGuide(); UpdateTurnConfirmationVisual(); return; }
                HideTurnConfirmationVisual();
                if (TouchQuickGuideVisible) { UpdateTouchQuickGuide(); return; }
                HideTouchQuickGuide();
                _liveDisplayEnabled = option.Enabled == null || option.Enabled();
                _liveDisplayTitle = "RT MAQUETA VR  ·  " + _liveNavigation.Menu.Title.ToUpperInvariant() + "  ·  " + (_livePage + 1) + " / " + _liveOptions.Count;
                _liveDisplayLabel = option.Label;
                _liveDisplayDescription = option.Description ?? "";
                _liveDisplayValue = option.Menu != null ? ModLocalization.Format("{0} settings", option.Menu.Options.Count - 2) :
                    option.Command == OverlayCommand.Close ? ModLocalization.Text("Return to game") :
                    option.Command == OverlayCommand.Back ? ModLocalization.Text("Return to previous menu") : option.Value == null ? "" : option.Value();
                _liveDisplayValueSize = _liveDisplayValue != null && _liveDisplayValue.IndexOf('\n') >= 0 ? 28 : 36;
                _liveDisplaySlider = option.Slider01 != null;
                _liveDisplayFraction = _liveDisplaySlider ? LiveOverlayPolicy.Slider(option.Slider01()) : 0;
                _liveDisplayAction = option.Change != null ? ModLocalization.Text("Point at - / +, or use the right stick") : ModLocalization.Format("A / right trigger  ·  {0}", _liveNavigation.ExecuteLabel);
                if (!_liveDisplayEnabled) _liveDisplayAction = ModLocalization.Text("Setting unavailable  ·  B: back");
                bool guide = TouchGuideVisible && TouchGuideTopic != null;
                _liveDisplayState = guide ? TouchGuideTopic.Binding + "\n" + TouchGuideTopic.Summary :
                    (_liveMessage == null ? "" : ModLocalization.Text(_liveMessage) + "\n") + (_liveStatus == null ? "" : _liveStatus());
                if(option.ActionButton) _liveDisplayState=(_liveMessage==null?"":ModLocalization.Text(_liveMessage)+"\n")+_liveDisplayValue;
                if (guide)
                {
                    _liveDisplayDescription = TouchGuideTopic.Details;
                    _liveDisplayAction = ModLocalization.Text("CLOSE  ·  point and release the right trigger, or press A");
                    _liveDisplayValue = "";
                }
                _liveDisplayHelp = ModLocalization.Text(TouchBindings.OverlayHelp);
                int firstRow = LiveOverlayLayout.FirstRow(_livePage);
                for (int i = 0; i < _liveRows.Length; ++i)
                {
                    int index = firstRow + i;
                    var row = _liveRows[i];
                    bool exists = index < _liveOptions.Count;
                    _liveDisplayRows[i] = exists ? (index == _livePage ? "> " : "   ") + _liveOptions[index].Label : "";
                    _liveDisplayRowEnabled[i] = exists && (_liveOptions[index].Enabled == null || _liveOptions[index].Enabled());
                    if (row == null) continue;
                    row.gameObject.SetActive(exists);
                    if (!exists) continue;
                    bool selected = index == _livePage;
                    SetLiveText(row, _liveDisplayRows[i]);
                    row.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
                    row.color = !_liveDisplayRowEnabled[i] ? LiveFrame : selected ? LiveInk : LiveMuted;
                }
                if (_liveCanvas == null) return; // IMGUI flat presentation needs no world-space resources.
                SetLiveText(_livePreviousText, ModLocalization.Text("< PREVIOUS"));
                SetLiveText(_liveNextText, ModLocalization.Text("NEXT >"));
                SetLiveText(_liveCloseHint, ModLocalization.Text("[F1] / [LT]+[LG]+[RT]+[RG] · 1 s"));
                SetLiveText(_liveDescriptionHeading, ModLocalization.Text("DESCRIPTION"));
                _liveSelection.anchoredPosition = new Vector2(-424, LivePanelHeight / 2 - 109 - (_livePage - firstRow) * 36);
                SetLiveText(_liveTitle, _liveDisplayTitle); SetLiveText(_liveLabel, _liveDisplayLabel);
                SetLiveText(_liveDescription, _liveDisplayDescription); SetLiveText(_liveValue, _liveDisplayValue);
                if (_liveValue.fontSize != _liveDisplayValueSize) _liveValue.fontSize = _liveDisplayValueSize;
                _liveValue.resizeTextMaxSize = _liveDisplayValueSize;
                _liveValue.color = _liveDisplayEnabled ? LiveAccent : LiveMuted;
                _liveDecreaseText.color = _liveIncreaseText.color = _liveDisplayEnabled && option.Change != null ? LiveAccent : LiveFrame;
                _livePreviousText.color = firstRow > 0 ? LiveAccent : LiveFrame;
                _liveNextText.color = firstRow + LiveOverlayLayout.VisibleRows < _liveOptions.Count ? LiveAccent : LiveFrame;
                _liveSlider.SetActive(_liveDisplaySlider);
                SetLiveActive(_liveDecreaseFrame, !guide && !option.ActionButton); SetLiveActive(_liveIncreaseFrame, !guide && !option.ActionButton);
                SetLiveActive(_liveDecreaseText.gameObject, !guide && !option.ActionButton); SetLiveActive(_liveIncreaseText.gameObject, !guide && !option.ActionButton);
                SetLiveActive(_liveValue.gameObject, !guide && !option.ActionButton);
                SetLiveActive(_livePrimaryActionFrame.gameObject,option.ActionButton);
                SetLiveActive(_livePrimaryActionText.gameObject,option.ActionButton);
                SetLiveText(_livePrimaryActionText,(option.ActionLabel??"").ToUpperInvariant());
                if (_liveDisplaySlider) _liveSliderFill.sizeDelta = new Vector2(LiveSliderWidth * _liveDisplayFraction, 12);
                SetLiveText(_liveActionText, _liveDisplayAction); SetLiveText(_liveState, _liveDisplayState); SetLiveText(_liveHelp, _liveDisplayHelp);
                foreach(var text in _liveRoot.GetComponentsInChildren<OverlayControlText72>(true))text.RefreshLayout();
                var help=_liveHelp as OverlayControlText72;
                if(help!=null)
                {
                    float needed=Mathf.Max(96,help.ContentHeight*.5f), top=1002-needed;
                    _liveHelp.rectTransform.anchoredPosition=new Vector2(36,-top);
                    _liveHelp.rectTransform.sizeDelta=new Vector2(2496,needed*2);
                    _liveDescription.rectTransform.sizeDelta=new Vector2(2496,Mathf.Max(80,top-646)*2);
                }
            }
            catch (Exception error) { _liveDisplayState = ModLocalization.Text("Status unavailable"); SetLiveText(_liveState, _liveDisplayState); _log.Error("[overlay] Status: " + error.Message); }
        }

        // Drawn by Runner.OnGUI before CaptureFlatFrame at end-of-frame. Labels
        // never consume GUI input; the shared Touch sampler owns all navigation.
        internal static void DrawFlatLiveOverlay()
        {
            if (!_liveNavigation.Visible) { _liveFontRetryOnOpen = true; return; }
            if (!LiveOverlayVr || !_liveNavigation.Visible || (!_modeFlat && _attached) ||
                Event.current == null || Event.current.type != EventType.Repaint) return;
            float x, y, scale;
            if (!TryCurrentLiveFlatPlacement(Screen.width, Screen.height, out x, out y, out scale)) return;
            RefreshLiveOverlayIfDue();
            var matrix = GUI.matrix; var color = GUI.color; int depth = GUI.depth;
            try
            {
                if (_liveFlatStyle == null)
                {
                    _liveFlatStyle = new GUIStyle(GUI.skin.label) {
                        wordWrap = true, richText = false, alignment = TextAnchor.UpperLeft,
                        padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0)
                    };
                    if (_liveFont != null) _liveFlatStyle.font = _liveFont;
                }
                GUI.depth = -10000;
                GUI.matrix = Matrix4x4.TRS(new Vector3(x, y, 0), Quaternion.identity, new Vector3(scale, scale, 1));
                if (TurnConfirmationVisible) { DrawFlatTurnConfirmation(); _liveFlatFrame=Time.frameCount; _liveFlatError=null; return; }
                if (TouchQuickGuideVisible) { DrawFlatTouchQuickGuide(); _liveFlatFrame = Time.frameCount; _liveFlatError = null; return; }
                DrawFlatLiveTheme("Background", 0, 0, LivePanelWidth, LivePanelHeight, LiveBackground);
                DrawFlatLiveRect(18, 18, 1284, 58, LiveSelection);
                DrawFlatLiveRect(469, 85, 1, 420, LiveFrame);
                DrawFlatLiveText(_liveDisplayTitle, 36, 24, 740, 48, 28, true, LiveInk);
                DrawFlatLiveText(ModLocalization.Text("[F1] / [LT]+[LG]+[RT]+[RG] · 1 s"), 804, 20, 488, 62, 28, false, LiveMuted);
                int selected = _livePage - LiveOverlayLayout.FirstRow(_livePage);
                DrawFlatLiveRect(34, 91 + selected * 36, 404, 36, LiveSelection);
                for (int i = 0; i < _liveDisplayRows.Length; ++i)
                    DrawFlatLiveText(_liveDisplayRows[i], 42, 92 + i * 36, 398, 34, 23, selected == i,
                        !_liveDisplayRowEnabled[i] ? LiveFrame : selected == i ? LiveInk : LiveMuted);
                DrawFlatLiveText(_liveDisplayLabel, 500, 86, 770, 70, 30, false, LiveInk);
                bool guide = TouchGuideVisible && TouchGuideTopic != null;
                if (guide) DrawFlatTouchGuide();
                else if(CurrentLiveOption()?.ActionButton != true) DrawFlatLiveText(_liveDisplayValue, 500, 166, 770, 108, _liveDisplayValueSize, true,
                    _liveDisplayEnabled ? LiveAccent : LiveMuted);
                if(CurrentLiveOption()?.ActionButton == true)
                {
                    DrawFlatLiveTheme("Save button",630,172,500,96,LiveSelection);
                    var alignment=_liveFlatStyle.alignment;
                    _liveFlatStyle.alignment=TextAnchor.MiddleCenter;
                    DrawFlatLiveText((CurrentLiveOption().ActionLabel??"").ToUpperInvariant(),640,182,480,76,36,true,LiveInk);
                    _liveFlatStyle.alignment=alignment;
                }
                if (_liveDisplaySlider)
                {
                    DrawFlatLiveRect(LiveOverlayLayout.SliderX, 306, LiveSliderWidth, 12, LiveFrame);
                    DrawFlatLiveRect(LiveOverlayLayout.SliderX, 306, LiveSliderWidth * _liveDisplayFraction, 12, LiveAccent);
                }
                if (!guide && CurrentLiveOption()?.ActionButton != true)
                {
                    Color adjustment = _liveDisplayEnabled && CurrentLiveOption()?.Change != null ? LiveAccent : LiveFrame;
                    DrawFlatLiveTheme("DecreaseFrame", 500, 284, 56, 50, LiveSelection); DrawFlatLiveTheme("IncreaseFrame", 1214, 284, 56, 50, LiveSelection);
                    DrawFlatLiveText("-", 518, 287, 38, 42, 30, true, adjustment); DrawFlatLiveText("+", 1227, 287, 38, 42, 30, true, adjustment);
                }
                DrawFlatLiveTheme("ActionFrame", 500, 336, 770, 96, LiveSelection);
                DrawFlatLiveText(_liveDisplayAction, 516, 348, 738, 72, 23, true, LiveAccent);
                int first = LiveOverlayLayout.FirstRow(_livePage);
                DrawFlatLiveTheme("PreviousFrame", 34, 468, 192, 34, LiveSelection); DrawFlatLiveTheme("NextFrame", 246, 468, 192, 34, LiveSelection);
                DrawFlatLiveText(ModLocalization.Text("< PREVIOUS"), 48, 471, 170, 30, 20, false, first > 0 ? LiveAccent : LiveFrame);
                DrawFlatLiveText(ModLocalization.Text("NEXT >"), 260, 471, 170, 30, 20, false, first + LiveOverlayLayout.VisibleRows < _liveOptions.Count ? LiveAccent : LiveFrame);
                DrawFlatLiveText(_liveDisplayState, 500, 435, 770, 132, 20, false, LiveMuted);
                DrawFlatLiveRect(36, 576, 1248, 1, LiveFrame);
                DrawFlatLiveText(ModLocalization.Text("DESCRIPTION"), 36, 590, 1248, 26, 20, true, LiveAccent);
                _liveFlatStyle.font=_liveFont;_liveFlatStyle.fontSize=21;_liveFlatStyle.fontStyle=FontStyle.Normal;
                float helpHeight=Mathf.Max(96,OverlayControlFlat72.Measure(_liveDisplayHelp,_liveFlatStyle,1248,ControlIconSizing65.OverlayBody));
                float helpTop=1002-helpHeight;
                DrawFlatLiveText(_liveDisplayDescription, 36, 628, 1248, Mathf.Max(80,helpTop-646), 23, false, LiveInk);
                DrawFlatLiveText(_liveDisplayHelp, 36, helpTop, 1248, helpHeight, 21, false, LiveMuted);
                DrawFlatOverlayTouchCursor();
                _liveFlatFrame = Time.frameCount;
                _liveFlatError = null;
            }
            catch (Exception error)
            {
                if (_liveFlatError != error.Message) _log.Error("[overlay] Flat presentation: " + error.Message);
                _liveFlatError = error.Message;
            }
            finally { GUI.matrix = matrix; GUI.color = color; GUI.depth = depth; }
        }

        static void DrawFlatLiveRect(float x, float y, float width, float height, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
        }

        static void SetLiveActive(GameObject target, bool active) { if (target != null && target.activeSelf != active) target.SetActive(active); }

        static void DrawFlatLiveText(string text, float x, float y, float width, float height, int size, bool bold, Color color)
        {
            GUI.color = Color.white;
            _liveFlatStyle.font = size == 28 && _liveHeaderFont != null ? _liveHeaderFont : _liveFont;
            _liveFlatStyle.fontSize = size; _liveFlatStyle.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            // The flat fallback has the same fixed hit geometry as the stereo
            // canvas. Fit longer translations inside it, never move controls.
            int minimum = size <= 24 ? Math.Max(17, size - 4) : Math.Max(24, size - 6);
            var content = new GUIContent(text ?? "");
            _liveFlatMeasureRuns.Clear();
            _liveFlatMeasureContent.text=ControlIconMarkup.Prepare(text??"",_liveFlatMeasureRuns,0,false);
            _liveFlatStyle.richText=false;
            if(_liveFlatMeasureRuns.Count==0)
                while (_liveFlatStyle.fontSize > minimum && _liveFlatStyle.CalcHeight(content,width)>height)--_liveFlatStyle.fontSize;
            _liveFlatStyle.normal.textColor = color;
            OverlayControlFlat72.Draw(new Rect(x, y, width, height), text ?? "", _liveFlatStyle,ControlIconSizing65.OverlayBody);
        }

        internal static void StopLiveOverlay()
        {
            DestroyTouchContextHints();
            ResetOverlayTouchPointer();
            StopTouchGuide();
            _liveStarted = false; _liveNavigation.Reset(null);
            RenderPipelineManager.beginCameraRendering -= LiveOverlayBeginCamera;
            RemoveLiveOverlayInput();
            DestroyLiveOverlayVisuals();
            _liveLeft = _liveRight = null;
            _liveStatus = null; _liveMessage = null;
            _liveCreateAttempts = _liveCameraLogBudget = 0; _liveBuiltRevision = -1; _liveVisualStatus = "stopped";
            _liveLeftCameraFrame = _liveRightCameraFrame = _liveFlatFrame = -1; _liveLabelVertices = 0;
            _liveFlatStyle = null; _liveFlatError = null;
        }

        static void DestroyLiveOverlayVisuals()
        {
            DestroyTouchContextHints(); // Hints borrow these UI materials and fonts.
            ReleaseLiveGameTheme();
            HideTouchGuideVisual(); _touchGuideGraphic = null;
            if (_liveRoot != null) { _liveRoot.SetActive(false); UnityEngine.Object.Destroy(_liveRoot); }
            if (_liveImageMaterial != null) UnityEngine.Object.Destroy(_liveImageMaterial);
            if (_liveFontMaterial != null) UnityEngine.Object.Destroy(_liveFontMaterial);
            _liveRoot = null; _liveCanvas = null;
            _liveImageMaterial = _liveFontMaterial = null; _liveFont = _liveHeaderFont = null;
            _liveDecreaseText = _liveIncreaseText = _livePreviousText = _liveNextText = null;
            _liveCloseHint = _liveDescriptionHeading = null; _liveLanguageRevision = -1;
            _liveDecreaseFrame = _liveIncreaseFrame = null;
            _liveTitle = _liveLabel = _liveValue = _liveState = _liveHelp = _liveDescription = _liveActionText = null;
            Array.Clear(_liveRows, 0, _liveRows.Length); _liveSelection = null;
            _liveSlider = null; _liveSliderFill = null;
        }
    }
}
