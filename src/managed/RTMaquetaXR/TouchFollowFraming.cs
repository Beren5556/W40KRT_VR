using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _touchFollowPlacementSettling;
        static ViewPose _followTrackedReference78;
        static Vector3 _followReferencePosition78;
        static Quaternion _followReferenceRotation78;
        static float _followBaselinePitch78;
        static Component _followReferenceActor78;
        static bool _followCollisionBlocked78;
        static Component _followMeasuredActor76;
        static float _followActorHeight76=1.8f,_followActorRadius76=.5f;
        // Read actor transforms only. No renderer, collider or hierarchy scans
        // on the locomotion frame path; the target is the group's actual centre.
        static void FollowTouchSelectionFrame(Component leader, bool moving, XrFrame frame,
            Vector3 referencePosition, Quaternion referenceRotation)
        {
            if (!moving && !_touchFollowWasMoving && !_touchThirdPersonFollower.Settling && !_touchFollowPlacementSettling) return;
            if(_followMeasuredActor76!=leader)
            {
                var body=TouchCameraSelectionBounds(leader.transform);
                _followMeasuredActor76=leader;_followActorHeight76=body.size.y;
                _followActorRadius76=Mathf.Max(body.extents.x,body.extents.z);
            }
            Vector3 minimum=leader.transform.position, maximum=minimum;
            var units=TouchCameraSelectedUnits();
            if(units!=null)for(int i=0;i<units.Count&&i<32;i++)
            {
                var member=units[i]==null?null:_touchBoxUnitView(units[i]) as Component;
                if(member==null||!member.gameObject.activeInHierarchy ||
                    !TouchCameraFocusPolicy.NearbyParty(TablePoint(leader.transform.position),TablePoint(member.transform.position)))continue;
                minimum=Vector3.Min(minimum,member.transform.position);maximum=Vector3.Max(maximum,member.transform.position);
            }
            Vector3 centre=(minimum+maximum)*.5f+Vector3.up*(_followActorHeight76*.5f);
            float radius=Mathf.Max((maximum.x-minimum.x)*.5f,(maximum.z-minimum.z)*.5f)+_followActorRadius76;
            float distance=TouchCameraFocusPolicy.GroupDistance(_followActorHeight76,radius,TouchWorldScale);
            // Follow the logical rig, preserving physical head motion on top.
            bool entering78 = !_touchFollowReady || _followReferencePosition78 != referencePosition ||
                Quaternion.Angle(_followReferenceRotation78,referenceRotation) > .001f || _followReferenceActor78 != leader;
            if(entering78)
            {
                _followReferenceActor78=leader;
                _followTrackedReference78=new ViewPose(TablePoint(frame.head.Position),TableRotation(frame.head.Rotation));
                _followReferencePosition78=referencePosition;_followReferenceRotation78=referenceRotation;
            }
            Quaternion current=TableQuaternion(_touchTabletop.Pose.rotation) * Quaternion.Inverse(referenceRotation) *
                TableQuaternion(_followTrackedReference78.rotation);
            Vector3 forward=current*Vector3.forward;
            float yaw=Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg;
            Vector3 facing=leader.transform.forward;
            float heading=Mathf.Atan2(facing.x,facing.z)*Mathf.Rad2Deg;
            _touchThirdPersonFollower.StepLeader(default, moving, yaw, Time.unscaledDeltaTime, heading);
            // Freeze physical compensation at entry, then keep live 6DoF additive.
            // The geometric pitch is independent of the elevated output.
            if (entering78)
            {
                _followBaselinePitch78 = Mathf.Asin(Mathf.Clamp(-forward.y,-1,1))*Mathf.Rad2Deg;
            }
            _followBaselinePitch78 += TouchCameraFollowPolicy.FollowPitchDelta(_followBaselinePitch78,Time.unscaledDeltaTime);
            float nextPitch = _followBaselinePitch78;
            Quaternion next=Quaternion.Euler(nextPitch,yaw+_touchThirdPersonFollower.TurnDelta,0);
            Vector3 baseline = centre-next*Vector3.forward*distance;
            Vector3 elevated = baseline; elevated.y = TouchCameraFocusPolicy.AutomaticHeight78(baseline.y,minimum.y);
            // Collision checks use the elevated destination. A blocked endpoint
            // retains the prior pose, never silently undoes the height policy.
            _touchCameraCollisionIgnore=leader.transform; _touchCameraCollisionGroup=true;
            Vector3 safe;
            try { safe=ConstrainCinematicTravel(centre,elevated,.08f,out bool blocked); }
            finally { _touchCameraCollisionIgnore=null; _touchCameraCollisionGroup=false; }
            if (!TouchCameraFocusPolicy.AutomaticHeightReached78(baseline.y,minimum.y,safe.y))
            {
                if(!_followCollisionBlocked78) _log.Log("[camera78/follow] Collision fallback: retaining pose; requested +35% endpoint obstructed");
                _followCollisionBlocked78=true; _touchFollowPlacementSettling=true; return;
            }
            _followCollisionBlocked78=false;
            next=Quaternion.LookRotation(centre-safe,Vector3.up);
            var desired=TouchCameraFocusPolicy.PlaceHeadAt(new ViewPose(TablePoint(safe),TableRotation(next)),
                new ViewPose(TablePoint(referencePosition),TableRotation(referenceRotation)),_followTrackedReference78,TouchWorldScale);
            float blend=1-Mathf.Exp(-ComfortCameraOptions.Seconds(Time.unscaledDeltaTime)/.25f);
            var old=_touchTabletop.Pose;
            _touchFollowPlacementSettling=(TableQuaternion(old.rotation)*Vector3.forward-TableQuaternion(desired.rotation)*Vector3.forward).sqrMagnitude>.00001f ||
                (TableVector(old.position)-TableVector(desired.position)).sqrMagnitude>.0001f;
            _touchTabletop.FollowPose(new ViewPose(old.position+(desired.position-old.position)*blend,desired.rotation),TablePoint(centre));
            _combatFocus.Cancel(true);++_touchFollowFrames;
        }
    }
}
