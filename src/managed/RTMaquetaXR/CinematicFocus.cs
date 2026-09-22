using System;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _cinematicCloseupReady;
        static Transform _cinematicFocusTransform;
        static Vector3 _cinematicFocusLocal;
        static float _cinematicCharacterHeight;
        static object _cinematicSpeaker;
        static ViewPose _cinematicCloseupPose;
        static string _cinematicCloseupStatus;
        static float _cinematicNextStatusLog;
        static readonly RaycastHit[] _cinematicCollisionHits = new RaycastHit[32];
        static readonly Collider[] _cinematicOverlapHits = new Collider[32];

        static void ResetCinematicCloseup()
        {
            _cinematicCloseupReady = _cinematicFormationReady = false;
            _cinematicFocusTransform = null; _cinematicSpeaker = null; _cinematicCharacterHeight = 0;
            _cinematicCloseupStatus = null; _cinematicNextStatusLog = 0;
            ResetDialogueFraming77();
            ResetCinematicParticipants();
        }
        static void CinematicFocusStatus(string status)
        {
            if (_cinematicCloseupStatus == status) return;
            _cinematicCloseupStatus = status;
            if (Time.unscaledTime < _cinematicNextStatusLog) return;
            _cinematicNextStatusLog = Time.unscaledTime + 1f;
            _log.Log("[cinematic/focus] " + status);
        }

        static Quaternion _cinematicFormationRotation;
        static Quaternion _cinematicFormationHeading, _cinematicReferenceRotation;
        static Vector3 _cinematicReferencePosition;
        static bool _cinematicFormationReady;
        static bool TryCinematicFocus(Camera source, out Vector3 target, out float height)
        {
            target = Vector3.zero; height = 0;
            // Latch one formation member and one heading for the whole sequence.
            // Dialogue speaker changes no longer expand bounds or change sides.
            if (_cinematicFocusTransform == null)
            {
                object leader = TouchCameraFormationReference();
                var view = leader == null || _touchBoxUnitView == null ? null : _touchBoxUnitView(leader) as Component;
                if (view == null) return false;
                _cinematicCloseupReady = false;
                _cinematicSpeaker = leader; CaptureCinematicCharacter(view.transform);
                Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < .5f) forward = Vector3.forward;
                float pitch=PresentationSceneMode(ReadGameMode())=="Dialog"?TouchCameraFocusPolicy.DialoguePitch76:TouchCameraFocusPolicy.CinematicPitch;
                forward = forward * Mathf.Cos(pitch * Mathf.Deg2Rad) -
                    Vector3.up * Mathf.Sin(pitch * Mathf.Deg2Rad);
                _cinematicFormationHeading = Quaternion.LookRotation(forward, Vector3.up);
                _cinematicFormationReady = true;
            }
            // Follow the actor's root translation, not its animated bones, turn
            // requests or the source camera's intermediate scripted positions.
            float eyeHeight=TouchCameraFocusPolicy.CinematicTargetHeight(_cinematicCharacterHeight);
            if(PresentationSceneMode(ReadGameMode())=="Dialog")eyeHeight+=_cinematicCharacterHeight*.20f;
            target = _cinematicFocusTransform.position + Vector3.up * eyeHeight;
            height = _cinematicCharacterHeight;
            return FiniteCinematicPoint(target);
        }
        static void CaptureCinematicCharacter(Transform root)
        {
            Bounds bounds = CinematicCharacterBounds(root);
            float height = Mathf.Clamp(bounds.size.y, .8f, 8f);
            Vector3 center = bounds.center;
            _cinematicFocusTransform = root; _cinematicFocusLocal = root.InverseTransformPoint(center); _cinematicCharacterHeight = height;
        }

        static bool FiniteCinematicPoint(Vector3 value) => CinematicCloseupPolicy.Finite(value.x) && CinematicCloseupPolicy.Finite(value.y) && CinematicCloseupPolicy.Finite(value.z);
        static bool CinematicObstacle(Collider collider)
        {
            if (collider == null || !collider.enabled || collider.isTrigger) return false;
            if (_touchCameraCollisionIgnore != null && collider.transform.IsChildOf(_touchCameraCollisionIgnore)) return false;
            if (TouchCameraSelectedCollider(collider)) return false;
            if (IsCinematicParticipantCollider(collider)) return false;
            return _cinematicFocusTransform == null || !collider.transform.IsChildOf(_cinematicFocusTransform);
        }
        static Vector3 ConstrainCinematicTravel(Vector3 from, Vector3 proposed, float radius, out bool blocked)
        {
            blocked = false; Vector3 delta = proposed - from; float distance = delta.magnitude;
            if (distance <= .00001f) return from;
            int count = Physics.SphereCastNonAlloc(from, radius, delta / distance, _cinematicCollisionHits,
                distance + .02f, Physics.DefaultRaycastLayers & ~(1 << 5), QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < count; ++i)
                if (CinematicObstacle(_cinematicCollisionHits[i].collider))
                    nearest = Mathf.Min(nearest, _cinematicCollisionHits[i].distance);
            float travel = CinematicCloseupPolicy.SafeTravel(distance, nearest, false, count >= _cinematicCollisionHits.Length);
            Vector3 result = from + delta * (travel / distance);
            int overlaps = Physics.OverlapSphereNonAlloc(result, radius, _cinematicOverlapHits,
                Physics.DefaultRaycastLayers & ~(1 << 5), QueryTriggerInteraction.Ignore);
            if (overlaps >= _cinematicOverlapHits.Length) { blocked = true; return from; }
            for (int i = 0; i < overlaps; ++i)
                if (CinematicObstacle(_cinematicOverlapHits[i])) { blocked = true; return from; }
            blocked = travel < distance; return result;
        }

        static ViewPose CinematicCloseupPose(XrFrame frame, Camera source, Vector3 referencePosition, Quaternion referenceRotation)
        {
            var authored = new ViewPose(TablePoint(source.transform.position), TableRotation(source.transform.rotation));
            try
            {
                if (!TryCinematicFocus(source, out Vector3 focus, out float height) || !_cinematicFormationReady)
                { CinematicFocusStatus("authored fallback: formation character unavailable"); return _cinematicCloseupPose = authored; }
                Vector3 direction = _cinematicFormationHeading * Vector3.forward;
                float distance = TouchCameraFocusPolicy.CinematicDistance(height, TouchWorldScale);
                Vector3 baseline = focus - direction * distance;
                float floorY = _cinematicFocusTransform.position.y;
                Vector3 desired = baseline; desired.y = TouchCameraFocusPolicy.AutomaticHeight78(baseline.y, floorY);
                // Solve the endpoint once at entry. Following the same root is
                // cheap and does not run scene-wide collision queries per frame.
                if (!_cinematicCloseupReady || referencePosition != _cinematicReferencePosition ||
                    Quaternion.Angle(referenceRotation, _cinematicReferenceRotation) > .001f)
                {
                    Vector3 safe = ConstrainCinematicTravel(focus, desired, .08f, out bool blocked);
                    bool endpointSafe = (safe - focus).sqrMagnitude > .25f;
                    bool fullHeight = endpointSafe && TouchCameraFocusPolicy.AutomaticHeightReached78(baseline.y, floorY, safe.y);
                    desired = endpointSafe ? safe : source.transform.position;
                    var placement = TouchCameraFocusPolicy.PlaceHeadAt(
                        new ViewPose(TablePoint(desired), TableRotation(endpointSafe ? Quaternion.LookRotation(focus - desired, Vector3.up) : source.transform.rotation)),
                        new ViewPose(TablePoint(referencePosition), TableRotation(referenceRotation)),
                        new ViewPose(TablePoint(frame.head.Position), TableRotation(frame.head.Rotation)), TouchWorldScale);
                    _cinematicFormationRotation = TableQuaternion(placement.rotation);
                    _cinematicFocusLocal = TableVector(placement.position) - _cinematicFocusTransform.position;
                    _cinematicReferencePosition = referencePosition; _cinematicReferenceRotation = referenceRotation;
                    _cinematicCloseupReady = true;
                    CinematicFocusStatus(fullHeight ? "formation close view: height +35% above local floor, manual Touch available" : endpointSafe ?
                        "collision fallback: reduced formation height; baselineY=" + baseline.y + " floorY=" + floorY + " actualY=" + desired.y :
                        "stable authored fallback: formation close viewpoint obstructed; manual Touch available");
                }
                _cinematicCloseupPose = new ViewPose(TablePoint(_cinematicFocusTransform.position + _cinematicFocusLocal),
                    TableRotation(_cinematicFormationRotation));
                _cinematicCloseupPose = ApplyDialogueFraming77(_cinematicCloseupPose, source);
                return _cinematicCloseupPose;
            }
            catch (Exception error)
            {
                CinematicFocusStatus("authored fallback: " + error.GetType().Name);
                return _cinematicCloseupPose = authored;
            }
        }
    }
}
