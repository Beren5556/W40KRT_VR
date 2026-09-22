namespace RTMaquetaXR
{
    internal enum EyeAaMode { Taa = 0, Smaa = 1, Off = 2 }

    internal static class EyeAaOptions
    {
        internal static EyeAaMode Get(bool temporal, bool disabled) => disabled ? EyeAaMode.Off : temporal ? EyeAaMode.Taa : EyeAaMode.Smaa;
        internal static EyeAaMode Next(EyeAaMode mode) => mode == EyeAaMode.Taa ? EyeAaMode.Smaa : mode == EyeAaMode.Smaa ? EyeAaMode.Off : EyeAaMode.Taa;
        internal static void Apply(EyeAaMode mode, out bool temporal, out bool disabled)
        {
            disabled = mode == EyeAaMode.Off;
            temporal = mode != EyeAaMode.Smaa && !disabled;
        }
        internal static string Name(EyeAaMode mode) => mode == EyeAaMode.Off ? "Off" : mode == EyeAaMode.Smaa ? "SMAA High" : "TAA High";
    }
}
