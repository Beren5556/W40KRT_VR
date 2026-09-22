using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        const float ContextHintWidth = TouchContextHintLayout.Width;
        static float _contextHintWidth = ContextHintWidth;
        static float _contextHintHeight = TouchContextHintLayout.HeightForRows(0);
        static readonly TouchContextHintCache _contextHints = new TouchContextHintCache();
        static GameObject _contextHintRoot;
        static Canvas _contextHintCanvas;
        static Text _contextHintHeading;
        static ContextHintControlGroup _contextHintCommon;
        static readonly List<Text> _contextHintCaptions = new List<Text>();
        static readonly List<ContextHintControlGroup> _contextHintControls = new List<ContextHintControlGroup>();
        static readonly List<string> _contextHintRows = new List<string>(), _contextHintSymbols = new List<string>();
        static readonly List<float> _contextHintGlyphWidths = new List<float>(), _contextHintRowY = new List<float>(), _contextHintRowHeights = new List<float>();
        static Camera _contextHintLeft, _contextHintRight;
        static bool _contextHintVisible;
        static string _contextHintTitle = "", _contextHintText = "", _contextHintCommonText = "";
        static GUIStyle _contextHintFlatTitle, _contextHintFlatBody, _contextHintFlatGlyph;
        static readonly GUIContent _contextHintTitleContent = new GUIContent(), _contextHintBodyContent = new GUIContent();
        static readonly GUIContent _contextHintGlyphContent = new GUIContent();
        static readonly List<ControlIconRun> _contextHintGlyphRuns = new List<ControlIconRun>();
        static int _contextHintFlatFitRevision = -1;
        static bool _contextHintsSessionVisible;
        static bool _contextHintDistance;
        internal static bool TouchContextHintsEnabled => _contextHintsSessionVisible;
        internal static void SetTouchContextHintsEnabled(bool value)
        { _contextHintsSessionVisible = value; if (!value) HideTouchContextHints(); }

        static TouchHintContext CurrentTouchHintContext()
        {
            if (_modeName == "MainMenu" || _modeName == "None") return TouchHintContext.Hidden;
            bool combat = false;
            if (_presentation != null && !InNavigationMap && !InSpaceCombat)
            {
                var game = _presentation.Game(); var turn = game == null ? null : _presentation.Turn(game);
                combat = turn != null && _presentation.InCombat(turn);
            }
            return TouchContextHintsPolicy.Resolve(new TouchHintState {
                Enabled = TouchContextHintsEnabled, Ready = TouchInputOwned && _touchSampleValid && !TouchCameraFaulted,
                Loading = PresentationTransition, Welcome = TouchQuickGuideVisible, Settings = TouchOverlayOpen,
                Radial = _touchRadial.Visible, RightWheel = _touchRadial.Side == 1, Actors = TouchRadialInformationOnly,
                Party = !_touchRadialSpace && TouchRadialModePolicy.IsCharacters(_touchRadialLeftMode), SpaceWheel = _touchRadialSpace,
                LocalMap = TouchLocalMapOwnsAxes, Tutorial = _tutorialShowing,
                Management = TouchMenuWindowVisible || (NativeUiOnlyPresentation() && !InNavigationMap),
                Galactic = InGalacticMap, System = InStarSystemMap, Space = InSpaceCombat,
                Dialogue = CinematicWanted || CinematicPolicy.ScriptedMode(_modeName), Combat = combat, FirstPerson = TouchCameraFirstPersonActive,
                Flat = _modeFlat, Paused = _modeName == "Pause"
            });
        }

        static bool RefreshTouchContextHints()
        {
            if (!TouchContextHintsEnabled || !TouchInputOwned || !_touchSampleValid || PresentationTransition) return false;
            var context = CurrentTouchHintContext();
            bool changed = _contextHints.Changed(context, ModLocalization.Revision, TouchCombatGroundDoubleClickHeadEnabled);
            if (changed || _contextHintDistance != _cfg.touchDrawDistanceShortcut)
            {
                _contextHintTitle = ModLocalization.Text(TouchControlAssignments.ContextTitle(context));
                _contextHintText = TouchControlAssignments.ContextBody(context, TouchCombatGroundDoubleClickHeadEnabled);
                _contextHintDistance = _cfg.touchDrawDistanceShortcut;
                if (_contextHintDistance && (context == TouchHintContext.Exploration || context == TouchHintContext.GroundCombat))
                    _contextHintText += "\n" + TouchControlAssignments.ContextBinding("distance");
                _contextHintTitleContent.text = _contextHintTitle; _contextHintBodyContent.text = _contextHintText;
                if (_contextHintHeading != null) _contextHintHeading.text = _contextHintTitle;
                _contextHintRows.Clear(); _contextHintSymbols.Clear();
                foreach (string row in _contextHintText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int split = row.IndexOf(" · ", StringComparison.Ordinal);
                    _contextHintSymbols.Add(split < 0 ? "" : row.Substring(0, split));
                    _contextHintRows.Add(split < 0 ? row : row.Substring(split + 3));
                }
                RefreshContextHintCells(); _contextHintFlatFitRevision = -1;
            }
            return context != TouchHintContext.Hidden;
        }

        static float ContextHintViewY(Camera camera,float viewportY,float distance)
        {
            Ray ray=camera.ViewportPointToRay(new Vector3(.5f,viewportY,0));
            Vector3 local=camera.transform.InverseTransformDirection(ray.direction);
            return Mathf.Abs(local.z)>.00001f ? local.y/local.z*distance : 0;
        }

        internal static void UpdateTouchContextHints(Camera left, Camera right)
        {
            _contextHintLeft = left; _contextHintRight = right;
            if (_modeFlat || !_attached || left == null || right == null || !RefreshTouchContextHints())
            { HideTouchContextHints(); return; }
            if (_liveFont == null || _liveFontMaterial == null || _liveImageMaterial == null) return;
            if (_contextHintCanvas == null) CreateTouchContextHints();
            float near = Mathf.Max(left.nearClipPlane, right.nearClipPlane), far = Mathf.Min(left.farClipPlane, right.farClipPlane);
            if (!LiveOverlayPolicy.TryDistance(near, far, WorldScale, out float distance)) { HideTouchContextHints(); return; }
            Quaternion rotation = Quaternion.Slerp(left.transform.rotation, right.transform.rotation, .5f);
            Vector3 head = (left.transform.position + right.transform.position) * .5f;
            float verticalShift=TouchContextHintsPolicy.VerticalShift(
                ContextHintViewY(left,0,distance),ContextHintViewY(left,1,distance),
                ContextHintViewY(right,0,distance),ContextHintViewY(right,1,distance),.10f);
            PositionHudHelper(_contextHintRoot.transform, head + rotation * new Vector3(_cfg.touchHintX * distance, _cfg.touchHintY * distance+verticalShift, distance), rotation,
                distance * 1.12f * _cfg.touchHintSize / ContextHintWidth);
            Camera textCamera = _cfg.uiFullResolution && !_hudCaptureFault && _hudBlackCamera != null ? _hudBlackCamera : left;
            if (_contextHintCanvas.worldCamera != textCamera) _contextHintCanvas.worldCamera = textCamera;
            if (_contextHintHeading.font != _liveFont)
            {
                _contextHintHeading.font = _liveFont; _contextHintCommon.SetFont(_liveFont);
                foreach (var group in _contextHintControls) group.SetFont(_liveFont);
                foreach (var caption in _contextHintCaptions) caption.font = _liveFont;
                LayoutContextHintCells();
            }
            _contextHintVisible = true; _contextHintCanvas.enabled = true;
        }

        static ContextHintControlGroup CreateContextControlGroup(string name, RectTransform parent, float x, float y, float width, float height, int fontSize, int bodySize)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(ContextHintControlGroup)); obj.layer = 5;
            var rect = (RectTransform)obj.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(width, height);
            var group = obj.GetComponent<ContextHintControlGroup>();
            group.Configure(_liveFont, _liveFontMaterial, _liveImageMaterial, fontSize, bodySize, LiveAccent);
            return group;
        }

        static void CreateTouchContextHints()
        {
            _contextHintRoot = new GameObject("RTMaquetaXR contextual controls", typeof(RectTransform), typeof(Canvas));
            UnityEngine.Object.DontDestroyOnLoad(_contextHintRoot); _contextHintRoot.layer = 5;
            var rect = (RectTransform)_contextHintRoot.transform; rect.sizeDelta = new Vector2(ContextHintWidth, _contextHintHeight);
            _contextHintCanvas = _contextHintRoot.GetComponent<Canvas>(); _contextHintCanvas.renderMode = RenderMode.WorldSpace;
            _contextHintCanvas.sortingOrder = 32763; _contextHintCanvas.overrideSorting = true;
            _contextHintHeading = LiveText("Context", rect, 27, FontStyle.Bold, LiveAccent, 24, -4, TouchContextHintLayout.CellWidth, 46);
            _contextHintHeading.resizeTextForBestFit = false; _contextHintHeading.raycastTarget = false;
            SharpenRadialText(_contextHintHeading); OutlineRadialText(_contextHintHeading);
            _contextHintCommon = CreateContextControlGroup("Common controls", rect, 24, -52, TouchContextHintLayout.CellWidth, 48, 24, 36);
            EnsureContextHintRows(_contextHintRows.Count);
            _contextHintHeading.text = _contextHintTitle;
            RefreshContextHintCells();
            RenderPipelineManager.beginCameraRendering += TouchContextHintsBeginCamera;
        }

        static void EnsureContextHintRows(int count)
        {
            if (_contextHintRoot == null) return;
            var parent = (RectTransform)_contextHintRoot.transform;
            while (_contextHintControls.Count < count)
            {
                int index = _contextHintControls.Count;
                var group = CreateContextControlGroup("Control combination " + index, parent, TouchContextHintLayout.Padding,
                    -TouchContextHintLayout.CellY(index), 460, TouchContextHintLayout.CellHeight, 25, 40);
                _contextHintControls.Add(group);
                var caption = LiveText("Action " + index, parent, 25, FontStyle.Normal, Color.white, 520,
                    -TouchContextHintLayout.CellY(index), 560, TouchContextHintLayout.CellHeight);
                caption.resizeTextForBestFit = false; caption.alignment = TextAnchor.MiddleLeft;
                caption.horizontalOverflow = HorizontalWrapMode.Wrap; caption.verticalOverflow = VerticalWrapMode.Overflow;
                caption.raycastTarget = false; SharpenRadialText(caption); OutlineRadialText(caption);
                _contextHintCaptions.Add(caption);
            }
            for (int i = 0; i < _contextHintControls.Count; ++i)
            {
                bool active = i < count;
                if (_contextHintControls[i].gameObject.activeSelf != active) _contextHintControls[i].gameObject.SetActive(active);
                if (_contextHintCaptions[i].gameObject.activeSelf != active) _contextHintCaptions[i].gameObject.SetActive(active);
            }
        }

        static void EnsureContextHintMetrics(int count)
        {
            while (_contextHintGlyphWidths.Count < count) _contextHintGlyphWidths.Add(0);
            while (_contextHintRowY.Count < count) _contextHintRowY.Add(0);
            while (_contextHintRowHeights.Count < count) _contextHintRowHeights.Add(TouchContextHintLayout.CellHeight);
        }

        static void RefreshContextHintCells()
        {
            _contextHintCommonText = TouchControlAssignments.ContextCommon(_contextHints.Context);
            EnsureContextHintMetrics(_contextHintRows.Count);
            if (_contextHintRoot == null)
            {
                for (int i = 0; i < _contextHintRows.Count; ++i) { _contextHintRowY[i] = TouchContextHintLayout.CellY(i); _contextHintRowHeights[i] = TouchContextHintLayout.CellHeight; }
                _contextHintHeight = TouchContextHintLayout.HeightForRows(_contextHintRows.Count); return;
            }
            EnsureContextHintRows(_contextHintRows.Count);
            _contextHintCommon.SetValue(_contextHintCommonText);
            for (int i = 0; i < _contextHintRows.Count; ++i)
            {
                _contextHintControls[i].SetValue(TouchControlAssignments.ContextGlyphLabel(_contextHintSymbols[i]));
                _contextHintCaptions[i].text = _contextHintRows[i];
            }
            LayoutContextHintCells();
        }

        static void LayoutContextHintCells()
        {
            if (_contextHintRoot == null) return;
            float glyphColumn=0,captionColumn=0;
            for(int i=0;i<_contextHintRows.Count;i++)
            {
                glyphColumn=Mathf.Max(glyphColumn,Mathf.Ceil(_contextHintControls[i].PreferredWidth)+8);
                captionColumn=Mathf.Max(captionColumn,Mathf.Ceil(_contextHintCaptions[i].preferredWidth*.5f)+4);
            }
            captionColumn=Mathf.Min(captionColumn,TouchContextHintLayout.CaptionMaximum);
            float titleHeight=Mathf.Ceil(_contextHintHeading.preferredHeight*.5f);
            float commonHeight=Mathf.Ceil(_contextHintCommon.PreferredHeight)+8;
            float commonY=TouchContextHintLayout.Padding+titleHeight+6;
            _contextHintHeading.rectTransform.anchoredPosition=new Vector2(TouchContextHintLayout.Padding,-TouchContextHintLayout.Padding);
            _contextHintCommon.Rect.anchoredPosition=new Vector2(TouchContextHintLayout.Padding,-commonY);
            _contextHintCommon.Rect.sizeDelta=new Vector2(Mathf.Ceil(_contextHintCommon.PreferredWidth)+8,commonHeight);
            _contextHintCommon.Configure(_liveFont,_liveFontMaterial,_liveImageMaterial,24,36,LiveAccent);
            float y=commonY+commonHeight+6;
            float contentWidth=Mathf.Max(glyphColumn+TouchContextHintLayout.Gap+captionColumn,
                Mathf.Max(_contextHintCommon.PreferredWidth,Mathf.Ceil(_contextHintHeading.preferredWidth*.5f)));
            for (int i = 0; i < _contextHintRows.Count; ++i)
            {
                var group=_contextHintControls[i];var caption=_contextHintCaptions[i];
                caption.rectTransform.sizeDelta=new Vector2(captionColumn*2,2048);
                float rowHeight=Mathf.Ceil(Mathf.Max(group.PreferredHeight,caption.preferredHeight*.5f))+8;
                _contextHintGlyphWidths[i]=glyphColumn;_contextHintRowY[i]=y;_contextHintRowHeights[i]=rowHeight;
                group.Rect.anchoredPosition=new Vector2(TouchContextHintLayout.Padding,-y);
                group.Rect.sizeDelta=new Vector2(glyphColumn,rowHeight);
                group.Configure(_liveFont,_liveFontMaterial,_liveImageMaterial,25,40,LiveAccent);
                caption.rectTransform.anchoredPosition=new Vector2(TouchContextHintLayout.Padding+glyphColumn+TouchContextHintLayout.Gap,-y);
                caption.rectTransform.sizeDelta=new Vector2(captionColumn*2,rowHeight*2);
                y+=rowHeight;
            }
            _contextHintHeight=y+TouchContextHintLayout.Padding;
            _contextHintWidth=contentWidth+2*TouchContextHintLayout.Padding;
            _contextHintHeading.rectTransform.sizeDelta=new Vector2(contentWidth*2,titleHeight*2);
            ((RectTransform)_contextHintRoot.transform).sizeDelta=new Vector2(_contextHintWidth,_contextHintHeight);
        }

        static void TouchContextHintsBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (_contextHintCanvas == null) return;
            bool isolated = HudCaptureActive && _contextHintRoot != null && _contextHintRoot.layer == _hudLayer;
            bool visible = _contextHintVisible && !_modeFlat && (isolated || camera == _contextHintLeft || camera == _contextHintRight || IsHudCaptureCamera(camera));
            if (_contextHintCanvas.enabled != visible) _contextHintCanvas.enabled = visible;
        }
        internal static void HideTouchContextHints()
        { _contextHintVisible = false; if (_contextHintCanvas != null) _contextHintCanvas.enabled = false; }

        internal static void DrawFlatTouchContextHints()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint || !_modeFlat || !RefreshTouchContextHints()) return;
            if (_contextHintFlatBody == null)
            {
                _contextHintFlatTitle = new GUIStyle { alignment = TextAnchor.MiddleLeft, richText = false, wordWrap = false, fontStyle = FontStyle.Bold };
                _contextHintFlatBody = new GUIStyle { alignment = TextAnchor.MiddleLeft, richText = false, wordWrap = true };
                _contextHintFlatGlyph = new GUIStyle { alignment = TextAnchor.MiddleLeft, richText = true, wordWrap = false, fontStyle = FontStyle.Bold };
            }
            _contextHintFlatTitle.font = _contextHintFlatBody.font = _contextHintFlatGlyph.font = _liveFont;
            _contextHintFlatGlyph.normal.textColor = LiveAccent;
            _contextHintFlatTitle.normal.textColor = LiveAccent; _contextHintFlatBody.normal.textColor = LiveInk;
            if (_contextHintFlatFitRevision != _contextHints.Changes)
            {
                _contextHintFlatFitRevision = (int)_contextHints.Changes;
                _contextHintFlatTitle.fontSize = 27; _contextHintFlatBody.fontSize = 25; _contextHintFlatGlyph.fontSize = 25;
                EnsureContextHintMetrics(_contextHintRows.Count); float y = TouchContextHintLayout.HeaderHeight;float right=640;
                for (int i = 0; i < _contextHintRows.Count; ++i)
                {
                    _contextHintGlyphContent.text = ControlIconMarkup.Prepare(TouchControlAssignments.ContextGlyphLabel(_contextHintSymbols[i]), _contextHintGlyphRuns, 40);
                    float glyphWidth = Mathf.Ceil(_contextHintFlatGlyph.CalcSize(_contextHintGlyphContent).x) + 8;
                    _contextHintBodyContent.text = _contextHintRows[i];
                    float captionWidth=Mathf.Clamp(Mathf.Ceil(_contextHintFlatBody.CalcSize(_contextHintBodyContent).x)+4,
                        TouchContextHintLayout.CaptionMinimum,TouchContextHintLayout.CaptionMaximum);
                    float artHeight=ControlIconAtlas.BodyFit(new Rect(0,0,40,40),_contextHintSymbols[i]).height;
                    float rowHeight = Mathf.Max(TouchContextHintLayout.CellHeight,Mathf.Max(Mathf.Ceil(artHeight)+8,
                        Mathf.Ceil(_contextHintFlatBody.CalcHeight(_contextHintBodyContent, captionWidth)) + 8));
                    _contextHintGlyphWidths[i] = glyphWidth; _contextHintRowY[i] = y; _contextHintRowHeights[i] = rowHeight; y += rowHeight;
                    right=Mathf.Max(right,TouchContextHintLayout.Padding+glyphWidth+TouchContextHintLayout.Gap+captionWidth+TouchContextHintLayout.Padding);
                }
                _contextHintHeight = y + TouchContextHintLayout.Padding;_contextHintWidth=Mathf.Clamp(Mathf.Ceil(right),640,ContextHintWidth);
            }
            float scale = Mathf.Min(Screen.width * .78f / _contextHintWidth, Screen.height * .82f / _contextHintHeight) * _cfg.touchHintSize;
            var matrix = GUI.matrix; var color = GUI.color; int depth = GUI.depth;
            try
            {
                GUI.depth = -10001;
                GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - _contextHintWidth * scale) * .5f + _cfg.touchHintX * Screen.width * .4f,
                    Screen.height * (.5f - _cfg.touchHintY * .8f) - _contextHintHeight * scale * .5f, 0), Quaternion.identity, Vector3.one * scale);
                GUI.color = Color.white;
                GUI.Label(new Rect(24, 4, _contextHintWidth-48, 46), _contextHintTitleContent, _contextHintFlatTitle);
                _contextHintFlatGlyph.fontSize = 24;
                ControlIconDrawing.Label(new Rect(24, 52, _contextHintWidth-48, 48), _contextHintCommonText, _contextHintFlatGlyph);
                _contextHintFlatGlyph.fontSize = 25;
                for (int i = 0; i < _contextHintRows.Count; ++i)
                {
                    float y = _contextHintRowY[i], height = _contextHintRowHeights[i], glyph = _contextHintGlyphWidths[i];
                    ControlIconDrawing.Label(new Rect(TouchContextHintLayout.Padding, y, glyph, height), TouchControlAssignments.ContextGlyphLabel(_contextHintSymbols[i]), _contextHintFlatGlyph);
                    GUI.Label(new Rect(TouchContextHintLayout.Padding + glyph + TouchContextHintLayout.Gap, y,
                        TouchContextHintLayout.CaptionMaximum, height), _contextHintRows[i], _contextHintFlatBody);
                }
            }
            finally { GUI.matrix = matrix; GUI.color = color; GUI.depth = depth; }
        }

        static void DestroyTouchContextHints()
        {
            RenderPipelineManager.beginCameraRendering -= TouchContextHintsBeginCamera;
            if (_contextHintRoot != null) UnityEngine.Object.Destroy(_contextHintRoot);
            _contextHintRoot = null; _contextHintCanvas = null; _contextHintHeading = null; _contextHintCommon = null;
            _contextHintControls.Clear(); _contextHintCaptions.Clear();
            _contextHintVisible = false; _contextHintLeft = _contextHintRight = null;
        }
    }
}
