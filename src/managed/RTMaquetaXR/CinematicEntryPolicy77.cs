namespace RTMaquetaXR
{
    // The caller supplies the resolved scene mode, so Pause retains Dialog.
    internal sealed class CinematicEntryPolicy77
    {
        internal string Previous { get; private set; }
        internal bool Observe(bool wanted, string scene)
        {
            string next = wanted ? scene : null;
            bool entered = next == "Dialog" && Previous != "Dialog";
            Previous = next;
            return entered;
        }
        internal void Reset() { Previous = null; }
    }
}
