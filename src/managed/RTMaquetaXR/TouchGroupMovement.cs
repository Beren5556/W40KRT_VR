using System;
using System.Collections;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly TouchGroupMovementPolicy _touchGroupMove = new TouchGroupMovementPolicy();
        static TouchGroupMovementContracts _touchGroupContracts;
        static object _touchGroupSender, _touchGroupSentUnits;
        static bool _touchGroupFault;
        static string _touchGroupError;
        static string _touchGroupBlockReason = "Not sampled";
        static int _touchGroupUnityFrame = -1;
        static int _touchGroupStartFrame = -1;
        static long _touchGroupMovePackets, _touchGroupStopPackets;

        static void InstallTouchGroupMovement()
        {
            try
            {
                _touchGroupContracts = TouchGroupMovementContracts.Create(AccessTools.TypeByName);
                _touchHarmony.Patch(_touchGroupContracts.Fill, prefix: new HarmonyMethod(typeof(Main), nameof(TouchGroupBeforeFill)));
                _touchGroupFault = false; _touchGroupError = null; _touchGroupBlockReason = "Waiting for gameplay";
                _touchGroupUnityFrame = -1; _touchGroupMove.Reset();
                _log.Log("[touch/move] Right stick drives the game's selected-group movement in exploration; PC interface and combat rules retained.");
            }
            catch (Exception error)
            {
                _touchGroupFault = true; _touchGroupError = error.Message;
                _log.Error("[touch/move] Native movement contract unavailable; Touch pointer, UI and VR remain active: " + error.Message);
            }
        }
        static void TouchGroupBeforeFill(object __instance)
        {
            if (_touchGroupContracts == null || _touchGroupFault) return;
            if (!TouchInputOwned) { if (_touchGroupMove.Moving) StopTouchGroupMovement(); return; }
            if (_touchGroupUnityFrame == Time.frameCount) return;
            _touchGroupUnityFrame = Time.frameCount;
            try
            {
                EnsureTouchSample();
                if (_touchGroupSender != null && !ReferenceEquals(_touchGroupSender, __instance)) StopTouchGroupMovement();
                bool allowed = !InSpaceCombat && !InNavigationMap && TouchGameInputAllowed && !TouchLocalMapOwnsAxes && !TouchHelpClickOwnsAxes && _touchGameAxesArmed && !TouchDistanceReserved && !_modeFlat && _attached &&
                    _modeName == "Default" && !CinematicCameraOwnsInput && !NativeTutorialInputBlocked && !_touchUiPress.Captured &&
                    !TouchSelectionCaptured && !_touchBox.Pending && !TouchGestureActive && TouchTabletopPoseReady &&
                    !_touchRawRightTrigger && !_touchRawLeftTrigger && !_touchA.Held && !_touchB.Held && !_touchX.Held && !_touchMenu.Held &&
                    (_touchSample.right.activeControls & (uint)XrTouchControl.Stick) != 0 &&
                    _touchGamePointer != null && _touchBoxPointerMode(_touchGamePointer) == 0 && _touchGroupContracts.Allowed();
                // Main-menu samples arrive before the tabletop has ever been
                // initialized. Do not inspect that zero pose on a blocked path.
                if (!allowed) { _touchGroupBlockReason = "Input or game state blocks movement"; StopTouchGroupMovement(); return; }
                object game = _touchGroupContracts.Game();
                if (_touchGroupContracts.UiBlocked(game))
                { _touchGroupBlockReason = "An open game window owns input"; StopTouchGroupMovement(); return; }
                object unit = null; ulong group = 0;
                {
                    var selection = _touchGroupContracts.Selection(game);
                    var property = selection == null ? null : _touchGroupContracts.SelectedProperty(selection);
                    unit = property == null ? null : _touchGroupContracts.SelectedValue(property);
                    var units = selection == null ? null : _touchGroupContracts.SelectedUnits(selection) as IList;
                    bool contains = false;
                    group = 1469598103934665603;
                    if (units != null)
                        for (int i = 0; i < units.Count; ++i)
                        {
                            object selected = units[i]; contains |= ReferenceEquals(selected, unit);
                            group = unchecked((group ^ (uint)(selected == null ? 0 : RuntimeHelpers.GetHashCode(selected))) * 1099511628211);
                        }
                    allowed = unit != null && contains && _touchGroupContracts.Controllable(unit) && _touchGroupContracts.CanMove(unit);
                }
                float yaw=0;
                if (allowed && !TryTouchMovementAim(unit,out yaw))
                { _touchGroupBlockReason = "Waiting for a valid horizontal control view"; StopTouchGroupMovement(); return; }
                var command = _touchGroupMove.Step(allowed, unit, group, _touchSample.right.stickX, _touchSample.right.stickY, yaw);
                _touchGroupBlockReason = !allowed ? "No movable selected leader" : !_touchGroupMove.Armed ? "Centre the right stick" : "Ready";
                SendTouchGroupMovement(__instance, command);
            }
            catch (Exception error)
            {
                StopTouchGroupMovement(); _touchGroupFault = true; _touchGroupError = error.Message;
                _log.Error("[touch/move] Movement suspended: " + error.Message);
            }
        }
        // The right stick retains its exploration role while the ray merely
        // passes over HUD graphics. It must not also scroll that hovered HUD.
        // Actual presses/drags and open windows still keep native UI scrolling.
        internal static bool TouchGroupHoverScrollBlocked
        {
            get
            {
                if (InSpaceCombat || InNavigationMap || !TouchInputOwned || _touchGroupContracts == null || _touchGroupFault || !_attached || _modeFlat ||
                    _modeName != "Default" || CinematicCameraOwnsInput || NativeTutorialInputBlocked || _touchUiPress.Captured) return false;
                try { return _touchGroupContracts.Allowed() && !_touchGroupContracts.UiBlocked(_touchGroupContracts.Game()); }
                catch { return false; } // A failed optional movement route never traps native menu scrolling.
            }
        }
        static void SendTouchGroupMovement(object controller, TouchGroupMoveCommand command)
        {
            if (command.Kind == TouchGroupMoveKind.None || controller == null || command.Unit == null) return;
            _touchGroupContracts.Push(controller, command.Unit, new Vector2(command.X, command.Y), command.Strength);
            if (command.Kind == TouchGroupMoveKind.Stop)
            {
                // Push normally captures the CURRENT selection. A cancellation
                // after selection changes must stop our previous group, without
                // asking newly selected units to follow the previous leader.
                if (_touchGroupSentUnits != null) _touchGroupContracts.RestoreSentUnits(controller, _touchGroupSentUnits);
                _touchGroupSentUnits = _touchGroupSender = null; ++_touchGroupStopPackets;
            }
            else
            {
                if (_touchGroupSender == null) _touchGroupStartFrame = Time.frameCount;
                _touchGroupSender = controller; _touchGroupSentUnits = _touchGroupContracts.SentUnits(controller);
                ++_touchGroupMovePackets;
            }
        }
        static void StopTouchGroupMovement()
        {
            _touchMoveAim.Reset();
            var command = _touchGroupMove.Stop();
            try { if (_touchGroupContracts != null) SendTouchGroupMovement(_touchGroupSender, command); }
            catch (Exception error) { _log.Error("[touch/move] Stop: " + error.Message); }
            finally { _touchGroupMove.Reset(); _touchGroupSender = _touchGroupSentUnits = null; }
        }
        internal static object TouchGroupMovementSnapshot() => new {
            Ready = _touchGroupContracts != null && !_touchGroupFault, Moving = _touchGroupMove.Moving,
            Error = _touchGroupError,
            BlockReason = _touchGroupBlockReason,
            RequiresNeutral = !_touchGroupMove.Armed, MovePackets = _touchGroupMovePackets, StopPackets = _touchGroupStopPackets,
            LastStartFrame = _touchGroupStartFrame,
            Route = "Right stick / stable horizontal control view captured per movement segment / exploration only; combat stays tactical"
        };
    }
}

