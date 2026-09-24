using System;
using System.Collections.Generic;
using System.Globalization;
namespace RTMaquetaXR
{
    // Shared by default Settings, the layout reset and the package generator.
    // No player file, rendering preset, runtime or diagnostic preference is copied.
    public static class ApprovedLayout80
    {
        public static IDictionary<string,object> Values() => new Dictionary<string,object>(StringComparer.Ordinal) {
            {"worldScale",ApprovedUserDefaults.WorldScale},{"ipdScale",1f},
            {"uiDistance",ApprovedUserDefaults.Distance},{"uiWidth",ApprovedUserDefaults.Width},
            {"uiAspect",ApprovedUserDefaults.Aspect},{"uiOffsetX",ApprovedUserDefaults.OffsetX},{"uiOffsetY",ApprovedUserDefaults.OffsetY},{"uiElementScale",1f},
            {"uiMenuDistance",ApprovedUserDefaults.MenuDistance},{"uiMenuWidth",ApprovedUserDefaults.MenuWidth},{"uiMenuAspect",ApprovedUserDefaults.MenuAspect},
            {"uiMenuOffsetX",ApprovedUserDefaults.MenuX},{"uiMenuOffsetY",ApprovedUserDefaults.MenuY},{"uiMenuScale",ApprovedUserDefaults.MenuText},
            {"mapGlobalWidth",.82f},{"mapGlobalDistance",.5f},{"mapGlobalAspect",0f},{"mapGlobalOffsetX",0f},{"mapGlobalOffsetY",0f},{"mapGlobalZoom",ApprovedUserDefaults.MapZoom},
            {"mapSystemWidth",.82f},{"mapSystemDistance",.5f},{"mapSystemAspect",0f},{"mapSystemOffsetX",0f},{"mapSystemOffsetY",0f},{"mapSystemZoom",ApprovedUserDefaults.MapZoom},
            // The stored mod offset predates centered defaults: +.20 cancels
            // its legacy -.20 base without reinterpreting any existing file.
            {"touchHintX",0f},{"touchHintY",0f},{"touchHintSize",1f},{"modMenuOffsetX",0f},{"modMenuOffsetY",.20f},
            {"uiRasterMode",ApprovedUserDefaults.Raster},{"uiFullResolution",true}
        };
        public static void CopyTo(object settings, bool includeWorld)
        {
            if(settings==null)throw new ArgumentNullException(nameof(settings));
            foreach(var pair in Values())
            {
                if(!includeWorld&&(pair.Key=="worldScale"||pair.Key=="ipdScale"))continue;
                var field=settings.GetType().GetField(pair.Key);
                if(field==null||field.FieldType!=pair.Value.GetType())throw new InvalidOperationException("Layout field mismatch: "+pair.Key);
                field.SetValue(settings,pair.Value);
            }
        }
        public static string Serialize()
        {
            var text=new System.Text.StringBuilder();
            foreach(var pair in Values())text.Append(pair.Key).Append('=').Append(pair.Value is float f?f.ToString("R",CultureInfo.InvariantCulture):Convert.ToString(pair.Value,CultureInfo.InvariantCulture)).Append('\n');
            return text.ToString();
        }
    }
}
