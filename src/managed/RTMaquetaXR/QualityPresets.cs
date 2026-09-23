using System;
using System.IO;
using UnityEngine;

namespace RTMaquetaXR
{
    internal static class QualityPresetPolicy
    {
        internal static readonly string[] Names = { "Ultra performance", "Performance", "Maximum quality", "Custom" };
        static Main.Settings[] expectedProfiles;
        // These are mod render settings. Game graphics, drivers, runtimes and
        // the user's hand/window placement are never changed by quality alone.
        internal static void Apply(Main.Settings c, int preset, bool nvidia)
        {
            if(preset<0||preset>2)throw new ArgumentOutOfRangeException(nameof(preset));
            c.qualityPreset=preset;c.neuralMode=nvidia?(preset==2?2:3):0;
            c.temporalAA=true;c.disableAA=false;c.allowGameFsr=false;
            c.renderScale=preset==0?.80f:preset==2?1.15f:1f;
            c.neuralScale=preset==0?.50f:.67f;c.neuralPreset=0;
            c.neuralSharpness=.25f;c.taaSharpness=.35f;
            c.engineEffectProfile=preset==0?2:preset==2?0:1;
            c.drawDistanceMode=3;c.drawDistanceCustom=preset==0?30f:preset==2?80f:ApprovedUserDefaults.DrawDistance;
            c.adaptiveDistance=true;c.skipDesktopWorld=true;c.syncForcedVisibility=true;
            c.visibleRegionCulling=true;c.indirectVisibleRegionCulling=true;
            c.uiFullResolution=true;c.stableHudCapture=true;c.coalesceHudLayout=true;
            c.isolateOvertipBatches=false;c.useGamePointerCache=false;
            c.preparationAuraEnabled=false;
        }
        internal static bool Matches(Main.Settings c,int preset,bool nvidia,float requestedOutput)
        {
            if(preset<0||preset>2)return false;
            if(expectedProfiles==null)
            {
                var profiles=new Main.Settings[6];
                for(int i=0;i<6;i++){profiles[i]=new Main.Settings();Apply(profiles[i],i%3,i>=3);}
                expectedProfiles=profiles;
            }
            var expected=expectedProfiles[preset+(nvidia?3:0)];
            return c.neuralMode==expected.neuralMode&&c.temporalAA&&!c.disableAA&&!c.allowGameFsr&&
                Math.Abs(requestedOutput-expected.renderScale)<.001f&&Math.Abs(c.neuralScale-expected.neuralScale)<.001f&&
                c.engineEffectProfile==expected.engineEffectProfile&&c.drawDistanceMode==3&&
                Math.Abs(c.drawDistanceCustom-expected.drawDistanceCustom)<.001f&&c.adaptiveDistance&&
                c.skipDesktopWorld&&c.syncForcedVisibility&&c.visibleRegionCulling&&c.indirectVisibleRegionCulling&&
                c.uiFullResolution&&c.stableHudCapture&&c.coalesceHudLayout&&!c.isolateOvertipBatches&&!c.useGamePointerCache&&
                !c.preparationAuraEnabled&&c.neuralPreset==expected.neuralPreset&&
                Math.Abs(c.neuralSharpness-expected.neuralSharpness)<.001f&&Math.Abs(c.taaSharpness-expected.taaSharpness)<.001f;
        }
    }
    public static partial class Main
    {
        static string CustomSettingsPath => SettingsFiles76.PathFor(Path.GetDirectoryName(_settingsPath),"custom");
        static bool NvidiaQuality => SystemInfo.graphicsDeviceVendorID==0x10de;
        static int ActiveQualityPreset => QualityPresetPolicy.Matches(_cfg,_cfg.qualityPreset,NvidiaQuality,RequestedOutputScale)?_cfg.qualityPreset:3;
        static OverlayMenu _qualityRoot;
        static Settings _initialCustomSettings;
        static string _customSavedAt;
        static float _customSavedUntil;
        static void EnsureCustomSettings()
        {
            if (_initialCustomSettings == null) { _initialCustomSettings = CopySettings(_cfg); _initialCustomSettings.qualityPreset = 3; }
            if (File.Exists(CustomSettingsPath)) return;
            try { WriteCustomSettings(_initialCustomSettings); }
            catch (Exception error) { _log.Log("[quality/custom] Using initial in-memory configuration: " + error.Message); }
        }
        static void WriteCustomSettings(Settings saved)
        {
            SettingsFiles76.Save(CustomSettingsPath, Serialize(saved));
        }
        static void ApplyCompleteSettings(Settings next, bool resetDiagnostics = false)
        {
            // Let the normal before-eye transaction allocate BOTH outputs.
            // Never publish new dimensions while the old GPU pair is in flight.
            float output=next.renderScale, oldOutput=_cfg.renderScale;
            next.openXrRuntime = _cfg.openXrRuntime; // Quality/personal presets never switch the connection.
            next.openXrRuntimeManifest80 = _cfg.openXrRuntimeManifest80;
            next.allDiagnosticsEnabled = !resetDiagnostics && _cfg.allDiagnosticsEnabled;
            next.detailedProfiling = next.allDiagnosticsEnabled;
            next.diagnosticsRevision77 = 77;
            next.ofxrEnabled77 = !resetDiagnostics && _cfg.ofxrEnabled77;
            _cfg=next;_cfg.renderScale=oldOutput;
            ApplyAllDiagnosticsSetting();
            RequestOutputScale(output);ApplyHudPresetPlacement();
            SetEyeAa(RequestedEyeAa);RequestEyeAaHistoryReset();RequestNeuralHistoryReset();
            _renderTargets.Clear();_camDataNextRefresh=0;_reticleState=-1;
            SetTouchWorldScale(_cfg.worldScale);_touchThirdPersonFollower.Reset();StopTouchGroupMovement();
            UpdateModLanguage(true);MarkSettingsDirty();
        }
        static Settings CopySettings(Settings source)
        {
            var copy=new Settings();ParseInto(copy,Serialize(source).Split('\n'));return copy;
        }
        static void SelectQualityPreset(int value)
        {
            Settings next;
            if(value==3)
            {
                EnsureCustomSettings(); next=CopySettings(_initialCustomSettings);
                if(File.Exists(CustomSettingsPath)) ParseInto(next,File.ReadAllLines(CustomSettingsPath));
                next.qualityPreset=3;
            }
            else { next=CopySettings(_cfg);QualityPresetPolicy.Apply(next,value,NvidiaQuality); }
            ApplyCompleteSettings(next);
        }
        static void SaveCustomSettings()
        {
            var saved=CopySettings(_cfg);saved.renderScale=RequestedOutputScale;saved.qualityPreset=3;
            WriteCustomSettings(saved); _initialCustomSettings=CopySettings(saved);
            _customSavedAt=DateTime.Now.ToString("HH:mm:ss");
            _customSavedUntil=Time.unscaledTime+3.5f; _liveNextTextUpdate=0;
            _cfg.qualityPreset=3;MarkSettingsDirty();_liveMessage=ModLocalization.Text("Custom configuration saved.");
        }
        static void RestoreAllSettings()
        {
            var next=new Settings();QualityPresetPolicy.Apply(next,1,NvidiaQuality);
            // Reset is an explicit confirmed user action. Remove only our
            // personal snapshots; game saves and other mods are not involved.
            if(File.Exists(CustomSettingsPath))File.Delete(CustomSettingsPath);
            for(int slot=1;slot<=3;slot++)if(File.Exists(HudPresetPath(slot)))File.Delete(HudPresetPath(slot));
            foreach(string path in _presetPaths)if(!string.IsNullOrEmpty(path)&&File.Exists(path))File.Delete(path);
            foreach(bool system in new[]{false,true})if(File.Exists(NavigationPresetPath(system)))File.Delete(NavigationPresetPath(system));
            ApplyCompleteSettings(next, resetDiagnostics: true);_initialCustomSettings=null;EnsureCustomSettings();_liveNavigation.OpenMenu(_qualityRoot);
        }
        static void SetExplorationZoomLimit(bool value)
        {
            _cfg.limitExplorationZoom=value;
            if(!InSpaceCombat&&!TouchCameraFirstPersonActive)
            {
                _touchTabletop.ConfigureLimits(value?6:.1f,value?14:10000,ComfortCameraOptions.TiltMin,ComfortCameraOptions.TiltMax);
                SetTouchWorldScale(_cfg.worldScale);
            }
            MarkSettingsDirty();
        }
    }
}
