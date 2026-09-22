using System.Collections;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        // Use the actual present party on area entry. Selection may still name
        // actors from the previous save, even while their views remain alive.
        static IList TouchCameraPartyUnits()
        {
            var c = _touchCameraFocusContracts;
            if (c == null || _touchGroupContracts == null) return null;
            var game = _touchGroupContracts.Game();
            var player = game == null ? null : c.Player(game);
            return player == null ? null : c.Party(player) as IList;
        }
        static IList TouchCameraFramingUnits() => _touchCameraFrameParty ? TouchCameraPartyUnits() : TouchCameraSelectedUnits();
        static bool TouchCameraPresentUnit(object unit, out Component view)
        {
            view = null;
            var c = _touchCameraFocusContracts;
            if (unit == null || c == null || _touchBoxUnitView == null || !c.InGame(unit) || c.Dead(unit)) return false;
            object scene = c.HoldingState(unit);
            if (scene == null || !c.SceneLoaded(scene)) return false;
            view = _touchBoxUnitView(unit) as Component;
            return view != null && view.gameObject.activeInHierarchy && TouchTabletopState.Finite(TablePoint(view.transform.position));
        }
        static object TouchCameraLoadPartyLeader(out int count)
        {
            count = 0;
            var units = TouchCameraPartyUnits();
            if (units == null || units.Count == 0 || units.Count > 32) return null;
            object selected = TouchCameraSelectedLeader(), leader = null;
            Component leaderView = null;
            for (int i = 0; i < units.Count; ++i)
            {
                if (!TouchCameraPresentUnit(units[i], out Component view)) continue;
                if (leader == null || ReferenceEquals(units[i], selected)) { leader = units[i]; leaderView = view; }
            }
            if (leaderView == null) return null;
            for (int i = 0; i < units.Count; ++i)
                if (TouchCameraPresentUnit(units[i], out Component view) &&
                    TouchCameraFocusPolicy.NearbyParty(TablePoint(leaderView.transform.position), TablePoint(view.transform.position))) ++count;
            return leader;
        }
        static void FocusTouchLoadedParty(object leader, XrFrame frame, Vector3 referencePosition, Quaternion referenceRotation)
        {
            _touchCameraFrameParty = true;
            try { FocusTouchSelection(leader, true, frame, referencePosition, referenceRotation); }
            finally { _touchCameraFrameParty = false; }
        }
    }
}
