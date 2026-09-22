using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static GameObject _touchHandLeftRoot, _touchHandRightRoot;
        static MeshRenderer[] _touchHandLeftRenderers, _touchHandRightRenderers;
        static GameServoSkullPart[] _touchHandParts;
        static Camera _touchHandEyeLeft, _touchHandEyeRight;
        static bool _touchHandLeftShown, _touchHandRightShown, _touchHandEvents;
        static float _touchHandNextAttempt;
        static int _touchHandFailures, _touchHandDepthPass, _touchHandColorPass;
        static float _touchHandFlatRetry;
        static int _touchHandFlatFailures;
        static CommandBuffer _touchHandFlatCommands;
        static RenderTexture _touchHandFlatLeft, _touchHandFlatRight;
        static RenderTexture _touchHandFlatLeftWork, _touchHandFlatRightWork;
        static readonly TouchServoPoseState _touchServoLeftPose = new TouchServoPoseState();
        static readonly TouchServoPoseState _touchServoRightPose = new TouchServoPoseState();
        static readonly int TouchHandFowId = Shader.PropertyToID("_FogOfWarGlobalFlag");
        static readonly int TouchHandVolumeId = Shader.PropertyToID("_VolumetricLightingEnabled");
        static readonly int TouchHandFogParamsId = Shader.PropertyToID("unity_FogParams");
        static readonly int TouchHandFogColorId = Shader.PropertyToID("unity_FogColor");
        static readonly int TouchHandMipBiasId = Shader.PropertyToID("_GlobalMipBias");
        static readonly int TouchHandCameraId = Shader.PropertyToID("_WorldSpaceCameraPos");

        internal static void UpdateTouchHands(Camera left, Camera right)
        {
            UpdateTouchRadialVisuals(left, right);
            UpdateTouchProximityInteractionVisual73(left,right);
            _touchHandEyeLeft = left; _touchHandEyeRight = right;
            _touchHandLeftShown = _touchHandRightShown = false;
            var touch = CurrentTouchFrame;
            bool available = TouchHandsVisible && TouchInputOwned && !TouchCameraFaulted && !_modeFlat &&
                left != null && right != null && Application.isFocused && touch.ready != 0 && touch.focused != 0 &&
                touch.serial != 0 && touch.serial == _touchTabletopSerial;
            if (available && EnsureTouchHandVisuals())
            {
                try
                {
                    _touchHandLeftShown = PlaceTouchHand(_touchHandLeftRoot, touch.left, _touchServoLeftPose);
                    _touchHandRightShown = PlaceTouchHand(_touchHandRightRoot, touch.right, _touchServoRightPose);
                }
                catch (Exception error)
                {
                    int failures = _touchHandFailures + 1;
                    DestroyTouchHands(); _touchHandFailures = failures; _touchHandNextAttempt = Time.unscaledTime + 3f;
                    _log.Error("[touch/hands] Tracking visual failed; input preserved: " + error.Message);
                }
            }
            SetTouchHandRenderers(false);
        }

        static bool TryTouchServoPose(XrTouchHand hand, TouchServoPoseState calibration, out ViewPose screen)
        {
            return calibration.TryPose(hand.GripTracked,
                new ViewPose(TablePoint(hand.grip.Position), TableRotation(hand.grip.Rotation)), hand.AimValid,
                new ViewPose(TablePoint(hand.aim.Position), TableRotation(hand.aim.Rotation)), out screen);
        }

        static bool PlaceTouchHand(GameObject root, XrTouchHand hand, TouchServoPoseState calibration)
        {
            if (!_touchPoseReady || !_touchHaveTrackingReference || !_touchTabletop.Initialized ||
                !TryTouchServoPose(hand, calibration, out ViewPose screen)) return false;
            var inverse = Quaternion.Inverse(_touchTrackingReferenceRotation);
            var localPosition = TablePoint(inverse * (TableVector(screen.position) - _touchTrackingReferencePosition));
            if (!TouchTabletopState.Finite(localPosition)) return false;
            Vector3 position = TableVector(_touchTabletop.MapPoint(localPosition));
            Quaternion rotation = TableQuaternion(_touchTabletop.Pose.rotation) * inverse * TableQuaternion(screen.rotation);
            root.transform.SetPositionAndRotation(position, rotation);
            root.transform.localScale = Vector3.one * WorldScale;
            return true;
        }

        static void SetTouchHandRenderers(bool eye)
        {
            if (_touchHandLeftRenderers != null)
                for (int i = 0; i < _touchHandLeftRenderers.Length; ++i) if (_touchHandLeftRenderers[i] != null) _touchHandLeftRenderers[i].enabled = eye && _touchHandLeftShown;
            if (_touchHandRightRenderers != null)
                for (int i = 0; i < _touchHandRightRenderers.Length; ++i) if (_touchHandRightRenderers[i] != null) _touchHandRightRenderers[i].enabled = eye && _touchHandRightShown;
        }

        static void TouchHandsBeginCamera(ScriptableRenderContext context, Camera camera)
        { SetTouchHandRenderers(camera == _touchHandEyeLeft || camera == _touchHandEyeRight); }

        static bool EnsureTouchHandVisuals()
        {
            if (_touchHandParts != null) return true;
            if (Time.unscaledTime < _touchHandNextAttempt || _touchHandFailures >= 4) return false;
            try
            {
                if (!TryCreateGameServoSkull(out _touchHandParts))
                { _touchHandNextAttempt = Time.unscaledTime + 3f; return false; }
                _touchHandDepthPass = _touchHandParts[0].Material.FindPass("GBUFFER");
                _touchHandColorPass = _touchHandParts[0].Material.FindPass("Unlit");
                if (_touchHandDepthPass < 0 || _touchHandColorPass < 0)
                    throw new InvalidOperationException("Servo-skull material lacks verified depth/color passes");
                _touchHandLeftRoot = MakeTouchHand("RTMaquetaXR left servo-skull", out _touchHandLeftRenderers);
                _touchHandRightRoot = MakeTouchHand("RTMaquetaXR right servo-skull", out _touchHandRightRenderers);
                if (!_touchHandEvents) { RenderPipelineManager.beginCameraRendering += TouchHandsBeginCamera; _touchHandEvents = true; }
                _log.Log("[touch/hands] Original game servo-skull mesh ready; shared geometry; no simulation, colliders, lights or shadows");
                return true;
            }
            catch (Exception error)
            {
                int failures = _touchHandFailures + 1;
                DestroyTouchHands(); _touchHandFailures = failures; _touchHandNextAttempt = Time.unscaledTime + 3f;
                _log.Error("[touch/hands] " + error.Message); return false;
            }
        }

        static GameObject MakeTouchHand(string name, out MeshRenderer[] renderers)
        {
            var root = new GameObject(name);
            renderers = null;
            try
            {
                UnityEngine.Object.DontDestroyOnLoad(root); root.layer = 5;
                renderers = new MeshRenderer[_touchHandParts.Length];
                for (int i = 0; i < _touchHandParts.Length; ++i)
                {
                    var part = _touchHandParts[i];
                    var piece = new GameObject(part.Mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
                    piece.layer = 5; piece.transform.SetParent(root.transform, false);
                    piece.transform.localPosition = part.LocalToHand.GetColumn(3);
                    piece.transform.localRotation = part.LocalToHand.rotation;
                    piece.transform.localScale = part.LocalToHand.lossyScale;
                    piece.GetComponent<MeshFilter>().sharedMesh = part.Mesh;
                    var renderer = piece.GetComponent<MeshRenderer>(); renderer.sharedMaterial = part.Material;
                    renderer.enabled = false; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    renderers[i] = renderer;
                }
                return root;
            }
            catch { root.SetActive(false); UnityEngine.Object.Destroy(root); throw; }
        }

        // Isolated full-FOV stereo render for the native layer above the flat
        // game menu. No game cameras, scene culling, post effects or simulation.
        internal static bool RenderFlatTouchHands(XrFrame frame, out RenderTexture left, out RenderTexture right)
        {
            left = right = null;
            if (_touchHandFlatFailures >= 4 || Time.unscaledTime < _touchHandFlatRetry) return false;
            XrTouchFrame touch = CurrentTouchFrame;
            if ((!TouchHandsVisible && !_touchRadial.Visible) || !TouchInputOwned || TouchCameraFaulted || !Application.isFocused ||
                frame.valid == 0 || frame.shouldRender == 0 || frame.serial == 0 ||
                touch.ready == 0 || touch.focused == 0 || touch.serial != frame.serial ||
                (!touch.left.GripTracked && !touch.right.GripTracked)) return false;
            if (TouchHandsVisible && !EnsureTouchHandVisuals()) return false;
            try
            {
                EnsureFlatHandTargets(frame);
                PrepareFlatRadial(frame, true);
                bool showLeft = TryTouchServoPose(touch.left, _touchServoLeftPose, out ViewPose leftScreen);
                bool showRight = TryTouchServoPose(touch.right, _touchServoRightPose, out ViewPose rightScreen);
                showLeft &= TouchHandsVisible; showRight &= TouchHandsVisible;
                Matrix4x4 l = showLeft ? Matrix4x4.TRS(TableVector(leftScreen.position), TableQuaternion(leftScreen.rotation), Vector3.one) : Matrix4x4.identity;
                Matrix4x4 r = showRight ? Matrix4x4.TRS(TableVector(rightScreen.position), TableQuaternion(rightScreen.rotation), Vector3.one) : Matrix4x4.identity;
                DrawFlatHandEye(_touchHandFlatLeftWork, _touchHandFlatLeft, frame.left, l, r, showLeft, showRight);
                DrawFlatHandEye(_touchHandFlatRightWork, _touchHandFlatRight, frame.right, l, r, showLeft, showRight);
                left = _touchHandFlatLeft; right = _touchHandFlatRight; return true;
            }
            catch (Exception error)
            {
                // A cosmetic failure must leave the original flat quad playable.
                ++_touchHandFlatFailures; _touchHandFlatRetry = Time.unscaledTime + 3f;
                _log.Error("[touch/hands/flat] " + error.Message);
                ReleaseFlatHandTargets(); return false;
            }
        }

        static void EnsureFlatHandTargets(XrFrame frame)
        {
            FlatHandQualityPolicy.Size(frame.width, frame.height, out int width, out int height);
            if (_touchHandFlatLeft != null && _touchHandFlatRight != null && _touchHandFlatLeftWork != null && _touchHandFlatRightWork != null &&
                _touchHandFlatLeft.width == width && _touchHandFlatLeft.height == height) return;
            ReleaseFlatHandTargets();
            var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24) { msaaSamples = 4, sRGB = true };
            int samples = FlatHandQualityPolicy.Samples(SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor));
            FlatHandQualityPolicy.WorkSize(width, height, samples, out int workWidth, out int workHeight);
            _touchHandFlatCommands = new CommandBuffer { name = "RTMaquetaXR isolated Touch hands + antialias resolve" };
            _touchHandFlatLeft = NewFlatHandTarget("RTMaquetaXR flat left hand resolved", width, height, 1, 0);
            _touchHandFlatRight = NewFlatHandTarget("RTMaquetaXR flat right hand resolved", width, height, 1, 0);
            _touchHandFlatLeftWork = NewFlatHandTarget("RTMaquetaXR flat left hand AA", workWidth, workHeight, samples, 24);
            _touchHandFlatRightWork = NewFlatHandTarget("RTMaquetaXR flat right hand AA", workWidth, workHeight, samples, 24);
            _log.Log("[touch/hands/flat] " + width + "x" + height + " per eye; " +
                (samples > 1 ? samples + "x MSAA + explicit RGBA resolve" : "supersampling fallback " + workWidth + "x" + workHeight) +
                "; headset aspect; single-sample premultiplied-alpha bridge targets");
        }
        static RenderTexture NewFlatHandTarget(string name, int width, int height, int samples, int depth)
        {
            var target = new RenderTexture(width, height, depth, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { name = name, antiAliasing = samples, bindTextureMS = samples > 1, useMipMap = false, autoGenerateMips = false,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            if (!target.Create()) { UnityEngine.Object.Destroy(target); throw new InvalidOperationException("Hand layer target allocation"); }
            return target;
        }
        static void DrawFlatHandEye(RenderTexture work, RenderTexture target, XrView eye, Matrix4x4 l, Matrix4x4 r, bool showLeft, bool showRight)
        {
            const float near = .02f, far = 20f;
            Matrix4x4 projection = Matrix4x4.Frustum(Mathf.Tan(eye.fov.left) * near, Mathf.Tan(eye.fov.right) * near,
                Mathf.Tan(eye.fov.down) * near, Mathf.Tan(eye.fov.up) * near, near, far);
            Matrix4x4 view = Matrix4x4.Scale(new Vector3(1, 1, -1)) *
                Matrix4x4.TRS(eye.pose.Position, eye.pose.Rotation, Vector3.one).inverse;
            // The verified Unlit shader has optional world fog/volume branches
            // in $Globals. Raw tracking-space hands must not sample those maps.
            // Restore every captured value in this same command buffer, before
            // another renderer or the next eye can observe modified game state.
            float savedFow = Shader.GetGlobalFloat(TouchHandFowId);
            float savedVolume = Shader.GetGlobalFloat(TouchHandVolumeId);
            float savedMip = Shader.GetGlobalFloat(TouchHandMipBiasId);
            Vector4 savedFog = Shader.GetGlobalVector(TouchHandFogParamsId);
            Vector4 savedFogColor = Shader.GetGlobalVector(TouchHandFogColorId);
            Vector4 savedCamera = Shader.GetGlobalVector(TouchHandCameraId);
            var command = _touchHandFlatCommands; command.Clear();
            command.SetRenderTarget(work);
            command.SetViewport(new Rect(0, 0, work.width, work.height));
            command.ClearRenderTarget(true, true, Color.clear);
            // SetViewProjectionMatrices performs Unity's graphics conversion
            // itself, exactly as Waaagh CameraSetupPass does with ProjectionMatrix.
            // Passing an already converted GPU projection flips Y twice: after
            // the native eye blit a controller below the head appears above it.
            command.SetViewProjectionMatrices(view, projection);
            command.SetGlobalFloat(TouchHandFowId, 0); command.SetGlobalFloat(TouchHandVolumeId, 0);
            command.SetGlobalFloat(TouchHandMipBiasId, 0);
            command.SetGlobalVector(TouchHandFogParamsId, new Vector4(0, 0, 0, 1));
            command.SetGlobalVector(TouchHandFogColorId, Vector4.zero);
            command.SetGlobalVector(TouchHandCameraId, eye.pose.Position);
            // Owlcat/Unlit writes depth in GBUFFER, then reads it in Unlit.
            // Both hands share the same depth before color, preventing bleedthrough.
            if (showLeft) DrawFlatHandParts(command, l, _touchHandDepthPass);
            if (showRight) DrawFlatHandParts(command, r, _touchHandDepthPass);
            command.ClearRenderTarget(false, true, Color.clear);
            if (showLeft) DrawFlatHandParts(command, l, _touchHandColorPass);
            if (showRight) DrawFlatHandParts(command, r, _touchHandColorPass);
            DrawFlatRadial(command);
            command.SetGlobalFloat(TouchHandFowId, savedFow); command.SetGlobalFloat(TouchHandVolumeId, savedVolume);
            command.SetGlobalFloat(TouchHandMipBiasId, savedMip);
            command.SetGlobalVector(TouchHandFogParamsId, savedFog); command.SetGlobalVector(TouchHandFogColorId, savedFogColor);
            command.SetGlobalVector(TouchHandCameraId, savedCamera);
            // Opaque samples resolve against transparent black to premultiplied
            // RGBA coverage. The native OpenXR hand layer already expects this.
            // Never hand a multisampled resource to its Texture2D source view.
            if (work.antiAliasing > 1) command.ResolveAntiAliasedSurface(work, target);
            // Its shipped DXBC explicitly converts linear albedo to sRGB before
            // writing o0. Avoid a second conversion into this sRGB-tagged target;
            // keep the tag for the native SRGB source view/compositor contract.
            bool savedSrgbWrite = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false; Graphics.ExecuteCommandBuffer(command);
                if (work.antiAliasing == 1)
                {
                    // Hardware without MSAA gets a larger isolated hand render
                    // downsampled once, preserving the transparent coverage.
                    GL.sRGBWrite = true; Graphics.Blit(work, target);
                }
            }
            finally { GL.sRGBWrite = savedSrgbWrite; }
        }

        static void DrawFlatHandParts(CommandBuffer command, Matrix4x4 hand, int pass)
        {
            for (int i = 0; i < _touchHandParts.Length; ++i)
            {
                var part = _touchHandParts[i];
                command.DrawMesh(part.Mesh, hand * part.LocalToHand, part.Material, 0, pass);
            }
        }

        static void ReleaseFlatHandTargets()
        {
            if (_touchHandFlatLeft != null) { OpenXR.ForgetTexture(_touchHandFlatLeft); _touchHandFlatLeft.Release(); UnityEngine.Object.Destroy(_touchHandFlatLeft); }
            if (_touchHandFlatRight != null) { OpenXR.ForgetTexture(_touchHandFlatRight); _touchHandFlatRight.Release(); UnityEngine.Object.Destroy(_touchHandFlatRight); }
            _touchHandFlatLeft = _touchHandFlatRight = null;
            if (_touchHandFlatLeftWork != null) { _touchHandFlatLeftWork.Release(); UnityEngine.Object.Destroy(_touchHandFlatLeftWork); }
            if (_touchHandFlatRightWork != null) { _touchHandFlatRightWork.Release(); UnityEngine.Object.Destroy(_touchHandFlatRightWork); }
            _touchHandFlatLeftWork = _touchHandFlatRightWork = null;
            if (_touchHandFlatCommands != null) _touchHandFlatCommands.Release();
            _touchHandFlatCommands = null;
        }
        internal static void DestroyTouchHands()
        {
            if (_touchHandEvents) RenderPipelineManager.beginCameraRendering -= TouchHandsBeginCamera;
            _touchHandEvents = false; _touchHandLeftShown = _touchHandRightShown = false;
            SetTouchHandRenderers(false);
            if (_touchHandLeftRoot != null) UnityEngine.Object.Destroy(_touchHandLeftRoot);
            if (_touchHandRightRoot != null) UnityEngine.Object.Destroy(_touchHandRightRoot);
            _touchHandLeftRoot = _touchHandRightRoot = null; _touchHandLeftRenderers = _touchHandRightRenderers = null;
            _touchHandParts = null;
            _touchHandEyeLeft = _touchHandEyeRight = null;
            _touchServoLeftPose.Reset(); _touchServoRightPose.Reset();
            ReleaseFlatHandTargets();
            ReleaseServoSkullPortraits();
            ResetGameServoSkullLoader();
            _touchHandFailures = _touchHandFlatFailures = 0; _touchHandNextAttempt = _touchHandFlatRetry = 0;
        }
    }
}
