namespace RTMaquetaXR
{
    internal static class TouchRadialDisabledColor
    {
        // Disabled art is visibly subdued, not almost as white as live art.
        internal static byte Gray(byte r, byte g, byte b) => (byte)(76 + (72 * (2126*r + 7152*g + 722*b)) / 2550000);
    }
}
