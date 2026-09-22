using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    // Pool/layout changes invalidate one source, not the whole world HUD.
    internal sealed class WorldInformationHierarchy72 : MonoBehaviour
    {
        internal Component Source;
        void OnTransformChildrenChanged() { Main.InvalidateWorldInformation72(Source); }
        void OnEnable() { Main.InvalidateWorldInformation72(Source); }
    }

    public static partial class Main
    {
        sealed class WorldGraphicState72
        {
            internal WorldInformationSource70 Source;
            internal bool Tactical, Textual, Hidden, NativeCull;
        }
        static readonly Dictionary<Graphic, WorldGraphicState72> _worldGraphicStates72 = new Dictionary<Graphic, WorldGraphicState72>();
        static readonly HashSet<WorldInformationSource70> _worldDirtySources72 = new HashSet<WorldInformationSource70>();
        static readonly List<WorldInformationSource70> _worldDirtyWork72 = new List<WorldInformationSource70>();
        static readonly List<WorldInformationSource70> _worldInteractionWork72 = new List<WorldInformationSource70>();
        static readonly Dictionary<Transform, WorldInformationSource70> _worldSourceRoots72 = new Dictionary<Transform, WorldInformationSource70>();
        static readonly List<Graphic> _worldGraphicsDead72 = new List<Graphic>();
        static readonly List<WorldInformationHierarchy72> _worldHierarchyWatchers72 = new List<WorldInformationHierarchy72>();
        static bool _worldPresentationHooks72, _worldPresentationRestoring72;
        static long _worldRebuildsSkipped72, _worldVisibilityTransitions72, _worldInspectionStarts72, _worldInspectionStops72, _worldInspectionReconciles72;
        static int _worldTacticalDraws72, _worldTacticalFallback72;
        static float _worldInspectionNext72, _worldDialogueNext72;
        static string _worldDialogueCached72;
        static Func<object, bool> _mapObjectFrustumNative72;
        static MethodInfo _mapObjectViewUpdate72, _mapObjectUnitsNear72, _mapObjectCanInteract72;

        static void InstallWorldInformation72()
        {
            if (_worldPresentationHooks72) return;
            _worldPresentationHooks72 = true;
            try
            {
                _harmony.Patch(AccessTools.DeclaredMethod(typeof(Graphic), "Rebuild"), prefix: new HarmonyMethod(typeof(Main), nameof(WorldGraphicRebuildPrefix72)));
                var tmp = AccessTools.TypeByName("TMPro.TextMeshProUGUI");
                if (tmp != null)
                {
                    _harmony.Patch(AccessTools.DeclaredMethod(tmp, "Rebuild"), prefix: new HarmonyMethod(typeof(Main), nameof(WorldGraphicRebuildPrefix72)));
                    _harmony.Patch(AccessTools.DeclaredMethod(tmp, "OnPreRenderCanvas"), prefix: new HarmonyMethod(typeof(Main), nameof(WorldTextRenderPrefix72)));
                }
                _harmony.Patch(AccessTools.DeclaredMethod(typeof(MaskableGraphic), "Cull"), prefix: new HarmonyMethod(typeof(Main), nameof(WorldGraphicCullPrefix72)),
                    postfix: new HarmonyMethod(typeof(Main), nameof(WorldGraphicCullPostfix72)));
                _harmony.Patch(AccessTools.DeclaredMethod(typeof(Graphic), "OnEnable"), postfix: new HarmonyMethod(typeof(Main), nameof(WorldGraphicEnabled72)));
                Canvas.willRenderCanvases += EnforceCoverage77;

                var vm = AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.Overtips.MapObject.OvertipMapObjectVM");
                var view = AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.View.Overtips.MapObject.OvertipMapObjectInteractionView");
                _mapObjectUnitsNear72 = AccessTools.DeclaredMethod(vm, "UpdateUnitsNear");
                _mapObjectCanInteract72 = AccessTools.DeclaredMethod(vm, "UpdateCanInteract");
                _mapObjectViewUpdate72 = AccessTools.DeclaredMethod(view, "UpdateVisibility");
                var entity = AccessTools.TypeByName("Kingmaker.EntitySystem.Entities.Base.Entity");
                _mapObjectFrustumNative72 = (Func<object, bool>)TouchSelectionCallFactory.Build(typeof(Func<object, bool>), AccessTools.PropertyGetter(entity, "IsInCameraFrustum"));
                _harmony.Patch(_mapObjectUnitsNear72, transpiler: new HarmonyMethod(typeof(Main), nameof(MapObjectFrustumTranspiler72)));
                var baseMapView = AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.View.Overtips.MapObject.BaseOvertipMapObjectView");
                _harmony.Patch(AccessTools.PropertyGetter(baseMapView, "CheckCanBeVisible"), transpiler: new HarmonyMethod(typeof(Main), nameof(MapObjectFrustumTranspiler72)));
                _harmony.Patch(AccessTools.PropertyGetter(view, "CheckVisibleTrigger"), postfix: new HarmonyMethod(typeof(Main), nameof(MapObjectHighlightVisibility72)));
                _harmony.Patch(AccessTools.DeclaredMethod(view, "BindViewImplementation"), postfix: new HarmonyMethod(typeof(Main), nameof(MapObjectBound72)));
                _log?.Log("[ui72] Selective world graphics, native interaction visibility and VR proximity installed");
            }
            catch (Exception e) { _log?.Error("[ui72] Presentation contracts: " + e); }
        }

        internal static void InvalidateWorldInformation72(Component view)
        {
            if (!_worldPresentationRestoring72 && view != null && _worldInformationSources70.TryGetValue(view, out var source))
                _worldDirtySources72.Add(source);
        }
        static void WorldGraphicEnabled72(Graphic __instance)
        {
            if (_worldPresentationRestoring72 || !_active || !_attached || __instance == null) return;
            for (Transform node = __instance.transform; node != null; node = node.parent)
                if (_worldSourceRoots72.TryGetValue(node, out var source)) { _worldDirtySources72.Add(source); break; }
        }
        static bool WorldGraphicSuppressed72(Graphic graphic) => !_worldPresentationRestoring72 && _active && _attached && !_modeFlat &&
            graphic != null && (CombatPresentationContext74&&_coverageGraphics74.ContainsKey(graphic) || _worldGraphicStates72.TryGetValue(graphic, out var state) && state.Hidden);
        static bool WorldGraphicRebuildPrefix72(Graphic __instance, CanvasUpdate __0)
        {
            // Hidden combat graphics also skip layout in block 4A. Other
            // contexts retain native layout; reveal reschedules via SetAllDirty.
            if (!WorldGraphicSuppressed72(__instance)) return true;
            if(__0!=CanvasUpdate.PreRender&&!(Presentation74(1)&&CombatPresentationContext74&&
                _worldGraphicStates72.TryGetValue(__instance,out var state)&&
                (state.Source.Family&WorldHudPolicy.AttackFamily)!=0))return true;
            if (__instance.canvasRenderer != null) __instance.canvasRenderer.cull = true;
            ++_worldRebuildsSkipped72; return false;
        }
        static bool WorldTextRenderPrefix72(Graphic __instance)
        {
            if (!WorldGraphicSuppressed72(__instance)) return true;
            ++_worldRebuildsSkipped72; return false;
        }
        static void WorldGraphicCullPostfix72(MaskableGraphic __instance)
        {
            if(__instance!=null&&CombatPresentationContext74&&_coverageGraphics74.ContainsKey(__instance))
            {_coverageGraphics74[__instance]=__instance.canvasRenderer.cull;__instance.canvasRenderer.cull=true;return;}
            if (_worldPresentationRestoring72 || __instance == null || !_worldGraphicStates72.TryGetValue(__instance, out var state)) return;
            var renderer = __instance.canvasRenderer; if (renderer == null) return;
            state.NativeCull = renderer.cull;
            if (WorldGraphicSuppressed72(__instance)) renderer.cull = true;
        }
        static void WorldGraphicCullPrefix72(MaskableGraphic __instance)
        {
            if(__instance!=null&&CombatPresentationContext74&&_coverageGraphics74.TryGetValue(__instance,out bool nativeCull))
            {__instance.canvasRenderer.cull=nativeCull;return;}
            if(WorldGraphicSuppressed72(__instance)&&__instance.canvasRenderer!=null&&_worldGraphicStates72.TryGetValue(__instance,out var state))
                __instance.canvasRenderer.cull=state.NativeCull;
        }

        static bool IsTacticalPart72(Component part)
        {
            string name = part == null ? "" : part.GetType().Name;
            return name == "OvertipAimView" || name == "OvertipHitChanceBlockView" || name == "OvertipUnitHealthBlockView" ||
                name == "OvertipTargetDefensesView" || name == "OvertipNameBlockView" || name == "OvertipPointBlockPCView";
        }
        static bool IsTextBlock72(Component part)
        {
            string name = part == null ? "" : part.GetType().Name;
            return name == "MapObjectOvertipNameBlockView" || name == "OvertipBarkBlockView";
        }
        static bool UnderPart72(Graphic graphic, Component part) => part != null &&
            (graphic.transform == part.transform || graphic.transform.IsChildOf(part.transform));

        static void RegisterWorldGraphics72(WorldInformationSource70 source)
        {
            if (source?.View == null) return;
            source.PresentationState74=-1;
            _worldSourceRoots72[source.View.transform] = source;
            source.TacticalGraphics72.Clear();
            bool standardCover75=StandardCover75(source);
            RegisterCoverPieces76(source,standardCover75);
            // Ancestor callbacks catch nested TMP submeshes/pooled rows, which a
            // direct childCount comparison cannot detect.
            foreach (var part in source.Parts)
            {
                if (part == null) continue;
                var watcher = part.GetComponent<WorldInformationHierarchy72>();
                if (watcher == null) { watcher = part.gameObject.AddComponent<WorldInformationHierarchy72>(); _worldHierarchyWatchers72.Add(watcher); }
                watcher.Source = source.View;
            }
            foreach (var graphic in source.Texts)
            {
                if (graphic == null) continue;
                bool tactical = false, textBlock = false, excluded = _coverageGraphics74.ContainsKey(graphic);
                foreach (var part in source.Parts)
                {
                    if (!UnderPart72(graphic, part)) continue;
                    tactical |= IsTacticalPart72(part); textBlock |= IsTextBlock72(part);
                    string name = part.GetType().Name;
                    bool cover75=CoverDecoration75(part,standardCover75);
                    excluded |= cover75 || name == "OvertipBarkBlockView";
                    if(cover75)RegisterCoverageGraphic74(graphic,source);
                }
                bool text = textBlock || graphic.GetType().Name.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0;
                // TMP's material submeshes inherit their parent text's purpose.
                if (!text && graphic.GetType().Name == "TMP_SubMeshUI") text = true;
                if (!_worldGraphicStates72.TryGetValue(graphic, out var state))
                    _worldGraphicStates72.Add(graphic, state = new WorldGraphicState72 { NativeCull = graphic.canvasRenderer != null && graphic.canvasRenderer.cull });
                state.Source = source; state.Tactical = tactical && !excluded; state.Textual = text;
                if (state.Tactical) source.TacticalGraphics72.Add(graphic);
                if ((source.Family & WorldHudPolicy.InteractionFamily) != 0 && textBlock)
                {
                    source.InteractionTextRenderers.Add(graphic.canvasRenderer);
                    _interactionGraphics70.Remove(graphic); _interactionTargets70.Remove(graphic); source.InteractionGraphics.Remove(graphic);
                }
            }
            ApplyWorldSourcePresentation72(source);
            if(standardCover75 && AllDiagnosticsEnabled)
            {
                int owned = _coverageOwners77.TryGetValue(source,out var covered78) ? covered78.Count : 0;
                _log.Log("[coverage78/binding] type="+source.View.GetType().FullName+" graphics="+owned+
                    (owned==0 ? " UNRESOLVED: no native cover pieces discovered" : " ownership registered; final cull/draw veto active in combat"));
            }
        }

        static void ApplyWorldSourcePresentation72(WorldInformationSource70 source)
        {
            bool interaction = (source.Family & WorldHudPolicy.InteractionFamily) != 0;
            bool selected = _tacticalInspectionHeld71 && _tacticalInspectionSources71.Contains(source);
            bool migrated=interaction&&ProximityOwnsNativeMarker74(source.View);
            int revision=(interaction?1:0)|(selected?2:0)|(migrated?4:0);
            if(Presentation74(4)&&source.PresentationState74==revision)return;
            source.PresentationState74=revision;
            foreach (var graphic in source.Texts)
            {
                if (graphic == null || !_worldGraphicStates72.TryGetValue(graphic, out var state) || !ReferenceEquals(state.Source, source)) continue;
                bool hidden = interaction ? state.Textual || migrated : !(selected && state.Tactical);
                var renderer = graphic.canvasRenderer;
                if (renderer == null) continue;
                if (hidden != state.Hidden)
                {
                    if (hidden) state.NativeCull = renderer.cull;
                    state.Hidden = hidden; ++_worldVisibilityTransitions72;
                    if (!hidden) { renderer.cull = state.NativeCull; graphic.SetAllDirty(); }
                }
                if (hidden && !renderer.cull) { renderer.cull = true; ++_suppressedWorldDraws70; }
            }
        }
        static void ReconcileWorldGraphics72()
        {
            if(Time.frameCount%120==0)
            {
                _worldGraphicsDead72.Clear();
                foreach(var pair in _worldGraphicStates72)
                    if(pair.Key==null||pair.Value.Source.View==null||
                        (pair.Key.transform!=pair.Value.Source.View.transform&&!pair.Key.transform.IsChildOf(pair.Value.Source.View.transform)))_worldGraphicsDead72.Add(pair.Key);
                foreach(var graphic in _worldGraphicsDead72)
                {
                    var state=_worldGraphicStates72[graphic];
                    if(graphic!=null&&state.Hidden){graphic.canvasRenderer.cull=state.NativeCull;graphic.SetAllDirty();}
                    _worldGraphicStates72.Remove(graphic);
                }
                _worldHierarchyWatchers72.RemoveAll(w=>w==null);
            }
            _worldDirtyWork72.Clear(); _worldDirtyWork72.AddRange(_worldDirtySources72); _worldDirtySources72.Clear();
            foreach (var source in _worldDirtyWork72) if (source.View != null) RefreshWorldInformationSourceHierarchy70(source);
            _worldDirtyWork72.Clear();
        }
        static void EnforceWorldPresentation72()
        {
            if (!_active || !_attached || _modeFlat || _worldPresentationRestoring72) return;
            // Coverage is unconditional in combat, including Y. Reassert on
            // every capture; the generic presentation cadence is independent.
            EnforceCoverage77();
            if(Presentation74(4)&&Time.frameCount%120!=0)return;
            foreach (var pair in _worldGraphicStates72)
                if (pair.Key != null && pair.Value.Hidden && pair.Key.canvasRenderer != null && !pair.Key.canvasRenderer.cull)
                    pair.Key.canvasRenderer.cull = true;
        }
        static void CollectTacticalGraphics72(List<Graphic> graphics)
        {
            _worldTacticalDraws72 = _worldTacticalFallback72 = 0;
            if (!_tacticalInspectionHeld71) return;
            foreach (var source in _tacticalInspectionSources71)
                foreach (var graphic in source.TacticalGraphics72)
                    if (graphic != null && !CoverageSuppressed77(graphic) && _worldGraphicStates72.TryGetValue(graphic,out var state) &&
                        ReferenceEquals(state.Source,source) && state.Tactical && !state.Hidden) graphics.Add(graphic);
        }
        static bool IsTacticalGraphic72(Graphic graphic) => graphic != null && _worldGraphicStates72.TryGetValue(graphic, out var state) && state.Tactical && !state.Hidden;

        static bool NativeStateBool72(object state, string field) => ReactiveValue70(ReadField70(state, field)) is bool value && value;
        static bool IsTacticalTarget72(WorldInformationSource70 source, WorldInformationSource70 pointed)
        {
            if(CombatPresentationContext74&&(_presentationReady74&2)!=0)return TargetIncluded74(source,pointed);
            object state = ReadField70(source.ViewModel, "UnitState");
            object hit = ReadField70(source.ViewModel, "HitChanceBlockVM");
            if (state == null || !NativeStateBool72(state, "IsVisibleForPlayer") || NativeStateBool72(state, "IsCaster") || NativeStateBool72(hit, "IsCaster")) return false;
            if (NativeTacticalPreviewActive70)
                return NativeStateBool72(state, "IsAoETarget") || NativeStateBool72(hit, "HasHit");
            return ReferenceEquals(source, pointed) || NativeStateBool72(hit, "HasHit");
        }
        static void ReconcileTacticalInspection72(bool requested)
        {
            if(!requested&&!_tacticalInspectionHeld71)return;
            bool transition=requested!=_tacticalInspectionHeld71;
            if(transition){if(requested)++_worldInspectionStarts72;else ++_worldInspectionStops72;}
            _tacticalInspectionHeld71=requested;
            // Targets arrive by native revision. For ordinary unit pointing,
            // cheap membership comparisons retain the same visual set.
            var pointed=requested&&_attackTargets74.Count==0?FindPointedWorldInformation70():null;
            if(Presentation74(2)&&requested&&!transition&&_inspectionRevision74==_attackRevision74&&
                _attackTargets74.Count>0){++_inspectionReuse74;return;}
            if(!Presentation74(2)&&requested&&!transition&&Time.unscaledTime<_worldInspectionNext72)return;
            _worldInspectionNext72=Time.unscaledTime+.10f;++_worldInspectionReconciles72;
            _inspectionRevision74=_attackRevision74;
            _inspectionWork74.Clear();
            foreach(var source in _worldInformationSources70.Values)
                if((source.Family&WorldHudPolicy.AttackFamily)!=0)_inspectionWork74.Add(source);
            foreach(var source in _inspectionWork74)
            {
                bool selected=requested&&WorldSourceCurrent75(source)&&IsTacticalTarget72(source,pointed);
                bool was=_tacticalInspectionSources71.Contains(source);
                if(selected)_tacticalInspectionSources71.Add(source);else _tacticalInspectionSources71.Remove(source);
                if(selected&&(!was||transition))
                {
                    if(!RefreshInspectionSource75(source))
                    {selected=false;_tacticalInspectionSources71.Remove(source);}
                }
                if(was!=selected||!Presentation74(4))ApplyWorldSourcePresentation72(source);
            }
            _inspectionWork74.Clear();
            if(!requested){_worldTacticalDraws72=_worldTacticalFallback72=0;_worldHudCollected68=-1;}
        }

        static IEnumerable<CodeInstruction> MapObjectFrustumTranspiler72(IEnumerable<CodeInstruction> input)
        {
            var result = new List<CodeInstruction>(); int replaced = 0;
            foreach (var op in input)
            {
                if ((op.opcode == OpCodes.Callvirt || op.opcode == OpCodes.Call) && op.operand is MethodInfo method && method.Name == "get_IsInCameraFrustum")
                { op.opcode = OpCodes.Call; op.operand = AccessTools.Method(typeof(Main), nameof(MapObjectInVrView72)); ++replaced; }
                result.Add(op);
            }
            if (replaced != 1) throw new InvalidOperationException("Expected one map-object proximity frustum check");
            return result;
        }
        static bool MapObjectInVrView72(object entity)
        {
            if (_active && _attached && !_modeFlat && !InSpaceCombat && !InNavigationMap)
            {
                var camera = HudWorldPickingCamera();
                if (camera != null && TryGetProp(entity, "Position") is Vector3 position)
                { var p = camera.WorldToViewportPoint(position); return p.z > 0 && p.x >= -.1f && p.x <= 1.1f && p.y >= -.1f && p.y <= 1.1f; }
            }
            return entity != null && _mapObjectFrustumNative72 != null && _mapObjectFrustumNative72(entity);
        }
        static void MapObjectHighlightVisibility72(Component __instance, ref bool __result)
        {
            if (__result || !TouchExplorationHighlightAllowed72 || __instance == null) return;
            object entity = ReadField70(GetViewModel(__instance), "MapObjectEntity");
            var contracts = _touchLootContracts65;
            if (contracts != null && entity != null && (contracts.Eligible(entity)||TouchProximityYReveal73(entity))) __result = true;
            // The caller STILL checks CheckCanBeVisible: no reveal/fog bypass.
        }
        static void MapObjectBound72(Component __instance)
        {
            if (!_active || !_attached || _modeFlat || __instance == null) return;
            RegisterWorldInformationSource70(__instance, GetViewModel(__instance));
            _nativeWorldViews66.Add(__instance); _nativeWorldTransforms66.Add(__instance.transform);
        }
        static void RefreshNativeInteractions72()
        {
            if (!_active || !_attached || _modeFlat || InSpaceCombat || InNavigationMap || TouchRadialCombatNow(out _) || Time.unscaledTime < _interactionRefreshNext71) return;
            _interactionRefreshNext71 = Time.unscaledTime + .20f;
            // Native refresh may bind another pooled view. Never enumerate the
            // registry across a callback that can add or remove a source.
            _worldInteractionWork72.Clear();
            foreach (var source in _worldInformationSources70.Values)
                if ((source.Family & WorldHudPolicy.InteractionFamily) != 0) _worldInteractionWork72.Add(source);
            foreach (var source in _worldInteractionWork72)
            {
                if ((source.Family & WorldHudPolicy.InteractionFamily) == 0 || source.View == null || source.ViewModel == null) continue;
                try
                {
                    if(!source.ProximityClassified73&&TouchProximityContractsReady73)
                    {source.ProximityOnly73=ProximityOwnsNativeMarker74(source.View);source.ProximityClassified73=true;}
                    if (_mapObjectUnitsNear72 != null && _mapObjectUnitsNear72.DeclaringType.IsInstanceOfType(source.ViewModel))
                    { _mapObjectUnitsNear72.Invoke(source.ViewModel, null); _mapObjectCanInteract72?.Invoke(source.ViewModel, null); }
                    // Native reactive visibility changes drive their own tween. Do not restart it every 200 ms.
                    if(ProximityOwnsNativeMarker74(source.View))ApplyWorldSourcePresentation72(source);
                }
                catch (Exception e) { ReportTouchLootHighlight65(e); }
            }
            _worldInteractionWork72.Clear();
        }
        static void ReleaseWorldPresentation72()
        {
            _worldPresentationRestoring72 = true;
            try
            {
                foreach (var pair in _worldGraphicStates72)
                    if (pair.Key != null && pair.Value.Hidden) { pair.Key.canvasRenderer.cull = pair.Value.NativeCull; pair.Key.SetAllDirty(); }
                foreach (var watcher in _worldHierarchyWatchers72) if (watcher != null) UnityEngine.Object.Destroy(watcher);
                _worldGraphicStates72.Clear(); _worldSourceRoots72.Clear(); _worldDirtySources72.Clear(); _worldDirtyWork72.Clear(); _worldHierarchyWatchers72.Clear(); _worldInteractionWork72.Clear();
                _worldDialogueCached72 = null; _worldDialogueNext72 = _worldInspectionNext72 = 0;
            }
            finally { _worldPresentationRestoring72 = false; }
        }
    }
}
