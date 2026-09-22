using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        // The independent compositor follows the head. Moving the actual PC
        // Canvas hierarchy with it forces Unity to rebuild otherwise unchanged
        // batches. Keep its render coordinates fixed and transform only rays.
        static bool _hudStableSpace;
        static Vector3 _hudPhysicalHead;
        static Quaternion _hudPhysicalRotation = Quaternion.identity;
        // A canonical orientation alone still rescales every Canvas on each
        // two-hand zoom. Keep UI geometry in fixed units too; the compositor
        // and pointer transforms express those units back in physical space.
        const float HudCanonicalScale = 10f;
        static float _hudRenderRatio = 1f;
        static float HudLayoutWorldScale => _hudStableSpace ? HudCanonicalScale : WorldScale;
        static float HudRenderLength(float physicalWorldLength) => physicalWorldLength * _hudRenderRatio;
        static Camera _hudWorldPickCamera;
        static int _hudWorldPickFrame = -1;
        static long _hudStableFrames, _hudCapturePauses, _hudTransformWrites, _hudTransformSkips;

        static void BeginHudStableSpace(ref Vector3 head, ref Quaternion rotation)
        {
            _hudPhysicalHead = head; _hudPhysicalRotation = rotation;
            _hudStableSpace = _cfg.uiFullResolution && _cfg.stableHudCapture && !_hudCaptureFault && _hudLayer >= 0 &&
                _hudBlackCamera != null && !_chartFit && _chartBlend <= 0;
            _hudRenderRatio = _hudStableSpace ? HudCanonicalScale / WorldScale : 1f;
            if (_hudStableSpace) { head = Vector3.zero; rotation = Quaternion.identity; ++_hudStableFrames; }
        }
        internal static Ray HudWorldToRenderRay(Ray ray)
        {
            if (!_hudStableSpace) return ray;
            Quaternion inverse = Quaternion.Inverse(_hudPhysicalRotation);
            return new Ray(inverse * (ray.origin - _hudPhysicalHead) * _hudRenderRatio, inverse * ray.direction);
        }
        static Vector3 HudWorldToRenderPoint(Vector3 point) => _hudStableSpace ?
            Quaternion.Inverse(_hudPhysicalRotation) * (point - _hudPhysicalHead) * _hudRenderRatio : point;
        internal static Vector3 HudRenderToWorldPoint(Vector3 point) => _hudStableSpace ?
            _hudPhysicalHead + _hudPhysicalRotation * (point / _hudRenderRatio) : point;
        static bool IsWorldHudTransform(Transform item) => EffectiveWorldOvertips && _overtipsRoot != null &&
            item != null && (item == _overtipsRoot || item.IsChildOf(_overtipsRoot));
        static void PositionHudHelper(Transform item, Vector3 position, Quaternion rotation, float physicalScale = 0)
        {
            if (_hudStableSpace)
            {
                position = HudWorldToRenderPoint(position);
                rotation = Quaternion.Inverse(_hudPhysicalRotation) * rotation;
            }
            PlaceHudTransform(item, position, rotation, physicalScale > 0 ? Vector3.one * HudRenderLength(physicalScale) : item.localScale);
        }
        static void PlaceHudTransform(Transform item, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            // Ignore sub-pixel numerical noise from relative OpenXR poses. The
            // threshold is at most 1/64 of a logical UI pixel, never head motion.
            float tolerance = Mathf.Max(1e-8f, Mathf.Abs(scale.x) / 64f);
            bool poseChanged = !_hudStableSpace || (item.position - position).sqrMagnitude > tolerance * tolerance ||
                Mathf.Abs(Quaternion.Dot(item.rotation, rotation)) < .9999999f;
            bool scaleChanged = !_hudStableSpace || (item.localScale - scale).sqrMagnitude >
                Mathf.Max(1e-18f, scale.sqrMagnitude * 1e-12f);
            if (poseChanged) { item.SetPositionAndRotation(position, rotation); ++_hudTransformWrites; }
            else ++_hudTransformSkips;
            if (scaleChanged) { item.localScale = scale; ++_hudTransformWrites; }
            else ++_hudTransformSkips;
        }
        static Camera HudWorldPickingCamera(bool refresh = false)
        {
            if (!_hudStableSpace || _pickCam == null) return _pickCam;
            if (_hudWorldPickCamera == null)
            {
                var root = new GameObject("RTMaquetaXR physical HUD picking", typeof(Camera));
                Object.DontDestroyOnLoad(root);
                _hudWorldPickCamera = root.GetComponent<Camera>();
            }
            if (refresh || _hudWorldPickFrame != Time.frameCount)
            {
                _hudWorldPickCamera.CopyFrom(_pickCam);
                _hudWorldPickCamera.enabled = false;
                _hudWorldPickCamera.transform.SetPositionAndRotation(_hudPhysicalHead, _hudPhysicalRotation);
                // Preserve the custom off-axis projection while changing the
                // length unit, including near-plane ray origin and clip depths.
                var projection = _pickCam.projectionMatrix;
                projection.m03 /= _hudRenderRatio; projection.m13 /= _hudRenderRatio;
                projection.m23 /= _hudRenderRatio; projection.m33 /= _hudRenderRatio;
                _hudWorldPickCamera.nearClipPlane = _pickCam.nearClipPlane / _hudRenderRatio;
                _hudWorldPickCamera.farClipPlane = _pickCam.farClipPlane / _hudRenderRatio;
                _hudWorldPickCamera.projectionMatrix = projection;
                _hudWorldPickFrame = Time.frameCount;
            }
            return _hudWorldPickCamera;
        }
        static void ClearHudStableSpace()
        {
            _hudStableSpace = false;
            _hudRenderRatio = 1f;
            if (_hudWorldPickCamera != null) Object.Destroy(_hudWorldPickCamera.gameObject);
            _hudWorldPickCamera = null;
            _hudWorldPickFrame = -1;
        }
        // Pauses/one missed pose must not destroy 2 large textures and rescan
        // tens of thousands of UI nodes on resume. A true detach still restores
        // every original layer and releases the complete capture.
        internal static void PauseHudCapture()
        {
            _hudPreparedFrame = -1;
            if (_hudBlackCamera != null && _hudBlackCamera.enabled) _hudBlackCamera.enabled = false;
            if (_hudWhiteCamera != null && _hudWhiteCamera.enabled) _hudWhiteCamera.enabled = false;
            ++_hudCapturePauses;
        }
    }
}
