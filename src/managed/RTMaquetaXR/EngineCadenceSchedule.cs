using System;

namespace RTMaquetaXR
{
    internal enum EngineCadenceState { Off, WaitingForVr, WaitingForTiming, TimingMismatch, Suspended, Unavailable, Active }

    // One slot belongs to one native component, normally through a weak table.
    internal sealed class EngineCadenceSlot
    {
        internal int Phase { get; }
        internal EngineCadenceSlot(int phase = 0) { Phase = phase == 1 ? 1 : 0; }
        internal EngineCadenceSchedule Owner;
        internal long Epoch;
        internal int Frame;
        internal double NextDue;
        internal bool Initialized, RunThisFrame;
    }

    // A gate for individually reviewed visual methods. It never invokes a
    // method, changes game time, or schedules simulation/rendering phases.
    internal sealed class EngineCadenceSchedule
    {
        const int RequiredTimingSamples = 4;
        const double TimingTolerance = .025, TimingLifetime = .25;
        const double DueTolerance = .000001; // rounding tolerance, not a frame of slack
        readonly double interval;
        bool haveTiming, haveSerial, timingMismatch, haveFrame, haveFrameClock, haveContext;
        ulong lastSerial;
        int stableSamples, currentFrame;
        long contextRevision;
        double lastTimingAt, frameNow;

        internal bool Enabled { get; }
        internal int Mode { get; }
        internal int VrHz { get; }
        internal int VisualHz { get; }
        internal EngineCadenceState State { get; private set; }
        internal long Epoch { get; private set; }
        internal double ObservedHz { get; private set; }

        internal EngineCadenceSchedule(bool enabled, int mode)
        {
            Enabled = enabled; Mode = NormalizeMode(mode);
            VrHz = Mode == 1 ? 90 : Mode == 2 ? 120 : 72;
            VisualHz = VrHz / 2; interval = 1.0 / VisualHz;
            State = enabled ? EngineCadenceState.WaitingForVr : EngineCadenceState.Off;
            Epoch = 1;
        }

        internal static int NormalizeMode(int mode) { return mode >= 0 && mode <= 2 ? mode : 0; }
        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        internal void Observe(ulong serial, double periodMs, double now, bool valid)
        {
            if (!Enabled) return;
            if (!valid || !Finite(now) || !Finite(periodMs) || periodMs <= 0)
            {
                ClearTiming(); return;
            }
            if ((haveTiming && (now < lastTimingAt || now - lastTimingAt > TimingLifetime)) ||
                (haveFrameClock && now < frameNow) || (haveSerial && serial < lastSerial))
                ClearTiming();
            // A repeated native sample cannot establish cadence or extend its
            // freshness merely because Unity called the observer again.
            if (haveSerial && serial == lastSerial) return;
            double hz = 1000.0 / periodMs;
            if (!Finite(hz) || hz <= 0) { ClearTiming(); return; }
            haveTiming = haveSerial = true; lastSerial = serial; lastTimingAt = now;
            ObservedHz = hz;
            timingMismatch = Math.Abs(hz - VrHz) > VrHz * TimingTolerance;
            if (timingMismatch) stableSamples = 0;
            else if (stableSamples < RequiredTimingSamples) ++stableSamples;
        }

        internal void BeginFrame(int frame, double now, bool available, bool xrActive, bool eligibleContext, long contextRevision)
        {
            if (!Enabled) { State = EngineCadenceState.Off; return; }
            bool validClock = Finite(now);
            bool discontinuity = !validClock || (haveFrameClock && now < frameNow) || (haveFrame && frame < currentFrame);
            if (discontinuity) ClearTiming();
            if (!haveContext || this.contextRevision != contextRevision)
            {
                haveContext = true; this.contextRevision = contextRevision; InvalidateTargets();
            }
            currentFrame = frame; haveFrame = true;
            if (validClock) frameNow = now;
            haveFrameClock = validClock;
            if (haveTiming && validClock && (now < lastTimingAt || now - lastTimingAt > TimingLifetime)) ClearTiming();
            EngineCadenceState next;
            if (!available) next = EngineCadenceState.Unavailable;
            else if (!xrActive)
            {
                if (haveTiming || haveSerial) ClearTiming();
                next = EngineCadenceState.WaitingForVr;
            }
            else if (!eligibleContext) next = EngineCadenceState.Suspended;
            else if (discontinuity || !haveTiming) next = EngineCadenceState.WaitingForTiming;
            else if (timingMismatch) next = EngineCadenceState.TimingMismatch;
            else if (stableSamples < RequiredTimingSamples) next = EngineCadenceState.WaitingForTiming;
            else next = EngineCadenceState.Active;
            SetState(next);
        }

        internal bool ShouldRun(EngineCadenceSlot slot)
        {
            if (State != EngineCadenceState.Active || !haveFrame || !haveFrameClock || slot == null) return true;
            if (!slot.Initialized || !ReferenceEquals(slot.Owner, this) || slot.Epoch != Epoch)
            {
                slot.Owner = this; slot.Initialized = true; slot.Epoch = Epoch;
                slot.Frame = currentFrame; slot.RunThisFrame = true;
                // First/urgent work is always immediate. The following update
                // establishes one of two phases across successive VR frames.
                slot.NextDue = frameNow + interval * (slot.Phase == 1 ? .5 : 1);
                return true;
            }
            if (slot.Frame == currentFrame) return slot.RunThisFrame;
            slot.Frame = currentFrame;
            if (frameNow + DueTolerance < slot.NextDue) return slot.RunThisFrame = false;
            // Preserve the original phase under small jitter. A late caller
            // advances directly past missed deadlines, with no catch-up calls.
            double elapsedIntervals = Math.Floor((frameNow + DueTolerance - slot.NextDue) / interval) + 1;
            double due = slot.NextDue + elapsedIntervals * interval;
            slot.NextDue = Finite(due) && due > frameNow ? due : frameNow + interval;
            return slot.RunThisFrame = true;
        }

        internal void InvalidateTargets() { unchecked { ++Epoch; } }
        internal void ResetTracking()
        {
            ClearTiming(); haveFrame = haveFrameClock = false;
            SetState(Enabled ? EngineCadenceState.WaitingForTiming : EngineCadenceState.Off);
        }
        void ClearTiming()
        {
            haveTiming = haveSerial = timingMismatch = false;
            stableSamples = 0; lastSerial = 0; lastTimingAt = 0; ObservedHz = 0;
            InvalidateTargets();
            // ShouldRun may follow Observe before another BeginFrame.
            if (Enabled) SetState(EngineCadenceState.WaitingForTiming);
        }
        void SetState(EngineCadenceState state)
        {
            if (State == state) return;
            State = state; InvalidateTargets();
        }
    }
}
