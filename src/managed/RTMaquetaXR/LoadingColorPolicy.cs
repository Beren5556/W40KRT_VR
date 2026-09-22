namespace RTMaquetaXR
{
    internal static class LoadingColorPolicy
    {
        // Exact shipped pixel shaders have different output transfer functions.
        // Never guess for an unfamiliar shader: the native loading fallback
        // remains available if the installed engine material contract changes.
        internal static bool TrySrgbWrite(string shader, bool linearProject, out bool write)
        {
            write = false;
            if (shader == "Owlcat/UI/Default") return true;
            if (shader == "UI/Default") { write = linearProject; return true; }
            return false;
        }
    }
}
