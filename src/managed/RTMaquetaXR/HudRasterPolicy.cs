using System;

namespace RTMaquetaXR
{
    // Real desktop render surface, not a bigger destination for a 1080p capture.
    // CanvasScaler's logical units are independent of this raster pixel count.
    internal static class HudRasterPolicy
    {
        internal const int DefaultMode = 3;
        internal static int Normalize(int mode) => Math.Max(0, Math.Min(3, mode));
        internal static int Width(int mode)
        {
            switch (Normalize(mode)) { case 1: return 2560; case 2: return 2880; case 3: return 3840; default: return 1920; }
        }
        internal static int Height(int mode) => Width(mode) * 9 / 16;
        internal static int Next(int mode, int direction) => (Normalize(mode) + (direction < 0 ? 3 : 1)) % 4;
    }
}
