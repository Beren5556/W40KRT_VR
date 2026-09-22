using System;
using System.Globalization;

namespace RTMaquetaXR
{
    // The old class name keeps Main.Settings integration simple. There is one
    // bounded Touch camera; Libre/Confort preferences have no effect.
    public sealed class ComfortCameraSettings
    {
        public float touchTurnSpeed = 1f;
        public float touchGestureTurnSpeed = 1.1f;
        public float touchZoomSpeed = 1.1f;
        public float touchMoveSpeed = 1.1f;
    }

    internal static class ComfortCameraOptions
    {
        internal const float ScaleMin = 6f, ScaleMax = 14f;
        internal const float TiltMin = -75f, TiltMax = 75f;
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        internal static float Speed(float value) => Finite(value) ? Clamp(value, .25f, 2f) : 1f;
        internal static float Scale(float value) => Finite(value) ? Clamp(value, ScaleMin, ScaleMax) : 10f;
        internal static float Tilt(float value) => Finite(value) ? Clamp(value, TiltMin, TiltMax) : 0f;
        internal static float Seconds(float value) => Finite(value) && value > 0 ? Math.Min(value, 1f / 30f) : 0;
        internal static bool TryParse(ComfortCameraSettings settings, string key, string value)
        {
            if (settings == null || key == null) return false;
            if (key == "cameraMode" || key.StartsWith("comfort", StringComparison.Ordinal)) return true;
            if (key != "touchTurnSpeed" && key != "touchGestureTurnSpeed" && key != "touchZoomSpeed" && key != "touchMoveSpeed") return false;
            float number;
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && Finite(number))
            {
                if (key == "touchTurnSpeed") settings.touchTurnSpeed = Speed(number);
                else if (key == "touchGestureTurnSpeed") settings.touchGestureTurnSpeed = Speed(number);
                else if (key == "touchZoomSpeed") settings.touchZoomSpeed = Speed(number);
                else settings.touchMoveSpeed = Speed(number);
            }
            return true;
        }
        internal static string Serialize(ComfortCameraSettings settings)
        {
            settings = settings ?? new ComfortCameraSettings();
            return "gestureRevision76=76\n" + "touchTurnSpeed=" + Speed(settings.touchTurnSpeed).ToString("R", CultureInfo.InvariantCulture) + "\n" +
                "touchGestureTurnSpeed=" + Speed(settings.touchGestureTurnSpeed).ToString("R", CultureInfo.InvariantCulture) + "\n" +
                "touchZoomSpeed=" + Speed(settings.touchZoomSpeed).ToString("R", CultureInfo.InvariantCulture) + "\n" +
                "touchMoveSpeed=" + Speed(settings.touchMoveSpeed).ToString("R", CultureInfo.InvariantCulture) + "\n";
        }
    }
}
