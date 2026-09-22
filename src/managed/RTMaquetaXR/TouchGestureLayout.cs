namespace RTMaquetaXR
{
    // Tangents from the center view. The center is about 22 degrees to the
    // right and 16 degrees up; the art retains its previous 6.6-degree height.
    // Keeping position and size proportional to depth preserves this placement
    // at every table zoom and near/far-safe helper distance.
    internal static class TouchGestureLayout
    {
        internal const float Horizontal = .40f;
        internal const float Vertical = .28f;
        internal const float ScalePerDistance = .00095f;
    }
}
