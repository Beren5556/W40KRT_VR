using UnityEngine;

namespace RTMaquetaXR
{
    internal static class CombatEngineEffectPolicy
    {
        internal static int Effective(int selected, bool enabled, bool vr, bool combat) =>
            enabled && vr && combat ? 2 : EngineEffectPolicy.Normalize(selected);
    }
    public static partial class Main
    {
        static int _combatEffectsFrame = -1;
        static bool _combatEffectsInCombat;
        static bool CombatMinimumEffectsActive
        {
            get
            {
                if (!_cfg.combatMinimumEffects || !_active) return false;
                if (_combatEffectsFrame != Time.frameCount)
                {
                    _combatEffectsFrame = Time.frameCount;
                    // Native combat state survives inventory, pause and tutorial
                    // windows. It includes preparation and does not follow UI modes.
                    // Verified on Code.dll: BeginPreparationTurn requires
                    // TurnBasedModeActive, which requires this same InCombat
                    // flag. No heuristic based on a visible grid is necessary.
                    try { _combatEffectsInCombat = InSpaceCombat || (_attached && TouchRadialCombatNow(out var ignored)); }
                    catch { _combatEffectsInCombat=false; } // Native Player/Turn can disappear during unloading.
                }
                return _combatEffectsInCombat;
            }
        }
        static int EffectiveEngineEffectProfile => CombatEngineEffectPolicy.Effective(
            _cfg.engineEffectProfile, _cfg.combatMinimumEffects, _active, CombatMinimumEffectsActive);
        static string EngineEffectProfileText()
        {
            if (!EngineEffectProfileSupported) return ModLocalization.Text("Unavailable");
            string selected=ModLocalization.Text(EngineEffectProfile==0?"Original":EngineEffectProfile==1?"Reduced":"Minimal");
            return CombatMinimumEffectsActive ? ModLocalization.Format("Minimal · combat (saved: {0})",selected) : selected;
        }
        static OverlayOption CombatMinimumEffectsOption()
        {
            var option=ImageToggle("Minimal effects during combat",
            "Automatically use Minimal engine effects during ground and space combat, including preparation. Restore your selected effect level afterwards. Off by default. Keeps tactical indicators, particles and game rules. Does not change saved graphics settings.",
                () => _cfg.combatMinimumEffects, value => { _cfg.combatMinimumEffects = value; _combatEffectsFrame = -1; });
            option.Enabled=()=>EngineEffectProfileSupported;
            // Display the actually applied override on the shared option. The
            // base Engine effects value intentionally remains the user's saved
            // choice, including changes made while the combat override is on.
            option.Value=()=>!_cfg.combatMinimumEffects ? ModLocalization.Text("Off") :
                ModLocalization.Text("On") + (CombatMinimumEffectsActive ? " · " + ModLocalization.Text(
                    EngineEffectProfile==2 ? "Already Minimal" : "Minimal in combat") : "");
            return option;
        }
    }
}
