using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
namespace RTMaquetaXR
{
    public sealed class NavigationMapPreset
    {
        public int Format=1;
        public float Width=.82f, Distance=.5f, Aspect, X, Y, Zoom=.5f;
        internal static NavigationMapPreset Capture(Main.Settings c,bool system) => system?
            new NavigationMapPreset{Width=c.mapSystemWidth,Distance=c.mapSystemDistance,Aspect=c.mapSystemAspect,X=c.mapSystemOffsetX,Y=c.mapSystemOffsetY,Zoom=c.mapSystemZoom}:
            new NavigationMapPreset{Width=c.mapGlobalWidth,Distance=c.mapGlobalDistance,Aspect=c.mapGlobalAspect,X=c.mapGlobalOffsetX,Y=c.mapGlobalOffsetY,Zoom=c.mapGlobalZoom};
        internal void Apply(Main.Settings c,bool system)
        {
            if(Format!=1)throw new InvalidDataException("Unsupported navigation map preset");
            if(system){c.mapSystemWidth=Width;c.mapSystemDistance=Distance;c.mapSystemAspect=Aspect;c.mapSystemOffsetX=X;c.mapSystemOffsetY=Y;c.mapSystemZoom=Zoom;}
            else{c.mapGlobalWidth=Width;c.mapGlobalDistance=Distance;c.mapGlobalAspect=Aspect;c.mapGlobalOffsetX=X;c.mapGlobalOffsetY=Y;c.mapGlobalZoom=Zoom;}
            SpatialPanelSettings.Normalize(c);Release48Settings.Normalize(c);
        }
    }
    public static partial class Main
    {
        static string NavigationPresetPath(bool system)=>Path.Combine(Path.GetDirectoryName(_settingsPath),system?"star_system_map_preset.json":"galactic_map_preset.json");
    }
}
