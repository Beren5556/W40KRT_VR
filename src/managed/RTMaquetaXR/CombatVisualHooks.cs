using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static CombatVisualContracts _combatContracts;
        static Harmony _combatHarmony;
        static bool _combatInstalled, _combatActive, _combatEyeScope;
        static Camera _combatLeft, _combatRight;
        static string _combatStatus = "Not installed";
        static long _combatFilteredCalls;
        static int _combatLastOutlineMode;
        static float _combatNextPrune;
        static readonly Dictionary<object, CombatUnitVisual> _combatUnits = new Dictionary<object, CombatUnitVisual>();
        static readonly List<object> _combatDeadUnits = new List<object>();

        internal static void InstallCombatVisuals()
        {
            InstallPreparationAura();
            InstallFamiliarVisuals76();
            if (_combatInstalled) return;
            try
            {
                _combatContracts = CombatVisualContracts.Create(AccessTools.TypeByName);
                _combatHarmony = new Harmony("RTMaquetaXR.CombatVisuals");
                CombatPatch(_combatContracts.SurfaceEnable, nameof(CombatSurfaceReady));
                CombatPatch(_combatContracts.SurfaceDisplay, nameof(CombatSurfaceReady));
                CombatPatch(_combatContracts.MarkEnable, nameof(CombatMarkReady));
                CombatPatch(_combatContracts.DecalActive, nameof(CombatDecalReady));
                CombatPatch(_combatContracts.DecalMaterial, nameof(CombatDecalReady));
                CombatPatch(_combatContracts.UnitEnable, nameof(CombatUnitReady));
                _combatHarmony.Patch(_combatContracts.RendererInfos, prefix: new HarmonyMethod(typeof(Main), nameof(CombatRendererPrefixFactory)));
                CombatPatch(_combatContracts.ColorGetter, nameof(CombatColorPostfix));
                // Refresh the actual renderer caller if Mono inlined a getter.
                _combatHarmony.Patch(_combatContracts.SetupJobs, transpiler: new HarmonyMethod(typeof(Main), nameof(CombatRecompile)));
                _combatHarmony.Patch(_combatContracts.RenderHighlights, transpiler: new HarmonyMethod(typeof(Main), nameof(CombatRecompile)));
                RenderPipelineManager.beginCameraRendering += CombatBeginCamera;
                RenderPipelineManager.endCameraRendering += CombatEndCamera;
                _combatInstalled = true; _combatStatus = "Ready · exploration and combat";
            }
            catch (Exception e)
            {
                _combatHarmony?.UnpatchAll(_combatHarmony.Id); _combatHarmony = null;
                _combatStatus = "Unavailable: " + e.Message;
                _log.Error("[combat-visuals] " + _combatStatus);
            }
        }
        static void CombatPatch(MethodInfo method, string postfix) => _combatHarmony.Patch(method, postfix: new HarmonyMethod(typeof(Main), postfix));
        static IEnumerable<CodeInstruction> CombatRecompile(IEnumerable<CodeInstruction> code) => code;

        static void CombatSurfaceReady(object __instance)
        {
            if (!_combatActive) return;
            TrackCombatRenderer(_combatContracts.Fill(__instance) as Renderer, true);
            TrackCombatRenderer(_combatContracts.Outline(__instance) as Renderer, true);
        }
        static void CombatMarkReady(object __instance)
        {
            if (!_combatActive) return;
            var decals = _combatContracts.Decals(__instance) as IList;
            if (decals != null) for (int i = 0; i < decals.Count; ++i) CombatDecalReady(decals[i]);
        }
        static void CombatDecalReady(object __instance)
        {
            if (_combatActive && __instance != null) TrackCombatRenderer(_combatContracts.DecalRenderer(__instance) as Renderer, false);
        }
        static void CombatUnitReady(object __instance)
        {
            if (!_combatActive || __instance == null || !_combatContracts.UnitType.IsInstanceOfType(__instance)) return;
            var component = __instance as Component; if (component == null) return;
            object highlighter = _combatContracts.Highlighter(__instance);
            if (highlighter == null || _combatUnits.ContainsKey(highlighter)) return;
            var view = component.GetComponentInParent(_combatContracts.ViewType) as Component;
            // Exclude props, particles and highlighters not belonging to a unit.
            if (view == null) return;
            _combatUnits.Add(highlighter, new CombatUnitVisual(component, view, highlighter));
        }

        internal static void UpdateCombatVisuals(Camera left, Camera right)
        {
            UpdatePreparationAura(!InSpaceCombat && !InNavigationMap && !_modeFlat && left != null && right != null);
            if (!_combatInstalled) return;
            bool world = !InSpaceCombat && !InNavigationMap && !_modeFlat && left != null && right != null;
            if (!world || !CombatVisualPolicy.HasOverrides(_cfg.combatSurfaceIntensity, _cfg.combatUnitFxIntensity, _cfg.combatOutlineMode, _cfg.combatOutlineIntensity))
            {
                if (_combatActive || _combatPendingBlocks.Count != 0) RestoreCombatVisuals();
                return;
            }
            _combatLeft = left; _combatRight = right;
            try
            {
                if (!_combatActive)
                {
                    _combatActive = true;
                    // World-space presentation includes exploration, dialogue and
                    // combat. Never require IsInCombat: selected unit marks and
                    // speaker highlights also exist during normal exploration.
                    // One discovery per world attachment. Subsequent creations /
                    // pooled activations arrive through the native lifecycle hooks.
                    foreach (var item in UnityEngine.Object.FindObjectsByType(_combatContracts.SurfaceType, FindObjectsInactive.Exclude, FindObjectsSortMode.None)) CombatSurfaceReady(item);
                    foreach (var item in UnityEngine.Object.FindObjectsByType(_combatContracts.MarkType, FindObjectsInactive.Exclude, FindObjectsSortMode.None)) CombatMarkReady(item);
                    foreach (var item in UnityEngine.Object.FindObjectsByType(_combatContracts.UnitType, FindObjectsInactive.Exclude, FindObjectsSortMode.None)) CombatUnitReady(item);
                }
                int mode = _cfg.combatOutlineMode;
                bool prune = Time.unscaledTime >= _combatNextPrune;
                if (mode >= 2 || _combatLastOutlineMode >= 2 || prune)
                {
                    _combatDeadUnits.Clear();
                    foreach (var pair in _combatUnits)
                    {
                        if (!pair.Value.Alive) { pair.Value.Dispose(); _combatDeadUnits.Add(pair.Key); continue; }
                        if (mode >= 2 || _combatLastOutlineMode >= 2) pair.Value.Update(mode);
                    }
                    foreach (var key in _combatDeadUnits) _combatUnits.Remove(key);
                }
                _combatLastOutlineMode = mode;
                if (prune) { PruneCombatRenderers(); _combatNextPrune = Time.unscaledTime + 2f; }
            }
            catch (Exception e) { CombatVisualFailure(e); }
        }

        internal static void RestoreCombatVisuals()
        {
            _combatEyeScope = false; _combatActive = false;
            RestoreCombatBlocks();
            foreach (var unit in _combatUnits.Values) unit.Dispose();
            _combatUnits.Clear(); _combatRenderers.Clear(); _combatRendererSet.Clear();
            if (_combatCircleMaterial != null) UnityEngine.Object.Destroy(_combatCircleMaterial);
            _combatCircleMaterial = null; _combatCircleCount = 0; _combatLeft = _combatRight = null;
            _combatLastOutlineMode = 0; _combatNextPrune = 0;
        }
        static void CombatVisualFailure(Exception error)
        {
            RestoreCombatVisuals(); _combatStatus = "Disabled after error: " + error.Message;
            _combatInstalled = false;
            RenderPipelineManager.beginCameraRendering -= CombatBeginCamera;
            RenderPipelineManager.endCameraRendering -= CombatEndCamera;
            _combatHarmony?.UnpatchAll(_combatHarmony.Id); _combatHarmony = null;
            _log.Error("[combat-visuals] " + _combatStatus);
        }
        static void CombatBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!_combatActive && _combatPendingBlocks.Count == 0) return;
            try
            {
                RestoreCombatBlocks();
                bool previousEye = _combatEyeScope;
                _combatEyeScope = CombatVisualPolicy.EyeScope(_combatInstalled, _combatActive && !_modeFlat,
                    camera != null && (camera == _combatLeft || camera == _combatRight));
                if (_combatCircleCount != 0 && (previousEye || _combatEyeScope))
                    foreach (var unit in _combatUnits.Values) unit.Show(_combatEyeScope);
                if (_combatEyeScope) ApplyCombatBlocks();
            }
            catch (Exception e) { CombatVisualFailure(e); }
        }
        static void CombatEndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!_combatEyeScope && _combatPendingBlocks.Count == 0) return;
            try
            {
                RestoreCombatBlocks(); _combatEyeScope = false;
                if (_combatCircleCount != 0) foreach (var unit in _combatUnits.Values) unit.Show(false);
            }
            catch (Exception e) { CombatVisualFailure(e); }
        }
        static bool CombatSuppressOutline(object instance)
        {
            if (!_combatEyeScope || (_cfg.combatOutlineMode != 2 && _cfg.combatOutlineMode != 3)) return false;
            bool suppress = CombatVisualPolicy.SuppressOutline(_combatEyeScope, _combatUnits.ContainsKey(instance), _cfg.combatOutlineMode);
            if (suppress) ++_combatFilteredCalls;
            return suppress;
        }
        static void CombatColorPostfix(object __instance, ref Color __result)
        {
            if (!_combatEyeScope || _cfg.combatOutlineMode != 1) return;
            if (CombatVisualPolicy.DimOutline(_combatEyeScope, _combatUnits.ContainsKey(__instance), _cfg.combatOutlineMode))
                __result.a *= CombatVisualPolicy.Intensity(_cfg.combatOutlineIntensity, .1f);
        }
        static DynamicMethod CombatRendererPrefixFactory(MethodBase original) => BuildCombatRendererPrefix(((MethodInfo)original).ReturnType);
        internal static DynamicMethod BuildCombatRendererPrefix(Type listType)
        {
            return CombatVisualPrefixFactory.Build(listType, AccessTools.Method(typeof(Main), nameof(CombatSuppressOutline)));
        }
        internal static object CombatVisualSnapshot() => new {
            Installed = _combatInstalled, Active = _combatActive, Status = _combatStatus,
            SurfaceIntensity = _cfg.combatSurfaceIntensity, MarkIntensity = _cfg.combatUnitFxIntensity,
            OutlineMode = _cfg.combatOutlineMode, OutlineIntensity = _cfg.combatOutlineIntensity,
            TrackedRenderers = _combatRenderers.Count, TrackedUnitHighlighters = _combatUnits.Count,
            AppliedPropertyBlocks = _combatAppliedBlocks, UnsupportedMaterials = _combatUnsupportedMaterials,
            DeploymentAura = PreparationAuraSnapshot(),
            FilteredUnitHighlightQueries = _combatFilteredCalls, Scope = "All world scenes: exploration, dialogue and combat; exact VR eyes; tactical logic unchanged"
        };
    }
}
