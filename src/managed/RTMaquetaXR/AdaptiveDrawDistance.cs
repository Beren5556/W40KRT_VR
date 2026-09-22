using System;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly AdaptiveDistancePolicy _adaptiveDistance = new AdaptiveDistancePolicy();
        static int _adaptiveDistanceFrame = -1;
        static float _adaptiveDistanceCeiling, _adaptiveDistanceNeeded, _adaptiveFloorY;
        static bool _adaptiveGroundValid;
        static string _adaptiveDistanceStatus = "Manual";
        static float _adaptiveGroundAt = -100;
        static Vector3 _adaptiveProbeOrigin;
        static Quaternion _adaptiveProbeRotation;
        // Exact mask used by installed PointerController.IsGround; the build
        // verifier checks that original contract. The predicate is also called
        // for each returned collider, with entities and rigid bodies excluded.
        const int AdaptiveGroundLayerMask = 2359553;
        static readonly RaycastHit[] _adaptiveGroundHits = new RaycastHit[32];
        static readonly float[] _adaptiveGroundHeights = new float[AdaptiveGroundPolicy.ProbeCount];
        static readonly bool[] _adaptiveGroundSamples = new bool[AdaptiveGroundPolicy.ProbeCount];
        static Func<GameObject, bool> _adaptiveIsGround;
        static Type _adaptiveEntityViewType;
        static bool _adaptiveGroundContractAttempted, _adaptiveMultipleLevels, _adaptiveGroundOverflow;
        static int _adaptiveGroundSampleCount;
        static float _adaptiveGroundSpread;
        static long _adaptiveGroundRaycasts;
        static string _adaptiveGroundContractError;
        internal static string AdaptiveDistanceStatus => InSpaceCombat || InNavigationMap ?
            ModLocalization.Text("Native space distance · terrain clipping disabled") : ModLocalization.Text(_adaptiveDistanceStatus) + " · " +
            _adaptiveDistance.Current.ToString("0.#") + " / " + _adaptiveDistanceCeiling.ToString("0.#");

        // Called once BEFORE configuring either eye. No scene enumeration, GPU readback
        // or raycast per object. Five scene probes at most 10 Hz and eight corner rays.
        internal static void PrepareAdaptiveDistance(XrFrame frame, Vector3 referencePosition, Quaternion referenceRotation,
            Vector3 gameAnchor, Quaternion gameRotation, Camera source)
        {
            float near = DrawDistanceOptions.SanitizeNear(Mathf.Min(source.nearClipPlane, Mathf.Max(.01f, WorldScale * .02f)));
            bool spatial = InSpaceCombat || InNavigationMap;
            float ceiling = DrawDistanceOptions.FarClip(!spatial && _drawDistanceApplied ? _drawDistanceMode : 0, near,
                source.farClipPlane, _drawDistanceAppliedCustom);
            _adaptiveDistanceCeiling = ceiling; _adaptiveDistanceFrame = Time.frameCount;
            if (spatial || !_cfg.adaptiveDistance || _drawDistanceTrial.Running || _drawDistanceStartGate.Armed || _modeName != "Default" || CinematicWanted)
            {
                _adaptiveDistance.Reset(ceiling); _adaptiveGroundValid = false; _adaptiveGroundAt = -100;
                _adaptiveDistanceStatus = _cfg.adaptiveDistance ? "Manual during transition/test" : "Manual";
                return;
            }
            long started = DiagnosticTimestamp();
            var gamePose = AdaptivePose(gameAnchor, gameRotation);
            var reference = AdaptivePose(referencePosition, referenceRotation);
            var head = AdaptivePose(frame.head.Position, frame.head.Rotation);
            var left = Geometry.Eye(gamePose, reference, head, AdaptivePose(frame.left.pose.Position, frame.left.pose.Rotation), WorldScale, 1);
            var right = Geometry.Eye(gamePose, reference, head, AdaptivePose(frame.right.pose.Position, frame.right.pose.Rotation), WorldScale, 1);
            var center = new Vector3((left.position.x + right.position.x) * .5f,
                (left.position.y + right.position.y) * .5f, (left.position.z + right.position.z) * .5f);
            var rotation = new Quaternion(left.rotation.x, left.rotation.y, left.rotation.z, left.rotation.w);
            // Movement invalidates an old sample immediately. Until the next probe the
            // manual ceiling is used, so a stale nearby floor cannot shrink a new view.
            if ((center - _adaptiveProbeOrigin).sqrMagnitude > 1 || Quaternion.Angle(rotation, _adaptiveProbeRotation) > 8)
                _adaptiveGroundValid = false;
            if (Time.unscaledTime - _adaptiveGroundAt >= .1f)
            {
                _adaptiveGroundAt = Time.unscaledTime; _adaptiveProbeOrigin = center; _adaptiveProbeRotation = rotation;
                ProbeAdaptiveGround(center, rotation, frame.left.fov, ceiling);
            }
            float needed = 0;
            bool usable = _adaptiveGroundValid && AdaptiveEyeDepth(left, frame.left.fov, ref needed) &&
                AdaptiveEyeDepth(right, frame.right.fov, ref needed);
            // Reserve geometry above/behind the sampled tabletop surface and a margin
            // outside the delivered FOV. On multi-level/open scenes use manual distance.
            _adaptiveDistanceNeeded = needed + 8;
            _adaptiveDistance.Update(ceiling, _adaptiveDistanceNeeded, usable, Time.unscaledDeltaTime);
            _adaptiveDistanceStatus = !usable ? !_adaptiveGroundValid ? AdaptiveGroundFailureDescription() : "Manual · open horizon" :
                _adaptiveDistance.Current < ceiling - .5f ? "Adaptive · downward view" : "Adaptive · manual ceiling";
            RecordModStage("AdaptiveDistance", started);
        }

        static void ProbeAdaptiveGround(Vector3 center, Quaternion rotation, XrFov fov, float ceiling)
        {
            _adaptiveGroundValid = false; _adaptiveMultipleLevels = _adaptiveGroundOverflow = false;
            _adaptiveGroundSampleCount = 0; _adaptiveGroundSpread = 0;
            if (!EnsureAdaptiveGroundContract() || !ValidHudFov(fov)) return;
            float left = Mathf.Tan(fov.left), right = Mathf.Tan(fov.right), down = Mathf.Tan(fov.down), up = Mathf.Tan(fov.up);
            float middleX = (left + right) * .5f, middleY = (down + up) * .5f;
            for (int i = 0; i < AdaptiveGroundPolicy.ProbeCount; ++i)
            {
                float x = middleX + (i == 1 ? -.22f : i == 2 ? .22f : 0) * (right - left);
                float y = middleY + (i == 3 ? -.22f : i == 4 ? .22f : 0) * (up - down);
                Vector3 direction = rotation * new Vector3(x, y, 1);
                int hits = Physics.RaycastNonAlloc(center, direction.normalized, _adaptiveGroundHits, ceiling,
                    AdaptiveGroundLayerMask, QueryTriggerInteraction.Ignore);
                ++_adaptiveGroundRaycasts; _adaptiveGroundSamples[i] = false;
                if (hits == _adaptiveGroundHits.Length) { _adaptiveGroundOverflow = true; continue; }
                float closest = float.MaxValue, lowest = float.MaxValue, highest = float.MinValue;
                for (int j = 0; j < hits; ++j)
                {
                    var hit = _adaptiveGroundHits[j]; var collider = hit.collider;
                    if (collider == null || !(hit.normal.y > .85f) || hit.rigidbody != null ||
                        !DrawDistanceOptions.Finite(hit.point.y) || !DrawDistanceOptions.Finite(hit.distance) ||
                        !_adaptiveIsGround(collider.gameObject) || collider.GetComponentInParent(_adaptiveEntityViewType) != null) continue;
                    lowest = Mathf.Min(lowest, hit.point.y); highest = Mathf.Max(highest, hit.point.y);
                    if (hit.distance < closest)
                    { closest = hit.distance; _adaptiveGroundSamples[i] = true; _adaptiveGroundHeights[i] = hit.point.y; }
                }
                if (_adaptiveGroundSamples[i] && highest - lowest > AdaptiveGroundPolicy.MaximumHeightSpread) _adaptiveMultipleLevels = true;
            }
            _adaptiveGroundValid = AdaptiveGroundPolicy.TryPlane(_adaptiveGroundHeights, _adaptiveGroundSamples,
                _adaptiveMultipleLevels, _adaptiveGroundOverflow, out _adaptiveFloorY, out _adaptiveGroundSampleCount, out _adaptiveGroundSpread);
        }

        static bool EnsureAdaptiveGroundContract()
        {
            if (!_adaptiveGroundContractAttempted)
            {
                _adaptiveGroundContractAttempted = true;
                try
                {
                    var method = AccessTools.Method(AccessTools.TypeByName("Kingmaker.Controllers.Clicks.PointerController"), "IsGround", new[] { typeof(GameObject) });
                    _adaptiveEntityViewType = AccessTools.TypeByName("Kingmaker.View.EntityViewBase") ?? throw new TypeLoadException("EntityViewBase");
                    _adaptiveIsGround = (Func<GameObject, bool>)Delegate.CreateDelegate(typeof(Func<GameObject, bool>), method);
                }
                catch (Exception error) { _adaptiveIsGround = null; _adaptiveGroundContractError = error.Message; }
            }
            return _adaptiveIsGround != null && _adaptiveEntityViewType != null;
        }
        static string AdaptiveGroundFailureDescription() => _adaptiveGroundContractError != null ? "Manual · ground unavailable" :
            _adaptiveGroundOverflow ? "Manual · too many contacts" : _adaptiveMultipleLevels || _adaptiveGroundSpread > AdaptiveGroundPolicy.MaximumHeightSpread ?
                "Manual · multiple levels" : "Manual · insufficient ground";
        static ViewPose AdaptivePose(Vector3 p, Quaternion q) => new ViewPose(new Point3(p.x,p.y,p.z), new Rotation4(q.x,q.y,q.z,q.w));
        static bool AdaptiveEyeDepth(ViewPose pose, XrFov fov, ref float needed)
        {
            var q = new Quaternion(pose.rotation.x,pose.rotation.y,pose.rotation.z,pose.rotation.w);
            const float margin = 3 * Mathf.Deg2Rad;
            if (!ValidHudFov(fov) || fov.left - margin <= -1.56f || fov.right + margin >= 1.56f ||
                fov.down - margin <= -1.56f || fov.up + margin >= 1.56f) return false;
            for (int x = 0; x < 2; ++x) for (int y = 0; y < 2; ++y)
            {
                // Keep z=1: the intersection parameter is camera depth, not ray length.
                var d = q * new Vector3(Mathf.Tan(x == 0 ? fov.left - margin : fov.right + margin),
                    Mathf.Tan(y == 0 ? fov.down - margin : fov.up + margin), 1);
                if (!AdaptiveDistancePolicy.GroundDepth(pose.position.y, d.y, _adaptiveFloorY, out float depth)) return false;
                needed = Mathf.Max(needed, depth);
            }
            return true;
        }
        static float ApplyAdaptiveFar(float far) => !InSpaceCombat && !InNavigationMap && _cfg.adaptiveDistance && !_drawDistanceTrial.Running &&
            _adaptiveDistanceFrame == Time.frameCount ? Mathf.Min(far, _adaptiveDistance.Current) : far;
        static object AdaptiveDistanceSnapshot() => new {
            Enabled = _cfg.adaptiveDistance, Current = _adaptiveDistance.Current, Ceiling = _adaptiveDistanceCeiling,
            Needed = _adaptiveDistanceNeeded, GroundValid = _adaptiveGroundValid, Status = _adaptiveDistanceStatus,
            ExperimentalGroundPlaneEstimate = true, BothEyesSameLimit = true, MaximumGroundProbesPerSecond = 50,
            MaximumGroundProbeGroupsPerSecond = 10, GroundRaysPerGroup = AdaptiveGroundPolicy.ProbeCount,
            GroundValidSamples = _adaptiveGroundSampleCount, GroundAgreementRequired = AdaptiveGroundPolicy.MinimumAgreement, GroundHeightSpread = _adaptiveGroundSpread,
            MultipleGroundLevels = _adaptiveMultipleLevels, GroundHitBufferOverflow = _adaptiveGroundOverflow,
            GroundContractError = _adaptiveGroundContractError, GroundRaycasts = _adaptiveGroundRaycasts,
            GroundMaskMatchesOriginalPointer = AdaptiveGroundLayerMask, EntityCollidersExcluded = true,
            NearPlaneUnchanged = true, TemporalHistoryResetPerAdjustment = false
        };
    }
}
