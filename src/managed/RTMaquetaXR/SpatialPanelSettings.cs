using System;
using System.Globalization;
using System.Text;

namespace RTMaquetaXR
{
    // Separate persisted profiles; opening a map never rewrites management/HUD.
    internal static class SpatialPanelSettings
    {
        internal static float Value(float value,float fallback,float minimum,float maximum) =>
            float.IsNaN(value)||float.IsInfinity(value)?fallback:Math.Max(minimum,Math.Min(maximum,value));
        internal static float Aspect(float value) => value<=0?0:Value(value,0,.8f,2.4f);
        internal static bool TryParse(Main.Settings cfg,string key,string text)
        {
            if(!float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out float value))return false;
            switch(key)
            {
                case "mapGlobalWidth":cfg.mapGlobalWidth=value;break;
                case "mapGlobalDistance":cfg.mapGlobalDistance=value;break;
                case "mapGlobalAspect":cfg.mapGlobalAspect=value;break;
                case "mapGlobalOffsetX":cfg.mapGlobalOffsetX=value;break;
                case "mapGlobalOffsetY":cfg.mapGlobalOffsetY=value;break;
                case "mapSystemWidth":cfg.mapSystemWidth=value;break;
                case "mapSystemDistance":cfg.mapSystemDistance=value;break;
                case "mapSystemAspect":cfg.mapSystemAspect=value;break;
                case "mapSystemOffsetX":cfg.mapSystemOffsetX=value;break;
                case "mapSystemOffsetY":cfg.mapSystemOffsetY=value;break;
                default:return false;
            }
            return true;
        }
        internal static void Normalize(Main.Settings cfg)
        {
            cfg.mapGlobalWidth=Value(cfg.mapGlobalWidth,.82f,.45f,1);
            cfg.mapGlobalDistance=Value(cfg.mapGlobalDistance,.5f,.5f,3);
            cfg.mapGlobalAspect=Aspect(cfg.mapGlobalAspect);
            cfg.mapGlobalOffsetX=Value(cfg.mapGlobalOffsetX,0,-.65f,.65f);
            cfg.mapGlobalOffsetY=Value(cfg.mapGlobalOffsetY,0,-.65f,.65f);
            cfg.mapSystemWidth=Value(cfg.mapSystemWidth,.82f,.45f,1);
            cfg.mapSystemDistance=Value(cfg.mapSystemDistance,.5f,.5f,3);
            cfg.mapSystemAspect=Aspect(cfg.mapSystemAspect);
            cfg.mapSystemOffsetX=Value(cfg.mapSystemOffsetX,0,-.65f,.65f);
            cfg.mapSystemOffsetY=Value(cfg.mapSystemOffsetY,0,-.65f,.65f);
        }
        static void Add(StringBuilder b,string key,float value) =>
            b.Append(key).Append('=').Append(value.ToString("R",CultureInfo.InvariantCulture)).Append('\n');
        internal static string Serialize(Main.Settings cfg)
        {
            var b=new StringBuilder(256);
            Add(b,"mapGlobalWidth",cfg.mapGlobalWidth);Add(b,"mapGlobalDistance",cfg.mapGlobalDistance);
            Add(b,"mapGlobalAspect",cfg.mapGlobalAspect);Add(b,"mapGlobalOffsetX",cfg.mapGlobalOffsetX);Add(b,"mapGlobalOffsetY",cfg.mapGlobalOffsetY);
            Add(b,"mapSystemWidth",cfg.mapSystemWidth);Add(b,"mapSystemDistance",cfg.mapSystemDistance);
            Add(b,"mapSystemAspect",cfg.mapSystemAspect);Add(b,"mapSystemOffsetX",cfg.mapSystemOffsetX);Add(b,"mapSystemOffsetY",cfg.mapSystemOffsetY);
            return b.ToString();
        }
    }
}
