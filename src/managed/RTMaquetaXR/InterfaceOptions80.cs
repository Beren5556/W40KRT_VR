using System;
using UnityEngine;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static OverlayOption FindLayoutOption80(OverlayMenu menu,string key)
        {
            foreach(var option in menu.Options)
            {
                if(option.LabelKey==key)return option;
                if(option.Menu!=null){var found=FindLayoutOption80(option.Menu,key);if(found!=null)return found;}
            }
            return null;
        }
        static OverlayOption ShareLayoutOption80(OverlayMenu menu,string key,string label=null)
        {
            var option=FindLayoutOption80(menu,key);
            if(option==null)throw new InvalidOperationException("Missing layout option: "+key);
            if(label!=null)
            {
                option.LabelProvider=()=>ModLocalization.Text(label);
                if(option.Menu!=null)option.Menu.TitleProvider=option.LabelProvider;
            }
            return option;
        }
        static OverlayOption FollowRecenterOption80()=>ImageToggle("Automatic follow camera recentering",
            "Off follows right-stick movement while preserving camera height, angle and zoom. On restores automatic framing from 0.9.79. Manual framing and physical head tracking remain available.",
            ()=>_cfg.touchFollowRecenter,SetFollowRecenter80);
        static OverlayOption InterfacePlacementOptions80(OverlayMenu advanced)
        {
            var menu=ImageGroup("Mod menu (F1)","Move this menu and its full help pages independently of the game panels. Placement is fitted to both eyes without replacing your saved offsets.",
                ImageValue("Mod menu horizontal position","Move the mod menu left or right. Zero requests its original horizontal position. The visible area can limit the applied position.",
                    ()=>(_cfg.modMenuOffsetX*100).ToString("0",ModLocalization.Culture)+"%",d=>{_cfg.modMenuOffsetX=PanelFit80.Offset(_cfg.modMenuOffsetX+d*.01f);MarkSettingsDirty();},()=> (_cfg.modMenuOffsetX+.4f)/.8f),
                ImageValue("Mod menu height","Move the mod menu up or down. Zero requests its original height. The visible area can limit the applied position.",
                    ()=>(_cfg.modMenuOffsetY*100).ToString("0",ModLocalization.Culture)+"%",d=>{_cfg.modMenuOffsetY=PanelFit80.Offset(_cfg.modMenuOffsetY+d*.01f);MarkSettingsDirty();},()=> (_cfg.modMenuOffsetY+.4f)/.8f),
                new OverlayOption{Label="Menu placement",Description="Your requested offsets are preserved when fitting the panel inside both eyes.",Value=()=>ModLocalization.Text(_livePlacementLimited80?"Adjusted to visible area":"Requested position")});
            var group=ImageGroup("Interface position and size","Adjust HUD, management windows, maps, control hints and the mod menu independently. Changes are saved with your configuration.",
                ImageGroup("Regular HUD","Position and scale the usual game interface.",
                    ShareLayoutOption80(advanced,"Panel distance","HUD distance"),ShareLayoutOption80(advanced,"Whole panel size","HUD panel size"),
                    ShareLayoutOption80(advanced,"HUD aspect ratio"),ShareLayoutOption80(advanced,"HUD horizontal position"),ShareLayoutOption80(advanced,"HUD vertical position"),
                    ShareLayoutOption80(advanced,"Text and button size"),ShareLayoutOption80(advanced,"HUD presets")),
                ImageGroup("Management windows","Position inventory and other management windows independently of the regular HUD.",
                    ShareLayoutOption80(advanced,"Management window distance","Window distance"),ShareLayoutOption80(advanced,"Management window size","Window size"),
                    ShareLayoutOption80(advanced,"Management window proportions","Window aspect ratio"),ShareLayoutOption80(advanced,"Management horizontal position","Window horizontal position"),
                    ShareLayoutOption80(advanced,"Management vertical position","Window vertical position"),ShareLayoutOption80(advanced,"Service window text size","Management text size")),
                ShareLayoutOption80(advanced,"Galactic map panel","Star map (routes)"),ShareLayoutOption80(advanced,"Star-system map panel","System map (planets)"),
                ShareLayoutOption80(advanced,"Control hint placement"),menu,
                new OverlayOption{Label="Restore factory placement",Description="Restore the approved interface layout only. Saved Custom and layout presets, world camera, quality and runtime remain unchanged.",
                    Menu=ConfirmationMenu("Restore factory placement","Replace the current interface placement with the factory layout?",()=>{ApprovedLayout80.CopyTo(_cfg,false);ApplyHudPresetPlacement();_liveNavigation.OpenMenu(_qualityRoot);})});
            // One instance for the main entry and Advanced; no duplicated settings.
            FindLayoutOption80(advanced,"Interface").Menu.Options.Insert(0,group);
            return group;
        }
    }
}
