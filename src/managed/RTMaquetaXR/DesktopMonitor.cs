using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _desktopMirrorHook, _desktopFallback;
        static int _stereoPreparedFrame = -1, _desktopSkippedFrame = -1, _desktopHandledFrame = -1;
        static readonly DesktopMirrorRecovery _desktopRecovery = new DesktopMirrorRecovery();
        static int _desktopWorldSkips, _desktopCopies;

        internal static void BeginStereoPreparation() { _stereoPreparedFrame = -1; }
        internal static void MarkStereoPrepared() { _stereoPreparedFrame = Time.frameCount; }
        static bool SkipDesktopWorld(Camera camera)
        {
            if (!_active || !_attached || _modeFlat || !_cfg.skipDesktopWorld || !_desktopMirrorHook || _desktopFallback ||
                _stereoPreparedFrame != Time.frameCount || camera == null || camera != _attachedCam || camera.targetTexture != null)
                return false;
            if (!DesktopBackbufferAvailable())
            { DesktopMirrorFailed("Main camera temporarily targets a texture", false); return false; }
            // Leave Camera.enabled, its tag, transforms and callbacks intact:
            // input and game logic still use the real game camera.
            _desktopSkippedFrame = Time.frameCount; ++_desktopWorldSkips;
            return true;
        }
        static void CopyEyeToDesktop(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!_active || !_attached || _modeFlat || !_cfg.skipDesktopWorld || !_desktopMirrorHook ||
                _desktopSkippedFrame != Time.frameCount || _desktopHandledFrame == Time.frameCount) return;
            bool screenSafe = _attachedCam != null && _attachedCam.targetTexture == null && DesktopBackbufferAvailable();
            bool completePair = _runner != null && BothEyesRendered(_runner.GetEyeL(), _runner.GetEyeR(), Time.frameCount);
            _desktopHandledFrame = Time.frameCount;
            if (!screenSafe) { DesktopMirrorFailed("Monitor target changed during rendering", false); return; }
            if (!completePair) { DesktopMirrorFailed("Incomplete current eye pair", false); return; }
            try
            {
                long started = DiagnosticTimestamp();
                var eye = EyeL;
                // A centered crop keeps the eye image's aspect on a wide monitor.
                float fraction = Mathf.Clamp01((float)eye.width / eye.height / ((float)Screen.width / Screen.height));
                // Explicit null destination with Camera.main.targetTexture null
                // selects the screen, never the last eye's render target.
                var previousTarget = RenderTexture.active;
                bool previousSrgb = GL.sRGBWrite;
                try
                {
                    GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                    Graphics.Blit(eye, (RenderTexture)null, new Vector2(1, fraction), new Vector2(0, (1 - fraction) * 0.5f));
                }
                finally { RenderTexture.active = previousTarget; GL.sRGBWrite = previousSrgb; }
                ++_desktopCopies; _desktopRecovery.Copied(); RecordModStage("MonitorCopy", started);
            }
            catch (Exception e)
            {
                DesktopMirrorFailed("Monitor copy failed: " + e.Message, true);
            }
        }
        static bool DesktopBackbufferAvailable()
        {
            // Graphics.Blit(null destination) only redirects to a texture when
            // Camera.main has one. Its identity may legitimately change in a
            // menu/cutscene; equality with the attached game camera is not a
            // backbuffer requirement. Never alter either camera's target.
            var main = Camera.main;
            return main == null || main.targetTexture == null;
        }
        internal static void CompleteDesktopMirrorFrame(bool stereo)
        {
            // Called at WaitForEndOfFrame, once the existing submission path
            // has verified both eyes. Nested render contexts cannot consume a
            // frame's recovery observation before its eyes have rendered.
            if (!_desktopFallback) return;
            bool eligible = stereo && _active && _attached && !_modeFlat && _cfg.skipDesktopWorld &&
                _desktopMirrorHook && _stereoPreparedFrame == Time.frameCount;
            bool screenSafe = eligible && _attachedCam != null && _attachedCam.targetTexture == null && DesktopBackbufferAvailable();
            if (_desktopRecovery.Observe(Time.frameCount, eligible, screenSafe, stereo)) _desktopFallback = false;
        }
        static void DesktopMirrorFailed(string reason, bool copyError)
        {
            _desktopRecovery.Fail(Time.frameCount, reason, copyError);
            _desktopFallback = true;
            _log?.Log("[performance/monitor] Temporary native monitor render: " + reason + "; automatic recovery after complete stereo frames.");
        }
    }
}
