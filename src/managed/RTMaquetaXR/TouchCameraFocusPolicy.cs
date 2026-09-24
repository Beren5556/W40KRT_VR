using System;

namespace RTMaquetaXR
{
    internal enum TouchCameraClick { None, Focus, FirstPerson }
    // Selection stays native and immediate. Only a second confirmed click
    // frames the actor. First person belongs to the separate deep hold.
    internal sealed class TouchCameraFocusPolicy
    {
        internal const float DoubleClickSeconds = .35f;
        internal const float ExitZoomRatio = .92f;
        // Always applied to the geometric baseline, never to an elevated pose.
        internal const float AutomaticHeightMultiplier78 = 1.35f;
        internal static float AutomaticHeight78(float baselineY, float floorY) =>
            floorY + (baselineY - floorY) * AutomaticHeightMultiplier78;
        internal static bool AutomaticHeightReached78(float baselineY, float floorY, float actualY) =>
            ComfortCameraOptions.Finite(actualY) && Math.Abs(actualY - AutomaticHeight78(baselineY, floorY)) <= .002f;
        object lastUnit;
        float lastClick = -1, exitSpan;
        bool haveExitSpan;
        internal TouchCameraClick Click(object unit, float seconds)
        {
            if (unit == null || !ComfortCameraOptions.Finite(seconds) || seconds < 0) { ResetClick(); return TouchCameraClick.None; }
            bool twice = ReferenceEquals(unit, lastUnit) && seconds > lastClick && seconds - lastClick <= DoubleClickSeconds;
            lastUnit = twice ? null : unit; lastClick = twice ? -1 : seconds;
            return twice ? TouchCameraClick.Focus : TouchCameraClick.None;
        }
        internal void ResetClick() { lastUnit = null; lastClick = -1; }
        internal void ResetExit() { haveExitSpan = false; exitSpan = 0; }
        internal bool ExitGesture(bool allowed, bool leftValid, bool rightValid, float leftGrip, float rightGrip, float span)
        {
            if (!allowed || !leftValid || !rightValid || !ComfortCameraOptions.Finite(span) ||
                !ComfortCameraOptions.Finite(leftGrip) || !ComfortCameraOptions.Finite(rightGrip) ||
                leftGrip < TouchTabletopState.GripPress || rightGrip < TouchTabletopState.GripPress || span < TouchTabletopState.MinimumSpan)
            { ResetExit(); return false; }
            if (!haveExitSpan) { haveExitSpan = true; exitSpan = span; return false; }
            // A small deliberate inward movement exits. Rotation, spreading or
            // a tracking jump cannot be mistaken for zooming away from the head.
            return span <= exitSpan * ExitZoomRatio && exitSpan - span >= .025f;
        }
        internal static Point3 FollowDelta(Point3 previous, Point3 current)
        {
            if (!TouchTabletopState.Finite(previous) || !TouchTabletopState.Finite(current)) return default(Point3);
            return current - previous;
        }
        internal static ViewPose PlaceHeadAt(ViewPose desiredHead, ViewPose reference, ViewPose trackedHead, float scale)
        {
            Rotation4 inverse = reference.rotation.Inverse();
            Rotation4 rotation = desiredHead.rotation * (inverse * trackedHead.rotation).Inverse();
            Point3 offset = inverse.Rotate(trackedHead.position - reference.position);
            return new ViewPose(desiredHead.position - rotation.Rotate(offset) * scale, rotation);
        }
        internal static float CloseDistance(float height, float radius, float scale)
            => GroupDistance(height, radius, scale) * .75f;
        internal static float GroupDistance(float height, float radius, float scale)
        {
            if (!ComfortCameraOptions.Finite(height) || !ComfortCameraOptions.Finite(radius) || !ComfortCameraOptions.Finite(scale)) return 0;
            return Math.Max(Math.Max(.8f, height) * 3.0f, Math.Max(scale * .95f, Math.Max(0, radius) * 2.6f));
        }
        internal static Point3 BodySize(Point3 visualSize)
        {
            // Ground auras, weapons and stale culling bounds are not bodies.
            // Keep even unusual controllable companions within a bounded frame.
            float height = ComfortCameraOptions.Finite(visualSize.y) ? ComfortCameraOptions.Clamp(visualSize.y, .8f, 4f) : 1.8f;
            float width = Math.Max(1.2f, height * 1.1f);
            return new Point3(ComfortCameraOptions.Finite(visualSize.x) ? ComfortCameraOptions.Clamp(visualSize.x, .4f, width) : .8f,
                height, ComfortCameraOptions.Finite(visualSize.z) ? ComfortCameraOptions.Clamp(visualSize.z, .4f, width) : .8f);
        }
        internal static bool NearbyParty(Point3 origin, Point3 member) => TouchTabletopState.Finite(origin) &&
            TouchTabletopState.Finite(member) && TouchTabletopState.Length(member - origin) <= 15f;
        // A model's world-space bounds may retain the previous animation/cull
        // update. Its root is the authoritative current floor, including lifts
        // and elevated walkways. Height remains a character-relative quantity.
        internal static float SelectionCenterHeight(float rootHeight, float characterHeight) =>
            ComfortCameraOptions.Finite(rootHeight) && ComfortCameraOptions.Finite(characterHeight) && characterHeight > 0 ?
                rootHeight + characterHeight * .5f : rootHeight;
        internal const int SelectionViewpointAttempts = 3;
        internal static float SelectionPitch(int attempt) => 38f + Math.Max(0, Math.Min(SelectionViewpointAttempts - 1, attempt)) * 10f;
        internal static bool SelectionDistanceSafe(float requested, float actual) =>
            ComfortCameraOptions.Finite(requested) && ComfortCameraOptions.Finite(actual) && requested > 0 &&
                actual >= requested * .85f && actual <= requested + .001f;
        internal static ViewPose FollowHead(ViewPose unitYaw, Point3 localEyeOffset, Rotation4 inversePhysicalYaw, Point3 entryHeadOffset)
        {
            Rotation4 rotation = unitYaw.rotation * inversePhysicalYaw;
            Point3 anchor = unitYaw.position + unitYaw.rotation.Rotate(localEyeOffset) - rotation.Rotate(entryHeadOffset);
            return new ViewPose(anchor, rotation);
        }
        internal static float CinematicDistance(float height, float scale) =>
            ComfortCameraOptions.Finite(height) && ComfortCameraOptions.Finite(scale) ? Math.Max(Math.Max(.8f, height) * .95f, scale * .24f) : 0;
        internal const float CinematicPitch = 20f;
        internal const float DialoguePitch76 = 25f;
        internal static float CinematicTargetHeight(float height) => ComfortCameraOptions.Finite(height) && height > 0 ?
            height * 1.10f + ComfortCameraOptions.Clamp(height * .15f, .15f, .50f) : 0;
    }

