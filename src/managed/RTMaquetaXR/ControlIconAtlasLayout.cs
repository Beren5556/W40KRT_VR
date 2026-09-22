using System;
namespace RTMaquetaXR
{
    // Coordinates belong to the approved sheet, NOT a generated approximation
    // of its silhouettes. All 36 cells remain addressable, including variants.
    internal static class ControlIconAtlasLayout
    {
        internal static readonly string[] Symbols = {
            "L","L:PRESS","LT","LT:PRESS","REST","RT","RT:PRESS","R","R:PRESS",
            "L:XY","L:ROTATE","LG","LG:PRESS","REST:PRESS","RG:PRESS","RG","R:ROTATE","R:XY",
            "L:X","L:CW","X","X:PRESS","MENU","A:PRESS","A","R:CW","R:X",
            "L:Y","L:CCW","Y","Y:PRESS","MENU:PRESS","B:PRESS","B","R:CCW","R:Y" };
        static readonly float[] cx={139,314,489,664,837,1011,1186,1360,1535};
        static readonly float[] cy={216,391,563,735};
        static readonly float[] keyCenters={161.5f,472.5f,783f,1095f};
        internal static int Index(string symbol)
        {
            symbol=(symbol??"").ToUpperInvariant();
            for(int i=0;i<Symbols.Length;i++)if(Symbols[i]==symbol)return i;
            return -1;
        }
        internal static bool Keyboard(string symbol) => symbol!=null && symbol.Length==2 && symbol[0]=='F' && symbol[1]>='1' && symbol[1]<='4';
        internal static bool Known(string symbol) => Keyboard(symbol)||Index(symbol)>=0;
        internal static void Bounds(string symbol,out float x,out float y,out float w,out float h)
        {
            if(Keyboard(symbol))
            {
                // The F1-F4 artwork occupies only ~216 px of each 313.5 px cell.
                // Crop the visible key with a bounded gutter; do not scale its cell padding.
                x=(keyCenters[symbol[1]-'1']-112f)/1254f;y=1-(1077f+110f)/1254f;
                w=224f/1254f;h=220f/1254f;return;
            }
            int index=Index(symbol);if(index<0)throw new ArgumentException("Unknown control glyph: "+symbol);
            // Reference centres and crop bounds expressed in original pixels.
            // Wide grip silhouettes retain their aspect; directional arrows are
            // included rather than clipped to the circular button underneath.
            bool grip=symbol.StartsWith("LG")||symbol.StartsWith("RG");
            bool directional=symbol.Contains(":X")||symbol.Contains(":Y")||symbol.Contains("CW")||symbol.Contains("ROTATE");
            bool pressed=symbol.Contains(":PRESS")||symbol=="R";
            float width=grip?150:directional?156:pressed?120:108;
            float height=grip?112:directional?156:pressed?126:108;
            float centerY=cy[index/9]-(pressed?6:0);
            x=(cx[index%9]-width*.5f)/1675f;y=1-(centerY+height*.5f)/940f;
            w=width/1675f;h=height/940f;
        }
    }
}
