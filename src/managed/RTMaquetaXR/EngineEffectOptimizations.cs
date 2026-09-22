using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Harmony _engineEffectHarmony;
        static string _engineEffectFailure;
        static bool _engineEffectReady;
        static Camera _engineEffectDataCamera;
        static int _engineEffectDataDepth;
        static long _engineEffectEarlyGuards;
        static long _engineEffectStackSsrInputsCorrected;
        static long _engineEffectGuards, _engineEffectBlurSkips, _engineVolumetricSetupsAvoided;
        static readonly Dictionary<MethodBase, int> _engineEffectMinimum = new Dictionary<MethodBase, int>();
        internal static bool EngineEffectProfileSupported => _engineEffectReady;
        internal static int EngineEffectProfile => EngineEffectPolicy.Normalize(_cfg.engineEffectProfile);
        internal static void SetEngineEffectProfile(int value)
        {
            value = EngineEffectPolicy.Normalize(value);
            if (_cfg.engineEffectProfile == value) return;
            _cfg.engineEffectProfile = value;
            MarkSettingsDirty();
        }
        static bool EngineSuppressEffect(int minimum) => EngineEffectPolicy.Suppress(EffectiveEngineEffectProfile, minimum,
            _active, _attached, _modeFlat, IsEye(_engineEffectDataDepth != 0 ? _engineEffectDataCamera : _renderingCamera));

        struct EngineEffectDataScope { internal Camera Camera; internal int Depth; }
        static void EngineEffectInitializationPrefix(Camera __0, ref bool __4, out EngineEffectDataScope __state)
        {
            EngineEffectDataPrefix(__0, out __state);
            // RenderCameraStack computes this argument BEFORE InitializeCameraData
            // opens our eye scope. Its stale true flag otherwise rejects our
            // visible-frustum culling even with Reduced disabling actual SSR.
            // Keep the stack metadata consistent with this eye's effect policy.
            if (__4 && _engineEffectReady && EngineSuppressEffect(1))
            { __4 = false; ++_engineEffectStackSsrInputsCorrected; }
        }
        static void EngineEffectDataPrefix(Camera __0, out EngineEffectDataScope __state)
        {
            __state = new EngineEffectDataScope { Camera = _engineEffectDataCamera, Depth = _engineEffectDataDepth };
            _engineEffectDataCamera = __0; ++_engineEffectDataDepth;
        }
        static Exception EngineEffectDataFinalizer(Exception __exception, EngineEffectDataScope __state)
        {
            _engineEffectDataCamera = __state.Camera; _engineEffectDataDepth = __state.Depth;
            return __exception;
        }

        static void InstallEngineEffectOptimizations()
        {
            InstallEmptyHighlightOptimization();
            InstallEmptyOccludedHighlightOptimization();
            InstallOccludedGeometryDrawSuppression();
            InstallPcHudWorkIsolation();
            if (_engineEffectHarmony != null) return;
            try
            {
                _engineEffectHarmony = new Harmony("RTMaquetaXR.EngineEffectBudget");
                // CameraData is created before RenderSingleCamera opens the
                // render-eye scope. Its native SSR/depth-pyramid decisions must
                // see the SAME exact camera policy as the later render graph.
                // A nested UI/desktop camera overrides, rather than inherits,
                // the outer eye; the finalizer restores even on native errors.
                var pipeline = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.WaaaghPipeline");
                var initialize = AccessTools.Method(pipeline, "InitializeCameraData");
                var parameters = initialize?.GetParameters();
                if (initialize == null || initialize.IsStatic || initialize.ReturnType != typeof(void) ||
                    parameters.Length != 6 || parameters[0].ParameterType != typeof(Camera) ||
                    parameters[4].ParameterType != typeof(bool) ||
                    parameters[5].ParameterType.FullName != "Owlcat.Runtime.Visual.Waaagh.CameraData&")
                    throw new MissingMethodException("WaaaghPipeline.InitializeCameraData camera ownership");
                _engineEffectHarmony.Patch(initialize,
                    prefix: new HarmonyMethod(typeof(Main), nameof(EngineEffectInitializationPrefix)),
                    finalizer: new HarmonyMethod(typeof(Main), nameof(EngineEffectDataFinalizer)));
                // Use the native inactive paths: they manage render-graph inputs,
                // keywords and cleanup. Skipping an already-recorded pass would
                // leave undefined textures, so no Render/Execute call is removed.
                foreach (string name in new[] { "VolumetricFog", "ScreenSpaceReflections" }) EngineEffectGuard(name, 1);
                foreach (string name in new[] { "HBAO.Hbao", "Bloom", "BloomEnhanced", "RadialBlur" }) EngineEffectGuard(name, 2);
                var blur = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.RendererFeatures.FullscreenBlur.FullscreenBlurFeature");
                var add = AccessTools.Method(blur, "AddRenderPasses");
                if (add == null || add.ReturnType != typeof(void) || add.GetParameters().Length != 2)
                    throw new MissingMethodException("FullscreenBlurFeature.AddRenderPasses");
                _engineEffectHarmony.Patch(add, prefix: new HarmonyMethod(typeof(Main), nameof(EngineEffectBlurPrefix)));
                var volumetric = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.RendererFeatures.VolumetricLighting.VolumetricLightingFeature");
                var volumeSetup = AccessTools.Method(volumetric, "StartSetupJobs");
                if (volumeSetup == null || volumeSetup.ReturnType != typeof(void) || volumeSetup.GetParameters().Length != 1)
                    throw new MissingMethodException("VolumetricLightingFeature.StartSetupJobs");
                _engineEffectHarmony.Patch(volumeSetup, prefix: new HarmonyMethod(typeof(Main), nameof(EngineVolumetricSetupPrefix)));
                // Re-JIT the known consumers as well as the tiny IsActive methods:
                // old Mono inlining must not silently ignore an optional profile.
                foreach (string typeName in new[] {
                    "Owlcat.Runtime.Visual.Waaagh.WaaaghPipeline",
                    "Owlcat.Runtime.Visual.Waaagh.WaaaghRenderer",
                    "Owlcat.Runtime.Visual.Waaagh.Passes.PostProcess.PostProcessPass",
                    "Owlcat.Runtime.Visual.Waaagh.Passes.HbaoPass",
                    "Owlcat.Runtime.Visual.Waaagh.Passes.ScreenSpaceReflectionsPass",
                    "Owlcat.Runtime.Visual.Waaagh.Passes.StochasticScreenSpaceReflectionsPass",
                    "Owlcat.Runtime.Visual.Waaagh.RendererFeatures.VolumetricLighting.VolumetricLightingFeature",
                    "Owlcat.Runtime.Visual.Waaagh.Passes.SetupFogPass" })
                {
                    var type = AccessTools.TypeByName(typeName);
                    if (type == null) continue;
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                        if (method.GetMethodBody() != null && EngineEffectCallsGuard(method))
                            _engineEffectHarmony.Patch(method, transpiler: new HarmonyMethod(typeof(Main), nameof(CombatRecompile)));
                }
                _engineEffectReady = true;
            }
            catch (Exception error)
            {
                _engineEffectHarmony?.UnpatchAll(_engineEffectHarmony.Id); _engineEffectHarmony = null;
                _engineEffectMinimum.Clear(); _engineEffectReady = false; _engineEffectFailure = error.Message;
                _log?.Error("[performance/effects] Native visual effects retained: " + error.Message);
            }
        }
        static void EngineEffectGuard(string name, int minimum)
        {
            var type = AccessTools.TypeByName("Owlcat.Runtime.Visual.Overrides." + name);
            var method = type?.GetMethod("IsActive", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != typeof(bool) || method.GetMethodBody() == null)
                throw new MissingMethodException("Effect guard " + name);
            _engineEffectMinimum.Add(method, minimum);
            _engineEffectHarmony.Patch(method, postfix: new HarmonyMethod(typeof(Main), nameof(EngineEffectActivePostfix)));
        }
        static bool EngineEffectCallsGuard(MethodInfo method)
        {
            // Harmony's instruction reader resolves metadata once at install;
            // no reflection, strings, delegates or allocation in a render loop.
            foreach (var instruction in PatchProcessor.GetOriginalInstructions(method))
                if (instruction.operand is MethodInfo target && _engineEffectMinimum.ContainsKey(target)) return true;
            return false;
        }
        static void EngineEffectActivePostfix(MethodBase __originalMethod, ref bool __result)
        {
            if (!__result || !_engineEffectReady) return;
            if (_engineEffectMinimum.TryGetValue(__originalMethod, out var minimum) && EngineSuppressEffect(minimum))
            { __result = false; ++_engineEffectGuards; if (_engineEffectDataDepth != 0) ++_engineEffectEarlyGuards; }
        }
        static bool EngineEffectBlurPrefix()
        {
            if (!_engineEffectReady || !EngineSuppressEffect(2)) return true;
            ++_engineEffectBlurSkips; return false;
        }
        static bool EngineVolumetricSetupPrefix()
        {
            if (!_engineEffectReady || !EngineSuppressEffect(1)) return true;
            // The native completion method only completes the previous handle;
            // its already completed handle remains legal. Native AddRenderPasses
            // still performs cleanup and takes its inactive-volume branch.
            ++_engineVolumetricSetupsAvoided; return false;
        }
        static void StopEngineEffectOptimizations()
        {
            StopEmptyHighlightOptimization();
            StopEmptyOccludedHighlightOptimization();
            StopOccludedGeometryDrawSuppression();
            StopPcHudWorkIsolation();
            _engineEffectReady = false;
            _engineEffectHarmony?.UnpatchAll(_engineEffectHarmony.Id); _engineEffectHarmony = null;
            _engineEffectMinimum.Clear();
            _engineEffectDataCamera = null; _engineEffectDataDepth = 0;
        }
        static object EngineEffectSnapshot() => new {
            Ready = _engineEffectReady, Profile = EngineEffectProfile, EffectiveProfile = EffectiveEngineEffectProfile,
            CombatAutomatic = _cfg.combatMinimumEffects, CombatOverride = CombatMinimumEffectsActive, Failure = _engineEffectFailure,
            NativeActiveGuardsSuppressed = _engineEffectGuards, FullscreenBlurPassesAvoided = _engineEffectBlurSkips,
            CameraInitializationGuardsSuppressed = _engineEffectEarlyGuards,
            CameraStackSsrInputsCorrected = _engineEffectStackSsrInputsCorrected,
            VolumetricSetupsAvoided = _engineVolumetricSetupsAvoided,
            EmptyHighlights = EmptyHighlightSnapshot(), EmptyOccludedHighlights = EmptyOccludedHighlightSnapshot(), HiddenHudWork = PcHudWorkSnapshot(),
            FadedScenery = OccludedGeometryDrawSnapshot(),
            PopupSubtreeLayouts = _hudPopupSubtreeLayouts,
            Scope = "Exact VR eyes. Reduced: volumetric fog and screen-space reflections; Minimal also HBAO, bloom, radial/fullscreen blur. Native inactive branches, no game settings or simulation changes."
        };
    }
}
