using System;
using System.Collections.Generic;
namespace RTMaquetaXR
{
    internal static class ControlIconGeometry
    {
        static readonly Dictionary<string,float[][]> cache=new Dictionary<string,float[][]>();
        internal static float[][] Paths(string symbol)
        {
            symbol=ControlIconMarkup.Symbol(symbol??"");
            if(cache.TryGetValue(symbol,out var found))return found;
            var paths=new List<float[]>();
            bool grip=symbol.EndsWith("G"),trigger=symbol.EndsWith("T"),key=symbol.StartsWith("F");
            if(grip) paths.Add(new float[]{8,48,56,48,59,43,54,15,48,12,16,12,10,15,5,43,8,48});
            else if(key) paths.Add(new float[]{8,12,56,12,56,52,8,52,8,12});
            else Circle(paths,32,32,25);
            // Restrained metal lip, matching the approved minimal reference.
            if(trigger) Arc(paths,32,32,29,205,335);
            if(symbol=="L"||symbol=="R") Circle(paths,32,32,21);
            float width=(symbol.Length*13-3)*1.45f, x=32-width*.5f;
            foreach(char c in symbol) { Letter(paths,c,x,14); x+=13*1.45f; }
            return cache[symbol]=paths.ToArray();
        }
        static void Circle(List<float[]> paths,float x,float y,float r) => Arc(paths,x,y,r,0,360);
        static void Arc(List<float[]> paths,float x,float y,float r,float start,float end)
        {
            int n=Math.Max(8,(int)Math.Ceiling((end-start)/5.625)); var p=new float[(n+1)*2];
            for(int i=0;i<=n;i++){double a=(start+(end-start)*i/n)*Math.PI/180;p[2*i]=x+r*(float)Math.Cos(a);p[2*i+1]=y+r*(float)Math.Sin(a);}paths.Add(p);
        }
        static void Letter(List<float[]> paths,char c,float x,float y)
        {
            string data;
            switch(c) {
                case 'L':data="0,18,0,0,10,0";break;
                case 'R':data="0,0,0,18,8,18,10,15,10,11,8,9,0,9;5,9,11,0";break;
                case 'T':data="0,18,10,18;5,18,5,0";break;
                case 'G':data="10,15,8,18,2,18,0,15,0,3,2,0,10,0,10,8,6,8";break;
                case 'A':data="0,0,5,18,10,0;2,6,8,6";break;
                case 'B':data="0,0,0,18,8,18,10,15,10,12,8,9,0,9;8,9,10,6,10,3,8,0,0,0";break;
                case 'X':data="0,0,10,18;0,18,10,0";break;
                case 'Y':data="0,18,5,9,10,18;5,9,5,0";break;
                case 'F':data="0,0,0,18,10,18;0,10,8,10";break;
                case '1':data="1,14,5,18,5,0;1,0,9,0";break;
                case '2':data="0,15,2,18,8,18,10,15,10,12,0,0,10,0";break;
                case '3':data="0,18,10,18,6,9,10,6,10,3,7,0,0,0;3,9,6,9";break;
                case '4':data="8,0,8,18,0,6,10,6";break;
                default:return;
            }
            foreach(var stroke in data.Split(';')){var coords=stroke.Split(',');var p=new float[coords.Length];for(int i=0;i<p.Length;i++)p[i]=float.Parse(coords[i],System.Globalization.CultureInfo.InvariantCulture)*(i%2==0?1.45f:2)+(i%2==0?x:y);paths.Add(p);}
        }
    }
}
