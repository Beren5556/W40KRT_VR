using System;

namespace RTMaquetaXR
{
    internal enum TouchTabletopGesture { Idle, WaitingForRelease, PanLeft, PanRight, RotateAndScale, Tilt, Turn }
    internal struct TouchTabletopInput
    {
        internal bool Allowed, LeftValid, RightValid, StickBlocked;
        internal float LeftSqueeze, RightSqueeze, TiltAxis, TurnAxis, Seconds;
        internal Point3 Left, Right, PhysicalUpReference;
    }
    internal struct TouchTabletopSnapshot
    {
        internal ViewPose Pose;
        internal Point3 PivotReference;
        internal float Scale, Tilt, Yaw, ScaleMin, ScaleMax, TiltMin, TiltMax;
    }

    // Every tracked pose and ray uses this mapping. World geometry and physics
    // stay untouched. Gesture limits never filter the physical head pose.
    internal sealed class TouchTabletopState
    {
        internal const float GripPress = .65f, GripRelease = .35f, MinimumSpan = .075f;
        internal const float MaxPanMetersPerSecond = 1.5f, MaxTurnDegreesPerSecond = 135f;
        internal const float TwoHandTurnGain = 1.65f, TrackingPauseSeconds = .3f;
        // The requested reduction belongs only to two-grip yaw. Stick turning,
        // tilt, pan and zoom retain their established response and limits.
        internal const float TwoHandTurnReduction = .85f;
        internal const float MaxZoomLogPerSecond = .9f, TiltDegreesPerSecond = 18f;
        internal const float StickTurnDegreesPerSecond = 34.5f;
        // A modest extra response to hand separation, shared by the distance
        // response and its rate cap so normal quick gestures also benefit.
        internal const float TwoHandZoomGain = 1.25f;
        internal ViewPose Pose { get; private set; }
        internal float Scale { get; private set; } = 10f;
        internal float Tilt { get; private set; }
        internal float Yaw { get; private set; }
        internal bool Limited { get; private set; }
        internal bool Initialized { get; private set; }
        internal TouchTabletopGesture Gesture { get; private set; }
        internal bool Manipulating => Gesture == TouchTabletopGesture.PanLeft || Gesture == TouchTabletopGesture.PanRight ||
            Gesture == TouchTabletopGesture.RotateAndScale || Gesture == TouchTabletopGesture.Tilt || Gesture == TouchTabletopGesture.Turn;
        float scaleMin = ComfortCameraOptions.ScaleMin, scaleMax = ComfortCameraOptions.ScaleMax;
        float tiltMin = ComfortCameraOptions.TiltMin, tiltMax = ComfortCameraOptions.TiltMax;
        internal void ConfigureLimits(float near, float far, float lowTilt, float highTilt)
        {
            if (!Finite(near) || !Finite(far) || near <= 0 || far < near || !Finite(lowTilt) || !Finite(highTilt) || lowTilt > highTilt) throw new ArgumentOutOfRangeException("table limits");
            scaleMin = near; scaleMax = far; tiltMin = lowTilt; tiltMax = highTilt;
        }
        float LimitScale(float value) => ComfortCameraOptions.Clamp(Finite(value) ? value : scaleMin, scaleMin, scaleMax);
        bool requireRelease = true, leftHeld, rightHeld, stickReady;
        float trackingPause;
        Point3 previousLeft, previousRight, pivotReference;

