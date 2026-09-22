using System;

namespace RTMaquetaXR
{
    internal sealed class TouchDrawDistancePolicy
    {
        bool contactOwned, requireNeutral;
        internal bool Reserved { get; private set; }
        internal bool Adjusting { get; private set; }
        internal float Value { get; private set; }
        internal bool Step(bool available, bool contact, float x, float y, float seconds, float current)
        {
            bool finite = Finite(x) && Finite(y);
            bool neutral = finite && Math.Abs(x) <= TouchBindings.DistanceDeadZone && Math.Abs(y) <= TouchBindings.DistanceDeadZone;
            Adjusting = false;
            if (!available || !finite)
            {
                if (contactOwned) requireNeutral = true;
                contactOwned = false;
                if (neutral) requireNeutral = false;
                Reserved = requireNeutral; return false;
            }
            if (requireNeutral)
            {
                Reserved = true;
                if (neutral) requireNeutral = false;
                return false; // The neutral hand-off frame is consumed too.
            }
            if (!contact)
            {
                if (contactOwned) requireNeutral = !neutral;
                contactOwned = false; Reserved = requireNeutral; return false;
            }
            Reserved = true;
            if (!contactOwned) Value = Finite(current) ? Clamp(current,20,400) : 100;
            contactOwned = true;
            if (Math.Abs(y) <= TouchBindings.DistanceDeadZone || !Finite(seconds) || seconds <= 0) return false;
            Adjusting = true;
            float axis = Math.Sign(y) * Math.Min(1,(Math.Abs(y)-TouchBindings.DistanceDeadZone)/(1-TouchBindings.DistanceDeadZone));
            float next = Clamp(Value + axis * TouchBindings.DistanceUnitsPerSecond * Math.Min(.1f,seconds),20,400);
            bool changed = Math.Abs(next-Value)>.00001f; Value=next; return changed;
        }
        internal void Reset() { contactOwned=requireNeutral=Reserved=Adjusting=false; Value=100; }
        static float Clamp(float x,float low,float high)=>Math.Max(low,Math.Min(high,x));
        static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
    }
}
