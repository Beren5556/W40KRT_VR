using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        // Native buff -> attached area -> FX, verified against the installed
        // blueprint pack. CombatEnd / 99 rounds describe this persistent aura,
        // not a timed attack. Unknown pet abilities never enter this catalogue.
        internal const string FamiliarAuraArea78 = "536d7471d07a4eb4b1163f043e63a0d6";
        internal const string FamiliarAuraBuff78 = "215d9f42b7e6435e9ed77337156e0fc3";
        static string FamiliarBlueprint78(object owner)
        {
            object blueprint = ReadMember73(owner, "Blueprint");
            object guid = ReadMember73(blueprint, "AssetGuid");
            if (guid == null)
                guid = ReadMember73(ReadMember73(owner, "m_Blueprint"), "Guid");
            return guid?.ToString().Replace("-", "").ToLowerInvariant() ?? "";
        }
        static bool CataloguedFamiliarArea78(object view) =>
            FamiliarBlueprint78(ReadMember73(view, "Data")) == FamiliarAuraArea78 ||
            FamiliarBlueprint78(view) == FamiliarAuraArea78;

        static readonly Dictionary<string,bool> _familiarContracts78 = new Dictionary<string,bool>();
        static void FamiliarHook78(Type type, string method, string callback, bool prefix = false)
        {
            string key = (type?.FullName ?? "missing type") + "." + method;
            try
            {
                var native = type == null ? null : AccessTools.DeclaredMethod(type, method);
                if (native == null) throw new MissingMethodException(key);
                var patch = new HarmonyMethod(typeof(Main), callback);
                _harmony.Patch(native, prefix: prefix ? patch : null, postfix: prefix ? null : patch);
                _familiarContracts78[key] = true;
            }
            catch (Exception error)
            { _familiarContracts78[key] = false; _log.Error("[familiar78/contract] " + key + ": " + error.Message); }
        }
        static void FamiliarSeed78(Type type, Action<Component> seed)
        {
            if (type == null) return;
            try { foreach(var value in UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.None)) seed(value as Component); }
            catch(Exception error) { _log.Error("[familiar78/discovery] " + type.FullName + ": " + error.Message); }
        }
        static void FamiliarDecalVisible78(Component __instance, ref bool __result)
        {
            if (!__result || !HideFamiliarVisuals76 || __instance == null) return;
            var lease = __instance.GetComponentInParent<FamiliarFxVisual76>();
            if (lease != null && lease.enabled && !lease.Released78 && lease.FullRoot78) __result = false;
        }
    }
}
