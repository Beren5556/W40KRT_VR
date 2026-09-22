using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static GameObject _touchPointerRoot, _touchBeamRoot, _touchGestureRoot;
        static Canvas _touchPointerCanvas, _touchGestureCanvas;
        static TouchPointerGraphic _touchPointerGraphic, _touchGestureGraphic;
        static Text _touchPointerLabel;
        static RawImage _touchGesturePortraitLeft, _touchGesturePortraitRight;
        static LineRenderer _touchBeam;
        static Material _touchPointerMaterial, _touchPointerTextMaterial;
        static Camera _touchPointerLeft, _touchPointerRight;
        static bool _touchVisualShown, _touchBeamShown, _touchReticleShown, _touchGestureShown;
        static float _touchVisualRetry;
        static int _touchVisualFailures;
        static string _touchVisualLabel;
        internal static bool TouchHandsVisible => _cfg.touchHandsVisible;

        internal static void UpdateTouchPointer(Camera left, Camera right)
        {
            EnsureTouchSample();
            _touchPointerLeft = left; _touchPointerRight = right;
            _touchVisualShown = TouchInputOwned && _touchSampleValid && !TouchOverlayOpen && !TouchRadialCaptured && !_modeFlat && left != null && right != null;
            if (!_touchVisualShown) { SetTouchVisualVisible(false); return; }
            if (_touchPointerRoot == null && Time.unscaledTime >= _touchVisualRetry && _touchVisualFailures < 5)
            {
                try { CreateTouchPointer(); }
                catch (Exception error)
                {
                    DestroyTouchPointer(); ++_touchVisualFailures; _touchVisualRetry = Time.unscaledTime + 2;
                    _log.Error("[touch/pointer] " + error.Message); return;
                }
            }
            if (_touchPointerRoot == null) return;
            Vector3 center = (left.transform.position + right.transform.position) * .5f;
            Quaternion rotation = Quaternion.Slerp(left.transform.rotation, right.transform.rotation, .5f);
            bool gesture = TouchGestureActive || TouchManipulationRequested;
            UpdateTouchGestureHelp(left, right, center, rotation, gesture || _touchBox.Active);
            Ray ray = _touchRay;
            bool haveRay = _touchRayReady;
            if (gesture || !haveRay) haveRay = TryTouchWorldRay(false, out ray);
            Vector3 target = !gesture && _touchHasPoint ? _touchTargetPoint : haveRay ? ray.GetPoint(WorldScale * .85f) : center + rotation * Vector3.forward * WorldScale;
            if(!gesture&&_touchWorldHit68&&_touchTarget!=null&&haveRay)
            {
                target=_touchWorldResult68.worldPosition;
            }
            if (!gesture && _touchOverUi && !_touchWorldHit68 && _touchHasPoint && _pickCam != null && _touchTarget != null)
            {
                // The HUD follows the new head pose in LateUpdate. Reproject the
                // screen coordinate accepted by the UI into its updated plane,
                // rather than leaving its reticle at last frame's world point.
                Transform uiTarget = _touchTarget.transform;
                Ray uiRay = _pickCam.ScreenPointToRay(_touchScreen);
                if (new Plane(uiTarget.forward, uiTarget.position).Raycast(uiRay, out float uiDistance))
                    target = HudRenderToWorldPoint(uiRay.GetPoint(uiDistance));
            }
            if (Vector3.Dot(target - center, rotation * Vector3.forward) <= .02f)
            { _touchReticleShown = _touchBeamShown = false; SetTouchVisualVisible(true); return; }
            float distance = Vector3.Distance(center, target);
            // Slightly toward the eyes to avoid coplanar flicker; keep the visual
            // and the game's actual target aligned in angular position.
            target = Vector3.MoveTowards(target, center, Mathf.Min(.01f * WorldScale, distance * .005f));
            float pointerScale=Mathf.Max(.0001f,distance*.00065f);
            if (_touchOverUi&&!_touchWorldHit68) PositionHudHelper(_touchPointerRoot.transform, target, rotation,pointerScale);
            else { _touchPointerRoot.transform.SetPositionAndRotation(target, rotation); _touchPointerRoot.transform.localScale=Vector3.one*pointerScale; }
            _touchPointerCanvas.worldCamera = left;
            // Green means an actionable UI/interaction target. Ordinary
            // terrain intersections remain white, so the user can tell when
            // the original game marker will accept the trigger.
            _touchPointerGraphic.SetState(TouchGlyphMode.Aim, false, false, false, _touchWorldHit68 || _touchOverUi);
            _touchReticleShown = !gesture && haveRay;
            _touchBeamShown = haveRay && !gesture;
            _touchBeam.enabled = _touchBeamShown;
            if (_touchBeam.enabled)
            {
                // Selection can keep a pressed world ray until release. Its
                // target stays authoritative, but the visible emitter follows
                // the live screen of the servo-skull as the hand moves.
                if (TryTouchWorldRay(false, out Ray liveRay))
                {
                    _touchBeam.SetPosition(0, liveRay.origin); _touchBeam.SetPosition(1, target);
                    _touchBeam.startWidth = _touchBeam.endWidth = WorldScale * .00065f;
                }
                else
                {
                    // A grip-only skull can remain visible during brief aim
                    // loss, but a stale laser must not detach from its screen.
                    _touchBeamShown = false; _touchBeam.enabled = false;
                }
            }
            SetTouchVisualVisible(true);
        }
        static void UpdateTouchGestureHelp(Camera left, Camera right, Vector3 center, Quaternion rotation, bool gesture)
        {
            // The holographic wheel already labels its grip and confirmation.
            // Its captured tabletop state is WaitingForRelease, not an actual
            // request to release the grip while choosing a command.
            if (_touchRadial.Visible) { _touchGestureShown = false; return; }
            string distanceFeedback = TouchDistanceFeedback;
            _touchGestureShown = (gesture && _cfg.touchGestureHelp) || distanceFeedback != null;
            if (!_touchGestureShown) return;
            float near = Mathf.Max(left.nearClipPlane, right.nearClipPlane);
            float far = Mathf.Min(left.farClipPlane, right.farClipPlane);
            if (!LiveOverlayPolicy.TryDistance(near, far, WorldScale, out float distance))
            { _touchGestureShown = false; return; }
            // Head-relative, right of center and slightly down/in from the old
            // corner position. Never follows the ray or rotating table.
            PositionHudHelper(_touchGestureRoot.transform, center + rotation * new Vector3(TouchGestureLayout.Horizontal * distance, TouchGestureLayout.Vertical * distance, distance), rotation,
                distance * TouchGestureLayout.ScalePerDistance);
            _touchGestureCanvas.worldCamera = left;
            if (_touchPointerLabel.font == null && _liveFont != null) _touchPointerLabel.font = _liveFont;
            TouchGlyphMode mode = _touchTabletop.Gesture == TouchTabletopGesture.RotateAndScale ? TouchGlyphMode.RotateScale :
                _touchTabletop.Gesture == TouchTabletopGesture.Turn ? TouchGlyphMode.Turn :
                _touchTabletop.Gesture == TouchTabletopGesture.Tilt ? TouchGlyphMode.Tilt :
                _touchTabletop.Gesture == TouchTabletopGesture.WaitingForRelease ? TouchGlyphMode.Release : TouchGlyphMode.Pan;
            bool selecting = _touchBox.Active;
            TouchControllerArtwork.Place(_touchGesturePortraitLeft,true,new Vector2(-28,-1),62,!selecting && _touchFrame.left.squeeze>.5f);
            TouchControllerArtwork.Place(_touchGesturePortraitRight,false,new Vector2(28,-1),62,selecting || _touchFrame.right.squeeze>.5f);
            _touchGestureGraphic.SetState(selecting ? TouchGlyphMode.Selection : mode, !selecting && _touchFrame.left.squeeze > .5f, selecting || _touchFrame.right.squeeze > .5f, !selecting && TouchGestureLimited, selecting);
            string label = selecting ? ModLocalization.Text("Select · release right trigger") : (TouchGestureLimited ? ModLocalization.Text("LIMIT · ") : "") +
                ModLocalization.Text(mode == TouchGlyphMode.RotateScale ? "Rotate / zoom" : mode == TouchGlyphMode.Turn ? "Rotate · left stick" : mode == TouchGlyphMode.Tilt ? "Tilt" : mode == TouchGlyphMode.Release ? "Release grips" : "Move table");
            if (distanceFeedback != null) label = distanceFeedback;
            if (_touchVisualLabel != label) { _touchVisualLabel = label; _touchPointerLabel.text = label; }
        }
        static bool IsTouchOwnObject(GameObject item) => item != null &&
            ((_touchPointerRoot != null && item.transform.IsChildOf(_touchPointerRoot.transform)) ||
             (_touchGestureRoot != null && item.transform.IsChildOf(_touchGestureRoot.transform)) ||
             (_touchProximityRoot73 != null && item.transform.IsChildOf(_touchProximityRoot73.transform)) ||
             (_liveRoot != null && item.transform.IsChildOf(_liveRoot.transform)));
        static void SetTouchVisualVisible(bool visible)
        {
            if (_touchPointerCanvas != null) _touchPointerCanvas.enabled = visible && _touchReticleShown;
            if (_touchGestureCanvas != null) _touchGestureCanvas.enabled = visible && _touchGestureShown;
            if (_touchBeam != null) _touchBeam.enabled = visible && _touchBeamShown;
        }
        static void TouchPointerBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            bool eye=camera==_touchPointerLeft||camera==_touchPointerRight;
            bool panelCapture=!_touchWorldHit68&&IsHudCaptureCamera(camera);
            SetTouchVisualVisible(_touchVisualShown&&(eye||panelCapture));
        }
        static void CreateTouchPointer()
        {
            _touchPointerMaterial = CreateLiveUiMaterial("RTMaquetaXR Touch vectors");
            _touchPointerTextMaterial = CreateLiveUiMaterial("RTMaquetaXR Touch text");
            _touchPointerRoot = new GameObject("RTMaquetaXR Touch pointer", typeof(RectTransform), typeof(Canvas));
            UnityEngine.Object.DontDestroyOnLoad(_touchPointerRoot); _touchPointerRoot.layer = 5;
            _touchPointerCanvas = _touchPointerRoot.GetComponent<Canvas>();
            _touchPointerCanvas.renderMode = RenderMode.WorldSpace; _touchPointerCanvas.sortingOrder = 32762;
            ((RectTransform)_touchPointerRoot.transform).sizeDelta = new Vector2(180, 180);
            var art = new GameObject("Touch aim reticle", typeof(RectTransform), typeof(CanvasRenderer), typeof(TouchPointerGraphic));
            art.layer = 5; art.transform.SetParent(_touchPointerRoot.transform, false);
            _touchPointerGraphic = art.GetComponent<TouchPointerGraphic>();
            _touchPointerGraphic.material = _touchPointerMaterial; _touchPointerGraphic.raycastTarget = false;
            _touchPointerGraphic.rectTransform.sizeDelta = new Vector2(150, 150);
            _touchGestureRoot = new GameObject("RTMaquetaXR Touch gesture help", typeof(RectTransform), typeof(Canvas));
            UnityEngine.Object.DontDestroyOnLoad(_touchGestureRoot); _touchGestureRoot.layer = 5;
            _touchGestureCanvas = _touchGestureRoot.GetComponent<Canvas>();
            _touchGestureCanvas.renderMode = RenderMode.WorldSpace; _touchGestureCanvas.sortingOrder = 32762;
            _touchGestureCanvas.enabled = false;
            ((RectTransform)_touchGestureRoot.transform).sizeDelta = new Vector2(180, 180);
            var gestureArt = new GameObject("Servo-skull gesture illustration · L / R", typeof(RectTransform), typeof(CanvasRenderer), typeof(TouchPointerGraphic));
            gestureArt.layer = 5; gestureArt.transform.SetParent(_touchGestureRoot.transform, false);
            _touchGestureGraphic = gestureArt.GetComponent<TouchPointerGraphic>();
            _touchGestureGraphic.material = _touchPointerMaterial; _touchGestureGraphic.raycastTarget = false;
            _touchGestureGraphic.rectTransform.sizeDelta = new Vector2(150, 150);
            _touchGesturePortraitLeft = TouchControllerArtwork.Create(gestureArt.transform,true,_touchPointerMaterial);
            _touchGesturePortraitRight = TouchControllerArtwork.Create(gestureArt.transform,false,_touchPointerMaterial);
            _touchGestureGraphic.PlaceAnnotationsOverPortraits();
            var text = new GameObject("Gesture", typeof(RectTransform), typeof(CanvasRenderer), typeof(ControlIconText));
            text.layer = 5; text.transform.SetParent(_touchGestureRoot.transform, false);
            _touchPointerLabel = text.GetComponent<Text>(); _touchPointerLabel.font = _liveFont;
            _touchPointerLabel.fontSize = 16; _touchPointerLabel.alignment = TextAnchor.MiddleCenter;
            _touchPointerLabel.color = Color.white; _touchPointerLabel.material = _touchPointerTextMaterial; _touchPointerLabel.raycastTarget = false;
            _touchPointerLabel.rectTransform.sizeDelta = new Vector2(270, 44);
            _touchPointerLabel.rectTransform.anchoredPosition = new Vector2(0, -81);
            _touchBeamRoot = new GameObject("RTMaquetaXR Touch ray", typeof(LineRenderer));
            UnityEngine.Object.DontDestroyOnLoad(_touchBeamRoot); _touchBeamRoot.layer = 5;
            _touchBeam = _touchBeamRoot.GetComponent<LineRenderer>(); _touchBeam.sharedMaterial = _touchPointerMaterial;
            _touchBeam.positionCount = 2; _touchBeam.useWorldSpace = true; _touchBeam.shadowCastingMode = ShadowCastingMode.Off;
            _touchBeam.receiveShadows = false; _touchBeam.lightProbeUsage = LightProbeUsage.Off; _touchBeam.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _touchBeam.startColor = new Color(.25f, .9f, 1, .32f); _touchBeam.endColor = new Color(.25f, .9f, 1, .8f);
            RenderPipelineManager.beginCameraRendering += TouchPointerBeginCamera;
        }
        static void DestroyTouchPointer()
        {
            RenderPipelineManager.beginCameraRendering -= TouchPointerBeginCamera;
            if (_touchPointerRoot != null) UnityEngine.Object.Destroy(_touchPointerRoot);
            if (_touchGestureRoot != null) UnityEngine.Object.Destroy(_touchGestureRoot);
            if (_touchBeamRoot != null) UnityEngine.Object.Destroy(_touchBeamRoot);
            if (_touchPointerMaterial != null) UnityEngine.Object.Destroy(_touchPointerMaterial);
            if (_touchPointerTextMaterial != null) UnityEngine.Object.Destroy(_touchPointerTextMaterial);
            _touchPointerRoot = _touchBeamRoot = _touchGestureRoot = null;
            _touchPointerCanvas = _touchGestureCanvas = null; _touchBeam = null; _touchPointerGraphic = _touchGestureGraphic = null; _touchPointerLabel = null;
            _touchGesturePortraitLeft = _touchGesturePortraitRight = null;
            _touchPointerMaterial = _touchPointerTextMaterial = null; _touchVisualLabel = null;
            _touchVisualShown = _touchBeamShown = _touchReticleShown = _touchGestureShown = false;
        }
    }
}
