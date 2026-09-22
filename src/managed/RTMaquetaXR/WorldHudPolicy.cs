namespace RTMaquetaXR
{
    internal static class WorldHudPolicy
    {
        // Exact installed Waaagh ranges. Other lists and other cameras keep
        // their original masks, material passes and queues.
        internal static int QueueSlot(int lower, int upper) =>
            lower == 3000 && upper == 3999 ? 0 : lower == 4000 && upper == 4999 ? 1 : -1;

        internal static bool ValidTarget(int eyeWidth, int eyeHeight, int width, int height,
            int samples, bool dynamicResolution, bool completeViewport) =>
            eyeWidth > 0 && eyeHeight > 0 && width == eyeWidth && height == eyeHeight &&
            samples == 1 && !dynamicResolution && completeViewport;

        // A private render layer is an implementation detail of our own capture,
        // not a structural edit by the game. Keep the ledger in the original
        // layer space but audit against the currently owned render layer. An
        // external write (including a write back to the original layer while
        // ownership is active) must still invalidate the cache.
        internal static int TrackedLayer(int actual, int original, int reserved, bool owned) =>
            owned && reserved >= 0 && actual == reserved ? original : actual;

        internal static int ExpectedLayer(int tracked, int reserved, bool owned) =>
            owned && reserved >= 0 ? reserved : tracked;

        // Installed Unity: Processing=-1, Empty=0, Populated=1. Pending batch
        // processing says nothing about visibility; only proven empty lists may
        // omit the late pass. No polling, waiting or speculative scene cull.
        internal static bool ValidListStatus(int value) => value >= -1 && value <= 1;
        internal static bool SkipEmptyLists(int transparent, int overlay) => transparent == 0 && overlay == 0;

        // The high-resolution route may legitimately receive empty lists when
        // the game has no visible world UI. It becomes anomalous only when a
        // protected native family is currently eligible in both-eye frames and
        // both private lists remain proven empty. Three consecutive complete
        // frames avoid reacting to a one-frame pool/window transition.
        internal const int EmptyRecoveryFrames = 3;
        internal static int NextEmptyRecoveryStreak(int previous, int previousFrame, int frame,
            bool protectedEligible, bool bothEyesReported, bool bothEyesEmpty)
        {
            if (!protectedEligible || !bothEyesReported || !bothEyesEmpty) return 0;
            return previousFrame + 1 == frame ? previous + 1 : 1;
        }
        internal static bool RestoreNativeRoute(int streak) => streak >= EmptyRecoveryFrames;

        // Family bits are deliberately based on the game's exact native type
        // names. They describe only the approved interaction, attack/unit and
        // bark surfaces; PointMarker Unit pointers remain a separate filter.
        internal const int InteractionFamily = 1, AttackFamily = 2, BarkFamily = 4;
        static readonly System.Collections.Generic.Dictionary<System.Type, int> Families78 = new System.Collections.Generic.Dictionary<System.Type, int>();
        internal static int ProtectedFamily(System.Type type)
        {
            if (type == null) return 0;
            if (Families78.TryGetValue(type, out int family)) return family;
            for (var current = type; current != null && family == 0; current = current.BaseType)
                family = ProtectedFamily(current.FullName);
            Families78[type] = family;
            return family;
        }
        internal static bool DestructibleCoverView78(System.Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
                if (current.Name == "OvertipDestructibleObjectView") return true;
            return false;
        }
        internal static int ProtectedFamily(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return 0;
            if (typeName.EndsWith("OvertipMapObjectInteractionPCView", System.StringComparison.Ordinal)) return InteractionFamily;
            if (typeName.EndsWith("OvertipUnitPCView", System.StringComparison.Ordinal) ||
                typeName.EndsWith("OvertipUnitView", System.StringComparison.Ordinal) ||
                typeName.EndsWith("LightweightUnitOvertipView", System.StringComparison.Ordinal) ||
                typeName.EndsWith("OvertipDestructibleObjectView", System.StringComparison.Ordinal)) return AttackFamily;
            if (typeName.EndsWith("OvertipBarkBlockView", System.StringComparison.Ordinal) ||
                typeName.EndsWith("UnitBarkPartView", System.StringComparison.Ordinal)) return BarkFamily;
            return 0;
        }
    }
}
