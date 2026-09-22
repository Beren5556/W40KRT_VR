using System;
using UnityEngine;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static OverlayMenu ConfirmationMenu(string title,string warning,Action confirmed)
        {
            var menu=new OverlayMenu(title,new[]{
                new OverlayOption {Label="Cancel",Description=warning,Command=OverlayCommand.Back},
                new OverlayOption {Label="Confirm",Description=warning,Action=confirmed,ActionLabel="confirm"}
            });return menu;
        }
        static OverlayOption QualityPresetOption() => ImageValue("Quality preset",
            "Choose a complete render profile. Window placement and controls stay as configured. Custom restores your saved complete configuration. DLSS/DLAA use NVIDIA hardware; other GPUs use TAA.",
            ()=>ModLocalization.Text(QualityPresetPolicy.Names[ActiveQualityPreset]),d=>SelectQualityPreset(CycleImageValue(ActiveQualityPreset,d,4)));
        static OverlayOption SaveCustomOption()=>new OverlayOption{Label="Save current as Custom",
            Description="Save all current mod settings, including quality, panels, maps and controls. Replaces your previously saved Custom configuration.",
            Action=SaveCustomSettings,ActionLabel="save",ActionButton=true,
            Value=()=>Time.unscaledTime<_customSavedUntil?ModLocalization.Text("Configuration saved"):
                _customSavedAt==null?"":ModLocalization.Format("Custom saved at {0}",_customSavedAt)};
        static OverlayOption ResetSettingsOption()=>new OverlayOption{Label="Reset settings",Description="Restore Performance and the approved default layout. Your personal configuration will be lost. Confirmation is required.",
            Menu=ConfirmationMenu("Reset settings","All personal settings will be lost. Restore Performance and the approved defaults?",RestoreAllSettings)};
        static OverlayOption AdvancedWarningOption(OverlayMenu advanced)
        {
            var warning=new OverlayMenu("Advanced settings warning",new[]{
                new OverlayOption{Label="Cancel",Description="Changing advanced settings can misalign the interface or disrupt image quality, controls and performance.",Command=OverlayCommand.Back},
                new OverlayOption{Label="Continue to advanced settings",Description="Changing advanced settings can misalign the interface or disrupt image quality, controls and performance.",Menu=advanced}
            });
            return new OverlayOption{Label="Advanced settings",Description="Changing advanced settings can misalign the interface or disrupt image quality, controls and performance.",Menu=warning};
        }
        static OverlayOption ExplorationZoomLimitOption()=>ImageValue("Limit exploration zoom",
            "On keeps the predefined tabletop zoom range. Turning this off allows free zoom and can increase rendering cost.",
            ()=>ModLocalization.Text(_cfg.limitExplorationZoom?"On":"Off"),d=>{
                if(!_cfg.limitExplorationZoom){SetExplorationZoomLimit(true);return;}
                _liveNavigation.OpenMenu(ConfirmationMenu("Free zoom warning","Free zoom can expose much more scenery and reduce performance. Remove the zoom limit?",()=>{SetExplorationZoomLimit(false);_liveNavigation.OpenMenu(_qualityRoot);}));
            });
    }
}
