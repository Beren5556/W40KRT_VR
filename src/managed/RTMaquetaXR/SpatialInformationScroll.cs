using UnityEngine;

namespace RTMaquetaXR
{
    internal sealed class SpatialInformationScrollPolicy
    {
        int revision = -1;
        bool armed;
        internal void Reset() { revision = -1; armed = false; }
        internal float Step(int currentRevision, float axis, bool available)
        {
            if (!available || float.IsNaN(axis) || float.IsInfinity(axis)) { Reset(); return 0; }
            if (currentRevision != revision) { revision = currentRevision; armed = false; }
            if (Mathf.Abs(axis) <= .22f) { armed = true; return 0; }
            return armed ? Mathf.Sign(axis) * Mathf.Clamp01((Mathf.Abs(axis) - .22f) / .78f) : 0;
        }
    }
    public static partial class Main
    {
        static readonly SpatialInformationScrollPolicy _spatialInfoScroll = new SpatialInformationScrollPolicy();
        static bool ScrollSpatialInformation(float axis)
        {
            bool available = !TouchOverlayOpen && !TouchOverlayChordCaptured && _touchRadial.Visible && _touchRadial.Side == 0 &&
                TouchRadialInformationHoverActive && _spatialNative != null && _spatialNative.View == TouchRadialInformationContent && _spatialNative.CanScroll;
            float step = _spatialInfoScroll.Step(TouchRadialInformationRevision, axis, available);
            return step != 0 && _spatialNative.Scroll(step, Time.unscaledDeltaTime);
        }
        static void ResetSpatialInformationScroll() => _spatialInfoScroll.Reset();
    }
}
