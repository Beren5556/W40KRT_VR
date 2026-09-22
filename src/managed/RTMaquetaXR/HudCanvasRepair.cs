using UnityEngine;

namespace RTMaquetaXR
{
    internal struct HudRepairLogState
    {
        internal bool Reported;
        internal int Pending;
        internal float Next;
        internal bool Observe(float now, bool recording)
        {
            if (Pending < int.MaxValue) ++Pending;
            if (!recording || (Reported && now < Next)) return false;
            Reported = true;
            Next = now + 5f;
            return true;
        }
    }

    public static partial class Main
    {
        static long _hudRepairEvents, _hudRepairModeWrites, _hudRepairCameraWrites;
        static long _hudRepairGeometryWrites, _hudRepairReports;

        static void HealConvertedCanvas(SavedCanvas saved)
        {
            var canvas = saved.canvas;
            bool modeWrong = canvas.renderMode != RenderMode.WorldSpace;
            var previousCamera = canvas.worldCamera;
            bool cameraWrong = previousCamera != saved.panelCam;
            if (!modeWrong && !cameraWrong) return;
            ++_hudRepairEvents;
            var rect = canvas.transform as RectTransform;
            // A camera-only rebind must not reapply the Canvas render mode.
            // Keep the original geometry recovery and native rebind protection.
            if (modeWrong) { canvas.renderMode = RenderMode.WorldSpace; ++_hudRepairModeWrites; }
            if (modeWrong) cameraWrong = canvas.worldCamera != saved.panelCam;
            if (cameraWrong) { canvas.worldCamera = saved.panelCam; ++_hudRepairCameraWrites; }
            bool geometryWrong = saved.panelGeomValid && rect != null &&
                (rect.sizeDelta != saved.panelSize || rect.pivot != saved.panelPivot ||
                 rect.anchorMin != saved.panelAnchorMin || rect.anchorMax != saved.panelAnchorMax);
            if (geometryWrong)
            {
                if (rect.anchorMin != saved.panelAnchorMin) rect.anchorMin = saved.panelAnchorMin;
                if (rect.anchorMax != saved.panelAnchorMax) rect.anchorMax = saved.panelAnchorMax;
                if (rect.pivot != saved.panelPivot) rect.pivot = saved.panelPivot;
                if (rect.sizeDelta != saved.panelSize) rect.sizeDelta = saved.panelSize;
                ++_hudRepairGeometryWrites;
            }
            // Repeated camera claims generated >14,000 synchronous UMM lines in
            // the 0.1.34 session. Counters retain every repair; log at most once
            // per canvas per five seconds and respect the diagnostic switch.
            if (!saved.repairLog.Observe(Time.unscaledTime, DiagnosticsRecording)) return;
            int count = saved.repairLog.Pending;
            saved.repairLog.Pending = 0;
            ++_hudRepairReports;
            _log.Log("[re-convert] '" + canvas.name + "' repaired " + count + " time(s); mode=" + modeWrong +
                ", camera=" + cameraWrong + ", rect=" + geometryWrong + "; previousCamera='" +
                (previousCamera == null ? "null" : previousCamera.name) + "'; panelCamera='" +
                (saved.panelCam == null ? "null" : saved.panelCam.name) + "'.");
        }
    }
}
