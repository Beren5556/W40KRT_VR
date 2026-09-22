using System;
using UnityEngine;

namespace RTMaquetaXR
{
    // Distinguish short click from a hold without dispatching both actions.
    internal sealed class TouchHelpClickPolicy
    {
        readonly TouchButtonLatch latch = new TouchButtonLatch();
        float started;
        bool longPress, centred;
        internal const float CentreSeconds = 1f;
        internal bool Toggle { get; private set; }
        internal bool CentreGroup { get; private set; }
        internal bool AxesReserved { get; private set; }
        internal void Step(bool pressed, bool allowed, float now, float stickX = 0, float stickY = 0)
        {
            Toggle = CentreGroup = false;
            // Physically pressing a stick often also deflects its axes. That
            // must not start movement/follow, or a short help click looks like a
            // recenter. Keep the axes reserved through release until neutral.
            if (pressed) AxesReserved = true;
            else if (Math.Abs(stickX) < .2f && Math.Abs(stickY) < .2f) AxesReserved = false;
            latch.Step(pressed, allowed);
            if (!allowed) { longPress=centred=false; return; }
            if (latch.Down) { started=now; longPress=centred=false; }
            // A stalled frame may deliver the release as the first observation
            // past the threshold. Classify its duration too, never as short.
            if ((latch.Held || latch.Up) && now-started >= .55f) longPress=true;
            if ((latch.Held || latch.Up) && !centred && now-started >= CentreSeconds)
            { CentreGroup=true; centred=true; }
            if (latch.Up) Toggle = !longPress;
        }
    }
    public static partial class Main
    {
        static readonly TouchHelpClickPolicy _touchHelpClick = new TouchHelpClickPolicy();
        static bool TouchHelpClickOwnsAxes => _touchHelpClick.AxesReserved;
        static bool TouchGroundPreparationNow()
        {
            var c=_touchCombatHeadContracts;
            if (InSpaceCombat || c==null) return false;
            try { var game=_touchGroupContracts?.Game(); var turn=game==null?null:c.Turn(game); return turn!=null&&c.Preparation(turn); }
            catch { return false; }
        }
        static bool ContextHelpSceneAllowed => !InNavigationMap && !TouchMenuWindowVisible && !CinematicWanted &&
            !_tutorialShowing && !PresentationTransition && (_modeName=="Default" || _modeName=="Pause" || InSpaceCombat);
        static void ProcessTouchHelpShortcut(bool right, bool overlayWasOpen)
        {
            bool allowed=_touchSampleValid && TouchInputOwned && TouchGameInputAllowed && ContextHelpSceneAllowed &&
                !overlayWasOpen && !TouchRadialCaptured && !TouchDistanceReserved && !TouchSelectionCaptured &&
                !_touchUiPress.Captured && !_touchRawRightTrigger && !_touchRawLeftTrigger;
            _touchHelpClick.Step(right, allowed, Time.unscaledTime, _touchSample.right.stickX, _touchSample.right.stickY);
            if (_touchHelpClick.Toggle) SetTouchContextHintsEnabled(!TouchContextHintsEnabled);
            bool exploration=allowed && !InSpaceCombat && !TouchRadialCombatNow(out _) &&
                TouchRadialSceneAllowed && _touchGamePointer!=null && _touchBoxPointerMode(_touchGamePointer)==0;
            if (exploration && _touchHelpClick.CentreGroup) RequestTouchExplicitGroupFocus();
            bool highlight=TouchExplorationHighlightAllowed72;
            if (!highlight) { RestoreTouchExplorationHighlight(); return; }
            UpdateTouchLootHighlight65();
        }
        // A right-trigger click on a world marker must not release Y's lease.
        static bool TouchExplorationHighlightAllowed72 => _touchSampleValid && TouchInputOwned && TouchGameInputAllowed &&
            ContextHelpSceneAllowed && !TouchOverlayOpen && !TouchRadialCaptured && !TouchDistanceReserved &&
            !TouchSelectionCaptured && !InSpaceCombat && !InNavigationMap && _touchY.Held &&
            !TouchRadialCombatNow(out _) && TouchRadialSceneAllowed &&
            (!_touchUiPress.Captured || (_touchUiPressTarget!=null && IsWorldHudTransform(_touchUiPressTarget.transform)));
    }
}
