using System;

namespace RTMaquetaXR
{
    internal static class FlatHandQualityPolicy
    {
        internal const int MinimumLongEdge = 2048, MaximumLongEdge = 2304, MaximumFallbackLongEdge = 4096;
        internal static void Size(int headsetWidth, int headsetHeight, out int width, out int height)
        {
            if (headsetWidth <= 0 || headsetHeight <= 0) headsetWidth = headsetHeight = MinimumLongEdge;
            double aspect = Math.Max(.5, Math.Min(2, (double)headsetWidth / headsetHeight));
            int edge = Math.Max(MinimumLongEdge, Math.Min(MaximumLongEdge, Math.Max(headsetWidth, headsetHeight)));
            width = Align((int)Math.Round(aspect >= 1 ? edge : edge * aspect));
            height = Align((int)Math.Round(aspect >= 1 ? edge / aspect : edge));
        }
        internal static int Samples(int supported) => supported >= 4 ? 4 : supported >= 2 ? 2 : 1;
        internal static void WorkSize(int width, int height, int samples, out int workWidth, out int workHeight)
        {
            double factor = samples > 1 ? 1 : Math.Min(2, (double)MaximumFallbackLongEdge / Math.Max(width, height));
            workWidth = Align((int)(width * factor)); workHeight = Align((int)(height * factor));
        }
        static int Align(int value) => Math.Max(8, (value + 7) / 8 * 8);
    }
}
