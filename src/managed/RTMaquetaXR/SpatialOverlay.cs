using System;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static OverlayOption SpatialInterfaceMenu() => ImageGroup("Space", "Galactic and star-system maps have independent panels. Space battles use the 3D tabletop and native tactical controls.",
            ImageToggle("Show space combat panels","Show the original ship panels. Hidden by default: lower, left and right panels are replaced by the wheels. Opened windows and essential popups remain available.",()=>_cfg.spaceHudVisible,v=>{_cfg.spaceHudVisible=v;_pcHudNext=0;MarkSettingsDirty();}),
            ImageToggle("Custom space background","Show the IC 2631 nebula below the Space combat board. Image: ESO, adapted under CC BY 4.0. Off restores the original game background. Ship lighting, board controls and grids remain unchanged.",()=>_cfg.customSpaceBackground,v=>{_cfg.customSpaceBackground=v;RestoreSpaceBackdrop();MarkSettingsDirty();}),
            NavigationPanelMenu("Galactic map panel",false,()=>GalacticMapPanel,SetGalacticMapWidth,SetGalacticMapDistance,CycleGalacticMapAspect,SetGalacticMapOffsetX,SetGalacticMapOffsetY),
            NavigationPanelMenu("Star-system map panel",true,()=>StarSystemMapPanel,SetStarSystemMapWidth,SetStarSystemMapDistance,CycleStarSystemMapAspect,SetStarSystemMapOffsetX,SetStarSystemMapOffsetY),
            new OverlayOption {Label="Frame active ship",Description="Close the overlay and release the controls to frame the active ship. This does not select it or issue an order. Available during a space battle.",
                Value=()=>ModLocalization.Text("Space battle"),Enabled=()=>InSpaceCombat,Action=RequestTouchSpaceCombatFocus,ActionLabel="recenter"});
        static OverlayOption NavigationPanelMenu(string name,bool system,Func<NavigationMapPanel> panel,Action<float> width,
            Action<float> distance,Action<int> aspect,Action<float> horizontal,Action<float> vertical) =>
            ImageGroup(name,"Adjust this map panel only. Native pan, zoom, routes and travel controls remain inside it. HUD presets also save both map layouts.",
                NavigationMapZoomOption(system),NavigationMapPresetOption(system),
                ImageValue("Map panel size","Change the visible size of this entire map panel. This does not zoom the map content or change dialogue and management windows.",
                    ()=>(panel().Width*100).ToString("0.#",ModLocalization.Culture)+"%",d=>width(panel().Width+d*.025f),()=> (panel().Width-.45f)/.55f),
                ImageValue("Map panel distance","Move this map panel nearer or farther. The pointer uses the displayed surface; the other map has its own distance.",
                    ()=>panel().Distance.ToString("0.00",ModLocalization.Culture)+" m",d=>distance(panel().Distance+d*.05f),()=>(panel().Distance-.5f)/2.5f),
                ImageValue("Map panel proportions","Original preserves the game's window ratio. Manual values change the panel width-to-height ratio. They do not alter the native map or its travel rules.",
                    ()=>panel().Aspect<=0?ModLocalization.Text("Original"):panel().Aspect.ToString("0.0",ModLocalization.Culture)+":1",aspect),
                ImageValue("Map panel horizontal position","Move this map panel left or right. Extreme offsets may put its edges outside the view. The pointer follows the new position.",
                    ()=>(panel().OffsetX*100).ToString("0.#",ModLocalization.Culture)+"%",d=>horizontal(panel().OffsetX+d*.025f),()=>(panel().OffsetX+.65f)/1.3f),
                ImageValue("Map panel vertical position","Move this map panel up or down. Negative lowers it; positive raises it. Management and dialogue placement are separate.",
                    ()=>(panel().OffsetY*100).ToString("0.#",ModLocalization.Culture)+"%",d=>vertical(panel().OffsetY+d*.025f),()=>(panel().OffsetY+.65f)/1.3f));
    }
}
