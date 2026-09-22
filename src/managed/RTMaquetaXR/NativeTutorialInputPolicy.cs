namespace RTMaquetaXR
{
    internal static class NativeTutorialInputPolicy
    {
        internal static bool BlocksInput(bool modal, bool hint) => modal;
        internal static bool StartBattleAvailable(bool active, bool preparation, bool tutorialModal,
            bool boundSameModel, bool nativeCanStart, bool buttonActive, bool buttonInteractable) =>
            active && preparation && !tutorialModal && boundSameModel && nativeCanStart && buttonActive && buttonInteractable;
    }
}
