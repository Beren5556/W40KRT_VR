using UnityEngine;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly FollowTranslation80 _followTranslation80 = new FollowTranslation80();
        static bool _followRecenterWasActive80;
        static void SetFollowRecenter80(bool value)
        {
            _cfg.touchFollowRecenter = value;
            _touchFollowReady = _touchFollowWasMoving = _touchFollowPlacementSettling = false;
            _touchThirdPersonFollower.Reset(); _followTranslation80.Reset(); MarkSettingsDirty();
        }
        static bool FollowWithoutRecenter80(Component leader, bool moving)
        {
            if (_followRecenterWasActive80 != _cfg.touchFollowRecenter)
            {
                _followRecenterWasActive80 = _cfg.touchFollowRecenter;
                _touchFollowReady = _touchFollowPlacementSettling = false;
                _touchThirdPersonFollower.Reset(); _followTranslation80.Reset();
            }
            if (_cfg.touchFollowRecenter) return false;
            // The caller has already selected and validated the leader. The
            // cached component identity avoids depending on a native reverse map.
            bool valid = _touchFollowReady && _followTranslationLeader80 == leader;
            _followTranslationLeader80 = leader;
            var delta = _followTranslation80.Step(TablePoint(leader.transform.position - _touchFollowPrevious),
                valid, moving, _touchFollowWasMoving, Time.unscaledDeltaTime);
            if (TouchTabletopState.Length(delta) <= 0) return true;
            Vector3 start = TableVector(_touchTabletop.Pose.position), proposed = start + TableVector(delta);
            _touchCameraCollisionIgnore = leader.transform; _touchCameraCollisionGroup = true;
            Vector3 safe;
            try { safe = ConstrainCinematicTravel(start, proposed, .08f, out bool blocked); if (blocked) _followTranslation80.Reset(); }
            finally { _touchCameraCollisionIgnore = null; _touchCameraCollisionGroup = false; }
            safe.y = start.y;
            _touchTabletop.Translate(TablePoint(safe - start));
            _combatFocus.Cancel(true); ++_touchFollowFrames;
            return true;
        }
        static Component _followTranslationLeader80;
    }
}
