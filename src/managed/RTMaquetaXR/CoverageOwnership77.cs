using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly Dictionary<object,HashSet<Graphic>> _coverageOwners77=new Dictionary<object,HashSet<Graphic>>();
        static readonly Dictionary<Graphic,int> _coverageReferences77=new Dictionary<Graphic,int>();
        static void RetainCoverage77(object owner,Graphic graphic)
        {
            if(owner==null)owner=graphic;
            if(!_coverageOwners77.TryGetValue(owner,out var graphics))_coverageOwners77.Add(owner,graphics=new HashSet<Graphic>());
            if(graphics.Add(graphic))_coverageReferences77[graphic]=_coverageReferences77.TryGetValue(graphic,out int count)?count+1:1;
        }
        static void ReleaseCoverageOwner77(object owner)
        {
            if(owner==null||!_coverageOwners77.TryGetValue(owner,out var graphics))return;
            _coverageOwners77.Remove(owner);
            foreach(var graphic in graphics)
            {
                if(!_coverageReferences77.TryGetValue(graphic,out int count))continue;
                if(count>1){_coverageReferences77[graphic]=count-1;continue;}
                _coverageReferences77.Remove(graphic);
                if(!_coverageGraphics74.TryGetValue(graphic,out bool native))continue;
                _coverageGraphics74.Remove(graphic);
                if(graphic!=null&&graphic.canvasRenderer!=null)
                {graphic.canvasRenderer.cull=native;graphic.SetAllDirty();}
            }
        }
        static void CoverViewReleased77(Component __instance)=>ReleaseCoverageOwner77(__instance);
        static bool CoverageSuppressed77(Graphic graphic)=>graphic!=null&&CombatPresentationContext74&&_coverageGraphics74.ContainsKey(graphic);
        static void EnforceCoverage77()
        {
            if(!CombatPresentationContext74||_worldPresentationRestoring72)return;
            foreach(var pair in _coverageGraphics74)
                if(pair.Key!=null&&pair.Key.canvasRenderer!=null)pair.Key.canvasRenderer.cull=true;
        }
        static void ResetCoverageOwners77(){_coverageOwners77.Clear();_coverageReferences77.Clear();}
    }
}
