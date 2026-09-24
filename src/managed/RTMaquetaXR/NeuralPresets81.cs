using System;
namespace RTMaquetaXR
{
    internal static class NeuralPresets81
    {
        internal static readonly int[] Values = { 0, 10, 11, 12, 13 };
        internal static bool Valid(int value) => value == 0 || (value >= 10 && value <= 13);
        internal static int Normalize(int value) => Valid(value) ? value : 0;
        internal static int Legacy(int value) => value == 1 ? 11 : 0;
        internal static string Name(int value) => value == 0 ? "Auto" : Valid(value) ? ((char)('A' + value - 1)).ToString() : "?";
        internal static int Index(int value) => Math.Max(0, Array.IndexOf(Values, value));
        internal static int Cycle(int value, int direction) => Values[(Index(value) + Math.Sign(direction) + Values.Length) % Values.Length];
        internal static string Label(int value) => Name(value);
        internal static string[] Labels() { var labels = new string[Values.Length]; for (int i = 0; i < labels.Length; ++i) labels[i] = Label(Values[i]); return labels; }
    }
}
