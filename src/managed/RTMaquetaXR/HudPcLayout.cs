using System.Collections.Generic;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Vector2 _hudReferenceSize = new Vector2(HudPanelLayout.ReferenceWidth, HudPanelLayout.ReferenceHeight);
        static Vector2 _hudNativeReferenceSize = new Vector2(HudPanelLayout.ReferenceWidth, HudPanelLayout.ReferenceHeight);
        static float _hudPanelDistanceWorld;
        static float _hudPanelReferenceWidth => Mathf.Max(320, _hudReferenceSize.x);
        static HudFrustumPlane[] _hudViewPlanes;
        static int _hudViewportRevision = 1;
        static float _hudAutoAspect = 1;
        internal static string HudAspectDescription => ModLocalization.Text(_cfg.uiAspect <= 0 ? "Headset · " : "Manual · ") +
            EffectiveHudAspect.ToString("0.00", ModLocalization.Culture) + " : 1";

        static float EffectiveHudAspect
        {
            get
            {
                if (ManagementPresentation) return ManagementPresentationAspect > 0 ? ManagementPresentationAspect : _hudNativeReferenceSize.x / Mathf.Max(1, _hudNativeReferenceSize.y);
                if (_cfg.uiAspect > 0) return HudPanelLayout.ClampAspect(_cfg.uiAspect);
                float measured = HudPanelLayout.ViewAspect(Mathf.Max(.5f, _cfg.uiDistance) * .95f, _hudViewPlanes);
                // Sub-pixel pose rounding must not trigger PC layout rebuilds.
                if (Mathf.Abs(measured - _hudAutoAspect) > .025f)
                    _hudAutoAspect = Mathf.Round(measured * 100) / 100;
                return _hudAutoAspect;
            }
        }

        internal static void SetHudAspect(float value)
        {
            _cfg.uiAspect = value <= 0 ? 0 : HudPanelLayout.ClampAspect(value);
            _logUiProjNow = true; MarkSettingsDirty();
        }

        internal static void CycleHudAspect(int direction)
        {
            if (_cfg.uiAspect <= 0) SetHudAspect(direction < 0 ? 2.4f : .8f);
            else
            {
                float next = Mathf.Round((_cfg.uiAspect + direction * .1f) * 10) / 10;
                SetHudAspect(next < .79f || next > 2.41f ? 0 : next);
            }
        }

        internal static void SetHudOffsetX(float value)
        { _cfg.uiOffsetX = HudPanelLayout.ClampOffset(value); _logUiProjNow = true; MarkSettingsDirty(); }

        internal static void SetHudOffsetY(float value)
        { _cfg.uiOffsetY = HudPanelLayout.ClampOffset(value); _logUiProjNow = true; MarkSettingsDirty(); }
        internal static void SetHudElementScale(float value)
        { _cfg.uiElementScale = HudPanelLayout.ClampElementScale(value); _logUiProjNow = true; MarkSettingsDirty(); }
        internal static float FlatPanelWidthRatio
        {
            get
            {
                float aspect = FlatPanelAspect > 0 ? FlatPanelAspect : (float)Screen.width / Mathf.Max(1, Screen.height);
                var panel = HudPanelLayout.Calculate(HudPresentationDistance, 1, ManagementPresentation ? ManagementPresentationWidth : _cfg.uiWidth,
                    Screen.width, Screen.width / aspect, float.MaxValue, 0, _hudViewPlanes);
                return panel.Width / panel.Distance;
            }
        }
        internal static float FlatPanelAspect => ManagementPresentation ? ManagementPresentationAspect : 0;
        internal static bool LoadingPanelPresentation => ObservedNativeLoading || _modeName == "Loading" ||
            _modeName == "MainMenu" || _startupVrGate.Blocking;
        internal static float FlatPanelOffsetX => LoadingPanelPresentation ? 0 : HudPresentationOffsetX;
        internal static float FlatPanelOffsetY => LoadingPanelPresentation ? 0 : HudPresentationOffsetY;

        internal static void UpdateHudFieldOfView(XrFrame frame)
        {
            if (frame.valid == 0) return;
            if (!ValidHudFov(frame.left.fov) || !ValidHudFov(frame.right.fov)) return;
            if (_hudViewPlanes == null) _hudViewPlanes = new HudFrustumPlane[8];
            var inverseHead = Quaternion.Inverse(frame.head.Rotation);
            SetHudEyePlanes(frame.left, frame.head.Position, inverseHead, 0);
            SetHudEyePlanes(frame.right, frame.head.Position, inverseHead, 4);
        }

        static bool ValidHudFov(XrFov fov) => fov.left > -1.56f && fov.left < 0 &&
            fov.right > 0 && fov.right < 1.56f && fov.down > -1.56f && fov.down < 0 &&
            fov.up > 0 && fov.up < 1.56f;

        static void SetHudEyePlanes(XrView eye, Vector3 head, Quaternion inverseHead, int index)
        {
            Vector3 origin = inverseHead * (eye.pose.Position - head);
            Quaternion orientation = inverseHead * eye.pose.Rotation;
            SetHudPlane(index, orientation * new Vector3(1, 0, -Mathf.Tan(eye.fov.left)), origin);
            SetHudPlane(index + 1, orientation * new Vector3(-1, 0, Mathf.Tan(eye.fov.right)), origin);
            SetHudPlane(index + 2, orientation * new Vector3(0, 1, -Mathf.Tan(eye.fov.down)), origin);
            SetHudPlane(index + 3, orientation * new Vector3(0, -1, Mathf.Tan(eye.fov.up)), origin);
        }

        static void SetHudPlane(int index, Vector3 normal, Vector3 origin)
        {
            _hudViewPlanes[index] = new HudFrustumPlane {
                X = normal.x, Y = normal.y, Z = normal.z, Offset = -Vector3.Dot(normal, origin) };
        }

        static void PreservePcCanvasHierarchy(List<Canvas> canvases)
        {
            // Detaching these nested PC views discarded their anchors, parent
            // transforms and visibility chain, then substituted guessed positions.
            // Keep the game's layout whenever its parent already joins the panel.
            for (int i = canvases.Count - 1; i >= 0; --i)
            {
                var canvas = canvases[i];
                if (!HudPanelLayout.IsNestedPcChrome(canvas.name) || _cfg.uiPos.ContainsKey(canvas.name)) continue;
                bool covered = false;
                for (var parent = canvas.transform.parent; parent != null && !covered; parent = parent.parent)
                    for (int j = 0; j < canvases.Count; ++j)
                        if (j != i && canvases[j].transform == parent) { covered = true; break; }
                if (!covered) continue;
                _log.Log("[ui/pc-layout] '" + canvas.name + "' keeps its native PC hierarchy, anchors and scale.");
                canvases.RemoveAt(i);
            }
        }

        static void CapturePcReferenceSize(List<Canvas> canvases)
        {
            _hudReferenceSize = new Vector2(HudPanelLayout.ReferenceWidth, HudPanelLayout.ReferenceHeight);
            // StaticCanvas is the PC screen layout, unlike PartyPCView's small
            // portrait rectangle or a map marker container.
            for (int pass = 0; pass < 2; ++pass)
                foreach (var canvas in canvases)
                {
                    if (pass == 0 ? canvas.name != "StaticCanvas" : !canvas.isRootCanvas) continue;
                    var rect = canvas.transform as RectTransform;
                    if (rect == null || !HudPanelLayout.ValidReference(rect.rect.width, rect.rect.height)) continue;
                    _hudReferenceSize = rect.rect.size;
                    _hudNativeReferenceSize = _hudReferenceSize;
                    _log.Log("[ui/pc-layout] Native screen " + _hudReferenceSize.x + "x" + _hudReferenceSize.y +
                        "; PC aspect and hierarchy retained independently of the tabletop zoom.");
                    return;
                }
            _hudNativeReferenceSize = _hudReferenceSize;
        }

        static bool IsPcViewportRoot(string name) => name == "StaticCanvas" || name == "DynamicCanvas" || name == "CommonCanvas";

        // Reflow the original anchors into a headset-shaped logical viewport.
        // Content size changes logical columns and rows together: fixed-size
        // icons/text grow inside the same physical panel, while anchored bars
        // remain on its edges. This is independent of enlarging the whole panel.
        // The desktop backbuffer and original geometry ledger remain untouched.
        static void ApplyPcViewportLayout()
        {
            if (_uiRoot == null) return;
            float logicalWidth = HudPanelLayout.LogicalWidth(_hudNativeReferenceSize.x, HudPresentationElementScale);
            Vector2 desired = new Vector2(logicalWidth, logicalWidth / EffectiveHudAspect);
            bool changed = (_hudReferenceSize - desired).sqrMagnitude > .25f;
            if (changed)
            {
                _hudReferenceSize = desired; _uiRoot.sizeDelta = desired;
                ++_hudViewportRevision;
                _logUiProjNow = true;
            }
            foreach (var saved in _savedCanvases)
            {
                if (saved.canvas == null || !saved.panelGeomValid) continue;
                if (saved.hudViewportRevision == _hudViewportRevision) continue;
                bool viewport = IsPcViewportRoot(saved.canvas.name), map = IsMapContainer(saved.canvas.name);
                if (!viewport && !map) continue;
                var rect = saved.canvas.transform as RectTransform;
                if (rect == null) continue;
                if (viewport) EnsureHudViewportMask(saved.canvas);
                Vector2 size = viewport ? desired : _hudNativeReferenceSize;
                // Compare actual rect, not sizeDelta: stretch anchors incorporate
                // the holder's changed size. Only an explicit geometry revision
                // or new canvas reaches this block, not a steady frame.
                if (Mathf.Abs(rect.rect.width - size.x) > .5f)
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
                if (Mathf.Abs(rect.rect.height - size.y) > .5f)
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
                saved.panelSize = rect.sizeDelta;
                saved.hudViewportRevision = _hudViewportRevision;
            }
        }

        static void ApplyHudPickProjection(HudPanelGeometry panel)
        {
            if (_pickCam == null) return;
            _pickCam.fieldOfView = panel.VerticalFov; _pickCam.aspect = panel.Aspect;
            float ratio = _pickCam.nearClipPlane / panel.Distance;
            _pickCam.projectionMatrix = Matrix4x4.Frustum(
                (panel.CentreX - panel.Width * .5f) * ratio, (panel.CentreX + panel.Width * .5f) * ratio,
                (panel.CentreY - panel.Height * .5f) * ratio, (panel.CentreY + panel.Height * .5f) * ratio,
                _pickCam.nearClipPlane, _pickCam.farClipPlane);
        }

        static void PositionCinematicFadeCanvas(SavedCanvas saved, Vector3 head, Quaternion rotation, float farClip)
        {
            var rect = saved.canvas.transform as RectTransform;
            if (rect == null) return;
            var cover = HudPanelLayout.CoverViews(HudPresentationDistance, HudLayoutWorldScale, farClip,
                _hudNativeReferenceSize.x, _hudViewPlanes);
            float aspect = Mathf.Round(cover.Aspect * 100) / 100;
            if (saved.hudFadeAspect <= 0 || Mathf.Abs(saved.hudFadeAspect - aspect) > .025f ||
                saved.hudViewportRevision != _hudViewportRevision)
            {
                saved.hudFadeAspect = aspect;
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, _hudNativeReferenceSize.x);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, _hudNativeReferenceSize.x / aspect);
                saved.panelSize = rect.sizeDelta; saved.hudViewportRevision = _hudViewportRevision;
            }
            cover = HudPanelLayout.CoverViews(HudPresentationDistance, HudLayoutWorldScale, farClip,
                _hudNativeReferenceSize.x, _hudViewPlanes, saved.hudFadeAspect);
            Vector3 centre = head + rotation * new Vector3(cover.CentreX, cover.CentreY, cover.Distance);
            PlaceHudTransform(rect, centre - rotation * (cover.UnitsScale *
                new Vector3(rect.rect.center.x, rect.rect.center.y, 0)), rotation, Vector3.one * cover.UnitsScale);
            // Do not touch Graphics, CanvasGroup alpha, input blocking, animation
            // or child visibility: the original game decides when the fade/bars show.
        }

        static Vector2 CapturePcPlacement(Canvas canvas, Camera uiCamera)
        {
            if (_cfg.uiPos.TryGetValue(canvas.name, out var custom)) return custom;
            if (IsMapContainer(canvas.name)) return new Vector2(.5f, .5f);
            var rect = canvas.transform as RectTransform;
            var camera = canvas.worldCamera != null ? canvas.worldCamera : uiCamera;
            if (rect != null && camera != null)
            {
                var viewport = camera.WorldToViewportPoint(rect.TransformPoint(rect.rect.center));
                if (viewport.z > 0 && viewport.x >= -.1f && viewport.x <= 1.1f && viewport.y >= -.1f && viewport.y <= 1.1f)
                    return new Vector2(viewport.x, viewport.y);
            }
            return PlacementFrac(canvas.name);
        }

        static float HudPickPlaneDistance => _hudPanelDistanceWorld > 0 ? _hudPanelDistanceWorld : HudPresentationDistance * HudLayoutWorldScale;

        static void MaintainPcNestedRaycasters()
        {
            // A PC-view rebind may reassign its own eventCamera even though its
            // root remains world-space. Keeping hierarchy must not lose the old
            // protection against a desktop UI camera replacing the VR pick ray.
            foreach (var canvas in _redirectedCanvases)
                if (canvas != null && _uiRoot != null && canvas.transform.IsChildOf(_uiRoot) && canvas.worldCamera != _pickCam)
                    canvas.worldCamera = _pickCam;
        }

        static void RedirectLatePcCanvas(Canvas canvas)
        {
            if (canvas == null || _uiRoot == null || _pickCam == null || !canvas.transform.IsChildOf(_uiRoot) ||
                canvas.worldCamera == _pickCam || IsConvertedCanvas(canvas)) return;
            // Original camera is the existing redirected family's UICamera.
            // Record new nested instances so teardown restores them as well.
            if (canvas.worldCamera != _savedUiRaycastCam) return;
            if (!_redirectedCanvases.Contains(canvas)) _redirectedCanvases.Add(canvas);
            canvas.worldCamera = _pickCam;
        }
    }
}
