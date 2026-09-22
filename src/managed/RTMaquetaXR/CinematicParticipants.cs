using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        sealed class CinematicParticipant
        {
            internal object Unit;
            internal Transform Root;
            internal Vector3 LocalCenter, LocalExtents;
            internal bool Present;
        }
        const int CinematicParticipantLimit = 32;
        static readonly List<CinematicParticipant> _cinematicParticipants = new List<CinematicParticipant>(CinematicParticipantLimit);
        static object _cinematicDialogIdentity;
        static bool _cinematicParticipantsOverflow;
        static Bounds _cinematicParticipantsBounds;
        static bool _cinematicHaveParticipants;

        static void ResetCinematicParticipants()
        {
            _cinematicParticipants.Clear(); _cinematicDialogIdentity = null;
            _cinematicParticipantsOverflow = _cinematicHaveParticipants = false;
        }
        static void RefreshCinematicParticipants(object dialog, object speaker)
        {
            object identity = dialog == null ? null : _presentation.DialogIdentity(dialog);
            if (!ReferenceEquals(identity, _cinematicDialogIdentity))
            { ResetCinematicParticipants(); _cinematicDialogIdentity = identity; }
            foreach (var participant in _cinematicParticipants) participant.Present = false;
            _cinematicParticipantsOverflow = false;
            if (dialog == null) return;
            AddCinematicParticipant(speaker);
            AddCinematicParticipant(_presentation.FirstSpeaker(dialog));
            AddCinematicParticipant(_presentation.Initiator(dialog));
            AddCinematicParticipant(_presentation.ActingUnit(dialog));
            var involved = _presentation.InvolvedUnits(dialog) as IEnumerable;
            if (involved != null)
            {
                int examined = 0;
                foreach (var unit in involved)
                {
                    if (++examined > CinematicParticipantLimit) { _cinematicParticipantsOverflow = true; break; }
                    AddCinematicParticipant(unit);
                }
            }
            for (int i = _cinematicParticipants.Count - 1; i >= 0; --i)
                if (!_cinematicParticipants[i].Present) _cinematicParticipants.RemoveAt(i);
        }
        static void AddCinematicParticipant(object unit)
        {
            if (unit == null) return;
            var view = _presentation.UnitView(unit) as Component;
            if (view == null || !view.gameObject.activeInHierarchy) return;
            foreach (var existing in _cinematicParticipants)
                if (ReferenceEquals(existing.Unit, unit) && existing.Root == view.transform)
                { existing.Present = true; return; }
            if (_cinematicParticipants.Count >= CinematicParticipantLimit) { _cinematicParticipantsOverflow = true; return; }
            Bounds bounds = CinematicCharacterBounds(view.transform);
            _cinematicParticipants.Add(new CinematicParticipant {
                Unit = unit, Root = view.transform, LocalCenter = view.transform.InverseTransformPoint(bounds.center),
                // A small envelope tolerates animation without scanning renderer
                // bounds every frame. The full group remains inside the fit.
                LocalExtents = CinematicBoundsExtents(view.transform.worldToLocalMatrix, bounds.extents + Vector3.one * .12f), Present = true
            });
        }
        static bool TryCinematicParticipantsBounds(out Bounds bounds)
        {
            bounds = default(Bounds); bool found = false;
            if (_cinematicParticipantsOverflow) return _cinematicHaveParticipants = false;
            foreach (var participant in _cinematicParticipants)
            {
                if (!participant.Present || participant.Root == null || !participant.Root.gameObject.activeInHierarchy) continue;
                var matrix = participant.Root.localToWorldMatrix;
                Vector3 center = matrix.MultiplyPoint3x4(participant.LocalCenter);
                if (!FiniteCinematicPoint(center)) continue;
                var next = new Bounds(center, CinematicBoundsExtents(matrix, participant.LocalExtents) * 2);
                if (!found) { bounds = next; found = true; } else bounds.Encapsulate(next);
            }
            _cinematicParticipantsBounds = bounds;
            return _cinematicHaveParticipants = found;
        }
        static Vector3 CinematicBoundsExtents(Matrix4x4 matrix, Vector3 extents) => new Vector3(
            Mathf.Abs(matrix.m00) * extents.x + Mathf.Abs(matrix.m01) * extents.y + Mathf.Abs(matrix.m02) * extents.z,
            Mathf.Abs(matrix.m10) * extents.x + Mathf.Abs(matrix.m11) * extents.y + Mathf.Abs(matrix.m12) * extents.z,
            Mathf.Abs(matrix.m20) * extents.x + Mathf.Abs(matrix.m21) * extents.y + Mathf.Abs(matrix.m22) * extents.z);
        static bool IsCinematicParticipantCollider(Collider collider)
        {
            foreach (var participant in _cinematicParticipants)
                if (participant.Present && participant.Root != null && collider.transform.IsChildOf(participant.Root)) return true;
            return false;
        }
        static Bounds CinematicCharacterBounds(Transform root)
        {
            bool found = false; Bounds bounds = default(Bounds);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) continue;
                var next = renderer.bounds;
                if (!FiniteCinematicPoint(next.center) || !FiniteCinematicPoint(next.size) || next.size.y < .05f || next.size.y > 12f ||
                    (next.center - root.position).sqrMagnitude > 100f) continue;
                if (!found) { bounds = next; found = true; } else bounds.Encapsulate(next);
            }
            return found ? bounds : new Bounds(root.position + Vector3.up * .9f, new Vector3(.8f, 1.8f, .8f));
        }
        static Vector3 CinematicManualPivot(Camera source)
        {
            if (_cinematicHaveParticipants) return _cinematicParticipantsBounds.center;
            if (_cinematicFocusTransform != null) return _cinematicFocusTransform.position + Vector3.up * (_cinematicCharacterHeight * .64f);
            return TableVector(_cinematicCloseupPose.position) + TableQuaternion(_cinematicCloseupPose.rotation) * Vector3.forward * Mathf.Max(1, TouchWorldScale * .6f);
        }
        static float CinematicGroupDistance(XrFrame frame, Quaternion orientation)
        {
            if (!_cinematicHaveParticipants) return 0;
            var bounds = _cinematicParticipantsBounds;
            var inverse = Quaternion.Inverse(orientation);
            float horizontal = 2 * Mathf.Min(Mathf.Min(-frame.left.fov.left, frame.left.fov.right),
                Mathf.Min(-frame.right.fov.left, frame.right.fov.right)) * Mathf.Rad2Deg;
            float vertical = 2 * Mathf.Min(Mathf.Min(-frame.left.fov.down, frame.left.fov.up),
                Mathf.Min(-frame.right.fov.down, frame.right.fov.up)) * Mathf.Rad2Deg;
            if (!CinematicCloseupPolicy.Finite(horizontal) || !CinematicCloseupPolicy.Finite(vertical) ||
                horizontal < 1 || horizontal > 170 || vertical < 1 || vertical > 170) return 0;
            float distance = TouchWorldScale * CinematicCloseupPolicy.MinimumPhysicalDistance;
            // Eight corners enclose all participant bounds. Fit in a comfortable
            // central portion of the actual binocular field, with room for HUD.
            for (int i = 0; i < 8; ++i)
            {
                var corner = new Vector3((i & 1) == 0 ? -bounds.extents.x : bounds.extents.x,
                    (i & 2) == 0 ? -bounds.extents.y : bounds.extents.y,
                    (i & 4) == 0 ? -bounds.extents.z : bounds.extents.z);
                Vector3 local = inverse * corner;
                distance = Mathf.Max(distance, CinematicCloseupPolicy.FitDistance(local.x, local.y, local.z, horizontal, vertical));
            }
            return distance;
        }
    }
}
