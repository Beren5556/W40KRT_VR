using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _cinematicWanted, _cinematicSavedTable, _cinematicVideoHooks;
        static ViewPose _cinematicTablePose;
        static TouchTabletopSnapshot _cinematicTableSnapshot;
        static readonly CinematicControlPolicy _cinematicControl = new CinematicControlPolicy();
        static readonly CinematicEntryPolicy77 _cinematicEntry77 = new CinematicEntryPolicy77();
        static Camera _cinematicTableSource;
        static FieldInfo _cinematicVideoPlay;
        static readonly List<Component> _cinematicVideos = new List<Component>();
        internal static bool CinematicWanted => _cinematicWanted;
        // Camera and input updates precede Runner.LateUpdate. Consult the actual
        // game mode here rather than letting a one-frame-old flat/stereo cache
        // suppress the first scripted camera movement or dispatch a world click.
        internal static bool CinematicCameraOwnsInput => _active && !InSpaceCombat && !InNavigationMap &&
            (_actionCameraActive || CinematicPolicy.ScriptedMode(PresentationSceneMode(ReadGameMode())));

        static void UpdateCinematicMode(string mode, bool flat)
        {
            bool wanted = !flat && !InSpaceCombat && !InNavigationMap && (_actionCameraActive || CinematicPolicy.ScriptedMode(mode));
            string previous = _cinematicEntry77.Previous;
            bool dialogueEntry = _cinematicEntry77.Observe(wanted, mode);
            if (wanted == _cinematicWanted)
            {
                if (dialogueEntry && !_cinematicControl.Manual) BeginDialogueFraming77(previous);
                return;
            }
            ResetCinematicCloseup();
            _cinematicControl.Reset();
            if (wanted)
            {
                ClearTouchCameraPendingFocus();
                _cinematicSavedTable = _touchTabletop.Initialized;
                _cinematicTablePose = _touchTabletop.Pose;
                _cinematicTableSnapshot = _touchTabletop.Capture();
                _cinematicTableSource = _touchTabletopSource;
                RestoreTouchCameraPreset();
            }
            else
            {
                if (_cinematicSavedTable && _touchTabletop.Initialized)
                {
                    _touchTabletop.Restore(_cinematicTableSnapshot);
                    if (!_touchFirstPerson && _cfg.worldScale != _touchTabletop.Scale) { _cfg.worldScale = _touchTabletop.Scale; MarkSettingsDirty(); }
                }
                _cinematicSavedTable = false; _cinematicTableSource = null;
                // End of a scene returns to the player's preceding table pose.
                // Actual area loads reset this snapshot and own their separate
                // post-load framing token; a menu exit is never a group focus.
            }
            _cinematicWanted = wanted;
            if (dialogueEntry) BeginDialogueFraming77(previous);
            _log.Log("[cinematic] " + (wanted ? "rendered scene: same-frame stereo + authored camera + 6DoF" : "return to preserved Touch tabletop"));
        }

        static void UpdateCinematicTabletop(XrFrame frame, Vector3 referencePosition, Quaternion referenceRotation,
            Camera source, out Vector3 gameAnchor, out Quaternion gameRotation)
        {
            gameAnchor = source != null ? source.transform.position : Vector3.zero;
            gameRotation = source != null ? source.transform.rotation : Quaternion.identity;
            if (source == null || source != _attachedCam || _touchCameraFault)
            { _touchPoseReady = false; return; }
            var touch = CurrentTouchFrame;
            bool allowed = TouchTabletopControlsAllowed(frame, touch);
            // A grip can already be held on the very first cinematic frame.
            // Resolve a valid displayed pose before giving manual control to it.
            var automaticPose = _cinematicControl.Manual ? _cinematicCloseupPose : CinematicCloseupPose(frame, source, referencePosition, referenceRotation);
            bool gesture = (touch.left.GripTracked && (touch.left.activeControls & (uint)XrTouchControl.Squeeze) != 0 && touch.left.squeeze >= TouchTabletopState.GripPress) ||
                (touch.right.GripTracked && (touch.right.activeControls & (uint)XrTouchControl.Squeeze) != 0 && touch.right.squeeze >= TouchTabletopState.GripPress) ||
                TouchStickCameraRequested(touch);
            if (_cinematicControl.Observe(allowed, gesture))
            {
                // Start from the displayed pose. Prime the new hand baseline on
                // this same gesture; no extra release/second squeeze is needed.
                _touchTabletop.Initialize(automaticPose, TouchWorldScale,
                    TablePoint(CinematicManualPivot(source)));
                _touchTabletop.Cancel(false);
                CinematicFocusStatus("manual Touch view; automatic framing suspended until this sequence ends");
            }
            var pose = _cinematicControl.Manual && _touchTabletop.Initialized ? _touchTabletop.Pose : automaticPose;
            gameAnchor = TableVector(pose.position); gameRotation = TableQuaternion(pose.rotation);
            // The same ordinary eye cameras, AA histories and HUD attachment
            // remain alive. Geometry.Eye adds both actual eye poses and physical
            // head movement to this authored camera in the same simulation frame.
            if (!_touchTabletop.Initialized || _touchTabletopSource != source)
            {
                _touchTabletop.Initialize(pose, TouchWorldScale, TablePoint(gameAnchor + gameRotation * Vector3.forward * 10f));
                _touchTabletopSource = source; _cinematicSavedTable = false;
            }
            else if (!_cinematicControl.Manual) _touchTabletop.OverrideViewPose(pose);
            if (_cinematicControl.Manual)
            {
                TickTouchTabletopControls(frame, referencePosition, referenceRotation);
                gameAnchor = TableVector(_touchTabletop.Pose.position); gameRotation = TableQuaternion(_touchTabletop.Pose.rotation);
            }
            else
            {
                _touchTrackingReferencePosition = referencePosition; _touchTrackingReferenceRotation = referenceRotation;
                _touchHaveTrackingReference = true;
            }
            _touchPoseReady = true; _touchTabletopSerial = frame.serial;
        }

        static void InstallCinematicHooks()
        {
            if (_cinematicVideoHooks) return;
            var type = AccessTools.TypeByName("Kingmaker.Utility.VideoPlayerHelper");
            var play = AccessTools.Method(type, "Play", Type.EmptyTypes);
            var reset = AccessTools.Method(type, "ResetValues", Type.EmptyTypes);
            var disable = AccessTools.Method(type, "OnDisable", Type.EmptyTypes);
            _cinematicVideoPlay = AccessTools.Field(type, "m_Play");
            if (type == null || play == null || reset == null || disable == null || _cinematicVideoPlay == null || _cinematicVideoPlay.FieldType != typeof(bool))
                throw new MissingMemberException("Game video player shape required for cinematic flat fallback");
            _harmony.Patch(play, postfix: new HarmonyMethod(typeof(Main), nameof(CinematicVideoStarted)));
            _harmony.Patch(reset, postfix: new HarmonyMethod(typeof(Main), nameof(CinematicVideoStopped)));
            _harmony.Patch(disable, postfix: new HarmonyMethod(typeof(Main), nameof(CinematicVideoStopped)));
            // VR can be enabled in the middle of a clip. Seed once, never scan
            // all Unity objects in a frame loop or perform video texture readback.
            foreach (var instance in UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                CinematicVideoStarted(instance);
            _cinematicVideoHooks = true;
        }
        static void CinematicVideoStarted(object __instance)
        {
            var component = __instance as Component;
            if (component != null && (bool)_cinematicVideoPlay.GetValue(__instance) && !_cinematicVideos.Contains(component))
                _cinematicVideos.Add(component);
        }
        static void CinematicVideoStopped(object __instance)
        {
            var component = __instance as Component;
            if (!ReferenceEquals(component, null)) _cinematicVideos.Remove(component);
        }
        static bool CinematicVideoShowing(string mode)
        {
            if (!CinematicPolicy.ScriptedMode(PresentationSceneMode(mode)) && !_actionCameraActive) return false;
            bool active = false;
            for (int i = _cinematicVideos.Count - 1; i >= 0; --i)
            {
                var component = _cinematicVideos[i];
                if (component == null) _cinematicVideos.RemoveAt(i);
                else if (component.gameObject.activeInHierarchy) active = true;
            }
            return active;
        }
        internal static void ResetCinematicSession()
        {
            _cinematicWanted = _cinematicSavedTable = _actionCameraActive = false;
            _cinematicControl.Reset();
            _cinematicEntry77.Reset();
            ResetCinematicCloseup();
            ResetCombatFocus();
            _tutorialShowing = false; _presentationSceneMode = null;
            _tutorialBlocksInput = false; _tutorialWindowIdentity = null;
            _presentationRetryAt = 0; _presentationContextWarning = false;
            _cinematicTableSource = null;
            // Hook ownership is process-long; actual video lifecycle callbacks
            // keep the tracked list current even while VR is temporarily off.
        }
    }
}