        internal void Initialize(ViewPose pose, float scale, Point3 worldPivot)
        {
            Pose = pose; Scale = LimitScale(scale); Tilt = Yaw = 0;
            pivotReference = pose.rotation.Inverse().Rotate(worldPivot - pose.position) * (1f / Scale);
            Initialized = true; Cancel(true);
        }
        internal void Reset() { Initialized = false; Cancel(true); }
        // A scripted camera temporarily owns only the view pose. Preserve the
        // player's pivot, zoom, tilt and yaw so leaving a cutscene can restore
        // the exact tabletop they arranged before it began.
        internal void OverrideViewPose(ViewPose pose) { Pose = pose; Cancel(true); }
        // Following observed entity displacement preserves the pivot and all
        // gesture baselines; it must not re-arm grips every rendered frame.
        internal void Translate(Point3 delta)
        {
            if (Initialized && Finite(delta)) Pose = new ViewPose(Pose.position + delta, Pose.rotation);
        }
        internal void FollowPose(ViewPose pose, Point3 pivot)
        {
            if (!Initialized || !Finite(pose.position) || !Finite(pivot)) return;
            Pose=pose;
            pivotReference=Pose.rotation.Inverse().Rotate(pivot-Pose.position)*(1f/Scale);
        }
        internal void FollowOrbit(Point3 worldPivot, float yawDelta)
            => FollowOrbit(worldPivot,yawDelta,0);
        internal void FollowOrbit(Point3 worldPivot, float yawDelta, float pitchDelta)
        {
            if (!Initialized || !Finite(worldPivot) || !Finite(yawDelta) || !Finite(pitchDelta)) return;
            var turn = AxisAngle(new Point3(0, 1, 0), yawDelta) * AxisAngle(Pose.rotation.Rotate(new Point3(1,0,0)),pitchDelta);
            Pose = new ViewPose(worldPivot + turn.Rotate(Pose.position - worldPivot), Normalize(turn * Pose.rotation));
            Yaw = AngleDelta(0, Yaw + yawDelta);
            Tilt = ComfortCameraOptions.Clamp(Tilt - pitchDelta, tiltMin, tiltMax);
        }
        // This is an explicit head-view mode, never a user-editable tabletop
        // zoom value. Ordinary Initialize/SetScale keep their closed 6-14 range.
        internal void InitializeFirstPerson(ViewPose pose)
        {
            Pose = pose; Scale = 1; Tilt = Yaw = 0; pivotReference = new Point3(0, 0, 1);
            Initialized = true; Cancel(true);
        }
        internal TouchTabletopSnapshot Capture() => new TouchTabletopSnapshot {
            Pose = Pose, Scale = Scale, Tilt = Tilt, Yaw = Yaw, PivotReference = pivotReference, ScaleMin = scaleMin, ScaleMax = scaleMax, TiltMin = tiltMin, TiltMax = tiltMax
        };
        internal void Restore(TouchTabletopSnapshot saved)
        {
            if (saved.ScaleMin > 0) ConfigureLimits(saved.ScaleMin, saved.ScaleMax, saved.TiltMin, saved.TiltMax);
            Pose = saved.Pose; Scale = saved.Scale; Tilt = saved.Tilt; Yaw = saved.Yaw;
            pivotReference = saved.PivotReference; Initialized = true; Cancel(true);
        }
        internal void Cancel(bool waitForRelease)
        {
            trackingPause = 0;
            requireRelease = waitForRelease; leftHeld = rightHeld = false;
            stickReady = !waitForRelease;
            Gesture = waitForRelease ? TouchTabletopGesture.WaitingForRelease : TouchTabletopGesture.Idle;
            Limited = false;
        }
        internal void SetScale(float scale)
        {
            float next = LimitScale(scale);
            if (!Initialized) { Scale = next; return; }
            Point3 pivot = MapPoint(pivotReference);
            Scale = next; Pose = new ViewPose(pivot - Pose.rotation.Rotate(pivotReference) * Scale, Pose.rotation);
            Cancel(true);
        }
        internal Point3 MapPoint(Point3 referencePoint) => Pose.position + Pose.rotation.Rotate(referencePoint) * Scale;
        internal Point3 MapDirection(Point3 referenceDirection) => Pose.rotation.Rotate(referenceDirection);
        internal void Tick(TouchTabletopInput input, float turnSpeed, float zoomSpeed, float moveSpeed = 1f, float gestureTurnSpeed = 1f)
        {
            Limited = false;
            if (!Initialized) return;
            if (!input.Allowed || !Finite(input.LeftSqueeze) || !Finite(input.RightSqueeze)) { Cancel(true); return; }
            if ((leftHeld && !input.LeftValid) || (rightHeld && !input.RightValid))
            {
                // A brief occlusion at arm's reach must not latch the gesture off.
                // Freeze and rebase on recovery; never integrate unseen hand motion.
                trackingPause += ComfortCameraOptions.Seconds(input.Seconds);
                if (trackingPause > TrackingPauseSeconds) Cancel(true);
                return;
            }
            if (trackingPause > 0) { trackingPause=0; previousLeft=input.Left; previousRight=input.Right; return; }
            if (requireRelease)
            {
                Gesture = TouchTabletopGesture.WaitingForRelease;
                if (input.LeftValid && input.RightValid && input.LeftSqueeze <= GripRelease && input.RightSqueeze <= GripRelease &&
                    !input.StickBlocked && StickNeutral(input))
                { requireRelease = false; stickReady = true; Gesture = TouchTabletopGesture.Idle; }
                return;
            }
            leftHeld = input.LeftValid && Finite(input.Left) && (leftHeld ? input.LeftSqueeze > GripRelease : input.LeftSqueeze >= GripPress);
            rightHeld = input.RightValid && Finite(input.Right) && (rightHeld ? input.RightSqueeze > GripRelease : input.RightSqueeze >= GripPress);
            if (leftHeld || rightHeld || input.StickBlocked) stickReady = false;
            var next = leftHeld && rightHeld ? TouchTabletopGesture.RotateAndScale : leftHeld ? TouchTabletopGesture.PanLeft :
                rightHeld ? TouchTabletopGesture.PanRight : TouchTabletopGesture.Idle;
            if (next != Gesture)
            {
                Gesture = next; previousLeft = input.Left; previousRight = input.Right;
                if (next != TouchTabletopGesture.Idle) return;
            }
            float seconds = ComfortCameraOptions.Seconds(input.Seconds);
            if (seconds <= 0) { previousLeft = input.Left; previousRight = input.Right; return; }
            turnSpeed = ComfortCameraOptions.Speed(turnSpeed); zoomSpeed = ComfortCameraOptions.Speed(zoomSpeed);
            moveSpeed = ComfortCameraOptions.Speed(moveSpeed);
            if (next == TouchTabletopGesture.PanLeft || next == TouchTabletopGesture.PanRight)
            {
                Point3 delta = next == TouchTabletopGesture.PanLeft ? input.Left - previousLeft : input.Right - previousRight;
                delta = LimitPan(delta * moveSpeed, seconds);
                Pose = new ViewPose(Pose.position - Pose.rotation.Rotate(delta) * Scale, Pose.rotation);
            }
            else if (next == TouchTabletopGesture.RotateAndScale)
            {
                Point3 oldSpan = previousRight - previousLeft, span = input.Right - input.Left;
                float oldLength = Length(oldSpan), length = Length(span);
                if (!Finite(oldLength) || !Finite(length) || oldLength < MinimumSpan || length < MinimumSpan) { Limited = true; }
                else
                {
                    float zoomResponse = zoomSpeed * TwoHandZoomGain;
                    float rawLog = (float)Math.Log(oldLength / length) * zoomResponse;
                    float log = Clip(rawLog, -MaxZoomLogPerSecond * zoomResponse * seconds, MaxZoomLogPerSecond * zoomResponse * seconds);
                    float wantedScale = Scale * (float)Math.Exp(log);
                    float newScale = LimitScale(wantedScale);
                    if (Math.Abs(wantedScale - newScale) > .00001f) Limited = true;
                    float yawDelta = 0;
                    Point3 up = input.PhysicalUpReference;
                    float upLength = Length(up);
                    up = Finite(upLength) && upLength > .0001f ? up * (1f / upLength) : new Point3(0, 1, 0);
                    Point3 flatOld = oldSpan - up * Dot(oldSpan, up), flatNew = span - up * Dot(span, up);
                    if (Length(flatOld) >= MinimumSpan * .5f && Length(flatNew) >= MinimumSpan * .5f)
                    {
                        float gestureResponse = turnSpeed * ComfortCameraOptions.Speed(gestureTurnSpeed) * TwoHandTurnReduction;
                        yawDelta = -(float)(Math.Atan2(Dot(up, Cross(flatOld, flatNew)), Dot(flatOld, flatNew)) * 180d / Math.PI) * gestureResponse * TwoHandTurnGain;
                        yawDelta = Clip(yawDelta, -MaxTurnDegreesPerSecond * gestureResponse * seconds, MaxTurnDegreesPerSecond * gestureResponse * seconds);
                    }
                    else Limited = true;
                    Rotation4 rotation = Normalize(AxisAngle(new Point3(0, 1, 0), yawDelta) * Pose.rotation);
                    Point3 oldMid = (previousLeft + previousRight) * .5f;
                    Point3 newMid = oldMid + LimitPan(((input.Left + input.Right) * .5f - oldMid) * moveSpeed, seconds);
                    Point3 fixedPoint = MapPoint(oldMid);
                    Pose = new ViewPose(fixedPoint - rotation.Rotate(newMid) * newScale, rotation);
                    Scale = newScale; Yaw = AngleDelta(0, Yaw + yawDelta);
                }
            }
            else
            {
                // A stick held while gripping, selecting or clicking a UI must
                // return to neutral before it can rotate or tilt the table.
                if (!stickReady)
                {
                    if (!input.StickBlocked && StickNeutral(input)) stickReady = true;
                    previousLeft = input.Left; previousRight = input.Right; return;
                }
                float axis = Finite(input.TiltAxis) ? input.TiltAxis : 0;
                float turn = Finite(input.TurnAxis) ? input.TurnAxis : 0;
                const float deadzone = .20f;
                if (Math.Abs(axis) > deadzone || Math.Abs(turn) > deadzone)
                {
                    axis = Math.Abs(axis) > deadzone ? Math.Sign(axis) * Math.Min(1, (Math.Abs(axis) - deadzone) / (1 - deadzone)) : 0;
                    turn = Math.Abs(turn) > deadzone ? Math.Sign(turn) * Math.Min(1, (Math.Abs(turn) - deadzone) / (1 - deadzone)) : 0;
                    float desired = Tilt + axis * TiltDegreesPerSecond * turnSpeed * seconds;
                    float newTilt = ComfortCameraOptions.Clamp(desired, tiltMin, tiltMax);
                    Point3 viewForward=Pose.rotation.Rotate(new Point3(0,0,1));
                    float currentPitch=(float)(Math.Asin(ComfortCameraOptions.Clamp(-viewForward.y,-1,1))*180/Math.PI);
                    float wantedPitch=currentPitch-(newTilt-Tilt);
                    newTilt=Tilt+currentPitch-ComfortCameraOptions.Clamp(wantedPitch,-80,88);
                    if (Math.Abs(desired - newTilt) > .00001f) Limited = true;
                    Point3 fixedPoint = MapPoint(pivotReference);
                    float yawDelta = turn * StickTurnDegreesPerSecond * turnSpeed * seconds;
                    Rotation4 rotation = Normalize(AxisAngle(new Point3(0, 1, 0), yawDelta) * Pose.rotation * AxisAngle(new Point3(1, 0, 0), -(newTilt - Tilt)));
                    Pose = new ViewPose(fixedPoint - rotation.Rotate(pivotReference) * Scale, rotation);
                    Tilt = newTilt; Yaw = AngleDelta(0, Yaw + yawDelta);
                    Gesture = turn != 0 ? TouchTabletopGesture.Turn : TouchTabletopGesture.Tilt;
                }
            }
            previousLeft = input.Left; previousRight = input.Right;
        }

