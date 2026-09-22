using UnityEngine;

namespace RTMaquetaXR
{
    // Native cards occupy a fixed, nearly centred HUD surface. Neither controller
    // distance nor the user's main HUD dimensions changes their readable scale.
    internal static class SpatialInformationLayout
    {
        // Same comfortable depth range as the wheel (.38-.64m). Preserve the
        // approved angular size when bringing the card closer.
        internal const float Distance = .55f, FixedWidth = .61875f * Distance / 1.10f;
        internal static float Width(bool equipment) => FixedWidth * (equipment ? 1.25f : 1f);
        internal static Vector3 Place(Vector3 head, Quaternion rotation, out float width, bool equipment = false)
        {
            width = Width(equipment);
            // Screen-relative, like a native tutorial/dialog. Never positioned
            // at a pointer hit, a character, or an arbitrary world location.
            return head + rotation * new Vector3(equipment ? .10f : .06f * Distance / 1.10f, 0, Distance);
        }
        internal static Vector3 HintPosition(Vector2 cardSize)
        {
            float height = Mathf.Clamp(cardSize.y, 72, 950);
            return new Vector3(0, SpatialPresentationPolicy.InformationY - height * .5f - 38, -.10f);
        }
    }
}
