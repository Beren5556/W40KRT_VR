using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _hudRendererRestrictionsReady;
        static object _hudEmptyFeatures;
        static FieldInfo[] _hudUiPassFields;
        static FieldInfo[] _hudBasePassFields;
        static FieldInfo _hudBasePassesField;
        static bool _hudRenderFailurePending;
        static readonly Dictionary<object, HashSet<object>> _hudUiPasses = new Dictionary<object, HashSet<object>>();

        // Verified against the installed Waaagh renderer. Clear retains depth /
        // stencil for masks; normal transparent and OverlayForward draw the
        // original UI shaders, and FinalBlit resolves the actual target. Camera
        // setup / render-graph resource initialization remain in the pipeline.
        static readonly string[] HudUiPassNames = {
            "m_ClearPass", "m_DrawTransparentPass", "m_RenderOverlayForwardPass", "m_FinalBlitPass"
        };
        static readonly string[] HudBasePassNames = {
            "m_SetShaderTimePass", "m_CameraSetupPass", "m_SetCameraShaderVariablePass", "m_SetShaderTimeAfterCameraSetupPass"
        };

        static bool InHudRenderScope() => IsHudCaptureCamera(_renderingCamera) || IsSpatialCaptureCamera(_renderingCamera);

        static void EnsureHudRenderIsolation()
        {
            if (_hudRendererRestrictionsReady) return;
            Type renderer = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.ScriptableRenderer");
            Type waaagh = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.WaaaghRenderer");
            if (renderer == null || waaagh == null) throw new MissingMemberException("HUD renderer unavailable");
            MethodInfo features = AccessTools.PropertyGetter(renderer, "RendererFeatures");
            MethodInfo enqueue = AccessTools.Method(renderer, "EnqueuePass");
            if (features == null || !features.ReturnType.IsGenericType ||
                features.ReturnType.GetGenericTypeDefinition() != typeof(List<>) || enqueue == null ||
                enqueue.ReturnType != typeof(void) || enqueue.GetParameters().Length != 1)
                throw new MissingMemberException("HUD renderer isolation contract changed");
            _hudUiPassFields = new FieldInfo[HudUiPassNames.Length];
            for (int i = 0; i < HudUiPassNames.Length; ++i)
                _hudUiPassFields[i] = AccessTools.Field(waaagh, HudUiPassNames[i]) ??
                    throw new MissingFieldException("HUD renderer pass: " + HudUiPassNames[i]);
            _hudBasePassesField = AccessTools.Field(renderer, "m_BasePasses") ??
                throw new MissingFieldException("HUD renderer base setup missing");
            _hudBasePassFields = new FieldInfo[HudBasePassNames.Length];
            for (int i = 0; i < HudBasePassNames.Length; ++i)
                _hudBasePassFields[i] = AccessTools.Field(_hudBasePassesField.FieldType, HudBasePassNames[i]) ??
                    throw new MissingFieldException("HUD base setup pass: " + HudBasePassNames[i]);
            _hudEmptyFeatures = Activator.CreateInstance(features.ReturnType);
            _harmony.Patch(features, prefix: new HarmonyMethod(typeof(Main), nameof(HudFeaturePrefixFactory)));
            _harmony.Patch(enqueue, prefix: new HarmonyMethod(typeof(Main), nameof(HudEnqueuePassPrefix)));
            // Recompile this large caller after patching its small getters /
            // enqueuer: a Mono JIT may have inlined those before VR started.
            _harmony.Patch(AccessTools.Method(waaagh, "Setup"), transpiler:
                new HarmonyMethod(typeof(Main), nameof(HudRendererRecompile)));
            _harmony.Patch(AccessTools.Method(_hudBasePassesField.FieldType, "Setup"), transpiler:
                new HarmonyMethod(typeof(Main), nameof(HudRendererRecompile)));
            _hudRendererRestrictionsReady = true;
        }

        // Exact byref return type avoids boxing / replacing the renderer's own
        // list. Outside the two capture cameras the getter executes unchanged.
        static DynamicMethod HudFeaturePrefixFactory(MethodBase original) => BuildHudFeaturePrefix(((MethodInfo)original).ReturnType);
        static IEnumerable<CodeInstruction> HudRendererRecompile(IEnumerable<CodeInstruction> instructions) => instructions;
        internal static DynamicMethod BuildHudFeaturePrefix(Type listType)
        {
            var method = new DynamicMethod("RTMaquetaXR_HudFeatures", typeof(bool),
                new[] { listType.MakeByRefType() }, typeof(Main), true);
            method.DefineParameter(1, ParameterAttributes.None, "__result");
            var il = method.GetILGenerator(); var ordinary = il.DefineLabel();
            il.Emit(OpCodes.Call, AccessTools.Method(typeof(Main), nameof(InHudRenderScope)));
            il.Emit(OpCodes.Brfalse_S, ordinary);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldsfld, AccessTools.Field(typeof(Main), nameof(_hudEmptyFeatures)));
            il.Emit(OpCodes.Castclass, listType); il.Emit(OpCodes.Stind_Ref);
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            il.MarkLabel(ordinary); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
            return method;
        }

        static bool HudEnqueuePassPrefix(object __instance, object __0)
        {
            if (!InHudRenderScope()) return true;
            if (!_hudUiPasses.TryGetValue(__instance, out HashSet<object> allowed))
            {
                // A game renderer can be replaced during a transition. Do not
                // throw, scan metadata or destroy Unity resources inside SRP.
                _hudRenderFailurePending = true; return false;
            }
            return allowed.Contains(__0);
        }

        static void PrepareHudRendererPasses(object cameraData)
        {
            var getter = AccessTools.PropertyGetter(cameraData.GetType(), "ScriptableRenderer") ??
                throw new MissingMemberException("HUD camera has no ScriptableRenderer");
            object renderer = getter.Invoke(cameraData, null);
            CacheHudRendererPasses(renderer);
        }

        static void CacheHudRendererPasses(object renderer)
        {
            if (renderer == null) throw new InvalidOperationException("HUD camera renderer unavailable");
            if (_hudUiPasses.ContainsKey(renderer)) return;
            var allowed = new HashSet<object>();
            foreach (var field in _hudUiPassFields)
            {
                object pass = field.GetValue(renderer);
                if (pass == null) throw new InvalidOperationException("Required UI pass unavailable: " + field.Name);
                allowed.Add(pass);
            }
            object basePasses = _hudBasePassesField.GetValue(renderer);
            if (basePasses == null)
            {
                // SetupBasePasses performs this same lazy construction in the
                // installed game. Do it before enabling the camera so the SRP
                // hot path only sees already validated pass identities.
                basePasses = Activator.CreateInstance(_hudBasePassesField.FieldType, true);
                _hudBasePassesField.SetValue(renderer, basePasses);
            }
            foreach (var field in _hudBasePassFields)
            {
                object pass = field.GetValue(basePasses);
                if (pass == null) throw new InvalidOperationException("Required UI base pass unavailable: " + field.Name);
                allowed.Add(pass);
            }
            _hudUiPasses.Add(renderer, allowed);
        }
    }
}
