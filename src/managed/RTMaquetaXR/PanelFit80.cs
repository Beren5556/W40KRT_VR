using System;
namespace RTMaquetaXR
{
    internal struct PanelPlacement80 { internal float X, Y, Width, Height; internal bool Limited; }
    internal static class PanelFit80
    {
        internal static float Offset(float value) => Finite(value) ? Math.Max(-.65f, Math.Min(.65f, value)) : 0;
        internal static bool Fit(float depth, float width, float height, float x, float y,
            HudFrustumPlane[] planes, out PanelPlacement80 result)
        {
            result = default;
            if (!Finite(depth) || !Finite(width) || !Finite(height) || !Finite(x) || !Finite(y) ||
                depth <= 0 || width <= 0 || height <= 0 || planes == null || planes.Length != 8) return false;
            foreach (var p in planes)
                if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z) || !Finite(p.Offset) || p.X*p.X+p.Y*p.Y < .000001f) return false;
            float px=x, py=y, scale=1;
            if (!Place(depth, width, height, planes, ref px, ref py))
            {
                // A guaranteed centered fit, including a 3% margin on each side.
                foreach (var p in planes)
                {
                    float room=p.Z*depth+p.Offset;
                    if (room <= 0) return false;
                    scale=Math.Min(scale,room/(Math.Abs(p.X)*width*.53f+Math.Abs(p.Y)*height*.53f));
                }
                width*=scale; height*=scale; px=x; py=y;
                if (!Place(depth,width,height,planes,ref px,ref py)) { px=py=0; }
            }
            result=new PanelPlacement80 {X=px,Y=py,Width=width,Height=height,
                Limited=scale<.9999f || Math.Abs(px-x)>.0001f || Math.Abs(py-y)>.0001f};
            return Contains(depth, width, height, px, py, planes);
        }
        static bool Place(float z,float w,float h,HudFrustumPlane[] planes,ref float x,ref float y)
        {
            for(int pass=0;pass<32;pass++)
            {
                bool moved=false;
                foreach(var p in planes)
                {
                    float slack=p.X*x+p.Y*y+p.Z*z+p.Offset-Math.Abs(p.X)*w*.53f-Math.Abs(p.Y)*h*.53f;
                    if(slack>=-.000001f)continue;
                    float push=-slack/(p.X*p.X+p.Y*p.Y); x+=p.X*push; y+=p.Y*push; moved=true;
                }
                if(!moved)return true;
            }
            return Contains(z,w,h,x,y,planes);
        }
        internal static bool Contains(float z,float w,float h,float x,float y,HudFrustumPlane[] planes)
        {
            foreach(var p in planes)
                if(p.X*x+p.Y*y+p.Z*z+p.Offset-Math.Abs(p.X)*w*.5f-Math.Abs(p.Y)*h*.5f < -.00001f)return false;
            return true;
        }
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
}
