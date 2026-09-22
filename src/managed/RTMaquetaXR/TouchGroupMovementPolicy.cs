using System;

namespace RTMaquetaXR
{
    internal enum TouchGroupMoveKind { None, Move, Stop }
    internal struct TouchGroupMoveCommand
    {
        internal TouchGroupMoveKind Kind;
        internal object Unit;
        internal float X, Y, Strength;
    }
    internal sealed class TouchGroupMovementPolicy
    {
        internal const float DeadZone = .2f;
        internal bool Moving { get; private set; }
        internal bool Armed { get; private set; }
        internal object Unit { get; private set; }
        ulong group;
        bool headingLatched;
        float heldHeading;

        internal TouchGroupMoveCommand Step(bool allowed, object unit, ulong selectedGroup, float x, float y, float yaw, bool headView = false)
        {
            if (!Finite(x) || !Finite(y) || !Finite(yaw)) allowed = false;
            float length = allowed ? (float)Math.Sqrt(x * x + y * y) : 0;
            if (!Finite(length)) allowed = false;
            bool neutral = length <= DeadZone;
            if (!allowed || unit == null || (Moving && (!ReferenceEquals(unit, Unit) || group != selectedGroup)))
            {
                var stop = Stop(); Armed = false; return stop;
            }
            if (!Armed)
            { if (neutral) Armed = true; return default; }
            if (neutral) return Stop();
            // Head view and optional table follow track the actor's heading. Reusing that
            // changing heading for one held stick would turn right into an
            // endless spiral. Keep this deflection relative to its starting view.
            if (headView)
            {
                if (!headingLatched) { heldHeading = yaw; headingLatched = true; }
                yaw = heldHeading;
            }
            else headingLatched = false;
            float strength = Math.Min(1, (length - DeadZone) / (1 - DeadZone));
            float nx = x / length, ny = y / length;
            float sin = (float)Math.Sin(yaw), cos = (float)Math.Cos(yaw);
            Moving = true; Unit = unit; group = selectedGroup;
            return new TouchGroupMoveCommand { Kind = TouchGroupMoveKind.Move, Unit = unit,
                X = nx * cos + ny * sin, Y = ny * cos - nx * sin, Strength = strength };
        }
        internal TouchGroupMoveCommand Stop()
        {
            var result = Moving ? new TouchGroupMoveCommand { Kind = TouchGroupMoveKind.Stop, Unit = Unit } : default;
            Moving = false; Unit = null; group = 0; headingLatched = false; return result;
        }
        internal void Reset() { Stop(); Armed = false; }
        internal static bool TryYaw(float x, float y, float z, float w, out float yaw)
        {
            yaw = 0;
            if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(w)) return false;
            double norm = (double)x * x + (double)y * y + (double)z * z + (double)w * w;
            if (norm < .000001 || double.IsInfinity(norm) || double.IsNaN(norm)) return false;
            double forwardX = 2 * ((double)x * z + (double)y * w) / norm;
            double forwardZ = 1 - 2 * ((double)x * x + (double)y * y) / norm;
            if (forwardX * forwardX + forwardZ * forwardZ < .000001) return false;
            yaw = (float)Math.Atan2(forwardX, forwardZ);
            return Finite(yaw);
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
