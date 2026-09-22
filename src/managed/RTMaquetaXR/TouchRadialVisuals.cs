using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

namespace RTMaquetaXR
{
    internal static class TouchRadialLayout
    {
        internal const float InnerRadius = 155, OuterRadius = 470;
        internal static float Radius(int index,int count,int inner=-1) => InnerRadius+
            (OuterRadius-InnerRadius)/System.Math.Max(1,TouchRadialPolicy.Rings(count,inner))*(TouchRadialPolicy.RingFor(index,count,inner)+.5f);
        internal static int Hit(float x,float y,int count,int inner=-1)
        {
            if(count<=0||!TouchPointerMath.Finite(x)||!TouchPointerMath.Finite(y))return -1;
            float radius=Mathf.Sqrt(x*x+y*y);
            if(radius<InnerRadius||radius>OuterRadius)return -1;
            int ring=TouchRadialPolicy.Rings(count,inner)>1&&radius>=(InnerRadius+OuterRadius)*.5f?1:0;
            return TouchRadialPolicy.PickRing(x,y,count,ring,inner);
        }
    }

    public static partial class Main
    {
        static GameObject _touchRadialRoot, _touchRadialProjectorRoot, _touchRadialRayRoot;
        static Canvas _touchRadialCanvas;
        static TouchRadialGraphic _touchRadialGraphic;
        static Text _touchRadialTitle, _touchRadialLabel, _touchRadialHint, _touchRadialModeHint;
        static Text _touchRadialPageTitle, _touchRadialWeaponName;
        static Image _touchRadialWeaponMain, _touchRadialWeaponOff;
        static Text[] _touchRadialNavigationLabels = new Text[0];
        static bool _touchRadialFeedbackWeapon, _touchRadialFeedbackGuard;
        static bool _touchRadialWeaponPlaced, _touchRadialWeaponPair, _touchRadialWeaponCentre;
        static TouchRadialIcon[] _touchRadialIcons = new TouchRadialIcon[0];
        static bool[] _touchRadialEnabled = new bool[0];
        static bool[] _touchRadialCharacters = new bool[0], _touchRadialMarked = new bool[0];
        static string[] _touchRadialStateLabels=new string[0];
        static bool[] _touchRadialAbilities=new bool[0];static float[] _touchRadialIconAspects=new float[0];
        static int _touchRadialFeedbackSelection=-2,_touchRadialFeedbackHover=-2;
        static int _touchRadialFeedbackInspect=-2;
        static string _touchRadialFeedbackMessage;
        static int _touchRadialFeedbackLeftMode=-2;
        static int _touchRadialFeedbackLanguage = -1;
        static Material _touchRadialMaterial, _touchRadialTextMaterial;
        static LineRenderer _touchRadialProjector, _touchRadialRayBeam;
        static Camera _touchRadialEyeLeft, _touchRadialEyeRight;
        static bool _touchRadialVisualReady, _touchRadialShown, _touchRadialRayShown;
        static bool _touchRadialLayoutDirty;
        static int _touchRadialRenderLogBudget, _touchRadialMeshVertices, _touchRadialNativeIcons;
        static long _touchRadialLayoutBuilds, _touchRadialHudCameras;
        static int _touchRadialVisualCount = -1;
        static float _touchRadialVisualRetry;
        static Vector3 _touchRadialWorldCenter, _touchRadialPointerPoint;
        static Quaternion _touchRadialWorldRotation;
        static float _touchRadialWorldPixel;
        static bool _touchRadialPlaneReady;
        static Ray _touchRadialSamplePointer;
        static bool _touchRadialSamplePointerReady;
        static bool _touchRadialPointerObserved;
        static TouchRadialDisabledArt _touchRadialDisabledArt;
        static TouchRadialTurnHalo[] _touchRadialHalos = new TouchRadialTurnHalo[0];
        static Text[] _touchRadialOrderLabels = new Text[0];
        static int[] _touchRadialOrders = new int[0];
        internal static bool IsTouchRadialCanvas(Canvas canvas) => canvas != null && canvas == _touchRadialCanvas;
        internal static void IncludeTouchRadialBoundsDepth(ref float depth, Vector3 head, Vector3 forward)
        {
            if(!_touchRadialShown||_touchRadialRoot==null)return;
            var t=_touchRadialRoot.transform; float pixel=t.lossyScale.x;
            float bound=TouchRadialProjection.FurthestDepth(Vector3.Dot(t.position-head,forward),
                Vector3.Dot(t.right,forward)*pixel,Vector3.Dot(t.up,forward)*pixel,!string.IsNullOrEmpty(_touchRadialInformationText));
            if(!float.IsNaN(bound)&&!float.IsInfinity(bound)&&bound>depth)depth=bound;
        }
        static void UpdateTouchRadialVisuals(Camera left, Camera right)
        {
            try { UpdateTouchRadialVisualCore(left, right); }
            catch (Exception error)
            {
                _touchRadial.Cancel(); CloseTouchRadialVisual(); _touchRadialFault = error.Message;
                _log.Error("[touch/radial/visual] Wheel cancelled without stopping VR: " + error.Message);
            }
        }
        static void UpdateTouchRadialVisualCore(Camera left, Camera right)
        {
            _touchRadialEyeLeft = left; _touchRadialEyeRight = right;
            _touchRadialShown = _touchRadial.Visible && TouchInputOwned && _touchSampleValid && !TouchOverlayOpen &&
                !TouchOverlayChordCaptured && left != null && right != null && (!_modeFlat || _radialFlatDrawing);
            if (!_touchRadialShown) { ReleaseRadialCombatStatus(); SetTouchRadialVisible(false, false); return; }
            if (_touchRadialRoot == null)
            {
                if (Time.unscaledTime < _touchRadialVisualRetry) return;
                try { CreateTouchRadialVisual(); }
                catch (Exception error) { DestroyTouchRadialVisual(); _touchRadialVisualRetry = Time.unscaledTime + 3; _log.Error("[touch/radial/visual] " + error.Message); return; }
            }
            if (!TryTouchWorldRay(_touchRadial.Side == 0, out Ray emitter)) { ReleaseRadialCombatStatus(); SetTouchRadialVisible(false, false); return; }
            if (!_touchRadialPlaneReady)
            {
                Vector3 center = (left.transform.position + right.transform.position) * .5f;
                _touchRadialWorldRotation = Quaternion.Slerp(left.transform.rotation, right.transform.rotation, .5f);
                Vector3 local = Quaternion.Inverse(_touchRadialWorldRotation) * (emitter.GetPoint(WorldScale * .12f) - center) / WorldScale;
                local.x = Mathf.Clamp(local.x, -.26f, .26f);
                local.y = Mathf.Clamp(local.y + .06f, -.22f, .18f);
                local.z = Mathf.Clamp(local.z, .38f, .64f);
                _touchRadialWorldCenter = center + _touchRadialWorldRotation * (local * WorldScale);
                _touchRadialWorldPixel = WorldScale * TouchRadialProjection.MetresPerPixel;
                _touchRadialPlaneReady = true;
            }
            // Previously the plane followed a newer pose AFTER input picking.
            // A fixed physical plane keeps the visible sector at the click.
            PositionTouchRadialPlane();
            // Spatial capture owns this canvas camera. Toggling eye/capture
            // cameras every frame invalidated its retained batches twice.
            if ((_radialFlatDrawing || _spatialFault != null) && _touchRadialCanvas.worldCamera != left) _touchRadialCanvas.worldCamera = left;
            _touchRadialVisualReady = true;
            if (_touchRadialVisualCount != _touchRadialEntries.Count) RebuildTouchRadialVisual();
            int selection = _touchRadial.Selected, hovered = _touchRadial.Hovered;
            int leftMode=TouchRadialLeftCanSwitch?(int)TouchRadialLeftMode:-1;
            bool weaponCentre = _radialActiveWeaponName != null;
            bool catalogueGuard = _touchRadial.CatalogueWaitingNeutral || _touchRadial.PointerWaitingExit;
            UpdateTouchRadialWeaponCentre(weaponCentre);
            bool changed = false;
            for (int i = 0; i < _touchRadialIcons.Length; ++i)
            {
                var entry = _touchRadialEntries[i];
                UpdateSpaceRadialStatus(i, entry);
                Sprite art = entry.Enabled ? entry.Icon : _touchRadialDisabledArt.Get(entry.Icon) ?? entry.Icon;
                var tint = entry.Enabled ? entry.IconColor : new Color(.72f,.72f,.72f,1);
                if (_touchRadialIcons[i].sprite != art) _touchRadialIcons[i].sprite = art;
                _touchRadialIcons[i].Accent=entry.Enabled?entry.EndTurnGlow:null;
                if (_touchRadialIcons[i].color != tint) _touchRadialIcons[i].color = tint;
                if (_touchRadialOrderLabels[i] != null && _touchRadialOrders[i] != entry.NativeOrder)
                { _touchRadialOrders[i] = entry.NativeOrder; SetLiveText(_touchRadialOrderLabels[i],entry.NativeOrder < 0 ? "" : (entry.NativeOrder+1).ToString()); }
                if (_touchRadialHalos[i] != null) _touchRadialHalos[i].Animate(Time.unscaledTime);
                UpdateTouchRadialLevelBadge(i,entry);
                bool marked=entry.Selected||entry.Current;
                changed |= _touchRadialEnabled[i] != entry.Enabled || _touchRadialMarked[i] != marked || _touchRadialStateLabels[i]!=entry.StateLabel;
                _touchRadialEnabled[i] = entry.Enabled; _touchRadialMarked[i] = marked;_touchRadialStateLabels[i]=entry.StateLabel;
            }
            _touchRadialDisabledArt.Pump();
            if (changed) _touchRadialGraphic.SetVerticesDirty();
            _touchRadialGraphic.State(_touchRadialEntries.Count, selection, hovered, _touchRadialEnabled, _touchRadialCharacters, _touchRadialMarked,_touchRadialAbilities,_touchRadialIconAspects,_touchRadialInnerCount);
            _touchRadialGraphic.Dwell((float)TouchRadialDwellProgress);
            // No repeated string concatenation or Text dirtying while a user
            // holds the same selection. Native availability is still sampled.
            if (changed || weaponCentre != _touchRadialFeedbackWeapon || catalogueGuard != _touchRadialFeedbackGuard || selection!=_touchRadialFeedbackSelection || hovered!=_touchRadialFeedbackHover || _touchRadial.InspectIndex!=_touchRadialFeedbackInspect || _touchRadialMessage!=_touchRadialFeedbackMessage || leftMode!=_touchRadialFeedbackLeftMode || _touchRadialFeedbackLanguage != ModLocalization.Revision)
            {
            SetLiveText(_touchRadialTitle, weaponCentre ? ModLocalization.Text(_touchRadialSpace ? "ACTIVE WEAPON" : "ACTIVE SET") + (_radialActiveWeaponSet > 0 ? " " + _radialActiveWeaponSet : "") : ModLocalization.Text(_touchRadialSpace && _touchRadial.Side == 0 && !TouchRadialInformationOnly ? "SPACE WEAPONS" : leftMode<0?"COMMAND DECK":TouchRadialLeftMode==RTMaquetaXR.TouchRadialLeftMode.Actions?"ACTIONS":_touchRadialCombat?"COMBATANTS":"PARTY"));
            SetLiveText(_touchRadialPageTitle, TouchRadialRingCaption());
            for (int i = 0; i < _touchRadialNavigationLabels.Length; ++i)
                if (_touchRadialNavigationLabels[i] != null) SetLiveText(_touchRadialNavigationLabels[i], _touchRadialEntries[i].NavigationDelta == 0 ? _touchRadialEntries[i].DisplayLabel : ModLocalization.Text(_touchRadialEntries[i].NavigationDelta < 0 ? "PREVIOUS" : "NEXT"));
            // Keep the latched inspected actor's name through stick centre.
            // Original native details occupy their own centred game layer;
            // the wheel centre stays compact and readable.
            bool pointerPortrait=leftMode>=0&&hovered>=0&&hovered<_touchRadialEntries.Count&&_touchRadialEntries[hovered].Character;
            bool informationMode = TouchRadialInformationOnly;
            bool navigationPreview = _touchRadial.PreviewIndex >= 0 && _touchRadial.PreviewIndex < _touchRadialEntries.Count && _touchRadialEntries[_touchRadial.PreviewIndex].NavigationDelta != 0;
            string stickLabel = selection >= 0 && selection < _touchRadialEntries.Count ? RadialEntryLabel(selection,!pointerPortrait&&!informationMode) : "";
            string pointerLabel = hovered >= 0 && hovered < _touchRadialEntries.Count ? RadialEntryLabel(hovered,!informationMode) : null;
            if(_touchRadialEntries.Count==0)stickLabel=ModLocalization.Text(_touchRadialMessage);
            string inspected = _touchRadial.InspectIndex >= 0 && _touchRadial.InspectIndex < _touchRadialEntries.Count ? RadialEntryLabel(_touchRadial.InspectIndex,false) : null;
            SetLiveText(_touchRadialLabel, informationMode && !navigationPreview ? inspected??pointerLabel??stickLabel :
                TouchRadialAbilityMode ? pointerLabel??stickLabel : pointerLabel == null ? stickLabel : ModLocalization.Format("Click: {0}\nHold: {1}",pointerLabel,stickLabel));
            int preview = hovered >= 0 ? hovered : selection;
            bool endTurn = preview >= 0 && preview < _touchRadialEntries.Count && _touchRadialEntries[preview].EndTurn;
            SetLiveText(_touchRadialHint, _touchRadialEntries.Count == 0 ? "" : ModLocalization.Text(catalogueGuard ? "[L:XY] · Centre  / [A] [RT] · Release" : endTurn ? "[L:XY] / [A] / [RT] · 1 s" : informationMode ? "[RG:PRESS] · INFORMATION" : TouchRadialAbilityMode ? "[A] / [RT] · USE" : (_touchRadial.Side == 0 ? "[L:XY] · 1 s / [RT] · CONFIRM" : "[R:XY] · 1 s / [LT] · CONFIRM")));
            SetLiveText(_touchRadialModeHint,leftMode>=0 && _touchRadialEntries.Count>0?ModLocalization.Text("[L:PRESS] · MODE"):"");
            _touchRadialFeedbackSelection=selection;_touchRadialFeedbackHover=hovered;_touchRadialFeedbackMessage=_touchRadialMessage;
            _touchRadialFeedbackInspect=_touchRadial.InspectIndex;
            _touchRadialFeedbackLeftMode=leftMode;
            _touchRadialFeedbackLanguage = ModLocalization.Revision;
            _touchRadialFeedbackWeapon = weaponCentre; _touchRadialFeedbackGuard = catalogueGuard;
            }
            _touchRadialProjector.SetPosition(0, emitter.origin);
            _touchRadialProjector.SetPosition(1, _touchRadialWorldCenter);
            _touchRadialProjector.startWidth = WorldScale * .002f; _touchRadialProjector.endWidth = WorldScale * .018f;
            // Use the same input sample as the hit and highlighted sector.
            _touchRadialRayShown = _touchRadialSamplePointerReady;
            if (_touchRadialRayShown)
            {
                _touchRadialRayBeam.SetPosition(0, _touchRadialSamplePointer.origin); _touchRadialRayBeam.SetPosition(1, _touchRadialPointerPoint);
                _touchRadialRayBeam.startWidth = _touchRadialRayBeam.endWidth = WorldScale * .0012f;
            }
            PrepareTouchRadialCanvas();
        }
        static string RadialEntryLabel(int index,bool includeStatus=true)
        {
            var entry=_touchRadialEntries[index];
            return entry.DisplayLabel+(includeStatus&&!string.IsNullOrEmpty(entry.StateLabel)?"\n"+RadialPortraitState(entry):entry.Enabled?"":ModLocalization.Text(" (unavailable)"));
        }
        static string RadialPortraitState(TouchRadialEntry entry)
        {
            // These status tokens are authored by the mod. Native health
            // values/names never enter the translation lookup.
            string status = entry.PortraitStatus ?? "";
            int split = status.IndexOf(" · ", StringComparison.Ordinal);
            string localized = split < 0 ? ModLocalization.Text(status) :
                ModLocalization.Text(status.Substring(0, split)) + " · " + ModLocalization.Text(status.Substring(split + 3));
            string hp = TouchRadialPortraitText.PlainHealth(entry.NativeHp);
            return string.IsNullOrEmpty(hp) ? localized : localized + "\n" + ModLocalization.Text("HP") + " " + hp;
        }
        static void PrepareTouchRadialCanvas()
        {
            // Canvas geometry must be present during Unity's normal UI rebuild.
            // Enabling only inside beginCameraRendering was too late for new
            // Text/Image batches. Camera callbacks still restrict visibility.
            UpdateRadialCombatStatus();
            SetTouchRadialVisible(_touchRadialShown, false);
            if (!_touchRadialShown || !_touchRadialLayoutDirty || _touchRadialCanvas == null) return;
            // LateUpdate precedes PlayerUpdateCanvases. Let that single native
            // update build the wheel along with the rest of the UI. Forcing it
            // here rebuilt every pending game window, including combat panels.
            _touchRadialLayoutDirty = false; ++_touchRadialLayoutBuilds;
            LogTouchRadialLayout();
        }
        static void LogTouchRadialLayout()
        {
            var mesh = _touchRadialGraphic.canvasRenderer.GetMesh();
            _touchRadialMeshVertices = mesh == null ? 0 : mesh.vertexCount;
            _touchRadialNativeIcons = 0;
            foreach (var icon in _touchRadialIcons) if (icon != null && icon.sprite != null) ++_touchRadialNativeIcons;
            // Counters are consumed by the existing asynchronous diagnostic
            // snapshot. Six synchronous UMM log writes at wheel opening were
            // correlated with 40–50 ms update spikes in the 0.1.43 trace.
        }
        static int TouchRadialPointerHit()
        {
            _touchRadialSamplePointerReady = false;
            _touchRadialPointerObserved = false;
            if (!_touchRadialVisualReady || !_touchRadialTrackingReady || !_touchRadial.Visible || !_touchSampleValid) return -1;
            var hand = _touchRadial.Side != 0 ? _touchSample.left : _touchSample.right;
            if (!hand.AimValid) return -1;
            _touchRadialPointerObserved = true;
            var origin = TablePoint(hand.aim.Position);
            var direction = TablePoint(hand.aim.Rotation * Vector3.forward);
            if (!TouchRadialProjection.Intersect(origin, direction, TablePoint(_touchRadialTrackingCentre), TableRotation(_touchRadialTrackingRotation),
                TouchRadialProjection.MetresPerPixel, out Point3 local, out Point3 point)) return -1;
            if (!TouchRadialProjection.InPointerField(local)) return -1;
            local = _touchRadialPointerFilter.Step(local, Time.unscaledTime, _touchRadialOpens + _touchRadialModeSwitches);
            point = TablePoint(_touchRadialTrackingCentre) + TableRotation(_touchRadialTrackingRotation).Rotate(local * TouchRadialProjection.MetresPerPixel);
            int index = TouchRadialLayout.Hit(local.x, local.y, _touchRadialEntries.Count, _touchRadialInnerCount);
            _touchRadialTrackingRay = new Ray(TableVector(origin), TableVector(direction));
            _touchRadialSamplePointerReady = true; _touchRadialTrackingHit = TableVector(point);
            return index; // Availability guards commit, not truthful pointing.
        }
        static void CreateTouchRadialVisual()
        {
            _touchRadialMaterial = CreateLiveUiMaterial("RTMaquetaXR imperial cogitator wheel");
            _touchRadialTextMaterial = CreateLiveUiMaterial("RTMaquetaXR cogitator text");
            _touchRadialRoot = new GameObject("RTMaquetaXR Touch radial", typeof(RectTransform), typeof(Canvas));
            _touchRadialRoot.layer = 5; UnityEngine.Object.DontDestroyOnLoad(_touchRadialRoot);
            var rect = (RectTransform)_touchRadialRoot.transform; rect.sizeDelta = new Vector2(1600, 1200);
            _touchRadialCanvas = _touchRadialRoot.GetComponent<Canvas>(); _touchRadialCanvas.renderMode = RenderMode.WorldSpace;
            _touchRadialCanvas.overrideSorting = true; _touchRadialCanvas.sortingOrder = 32763; _touchRadialCanvas.enabled = false;
            var art = new GameObject("Cogitator chassis and medallions", typeof(RectTransform), typeof(CanvasRenderer), typeof(TouchRadialGraphic));
            art.layer = 5; art.transform.SetParent(rect, false); _touchRadialGraphic = art.GetComponent<TouchRadialGraphic>();
            _touchRadialGraphic.rectTransform.sizeDelta = new Vector2(1000, 1000); _touchRadialGraphic.raycastTarget = false; _touchRadialGraphic.material = _touchRadialMaterial;
            _touchRadialTitle = RadialText("Command heading", rect, 21, TouchRadialArtwork.TitlePosition, TouchRadialArtwork.TitleBox);
            _touchRadialTitle.font = _liveHeaderFont!=null ? _liveHeaderFont : _touchRadialTitle.font;
            _touchRadialTitle.color = TouchRadialPalette.Edge;
            _touchRadialTitle.resizeTextForBestFit=true;_touchRadialTitle.resizeTextMinSize=16;_touchRadialTitle.resizeTextMaxSize=21;
            _touchRadialLabel = RadialText("Native action", rect, 32, TouchRadialArtwork.LabelPosition, TouchRadialArtwork.LabelBox);
            _touchRadialLabel.color=TouchRadialPalette.Ink;
            _touchRadialLabel.resizeTextForBestFit = true; _touchRadialLabel.resizeTextMinSize = 18; _touchRadialLabel.resizeTextMaxSize = 32;
            _touchRadialHint = RadialText("Cancel centre", rect, 17, TouchRadialArtwork.CancelPosition, TouchRadialArtwork.CancelBox);
            _touchRadialHint.color = TouchRadialPalette.Ink;
            _touchRadialHint.resizeTextForBestFit=true; _touchRadialHint.resizeTextMinSize=13; _touchRadialHint.resizeTextMaxSize=17;
            _touchRadialModeHint=RadialText("Left wheel switch",rect,15,TouchRadialArtwork.SwitchPosition,TouchRadialArtwork.SwitchBox);
            _touchRadialModeHint.color=TouchRadialPalette.Ink;
            _touchRadialModeHint.resizeTextForBestFit=true; _touchRadialModeHint.resizeTextMinSize=12; _touchRadialModeHint.resizeTextMaxSize=15;
            _touchRadialPageTitle = RadialText("Native action groups",rect,24,new Vector2(625,280),new Vector2(280,80));
            _touchRadialPageTitle.color=TouchRadialPalette.Gold; _touchRadialPageTitle.resizeTextForBestFit=true;
            _touchRadialPageTitle.resizeTextMinSize=20; _touchRadialPageTitle.resizeTextMaxSize=24;
            _touchRadialModeHint.rectTransform.anchoredPosition=new Vector2(450,543); _touchRadialModeHint.rectTransform.sizeDelta=new Vector2(240,108)
            ;_touchRadialModeHint.fontSize=28;_touchRadialModeHint.resizeTextForBestFit=false;((ControlIconText)_touchRadialModeHint).IconBodySize=40;
            _touchRadialHint.rectTransform.anchoredPosition=TouchRadialFooterLayout.HintPosition; _touchRadialHint.rectTransform.sizeDelta=TouchRadialFooterLayout.HintTextSize;
            _touchRadialHint.fontSize=28;_touchRadialHint.resizeTextForBestFit=false;((ControlIconText)_touchRadialHint).IconBodySize=40;
            _touchRadialWeaponName = RadialText("Active equipment native name",rect,23,new Vector2(0,-52),new Vector2(218,90));
            _touchRadialWeaponName.resizeTextForBestFit=true; _touchRadialWeaponName.resizeTextMinSize=16; _touchRadialWeaponName.resizeTextMaxSize=23;
            _touchRadialWeaponName.color=TouchRadialPalette.Ink;
            _touchRadialWeaponMain=CreateTouchRadialWeaponImage(rect,"Active main-hand equipment");
            _touchRadialWeaponOff=CreateTouchRadialWeaponImage(rect,"Active off-hand equipment");
            _touchRadialWeaponPlaced=false;
            SharpenRadialText(_touchRadialPageTitle); SharpenRadialText(_touchRadialWeaponName);
            SharpenRadialText(_touchRadialTitle);SharpenRadialText(_touchRadialLabel);
            SharpenRadialText(_touchRadialHint);SharpenRadialText(_touchRadialModeHint);
            OutlineRadialText(_touchRadialPageTitle);OutlineRadialText(_touchRadialLabel);
            OutlineRadialText(_touchRadialHint);OutlineRadialText(_touchRadialModeHint);
            _touchRadialProjector = RadialBeam("RTMaquetaXR skull hologram", out _touchRadialProjectorRoot, new Color(.13f, 1, .5f, .08f));
            _touchRadialRayBeam = RadialBeam("RTMaquetaXR opposite Touch wheel ray", out _touchRadialRayRoot, new Color(.4f, 1, .7f, .75f));
            RenderPipelineManager.beginCameraRendering += TouchRadialBeginCamera;
            _touchRadialDisabledArt = new TouchRadialDisabledArt();
            RebuildTouchRadialVisual();
        }
        static Text RadialText(string name, RectTransform parent, int fontSize, Vector2 position, Vector2 size)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                name == "Native action" || name == "Explicit navigation or native text" || name == "Active equipment native name" ? typeof(Text) : typeof(ControlIconText)); obj.layer = 5; obj.transform.SetParent(parent, false);
            var text = obj.GetComponent<Text>(); text.font = _liveFont != null ? _liveFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white; text.raycastTarget = false;
            text.supportRichText = false; text.material = _touchRadialTextMaterial;
            text.rectTransform.sizeDelta = size; text.rectTransform.anchoredPosition = position;
            return text;
        }
        static Image CreateTouchRadialWeaponImage(RectTransform parent,string name)
        {
            var obj = new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            obj.layer=5; obj.transform.SetParent(parent,false); var image=obj.GetComponent<Image>();
            image.raycastTarget=false; image.preserveAspect=true; image.material=_touchRadialMaterial; return image;
        }
        static void UpdateTouchRadialWeaponCentre(bool visible)
        {
            if (_touchRadialWeaponName==null) return;
            bool pair=visible&&_radialActiveWeaponMain!=null&&_radialActiveWeaponOff!=null;
            bool mainVisible=visible&&_radialActiveWeaponMain!=null, offVisible=visible&&_radialActiveWeaponOff!=null&&(pair||_radialActiveWeaponMain==null);
            if(_touchRadialWeaponMain.enabled!=mainVisible)_touchRadialWeaponMain.enabled=mainVisible;
            if(_touchRadialWeaponOff.enabled!=offVisible)_touchRadialWeaponOff.enabled=offVisible;
            if(_touchRadialWeaponMain.sprite!=_radialActiveWeaponMain)_touchRadialWeaponMain.sprite=_radialActiveWeaponMain;
            if(_touchRadialWeaponOff.sprite!=_radialActiveWeaponOff)_touchRadialWeaponOff.sprite=_radialActiveWeaponOff;
            if(!_touchRadialWeaponPlaced||pair!=_touchRadialWeaponPair||visible!=_touchRadialWeaponCentre)
            {
                _touchRadialWeaponMain.rectTransform.anchoredPosition=new Vector2(pair?-52:0,35);
                _touchRadialWeaponOff.rectTransform.anchoredPosition=new Vector2(pair?52:0,35);
                _touchRadialWeaponMain.rectTransform.sizeDelta=_touchRadialWeaponOff.rectTransform.sizeDelta=new Vector2(pair?94:178,84);
                _touchRadialLabel.rectTransform.anchoredPosition=visible?TouchRadialFooterLayout.ActionPosition:TouchRadialArtwork.LabelPosition;
                _touchRadialLabel.rectTransform.sizeDelta=(visible?TouchRadialFooterLayout.ActionTextSize:TouchRadialArtwork.LabelBox)*2;
                _touchRadialWeaponPlaced=true; _touchRadialWeaponPair=pair; _touchRadialWeaponCentre=visible;
            }
            SetLiveText(_touchRadialWeaponName,visible?_radialActiveWeaponName:"");
            // Equipped gear stays in the centre; the inspected action never
            // impersonates the active weapon by replacing its portrait.
        }
        static LineRenderer RadialBeam(string name, out GameObject root, Color color)
        {
            root = new GameObject(name, typeof(LineRenderer)); root.layer = 5; UnityEngine.Object.DontDestroyOnLoad(root);
            var beam = root.GetComponent<LineRenderer>(); beam.sharedMaterial = _touchRadialMaterial; beam.enabled = false;
            beam.positionCount = 2; beam.useWorldSpace = true; beam.shadowCastingMode = ShadowCastingMode.Off; beam.receiveShadows = false;
            beam.lightProbeUsage = LightProbeUsage.Off; beam.reflectionProbeUsage = ReflectionProbeUsage.Off;
            beam.startColor = beam.endColor = color; return beam;
        }
        static void RebuildTouchRadialVisual()
        {
            _spatialScanFrame = 0;
            _touchRadialVisualCount = -1; _touchRadialVisualReady = false;
            _touchRadialFeedbackSelection=_touchRadialFeedbackHover=-2;_touchRadialFeedbackMessage=null;
            _touchRadialFeedbackLeftMode=-2;
            _touchRadialLayoutDirty = true; _touchRadialRenderLogBudget = 15;
            if (_touchRadialRoot == null) return;
            foreach (var icon in _touchRadialIcons) if (icon != null) UnityEngine.Object.Destroy(icon.gameObject);
            foreach (var halo in _touchRadialHalos) if (halo != null) halo.Destroy();
            foreach (var order in _touchRadialOrderLabels) if (order != null) UnityEngine.Object.Destroy(order.gameObject);
            foreach (var label in _touchRadialNavigationLabels) if (label != null) UnityEngine.Object.Destroy(label.gameObject);
            ClearTouchRadialLevelBadges();
            int count = _touchRadialEntries.Count, rings = TouchRadialPolicy.Rings(count,_touchRadialInnerCount);
            _radialCrewPortraits=new Image[count]; _radialCooldownLabels=new Text[count];
            _touchRadialGraphic.DangerSlots=new bool[count];
            _touchRadialDisabledArt.SetCatalogue(_touchRadialEntries);
            _touchRadialIcons = new TouchRadialIcon[count]; _touchRadialEnabled = new bool[count];
            _touchRadialHalos = new TouchRadialTurnHalo[count]; _touchRadialOrderLabels = new Text[count]; _touchRadialOrders = new int[count];
            _touchRadialCharacters=new bool[count];_touchRadialMarked=new bool[count];
            _touchRadialStateLabels=new string[count];
            _touchRadialAbilities=new bool[count];_touchRadialIconAspects=new float[count];
            _touchRadialNavigationLabels=new Text[count];
            for (int i = 0; i < count; ++i)
            {
                var obj = new GameObject("Native cogitator medallion " + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(TouchRadialIcon));
                obj.layer = 5; obj.transform.SetParent(_touchRadialRoot.transform, false);
                var entry=_touchRadialEntries[i];var icon = obj.GetComponent<TouchRadialIcon>(); icon.sprite = entry.Icon; icon.raycastTarget = false;
                icon.Portrait=entry.Character;icon.color=entry.IconColor;icon.material = _touchRadialMaterial;
                icon.Accent=entry.Enabled?entry.EndTurnGlow:null;
                float size = TouchRadialArtwork.IconSize(i,count,entry.Character,_touchRadialInnerCount);
                icon.rectTransform.sizeDelta = new Vector2(size, size); float angle = (float)TouchRadialPolicy.Angle(i, count,_touchRadialInnerCount);
                float radius = TouchRadialLayout.Radius(i, count,_touchRadialInnerCount);
                icon.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * radius;
                if(entry.NavigationDelta!=0 || entry.Icon==null)
                {
                    var label=RadialText("Explicit navigation or native text",(RectTransform)_touchRadialRoot.transform,entry.NavigationDelta!=0?24:21,icon.rectTransform.anchoredPosition,new Vector2(size+12,size));
                    label.resizeTextForBestFit=true;label.resizeTextMinSize=17;label.resizeTextMaxSize=entry.NavigationDelta!=0?24:21;
                    label.color=entry.NavigationDelta!=0?TouchRadialPalette.Cyan:TouchRadialPalette.Ink;SharpenRadialText(label);
                    SetLiveText(label,entry.NavigationDelta!=0?ModLocalization.Text(entry.NavigationDelta<0?"PREVIOUS":"NEXT"):entry.DisplayLabel);
                    _touchRadialNavigationLabels[i]=label;
                }
                if (entry.Character && !entry.Party)
                {
                    // The badge stays in its own sector, beyond the portrait
                    // rim, so neither the turn number nor level-up hides a face.
                    float badgeSize=TouchRadialBadgeLayout.Size(i,count,size,_touchRadialInnerCount);
                    var badge=CreateTouchRadialPortraitBadge(icon.rectTransform,"Native turn order badge",new Vector2(.5f,.5f),badgeSize);
                    badge.rectTransform.anchoredPosition=TouchRadialBadgeLayout.Offset(i,count,size,badgeSize,_touchRadialInnerCount);
                    _touchRadialOrderLabels[i] = RadialText("Native turn order",badge.rectTransform,Mathf.Max(18,Mathf.RoundToInt(badgeSize*.72f)),Vector2.zero,new Vector2(badgeSize,badgeSize));
                    _touchRadialOrderLabels[i].color=TouchRadialPalette.Ink;
                    SharpenRadialText(_touchRadialOrderLabels[i]);
                    _touchRadialOrders[i]=entry.NativeOrder;
                    SetLiveText(_touchRadialOrderLabels[i],entry.NativeOrder < 0 ? "" : (entry.NativeOrder+1).ToString());
                    if (entry.Current && entry.Player)
                    {
                        _touchRadialHalos[i]=new TouchRadialTurnHalo(_touchRadialRoot.transform,_touchRadialMaterial,size*.5f+11);
                        _touchRadialHalos[i].Root.transform.localPosition=icon.rectTransform.anchoredPosition;
                    }
                }
                // A missing native icon remains a textual action; never invent
                // an unrelated replacement image or a decorative fake ability.
                icon.enabled = icon.sprite != null;
                _touchRadialIcons[i] = icon; _touchRadialEnabled[i] = _touchRadialEntries[i].Enabled;
                _touchRadialCharacters[i]=entry.Character;_touchRadialMarked[i]=entry.Selected||entry.Current;_touchRadialStateLabels[i]=entry.StateLabel;
                _touchRadialAbilities[i]=entry.Ability;
                _touchRadialGraphic.DangerSlots[i]=entry.EndTurn;
                CreateSpaceRadialStatus(i,entry,icon.rectTransform,size);
                _touchRadialIconAspects[i]=entry.Icon!=null?entry.Icon.rect.width/Mathf.Max(1,entry.Icon.rect.height):1;
            }
            CreateTouchRadialLevelBadges();
            _touchRadialGraphic.State(count, -1, -1, _touchRadialEnabled,_touchRadialCharacters,_touchRadialMarked,_touchRadialAbilities,_touchRadialIconAspects,_touchRadialInnerCount); _touchRadialVisualCount = count;
        }
        static void TouchRadialBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            bool eye = camera == _touchRadialEyeLeft || camera == _touchRadialEyeRight;
            bool capture = _spatialFault == null ? IsSpatialCaptureCamera(camera) : IsHudCaptureCamera(camera);
            // The private layer isolates the canvas. Keep it enabled through
            // the normal uGUI rebuild and intervening scene/HUD cameras;
            // toggling it off there discards batches before the spatial pass.
            SetTouchRadialVisible(_touchRadialShown && (_spatialFault == null || eye || capture), _touchRadialShown && eye);
            if (!_touchRadialShown || (!eye && !capture) || _touchRadialRoot == null) return;
            RecordTouchRadialCamera(camera, eye, capture);
        }
        static void RecordTouchRadialCamera(Camera camera, bool eye, bool capture)
        {
            if (capture) ++_touchRadialHudCameras;
            int bit = eye ? (camera == _touchRadialEyeLeft ? 1 : 2) : camera == _hudBlackCamera ? 4 : 8;
            if ((_touchRadialRenderLogBudget & bit) == 0) return;
            _touchRadialRenderLogBudget &= ~bit;
            // Do not format/log four camera descriptions from the render
            // callback. Membership and draw counts stay available in snapshots.
        }
        static void SetTouchRadialVisible(bool canvas, bool beams)
        {
            if (_touchRadialCanvas != null && _touchRadialCanvas.enabled!=canvas) _touchRadialCanvas.enabled = canvas;
            foreach(var halo in _touchRadialHalos) if(halo!=null&&halo.Renderer!=null&&halo.Renderer.enabled!=canvas)halo.Renderer.enabled=canvas;
            if (_touchRadialProjector != null && _touchRadialProjector.enabled!=beams) _touchRadialProjector.enabled = beams;
            if (_touchRadialRayBeam != null && _touchRadialRayBeam.enabled!=(beams&&_touchRadialRayShown)) _touchRadialRayBeam.enabled = beams && _touchRadialRayShown;
        }
        static void CloseTouchRadialVisual()
        { ReleaseRadialCombatStatus(); _touchRadialShown = _touchRadialVisualReady = _touchRadialRayShown = false; _touchRadialTrackingReady = _touchRadialPlaneReady = _touchRadialSamplePointerReady = false; SetTouchRadialVisible(false, false); }
        static void DestroyTouchRadialVisual()
        {
            DestroyFlatRadial();
            RenderPipelineManager.beginCameraRendering -= TouchRadialBeginCamera; CloseTouchRadialVisual();
            DestroyRetainedRadialCombatStatus();
            foreach(var halo in _touchRadialHalos) if(halo!=null)halo.Destroy();
            _touchRadialHalos=new TouchRadialTurnHalo[0];_touchRadialOrderLabels=new Text[0];_touchRadialOrders=new int[0];
            _touchRadialDisabledArt?.Dispose(); _touchRadialDisabledArt=null;
            ClearTouchRadialLevelBadges();
            if (_touchRadialRoot != null) UnityEngine.Object.Destroy(_touchRadialRoot);
            if (_touchRadialProjectorRoot != null) UnityEngine.Object.Destroy(_touchRadialProjectorRoot);
            if (_touchRadialRayRoot != null) UnityEngine.Object.Destroy(_touchRadialRayRoot);
            if (_touchRadialMaterial != null) UnityEngine.Object.Destroy(_touchRadialMaterial);
            if (_touchRadialTextMaterial != null) UnityEngine.Object.Destroy(_touchRadialTextMaterial);
            _touchRadialRoot = _touchRadialProjectorRoot = _touchRadialRayRoot = null; _touchRadialCanvas = null;
            _touchRadialMaterial = _touchRadialTextMaterial = null; _touchRadialIcons = new TouchRadialIcon[0]; _touchRadialEnabled = new bool[0];
            _touchRadialCharacters=_touchRadialMarked=new bool[0];_touchRadialFeedbackSelection=_touchRadialFeedbackHover=-2;_touchRadialFeedbackMessage=null;
            _touchRadialStateLabels=new string[0];
            _touchRadialFeedbackLeftMode=-2;_touchRadialModeHint=null;
            _touchRadialNavigationLabels=new Text[0];_touchRadialPageTitle=_touchRadialWeaponName=null;
            _touchRadialWeaponMain=_touchRadialWeaponOff=null;_touchRadialWeaponPlaced=false;
            _touchRadialAbilities=new bool[0];_touchRadialIconAspects=new float[0];
            _touchRadialVisualCount = -1; _touchRadialVisualRetry = 0; _touchRadialProjector = _touchRadialRayBeam = null;
            _touchRadialLayoutDirty = false; _touchRadialRenderLogBudget = _touchRadialMeshVertices = _touchRadialNativeIcons = 0;
        }
    }
}
