using System.Reflection;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Settings _spaceQualitySaved;
        // Exactly the settings controlled by QualityPresetPolicy.Apply. Preserve
        // custom placement/controls changed while the player is in space.
        static readonly string[] SpaceQualityFields = {
            "qualityPreset","neuralMode","temporalAA","disableAA","allowGameFsr","renderScale","neuralScale",
            "neuralPreset","neuralSharpness","taaSharpness","engineEffectProfile","drawDistanceMode","drawDistanceCustom",
            "adaptiveDistance","skipDesktopWorld","syncForcedVisibility","visibleRegionCulling","indirectVisibleRegionCulling",
            "uiFullResolution","stableHudCapture","coalesceHudLayout","isolateOvertipBatches","useGamePointerCache","preparationAuraEnabled"
        };
        static void CopyQualityFields(Settings target, Settings source)
        {
            foreach(string name in SpaceQualityFields)
            {var field=typeof(Settings).GetField(name,BindingFlags.Public|BindingFlags.Instance);field.SetValue(target,field.GetValue(source));}
        }
        static Settings PersistentSettings()
        {
            if(_spaceQualitySaved==null)return _cfg;
            var saved=CopySettings(_cfg);CopyQualityFields(saved,_spaceQualitySaved);return saved;
        }
        internal static void UpdateSpaceQualityProfile()
        {
            if(InSpaceCombat)
            {
                if(_spaceQualitySaved!=null)return;
                _spaceQualitySaved=CopySettings(_cfg);_spaceQualitySaved.renderScale=RequestedOutputScale;
                var next=CopySettings(_cfg);QualityPresetPolicy.Apply(next,2,NvidiaQuality);
                ApplyCompleteSettings(next);
                _log.Log("[space/quality] Maximum quality applied; previous rendering settings retained");
            }
            else RestoreSpaceQualityProfile();
        }
        static void RestoreSpaceQualityProfile(bool stopping = false)
        {
            if(_spaceQualitySaved==null)return;
            var next=CopySettings(_cfg);CopyQualityFields(next,_spaceQualitySaved);_spaceQualitySaved=null;
            if(stopping) { _cfg=next; MarkSettingsDirty(); }
            else ApplyCompleteSettings(next);
            _log.Log("[space/quality] Restored exact pre-space rendering settings");
        }
    }
}
