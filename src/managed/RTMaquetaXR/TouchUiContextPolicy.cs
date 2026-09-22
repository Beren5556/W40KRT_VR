namespace RTMaquetaXR
{
    // Native view-model identity changes even when a pooled window keeps the
    // same GameObject. A press never migrates into that new window or modal.
    internal struct TouchUiContextIdentity
    {
        internal object EventSystem, Module, Root, Window, Model, Modal, TutorialWindow;
        internal bool Flat, Tutorial;
        internal int Width, Height;
        internal long Area;
        internal bool Same(TouchUiContextIdentity other) =>
            ReferenceEquals(EventSystem, other.EventSystem) && ReferenceEquals(Module, other.Module) &&
            ReferenceEquals(Root, other.Root) && ReferenceEquals(Window, other.Window) &&
            ReferenceEquals(Model, other.Model) && ReferenceEquals(Modal, other.Modal) &&
            ReferenceEquals(TutorialWindow, other.TutorialWindow) &&
            Flat == other.Flat && Tutorial == other.Tutorial && Width == other.Width && Height == other.Height && Area == other.Area;
    }

    internal sealed class TouchUiContextGuard
    {
        bool known;
        TouchUiContextIdentity previous;
        internal bool Observe(TouchUiContextIdentity current)
        {
            bool changed = known && !previous.Same(current);
            previous = current; known = true; return changed;
        }
        internal void Reset() { known = false; previous = default(TouchUiContextIdentity); }
    }
}
