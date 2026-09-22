using System;
using System.Globalization;
namespace RTMaquetaXR
{
    internal static class Release48Settings
    {
        internal static void Normalize(Main.Settings c)
        {
            c.mapGlobalZoom=SpatialPanelSettings.Value(c.mapGlobalZoom,.5f,0,1);c.mapSystemZoom=SpatialPanelSettings.Value(c.mapSystemZoom,.5f,0,1);
            c.touchHintX=SpatialPanelSettings.Value(c.touchHintX,0,-.75f,.75f);c.touchHintY=SpatialPanelSettings.Value(c.touchHintY,-.46f,-.85f,.5f);
            c.touchHintSize=SpatialPanelSettings.Value(c.touchHintSize,.85f,.4f,1.5f);
        }
        internal static float WorldScale(float value, bool limited) =>
            SpatialPanelSettings.Value(value, 10, limited ? 6 : .1f, limited ? 14 : 10000);
        internal static bool TryParse(Main.Settings c, string key, string text)
        {
            bool b; float f;
            switch (key)
            {
                case "mapGlobalZoom": if(float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out f))c.mapGlobalZoom=SpatialPanelSettings.Value(f,.5f,0,1);return true;
                case "mapSystemZoom": if(float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out f))c.mapSystemZoom=SpatialPanelSettings.Value(f,.5f,0,1);return true;
                case "limitExplorationZoom": if(bool.TryParse(text,out b))c.limitExplorationZoom=b;return true;
                case "spaceHudVisible": if(bool.TryParse(text,out b))c.spaceHudVisible=b;return true;
                case "customSpaceBackground": if(bool.TryParse(text,out b))c.customSpaceBackground=b;return true;
                case "qualityPreset": if(int.TryParse(text,out int i))c.qualityPreset=Math.Max(0,Math.Min(3,i));return true;
                case "controlsLayoutRevision": if(int.TryParse(text,out int revision))c.controlsLayoutRevision=Math.Max(0,revision);return true;
                case "touchHintX": if(float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out f))c.touchHintX=SpatialPanelSettings.Value(f,0,-.75f,.75f);return true;
                case "touchHintY": if(float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out f))c.touchHintY=SpatialPanelSettings.Value(f,-.46f,-.85f,.5f);return true;
                case "touchHintSize": if(float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out f))c.touchHintSize=SpatialPanelSettings.Value(f,.85f,.4f,1.5f);return true;
                default:return false;
            }
        }
        internal static string Serialize(Main.Settings c) => "limitExplorationZoom="+c.limitExplorationZoom+"\nspaceHudVisible="+c.spaceHudVisible+"\ncustomSpaceBackground="+c.customSpaceBackground+
            "\nqualityPreset="+c.qualityPreset+"\ncontrolsLayoutRevision="+c.controlsLayoutRevision+"\ntouchHintX="+c.touchHintX.ToString("R",CultureInfo.InvariantCulture)+
            "\ntouchHintY="+c.touchHintY.ToString("R",CultureInfo.InvariantCulture)+"\ntouchHintSize="+c.touchHintSize.ToString("R",CultureInfo.InvariantCulture)+
            "\nmapGlobalZoom="+c.mapGlobalZoom.ToString("R",CultureInfo.InvariantCulture)+"\nmapSystemZoom="+c.mapSystemZoom.ToString("R",CultureInfo.InvariantCulture)+"\n";
    }
}
