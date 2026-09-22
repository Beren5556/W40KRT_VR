namespace RTMaquetaXR
{
    // One geometry source for the headset canvas, flat screens and previews.
    internal static class ContextControlGlyph
    {
        static readonly float[][] Empty = new float[0][];
        static readonly float[][] Grip = {
            new float[]{5,25,19,27,22,21,18,5,11,3,7,8,5,25},
            new float[]{18,20,14,19,13,12,17,12}
        };
        static readonly float[][] Trigger = {
            new float[]{4,25,22,25,22,20,15,20,14,8,8,5,6,9,10,12,10,20,4,20,4,25},
            new float[]{18,11,24,11,21,14}
        };
        static readonly float[][] Stick = {
            new float[]{7,24,20,24,20,19,7,19,7,24,13,24},
            new float[]{13,19,13,11,6,8,21,8},
            new float[]{2,17,2,23,5,20},new float[]{26,17,26,23,23,20}
        };
        static readonly float[][] Key = {
            new float[]{3,8,3,25,25,25,25,8,3,8},new float[]{7,5,22,5}
        };
        internal static float[][] Paths(string symbol)
        {
            if(string.IsNullOrEmpty(symbol))return Empty;
            if(symbol.Contains("GR"))return Grip;
            if(symbol.Contains("T")&&symbol!="F1")return Trigger;
            if(symbol.Contains("L")||symbol.Contains("R"))return Stick;
            return symbol=="F1"?Key:Empty;
        }
    }
}
