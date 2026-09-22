using System;
namespace RTMaquetaXR
{
    internal static class ControlIconSizing65
    {
        internal const int OverlayBody=40;
        internal static int Body(int font,int explicitSize) => explicitSize>0?explicitSize:Math.Max(28,font);
        // Includes directional arrows and grip outline, not just the central letter.
        internal static int Slot(int body) => (int)Math.Ceiling(body*1.85);
    }
}
