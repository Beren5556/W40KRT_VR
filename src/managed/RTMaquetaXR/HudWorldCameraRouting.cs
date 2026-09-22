using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static long _hudWorldCameraRoutes, _hudWorldCanvasCameraWrites;

        static void UpdateHudWorldPickingCamera()
        {
            // A nested Canvas shares root-canvas camera state in the installed
            // hierarchy. Giving an overtip child the physical camera repeatedly
            // changed DynamicCanvas too; its repair restored the panel camera
            // on the next frame. Keep Canvas state canonical and route world
            // raycasters at their eventCamera getter instead.
            HudWorldPickingCamera(true);
            foreach (var canvas in _hudAddedOvertipCanvases)
                if (canvas != null && canvas.worldCamera != _pickCam)
                { canvas.worldCamera = _pickCam; ++_hudWorldCanvasCameraWrites; }
        }

        static void HudWorldEventCameraPostfix(GraphicRaycaster __instance, ref Camera __result)
        {
            if (!_attached || !_hudStableSpace || _pickCam == null || __instance == null ||
                !IsWorldHudTransform(__instance.transform)) return;
            // Only the actual world-overtip subtree gets physical coordinates;
            // dialogs, ability bars and other PC canvases keep the panel camera.
            __result = HudWorldPickingCamera();
            ++_hudWorldCameraRoutes;
        }

        static void HudNativeWorldEventCamera68(BaseRaycaster __instance,ref Camera __result)
        {
            if(_attached&&_pickCam!=null&&__instance!=null&&IsWorldHudTransform(__instance.transform))
                __result=HudWorldPickingCamera();
        }
    }
}