        Point3 LimitPan(Point3 delta, float seconds)
        {
            float length = Length(delta), max = MaxPanMetersPerSecond * seconds;
            if (!Finite(length)) { Limited = true; return new Point3(); }
            if (length <= max || length < .000001f) return delta;
            Limited = true; return delta * (max / length);
        }
        float Clip(float value, float min, float max)
        {
            float limited = ComfortCameraOptions.Clamp(value, min, max);
            if (limited != value) Limited = true;
            return limited;
        }
        internal static bool Finite(float value) => ComfortCameraOptions.Finite(value);
        static bool StickNeutral(TouchTabletopInput input) => Finite(input.TiltAxis) && Finite(input.TurnAxis) &&
            Math.Abs(input.TiltAxis) <= .2f && Math.Abs(input.TurnAxis) <= .2f;
        internal static bool Finite(Point3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
        internal static float Length(Point3 p) => (float)Math.Sqrt((double)p.x * p.x + (double)p.y * p.y + (double)p.z * p.z);
        static float Dot(Point3 a, Point3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        static Point3 Cross(Point3 a, Point3 b) => new Point3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        internal static float AngleDelta(float from, float to)
        {
            float result = (float)(((double)to - from) % 360d);
            if (result > 180) result -= 360;
            if (result < -180) result += 360;
            return result;
        }
        internal static Rotation4 AxisAngle(Point3 axis, float degrees)
        {
            double half = degrees * Math.PI / 360d;
            float sine = (float)Math.Sin(half), cosine = (float)Math.Cos(half);
            return new Rotation4(axis.x * sine, axis.y * sine, axis.z * sine, cosine);
        }
        static Rotation4 Normalize(Rotation4 value)
        {
            double norm = Math.Sqrt((double)value.x * value.x + (double)value.y * value.y + (double)value.z * value.z + (double)value.w * value.w);
            if (norm < .000001 || double.IsNaN(norm) || double.IsInfinity(norm)) return Rotation4.Identity;
            return new Rotation4((float)(value.x / norm), (float)(value.y / norm), (float)(value.z / norm), (float)(value.w / norm));
        }
    }
}

