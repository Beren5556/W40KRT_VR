using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly TouchSelectionPolicy _touchBox = new TouchSelectionPolicy();
        static Func<object> _touchBoxInstance, _touchBoxGame;
        static Func<object, bool> _touchBoxAllowed, _touchBoxControllable;
        static Action<object> _touchBoxCommit, _touchBoxCancel, _touchBoxClearMembers;
        static Func<object, object> _touchBoxMembers, _touchBoxState, _touchBoxAwake, _touchBoxUnitView, _touchBoxViewEntity, _touchBoxSoftCollider;
        static Func<object, Vector3> _touchBoxUnitPosition;
        static Func<object, object, bool> _touchBoxAddMember;
        static Func<object, int> _touchBoxPointerMode;
        static Func<GameObject, bool> _touchBoxIsGround;
        static Type _touchBoxGroundHandlerType, _touchBoxUnitHandlerType, _touchBoxEntityViewType, _touchBoxUnitViewType;
        static object _touchBoxOwner, _touchBoxMemberSet;
        static Camera _touchBoxSourceCamera;
        static int _touchBoxScene, _touchBoxCandidateCount, _touchBoxLastSelectedCount;
        static float _touchBoxSourceScale, _touchBoxNextRoster;
        static Vector3 _touchBoxClickPoint;
        static Vector3 _touchBoxSourcePosition;
        static Quaternion _touchBoxSourceRotation;
        static GameObject _touchBoxClickTarget;
        static TouchSelectionPlane _touchBoxPlane;
        static Point3 _touchBoxAimAnchor;
        static SelectionRect _touchBoxAnchorRect, _touchBoxRectangle;
        static long _touchBoxCompletions, _touchBoxCancellations, _touchBoxUnitsSelected, _touchBoxEmptyCompletions;
        struct TouchSelectionUnit
        {
            internal object Unit;
            internal Component View;
            internal Bounds Footprint;
            internal bool Selected;
        }
        static readonly List<TouchSelectionUnit> _touchBoxUnits = new List<TouchSelectionUnit>(16);
        static bool TouchSelectionCaptured => _touchBox.Captured;
        static bool TouchSelectionSuppressTilt(XrTouchFrame sample) => !_modeFlat &&
            (_touchBox.Pending || _touchBox.Active) && !TouchGestureActive;

        static void InstallTouchSelection()
        {
            var c = TouchSelectionContracts.Create(AccessTools.TypeByName);
            _touchBoxGroundHandlerType = c.GroundHandlerType; _touchBoxUnitHandlerType = c.UnitHandlerType;
            _touchBoxEntityViewType = c.EntityViewType; _touchBoxUnitViewType = c.UnitViewType;
            _touchBoxInstance = c.Instance; _touchBoxAllowed = c.Allowed;
            _touchBoxCommit = c.Commit; _touchBoxCancel = c.Cancel;
            _touchBoxPointerMode = c.PointerMode; _touchBoxIsGround = c.IsGround;
            _touchBoxControllable = c.Controllable; _touchBoxUnitView = c.UnitView;
            _touchBoxUnitPosition = c.UnitPosition; _touchBoxViewEntity = c.ViewEntity;
            _touchBoxGame = c.Game; _touchBoxState = c.State; _touchBoxAwake = c.Awake;
            _touchBoxSoftCollider = c.SoftCollider; _touchBoxMembers = c.Members;
            _touchBoxClearMembers = c.ClearMembers; _touchBoxAddMember = c.AddMember;
            // The native PC rectangle/32-logical-unit threshold is intentionally
            // not called. Only original eligibility and final selection remain.
        }

        static void SampleTouchSelection(bool allowed)
        {
            bool canReserve = !InSpaceCombat && !InNavigationMap && allowed && !_modeFlat && _attached && !CinematicCameraOwnsInput && !TouchGestureActive &&
                !_touchRawLeftTrigger && !_touchA.Held && !_touchB.Held && !_touchMenu.Held;
            _touchBox.Step(TouchGameplayRightTrigger, canReserve);
            if (_touchBox.Cancelled)
            {
                CancelTouchSelectionNative(); ClearTouchWorldPointerPress(); CancelTouchUiModule(_touchModule);
                _touchClickFrozen = false;
            }
        }

        // The original game picker resolves this exact ray, target and handler.
        // A friendly selectable unit may start a rectangle, but an NPC, ability,
        // interactable or PC UI control retains its ordinary click semantics.
        static void TryStartTouchSelection(object pointer, GameObject target, Vector3 hit, object handler)
        {
            if (InSpaceCombat || InNavigationMap || !_touchPrimary.Down || !_touchRawRightTrigger || !TouchGameInputAllowed || _modeFlat || !_attached ||
                CinematicCameraOwnsInput || _touchOverUi || !_touchRayReady || !_touchClickFrozen ||
                _touchA.Held || _touchRawLeftTrigger || !ReferenceEquals(pointer, _touchGamePointer) || target == null || handler == null ||
                _touchBoxPointerMode(pointer) != 0 || !TouchSelectionPlane.Finite(ToPoint(hit)) || _attachedCam == null) return;
            var owner = _touchBoxInstance();
            if (!(owner is UnityEngine.Object unityOwner) || unityOwner == null || !_touchBoxAllowed(owner)) return;
            object startUnit = null; Component startView = null;
            if (handler.GetType() == _touchBoxUnitHandlerType)
            {
                startView = target.GetComponentInParent(_touchBoxUnitViewType);
                if (startView == null || (startUnit = _touchBoxViewEntity(startView)) == null || !_touchBoxControllable(startUnit)) return;
            }
            else if (handler.GetType() != _touchBoxGroundHandlerType || !_touchBoxIsGround(target) || target.GetComponentInParent(_touchBoxEntityViewType) != null) return;
            Vector3 support = startUnit == null ? hit : _touchBoxUnitPosition(startUnit);
            Vector3 right = _touchPointerLeft != null ? _touchPointerLeft.transform.right : _attachedCam.transform.right;
            if (!TouchSelectionPlane.TryCreate(ToPoint(support), ToPoint(right), new Point3(0, 1, 0), out var plane) ||
                !plane.RayPoint(ToPoint(_touchRay.origin), ToPoint(_touchRay.direction), WorldScale * 25, out var down)) return;
            float distance = Vector3.Distance(_touchRay.origin, TableVector(plane.World(down)));
            if (!_touchBox.StartCandidate(down.X, down.Y, TouchSelectionPlane.DragThreshold(WorldScale, distance), Time.unscaledTime)) return;
            _touchBoxPlane = plane;
            _touchBoxAimAnchor = plane.World(down);
            _touchBoxAnchorRect = startUnit == null ? new SelectionRect(down.X, down.Y) : new SelectionRect(0, 0);
            if (startView != null)
            {
                var footprint = TouchSelectionFootprint(startUnit, startView);
                _touchBoxAnchorRect = plane.IncludeFootprint(_touchBoxAnchorRect, ToPoint(footprint.center), ToPoint(footprint.extents));
            }
            _touchBoxRectangle = _touchBoxAnchorRect;
            _touchBoxOwner = owner; _touchBoxMemberSet = _touchBoxMembers(owner); _touchBoxSourceCamera = _attachedCam;
            _touchBoxClickPoint = hit; _touchBoxClickTarget = target;
            _touchBoxScene = _attachedCam.gameObject.scene.handle; _touchBoxSourceScale = WorldScale;
            _touchBoxSourcePosition = _attachedCam.transform.position; _touchBoxSourceRotation = _attachedCam.transform.rotation;
            _touchBoxNextRoster = 0; _touchBoxCandidateCount = 0; _touchBoxUnits.Clear();
        }

        static void ProcessTouchSelectionPointer()
        {
            if (!_touchBox.Pending && !_touchBox.Active) return;
            long started = DiagnosticsRecording && _cfg.detailedProfiling ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            try
            {
                var owner = _touchBoxInstance();
                if (!(owner is UnityEngine.Object unityOwner) || unityOwner == null || _attachedCam == null ||
                    !_touchRayReady || !_touchBoxAllowed(owner) || _touchBoxMemberSet == null ||
                    (_touchGamePointer != null && _touchBoxPointerMode(_touchGamePointer) != 0) ||
                    !ReferenceEquals(owner, _touchBoxOwner) || _touchBoxSourceCamera != _attachedCam ||
                    _touchBoxScene != _attachedCam.gameObject.scene.handle || Mathf.Abs(WorldScale - _touchBoxSourceScale) > .001f ||
                    (_attachedCam.transform.position - _touchBoxSourcePosition).sqrMagnitude > .000001f ||
                    Quaternion.Angle(_attachedCam.transform.rotation, _touchBoxSourceRotation) > .02f ||
                    !_touchBoxPlane.RayPoint(ToPoint(_touchRay.origin), ToPoint(_touchRay.direction), WorldScale * 25, out var point) ||
                    !TouchSelectionPlane.AimDistance(_touchBoxAimAnchor, ToPoint(_touchRay.origin), ToPoint(_touchRay.direction), out float aimDistance))
                { CancelTouchSelection(); return; }
                _touchBox.UpdatePointer(point.X, point.Y, Time.unscaledTime, aimDistance);
                if (_touchBox.Cancelled) { CancelTouchSelection(); return; }
                if (_touchBox.Begin)
                {
                    // Clear the stored native down BEFORE its release branch.
                    ClearTouchWorldPointerPress(); CancelTouchUiModule(_touchModule);
                    _touchPrimary.Cancel(); _touchSecondary.Cancel(); _touchGameAxesArmed = false; _touchClickFrozen = false;
                    _touchBoxCancel(owner);
                }
                if (_touchBox.Pending || _touchBox.ClickReleased)
                {
                    _touchRay = _touchFrozenRay; _touchScreen = _touchFrozenScreen;
                    _touchOverUi = false; _touchHasPoint = true;
                    _touchTarget = _touchBoxClickTarget; _touchTargetPoint = _touchBoxClickPoint;
                    if (_touchBox.ClickReleased) ClearTouchSelectionOwner();
                    return;
                }
                _touchBoxRectangle = _touchBoxAnchorRect; _touchBoxRectangle.Include(point);
                RefreshTouchSelectionMembers(_touchBox.Begin || _touchBox.Complete);
                _touchOverUi = false; _touchTarget = null; _touchHasPoint = true;
                _touchTargetPoint = TableVector(_touchBoxPlane.World(point));
                _touchScreen = new Vector2(-4096, -4096);
                if (_touchBox.Complete)
                {
                    _touchBoxLastSelectedCount = _touchBoxCandidateCount;
                    long commitStarted = started != 0 ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                    try { _touchBoxCommit(owner); if (_touchBoxLastSelectedCount > 0) TouchCameraSelectionCommitted(); }
                    finally { if (commitStarted != 0) RecordModStage("TouchSelectionCommit", commitStarted); }
                    ++_touchBoxCompletions; _touchBoxUnitsSelected += _touchBoxLastSelectedCount;
                    if (_touchBoxLastSelectedCount == 0) ++_touchBoxEmptyCompletions;
                    ClearTouchSelectionOwner();
                }
            }
            catch (Exception error) { CancelTouchSelection(); _log.Error("[touch/selection] " + error.Message); }
            finally { if (started != 0) RecordModStage("TouchSelectionTracking", started); }
        }

        static Bounds TouchSelectionFootprint(object unit, Component view)
        {
            Vector3 position = _touchBoxUnitPosition(unit);
            var collider = _touchBoxSoftCollider(view) as Collider;
            Bounds bounds = collider != null ? collider.bounds : new Bounds(position, new Vector3(.3f, 0, .3f));
            var center = bounds.center; center.y = _touchBoxPlane.Origin.y;
            var size = bounds.size; size.y = 0;
            return new Bounds(center, size);
        }

        static void RefreshTouchSelectionMembers(bool forceRoster)
        {
            if (forceRoster || Time.unscaledTime >= _touchBoxNextRoster)
            {
                _touchBoxNextRoster = Time.unscaledTime + .25f; _touchBoxUnits.Clear();
                var units = _touchBoxAwake(_touchBoxState(_touchBoxGame())) as IList;
                if (units == null) throw new InvalidOperationException("Game awake-unit list unavailable");
                for (int i = 0; i < units.Count; ++i)
                {
                    object unit = units[i]; if (unit == null || !_touchBoxControllable(unit)) continue;
                    var view = _touchBoxUnitView(unit) as Component;
                    if (view != null) _touchBoxUnits.Add(new TouchSelectionUnit { Unit = unit, View = view });
                }
            }
            _touchBoxClearMembers(_touchBoxMemberSet); _touchBoxCandidateCount = 0;
            for (int i = 0; i < _touchBoxUnits.Count; ++i)
            {
                var candidate = _touchBoxUnits[i]; candidate.Selected = false;
                if (candidate.View != null && _touchBoxControllable(candidate.Unit))
                {
                    candidate.Footprint = TouchSelectionFootprint(candidate.Unit, candidate.View);
                    candidate.Selected = _touchBoxPlane.Overlaps(_touchBoxRectangle, ToPoint(candidate.Footprint.center), ToPoint(candidate.Footprint.extents));
                    if (candidate.Selected && _touchBoxAddMember(_touchBoxMemberSet, candidate.View)) ++_touchBoxCandidateCount;
                }
                _touchBoxUnits[i] = candidate;
            }
        }

        static void CancelTouchSelection()
        {
            bool owned = _touchBox.Pending || _touchBox.Active || _touchBox.Captured;
            _touchBox.Cancel(); CancelTouchSelectionNative();
            if (owned)
            {
                ClearTouchWorldPointerPress(); CancelTouchUiModule(_touchModule);
                _touchPrimary.Cancel(); _touchSecondary.Cancel(); _touchClickFrozen = false;
            }
        }
        static void ClearTouchSelectionOwner()
        {
            _touchBoxOwner = _touchBoxMemberSet = null; _touchBoxSourceCamera = null;
            _touchBoxClickTarget = null;
            _touchBoxAimAnchor = default;
            _touchBoxUnits.Clear(); _touchBoxCandidateCount = 0; HideTouchSelectionVisuals();
        }
        static void CancelTouchSelectionNative()
        {
            object owner = _touchBoxOwner; ClearTouchSelectionOwner();
            if (owner is UnityEngine.Object unityOwner && unityOwner != null && _touchBoxCancel != null)
            {
                try { _touchBoxCancel(owner); ++_touchBoxCancellations; }
                catch (Exception error) { _log.Error("[touch/selection-cancel] " + error.Message); }
            }
        }
    }
}

