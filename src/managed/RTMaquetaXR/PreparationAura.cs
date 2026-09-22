using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static PreparationAuraContracts _preparationAuraContracts;
        static PreparationAuraPolicy _preparationAura;
        static Harmony _preparationAuraHarmony;
        static object _preparationAuraBlueprint;
        static bool _preparationAuraWorld, _preparationAuraFailed;
        static string _preparationAuraError;
        static long _preparationAuraDiscoveries;
        static readonly List<object> _preparationAuraExisting = new List<object>();
        internal static bool PreparationAuraEnabled => _cfg.preparationAuraEnabled;
        internal static void SetPreparationAuraEnabled(bool enabled)
        {
            if (_cfg.preparationAuraEnabled == enabled) return;
            _cfg.preparationAuraEnabled = enabled; MarkSettingsDirty();
            UpdatePreparationAura(_preparationAuraWorld);
        }
        static void InstallPreparationAura()
        {
            if (_preparationAuraHarmony != null || _preparationAuraFailed) return;
            try
            {
                _preparationAuraContracts = PreparationAuraContracts.Create(AccessTools.TypeByName);
                _preparationAura = new PreparationAuraPolicy(PreparationAuraMatches, PreparationAuraAlive,
                    _preparationAuraContracts.Clear, _preparationAuraContracts.Spawn);
                _preparationAuraHarmony = new Harmony("RTMaquetaXR.PreparationAura");
                _preparationAuraHarmony.Patch(_preparationAuraContracts.SpawnMethod,
                    prefix: new HarmonyMethod(typeof(Main), nameof(PreparationAuraSpawnPrefix)));
                _preparationAuraHarmony.Patch(_preparationAuraContracts.DisposeMethod,
                    postfix: new HarmonyMethod(typeof(Main), nameof(PreparationAuraDisposed)));
            }
            catch (Exception error) { PreparationAuraFailure(error); }
        }
        static bool PreparationAuraMatches(object buff)
        {
            if (_preparationAuraBlueprint == null)
            {
                object root = _preparationAuraContracts.FxRoot();
                object reference = root == null ? null : _preparationAuraContracts.PreparationReference(root);
                if (reference != null) _preparationAuraBlueprint = _preparationAuraContracts.ResolveBlueprint(reference);
            }
            return _preparationAuraBlueprint != null && ReferenceEquals(_preparationAuraBlueprint, _preparationAuraContracts.Blueprint(buff));
        }
        static bool PreparationAuraAlive(object buff) => buff != null && !_preparationAuraContracts.Disposed(buff) &&
            _preparationAuraContracts.Attached(buff) && _preparationAuraContracts.Active(buff);
        static bool PreparationAuraSpawnPrefix(object __instance)
        {
            if (_preparationAura == null || _preparationAuraFailed) return true;
            try { return _preparationAura.BeforeSpawn(__instance); }
            catch (Exception error) { PreparationAuraFailure(error); return true; }
        }
        static void PreparationAuraDisposed(object __instance) { _preparationAura?.Forget(__instance); }
        static void UpdatePreparationAura(bool world)
        {
            _preparationAuraWorld = world && _active && _attached;
            if (_preparationAura == null || _preparationAuraFailed) return;
            bool suppress = _preparationAuraWorld && !PreparationAuraEnabled;
            if (_preparationAura.Suppressed == suppress) return;
            try
            {
                // Only a transition into suppression enumerates the current
                // party. Future/recreated effects arrive at native Spawn itself.
                // No scene search, reflection or party enumeration per frame.
                if (suppress) DiscoverPreparationAura();
                _preparationAura.Set(suppress, suppress ? _preparationAuraExisting : null);
            }
            catch (Exception error) { PreparationAuraFailure(error); }
            finally { _preparationAuraExisting.Clear(); }
        }
        static void DiscoverPreparationAura()
        {
            _preparationAuraExisting.Clear(); ++_preparationAuraDiscoveries;
            object root = _preparationAuraContracts.FxRoot();
            object reference = root == null ? null : _preparationAuraContracts.PreparationReference(root);
            _preparationAuraBlueprint = reference == null ? null : _preparationAuraContracts.ResolveBlueprint(reference);
            if (_preparationAuraBlueprint == null) return;
            object game = _preparationAuraContracts.Game();
            object player = game == null ? null : _preparationAuraContracts.Player(game);
            var party = player == null ? null : _preparationAuraContracts.Party(player) as IList;
            if (party == null) return;
            for (int i = 0; i < party.Count; ++i)
            {
                if (party[i] == null) continue;
                object collection = _preparationAuraContracts.Buffs(party[i]);
                object buff = collection == null ? null : _preparationAuraContracts.GetBuff(collection, _preparationAuraBlueprint);
                if (buff != null) _preparationAuraExisting.Add(buff);
            }
        }
        static void PreparationAuraFailure(Exception error)
        {
            _preparationAuraFailed = true; _preparationAuraError = error.Message;
            try { _preparationAura?.Set(false); } catch { }
            _preparationAuraHarmony?.UnpatchAll(_preparationAuraHarmony.Id); _preparationAuraHarmony = null;
            _log?.Error("[preparation-aura] Original presentation retained: " + error.Message);
        }
        static void StopPreparationAura()
        {
            _preparationAuraWorld = false;
            try { _preparationAura?.Set(false); }
            catch (Exception error) { PreparationAuraFailure(error); }
            _preparationAuraHarmony?.UnpatchAll(_preparationAuraHarmony.Id); _preparationAuraHarmony = null;
            _preparationAura = null; _preparationAuraBlueprint = null; _preparationAuraExisting.Clear();
        }
        static object PreparationAuraSnapshot() => new {
            Enabled = PreparationAuraEnabled, Ready = _preparationAura != null && !_preparationAuraFailed,
            Suppressed = _preparationAura?.Suppressed ?? false, HiddenBuffVisuals = _preparationAura?.Hidden ?? 0,
            PreventedSpawns = _preparationAura?.Prevented ?? 0, ClearedExisting = _preparationAura?.Cleared ?? 0,
            RestoredVisuals = _preparationAura?.Restored ?? 0, PartyDiscoveries = _preparationAuraDiscoveries,
            BlueprintResolved = _preparationAuraBlueprint != null, Failure = _preparationAuraError,
            Scope = "FxRoot.PreparationTurnVisualBuff visual spawn/clear only; native fact and tactical markers preserved"
        };
    }
}
