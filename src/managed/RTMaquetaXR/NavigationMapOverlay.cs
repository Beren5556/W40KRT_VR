using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static OverlayOption NavigationMapZoomOption(bool system)=>ImageValue("Map content zoom",
            "Set the native camera zoom inside this map. Left stick up/down also changes it. This is independent of panel size and of the other map.",
            ()=>((system?_cfg.mapSystemZoom:_cfg.mapGlobalZoom)*100).ToString("0",ModLocalization.Culture)+"%",d=>{
                float zoom=Mathf.Clamp01((system?_cfg.mapSystemZoom:_cfg.mapGlobalZoom)+d*.05f);
                if(system)_cfg.mapSystemZoom=zoom;else _cfg.mapGlobalZoom=zoom;
                RequestNavigationZoom();MarkSettingsDirty();},()=>system?_cfg.mapSystemZoom:_cfg.mapGlobalZoom);
        static OverlayOption NavigationMapPresetOption(bool system)=>ImageGroup("This map preset",
            "Save or restore only this map's size, position, proportions, distance and content zoom. The other map and the rest of the interface are preserved.",
            new OverlayOption{Label="Save current layout",ActionLabel="save",Description="Replace the saved layout for this map only.",Action=()=>{
                File.WriteAllText(NavigationPresetPath(system),JsonConvert.SerializeObject(NavigationMapPreset.Capture(_cfg,system),Formatting.Indented));
                _liveMessage=ModLocalization.Text("Map preset saved.");}},
            new OverlayOption{Label="Load",ActionLabel="load",Description="Restore the saved layout for this map only.",Enabled=()=>File.Exists(NavigationPresetPath(system)),Action=()=>{
                var saved=JsonConvert.DeserializeObject<NavigationMapPreset>(File.ReadAllText(NavigationPresetPath(system)));
                if(saved==null)throw new InvalidDataException("Empty navigation map preset");
                saved.Apply(_cfg,system);RequestNavigationZoom();ApplyHudPresetPlacement();
                _liveMessage=ModLocalization.Text("Map preset restored.");}});
    }
}
