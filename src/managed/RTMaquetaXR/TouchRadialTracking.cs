using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Vector3 _spatialTrackingHead, _spatialGameHead;
        static Quaternion _spatialTrackingToGame = Quaternion.identity;
        static Vector3 _touchRadialTrackingCentre, _touchRadialTrackingHit;
        static Quaternion _touchRadialTrackingRotation;
        static Ray _touchRadialTrackingRay;
        static bool _touchRadialTrackingReady;
        internal static void ResetTouchSpatialReference()
        {
            // A new OpenXR LOCAL origin invalidates stored controller-space
            // planes. Cancel the wheel through its normal release guard;
            // a compact map is safely reanchored during this same LateUpdate.
            _touchRadial.Cancel(); _touchRadialConsumedFrame = true;
            CloseTouchRadialVisual(); ClearTouchRadialPortraitHover();
            ClearTouchRadialCombatInformation(false); ClearTouchRadialAbilityInformation(); ReleaseSpatialNative(); PauseSpatialUi();
        }
        internal static void SetTouchSpatialFrame(XrFrame frame, Vector3 head, Quaternion rotation)
        {
            _spatialTrackingHead = frame.head.Position; _spatialGameHead = head;
            _spatialTrackingToGame = rotation * Quaternion.Inverse(frame.head.Rotation);
        }
        static Vector3 SpatialToGame(Vector3 point) => _spatialGameHead +
            _spatialTrackingToGame * ((point - _spatialTrackingHead) * WorldScale);
        static Vector3 SpatialToTracking(Vector3 point) => _spatialTrackingHead +
            Quaternion.Inverse(_spatialTrackingToGame) * ((point - _spatialGameHead) / WorldScale);
        static void UpdateTouchRadialTrackingPlane()
        {
            if (!_touchRadialTrackingReady)
            {
                _touchRadialTrackingCentre = SpatialToTracking(_touchRadialWorldCenter);
                _touchRadialTrackingRotation = Quaternion.Inverse(_spatialTrackingToGame) * _touchRadialWorldRotation;
                _touchRadialTrackingReady = true;
            }
            // Automatic cinematic/combat camera motion behind the wheel must
            // not move its physical plane, or change the opposite-hand hit.
            _touchRadialWorldCenter = SpatialToGame(_touchRadialTrackingCentre);
            _touchRadialWorldRotation = _spatialTrackingToGame * _touchRadialTrackingRotation;
            _touchRadialWorldPixel = WorldScale * TouchRadialProjection.MetresPerPixel;
            if (_touchRadialSamplePointerReady)
            {
                _touchRadialSamplePointer = new Ray(SpatialToGame(_touchRadialTrackingRay.origin),
                    _spatialTrackingToGame * _touchRadialTrackingRay.direction);
                _touchRadialPointerPoint = SpatialToGame(_touchRadialTrackingHit);
            }
        }
    }
}
