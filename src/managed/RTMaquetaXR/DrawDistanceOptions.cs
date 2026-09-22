using System;
using System.Globalization;

namespace RTMaquetaXR
{
    // Pure decisions shared by the renderer and small regression tests.
    internal static class DrawDistanceOptions
    {
        internal static int Normalize(int mode) => mode >= 0 && mode <= 3 ? mode : 0;
        internal static int Next(int mode) => Normalize(mode) == 3 ? 0 : Normalize(mode) + 1;
        internal static float Cap(int mode, float custom = 100f) => mode == 1 ? 100f : mode == 2 ? 60f : mode == 3 ? SanitizeCustom(custom) : 0f;
        internal static string Name(int mode, float custom = 100f) => mode == 1 ? "100 units" : mode == 2 ? "60 units" :
            mode == 3 ? SanitizeCustom(custom).ToString("0.##", CultureInfo.InvariantCulture) + " units (custom)" : "Original";
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static float SanitizeCustom(float custom) => Finite(custom) ? Math.Max(20f, Math.Min(400f, custom)) : 100f;
        // An invalid or unrepresentably large near plane cannot form a useful finite projection.
        // Call this before assigning eye.nearClipPlane as well as when calculating its far plane.
        internal static float SanitizeNear(float near) => Finite(near) && near > 0 && near <= 1000000f ? near : 0.01f;
        internal static float FarClip(int mode, float near, float originalFar, float custom = 100f)
        {
            near = SanitizeNear(near);
            float minimumFar = near + Math.Max(0.01f, near * 0.0001f);
            float baseline = Finite(originalFar) && originalFar > near ? originalFar : Math.Max(1000f, minimumFar);
            float cap = Cap(Normalize(mode), custom);
            float candidate = cap > 0 ? Math.Min(baseline, cap) : baseline;
            // A cap behind the near plane cannot describe a valid view; retain the baseline.
            return candidate > near ? candidate : baseline;
        }
    }

    internal enum DrawDistanceTrialStep { None, NextPhase, Completed }

    // A panel click arms one run. Opening UI or leaving gameplay restarts the
    // settling delay, so no comparison starts underneath a menu.
    internal sealed class DrawDistanceStartGate
    {
        internal const double DelaySeconds = 5;
        internal bool Armed { get; private set; }
        internal double RemainingSeconds { get; private set; } = DelaySeconds;
        internal void Arm() { Armed = true; ResetDelay(); }
        internal void Cancel() { Armed = false; ResetDelay(); }
        internal void ResetDelay() { RemainingSeconds = DelaySeconds; }
        internal bool Tick(double seconds, bool ready, bool panelOpen)
        {
            if (!Armed) return false;
            if (!ready || panelOpen) { ResetDelay(); return false; }
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0 || seconds > .5)
            { ResetDelay(); return false; }
            RemainingSeconds -= seconds;
            if (RemainingSeconds > 1e-9) return false;
            Armed = false; RemainingSeconds = 0;
            return true;
        }
    }

    // Owns only temporary experiment state. It has no reference to persisted settings.
    internal sealed class DrawDistanceTrial
    {
        internal const double PhaseSeconds = 15;
        internal bool Running { get; private set; }
        internal int Phase { get; private set; } = -1;
        internal int PreviousMode { get; private set; }
        internal int Run { get; private set; }
        internal double UsefulSeconds { get; private set; }
        internal double TotalUsefulSeconds { get; private set; }
        internal double FinishedPhaseSeconds { get; private set; }
        internal int Mode => Phase == 1 ? 1 : Phase == 2 ? 2 : 0;

        internal bool Start(int previousMode)
        {
            if (Running) return false;
            PreviousMode = DrawDistanceOptions.Normalize(previousMode);
            Phase = 0; UsefulSeconds = TotalUsefulSeconds = FinishedPhaseSeconds = 0; Running = true; ++Run;
            return true;
        }
        internal DrawDistanceTrialStep Tick(double seconds, bool eligible)
        {
            // A long stall or time outside consecutive VR preparation is not useful comparison time.
            if (!Running || !eligible || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0 || seconds > 0.5)
                return DrawDistanceTrialStep.None;
            UsefulSeconds += seconds;
            TotalUsefulSeconds += seconds;
            if (UsefulSeconds + 1e-9 < PhaseSeconds) return DrawDistanceTrialStep.None;
            FinishedPhaseSeconds = UsefulSeconds; UsefulSeconds = 0;
            if (Phase < 3) { ++Phase; return DrawDistanceTrialStep.NextPhase; }
            Running = false; Phase = -1; return DrawDistanceTrialStep.Completed;
        }
        internal int Cancel()
        {
            Running = false; Phase = -1; UsefulSeconds = 0;
            return PreviousMode;
        }
    }
}
