using System;

namespace RTMaquetaXR
{
    internal static class LoadingQualityPolicy
    {
        internal const int MaximumDimension = 8192;
        internal const long MaximumPixelsPerEye = 32L * 1024 * 1024;
        internal static bool TrySize(int headsetWidth, int headsetHeight, int deviceMaximum, out int width, out int height)
        {
            width = height = 0;
            if (headsetWidth < 64 || headsetHeight < 64 || deviceMaximum < 64) return false;
            int limit = Math.Min(MaximumDimension, deviceMaximum);
            double factor = Math.Min(1, (double)limit / Math.Max(headsetWidth, headsetHeight));
            factor = Math.Min(factor, Math.Sqrt((double)MaximumPixelsPerEye / ((long)headsetWidth * headsetHeight)));
            width = Math.Max(1, (int)Math.Floor(headsetWidth * factor));
            height = Math.Max(1, (int)Math.Floor(headsetHeight * factor));
            return true;
        }
        internal static int TextRasterScale(int height) => Math.Max(2, Math.Min(4, (int)Math.Ceiling(Math.Max(0, height) / 1440.0)));
    }

    // Owns a complete pair. The caller changes dimensions only after the native
    // swapchain resize has drained prior copies; the pair is allocated before
    // publication so one allocation failure cannot mix eye sizes.
    internal sealed class LoadingTargetPair<T> where T : class
    {
        readonly Func<int, int, T> createLeft, createRight;
        readonly Action<T> release;
        internal T Left { get; private set; }
        internal T Right { get; private set; }
        internal int Width { get; private set; }
        internal int Height { get; private set; }
        internal LoadingTargetPair(Func<int,int,T> createLeft, Func<int,int,T> createRight, Action<T> release)
        { this.createLeft = createLeft; this.createRight = createRight; this.release = release; }
        internal bool Ensure(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException("width/height");
            if (Left != null && Right != null && Width == width && Height == height) return false;
            T nextLeft = null, nextRight = null;
            try
            {
                nextLeft = createLeft(width, height);
                nextRight = createRight(width, height);
                if (nextLeft == null || nextRight == null) throw new InvalidOperationException("Incomplete loading target pair");
            }
            catch { Retire(nextLeft, nextRight); throw; }
            T oldLeft = Left, oldRight = Right;
            Left = nextLeft; Right = nextRight; Width = width; Height = height;
            Retire(oldLeft, oldRight);
            return true;
        }
        internal void Clear()
        {
            T oldLeft = Left, oldRight = Right;
            Left = Right = null; Width = Height = 0;
            Retire(oldLeft, oldRight);
        }
        void Retire(T left, T right)
        { try { if (left != null) release(left); } finally { if (right != null) release(right); } }
    }
}
