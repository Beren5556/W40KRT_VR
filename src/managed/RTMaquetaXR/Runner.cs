using System;
using System.Collections;
using UnityEngine;

namespace RTMaquetaXR
{
    [DefaultExecutionOrder(31000)]
    public sealed class Runner : MonoBehaviour
    {
        Camera left, right;
        readonly EyeBackground background=new EyeBackground();
        readonly StartupReferencePolicy64 startupReference = new StartupReferencePolicy64();
        XrFrame frame;
        bool frameBegun, haveReference, recenterPanel;
        bool flatHandsFaulted;
        ulong referenceOriginRevision;
        Vector3 referencePosition;
        Quaternion referenceRotation;
        XrCrop leftCrop = XrCrop.Full, rightCrop = XrCrop.Full;
        int configuredFrame = -1;
        float nextReport;

        void Start() { StartCoroutine(SubmitLoop()); }
        void OnGUI() { Main.DrawFlatCursor(); Main.DrawFlatLiveOverlay(); Main.DrawFlatTouchContextHints(); }
        internal Camera GetEyeL() => left;
        internal Camera GetEyeR() => right;
        internal void RefreshEyeTargets()
        {
            if (left != null) left.targetTexture = Main.EyeL;
            if (right != null) right.targetTexture = Main.EyeR;
        }
        internal void SetRightEyeEnabled(bool enabled) { if (right != null) right.enabled = enabled && frameBegun && frame.valid != 0 && !Main.TouchCameraFaulted; }
        internal void BuildEyes()
        {
            if (left == null) left = NewEye("RTMaquetaXR_Left", Main.EyeL);
            if (right == null) right = NewEye("RTMaquetaXR_Right", Main.EyeR);
            Main.ResetEyeQualityRefresh();
        }
        internal void DestroyEyes()
        {
            Main.RestoreCombatVisuals();
            if (left != null) { left.enabled = false; left.targetTexture = null; Destroy(left.gameObject); }
            if (right != null) { right.enabled = false; right.targetTexture = null; Destroy(right.gameObject); }
            left = right = null; configuredFrame = -1;
        }
        Camera NewEye(string name, RenderTexture target)
        {
            var obj = new GameObject(name); obj.transform.SetParent(transform, false);
            var camera = obj.AddComponent<Camera>(); camera.enabled = false;
            camera.targetTexture = target; return camera;
        }
        void Update()
        {
            if (!Main.SessionActive) return;
            try
            {
                Main.RefreshSpatialGameContext();
                Main.AdvanceReadyLoading();
                Main.UpdateSpaceQualityProfile();
                Main.UpdateSpaceBackdrop();
                Main.PruneFamiliarVisuals76();
                bool areaChanged = Main.ConsumeTouchCameraAreaReset();
                if (areaChanged) { Main.ResetTouchTabletopForScene(); Main.ResetCinematicSession(); }
                // Inventory/management uses additive Unity UI scenes too. It
                // may require render detachment, but must preserve camera and
                // first-person state. Only typed native area/load markers reset.
                if (Main.ConsumeSuspendRequest() || areaChanged) Main.SuspendForTransition(areaChanged ? "game area load" : "render / UI scene transition");
                Main.BeginStereoPreparation();
                if (!Main.ApplyPendingOutputResolution())
                {
                    DisableUnpreparedEyes();
                    frameBegun = false;
                    return;
                }
                Main.MaintainVrPacing();
                long waitStarted = Main.DiagnosticTimestamp();
                int result = OpenXR.RTX_Begin(out frame);
                Main.UpdateTouchInput(frame);
                Main.RecordFrameWait(waitStarted);
                if (Main.DiagnosticsRecording || Main.EngineCadenceTimingNeeded)
                {
                    OpenXR.RTX_GetBeginTiming(out var beginTiming);
                    if (Main.EngineCadenceTimingNeeded) Main.ObserveEngineCadenceFrame(frame, beginTiming, result);
                    if (Main.DiagnosticsRecording)
                    {
                        Main.RecordBeginTiming(beginTiming);
                        if (OpenXR.RTX_GetRenderTiming(out var renderTiming) == 1) Main.RecordRenderTiming(renderTiming);
                    }
                }
                frameBegun = result == 1;
                if (result < 0)
                {
                    Main.Log.Log("[OpenXR] Begin=" + result + ": " + OpenXR.Error());
                    if (result == -3) Main.RequestRestart();
                }
                bool render = frameBegun && frame.valid != 0 && frame.shouldRender != 0 && !Main.TouchCameraFaulted;
                if (left != null) left.enabled = render && Main.Attached;
                if (right != null) right.enabled = render && Main.Attached;
                configuredFrame = -1;
            }
            catch (Exception exception) { Main.Log.Error("[OpenXR] Update: " + exception); Main.RequestRestart(); }
        }
        void LateUpdate()
        {
            if (!Main.SessionActive || !frameBegun) { DisableUnpreparedEyes(); return; }
            try
            {
                Main.UpdateFlatMode();
                if (Main.TouchCameraFaulted) { DisableUnpreparedEyes(); return; }
                if (frame.valid == 0 || frame.shouldRender == 0) { DisableUnpreparedEyes(); return; }
                Main.UpdateHudFieldOfView(frame);
                Main.UpdateNativeHintPlacement();
                ulong originRevision = OpenXR.RTX_GetTrackingOriginRevision();
                if (originRevision == 0) { DisableUnpreparedEyes(); return; }
                if (!haveReference && !startupReference.Observe(originRevision, frame.serial, Time.realtimeSinceStartup,
                    UsableInitialPose(frame))) { DisableUnpreparedEyes(); return; }
                bool requestedRecenter = Main.TakeRecenter();
                if (requestedRecenter || !haveReference || originRevision != referenceOriginRevision)
                {
                    Main.ResetTouchSpatialReference();
                    referencePosition = frame.head.Position;
                    referenceRotation = frame.head.Rotation;
                    // First focus and runtime reference changes are automatic
                    // upright placement. Only an explicit later recenter tilts
                    // the flat panel to the user's intentional gaze direction.
                    recenterPanel = requestedRecenter && haveReference;
                    haveReference = true; referenceOriginRevision = originRevision;
                    Main.RecenterLoadingStereo();
                    Main.RequestNeuralHistoryReset();
                    Main.Log.Log("[6DoF] Posición y orientación recentradas.");
                }
                if (Main.FlatWanted)
                {
                    DisableUnpreparedEyes();
                    if (Main.InNavigationMap) Main.UpdatePcHudPresentation();
                    Main.PrepareFlatRadial(frame);
                    return;
                }
                if (!Main.Attached) { Main.PollResume(); if (!Main.Attached) { DisableUnpreparedEyes(); return; } }
                var source = Camera.main;
                if (source == null || source != Main.AttachedCam)
                {
                    DisableUnpreparedEyes(); Main.SuspendForTransition("main camera changed"); return;
                }
                Main.ApplyPendingEyeAa();
                Main.ApplyPendingDrawDistance();
                Main.ApplyPendingNeural();
                Main.ApplyRuntimeOptimizationOptions();
                long configurationStarted = Main.DiagnosticTimestamp();
                Main.UpdateTouchTabletop(frame, referencePosition, referenceRotation, source, out Vector3 gameAnchor, out Quaternion gameRotation);
                if (!Main.TouchTabletopPoseReady) { DisableUnpreparedEyes(); return; }
                var inverse = Quaternion.Inverse(referenceRotation);
                Vector3 headOffset = inverse * (frame.head.Position - referencePosition) * Main.WorldScale;
                Quaternion headDelta = inverse * frame.head.Rotation;
                Quaternion centerRotation = gameRotation * headDelta;
                Vector3 centerPosition = gameAnchor + gameRotation * headOffset;
                Main.SetTouchSpatialFrame(frame, centerPosition, centerRotation);
                Main.PrepareAdaptiveDistance(frame, referencePosition, referenceRotation, gameAnchor, gameRotation, source);
                Configure(left, Main.EyeL, source, frame.left, centerPosition, centerRotation, gameAnchor, gameRotation, out leftCrop);
                Configure(right, Main.EyeR, source, frame.right, centerPosition, centerRotation, gameAnchor, gameRotation, out rightCrop);
                Main.MaybeApplyEyeCameraData(left, right, source);
                Main.ApplyEyeQuality(left); Main.ApplyEyeQuality(right);
                Main.PinEyeRenderType(left); Main.PinEyeRenderType(right);
                Main.PrepareForcedVisibility(source, left, right);
                Main.RecordModStage("EyeSetup", configurationStarted);
                long uiStarted = Main.DiagnosticTimestamp();
                Main.PositionWorldSpaceUi(source, centerPosition, centerRotation);
                Main.RecordModStage("UiPlacementAndPicking", uiStarted);
                long overtipStarted = Main.DiagnosticTimestamp();
                Main.PrepareWorldPresentation75(left,right,centerPosition);
                Main.RecordModStage("WorldOvertips", overtipStarted);
                long transitionStarted = Main.DiagnosticTimestamp();
                Main.SuppressTransitionPantograph();
                Main.RecordModStage("TransitionScan", transitionStarted);
                long presentationStarted79 = Main.DiagnosticTimestamp();
                Main.UpdateTouchGuide();
                Main.PositionLiveOverlay(left, right);
                Main.UpdateTouchContextHints(left, right);
                Main.UpdateTouchPointer(left, right);
                Main.UpdateTouchSelectionVisuals(left, right);
                Main.UpdateTouchHands(left, right);
                Main.RecordModStage("TouchPresentation79", presentationStarted79);
                presentationStarted79 = Main.DiagnosticTimestamp();
                Main.PrepareSpatialUi(frame, left, right, centerPosition, centerRotation);
                Main.RecordModStage("SpatialAtlas79", presentationStarted79);
                presentationStarted79 = Main.DiagnosticTimestamp();
                Main.UpdateCombatVisuals(left, right);
                Main.RecordModStage("CombatVisuals79", presentationStarted79);
                presentationStarted79 = Main.DiagnosticTimestamp();
                Main.PrepareHudCapture(left, right, centerPosition, centerRotation);
                Main.PrepareWorldHudResolution(left, right);
                Main.RecordModStage("HudCapture79", presentationStarted79);
                configuredFrame = Time.frameCount;
                Main.MarkStereoPrepared();
                Main.RecordModStage("CameraAndUi", configurationStarted);
            }
            catch (Exception exception)
            {
                Main.Log.Error("[OpenXR] Camera configuration: " + exception);
                DisableUnpreparedEyes();
                Main.SuspendForTransition("camera exception");
            }
        }
        void DisableUnpreparedEyes()
        {
            Main.HideTouchContextHints();
            Main.HideWorldInformation70();
            Main.UpdateTouchGuide(false);
            Main.RestoreCombatVisuals();
            if (left != null) left.enabled = false;
            if (right != null) right.enabled = false;
            configuredFrame = -1;
            Main.PauseHudCapture();
            Main.PauseSpatialUi();
            Main.UpdateTouchPointer(null, null);
            Main.UpdateTouchSelectionVisuals(null, null);
            Main.UpdateTouchHands(null, null);
        }
        void Configure(Camera eye, RenderTexture target, Camera source, XrView view,
                       Vector3 headPosition, Quaternion headRotation, Vector3 gameAnchor, Quaternion gameRotation, out XrCrop crop)
        {
            eye.CopyFrom(source); eye.gameObject.tag = "Untagged";
            background.Apply(eye,source,eye==left,Main.CustomSpaceBackgroundActive);
            Main.ApplyNativeSpaceBackgroundLayer(eye);
            eye.targetTexture = target; eye.enabled = true;
            // These are ordinary SRP cameras. stereoTargetEye is unsupported
            // here and used to emit two warnings on every rendered frame.
            eye.allowMSAA = false; eye.allowDynamicResolution = false;
            eye.depth = source.depth + (eye == left ? 0.01f : 0.02f);
            var gamePose = ToPose(gameAnchor, gameRotation);
            var reference = ToPose(referencePosition, referenceRotation);
            var trackedHead = ToPose(frame.head.Position, frame.head.Rotation);
            var trackedEye = ToPose(view.pose.Position, view.pose.Rotation);
            var placed = Geometry.Eye(gamePose, reference, trackedHead, trackedEye, Main.WorldScale, 1f);
            eye.transform.SetPositionAndRotation(new Vector3(placed.position.x,placed.position.y,placed.position.z),
                new Quaternion(placed.rotation.x,placed.rotation.y,placed.rotation.z,placed.rotation.w));
            float near = DrawDistanceOptions.SanitizeNear(Mathf.Min(source.nearClipPlane, Mathf.Max(0.01f, Main.WorldScale * 0.02f)));
            eye.nearClipPlane = near; eye.orthographic = false;
            float far = Main.EyeFarClip(near, source.farClipPlane);
            eye.farClipPlane = far;
            var projection = Projection(view.fov, near, far, out crop);
            eye.fieldOfView = 2f * Mathf.Atan(1f / projection.m11) * Mathf.Rad2Deg;
            eye.aspect = projection.m11 / projection.m00;
            eye.nonJitteredProjectionMatrix = projection;
            eye.projectionMatrix = projection;
            Main.PrepareVisibleCulling(eye, projection, crop, near, far, target.width, target.height);
        }
        static ViewPose ToPose(Vector3 position, Quaternion rotation) => new ViewPose(
            new Point3(position.x,position.y,position.z),new Rotation4(rotation.x,rotation.y,rotation.z,rotation.w));
        static bool UsableInitialPose(XrFrame value)
        {
            var p=value.head.Position;var q=value.head.Rotation;
            return Finite(p.x)&&Finite(p.y)&&Finite(p.z)&&Finite(q.x)&&Finite(q.y)&&Finite(q.z)&&Finite(q.w)&&
                q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w>.5f&&UsableInitialView(value.left)&&UsableInitialView(value.right);
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        static bool UsableInitialView(XrView view)=>Finite(view.fov.left)&&Finite(view.fov.right)&&Finite(view.fov.up)&&Finite(view.fov.down)&&
            view.fov.left<view.fov.right&&view.fov.down<view.fov.up;
        // Waaagh's tiled effects require a symmetric frustum. The native blitter
        // crops each symmetric image to its exact OpenXR asymmetric FOV.
        internal static Matrix4x4 Projection(XrFov fov, float near, float far, out XrCrop crop)
        {
            float l = Mathf.Tan(fov.left), r = Mathf.Tan(fov.right);
            float b = Mathf.Tan(fov.down), t = Mathf.Tan(fov.up);
            float x = Mathf.Max(Mathf.Abs(l), Mathf.Abs(r)), y = Mathf.Max(Mathf.Abs(b), Mathf.Abs(t));
            crop = new XrCrop { left = (l + x)/(2*x), right = (r+x)/(2*x), top = (y-t)/(2*y), bottom = (y-b)/(2*y) };
            return Matrix4x4.Frustum(-x*near, x*near, -y*near, y*near, near, far);
        }
        IEnumerator SubmitLoop()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                if (!Main.SessionActive || !frameBegun) continue;
                try
                {
                    bool stereo = !Main.TouchCameraFaulted && Main.Attached && !Main.FlatWanted && configuredFrame == Time.frameCount;
                    // RenderSingleCamera postfix markers prove BOTH cameras reached
                    // the end of Waaagh rendering in this same simulation frame.
                    stereo &= Main.BothEyesRendered(left, right, Time.frameCount);
                    Main.CompleteDesktopMirrorFrame(stereo);
                    Main.RecordFramePerformance(stereo);
                    bool loadingFallback = Main.NativeLoadingFallback;
                    RenderTexture flat = haveReference && !stereo && (Main.PresentationQuadWanted || loadingFallback) && frame.valid != 0 ? Main.CaptureFlatFrame() : null;
                    RenderTexture submitLeft = stereo ? Main.EyeL : null, submitRight = stereo ? Main.EyeR : null;
                    if (haveReference && Main.LoadingStereoWanted(stereo, Main.PresentationQuadWanted || loadingFallback, Main.ObservedNativeLoading,
                        frame.valid != 0, frame.shouldRender != 0, Time.unscaledTime))
                        Main.RenderLoadingStereo(frame, out submitLeft, out submitRight);
                    if (flat != null && frame.shouldRender != 0 && !flatHandsFaulted)
                    {
                        long handsStarted = Main.DiagnosticTimestamp();
                        try
                        {
                            if (!Main.RenderFlatTouchHands(frame, out submitLeft, out submitRight)) submitLeft = submitRight = null;
                        }
                        catch (Exception exception)
                        {
                            submitLeft = submitRight = null; flatHandsFaulted = true;
                            Main.Log.Error("[touch/hands] Flat visuals disabled; menu retained: " + exception.Message);
                        }
                        Main.RecordModStage("FlatTouchHands", handsStarted);
                    }
                    long submitStarted = Main.DiagnosticTimestamp();
                    OpenXR.Submit(frame, submitLeft, submitRight, flat,
                                  stereo ? leftCrop : XrCrop.Full, stereo ? rightCrop : XrCrop.Full, Main.PanelDistance, recenterPanel);
                    Main.RecordModStage("SubmitQueue", submitStarted);
                    recenterPanel = false;
                    if (Main.DiagnosticsRecording && Time.realtimeSinceStartup >= nextReport)
                    {
                        nextReport = Time.realtimeSinceStartup + 5;
                        OpenXR.RTX_GetStats(out var statistics);
                        OpenXR.Status = OpenXrRuntime.Name + ": pairs=" + statistics.submittedPairs + ", flat=" + statistics.flatFrames + ", errors=" + statistics.failedFrames;
                        Main.Log.Log("[OpenXR] " + OpenXR.Status + "; state=" + statistics.state + "; L=" + statistics.lastLeftSerial + "; R=" + statistics.lastRightSerial);
                        Main.WriteDiagnostics(statistics, frame, stereo);
                    }
                }
                catch (Exception exception) { Main.Log.Error("[OpenXR] Submit: " + exception); }
                frameBegun = false;
            }
        }
    }
}
