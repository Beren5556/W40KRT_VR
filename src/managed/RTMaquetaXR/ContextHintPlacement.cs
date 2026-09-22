using UnityEngine;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static OverlayOption ContextHintPlacementOptions()=>ImageGroup("Control hint placement",
            "Position and scale the transparent control reminders. These settings affect only the mod hints, including combat, and are saved with your configuration.",
            ImageValue("Hint height","Move the control hints down or up in your view.",()=>_cfg.touchHintY.ToString("0.00",ModLocalization.Culture),d=>{
                _cfg.touchHintY=Mathf.Clamp(_cfg.touchHintY+d*.025f,-.85f,.5f);MarkSettingsDirty();},()=>(_cfg.touchHintY+.85f)/1.35f),
            ImageValue("Hint horizontal position","Move the control hints left or right.",()=>_cfg.touchHintX.ToString("0.00",ModLocalization.Culture),d=>{
                _cfg.touchHintX=Mathf.Clamp(_cfg.touchHintX+d*.025f,-.75f,.75f);MarkSettingsDirty();},()=>(_cfg.touchHintX+.75f)/1.5f),
            ImageValue("Hint size","Scale the icons and short labels together.",()=>(_cfg.touchHintSize*100).ToString("0",ModLocalization.Culture)+"%",d=>{
                _cfg.touchHintSize=Mathf.Clamp(_cfg.touchHintSize+d*.05f,.4f,1.5f);MarkSettingsDirty();},()=>(_cfg.touchHintSize-.4f)/1.1f));
    }
}
