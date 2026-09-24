using System;
namespace RTMaquetaXR
{
    // A native pooled text can acquire a new font size while a screen is open.
    // Preserve that write and never multiply our own previous result again.
    internal sealed class FontScaleLease81
    {
        float original, last; bool owned;
        internal float Apply(float current,float multiplier,bool integral=false)
        {
            if(!owned || Math.Abs(current-last)>.001f)original=current;
            last=integral?(float)Math.Round(original*multiplier):original*multiplier;
            owned=true;return last;
        }
        internal float Restore(float current) => owned && Math.Abs(current-last)<=.001f ? original : current;
    }
}
