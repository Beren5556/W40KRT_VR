using UnityEngine;

namespace RTMaquetaXR
{
    internal static class NativeCardOwnership65
    {
        internal static bool Contains(Transform section, Transform ownedCard) =>
            section != null && ownedCard != null && (section == ownedCard || section.IsChildOf(ownedCard));
    }
}
