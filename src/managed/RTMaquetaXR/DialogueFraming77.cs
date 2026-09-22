using UnityEngine;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _dialogueApproach77, _dialogueStartReady77;
        static Vector3 _dialogueStartLocal77;
        static Quaternion _dialogueStartRotation77;
        static float _dialogueElapsed77;
        static string _dialogueFrom77;

        static void ResetDialogueFraming77()
        { _dialogueApproach77 = _dialogueStartReady77 = false; _dialogueElapsed77 = 0; }

        static void BeginDialogueFraming77(string previous)
        {
            // Do not reset the table snapshot, actor or manual ownership. Only
            // the automatic endpoint inherited from a cutscene is stale.
            ResetDialogueFraming77(); _dialogueFrom77 = previous ?? "table";
            if (_cinematicCloseupReady && _cinematicFocusTransform != null)
            {
                _dialogueStartLocal77 = TableVector(_cinematicCloseupPose.position) - _cinematicFocusTransform.position;
                _dialogueStartRotation77 = TableQuaternion(_cinematicCloseupPose.rotation);
                _dialogueStartReady77 = true;
            }
            _cinematicCloseupReady = false;
            if (_cinematicFormationReady)
            {
                Vector3 forward = Vector3.ProjectOnPlane(_cinematicFormationHeading * Vector3.forward, Vector3.up).normalized;
                float pitch = TouchCameraFocusPolicy.DialoguePitch76 * Mathf.Deg2Rad;
                _cinematicFormationHeading = Quaternion.LookRotation(forward * Mathf.Cos(pitch) - Vector3.up * Mathf.Sin(pitch), Vector3.up);
            }
            _dialogueApproach77 = true;
        }

        static ViewPose ApplyDialogueFraming77(ViewPose destination, Camera source)
        {
            if (!_dialogueApproach77 || _cinematicFocusTransform == null) return destination;
            Vector3 root = _cinematicFocusTransform.position;
            if (!_dialogueStartReady77)
            {
                _dialogueStartLocal77 = source.transform.position - root;
                _dialogueStartRotation77 = source.transform.rotation;
                _dialogueStartReady77 = true;
            }
            _dialogueElapsed77 += Mathf.Clamp(Time.unscaledDeltaTime, 0, .05f);
            float t = Mathf.SmoothStep(0, 1, _dialogueElapsed77 / .35f);
            Vector3 proposed = Vector3.Lerp(root + _dialogueStartLocal77, TableVector(destination.position), t);
            Vector3 from = _dialogueElapsed77 <= .05f ? root + _dialogueStartLocal77 : TableVector(_dialogueLastPose77.position);
            Vector3 safe = ConstrainCinematicTravel(from, proposed, .08f, out bool blocked);
            var result = new ViewPose(TablePoint(safe), TableRotation(Quaternion.Slerp(_dialogueStartRotation77, TableQuaternion(destination.rotation), t)));
            _dialogueLastPose77 = result;
            if (blocked || t >= 1)
            {
                _dialogueApproach77 = false;
                if (blocked)
                {
                    _cinematicFocusLocal = safe - root;
                    _cinematicFormationRotation = TableQuaternion(result.rotation);
                }
                if (AllDiagnosticsEnabled)
                    _log.Log("[cinematic/dialogue77] " + _dialogueFrom77 + " -> Dialog targetAnchorY=" + destination.position.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                        " appliedAnchorY=" + safe.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " fallback=" + (blocked ? "approach-obstructed" : "none"));
            }
            return result;
        }
        static ViewPose _dialogueLastPose77;
    }
}
