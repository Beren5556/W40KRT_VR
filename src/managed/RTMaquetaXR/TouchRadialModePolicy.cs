namespace RTMaquetaXR
{
    internal enum TouchRadialLeftMode { Actions, Characters }
    internal static class TouchRadialModePolicy
    {
        // Battle actions come from the native turn actor, never exploration
        // selection. Outside combat only a single selection opens actions.
        internal static TouchRadialLeftMode Initial(bool combat, int selectedCount) =>
            combat || selectedCount == 1 ? TouchRadialLeftMode.Actions : TouchRadialLeftMode.Characters;
        internal static TouchRadialLeftMode InitialForPhase(bool combat, bool space, bool preparing, int selectedCount) =>
            combat && !space && preparing ? TouchRadialLeftMode.Characters : Initial(combat || space, selectedCount);
        internal static TouchRadialLeftMode Other(TouchRadialLeftMode mode) =>
            mode == TouchRadialLeftMode.Actions ? TouchRadialLeftMode.Characters : TouchRadialLeftMode.Actions;
        internal static bool IsCharacters(TouchRadialLeftMode mode) => mode == TouchRadialLeftMode.Characters;
        internal static bool RequiresSingleSelection(int side, bool combat) => side == 0 && !combat;
    }
}
