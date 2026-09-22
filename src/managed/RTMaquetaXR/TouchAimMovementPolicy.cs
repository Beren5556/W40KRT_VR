using System;

namespace RTMaquetaXR
{
    internal sealed class TouchAimMovementPolicy
    {
        Rotation4 heading;
        bool ready;
        internal void Reset() { ready=false; }
        internal bool TryYaw(Rotation4 view,bool moving,bool neutral,out float yaw)
        {
            yaw=0;
            if(!Valid(view)) { ready=false;return false; }
            if(!moving || neutral || !ready)
            {
                heading=view;ready=true;
            }
            return TouchGroupMovementPolicy.TryYaw(heading.x,heading.y,heading.z,heading.w,out yaw);
        }
        static bool Valid(Rotation4 q)
        {
            double n=(double)q.x*q.x+(double)q.y*q.y+(double)q.z*q.z+(double)q.w*q.w;
            return n>.000001 && !double.IsNaN(n) && !double.IsInfinity(n);
        }
    }
}
