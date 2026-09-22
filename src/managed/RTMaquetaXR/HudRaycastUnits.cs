using System;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    internal static class HudRaycastUnitPolicy
    {
        internal static float PhysicalDistance(float renderDistance, float ratio)
        {
            if (float.IsNaN(renderDistance) || float.IsInfinity(renderDistance) || renderDistance < 0 ||
                float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio <= 0) return renderDistance;
            float physical = renderDistance / ratio;
            return float.IsInfinity(physical) ? renderDistance : physical;
        }
    }
    internal struct HudRaycastUnitScope
    {
        internal object Owner, Results;
        internal int Start, Depth;
        internal float Ratio;
        internal bool Active, Normalize;
    }
    internal sealed class HudRaycastUnitStack
    {
        readonly List<HudRaycastUnitScope> active = new List<HudRaycastUnitScope>(4);
        internal HudRaycastUnitScope Begin(object owner, object results, int count, float ratio)
        {
            bool normalize = true;
            for (int i = 0; i < active.Count; ++i)
                if (ReferenceEquals(active[i].Owner, owner) && ReferenceEquals(active[i].Results, results))
                { normalize = false; break; }
            var scope = new HudRaycastUnitScope { Owner = owner, Results = results, Start = count,
                Ratio = ratio, Active = true, Normalize = normalize, Depth = active.Count };
            active.Add(scope);
            return scope;
        }
        internal void End(HudRaycastUnitScope scope)
        {
            if (!scope.Active || scope.Depth < 0 || scope.Depth >= active.Count ||
                !ReferenceEquals(active[scope.Depth].Owner, scope.Owner) ||
                !ReferenceEquals(active[scope.Depth].Results, scope.Results)) return;
            active.RemoveRange(scope.Depth, active.Count - scope.Depth);
        }
        internal int Depth => active.Count;
    }
    public static partial class Main
    {
        // Canvas vertices/worldPosition remain in their renderer's coordinates.
        // Only the distance used by EventSystem's mixed-raycaster sort needs a
        // common unit. This class owns its Harmony state independently of the
        // other prefix that filters hidden PC canvases.
        static class HudRaycastUnitsHooks
        {
            [ThreadStatic] static HudRaycastUnitStack scopes;
            internal static void Prefix(GraphicRaycaster __instance, List<RaycastResult> __1, out HudRaycastUnitScope __state)
            {
                __state = default(HudRaycastUnitScope);
                if (!_hudStableSpace || _hudRenderRatio == 1 || __1 == null || __instance == null ||
                    __instance.eventCamera != _pickCam || IsWorldHudTransform(__instance.transform)) return;
                if (scopes == null) scopes = new HudRaycastUnitStack();
                __state = scopes.Begin(__instance, __1, __1.Count, _hudRenderRatio);
            }
            internal static void Postfix(GraphicRaycaster __instance, List<RaycastResult> __1, HudRaycastUnitScope __state)
            {
                if (!__state.Active || !__state.Normalize || !ReferenceEquals(__state.Results, __1)) return;
                for (int i = __state.Start; i < __1.Count; ++i)
                {
                    var hit = __1[i];
                    if (!ReferenceEquals(hit.module, __instance)) continue;
                    hit.distance = HudRaycastUnitPolicy.PhysicalDistance(hit.distance, __state.Ratio);
                    __1[i] = hit;
                }
            }
            internal static Exception Finalizer(Exception __exception, HudRaycastUnitScope __state)
            { scopes?.End(__state); return __exception; }
        }
    }
}
