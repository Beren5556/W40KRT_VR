using System;

namespace RTMaquetaXR
{
    // Tangent-space bounds, independent of Unity so containment can be tested
    // without a game or headset. Crop coordinates are top-left-origin UVs.
    internal struct FrustumBounds
    {
        public float Left, Right, Bottom, Top, AreaFraction;
    }

    internal struct CullingProjectionXY
    {
        public float M00, M02, M11, M12;
    }

    internal static class VisibleFrustum
    {
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        // Change only the X/Y clip rows. The caller retains the original
        // projection's depth and W rows, near/far and coordinate convention.
        internal static bool TryProjection(FrustumBounds bounds, out CullingProjectionXY projection)
        {
            projection = default;
            if (!Finite(bounds.Left) || !Finite(bounds.Right) || !Finite(bounds.Bottom) || !Finite(bounds.Top) ||
                bounds.Right <= bounds.Left || bounds.Top <= bounds.Bottom) return false;
            double width = (double)bounds.Right - bounds.Left, height = (double)bounds.Top - bounds.Bottom;
            projection = new CullingProjectionXY {
                M00 = (float)(2 / width), M02 = (float)(((double)bounds.Right + bounds.Left) / width),
                M11 = (float)(2 / height), M12 = (float)(((double)bounds.Top + bounds.Bottom) / height)
            };
            return Finite(projection.M00) && Finite(projection.M02) && Finite(projection.M11) && Finite(projection.M12) &&
                projection.M00 > 0 && projection.M11 > 0;
        }

        internal static bool TryBounds(float halfX, float halfY,
            float cropLeft, float cropTop, float cropRight, float cropBottom,
            int width, int height, float renderScale, float guardPixels, out FrustumBounds bounds)
        {
            bounds = default;
            if (!Finite(halfX) || !Finite(halfY) || halfX <= 0 || halfY <= 0 ||
                !Finite(cropLeft) || !Finite(cropTop) || !Finite(cropRight) || !Finite(cropBottom) ||
                cropLeft < 0 || cropTop < 0 || cropRight > 1 || cropBottom > 1 ||
                cropLeft >= cropRight || cropTop >= cropBottom || width <= 0 || height <= 0 ||
                !Finite(renderScale) || renderScale <= 0 || !Finite(guardPixels) || guardPixels < 0) return false;

            // Use internal resolution: the same guard must also cover filters
            // when the game lowers RenderScale (FSR). Never shrink the XR crop.
            double padX = guardPixels / ((double)width * renderScale);
            double padY = guardPixels / ((double)height * renderScale);
            double l = Math.Max(0, cropLeft - padX), r = Math.Min(1, cropRight + padX);
            double t = Math.Max(0, cropTop - padY), b = Math.Min(1, cropBottom + padY);
            bounds = new FrustumBounds {
                Left = (float)((2 * l - 1) * halfX), Right = (float)((2 * r - 1) * halfX),
                Bottom = (float)((1 - 2 * b) * halfY), Top = (float)((1 - 2 * t) * halfY),
                AreaFraction = (float)((r - l) * (b - t))
            };
            return Finite(bounds.Left) && Finite(bounds.Right) && Finite(bounds.Bottom) && Finite(bounds.Top);
        }
    }
}
