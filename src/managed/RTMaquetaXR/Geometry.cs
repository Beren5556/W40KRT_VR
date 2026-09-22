using System;

namespace RTMaquetaXR
{
    // Engine-independent math, shared by the mod and the regression executable.
    internal struct Point3
    {
        public float x, y, z;
        public Point3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public static Point3 operator +(Point3 a, Point3 b) => new Point3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Point3 operator -(Point3 a, Point3 b) => new Point3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Point3 operator *(Point3 a, float k) => new Point3(a.x*k,a.y*k,a.z*k);
    }
    internal struct Rotation4
    {
        public float x, y, z, w;
        public Rotation4(float x,float y,float z,float w) { this.x=x;this.y=y;this.z=z;this.w=w; }
        public static Rotation4 Identity => new Rotation4(0,0,0,1);
        public Rotation4 Inverse()
        {
            float n=x*x+y*y+z*z+w*w;
            if (n < 0.000001f) throw new ArgumentException("Invalid headset quaternion");
            return new Rotation4(-x/n,-y/n,-z/n,w/n);
        }
        public static Rotation4 operator *(Rotation4 a,Rotation4 b) => new Rotation4(
            a.w*b.x+a.x*b.w+a.y*b.z-a.z*b.y,
            a.w*b.y-a.x*b.z+a.y*b.w+a.z*b.x,
            a.w*b.z+a.x*b.y-a.y*b.x+a.z*b.w,
            a.w*b.w-a.x*b.x-a.y*b.y-a.z*b.z);
        public Point3 Rotate(Point3 p)
        {
            Rotation4 q=this*new Rotation4(p.x,p.y,p.z,0)*Inverse();
            return new Point3(q.x,q.y,q.z);
        }
    }
    internal struct ViewPose
    {
        public Point3 position;
        public Rotation4 rotation;
        public ViewPose(Point3 p,Rotation4 r) { position=p;rotation=r; }
        public static ViewPose FromOpenXR(float x,float y,float z,float qx,float qy,float qz,float qw) =>
            new ViewPose(new Point3(x,y,-z),new Rotation4(-qx,-qy,qz,qw));
    }
    internal static class Geometry
    {
        public static float ZoomFactor(float currentDegrees,float referenceDegrees)
        {
            double current=Math.Max(1,Math.Min(160,currentDegrees))*Math.PI/360;
            double reference=Math.Max(1,Math.Min(160,referenceDegrees))*Math.PI/360;
            return (float)Math.Max(.1,Math.Min(10,Math.Tan(current)/Math.Tan(reference)));
        }
        public static ViewPose Eye(ViewPose anchor,ViewPose reference,ViewPose head,ViewPose eye,float scale,float ipd)
        {
            Rotation4 basis=anchor.rotation*reference.rotation.Inverse();
            Point3 translation=head.position-reference.position+(eye.position-head.position)*ipd;
            return new ViewPose(anchor.position+basis.Rotate(translation)*scale,basis*eye.rotation);
        }
    }
}
