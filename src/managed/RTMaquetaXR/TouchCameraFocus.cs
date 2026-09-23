using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly TouchCameraFocusPolicy _touchCameraFocus = new TouchCameraFocusPolicy();
        static readonly TouchHeadYawPolicy _touchHeadYaw = new TouchHeadYawPolicy();
        static readonly TouchCameraFollowPolicy _touchThirdPersonFollower = new TouchCameraFollowPolicy();
        static TouchCameraFocusContracts _touchCameraFocusContracts;
        static object _touchHeadUnit, _touchFollowUnit, _touchCameraPendingUnit;
        static Transform _touchHeadRoot;
        static Vector3 _touchHeadLocal, _touchHeadEntryOffset, _touchHeadEntryRoot, _touchFollowPrevious;
        static Quaternion _touchHeadRotation, _touchHeadFacing, _touchHeadInversePhysicalYaw;
        static Vector3 _touchHeadReferencePosition;
        static Quaternion _touchHeadReferenceRotation;
        static TouchTabletopSnapshot _touchHeadTable;
        static bool _touchFirstPerson, _touchCameraPendingGroup, _touchCameraPendingHead, _touchFollowReady;
        static bool _touchFollowWasMoving;
        static float _touchHeadStickYaw;
        static int _touchCameraLastClickFrame = -1;
        static float _touchCameraPendingAt;
        static Camera _touchFocusSource;
        static string _touchFocusFault;
        static long _touchFocuses, _touchHeadEntries, _touchHeadExits, _touchFollowFrames;
        static Transform _touchCameraCollisionIgnore;
        static bool _touchCameraCollisionGroup, _touchCameraCollisionBattle;
        static bool _touchCameraFrameParty;
        internal static bool TouchCameraFirstPersonActive => !InNavigationMap && _touchFirstPerson && !CinematicWanted;
        internal static Quaternion TouchHeadMovementRotation { get; private set; } = Quaternion.identity;
        internal static float EffectiveTouchWorldScale => TouchCameraFirstPersonActive ? 1f : TouchWorldScale;

        internal static void SetTouchThirdPersonFollow(bool enabled)
        {
            if (_cfg.touchThirdPersonFollow == enabled) return;
            _cfg.touchThirdPersonFollow = enabled;
            _touchFollowReady = _touchFollowWasMoving = false;
            _touchThirdPersonFollower.Reset(); _touchFollowPlacementSettling=false;
            StopTouchGroupMovement(); // The new steering reference arms at neutral.
            MarkSettingsDirty();
        }

        static void InstallTouchCameraFocus()
        {
            try
            {
                _touchCameraFocusContracts = TouchCameraFocusContracts.Create(AccessTools.TypeByName);
                _touchHarmony.Patch(_touchCameraFocusContracts.Click, postfix: new HarmonyMethod(typeof(Main), nameof(TouchCameraUnitClicked)));
                _touchHarmony.Patch(_touchCameraFocusContracts.GroundClick, postfix: new HarmonyMethod(typeof(Main), nameof(TouchCameraGroundClicked)));
                _touchHarmony.Patch(_touchCameraFocusContracts.SelectAll, postfix: new HarmonyMethod(typeof(Main), nameof(TouchCameraNativeGroupSelected)));
                _touchHarmony.Patch(_touchCameraFocusContracts.AreaActivated,
                    prefix: new HarmonyMethod(typeof(Main), nameof(TouchCameraNativeAreaActivated)),
                    finalizer: new HarmonyMethod(typeof(Main), nameof(TouchCameraAreaActivationFinished)));
                InstallTouchHeadVisibility();
                InstallTouchCombatFirstPerson();
                _touchFocusFault = null;
            }
            catch (Exception error) { _touchFocusFault = error.Message; _log.Error("[touch/camera] Focus hooks unavailable: " + error.Message); }
        }
        static bool TouchCameraFocusAllowed => !InSpaceCombat && !InNavigationMap && TouchInputOwned && TouchGameInputAllowed && _attached && !_modeFlat &&
            !CinematicCameraOwnsInput && !TouchOverlayOpen && !TouchOverlayChordCaptured && !_touchOverUi &&
            !TouchSelectionCaptured && !TouchGestureActive && _touchGamePointer != null && _touchBoxPointerMode(_touchGamePointer) == 0;
        static void ClearTouchCameraPendingFocus(bool resetClick = true)
        {
            _touchCameraPendingUnit = null; _touchCameraPendingGroup = _touchCameraPendingHead = false;
            if (resetClick) _touchCameraFocus.ResetClick();
        }
        static void TouchCameraGroundClicked(int __2, bool __3)
        {
            // The exact native argument is `simulate`, not double-click. A real
            // ground command cancels only our deferred camera jump.
            if (TouchInputOwned && _touchPrimary.Up && __2 == 0 && !__3 &&
                _touchCameraLastClickFrame != Time.frameCount)
            { ClearTouchCameraPendingFocus();ScheduleInteractionRefresh71(); }
        }
        // Runs after the real PC handler. Abilities, UI lines and ground clicks
        // have different handlers and never reach this path.
        static void TouchCameraUnitClicked(GameObject __0, int __2, bool __3, bool __result)
        {
            if (InSpaceCombat) { TouchSpaceUnitClicked(__0, __2, __3, __result); return; }
            if (__2 == 0 && !__3 && _touchPrimary.Up) ResetTouchCombatGroundClicks();
            // A native handler may return false for an unselectable enemy. Its
            // valid physical click can still frame the visible actor; selection
            // and native orders remain entirely owned by the game.
            if (__2 != 0 || __3 || !_touchPrimary.Up || !TouchCameraFocusAllowed ||
                __0 == null || _touchCameraLastClickFrame == Time.frameCount) return;
            try
            {
                var view = __0.GetComponentInParent(_touchBoxUnitViewType);
                var unit = view == null ? null : _touchBoxViewEntity(view);
                if (!TouchCameraPresentUnit(unit, out Component visibleUnit)) return;
                bool controlled = _touchBoxControllable(unit) && TouchCameraIsSelected(unit);
                _touchCameraLastClickFrame = Time.frameCount;
                var action = _touchCameraFocus.Click(unit, Time.unscaledTime);
                if (TouchCameraFirstPersonActive) return; // Remain at the chosen head until the zoom-away gesture.
                if (!(TouchDeepRelease && controlled && __result) && action != TouchCameraClick.Focus) return;
                _touchCameraPendingUnit = unit; _touchCameraPendingGroup = false;
                _touchCameraPendingHead = TouchDeepRelease && controlled;
                _touchCameraPendingAt = Time.unscaledTime;
            }
            catch (Exception error) { TouchCameraFocusError(error); }
        }
        static bool TouchCameraIsSelected(object unit)
        {
            var units = TouchCameraSelectedUnits();
            if (units != null) for (int i = 0; i < units.Count; ++i) if (ReferenceEquals(units[i], unit)) return true;
            return false;
        }
        static IList TouchCameraSelectedUnits()
        {
            var c = _touchGroupContracts;
            if (c == null) return null;
            var game = c.Game(); var selection = game == null ? null : c.Selection(game);
            return selection == null ? null : c.SelectedUnits(selection) as IList;
        }
        static object TouchCameraSelectedLeader()
        {
            var c = _touchGroupContracts;
            if (c == null) return null;
            var game = c.Game(); var selection = game == null ? null : c.Selection(game);
            var property = selection == null ? null : c.SelectedProperty(selection);
            var unit = property == null ? null : c.SelectedValue(property);
            if (unit != null && TouchCameraIsSelected(unit)) return unit;
            var units = TouchCameraSelectedUnits(); return units != null && units.Count > 0 ? units[0] : null;
        }
        static object TouchCameraFormationReference()
        {
            object leader = TouchCameraSelectedLeader();
            if (_touchCameraFocusContracts == null || _touchGroupContracts == null || _touchBoxUnitView == null) return leader;
            var game = _touchGroupContracts.Game();
            var player = game == null ? null : _touchCameraFocusContracts.Player(game);
            var party = player == null ? null : _touchCameraFocusContracts.Party(player) as IList;
            if (party == null) return leader;
            var leaderView = leader == null ? null : _touchBoxUnitView(leader) as Component;
            if (leaderView == null)
                for (int i = 0; i < party.Count && i < 32; ++i)
                {
                    var view = party[i] == null ? null : _touchBoxUnitView(party[i]) as Component;
                    if (view != null && view.gameObject.activeInHierarchy) { leader = party[i]; leaderView = view; break; }
                }
            if (leaderView == null) return null;
            Vector3 facing = Vector3.ProjectOnPlane(leaderView.transform.forward, Vector3.up).normalized;
            Vector3 origin = leaderView.transform.position; float best = 0;
            for (int i = 0; i < party.Count && i < 32; ++i)
            {
                object unit = party[i];
                if (unit == null || _touchCameraFocusContracts.Dead(unit)) continue;
                var view = _touchBoxUnitView(unit) as Component;
                if (view == null || !view.gameObject.activeInHierarchy) continue;
                Vector3 delta = view.transform.position - origin;
                // A distant split-party actor must not become a cinematic anchor.
                if (delta.sqrMagnitude > 225f) continue;
                float advanced = Vector3.Dot(delta, facing);
                if (advanced > best + .25f) { best = advanced; leader = unit; }
            }
            return leader;
        }
        static void TouchCameraNativeGroupSelected()
        {
            if (InSpaceCombat || InNavigationMap || !TouchInputOwned || !TouchGameInputAllowed || !_attached || _modeFlat || CinematicCameraOwnsInput ||
                TouchOverlayOpen || _touchFirstPerson || (!_touchPrimary.Up && !_touchBox.Complete)) return;
            TouchCameraSelectionCommitted();
        }
        internal static void TouchCameraSelectionCommitted()
        {
            if (InSpaceCombat || InNavigationMap || _touchFirstPerson || !TouchInputOwned || !_attached || _modeFlat || CinematicCameraOwnsInput) return;
            // Committing a native selection (including group/box selection) never frames it.
            _touchFollowReady=_touchFollowWasMoving=false;_touchThirdPersonFollower.Reset(); _touchFollowPlacementSettling=false;
            // SelectAll can be raised inside each character click, before our
            // OnClick postfix. Do not erase the first click of a double-click.
            ClearTouchCameraPendingFocus(_touchBox.Complete);
        }
        static void RequestTouchExplicitGroupFocus()
        {
            if (InSpaceCombat || InNavigationMap || !_attached || _modeFlat || !_touchTabletop.Initialized ||
                CinematicCameraOwnsInput || TouchRadialCaptured || TouchOverlayOpen) return;
            if (TouchCameraSelectedLeader() == null) return;
            if (_touchFirstPerson) LeaveTouchHeadView();
            ClearTouchCameraPendingFocus();
            _touchCameraPendingGroup=true; _touchCameraPendingAt=Time.unscaledTime;
        }
        static void UpdateTouchCameraFocus(XrFrame frame, Vector3 referencePosition, Quaternion referenceRotation, Camera source)
        {
            if (InSpaceCombat || InNavigationMap || !_touchTabletop.Initialized || source == null) return;
            if (_touchFocusSource != null && _touchFocusSource != source)
            {
                // Native menus/scripted shots may replace Camera.main within
                // the same area. Keep the saved table/head owner; only actual
                // scene reset is allowed to discard them.
                ClearTouchCameraPendingFocus();
                _touchFollowReady = _touchFollowWasMoving = false;
                _touchThirdPersonFollower.Reset(); _touchFollowPlacementSettling=false;
            }
            _touchFocusSource = source;
            UpdateTouchCameraLoadFocus(frame, referencePosition, referenceRotation);
            UpdateTouchCameraGroupReturn(frame, referencePosition, referenceRotation);
            UpdateTouchCombatGroundHead(frame, referencePosition, referenceRotation);
            if (_touchFirstPerson) { UpdateTouchHeadView(frame, referencePosition, referenceRotation); return; }
            if (!TouchInputOwned || !TouchGameInputAllowed || TouchOverlayOpen || CinematicWanted || _modeFlat || frame.valid == 0 || TouchGestureActive ||
                (_touchOverUi && (_touchPrimary.Down || _touchPrimary.Held)))
            {
                ClearTouchCameraPendingFocus(); _touchFollowReady = false; _touchThirdPersonFollower.Reset(); _touchFollowPlacementSettling=false; return;
            }
            try
            {
                if (_touchCameraPendingGroup || (_touchCameraPendingUnit != null && Time.unscaledTime >= _touchCameraPendingAt))
                {
                    object unit = _touchCameraPendingUnit ?? TouchCameraSelectedLeader();
                    bool group = _touchCameraPendingGroup, head = _touchCameraPendingHead;
                    _touchCameraPendingGroup = _touchCameraPendingHead = false; _touchCameraPendingUnit = null;
                    if (unit != null && TouchCameraPresentUnit(unit, out Component present) && (!head || TouchCameraIsSelected(unit)))
                    {
                        if (head) EnterTouchHeadView(unit, frame, referencePosition, referenceRotation);
                        else FocusTouchSelection(unit, group, frame, referencePosition, referenceRotation);
                    }
                }
                if (_touchFirstPerson) return;
                if (!_cfg.touchThirdPersonFollow)
                { _touchFollowReady = _touchFollowWasMoving = false; _touchThirdPersonFollower.Reset(); _touchFollowPlacementSettling=false; return; }
                var followedUnits = TouchCameraSelectedUnits();
                object leader = followedUnits != null && followedUnits.Count > 0 ? followedUnits[0] : TouchCameraSelectedLeader();
                var view = leader == null ? null : _touchBoxUnitView(leader) as Component;
                bool moving = _touchGroupMove.Moving && !TouchGestureActive;
                if (view == null) { _touchFollowReady = _touchFollowWasMoving = false; _touchFollowUnit = null; _touchThirdPersonFollower.Reset(); _touchFollowPlacementSettling=false; return; }
                Vector3 position = view.transform.position;
                if (!ReferenceEquals(leader, _touchFollowUnit)) { _touchThirdPersonFollower.Reset(); _touchFollowPlacementSettling=false; _touchFollowWasMoving=false; }
                FollowTouchSelectionFrame(view, moving, frame, referencePosition, referenceRotation);
                // Observe the baseline also on the last idle frame: the first
                // movement step is included without integrating guessed speed.
                _touchFollowPrevious = position; _touchFollowUnit = leader; _touchFollowReady = true; _touchFollowWasMoving = moving;
            }
            catch (Exception error) { TouchCameraFocusError(error); }
        }
        static void FocusTouchSelection(object unit, bool group, XrFrame frame, Vector3 referencePosition, Quaternion referenceRotation)
        {
            if (!TouchCameraPresentUnit(unit, out Component view)) return;
            Bounds bounds = TouchCameraSelectionBounds(view.transform);
            if (group)
            {
                var units = TouchCameraFramingUnits();
                if (units != null) for (int i = 0; i < units.Count && i < 32; ++i)
                {
                    if (TouchCameraPresentUnit(units[i], out Component member) &&
                        TouchCameraFocusPolicy.NearbyParty(TablePoint(view.transform.position), TablePoint(member.transform.position)))
                        bounds.Encapsulate(TouchCameraSelectionBounds(member.transform));
                }
            }
            // Focus changes the framing, not the player's miniature scale.
            // 0.1.31 forced 6 and combined it with a very short 2.7-unit dolly.
            float scale = TouchWorldScale;
            Quaternion currentHeadView = TableQuaternion(_touchTabletop.Pose.rotation) * Quaternion.Inverse(referenceRotation) * frame.head.Rotation;
            Vector3 forward = Vector3.ProjectOnPlane(currentHeadView * Vector3.forward, Vector3.up).normalized;
            if (!group) forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .5f) forward = Vector3.forward;
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            float distance = group ? TouchCameraFocusPolicy.GroupDistance(bounds.size.y, radius, scale) :
                TouchCameraFocusPolicy.CloseDistance(bounds.size.y, radius, scale);
            Vector3 desired; Quaternion orientation; bool usable;
            _touchCameraCollisionIgnore = view.transform; _touchCameraCollisionGroup = group;
            try { usable = TryTouchSelectionViewpoint(bounds.center, forward, distance, bounds.min.y, out desired, out orientation); }
            finally { _touchCameraCollisionIgnore = null; _touchCameraCollisionGroup = false; }
            if (!usable)
            { _log.Log("[touch/camera] Selection retained; keeping current view because safe focus distance is obstructed"); return; }
            var placement = TouchCameraFocusPolicy.PlaceHeadAt(
                new ViewPose(TablePoint(desired), TableRotation(orientation)),
                new ViewPose(TablePoint(referencePosition), TableRotation(referenceRotation)),
                new ViewPose(TablePoint(frame.head.Position), TableRotation(frame.head.Rotation)), scale);
            _touchTabletop.Initialize(placement, scale, TablePoint(bounds.center));
            _combatFocus.Cancel(true); ++_touchFocuses; _touchFollowReady = false; _touchThirdPersonFollower.Reset(); _touchFollowPlacementSettling=false;
        }
        static Bounds TouchCameraSelectionBounds(Transform root)
        {
            Bounds bounds = CinematicCharacterBounds(root);
            Point3 body = TouchCameraFocusPolicy.BodySize(TablePoint(bounds.size));
            bounds.size = new Vector3(body.x, body.y, body.z);
            bounds.center = new Vector3(root.position.x, TouchCameraFocusPolicy.SelectionCenterHeight(root.position.y, body.y), root.position.z);
            return bounds;
        }
        static bool TryTouchSelectionViewpoint(Vector3 target, Vector3 heading, float distance, float floorY,
            out Vector3 desired, out Quaternion orientation)
        {
            desired = target; orientation = Quaternion.identity;
            // Collision previously allowed a 4-10 unit framing distance to
            // collapse to only 0.5, especially against raised-floor geometry.
            // Try a steeper view at the same distance, never a stronger zoom.
            // This runs only on explicit focus/load, not in the frame loop.
            for (int attempt = 0; attempt < TouchCameraFocusPolicy.SelectionViewpointAttempts; ++attempt)
            {
                float pitch = TouchCameraFocusPolicy.SelectionPitch(attempt) * Mathf.Deg2Rad;
                Vector3 forward = heading * Mathf.Cos(pitch) - Vector3.up * Mathf.Sin(pitch);
                Vector3 baseline = target - forward * distance;
                Vector3 proposed = baseline; proposed.y = TouchCameraFocusPolicy.AutomaticHeight78(baseline.y, floorY);
                Vector3 safe = ConstrainCinematicTravel(target, proposed, .08f, out bool blocked);
                if (!TouchCameraFocusPolicy.SelectionDistanceSafe((proposed-target).magnitude,(safe-target).magnitude) ||
                    !TouchCameraFocusPolicy.AutomaticHeightReached78(baseline.y, floorY, safe.y)) continue;
                desired = safe; orientation = Quaternion.LookRotation(target - safe, Vector3.up); return true;
            }
            return false;
        }
        static bool TouchCameraSelectedCollider(Collider collider)
        {
            if (!_touchCameraCollisionGroup) return false;
            var units = TouchCameraFramingUnits();
            if (_touchCameraCollisionBattle && _touchCameraFocusContracts != null && _touchGroupContracts != null)
            {
                var game = _touchGroupContracts.Game();
                var player = game == null ? null : _touchCameraFocusContracts.Player(game);
                units = player == null ? units : _touchCameraFocusContracts.Party(player) as IList;
            }
            if (units != null) for (int i = 0; i < units.Count && i < 32; ++i)
            {
                var view = units[i] == null ? null : _touchBoxUnitView(units[i]) as Component;
                if (view != null && collider.transform.IsChildOf(view.transform)) return true;
            }
            return false;
        }
        static void EnterTouchHeadView(object unit, XrFrame frame, Vector3 referencePosition, Quaternion referenceRotation)
        {
            long started=DiagnosticsRecording ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            try { EnterTouchHeadViewCore(unit,frame,referencePosition,referenceRotation); }
            finally { if(started!=0) RecordModStage("HeadViewEntry",started); }
        }
        static void EnterTouchHeadViewCore(object unit, XrFrame frame, Vector3 referencePosition, Quaternion referenceRotation)
        {
            var view = _touchBoxUnitView(unit) as Component;
            if (view == null || _touchCameraFocusContracts.Dead(unit)) return;
            if(_touchFirstPerson)
            {
                // Changing the selected actor restores the previous model but
                // retains the original tabletop return pose at the travelled location.
                Vector3 previousTravel=_touchHeadRoot!=null ? _touchHeadRoot.position-_touchHeadEntryRoot : Vector3.zero;
                _touchHeadTable.Pose.position += TablePoint(previousTravel);
            }
            else _touchHeadTable = _touchTabletop.Capture();
            RestoreTouchHeadVisuals(true); _touchHeadRoot = view.transform;
            _touchHeadUnit = unit; _touchHeadEntryRoot = _touchHeadRoot.position;
            Bounds bounds = CinematicCharacterBounds(_touchHeadRoot);
            Vector3 head = InSpaceCombat ? bounds.center + Vector3.up * bounds.extents.y * .5f :
                _touchHeadRoot.position + Vector3.up * Mathf.Clamp(bounds.size.y * .88f, .65f, 3.5f);
            // Resolve once, never traverse the skeleton during movement. Freeze
            // the local eye height so animation bob/roll cannot shake the HMD.
            if (!InSpaceCombat) foreach (var bone in _touchHeadRoot.GetComponentsInChildren<Transform>(false))
            {
                string name = bone.name;
                if (string.Equals(name, "Head", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "Bip01 Head", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Bip001 Head", StringComparison.OrdinalIgnoreCase)) { head = bone.position; break; }
            }
            Vector3 facing = Vector3.ProjectOnPlane(_touchHeadRoot.forward, Vector3.up).normalized;
            if (facing.sqrMagnitude < .5f) facing = Vector3.forward;
            head += facing * .12f;
            // Offset belongs to the entity yaw frame. Rebuilding from the real
            // root every frame avoids accumulated turn/orbit errors.
            _touchHeadFacing = Quaternion.LookRotation(facing, Vector3.up);
            _touchHeadYaw.Reset(Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg);
            _touchHeadLocal = Quaternion.Inverse(_touchHeadFacing) * (head - _touchHeadRoot.position);
            Quaternion relativeHead = Quaternion.Inverse(referenceRotation) * frame.head.Rotation;
            Vector3 relativeForward = Vector3.ProjectOnPlane(relativeHead * Vector3.forward, Vector3.up).normalized;
            Quaternion relativeYaw = relativeForward.sqrMagnitude > .5f ? Quaternion.LookRotation(relativeForward, Vector3.up) : Quaternion.identity;
            _touchHeadInversePhysicalYaw = Quaternion.Inverse(relativeYaw);
            _touchHeadStickYaw = 0;
            _touchHeadRotation = _touchHeadFacing * _touchHeadInversePhysicalYaw;
            _touchHeadEntryOffset = Quaternion.Inverse(referenceRotation) * (frame.head.Position - referencePosition);
            _touchHeadReferencePosition = referencePosition; _touchHeadReferenceRotation = referenceRotation;
            _touchFirstPerson = true; _touchCameraFocus.ResetExit(); _combatFocus.Cancel(true); ++_touchHeadEntries;
            RefreshTouchHeadVisuals();
            UpdateTouchHeadView(frame, referencePosition, referenceRotation);
        }
        static void UpdateTouchHeadView(XrFrame frame, Vector3 referencePosition, Quaternion referenceRotation)
        {
            try
            {
                // A simple selection never teleports the camera, including while in head view.
                if (_touchHeadRoot == null || _touchHeadUnit == null || !_touchHeadRoot.gameObject.activeInHierarchy || _touchCameraFocusContracts.Dead(_touchHeadUnit))
                { LeaveTouchHeadView(); return; }
                var touch = CurrentTouchFrame;
                if (TouchTabletopControlsAllowed(frame, touch) && !TouchRadialCaptured && !TouchOverlayOpen &&
                    (touch.left.activeControls & (uint)XrTouchControl.Stick) != 0)
                    _touchHeadStickYaw = Mathf.Repeat(_touchHeadStickYaw + MapAxis(touch.left.stickX) * 50f *
                        ComfortCameraOptions.Seconds(Time.unscaledDeltaTime), 360f);
                Vector3 actorForward = Vector3.ProjectOnPlane(_touchHeadRoot.forward, Vector3.up).normalized;
                if (actorForward.sqrMagnitude > .5f)
                    _touchHeadFacing = Quaternion.AngleAxis(_touchHeadYaw.Step(Mathf.Atan2(actorForward.x, actorForward.z) * Mathf.Rad2Deg,
                        Time.unscaledDeltaTime), Vector3.up);
                if (referencePosition != _touchHeadReferencePosition ||
                    Quaternion.Angle(referenceRotation, _touchHeadReferenceRotation) > .001f)
                {
                    _touchHeadEntryOffset = Quaternion.Inverse(referenceRotation) * (frame.head.Position - referencePosition);
                    Vector3 forward = Quaternion.Inverse(referenceRotation) * (frame.head.Rotation * Vector3.forward);
                    forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
                    _touchHeadInversePhysicalYaw = Quaternion.Inverse(forward.sqrMagnitude > .5f ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity);
                    _touchCameraFocus.ResetExit();
                }
                _touchHeadReferencePosition = referencePosition; _touchHeadReferenceRotation = referenceRotation;
                if (_touchCameraFocus.ExitGesture(TouchTabletopControlsAllowed(frame, touch), touch.left.GripTracked, touch.right.GripTracked,
                    touch.left.squeeze, touch.right.squeeze, Vector3.Distance(touch.left.grip.Position, touch.right.grip.Position)))
                { LeaveTouchHeadView(); return; }
                var pose = TouchCameraFocusPolicy.FollowHead(
                    new ViewPose(TablePoint(_touchHeadRoot.position), TableRotation(_touchHeadFacing)), TablePoint(_touchHeadLocal),
                    TableRotation(Quaternion.AngleAxis(_touchHeadStickYaw, Vector3.up) * _touchHeadInversePhysicalYaw), TablePoint(_touchHeadEntryOffset));
                _touchHeadRotation = TableQuaternion(pose.rotation);
                // Movement samples the visible direction once per stick gesture;
                // the absolute camera yaw can then follow the actor without a
                // feedback loop or an offset from the physical yaw on entry.
                TouchHeadMovementRotation = _touchHeadRotation * Quaternion.Inverse(referenceRotation) * frame.head.Rotation;
                _touchTabletop.InitializeFirstPerson(pose);
                if (_touchHeadVisualDirty) RefreshTouchHeadVisuals();
                _touchTrackingReferencePosition = referencePosition; _touchTrackingReferenceRotation = referenceRotation; _touchHaveTrackingReference = true;
            }
            catch (Exception error) { LeaveTouchHeadView(); TouchCameraFocusError(error); }
        }
        static void LeaveTouchHeadView()
        {
            RestoreTouchHeadVisuals(true);
            if (!_touchFirstPerson) return;
            Vector3 delta = _touchHeadRoot != null ? _touchHeadRoot.position - _touchHeadEntryRoot : Vector3.zero;
            _touchTabletop.Restore(_touchHeadTable); _touchTabletop.Translate(TablePoint(delta));
            if (_touchSpaceCameraActive) _touchSpaceScale = _touchHeadTable.Scale;
            else
            {
                float restoredScale = Release48Settings.WorldScale(_touchHeadTable.Scale, _cfg.limitExplorationZoom);
                if (_cfg.worldScale != restoredScale) { _cfg.worldScale = restoredScale; MarkSettingsDirty(); }
            }
            _touchFirstPerson = false; _touchHeadRoot = null; _touchHeadUnit = null; _touchFollowReady = false;
            _touchHeadYaw.Reset(0);
            _touchCameraFocus.ResetExit(); _touchCameraFocus.ResetClick(); ++_touchHeadExits;
        }
        internal static bool TryExitTouchFirstPerson()
        {
            if (!TouchInputOwned || !TouchCameraFirstPersonActive || CinematicWanted || !_attached || _modeFlat ||
                TouchMenuWindowVisible || _tutorialShowing || TouchMenuBlockingModal() || TouchCameraLoadMenuBlocked()) return false;
            LeaveTouchHeadView();
            ClearTouchCameraPendingFocus();
            _touchCameraReturn.Cancel();
            _touchTabletop.Cancel(true);
            StopTouchGroupMovement();
            _touchGameAxesArmed = false;
            return true;
        }
        internal static void ResetTouchCameraFocus()
        {
            ResetTouchCombatGroundClicks();
            _touchCameraReturn.Reset();
            LeaveTouchHeadView(); _touchCameraPendingGroup = _touchCameraPendingHead = _touchFollowReady = _touchFollowWasMoving = false;
            _touchCameraPendingUnit = _touchFollowUnit = null; _touchFocusSource = null;
            _touchCameraLastClickFrame = -1; _touchCameraPendingAt = 0; _touchCameraFocus.ResetClick(); _touchCameraFocus.ResetExit();
            _touchThirdPersonFollower.Reset(); _touchFollowPlacementSettling=false;
        }
        static void TouchCameraFocusError(Exception error)
        {
            if (_touchFocusFault == error.Message) return;
            _touchFocusFault = error.Message; _log.Error("[touch/camera] " + error.Message);
        }
        internal static object TouchCameraFocusSnapshot() => new {
            Ready = _touchCameraFocusContracts != null, FirstPerson = _touchFirstPerson, Focuses = _touchFocuses,
            HeadEntries = _touchHeadEntries, HeadExits = _touchHeadExits, FollowFrames = _touchFollowFrames, Fault = _touchFocusFault,
            ThirdPersonFollow = _cfg.touchThirdPersonFollow,
            DoubleClickSeconds = TouchCameraFocusPolicy.DoubleClickSeconds, EffectiveScale = EffectiveTouchWorldScale
            , LoadFraming = new { _touchCameraLoad.Pending, _touchCameraLoad.AreaReady, _touchCameraLoad.Requests, _touchCameraLoad.Attempts }
            , ReturnFraming = new { _touchCameraReturn.Pending, _touchCameraReturn.Requests, _touchCameraReturn.Attempts }
            , HeadVisuals = TouchHeadVisualSnapshot()
        };
    }
}

