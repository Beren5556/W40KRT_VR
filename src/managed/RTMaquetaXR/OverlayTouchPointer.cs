using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly OverlayTouchPointerPress _overlayPointerPress = new OverlayTouchPointerPress();
        static readonly OverlayTouchPointerContextGuard _overlayPointerContext = new OverlayTouchPointerContextGuard();
        static readonly TouchButtonLatch _touchOverlayConfirm = new TouchButtonLatch();
        static Vector2 _overlayPointerPixel;
        static bool _overlayPointerVisible;
        static Image _overlayPointerDot;
        static float _overlayPointerTime;

        static void ResetOverlayTouchPointer()
        {
            _overlayPointerPress.Cancel(); _overlayPointerVisible = false;
            _touchOverlayConfirm.Cancel();
            _overlayPointerContext.Reset();
            if (_overlayPointerDot != null) _overlayPointerDot.enabled = false;
        }

        // Returns true only when the ray owns the trigger. Button/stick
        // navigation stays available without sending events to the game's UI.
        static bool ProcessTouchOverlayPointer(bool navigationUsed, out bool changed)
        {
            changed = false;
            if (navigationUsed || !TouchOverlayOpen || !_touchSampleValid || TouchOverlayChordCaptured || !LiveOverlayVr)
            { ResetOverlayTouchPointer(); return false; }
            bool hit = TryOverlayTouchPixel(out _overlayPointerPixel, out var context);
            // The welcome is a stable two-button modal. If the native map's
            // quad snapshot is briefly unavailable, keep explicit focused
            // A/trigger navigation usable. Never replay a captured pointer Up.
            if ((TouchQuickGuideVisible || TurnConfirmationVisible) && !context.Ready && !_overlayPointerPress.Captured)
            { _overlayPointerVisible = false; _overlayPointerContext.Reset(); return false; }
            if (_overlayPointerContext.Observe(context, _touchOverlayConfirm.Down, _touchOverlayConfirm.Held,
                _touchOverlayConfirm.Up, out bool contextConsumes))
            {
                _overlayPointerPress.Cancel(); _overlayPointerVisible = false;
                UpdateOverlayTouchCursor(); return contextConsumes;
            }
            _overlayPointerVisible = hit; _overlayPointerTime = Time.unscaledTime;
            int target = hit ? LiveOverlayPointerHit(_overlayPointerPixel.x, _overlayPointerPixel.y) : -1;
            int action = _overlayPointerPress.Step(true, hit, target, _liveNavigation.Revision,
                _touchOverlayConfirm.Down, _touchOverlayConfirm.Up, out bool consume);
            if (action >= 0)
            {
                changed = _liveNavigation.Select(action);
                var option = CurrentLiveOption();
                if (option != null && (option.Menu != null || option.Action != null || option.Command != OverlayCommand.None))
                    changed |= _liveNavigation.Execute();
            }
            else if (action == -2 || action == -3) changed = _liveNavigation.Change(action == -2 ? -1 : 1);
            else if (action == -4) changed = _liveNavigation.Execute();
            else if (action == -5 || action == -6)
                changed = _liveNavigation.Select(Mathf.Clamp(_livePage + (action == -5 ? -1 : 1) * LiveOverlayLayout.VisibleRows, 0, _liveOptions.Count - 1));
            if (!TouchOverlayOpen) ResetOverlayTouchPointer();
            else UpdateOverlayTouchCursor();
            return consume;
        }

        static bool TryOverlayTouchPixel(out Vector2 pixel, out OverlayTouchPointerContext context)
        {
            pixel = default(Vector2);
            context = new OverlayTouchPointerContext {
                Flat = _modeFlat || !_attached, ScreenWidth = Screen.width, ScreenHeight = Screen.height,
                Root = _liveRoot == null ? 0 : _liveRoot.GetInstanceID()
            };
            if (_modeFlat || !_attached)
            {
                if (!OpenXR.GetFlatPanel(out var panel) || panel.valid == 0) return false;
                if (!TryCurrentLiveFlatPlacement(Screen.width, Screen.height, out float x, out float y, out float scale)) return false;
                context.Ready = panel.width > 0 && panel.height > 0 && TouchPointerMath.Finite(panel.width) && TouchPointerMath.Finite(panel.height);
                context.PanelWidth = panel.width; context.PanelHeight = panel.height; context.Flip = panel.flipVertical != 0;
                Quaternion inverse = Quaternion.Inverse(panel.pose.Rotation);
                if (!TouchPointerMath.PanelPoint(ToPoint(inverse * (_touchSample.right.aim.Position - panel.pose.Position)),
                    ToPoint(inverse * (_touchSample.right.aim.Rotation * Vector3.forward)), panel.width, panel.height,
                    panel.flipVertical != 0, out float u, out float v) || !TouchPointerMath.InPanel(u, v)) return false;
                pixel = new Vector2((u * Screen.width - x) / scale, ((1 - v) * Screen.height - y) / scale);
            }
            else
            {
                if (_liveRoot == null || _liveLeft == null || _liveRight == null || !TryTouchWorldRay(false, out Ray ray)) return false;
                ray = HudWorldToRenderRay(ray);
                context.Ready = true; context.Left = _liveLeft.GetInstanceID(); context.Right = _liveRight.GetInstanceID();
                Transform root = _liveRoot.transform;
                if (!TouchPointerMath.PanelPoint(ToPoint(root.InverseTransformPoint(ray.origin)),
                    ToPoint(root.InverseTransformVector(ray.direction)), CurrentLivePanelWidth, CurrentLivePanelHeight, false,
                    out float u, out float v)) return false;
                pixel = new Vector2(u * CurrentLivePanelWidth, (1 - v) * CurrentLivePanelHeight);
            }
            return TouchPointerMath.InPanel(pixel.x / CurrentLivePanelWidth, pixel.y / CurrentLivePanelHeight);
        }

        static void UpdateOverlayTouchCursor()
        {
            if (_modeFlat || !_attached || !_overlayPointerVisible)
            { if (_overlayPointerDot != null) _overlayPointerDot.enabled = false; return; }
            if (_liveRoot == null) return;
            if (_overlayPointerDot != null && _overlayPointerDot.rectTransform.parent != _liveRoot.transform)
            { UnityEngine.Object.Destroy(_overlayPointerDot.gameObject); _overlayPointerDot = null; }
            if (_overlayPointerDot == null)
            {
                var dot = new GameObject("RTMaquetaXR Overlay Touch cursor", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                // New UI nodes keep their original game layer in the restore
                // ledger, even when their parent is already capture-isolated.
                dot.layer = 5; dot.transform.SetParent(_liveRoot.transform, false);
                _overlayPointerDot = dot.GetComponent<Image>();
                _overlayPointerDot.raycastTarget = false; _overlayPointerDot.material = _liveImageMaterial;
                var rect = _overlayPointerDot.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(.5f, .5f);
                rect.sizeDelta = new Vector2(9, 9);
            }
            _overlayPointerDot.rectTransform.anchoredPosition = new Vector2(_overlayPointerPixel.x, -_overlayPointerPixel.y);
            _overlayPointerDot.color = _overlayPointerPress.Captured ? new Color(1, .8f, .4f) : new Color(.65f, 1, .65f);
            _overlayPointerDot.enabled = true;
        }

        static void DrawFlatOverlayTouchCursor()
        {
            if (!_overlayPointerVisible || !TouchOverlayOpen || Time.unscaledTime - _overlayPointerTime > .2f) return;
            Color previous = GUI.color;
            GUI.color = _overlayPointerPress.Captured ? new Color(1, .8f, .4f) : new Color(.65f, 1, .65f);
            GUI.DrawTexture(new Rect(_overlayPointerPixel.x - 4.5f, _overlayPointerPixel.y - 4.5f, 9, 9), Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
