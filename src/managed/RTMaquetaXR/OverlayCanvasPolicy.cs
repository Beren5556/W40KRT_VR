namespace RTMaquetaXR
{
    internal static class OverlayCanvasPolicy
    {
        // A camera excluding the private HUD layer cannot display the overlay.
        // Retain its prepared canvas batches rather than toggling them off and
        // on between scene and UI cameras. Unknown/nonisolated paths stay gated.
        internal static bool Retain(bool active, bool allowedCamera, bool isolated, int layer, int cameraMask)
        {
            return active && (allowedCamera || (isolated && layer >= 0 && layer < 32 &&
                (cameraMask & (1 << layer)) == 0));
        }
    }
}
