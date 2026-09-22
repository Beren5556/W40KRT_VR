using System;
using HarmonyLib;
using UnityEngine;
namespace RTMaquetaXR
{
    internal sealed class NavigationCameraContracts
    {
        internal Func<object> Rig;
        internal Func<object,object> Zoom,Fix;
        internal Func<object,bool> Fixed;
        internal Func<object,float> Length;
        internal Action<object> TickScroll,TickZoom;
        internal Action<object,Vector2> AddScroll;
        internal Action<object,float> SetPlayerPosition;
        internal Type StarController;
        internal static NavigationCameraContracts Create(Func<string,Type> type)
        {
            var c=new NavigationCameraContracts();var rig=type("Kingmaker.View.CameraRig");var zoom=type("Kingmaker.View.CameraZoom");
            c.Rig=(Func<object>)TouchSelectionCallFactory.Build(typeof(Func<object>),AccessTools.PropertyGetter(rig,"Instance"));
            c.Zoom=PcUiPath.Getter(PcUiPath.Property(rig,"CameraZoom"));
            var fix=PcUiPath.Property(rig,"FixCamera");c.Fix=PcUiPath.Getter(fix);
            c.Fixed=(Func<object,bool>)TouchSelectionCallFactory.Build(typeof(Func<object,bool>),AccessTools.Method(fix.PropertyType,"op_Implicit",new[]{fix.PropertyType}));
            c.Length=(Func<object,float>)TouchSelectionCallFactory.Build(typeof(Func<object,float>),AccessTools.PropertyGetter(zoom,"ZoomLength"));
            c.SetPlayerPosition=(Action<object,float>)TouchSelectionCallFactory.Build(typeof(Action<object,float>),AccessTools.PropertySetter(zoom,"PlayerScrollPosition"));
            c.AddScroll=(Action<object,Vector2>)TouchSelectionCallFactory.Build(typeof(Action<object,Vector2>),AccessTools.Method(rig,"AddScroll",new[]{typeof(Vector2)}));
            c.TickScroll=(Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>),AccessTools.Method(rig,"TickScroll",Type.EmptyTypes));
            c.TickZoom=(Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>),AccessTools.Method(zoom,"TickZoom",Type.EmptyTypes));
            c.StarController=type("Kingmaker.Controllers.GlobalMap.StarSystemCameraController");return c;
        }
    }
    public static partial class Main
    {
        static NavigationCameraContracts _navigationCamera;
        static int _navigationPanFrame=-1,_navigationZoomFrame=-1,_navigationSystemFrame=-1;
        static bool _navigationSticksArmed, _navigationZoomArmed;
        static long _navigationZoomRevision=-1;
        static float _navigationAppliedZoom=-1;
        static void RequestNavigationZoom(){_navigationZoomRevision=-1;_navigationAppliedZoom=-1;}
        static bool NavigationControlsReady
        {
            get
            {
                EnsureTouchSample();
                bool allowed=NavigationMapPolicy.Interactive(SpatialGameMode,InNavigationMap,_modeFlat,true,
                    TouchGameInputAllowed,NativeUiOnlyPresentation(),TouchMenuWindowVisible,TouchMenuBlockingModal(),NativeTutorialInputBlocked);
                if(!allowed){_navigationSticksArmed=_navigationZoomArmed=false;return false;}
                // Each axis owner rearms independently after a wheel/window.
                // A held LEFT zoom axis must not leave RIGHT pan locked (and
                // vice versa); stale deflections still require neutral first.
                if(NavigationMapPolicy.Neutral(_touchSample.right.stickX,_touchSample.right.stickY))_navigationSticksArmed=true;
                if(NavigationMapPolicy.Neutral(_touchSample.left.stickX,_touchSample.left.stickY))_navigationZoomArmed=true;
                return !_touchPrimary.Held&&!_touchSecondary.Held;
            }
        }
        static void InstallNavigationCameraControls()
        {
            _navigationCamera=NavigationCameraContracts.Create(AccessTools.TypeByName);
            _touchNavigationMapHarmony.Patch(AccessTools.Method(AccessTools.TypeByName("Kingmaker.View.CameraRig"),"TickScroll",Type.EmptyTypes),
                prefix:new HarmonyMethod(typeof(Main),nameof(NavigationPanBefore)));
            _touchNavigationMapHarmony.Patch(AccessTools.Method(AccessTools.TypeByName("Kingmaker.View.CameraZoom"),"TickZoom",Type.EmptyTypes),
                prefix:new HarmonyMethod(typeof(Main),nameof(NavigationZoomBefore)));
            _touchNavigationMapHarmony.Patch(AccessTools.PropertyGetter(AccessTools.TypeByName("Kingmaker.View.CameraZoom"),"ZoomLength"),
                postfix:new HarmonyMethod(typeof(Main),nameof(NavigationStarZoomLength)));
            _touchNavigationMapHarmony.Patch(AccessTools.PropertyGetter(AccessTools.TypeByName("Kingmaker.View.CameraZoom"),"PhysicalZoomMin"),
                postfix:new HarmonyMethod(typeof(Main),nameof(NavigationWarpPhysicalFar)));
            _touchNavigationMapHarmony.Patch(AccessTools.PropertyGetter(AccessTools.TypeByName("Kingmaker.View.CameraZoom"),"FovMax"),
                postfix:new HarmonyMethod(typeof(Main),nameof(NavigationWarpFovFar)));
            // Native CameraController.Tick returns before StarSystem cameras.
            // Supply only its missing scroll/zoom ticks, preserving lock checks.
            _touchNavigationMapHarmony.Patch(AccessTools.Method(AccessTools.TypeByName("Kingmaker.Controllers.Rest.CameraController"),"Tick",Type.EmptyTypes),
                postfix:new HarmonyMethod(typeof(Main),nameof(NavigationSystemTick)));
        }
        static void NavigationStarZoomLength(ref float __result)
        {
            // Extend only the star-system camera's native travel range. The
            // game's smoothing/limits still operate on one consistent length.
            if(TouchInputOwned && InNavigationMap && TouchPointerMath.Finite(__result) && __result>0) __result *= InGalacticMap ? 4f : 3f;
        }
        static void NavigationWarpPhysicalFar(ref float __result)
        {
            if(TouchInputOwned && InGalacticMap) __result=NavigationMapPolicy.WarpPhysicalFar(__result);
        }
        static void NavigationWarpFovFar(ref float __result)
        {
            if(TouchInputOwned && InGalacticMap) __result=NavigationMapPolicy.WarpFovFar(__result);
        }
        static void NavigationSystemTick(object __instance)
        {
            var c=_navigationCamera;
            if(!TouchInputOwned||!InStarSystemMap||c==null||!c.StarController.IsInstanceOfType(__instance)||_navigationSystemFrame==Time.frameCount||!NavigationControlsReady)return;
            _navigationSystemFrame=Time.frameCount;
            var rig=c.Rig();if(rig==null||c.Fixed(c.Fix(rig)))return;
            c.TickScroll(rig);var zoom=c.Zoom(rig);if(zoom!=null)c.TickZoom(zoom);
        }
        static void NavigationPanBefore(object __instance)
        {
            if(!TouchInputOwned||!InNavigationMap||_navigationCamera==null||_navigationPanFrame==Time.frameCount||!NavigationControlsReady||!_navigationSticksArmed)return;
            _navigationPanFrame=Time.frameCount;
            Vector2 pan=new Vector2(MapAxis(_touchSample.right.stickX),MapAxis(_touchSample.right.stickY));
            if(pan.sqrMagnitude>0)_navigationCamera.AddScroll(__instance,pan);
        }
        static float MapAxis(float value)=>!TouchPointerMath.Finite(value)||Mathf.Abs(value)<.2f?0:Mathf.Sign(value)*Mathf.Clamp01((Mathf.Abs(value)-.2f)/.8f);
        internal static float NavigationContentZoom(bool galaxy)=>galaxy?_cfg.mapGlobalZoom:_cfg.mapSystemZoom;
        internal static void SetNavigationContentZoom(bool galaxy,float value)
        {if(galaxy)_cfg.mapGlobalZoom=Mathf.Clamp01(value);else _cfg.mapSystemZoom=Mathf.Clamp01(value);MarkSettingsDirty();}
        static void NavigationZoomBefore(object __instance)
        {
            if(!TouchInputOwned||!InNavigationMap||_navigationCamera==null||_navigationZoomFrame==Time.frameCount||!NavigationControlsReady||!_navigationZoomArmed)return;
            _navigationZoomFrame=Time.frameCount;
            var c=_navigationCamera;float length=c.Length(__instance);
            if(!TouchPointerMath.Finite(length)||length<=0)return;
            bool galaxy=InGalacticMap;float zoom=NavigationContentZoom(galaxy);
            float delta=MapAxis(_touchSample.left.stickY)*Mathf.Min(Time.unscaledDeltaTime,.05f)*.45f;
            if(delta!=0){zoom=Mathf.Clamp01(zoom+delta);SetNavigationContentZoom(galaxy,zoom);++_touchNavigationMapZoomSamples;}
            if(_navigationZoomRevision!=SpatialGameContextRevision||Mathf.Abs(zoom-_navigationAppliedZoom)>.00001f||delta!=0)
            {c.SetPlayerPosition(__instance,zoom*length);_navigationAppliedZoom=zoom;_navigationZoomRevision=SpatialGameContextRevision;}
        }
    }
}
