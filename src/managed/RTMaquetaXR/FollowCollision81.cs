using UnityEngine;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static SphereCollider _followCollisionProbe81;
        static bool FollowPenetration81(Collider collider,Vector3 at,float radius,out Vector3 direction,out float depth)
        {
            if(_followCollisionProbe81==null)
            {
                var go=new GameObject("RTMaquetaXR follow query sphere"){hideFlags=HideFlags.HideAndDontSave};
                _followCollisionProbe81=go.AddComponent<SphereCollider>();_followCollisionProbe81.enabled=false;
            }
            _followCollisionProbe81.radius=radius;
            return Physics.ComputePenetration(_followCollisionProbe81,at,Quaternion.identity,collider,collider.transform.position,collider.transform.rotation,out direction,out depth);
        }
        static void StopFollowCollision81()
        {if(_followCollisionProbe81!=null)UnityEngine.Object.Destroy(_followCollisionProbe81.gameObject);_followCollisionProbe81=null;}
        // Only locomotion follow uses this horizontal solver. Cinematic framing
        // keeps its existing endpoint rules. Physical tracking remains additive.
        static Vector3 FollowCollisionTravel81(Vector3 head, Vector3 requested, float radius)
        {
            requested.y = 0;
            Vector3 cursor = head, remaining = requested;
            const int mask = Physics.DefaultRaycastLayers & ~(1 << 5);
            int initial=Physics.OverlapSphereNonAlloc(head,radius,_cinematicOverlapHits,mask,QueryTriggerInteraction.Ignore);
            if(initial>=_cinematicOverlapHits.Length)return Vector3.zero;
            Vector3 escape=Vector3.zero;float deepest=0;
            for(int i=0;i<initial;++i)
            {
                var collider=_cinematicOverlapHits[i];
                if(CinematicObstacle(collider)&&FollowPenetration81(collider,head,radius,out var n,out float penetration)&&penetration>deepest)
                {n.y=0;if(n.sqrMagnitude>.001f){deepest=penetration;escape=n.normalized;}}
            }
            if(deepest>0)
            {
                Vector3 step=escape*Mathf.Min(requested.magnitude,.025f);
                // Only reduce existing penetration, never escape across an unseen
                // second surface. This correction is bounded by the requested step.
                if(FollowEndpointSafe81(head,head+step,radius))return step;
                return Vector3.zero;
            }
            for (int pass = 0; pass < 3 && remaining.sqrMagnitude > .00000001f; ++pass)
            {
                float distance = remaining.magnitude;
                int count = Physics.SphereCastNonAlloc(cursor, radius, remaining / distance, _cinematicCollisionHits,
                    distance + .02f, mask, QueryTriggerInteraction.Ignore);
                if (count >= _cinematicCollisionHits.Length) break;
                float nearest = distance + .02f; Vector3 normal = Vector3.zero;
                for (int i = 0; i < count; ++i)
                {
                    var hit = _cinematicCollisionHits[i];
                    if (!CinematicObstacle(hit.collider) || hit.distance >= nearest) continue;
                    nearest = hit.distance; normal = hit.normal;
                }
                float travel = Mathf.Clamp(nearest - .02f, 0, distance);
                Vector3 step = remaining * (travel / distance);
                // Overlap is checked at the endpoint too. When the tracked head
                // already overlaps, only bounded motion away from ALL overlapping
                // surfaces is allowed; no teleport through a wall to escape it.
                if (!FollowEndpointSafe81(cursor, cursor + step, radius)) break;
                cursor += step;
                if (travel >= distance) break;
                remaining -= step; normal.y = 0;
                if (normal.sqrMagnitude < .0001f) break;
                normal.Normalize();
                remaining -= normal * Mathf.Min(0, Vector3.Dot(remaining, normal));
            }
            Vector3 applied = cursor - head; applied.y = 0; return applied;
        }
        static bool FollowEndpointSafe81(Vector3 from, Vector3 to, float radius)
        {
            int count = Physics.OverlapSphereNonAlloc(to, radius, _cinematicOverlapHits,
                Physics.DefaultRaycastLayers & ~(1 << 5), QueryTriggerInteraction.Ignore);
            if (count >= _cinematicOverlapHits.Length) return false;
            for (int i = 0; i < count; ++i)
            {
                var collider = _cinematicOverlapHits[i];
                if (!CinematicObstacle(collider)) continue;
                bool was=FollowPenetration81(collider,from,radius,out var before,out float oldDepth);
                bool remains=FollowPenetration81(collider,to,radius,out var after,out float newDepth);
                if(remains&&(!was||newDepth>=oldDepth-.000001f))return false;
            }
            return true;
        }
    }
}
