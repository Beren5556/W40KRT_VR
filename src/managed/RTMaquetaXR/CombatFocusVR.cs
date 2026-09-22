using System;
using System.Collections;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly CombatFocusPolicy _combatFocus = new CombatFocusPolicy();
        static float _combatFocusNextProbe, _combatFocusNextTargetProbe;
        static void ObserveCombatFocus()
        {
            if (InSpaceCombat || InNavigationMap) { _combatFocus.Cancel(true); return; }
            if (_presentation == null || Time.unscaledTime < _combatFocusNextProbe) return;
            _combatFocusNextProbe = Time.unscaledTime + .2f;
            try
            {
                var game = _presentation.Game();
                var turn = game == null ? null : _presentation.Turn(game);
                if (turn != null) _combatFocus.Observe(_presentation.InCombat(turn));
            }
            catch { /* An unloading area may temporarily have no TurnData. */ }
        }
        static void UpdateCombatFocus(Camera source, XrFrame frame, Vector3 referencePosition, Quaternion referenceRotation, XrTouchFrame touch)
        {
            if (InSpaceCombat || InNavigationMap) { _combatFocus.Cancel(true); return; }
            if (!_combatFocus.Pending || !_touchTabletop.Initialized || source == null) return;
            if (TouchOverlayOpen || !TouchInputOwned || _modeName != "Default" || _tutorialShowing || CinematicWanted ||
                frame.valid == 0 || frame.shouldRender == 0 || touch.serial == 0 || touch.serial != frame.serial ||
                touch.ready == 0 || touch.focused == 0 || !Application.isFocused || _touchCameraLoad.Pending ||
                TouchMenuWindowVisible || TouchRadialCaptured || _touchUiPress.Captured) return;
            bool moved = touch.left.squeeze > .35f || touch.right.squeeze > .35f || TouchStickCameraRequested(touch) ||
                Mathf.Abs(touch.right.stickX) > .2f || Mathf.Abs(touch.right.stickY) > .2f;
            bool pressed = touch.left.trigger > .15f || touch.right.trigger > .15f;
            // A held trigger may be dismissing the preparation tutorial. Wait
            // for release; an intentional camera/locomotion gesture takes over.
            if (moved) { _combatFocus.Cancel(true); return; }
            if (pressed || TouchCameraLoadMenuBlocked()) return;
            if (Time.unscaledTime < _combatFocusNextTargetProbe) return;
            _combatFocusNextTargetProbe = Time.unscaledTime + .2f;
            if (!TryCombatFocusBounds(out Bounds bounds, out Transform leader)) return;
            float scale = TouchWorldScale;
            float distance = CombatFocusPolicy.TacticalDistance(scale, bounds.size.y, Mathf.Max(bounds.extents.x, bounds.extents.z));
            if (distance <= 0) { _combatFocus.Cancel(true); return; }
            Quaternion currentHead = TableQuaternion(_touchTabletop.Pose.rotation) * Quaternion.Inverse(referenceRotation) * frame.head.Rotation;
            Vector3 heading = Vector3.ProjectOnPlane(currentHead * Vector3.forward, Vector3.up).normalized;
            if (heading.sqrMagnitude < .5f) heading = Vector3.forward;
            float pitch = CombatFocusPolicy.EntryPitch * Mathf.Deg2Rad;
            Vector3 forward = heading * Mathf.Cos(pitch) - Vector3.up * Mathf.Sin(pitch);
            Vector3 baseline = bounds.center - forward * distance;
            Vector3 desired = baseline; desired.y = TouchCameraFocusPolicy.AutomaticHeight78(baseline.y, bounds.min.y);
            Vector3 safe;
            _touchCameraCollisionIgnore = leader; _touchCameraCollisionGroup = _touchCameraCollisionBattle = true;
            try { safe = ConstrainCinematicTravel(bounds.center, desired, .08f, out bool blocked); }
            finally { _touchCameraCollisionIgnore = null; _touchCameraCollisionGroup = _touchCameraCollisionBattle = false; }
            // Never collapse a tactical frame onto a nearby wall or a body.
            if (!TouchCameraFocusPolicy.SelectionDistanceSafe((desired-bounds.center).magnitude,(safe-bounds.center).magnitude) ||
                !TouchCameraFocusPolicy.AutomaticHeightReached78(baseline.y, bounds.min.y, safe.y))
            { _log.Log("[combat/focus] Collision fallback: retaining current pose; requested height +35% unavailable"); _combatFocus.Cancel(true); return; }
            var placement = TouchCameraFocusPolicy.PlaceHeadAt(
                new ViewPose(TablePoint(safe), TableRotation(Quaternion.LookRotation(bounds.center - safe, Vector3.up))),
                new ViewPose(TablePoint(referencePosition), TableRotation(referenceRotation)),
                new ViewPose(TablePoint(frame.head.Position), TableRotation(frame.head.Rotation)), scale);
            _touchCameraReturn.Cancel();
            ClearTouchCameraPendingFocus();
            _combatFocus.Begin();
            _touchTabletop.Initialize(placement, scale, TablePoint(bounds.center));
            _combatFocus.Arrive();
            _touchFollowReady = _touchFollowWasMoving = false;
            _touchThirdPersonFollower.Reset();
            _log.Log("[combat/focus] Entry: group frame at relative tactical height and steep downward pitch; no repeated turn/exit recentering");
        }
        static bool TryCombatFocusBounds(out Bounds bounds, out Transform leader)
        {
            bounds = default(Bounds); leader = null;
            var c = _touchCameraFocusContracts;
            if (c == null || _touchGroupContracts == null || _touchBoxUnitView == null) return false;
            try
            {
                var game = _touchGroupContracts.Game();
                var player = game == null ? null : c.Player(game);
                // Battle entry frames the player's actual party, not an enemy
                // CurrentUnit or whichever single portrait happened to be selected.
                var units = player == null ? null : c.Party(player) as IList;
                if (units == null || units.Count == 0 || units.Count > 32) return false;
                for (int i = 0; i < units.Count; ++i)
                {
                    var unit = units[i];
                    if (unit == null || c.Dead(unit)) continue;
                    var view = _touchBoxUnitView(unit) as Component;
                    if (view == null || !view.gameObject.activeInHierarchy) continue;
                    Bounds member = TouchCameraSelectionBounds(view.transform);
                    if (!FiniteCinematicPoint(member.center)) continue;
                    if (leader == null) { bounds = member; leader = view.transform; }
                    else bounds.Encapsulate(member);
                }
                return leader != null;
            }
            catch { return false; }
        }
        static void ResetCombatFocus()
        {
            _combatFocus.Reset(); _combatFocusNextProbe = _combatFocusNextTargetProbe = 0;
        }
    }
}

