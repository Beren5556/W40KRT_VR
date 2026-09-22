using System;
namespace RTMaquetaXR
{
    internal sealed class TouchDeepTriggerPolicy
    {
        internal const float Threshold=.95f, Seconds=2f;
        internal const float CombatMovementSeconds=3f;
        internal bool Armed {get;private set;}
        internal bool Suppress {get;private set;}
        double since=-1,last=-1;
        internal void Arm(){if(!Suppress){Armed=true;since=last=-1;}}
        internal bool Step(float pressure,bool held,bool valid,double now,float seconds=Seconds)
        {
            if(!held){Armed=Suppress=false;since=last=-1;return false;}
            if(!valid){Armed=false;since=last=-1;return false;}
            if(!Armed||Suppress)return false;
            if(double.IsNaN(now)||double.IsInfinity(now)||now<0||float.IsNaN(pressure)||float.IsInfinity(pressure)||pressure<Threshold){since=last=-1;return false;}
            // A stalled frame, tracking gap or clock reset is not evidence of
            // an uninterrupted intentional hold. Resume a fresh countdown.
            bool continuous=last>=0 && now>=last && now-last<=.35;
            last=now;
            if(since<0||!continuous){since=now;return false;}
            if(now-since<seconds)return false;
            Armed=false;Suppress=true;since=last=-1;return true;
        }
        internal void Cancel(bool held){Armed=false;Suppress=held;since=last=-1;}
    }
}
