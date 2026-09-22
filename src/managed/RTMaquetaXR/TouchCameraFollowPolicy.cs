using System;

namespace RTMaquetaXR
{
    // Follow only observed actor travel. Camera heading must never feed back
    // into the direction of a stick that is still held (see group movement).
    internal sealed class TouchCameraFollowPolicy
    {
        internal const float PositionResponse = .16f, TurnResponse = .42f;
        internal const float MaximumTurnSpeed = 58f, MaximumTurnAcceleration = 160f;
        internal const float TeleportDistance = 12f;
        internal Point3 Pending { get; private set; }
        internal float TurnDelta { get; private set; }
        float targetYaw, turnVelocity, remainingTurn;
        bool haveHeading;
        internal bool Settling => haveHeading && Math.Abs(remainingTurn) > .1f;

        internal void Reset() { Pending = default; TurnDelta = targetYaw = turnVelocity = remainingTurn = 0; haveHeading = false; }

        internal Point3 Step(Point3 travel, bool moving, float currentYaw, float seconds)
            => StepCore(travel, moving, currentYaw, seconds, false, 0);
        internal Point3 StepLeader(Point3 travel, bool moving, float currentYaw, float seconds, float leaderYaw)
            => StepCore(travel, moving, currentYaw, seconds, true, leaderYaw);
        Point3 StepCore(Point3 travel, bool moving, float currentYaw, float seconds, bool leaderHeading, float leaderYaw)
        {
            TurnDelta = 0;
            if (!TouchTabletopState.Finite(travel) || !ComfortCameraOptions.Finite(currentYaw) ||
                !ComfortCameraOptions.Finite(seconds) || seconds <= 0) return default;
            float length = TouchTabletopState.Length(travel);
            // A teleport or scene correction is not ordinary stick locomotion.
            if (length > TeleportDistance) { Reset(); return default; }
            float dt = ComfortCameraOptions.Seconds(seconds);
            Pending = Pending + travel;
            float blend = (float)(1 - Math.Exp(-dt / PositionResponse));
            Point3 translation = Pending * blend;
            Pending = Pending - translation;
            if (TouchTabletopState.Length(Pending) < .00001f) { translation = translation + Pending; Pending = default; }
            if (moving && leaderHeading && ComfortCameraOptions.Finite(leaderYaw))
            { targetYaw = leaderYaw; haveHeading = true; }
            else if (moving && travel.x * travel.x + travel.z * travel.z > .000001f)
            {
                targetYaw = (float)(Math.Atan2(travel.x, travel.z) * 180 / Math.PI);
                haveHeading = true;
            }
            if ((!moving && !leaderHeading) || !haveHeading) { turnVelocity = 0; return translation; }
            float error = TouchTabletopState.AngleDelta(currentYaw, targetYaw);
            float wanted = ComfortCameraOptions.Clamp(error / TurnResponse, -MaximumTurnSpeed, MaximumTurnSpeed);
            turnVelocity += ComfortCameraOptions.Clamp(wanted - turnVelocity, -MaximumTurnAcceleration * dt, MaximumTurnAcceleration * dt);
            // Reverse commands brake without briefly turning away from the new
            // direction. Every output also keeps the shortest yaw wrap.
            if (turnVelocity * error < 0) turnVelocity = 0;
            TurnDelta = turnVelocity * dt;
            if (Math.Abs(TurnDelta) >= Math.Abs(error)) { TurnDelta = error; turnVelocity = 0; }
            remainingTurn = error - TurnDelta;
            return translation;
        }
        internal static float FollowPitchDelta(float pitch,float seconds)
        {
            if(!ComfortCameraOptions.Finite(pitch))return 0;
            float dt=ComfortCameraOptions.Seconds(seconds),error=48f-pitch;
            return ComfortCameraOptions.Clamp(error*(float)(1-Math.Exp(-dt/.35f)),-35f*dt,35f*dt);
        }
    }
}
