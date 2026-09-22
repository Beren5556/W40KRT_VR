using System;
using System.Reflection;
using HarmonyLib;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static MethodInfo _soundTarget75,_soundType75;
        static long _soundClips75,_soundNone75,_abilitySounds75,_weaponSounds75;
        static bool _soundAuditReady75;
        static void InstallCombatAudioAudit75()
        {
            try
            {
                var clip=AccessTools.TypeByName("Kingmaker.Visual.Animation.Events.AnimationClipEventSoundUnit");
                _soundTarget75=AccessTools.PropertyGetter(clip,"TargetType");
                _soundType75=AccessTools.PropertyGetter(clip,"Type");
                _harmony.Patch(AccessTools.DeclaredMethod(clip,"Start"),postfix:new HarmonyMethod(typeof(Main),nameof(SoundClipCompleted75)));
                var receiver=AccessTools.TypeByName("Kingmaker.Visual.Animation.UnitAnimationCallbackReceiver");
                _harmony.Patch(AccessTools.DeclaredMethod(receiver,"AbilityAnimationEvent"),postfix:new HarmonyMethod(typeof(Main),nameof(AbilitySoundCompleted75)));
                foreach(string name in new[]{"PostMainWeaponEquipEvent","PostOffWeaponEquipEvent","PostMainWeaponUnequipEvent","PostOffWeaponUnequipEvent","PostArmorFoleyEvent"})
                    _harmony.Patch(AccessTools.DeclaredMethod(receiver,name),postfix:new HarmonyMethod(typeof(Main),nameof(WeaponSoundCompleted75)));
                _soundAuditReady75=true;
            }
            catch(Exception error){_log?.Error("[audio75/audit] Passive counters unavailable: "+error.Message);}
        }
        static void SoundClipCompleted75(object __instance)
        {
            if(!DiagnosticsRecording)return;
            ++_soundClips75;
            // Postfix observes completion only: no prefix, return replacement,
            // synthetic playback, event cancellation or ownership of native audio.
            try
            {if(Convert.ToInt32(_soundTarget75.Invoke(__instance,null))==0&&Convert.ToInt32(_soundType75.Invoke(__instance,null))==0)++_soundNone75;}
            catch{/* A diagnostic must never affect the native animation. */}
        }
        static void AbilitySoundCompleted75(){if(DiagnosticsRecording)++_abilitySounds75;}
        static void WeaponSoundCompleted75(){if(DiagnosticsRecording)++_weaponSounds75;}
        static object CombatAudioSnapshot75()=>new{Ready=_soundAuditReady75,CompletedClips=_soundClips75,NativeNoneClips=_soundNone75,AbilityEvents=_abilitySounds75,WeaponEvents=_weaponSounds75};
    }
}
