using System;
using UnityEngine;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly TouchDeepTriggerPolicy _touchDeepTrigger=new TouchDeepTriggerPolicy();
        static long _touchDeepRevision;
        static int _touchDeepReleaseFrame=-1;
        static object _touchDeepUnit;
        static bool _touchDeepGround;
        static float _touchDeepSeconds = TouchDeepTriggerPolicy.Seconds;
        internal static bool TouchDeepRelease => _touchDeepReleaseFrame==Time.frameCount;
        static bool TouchGameplayRightTrigger => _touchRawRightTrigger&&!_touchDeepTrigger.Suppress;
        static void ArmTouchDeepTrigger(GameObject target,object handler)
        {
            if(!_touchPrimary.Down||!_touchRawRightTrigger||_touchA.Held||_touchRawLeftTrigger||_modeFlat||!_attached||
                InNavigationMap||CinematicWanted||_touchOverUi||target==null||handler==null||_touchGamePointer==null||
                _touchBoxPointerMode(_touchGamePointer)!=0)return;
            _touchDeepUnit=null;_touchDeepGround=false;
            if(handler.GetType()==_touchBoxUnitHandlerType)
            {
                var view=target.GetComponentInParent(_touchBoxUnitViewType);
                object unit=view==null?null:_touchBoxViewEntity(view);
                if(unit==null||(!_touchBoxControllable(unit)&&!InSpaceCombat)||_touchCameraFocusContracts.Dead(unit))return;
                _touchDeepUnit=unit;
            }
            else if(!InSpaceCombat&&handler.GetType()==_touchBoxGroundHandlerType&&_touchBoxIsGround(target)&&
                target.GetComponentInParent(_touchBoxEntityViewType)==null)_touchDeepGround=true;
            else return;
            _touchDeepSeconds = _touchDeepGround && TouchRadialCombatNow(out var actor)
                ? TouchDeepTriggerPolicy.CombatMovementSeconds : TouchDeepTriggerPolicy.Seconds;
            _touchDeepRevision=SpatialGameContextRevision;_touchDeepTrigger.Arm();
        }
        static void ProcessTouchDeepTrigger(bool allowed)
        {
            bool valid=allowed&&!_modeFlat&&!InNavigationMap&&!CinematicWanted&&!TouchMenuWindowVisible&&!NativeTutorialInputBlocked&&
                !_touchOverUi&&!_touchBox.Active&&!_touchB.Held&&!_touchA.Held&&!_touchRawLeftTrigger&&
                _touchDeepRevision==SpatialGameContextRevision&&(_touchDeepUnit!=null||_touchDeepGround);
            if(_touchB.Down&&_touchDeepTrigger.Armed)
            {
                _touchDeepTrigger.Cancel(_touchRawRightTrigger);ClearTouchWorldPointerPress();CancelTouchSelection();
            }
            if(_touchDeepTrigger.Step(_touchSample.right.trigger,_touchRawRightTrigger,valid,Time.unscaledTime,_touchDeepSeconds))
                _touchDeepReleaseFrame=Time.frameCount;
            // The one synthetic UP is handled by the original pointer just as
            // a physical release. Further held samples cannot produce DOWN.
            // UI clicks, active abilities, drag selections and stale areas
            // cannot arm this route.
        }
    }
}
