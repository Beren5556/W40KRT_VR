using System;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly TouchCombatFirstPersonPolicy _touchCombatGroundClicks = new TouchCombatFirstPersonPolicy();
        static TouchCombatFirstPersonContracts _touchCombatHeadContracts;
        static bool _touchCombatGroundInNative, _touchCombatGroundAccepted;
        static object _touchCombatGroundUnit, _touchCombatGroundPending;
        static long _touchCombatGroundTurn, _touchCombatGroundRevision, _touchCombatGroundEntries, _touchCombatNativeMoves;
        static long _touchCombatTurnRevision;
        static float _touchCombatGroundPendingAt;
        static string _touchCombatHeadFault;
        internal static bool TouchCombatGroundDoubleClickHeadEnabled => _cfg.touchCombatGroundDoubleClickHead;
        internal static void SetTouchCombatGroundDoubleClickHead(bool enabled)
        {
            if (_cfg.touchCombatGroundDoubleClickHead == enabled) return;
            _cfg.touchCombatGroundDoubleClickHead = enabled; ResetTouchCombatGroundClicks(); MarkSettingsDirty();
        }
        static void InstallTouchCombatFirstPerson()
        {
            try
            {
                _touchCombatHeadContracts = TouchCombatFirstPersonContracts.Create(AccessTools.TypeByName);
                _touchHarmony.Patch(_touchCameraFocusContracts.GroundClick,
                    prefix: new HarmonyMethod(typeof(Main), nameof(TouchCombatGroundBefore)),
                    postfix: new HarmonyMethod(typeof(Main), nameof(TouchCombatGroundAfter)),
                    finalizer: new HarmonyMethod(typeof(Main), nameof(TouchCombatGroundFinished)));
                _touchHarmony.Patch(_touchCombatHeadContracts.RunCommand,
                    postfix: new HarmonyMethod(typeof(Main), nameof(TouchCombatNativeMoveAccepted)));
                _touchHarmony.Patch(_touchCombatHeadContracts.TurnChanged,
                    prefix: new HarmonyMethod(typeof(Main), nameof(TouchCombatNativeTurnChanged)));
                _touchCombatHeadFault = null;
            }
            catch (Exception error) { _touchCombatHeadContracts = null; _touchCombatHeadFault = error.Message; _log.Error("[touch/combat-head] " + error.Message); }
        }
        static bool TryTouchCombatHeadActor(out object unit, out long turnToken)
        {
            unit = null; turnToken = 0;
            var c = _touchCombatHeadContracts;
            if (!TouchCombatGroundDoubleClickHeadEnabled || c == null || InSpaceCombat || InNavigationMap ||
                !TouchCameraFocusAllowed || _modeName != "Default" || ObservedNativeLoading || PresentationTransition ||
                _touchGroupContracts == null) return false;
            var game = _touchGroupContracts.Game();
            var turn = game == null ? null : c.Turn(game);
            if (turn != null && c.Active(turn) && (!c.PlayerTurn(turn) || c.Preparation(turn))) return false;
            unit = turn != null && c.Active(turn) ? c.CurrentUnit(turn) : TouchCameraSelectedLeader();
            if (unit == null || !_touchBoxControllable(unit) || !TouchCameraIsSelected(unit) ||
                !TouchCameraPresentUnit(unit, out Component view)) return false;
            turnToken = _touchCombatTurnRevision;
            return true;
        }
        static void TouchCombatNativeTurnChanged()
        {
            // Native CurrentUnit assignments also cover interrupt/bonus turns
            // to the same actor; comparing its object identity is insufficient.
            ++_touchCombatTurnRevision; ResetTouchCombatGroundClicks();
        }
        static void TouchCombatGroundBefore(int __2, bool __3)
        {
            _touchCombatGroundInNative = _touchCombatGroundAccepted = false;
            if (__2 != 0 || __3 || !_touchPrimary.Up || !TouchDeepRelease) return;
            try
            {
                if (!TryTouchCombatHeadActor(out _touchCombatGroundUnit, out _touchCombatGroundTurn))
                { ResetTouchCombatGroundClicks(); return; }
                _touchCombatGroundRevision = SpatialGameContextRevision;
                _touchCombatGroundInNative = true;
            }
            catch (Exception error) { ResetTouchCombatGroundClicks(); TouchCombatHeadError(error); }
        }
        static void TouchCombatNativeMoveAccepted(object __instance)
        {
            if (!_touchCombatGroundInNative) return;
            try
            {
                var c = _touchCombatHeadContracts;
                object handle = c.Handle(__instance);
                // Buffered native orders already have a valid handle while
                // handle.Executor may still be null. The virtual command's
                // own UnitReference is authoritative for both paths.
                if (handle != null && ReferenceEquals(c.Unit(__instance), _touchCombatGroundUnit))
                { _touchCombatGroundAccepted = true; ++_touchCombatNativeMoves; }
            }
            catch (Exception error) { TouchCombatHeadError(error); }
        }
        static void TouchCombatGroundAfter(Vector3 __1, bool __result)
        {
            if (!_touchCombatGroundInNative) return;
            _touchCombatGroundInNative = false;
            try
            {
                if (!__result) { ResetTouchCombatGroundClicks(); return; }
                if (TouchDeepRelease && _touchCombatGroundAccepted)
                { _touchCombatGroundPending = _touchCombatGroundUnit; _touchCombatGroundPendingAt = Time.unscaledTime; }
            }
            catch (Exception error) { ResetTouchCombatGroundClicks(); TouchCombatHeadError(error); }
        }
        static Exception TouchCombatGroundFinished(Exception __exception)
        {
            _touchCombatGroundInNative = false;
            if (__exception != null) ResetTouchCombatGroundClicks();
            return __exception;
        }
        static void UpdateTouchCombatGroundHead(XrFrame frame, Vector3 referencePosition, Quaternion referenceRotation)
        {
            if (_touchCombatGroundPending == null) return;
            object pending = _touchCombatGroundPending; _touchCombatGroundPending = null;
            try
            {
                if (_touchFirstPerson || Time.unscaledTime - _touchCombatGroundPendingAt > .5f || frame.valid == 0 ||
                    !TryTouchCombatHeadActor(out object unit, out long turn) || !ReferenceEquals(unit, pending) ||
                    turn != _touchCombatGroundTurn || SpatialGameContextRevision != _touchCombatGroundRevision) return;
                ClearTouchCameraPendingFocus();
                EnterTouchHeadView(unit, frame, referencePosition, referenceRotation);
                if (_touchFirstPerson) ++_touchCombatGroundEntries;
            }
            catch (Exception error) { TouchCombatHeadError(error); }
        }
        static void ResetTouchCombatGroundClicks()
        {
            _touchCombatGroundClicks.Reset(); _touchCombatGroundPending = _touchCombatGroundUnit = null;
            _touchCombatGroundInNative = _touchCombatGroundAccepted = false;
        }
        static void TouchCombatHeadError(Exception error)
        {
            if (_touchCombatHeadFault == error.Message) return;
            _touchCombatHeadFault = error.Message; _log.Error("[touch/combat-head] " + error.Message);
        }
        internal static object TouchCombatHeadSnapshot() => new {
            Enabled = TouchCombatGroundDoubleClickHeadEnabled, Ready = _touchCombatHeadContracts != null,
            NativeMoves = _touchCombatNativeMoves, HeadEntries = _touchCombatGroundEntries,
            DoubleClickSeconds = TouchCameraFocusPolicy.DoubleClickSeconds, Fault = _touchCombatHeadFault
        };
    }
}
