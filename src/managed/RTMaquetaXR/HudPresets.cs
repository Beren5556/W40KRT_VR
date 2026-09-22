using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace RTMaquetaXR
{
    public sealed class HudPreset
    {
        public int Format = 4;
        public float MapGlobalZoom=.5f,MapSystemZoom=.5f;
        public float MenuDistance = ApprovedUserDefaults.MenuDistance, MenuWidth = ApprovedUserDefaults.MenuWidth, MenuAspect = ApprovedUserDefaults.MenuAspect, MenuOffsetX = ApprovedUserDefaults.MenuX, MenuOffsetY = ApprovedUserDefaults.MenuY, MenuTextScale = ApprovedUserDefaults.MenuText;
        public float MapGlobalWidth=.82f,MapGlobalDistance=.5f,MapGlobalAspect=0,MapGlobalOffsetX=0,MapGlobalOffsetY=0;
        public float MapSystemWidth=.82f,MapSystemDistance=.5f,MapSystemAspect=0,MapSystemOffsetX=0,MapSystemOffsetY=0;
        public float Distance = ApprovedUserDefaults.Distance, Width = ApprovedUserDefaults.Width, Aspect = ApprovedUserDefaults.Aspect, OffsetX = ApprovedUserDefaults.OffsetX, OffsetY = ApprovedUserDefaults.OffsetY;
        public float ElementScale = 1f;
        public int Raster = ApprovedUserDefaults.Raster;
        public bool FullResolution = true, Visible = true, WorldOvertips = true;
        public float MarkerBoost = 2f;
        public Dictionary<string, float[]> Positions = new Dictionary<string, float[]>();

        internal static HudPreset Capture(Main.Settings settings)
        {
            var value = new HudPreset { Distance=settings.uiDistance, Width=settings.uiWidth, Aspect=settings.uiAspect,
                OffsetX=settings.uiOffsetX, OffsetY=settings.uiOffsetY, Raster=settings.uiRasterMode,
                FullResolution=settings.uiFullResolution, Visible=settings.uiEnabled, WorldOvertips=settings.worldOvertips,
                MarkerBoost=settings.markerBoostCap, ElementScale=settings.uiElementScale,
                MenuDistance=settings.uiMenuDistance,MenuWidth=settings.uiMenuWidth,MenuAspect=settings.uiMenuAspect,
                MenuOffsetX=settings.uiMenuOffsetX,MenuOffsetY=settings.uiMenuOffsetY,MenuTextScale=settings.uiMenuScale,
                MapGlobalWidth=settings.mapGlobalWidth,MapGlobalDistance=settings.mapGlobalDistance,MapGlobalAspect=settings.mapGlobalAspect,
                MapGlobalOffsetX=settings.mapGlobalOffsetX,MapGlobalOffsetY=settings.mapGlobalOffsetY,
                MapSystemWidth=settings.mapSystemWidth,MapSystemDistance=settings.mapSystemDistance,MapSystemAspect=settings.mapSystemAspect,
                MapSystemOffsetX=settings.mapSystemOffsetX,MapSystemOffsetY=settings.mapSystemOffsetY,
                MapGlobalZoom=settings.mapGlobalZoom,MapSystemZoom=settings.mapSystemZoom };
            foreach(var pair in settings.uiPos) value.Positions[pair.Key]=new[]{pair.Value.x,pair.Value.y};
            return value;
        }
        static float Safe(float value,float fallback,float minimum,float maximum) =>
            float.IsNaN(value)||float.IsInfinity(value)?fallback:Math.Max(minimum,Math.Min(maximum,value));
        internal void Apply(Main.Settings settings)
        {
            if(Format<1 || Format>4) throw new InvalidDataException("Unsupported HUD preset version");
            if(Format>=4){settings.mapGlobalZoom=Safe(MapGlobalZoom,.5f,0,1);settings.mapSystemZoom=Safe(MapSystemZoom,.5f,0,1);}
            if(Format>=3)
            {
                settings.mapGlobalWidth=MapGlobalWidth;settings.mapGlobalDistance=MapGlobalDistance;settings.mapGlobalAspect=MapGlobalAspect;
                settings.mapGlobalOffsetX=MapGlobalOffsetX;settings.mapGlobalOffsetY=MapGlobalOffsetY;
                settings.mapSystemWidth=MapSystemWidth;settings.mapSystemDistance=MapSystemDistance;settings.mapSystemAspect=MapSystemAspect;
                settings.mapSystemOffsetX=MapSystemOffsetX;settings.mapSystemOffsetY=MapSystemOffsetY;
                SpatialPanelSettings.Normalize(settings);
            }
            if(Format>=2)
            {
                settings.uiMenuDistance=Safe(MenuDistance,ApprovedUserDefaults.MenuDistance,.5f,3); settings.uiMenuWidth=Safe(MenuWidth,ApprovedUserDefaults.MenuWidth,.45f,1);
                settings.uiMenuAspect=MenuAspect<=0?0:Safe(MenuAspect,ApprovedUserDefaults.MenuAspect,.8f,2.4f);
                settings.uiMenuOffsetX=Safe(MenuOffsetX,ApprovedUserDefaults.MenuX,-.65f,.65f); settings.uiMenuOffsetY=Safe(MenuOffsetY,ApprovedUserDefaults.MenuY,-.65f,.65f);
                settings.uiMenuScale=Safe(MenuTextScale,ApprovedUserDefaults.MenuText,.65f,1.5f);
            }
            settings.uiDistance=Safe(Distance,ApprovedUserDefaults.Distance,.5f,10); settings.uiWidth=Safe(Width,ApprovedUserDefaults.Width,.5f,1.8f);
            settings.uiElementScale=HudPanelLayout.ClampElementScale(ElementScale);
            settings.uiAspect=Aspect<=0?0:Safe(Aspect,ApprovedUserDefaults.Aspect,.8f,2.4f);
            settings.uiOffsetX=Safe(OffsetX,ApprovedUserDefaults.OffsetX,-.65f,.65f); settings.uiOffsetY=Safe(OffsetY,ApprovedUserDefaults.OffsetY,-.65f,.65f);
            settings.uiRasterMode=HudRasterPolicy.Normalize(Raster); settings.uiFullResolution=FullResolution;
            settings.uiEnabled=Visible; settings.worldOvertips=WorldOvertips; settings.markerBoostCap=Safe(MarkerBoost,2,1,4);
            settings.uiPos.Clear();
            if(Positions!=null) foreach(var pair in Positions)
                if(!string.IsNullOrEmpty(pair.Key)&&pair.Value!=null&&pair.Value.Length==2)
                    settings.uiPos[pair.Key]=new Vector2(Safe(pair.Value[0],.5f,-.5f,1.5f),Safe(pair.Value[1],.5f,-.5f,1.5f));
        }
    }
    public static partial class Main
    {
        static string HudPresetPath(int slot)
        {
            if(slot<1||slot>3) throw new ArgumentOutOfRangeException(nameof(slot));
            return Path.Combine(Path.GetDirectoryName(_settingsPath),"hud_preset_"+slot+".json");
        }
        static bool HudPresetExists(int slot) => File.Exists(HudPresetPath(slot));
        static void SaveHudPreset(int slot)
        {
            File.WriteAllText(HudPresetPath(slot),JsonConvert.SerializeObject(HudPreset.Capture(_cfg),Formatting.Indented));
            _liveMessage=ModLocalization.Format("HUD saved to preset {0}",slot); _liveNextTextUpdate=0;
        }
        static void LoadHudPreset(int slot)
        {
            var value=slot==0?new HudPreset():JsonConvert.DeserializeObject<HudPreset>(File.ReadAllText(HudPresetPath(slot)));
            if(value==null) throw new InvalidDataException("Empty HUD preset");
            value.Apply(_cfg); ApplyHudPresetPlacement();
            _liveMessage=slot==0?ModLocalization.Text("Default HUD restored"):ModLocalization.Format("HUD preset {0} applied",slot);
            _liveNextTextUpdate=0;
        }
        static void ApplyHudPresetPlacement()
        {
            if (_worldOvertips != _cfg.worldOvertips)
            {
                _worldOvertips = _cfg.worldOvertips; _woFrame = 0;
                if (!_worldOvertips) RestoreOvertips();
            }
            foreach(var saved in _savedCanvases)
                if(saved.canvas!=null) saved.hudViewportRevision=0;
            _logUiProjNow=true; MarkSettingsDirty(); ResetPerformanceWindow();
        }
        static OverlayOption HudPresetMenu() => ImageGroup("HUD presets",
            "Restore the default interface or save three layouts for HUD, management and both space maps. Eye resolution, DLSS, camera and controls stay unchanged.",
            new OverlayOption { Label="Restore defaults", Description="Restore the approved HUD and management layout, including panel size, distance, proportions, position, interface resolution and markers. Other mod settings remain unchanged.",
                Value=()=>ModLocalization.Text("Default HUD layout"),Action=()=>LoadHudPreset(0),ActionLabel="restore" },
            HudPresetSlot(1),HudPresetSlot(2),HudPresetSlot(3));
        static OverlayOption HudPresetSlot(int slot)
        {
            var group = ImageGroup("HUD preset","Save or load the interface settings in this slot.",
            new OverlayOption { Label="Load",Description="Load saved HUD, management and space-map layouts. Other mod settings are preserved. Older presets keep any map or management settings they did not store.",
                Value=()=>ModLocalization.Text(HudPresetExists(slot)?"Saved":"Empty"),Enabled=()=>HudPresetExists(slot),Action=()=>LoadHudPreset(slot),ActionLabel="load" },
            new OverlayOption { Label="Save current layout",Description="Save the current HUD in this slot. Replaces the existing preset if the slot is occupied.",
                Value=()=>ModLocalization.Format(HudPresetExists(slot)?"Replace preset {0}":"Save preset {0}",slot),Action=()=>SaveHudPreset(slot),ActionLabel="save" });
            group.LabelProvider=()=>ModLocalization.Format("HUD preset {0}",slot);
            group.Menu.TitleProvider=group.LabelProvider;
            return group;
        }
    }
}
