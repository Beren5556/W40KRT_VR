using System;

namespace RTMaquetaXR
{
    // A fresh, continuous hold on the same available end-turn sector.
    // Each input has its own clock; owner release/tracking gaps cancel it.
    internal sealed class TouchRadialHoldPolicy
    {
        internal const double Seconds=1;
        internal double Progress {get;private set;}
        internal int Target => started >= 0 ? target : -1;
        bool wasHeld=true;
        int target=-1;
        double started=-1,last=double.NaN;
        internal bool Step(int candidate,bool held,bool valid,double now)
        {
            bool pressed=held&&!wasHeld;wasHeld=held;
            bool continuous=!double.IsNaN(now)&&!double.IsInfinity(now)&&now>=0&&
                (double.IsNaN(last)||now>=last&&now-last<=.3);
            last=now;
            if(!held||!valid||candidate<0||!continuous){started=-1;target=-1;Progress=0;return false;}
            if(pressed){target=candidate;started=now;Progress=0;return false;}
            if(started<0||target!=candidate){started=-1;target=-1;Progress=0;return false;}
            Progress=Math.Max(0,Math.Min(1,(now-started)/Seconds));
            if(Progress<1)return false;
            started=-1;target=-1;return true;
        }
        internal void Reset(){wasHeld=true;target=-1;started=-1;last=double.NaN;Progress=0;}
    }
    internal enum TouchRadialEvent { None, Open, Execute, Cancel, Switch }
    // No Unity state: input arbitration and the exact sector shown to the user
    // share this policy. Owner release always cancels. Menus use dwell or a
    // fresh opposite trigger; abilities use A/trigger, actors only inspect.
    internal sealed class TouchRadialPolicy
    {
        internal const double HoldSeconds = .15;
        internal const double DwellSeconds = 1;
        internal const int RingCapacity = 12;
        internal const float Deadzone = .25f;
        internal const float ReleaseDeadzone = .18f;
        internal const float RingBoundary = .72f, OuterEnter = .82f, InnerEnter = .62f;
        internal const double SectorHysteresis = .12;
        internal int Side { get; private set; } = -1;
        internal int Selected { get; private set; } = -1;
        internal int Hovered { get; private set; } = -1;
        internal bool PointerCommitted { get; private set; }
        internal bool LongPressCommitted { get; private set; }
        readonly TouchRadialHoldPolicy holdAction = new TouchRadialHoldPolicy();
        readonly TouchRadialHoldPolicy holdTrigger = new TouchRadialHoldPolicy();
        readonly TouchRadialHoldPolicy holdStickAction = new TouchRadialHoldPolicy();
        double EndTurnProgress => Math.Max(holdAction.Progress, Math.Max(holdTrigger.Progress, holdStickAction.Progress));
        bool holdStartedWithStick;
        // Once a fresh A/trigger starts confirmation from the owner stick,
        // relaxing that stick must not hand the action to the opposite ray.
        // Choosing another sector still cancels; resting the stick alone never
        // starts or completes a turn confirmation.
        internal int HeldStickConfirmation => holdStartedWithStick ? Math.Max(holdAction.Target, holdTrigger.Target) : -1;
        internal bool Visible { get; private set; }
        internal bool Captured { get; private set; }
        internal double Progress { get; private set; }
        internal double DwellProgress { get; private set; }
        internal int DwellIndex { get; private set; } = -1;
        internal bool StickWaitingNeutral { get; private set; }
        internal bool CatalogueWaitingNeutral { get; private set; }
        internal bool PointerWaitingExit { get; private set; }
        internal int InspectIndex { get; private set; } = -1;
        internal int PreviewIndex { get; private set; } = -1;
        bool armed, waiting, oppositeWasDown, oppositeArmed, switchWasDown, confirmWasDown;
        int previousInspectStick = -1;
        double started, dwellStarted, last = double.NaN;
        int dwellCount;
        internal TouchRadialEvent Step(bool lt, bool lg, bool rt, bool rg, bool valid, bool blocked,
            double now, float lx, float ly, float rx, float ry, int count, int pointerIndex = -1, bool leftStickClick = false,
            bool stickAvailable = true, bool pointerAvailable = true, bool informationOnly = false,
            bool explicitAction = false, bool confirmA = false, int navigationStart = -1, bool pointerObserved = true, int innerCount = -1, int holdActionIndex = -1)
        {
            LongPressCommitted = false;
            PreviewIndex = -1;
            bool confirmPressed = confirmA && !confirmWasDown; confirmWasDown = confirmA;
            bool switchPressed = leftStickClick && !switchWasDown; switchWasDown = leftStickClick;
            bool neutral = !lt && !lg && !rt && !rg;
            bool continuous = Finite(now) && (!Finite(last) || now >= last && now - last <= .3);
            bool ownerStillHeld = Side == 0 ? lt && lg : Side == 1 && rt && rg;
            // First-time native UI creation can stall a frame. Keep the wheel
            // while valid controls remain held, but never count the missing
            // time towards executing an action or ending a turn.
            if (!continuous && Finite(now) && Finite(last) && now >= last && valid && Visible &&
                ownerStillHeld && !blocked && !(lt && lg && rt && rg))
            {
                last=now; ResetHoldConfirmation(); ResetDwell();
                oppositeWasDown=Side==0?rt:lt;
                return TouchRadialEvent.None;
            }
            last = now;
            if (!valid || !continuous || (lt && lg && rt && rg))
            {
                bool existed = Visible || Side >= 0; Cancel();
                // A modal opened by our native action must not trap ordinary
                // UI input after the user has released the opening controls.
                if (neutral && valid && continuous) { armed = true; waiting = false; Captured = false; }
                return existed ? TouchRadialEvent.Cancel : TouchRadialEvent.None;
            }
            if (blocked)
            {
                // Higher-priority UI owns its ordinary trigger/grip presses.
                // Preserve a real prior wheel's release guard, but never make
                // a new capture simply because this input domain is blocked.
                bool existed = Visible || Side >= 0;
                if (existed || Captured) Cancel();
                else { armed = false; waiting = false; }
                if (neutral) { armed = true; waiting = false; Captured = false; }
                return existed ? TouchRadialEvent.Cancel : TouchRadialEvent.None;
            }
            if (!armed || waiting)
            {
                Captured = !neutral; if (neutral) { armed = true; waiting = false; Captured = false; Side = Selected = -1; }
                return TouchRadialEvent.None;
            }
            if (Side < 0)
            {
                if (!(lt && lg) && !(rt && rg)) { Captured = false; return TouchRadialEvent.None; }
                Side = lt && lg ? 0 : 1; started = now; Captured = true; Selected = -1;
            }
            bool held = Side == 0 ? lt && lg : rt && rg;
            bool opposite = Side == 0 ? rt : lt;
            float x = Side == 0 ? lx : rx, y = Side == 0 ? ly : ry;
            if (!held)
            {
                ResetHoldConfirmation();
                Visible = false; Progress = 0; waiting = true; Captured = true;
                ResetDwell(); Selected = Hovered = InspectIndex = PreviewIndex = -1;
                return TouchRadialEvent.Cancel;
            }
            if (Visible && Side == 0 && held && switchPressed)
            {
                ResetHoldConfirmation();
                Selected = Hovered = -1; PointerCommitted = false; CatalogueWaitingNeutral = true;
                PointerWaitingExit = true;
                InspectIndex = PreviewIndex = previousInspectStick = -1;
                ResetDwell(); StickWaitingNeutral = true;
                oppositeArmed = !opposite; oppositeWasDown = opposite;
                return TouchRadialEvent.Switch;
            }
            if (PointerWaitingExit && pointerObserved && pointerIndex < 0) PointerWaitingExit = false;
            if (CatalogueWaitingNeutral)
            {
                Selected = Hovered = -1; oppositeArmed = !opposite; oppositeWasDown = opposite;
                if (Centered(x, y)) CatalogueWaitingNeutral = StickWaitingNeutral = false;
                return TouchRadialEvent.None;
            }
            Hovered = Visible && !PointerWaitingExit && pointerIndex >= 0 && pointerIndex < count ? pointerIndex : -1;
            PreviewIndex = Hovered;
            bool pointerPress = Visible && oppositeArmed && opposite && !oppositeWasDown;
            if (!opposite) oppositeArmed = true;
            oppositeWasDown = opposite;
            int holdStick = StickWaitingNeutral ? -1 : PickStable(x,y,count,Selected,innerCount);
            // A deliberately deflected owner stick owns confirmation. The
            // opposite hand may be aimed elsewhere while pressing its trigger.
            bool retainedStickHold = HeldStickConfirmation >= 0 &&
                (holdStick < 0 || holdStick == HeldStickConfirmation);
            int holdCandidate = retainedStickHold ? HeldStickConfirmation : holdStick >= 0 ? holdStick : Hovered;
            bool holdFromStick = retainedStickHold || holdStick >= 0;
            bool holdAvailable = holdFromStick ? stickAvailable : pointerAvailable;
            bool wasHolding = holdAction.Target >= 0 || holdTrigger.Target >= 0;
            bool confirmationValid = Visible && holdAvailable && holdActionIndex >= 0;
            int confirmationTarget = holdCandidate == holdActionIndex ? holdCandidate : -1;
            // Never combine time held on A, trigger and stick. Stick-only ends
            // immediately on centring/leaving the icon; explicit confirmation
            // may retain the option previously selected with the owner stick.
            bool aDone = holdAction.Step(confirmationTarget, confirmA, confirmationValid, now);
            bool triggerDone = holdTrigger.Step(confirmationTarget, opposite, confirmationValid, now);
            bool stickDone = holdStickAction.Step(holdStick == holdActionIndex ? holdStick : -1,
                holdStick >= 0 && holdStick == holdActionIndex,
                Visible && stickAvailable && holdActionIndex >= 0, now);
            if (aDone || triggerDone || stickDone)
            { Selected=holdCandidate;PointerCommitted=!holdFromStick;LongPressCommitted=true;return Commit(); }
            if (!wasHolding && (holdAction.Target >= 0 || holdTrigger.Target >= 0)) holdStartedWithStick = holdFromStick;
            if (holdAction.Target < 0 && holdTrigger.Target < 0) holdStartedWithStick = false;
            bool pointerNavigation = navigationStart >= 0 && Hovered >= navigationStart;
            if (pointerPress && informationOnly && Hovered >= 0 && !pointerNavigation) InspectIndex = Hovered;
            if (pointerPress && (holdActionIndex < 0 || holdCandidate != holdActionIndex) && (!informationOnly || pointerNavigation))
            {
                // A pointer click wins even on the exact dwell-completion
                // frame. A miss/disabled target cancels the pending dwell too.
                ResetDwell(); StickWaitingNeutral = true;
                if (Hovered < 0 || !pointerAvailable) return TouchRadialEvent.None;
                Selected = Hovered; PointerCommitted = true;
                return Commit();
            }
            Progress = Math.Max(0, Math.Min(1, (now - started) / HoldSeconds));
            if (!Visible && Progress >= 1)
            {
                Visible = true; PointerCommitted = false; oppositeArmed = !opposite; oppositeWasDown = opposite;
                StickWaitingNeutral = !Centered(x, y); ResetDwell();
                return TouchRadialEvent.Open;
            }
            if (Visible)
            {
                if (StickWaitingNeutral)
                { Selected = -1; if (Centered(x, y)) StickWaitingNeutral = false; ResetDwell(); return TouchRadialEvent.None; }
                Selected = HeldStickConfirmation >= 0 ? HeldStickConfirmation : PickStable(x, y, count, Selected, innerCount);
                PreviewIndex = Hovered >= 0 ? Hovered : Selected;
                if (Selected >= 0 && Selected == holdActionIndex) PreviewIndex = Selected;
                if (holdActionIndex >= 0 && PreviewIndex == holdActionIndex)
                { ResetDwell(); DwellIndex=holdActionIndex; DwellProgress=EndTurnProgress; return TouchRadialEvent.None; }
                // Stick selection is immediate; pointing requires a fresh
                // opposite trigger. Returning to centre keeps the last actor.
                // A still-deflected stick must not overwrite a pointer click.
                bool navigation = navigationStart >= 0 && PreviewIndex >= navigationStart;
                if (informationOnly && !navigation)
                {
                    if (pointerPress && Hovered >= 0) InspectIndex = Hovered;
                    else if (Selected >= 0 && (navigationStart < 0 || Selected < navigationStart) && Selected != previousInspectStick) InspectIndex = Selected;
                    previousInspectStick = Selected;
                    ResetDwell(); return TouchRadialEvent.None;
                }
                if (explicitAction || navigation)
                {
                    ResetDwell();
                    bool available = PreviewIndex == Hovered && Hovered >= 0 ? pointerAvailable : stickAvailable;
                    if (confirmPressed && PreviewIndex >= 0 && available)
                    { Selected = PreviewIndex; PointerCommitted = Hovered >= 0; return Commit(); }
                    return TouchRadialEvent.None;
                }
                if (Selected < 0 || !stickAvailable) { ResetDwell(); return TouchRadialEvent.None; }
                if (DwellIndex != Selected || dwellCount != count)
                { DwellIndex = Selected; dwellCount = count; dwellStarted = now; DwellProgress = 0; }
                else
                {
                    DwellProgress = Math.Max(0, Math.Min(1, (now - dwellStarted) / DwellSeconds));
                    if (DwellProgress >= 1) { PointerCommitted = false; return Commit(); }
                }
            }
            return TouchRadialEvent.None;
        }
        TouchRadialEvent Commit()
        { Visible = false; Progress = 0; waiting = true; Captured = true; ResetDwell(); return TouchRadialEvent.Execute; }
        // Native weapon variants replace a catalogue while its opening chord
        // is still held. A held stick/A/trigger cannot commit the new entries.
        internal void ContinueCatalogue()
        {
            ResetHoldConfirmation();
            if (Side < 0) return;
            armed = Visible = Captured = true; waiting = false;
            CatalogueWaitingNeutral = StickWaitingNeutral = true;
            PointerWaitingExit = true;
            oppositeArmed = false; oppositeWasDown = true;
            Selected = Hovered = InspectIndex = PreviewIndex = previousInspectStick = -1;
            PointerCommitted = false; Progress = 1; ResetDwell();
        }
        void ResetDwell() { DwellIndex = -1; dwellCount = 0; DwellProgress = 0; dwellStarted = 0; }
        static bool Centered(float x, float y) => Finite(x) && Finite(y) && x * x + y * y <= ReleaseDeadzone * ReleaseDeadzone;
        internal void ResetHoldConfirmation() { holdAction.Reset();holdTrigger.Reset();holdStickAction.Reset();holdStartedWithStick=false;LongPressCommitted=false; }
        internal void Cancel()
        { ResetHoldConfirmation(); armed = false; waiting = true; Captured = true; Visible = false; oppositeArmed = oppositeWasDown = PointerCommitted = PointerWaitingExit = CatalogueWaitingNeutral = StickWaitingNeutral = false; Side = Selected = Hovered = InspectIndex = PreviewIndex = previousInspectStick = -1; Progress = 0; ResetDwell(); }
        internal void Reset()
        { ResetHoldConfirmation(); armed = waiting = Captured = Visible = oppositeArmed = oppositeWasDown = PointerCommitted = PointerWaitingExit = CatalogueWaitingNeutral = switchWasDown = StickWaitingNeutral = confirmWasDown = false; Side = Selected = Hovered = InspectIndex = PreviewIndex = previousInspectStick = -1; Progress = 0; last = double.NaN; ResetDwell(); }
        internal static int Split(int count,int inner=-1) => count<=0?0:inner>0&&inner<count?inner:count<=RingCapacity?count:(count+1)/2;
        internal static int Rings(int count,int inner=-1) => count<=0?0:Split(count,inner)<count?2:1;
        internal static int RingFor(int index,int count,int inner=-1) => index<Split(count,inner)?0:1;
        internal static int RingCount(int count,int ring,int inner=-1) => ring==0?Split(count,inner):ring==1?count-Split(count,inner):0;
        static double[] sectorAngles, sectorHalves;
        static int sectorSplit;
        internal static void SetSectorSides(int[] sides, int inner=-1)
        {
            sectorAngles=sectorHalves=null;
            if(sides==null||sides.Length==0)return;
            int count=sides.Length; sectorSplit=Split(count,inner);
            sectorAngles=new double[count];sectorHalves=new double[count];
            for(int ring=0;ring<Rings(count,inner);ring++)
            {
                int first=ring==0?0:sectorSplit, end=first+RingCount(count,ring,inner);
                int left=0,right=0,centre=0;
                for(int i=first;i<end;i++){if(sides[i]<0)left++;else if(sides[i]>0)right++;else centre++;}
                if(left==0&&right==0)
                {for(int i=first;i<end;i++){sectorAngles[i]=(i-first)*Math.PI*2/(end-first);sectorHalves[i]=Math.PI/(end-first);}continue;}
                double centreWidth=centre==0?0:Math.PI*5/9; // Native central mounts stay around twelve o'clock.
                double sideWidth=(Math.PI*2-centreWidth)/2;
                int l=0,r=0,c=0;
                for(int i=first;i<end;i++)
                {
                    int n;double start,width;
                    if(sides[i]<0){n=l++;start=Math.PI;width=sideWidth/left;}
                    else if(sides[i]>0){n=r++;start=centreWidth/2;width=sideWidth/right;}
                    else {n=c++;start=-centreWidth/2;width=centreWidth/centre;}
                    sectorAngles[i]=start+(n+.5)*width;sectorHalves[i]=width/2;
                }
            }
        }
        static bool CustomSectors(int count,int inner) => sectorAngles!=null&&sectorAngles.Length==count&&sectorSplit==Split(count,inner);
        internal static double HalfAngle(int index,int count,int inner=-1) => CustomSectors(count,inner)?sectorHalves[index]:Math.PI/Math.Max(1,RingCount(count,RingFor(index,count,inner),inner));
        internal static int PickStable(float x, float y, int count, int previous, int inner=-1)
        {
            if (!Finite(x) || !Finite(y) || count <= 0) return -1;
            double magnitude = Math.Sqrt(x*x+y*y);
            if (magnitude <= ReleaseDeadzone) return -1;
            if (previous < 0 || previous >= count) return Pick(x,y,count,inner);
            int ring=RingFor(previous,count,inner);
            if(Rings(count,inner)>1 && (ring==0&&magnitude>=OuterEnter || ring==1&&magnitude<=InnerEnter))
                return PickRing(x,y,count,ring==0?1:0,inner);
            double angle = Math.Atan2(x,y), delta = angle - Angle(previous,count,inner);
            delta = Math.Atan2(Math.Sin(delta),Math.Cos(delta));
            if (Math.Abs(delta) <= HalfAngle(previous,count,inner) * (1 + SectorHysteresis)) return previous;
            return magnitude > Deadzone ? PickRing(x,y,count,ring,inner) : -1;
        }
        internal static int Pick(float x, float y, int count,int inner=-1)
        {
            if (!Finite(x) || !Finite(y) || count <= 0) return -1;
            double magnitude = Math.Sqrt(x * x + y * y);
            if (magnitude <= Deadzone) return -1;
            return PickRing(x,y,count,Rings(count,inner)>1&&magnitude>=RingBoundary?1:0,inner);
        }
        internal static int PickRing(float x,float y,int count,int ring,int inner=-1)
        {
            int slots=RingCount(count,ring,inner);if(slots<=0)return -1;
            double angle = Math.Atan2(x, y); if (angle < 0) angle += Math.PI * 2;
            if(CustomSectors(count,inner))
            {
                int first=ring==0?0:Split(count,inner);
                for(int i=first;i<first+slots;i++)
                {double d=angle-sectorAngles[i];d=Math.Atan2(Math.Sin(d),Math.Cos(d));if(Math.Abs(d)<=sectorHalves[i]+1e-7)return i;}
                return -1; // No option in a side without a native weapon.
            }
            return (ring==0?0:Split(count,inner))+(int)Math.Floor(angle / (Math.PI * 2) * slots + .5) % slots;
        }
        internal static double Angle(int index, int count,int inner=-1) => CustomSectors(count,inner)?sectorAngles[index]:
            (index-(RingFor(index,count,inner)==0?0:Split(count,inner))) * Math.PI * 2 / Math.Max(1,RingCount(count,RingFor(index,count,inner),inner));
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
