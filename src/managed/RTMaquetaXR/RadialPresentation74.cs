using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    internal sealed class RadialBuffHierarchy74 : MonoBehaviour
    {
        void OnTransformChildrenChanged(){Main.RadialBuffHierarchyChanged74();}
    }
    public static partial class Main
    {
        static MethodInfo _presentationBuffUpdate74,_radialOwnedUpdate74;
        static bool _radialNeedsTick74;
        static long _radialRetainedLeases74,_radialHiddenTicks74;
        static int _radialBuffFingerprint74;
        static bool _radialBuffHierarchyDirty74=true;
        internal static void RadialBuffHierarchyChanged74(){_radialBuffHierarchyDirty74=true;}
        static bool RetainRadialInstruments74=>Presentation74(8)&&!TouchMenuWindowVisible&&!TouchOverlayOpen&&
            (_radialStatusSpace?!_cfg.spaceHudVisible:PcHudMinimal);

        static void InstallRadialPresentation74()
        {
            var unit=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.SurfaceCombat.SurfaceCombatUnitVM");
            _radialOwnedUpdate74=TouchSelectionCallFactory.ExactMethod(unit,"UpdateHandler",typeof(void),false);
            _harmony.Patch(_radialOwnedUpdate74,prefix:new HarmonyMethod(typeof(Main),nameof(RadialOwnedTick74)));
            var buffs=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.Party.UnitBuffPartVM");
            _presentationBuffUpdate74=TouchSelectionCallFactory.ExactMethod(buffs,"UpdateData",typeof(void),false);
            PatchPresentationNoArgs74(_presentationBuffUpdate74);
            // Buff handlers add/remove/dispose individual native models. They
            // are events, not interchangeable with a later UpdateData rebuild.
            // Let them run in order; hidden graphics still avoid render/layout.
        }
        static bool RadialOwnedTick74(object __instance)
        {
            if(!Presentation74(8)||!ReferenceEquals(__instance,_radialStatusModel)||_radialStatusRoot!=null&&_radialStatusRoot.activeInHierarchy)return true;
            ++_radialHiddenTicks74;_radialNeedsTick74=true;return false;
        }
        static bool PresentationBuffEvent74(object __instance)
        {return true;}
        static bool RadialBuffsChanged74()
        {
            if(!Presentation74(8))return true;
            object model=ReadField70(_radialStatusModel,"UnitBuffs");
            var buffs=ReadField70(model,"Buffs") as System.Collections.IEnumerable;
            int signature=17;
            unchecked{if(buffs!=null)foreach(object buff in buffs)
                signature=signature*31+System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(buff);}
            if(signature==_radialBuffFingerprint74&&!_radialBuffHierarchyDirty74)return false;
            _radialBuffFingerprint74=signature;_radialBuffHierarchyDirty74=false;return true;
        }
        static void HydrateRadialPortrait74()
        {
            if(!Presentation74(8)||_radialStatusModel==null)return;
            if(_radialNeedsTick74)
            {_radialNeedsTick74=false;_radialOwnedUpdate74.Invoke(_radialStatusModel,null);}
            var owner=RegisterPresentationOwner74(_radialStatusModel,true);
            FlushPresentationOwner74(owner);
        }
    }
}
