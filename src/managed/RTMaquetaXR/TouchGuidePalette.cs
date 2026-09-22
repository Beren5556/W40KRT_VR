using UnityEngine;

namespace RTMaquetaXR
{
    // Opaque reading surfaces prevent the native monitor grain/world from
    // altering contrast. Exact black/white are invariant through the native
    // UI shader's sRGB conversion; brass and cyan carry hierarchy, not opacity.
    internal static class TouchGuidePalette
    {
        internal static readonly Color Surface = Color.black;
        internal static readonly Color Ink = Color.white;
        internal static readonly Color Binding = new Color(.94f,.79f,.43f,1);
        internal static readonly Color Secondary = new Color(.88f,.91f,.88f,1);
        internal static readonly Color Rule = new Color(.52f,.43f,.25f,1);
        internal static readonly Color Grid = new Color(.58f,.68f,.63f,1);
        internal static readonly Color Action = new Color(.32f,.94f,1,1);
    }
}
