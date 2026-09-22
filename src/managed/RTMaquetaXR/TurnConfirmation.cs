using System;
using HarmonyLib;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Action<object> _confirmedNativeEndTurn;
        static Func<object,bool> _manualCanEnd,_manualPlayerTurn;
        static Func<object,object> _manualCurrentUnit;
        static bool _endTurnConfirmed;
        static OverlayMenu _endTurnConfirmation;
        static void InstallEndTurnConfirmation()
        {
            var type=AccessTools.TypeByName("Kingmaker.Controllers.TurnBased.TurnController");
            var method=TouchSelectionCallFactory.ExactMethod(type,"TryEndPlayerTurnManually",typeof(void),false);
            _confirmedNativeEndTurn=(Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>),method);
            _manualCanEnd=(Func<object,bool>)TouchSelectionCallFactory.Build(typeof(Func<object,bool>),AccessTools.PropertyGetter(type,"CanEndTurn"));
            _manualPlayerTurn=(Func<object,bool>)TouchSelectionCallFactory.Build(typeof(Func<object,bool>),AccessTools.PropertyGetter(type,"IsPlayerTurn"));
            _manualCurrentUnit=(Func<object,object>)TouchSelectionCallFactory.Build(typeof(Func<object,object>),AccessTools.PropertyGetter(type,"CurrentUnit"));
            _touchHarmony.Patch(method,prefix:new HarmonyMethod(typeof(Main),nameof(ConfirmManualEndTurn)));
        }
        static bool ConfirmManualEndTurn(object __instance)
        {
            if(!TouchInputOwned||_endTurnConfirmed)return true;
            // This is the common button / keyboard / wheel route. Automatic
            // AI and script turn completion do not call this method.
            if(!_manualPlayerTurn(__instance)||!_manualCanEnd(__instance))return true;
            if(_endTurnConfirmation!=null&&ReferenceEquals(_liveNavigation.Menu,_endTurnConfirmation))return false;
            RefreshSpatialGameContext();
            object actor=_manualCurrentUnit(__instance);long revision=SpatialGameContextRevision,turn=_touchCombatTurnRevision;
            _touchRadial.Cancel();ClearTouchRadialVariants();CloseTouchRadialVisual();ClearTouchRadialAbilityInformation();ClearTouchRadialCombatInformation(false);
            ResetOverlayTouchPointer();
            _touchA.Cancel(); _touchB.Cancel(); _touchNavVertical.Clear(); _touchNavHorizontal.Clear();
            _endTurnConfirmation=new OverlayMenu("End the turn",new[]{
                new OverlayOption{Label="Cancel",Command=OverlayCommand.Close},
                new OverlayOption{Label="Confirm",ActionLabel="confirm",Action=()=>{
                HideLiveOverlay();
                if(!TouchInputOwned||revision!=SpatialGameContextRevision||turn!=_touchCombatTurnRevision||
                    !ReferenceEquals(actor,_manualCurrentUnit(__instance))||!_manualPlayerTurn(__instance)||!_manualCanEnd(__instance))return;
                _endTurnConfirmed=true;
                try{_confirmedNativeEndTurn(__instance);}finally{_endTurnConfirmed=false;}
            }}},modal:true);
            _liveNavigation.OpenMenu(_endTurnConfirmation);_liveNextTextUpdate=0;return false;
        }
    }
}