    // Only inherited actor heading is eased. The tracked HMD never enters this
    // policy; Geometry.Eye composes that live pose after the camera base.
    internal sealed class TouchHeadYawPolicy
    {
        internal const float MaximumSpeed = 70f, MaximumAcceleration = 280f, ResponseSeconds = .18f;
        internal float Current { get; private set; }
        internal float Velocity { get; private set; }
        bool initialized;
        internal void Reset(float yaw)
        {
            initialized = ComfortCameraOptions.Finite(yaw);
            Current = initialized ? TouchTabletopState.AngleDelta(0, yaw) : 0; Velocity = 0;
        }
        internal float Step(float target, float seconds)
        {
            if (!ComfortCameraOptions.Finite(target)) return Current;
            if (!initialized) { Reset(target); return Current; }
            float dt = ComfortCameraOptions.Seconds(seconds);
            if (dt <= 0) return Current;
            float error = TouchTabletopState.AngleDelta(Current, target);
            float wanted = ComfortCameraOptions.Clamp(error / ResponseSeconds, -MaximumSpeed, MaximumSpeed);
            Velocity += ComfortCameraOptions.Clamp(wanted - Velocity, -MaximumAcceleration * dt, MaximumAcceleration * dt);
            float step = Velocity * dt;
            if (step * error >= 0 && Math.Abs(step) >= Math.Abs(error)) { Current = TouchTabletopState.AngleDelta(0, target); Velocity = 0; }
            else Current = TouchTabletopState.AngleDelta(0, Current + step);
            return Current;
        }
    }
}
