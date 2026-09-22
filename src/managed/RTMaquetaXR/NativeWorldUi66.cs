using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static bool _nativeWorldHooks66;
        static FieldInfo _nativeWorldPosition66, _nativePointUnit66;
        static MethodInfo _nativePointVisibility66;
        static bool _nativeRestoring66;
        static readonly Dictionary<Component,bool> _nativeMarkerWanted66=new Dictionary<Component,bool>();
        static readonly HashSet<Transform> _nativeWorldTransforms66=new HashSet<Transform>();
        static readonly HashSet<Component> _nativeWorldViews66=new HashSet<Component>();
        static readonly List<Component> _nativeWorldDead66=new List<Component>();
        static readonly List<Component> _nativeWorldParts66=new List<Component>(64);
        static readonly List<Graphic> _nativeWorldGraphics66=new List<Graphic>(32);
        static readonly List<CanvasGroup> _nativeWorldGroups66=new List<CanvasGroup>(8);
        static readonly List<Transform> _nativeCombatAnchors68=new List<Transform>(16);
        struct NativeCombatOffset68 { internal Vector3 Before,After; }
        sealed class NativeWorldCache69
        {
            internal object ViewModel;
            internal readonly List<Transform> CombatAnchors=new List<Transform>(8);
            internal readonly List<Component> HealthParts=new List<Component>(2);
        }
        static readonly Dictionary<Transform,NativeCombatOffset68> _nativeCombatOffsets68=new Dictionary<Transform,NativeCombatOffset68>();
        static readonly Dictionary<Component,bool> _nativeHealthHidden68=new Dictionary<Component,bool>();
        static readonly Dictionary<Component,NativeWorldCache69> _nativeWorldCache69=new Dictionary<Component,NativeWorldCache69>();
        static FieldInfo _nativeUnitState68,_nativeHealthState68,_nativeMouseOver68,_nativeHoverAbility68,_nativeAoeTarget68;
        static MethodInfo _nativeHealthVisibility68;
        static long _nativeWorldCacheBuilds69,_nativeWorldHierarchyScans69,_nativeWorldOffsetWrites69,_nativeWorldHealthEvaluations69;
        static readonly Dictionary<int,string> _nativeWorldAudit66=new Dictionary<int,string>();
        static int _nativeWorldAuditBudget66=24;
        static int _nativeWorldEligibleInteractions66,_nativeWorldEligibleAttacks66,_nativeWorldEligibleBarks66;
        static void InstallNativeWorldUi66()
        {
            if(_nativeWorldHooks66)return;
            _nativeWorldHooks66=true;
            InstallWorldInformation72();
            InstallCombatPresentation74();
            InstallDialogLayout75();
            try
            {
                var vm=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.Overtips.OvertipEntityVM");
                _nativeWorldPosition66=AccessTools.Field(vm,"Position");
                if(_nativeWorldPosition66==null)throw new MissingFieldException("OvertipEntityVM.Position");
                var patched=new HashSet<MethodBase>();
                foreach(var type in vm.Assembly.GetTypes())
                {
                    if(type.ContainsGenericParameters || !typeof(Component).IsAssignableFrom(type))continue;
                    for(var baseType=type.BaseType;baseType!=null;baseType=baseType.BaseType)
                    {
                        if(!baseType.IsGenericType || baseType.GetGenericTypeDefinition().FullName!="Kingmaker.Code.UI.MVVM.View.Overtips.BaseOvertipView`1")continue;
                        var method=AccessTools.DeclaredMethod(baseType,"GetCurrentCanvasPosition");
                        if(method!=null&&patched.Add(method))_harmony.Patch(method,prefix:new HarmonyMethod(typeof(Main),nameof(NativeWorldPositionPrefix66)));
                        break;
                    }
                }
                var marker=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.View.PointMarkers.PointMarkerPCView");
                _nativePointUnit66=AccessTools.Field(AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.PointMarkers.PointMarkerVM"),"Unit");
                _nativePointVisibility66=AccessTools.Method(marker,"SetVisibility");
                _harmony.Patch(_nativePointVisibility66,prefix:new HarmonyMethod(typeof(Main),nameof(NativeDirectionVisibilityPrefix66)));
                _harmony.Patch(AccessTools.Method(marker,"HandleUpdate"),prefix:new HarmonyMethod(typeof(Main),nameof(NativeDirectionUpdatePrefix66)));
                var unitVm=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.Overtips.Unit.OvertipEntityUnitVM");
                var unitStateType=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.Common.UnitState.UnitState");
                var health=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.View.Overtips.Unit.UnitOvertipParts.OvertipUnitHealthBlockView");
                _nativeUnitState68=AccessTools.Field(unitVm,"UnitState");
                _nativeMouseOver68=AccessTools.Field(unitStateType,"IsMouseOverUnit");
                _nativeHoverAbility68=AccessTools.Field(unitStateType,"HoverSelfTargetAbility");
                _nativeAoeTarget68=AccessTools.Field(unitStateType,"IsAoETarget");
                _nativeHealthState68=AccessTools.Field(AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.Overtips.Unit.UnitOvertipParts.OvertipHealthBlockVM"),"UnitState");
                _nativeHealthVisibility68=AccessTools.Method(health,"DoVisibility");
                _harmony.Patch(AccessTools.PropertyGetter(health,"IsVisible"),postfix:new HarmonyMethod(typeof(Main),nameof(NativeHealthVisible68)));
                var patternType=AccessTools.TypeByName("Kingmaker.UI.Pointer.AbilityTarget.AbilityPatternRange");
                var preview=AccessTools.Method(patternType,"SetRangeToWorldPosition",new[]{typeof(Vector3),typeof(bool)});
                if(preview==null)throw new MissingMethodException(patternType?.FullName,"SetRangeToWorldPosition");
                _harmony.Patch(preview,postfix:new HarmonyMethod(typeof(Main),nameof(ObserveTacticalPreviewPostfix70)));
                _log.Log("[ui66] Native overtip headset projection and unit direction marker filter installed: "+patched.Count);
            }
            catch(Exception e){_log.Error("[ui66] Native world UI contract: "+e);}
        }
        static bool TryNativeWorldPosition66(object vm,out Vector3 point)
        {
            point=default(Vector3);
            if(vm==null||_nativeWorldPosition66==null||!_nativeWorldPosition66.DeclaringType.IsInstanceOfType(vm))return false;
            object reactive=_nativeWorldPosition66.GetValue(vm);
            object value=TryGetProp(reactive,"Value");
            if(!(value is Vector3))return false;
            point=(Vector3)value;return TouchTabletopState.Finite(TablePoint(point));
        }
        static bool NativeWorldPositionPrefix66(Component __instance,ref Vector3? __result)
        {
            if(!_active||!_attached||_modeFlat||!EffectiveWorldOvertips||__instance==null||!IsWorldHudTransform(__instance.transform))return true;
            if(Presentation74(4)&&CombatPresentationContext74&&
                (WorldHudPolicy.ProtectedFamily(__instance.GetType())&(WorldHudPolicy.AttackFamily|WorldHudPolicy.BarkFamily))!=0&&
                !ShouldRevealWorldInformation71(__instance))
            {
                if(!_worldInformationSources70.ContainsKey(__instance))
                {RegisterWorldInformationSource70(__instance,GetViewModel(__instance));_nativeWorldViews66.Add(__instance);_nativeWorldTransforms66.Add(__instance.transform);}
                __result=null;++_positionSkips74;return false;
            }
            if(!TryNativeWorldPosition66(GetViewModel(__instance),out var position))return true;
            RegisterWorldInformationSource70(__instance,GetViewModel(__instance));
            _nativeWorldViews66.Add(__instance);_nativeWorldTransforms66.Add(__instance.transform);
            var camera=HudWorldPickingCamera();if(camera==null)return true;
            // Native CheckVisibility / combat rules / bark lifetime still run.
            // Only replace the desktop rectangle test. The actual widget is
            // positioned at its entity after native canvas updates in Runner.
            var viewport=camera.WorldToViewportPoint(position);
            __result=viewport.z>0 ? (Vector3?)Vector3.zero : null;
            return false;
        }
        static bool IsNativeUnitDirection66(Component view)
        {
            if(_nativeRestoring66||!_active||_modeFlat||(!InSpaceCombat&&!EffectiveWorldOvertips)||view==null||_nativePointUnit66==null)return false;
            var model=GetViewModel(view);
            return model!=null && _nativePointUnit66.DeclaringType.IsInstanceOfType(model) && _nativePointUnit66.GetValue(model)!=null;
        }
        static void NativeDirectionVisibilityPrefix66(Component __instance,ref bool __0)
        {
            // Exact UNIT directional pointers only; retain native desired state
            // to restore when leaving VR. Interactions and barks are separate VMs.
            if(!IsNativeUnitDirection66(__instance))return;
            _nativeMarkerWanted66[__instance]=__0;__0=false;
        }
        static bool NativeDirectionUpdatePrefix66(Component __instance)
        {
            if(!IsNativeUnitDirection66(__instance))return true;
            // Also covers an already-visible marker at attachment, once only.
            if(!_nativeMarkerWanted66.ContainsKey(__instance))
            {
                bool shown=__instance.gameObject.activeSelf;
                _nativeMarkerWanted66[__instance]=shown;
                if(shown)
                {
                    _nativeRestoring66=true;
                    try{_nativePointVisibility66.Invoke(__instance,new object[]{false});}
                    finally{_nativeRestoring66=false;}
                }
            }
            return false;
        }
        static void ReleaseNativeWorldUi66()
        {
            _nativeRestoring66=true;
            try{foreach(var item in _nativeMarkerWanted66)if(item.Key!=null)_nativePointVisibility66?.Invoke(item.Key,new object[]{item.Value});}
            catch(Exception e){_log.Error("[ui66] Restoring native pointers: "+e.Message);}
            finally{RestoreNativeCombatOffsets68();RestoreNativeHealth68();ReleaseWorldInformation70();_nativeRestoring66=false;_nativeMarkerWanted66.Clear();_nativeWorldViews66.Clear();_nativeWorldTransforms66.Clear();_nativeWorldCache69.Clear();
                _nativeWorldAudit66.Clear();_nativeWorldAuditBudget66=24;_nativeWorldEligibleInteractions66=_nativeWorldEligibleAttacks66=_nativeWorldEligibleBarks66=0;}
        }

        static void RestoreNativeCombatOffsets68()
        {
            foreach(var item in _nativeCombatOffsets68)
                // Native layout may already have replaced our value this frame.
                // Only undo a value still owned by us, never subtract from a
                // freshly written native position (which would drift downward).
                if(item.Key!=null&&(item.Key.localPosition-item.Value.After).sqrMagnitude<1e-8f)
                    item.Key.localPosition=item.Value.Before;
            _nativeCombatOffsets68.Clear();
        }

        static bool IsCombatInformationPart68(Component part)
        {
            if(part==null)return false;
            string name=part.GetType().FullName;
            return name.EndsWith("OvertipAimView",StringComparison.Ordinal)||
                name.EndsWith("OvertipHitChanceBlockView",StringComparison.Ordinal)||
                name.EndsWith("OvertipPointBlockPCView",StringComparison.Ordinal)||
                name.EndsWith("OvertipTargetDefensesView",StringComparison.Ordinal)||
                name.EndsWith("OvertipUnitHealthBlockView",StringComparison.Ordinal);
        }

        static void RestoreNativeCombatOffset69(Transform candidate)
        {
            if(candidate==null||!_nativeCombatOffsets68.TryGetValue(candidate,out var offset))return;
            if((candidate.localPosition-offset.After).sqrMagnitude<1e-8f)candidate.localPosition=offset.Before;
            _nativeCombatOffsets68.Remove(candidate);
        }

        static void ReleaseNativeWorldCache69(Component view)
        {
            if(view==null||!_nativeWorldCache69.TryGetValue(view,out var cache))return;
            foreach(var anchor in cache.CombatAnchors)RestoreNativeCombatOffset69(anchor);
            bool restoring=_nativeRestoring66;_nativeRestoring66=true;
            try
            {
                foreach(var health in cache.HealthParts)
                    if(health!=null&&_nativeHealthHidden68.Remove(health)&&GetViewModel(health)!=null)_nativeHealthVisibility68?.Invoke(health,null);
            }
            finally{_nativeRestoring66=restoring;}
            _nativeWorldCache69.Remove(view);
        }

        static NativeWorldCache69 NativeWorldCacheFor69(Component view,object vm)
        {
            if(_nativeWorldCache69.TryGetValue(view,out var existing)&&ReferenceEquals(existing.ViewModel,vm))return existing;
            if(existing!=null)ReleaseNativeWorldCache69(view);
            var cache=new NativeWorldCache69{ViewModel=vm};
            _nativeWorldParts66.Clear();view.GetComponentsInChildren(true,_nativeWorldParts66);
            ++_nativeWorldCacheBuilds69;_nativeWorldHierarchyScans69+=_nativeWorldParts66.Count;
            _nativeCombatAnchors68.Clear();
            foreach(var part in _nativeWorldParts66)
            {
                if(part==null)continue;
                if(_nativeHealthVisibility68!=null&&_nativeHealthVisibility68.DeclaringType.IsInstanceOfType(part))cache.HealthParts.Add(part);
                if(!IsCombatInformationPart68(part))continue;
                Transform candidate=part.transform;bool nested=false;
                foreach(var existingAnchor in _nativeCombatAnchors68)
                    if(candidate.IsChildOf(existingAnchor)){nested=true;break;}
                if(!nested&&!_nativeCombatAnchors68.Contains(candidate))_nativeCombatAnchors68.Add(candidate);
            }
            foreach(var candidate in _nativeCombatAnchors68)
            {
                bool nested=false;
                foreach(var other in _nativeCombatAnchors68)if(other!=candidate&&candidate.IsChildOf(other)){nested=true;break;}
                if(!nested)cache.CombatAnchors.Add(candidate);
            }
            _nativeWorldCache69.Add(view,cache);return cache;
        }

        static void LowerCombatInformation68(NativeWorldCache69 cache)
        {
            foreach(var candidate in cache.CombatAnchors)
            {
                if(candidate==null||candidate.parent==null)continue;
                if(!candidate.gameObject.activeInHierarchy){RestoreNativeCombatOffset69(candidate);continue;}
                if(_nativeCombatOffsets68.TryGetValue(candidate,out var previous)&&
                    (candidate.localPosition-previous.After).sqrMagnitude<1e-8f)continue;
                Vector3 localDelta=candidate.parent.InverseTransformVector(Vector3.down*OvertipHeight);
                var offset=new NativeCombatOffset68{Before=candidate.localPosition,After=candidate.localPosition+localDelta};
                candidate.localPosition=offset.After;_nativeCombatOffsets68[candidate]=offset;++_nativeWorldOffsetWrites69;
            }
        }

        static bool ReactiveBool68(FieldInfo field,object owner)
        {
            if(field==null||owner==null||!field.DeclaringType.IsInstanceOfType(owner))return false;
            object value=TryGetProp(field.GetValue(owner),"Value");return value is bool && (bool)value;
        }

        static bool NativeHealthContext68(object state)
        {
            return ReactiveBool68(_nativeMouseOver68,state)||ReactiveBool68(_nativeHoverAbility68,state)||ReactiveBool68(_nativeAoeTarget68,state);
        }

        static void NativeHealthVisible68(Component __instance,ref bool __result)
        {
            if(!__result||_nativeRestoring66||!_active||!_attached||_modeFlat||!EffectiveWorldOvertips||
                __instance==null||!IsWorldHudTransform(__instance.transform)||_nativeHealthState68==null)return;
            object vm=GetViewModel(__instance);
            if(vm!=null&&_nativeHealthState68.DeclaringType.IsInstanceOfType(vm))
                __result=NativeHealthContext68(_nativeHealthState68.GetValue(vm))||
                    TouchWorldInspectionRequested70&&_presentationModels74.TryGetValue(vm,out var owner)&&owner.Unit!=null&&_attackTargets74.ContainsKey(owner.Unit);
        }

        static void RestoreNativeHealth68()
        {
            bool restoring=_nativeRestoring66;_nativeRestoring66=true;
            try{foreach(var item in _nativeHealthHidden68)if(item.Key!=null&&GetViewModel(item.Key)!=null)_nativeHealthVisibility68?.Invoke(item.Key,null);}
            finally{_nativeHealthHidden68.Clear();_nativeRestoring66=restoring;}
        }

        static void UpdateNativeHealth68(object vm,NativeWorldCache69 cache)
        {
            if(_nativeHealthVisibility68==null||_nativeUnitState68==null)return;
            if(vm==null||!_nativeUnitState68.DeclaringType.IsInstanceOfType(vm))return;
            object unitState=_nativeUnitState68.GetValue(vm);
            object unit=ReadMember73(vm,"Unit");
            bool contextual=NativeHealthContext68(unitState)||TouchWorldInspectionRequested70&&unit!=null&&_attackTargets74.ContainsKey(unit);
            foreach(var part in cache.HealthParts)
            {
                if(part==null||!_nativeHealthVisibility68.DeclaringType.IsInstanceOfType(part)||GetViewModel(part)==null)continue;
                if(_nativeHealthHidden68.TryGetValue(part,out bool previous)&&previous==contextual)continue;
                _nativeHealthHidden68[part]=contextual;
                // Re-evaluate native settings/tweens, rather than restoring an
                // old alpha that could revive dead or newly hidden units.
                _nativeHealthVisibility68.Invoke(part,null);++_nativeWorldHealthEvaluations69;
            }
        }
        static void PlaceNativeWorldUi66(Vector3 head)
        {
            UpdateProximityMarkers74(head);
            _nativeWorldDead66.Clear();bool materialChecked=false;
            foreach(var view in _nativeWorldViews66)
            {
                if(view==null||!IsWorldHudTransform(view.transform)){_nativeWorldDead66.Add(view);continue;}
                if(!view.gameObject.activeInHierarchy)continue;
                object vm=GetViewModel(view);
                int family=WorldHudPolicy.ProtectedFamily(view.GetType());
                RegisterWorldInformationSource70(view,vm);
                if((family&(WorldHudPolicy.AttackFamily|WorldHudPolicy.BarkFamily))!=0)
                {
                    if(!ShouldRevealWorldInformation71(view))continue;
                }
                if(TryNativeWorldPosition66(vm,out var anchor))
                {
                    var cache=NativeWorldCacheFor69(view,vm);
                    PlaceOvertip(view.transform,anchor+Vector3.up*OvertipHeight,head);
                    LowerCombatInformation68(cache);
                    UpdateNativeHealth68(vm,cache);
                    if(!materialChecked){EnsureOvertipOnTop(view.transform);materialChecked=true;}
                }
            }
            foreach(var view in _nativeWorldDead66){ReleaseNativeWorldCache69(view);_nativeWorldViews66.Remove(view);if(view!=null)_nativeWorldTransforms66.Remove(view.transform);}
            _nativeWorldTransforms66.RemoveWhere(t=>t==null);
        }

        static bool NativeWorldVisibleCandidate66(Component part)
        {
            if(part==null||!part.gameObject.activeInHierarchy)return false;
            float alpha=1;
            for(Transform node=part.transform;node!=null;node=node.parent)
            {
                _nativeWorldGroups66.Clear();node.GetComponents(_nativeWorldGroups66);
                foreach(var group in _nativeWorldGroups66)
                {if(group==null)continue;alpha*=group.alpha;if(group.ignoreParentGroups)node=null;}
                if(node==null)break;
            }
            if(alpha<=.001f)return false;
            _nativeWorldGraphics66.Clear();part.GetComponentsInChildren(true,_nativeWorldGraphics66);
            foreach(var graphic in _nativeWorldGraphics66)
                if(graphic!=null&&graphic.isActiveAndEnabled&&graphic.color.a>.001f)return true;
            return false;
        }

        static void AuditNativeWorldCandidate66(Component part,int family)
        {
            if(part==null||_nativeWorldAuditBudget66<=0)return;
            _nativeWorldGraphics66.Clear();part.GetComponentsInChildren(true,_nativeWorldGraphics66);
            Graphic graphic=null;foreach(var item in _nativeWorldGraphics66)if(item!=null&&item.isActiveAndEnabled){graphic=item;break;}
            var canvas=part.GetComponentInParent<Canvas>();var camera=HudWorldPickingCamera();int layer=part.gameObject.layer;
            string material=graphic==null||graphic.material==null?"none":graphic.material.shader==null?graphic.material.name:graphic.material.shader.name;
            int queue=graphic==null||graphic.material==null?-1:graphic.material.renderQueue;
            string signature=part.gameObject.activeInHierarchy+"|"+(graphic!=null&&graphic.enabled)+"|"+
                (graphic==null?0:graphic.color.a)+"|"+(graphic==null?false:graphic.canvasRenderer.cull)+"|"+layer+"|"+
                (camera!=null&&((camera.cullingMask&(1<<layer))!=0))+"|"+material+"|"+queue+"|"+(canvas==null?"none":canvas.renderMode.ToString());
            if(_nativeWorldAudit66.TryGetValue(family,out var previous)&&previous==signature)return;
            _nativeWorldAudit66[family]=signature;--_nativeWorldAuditBudget66;
            _log.Log("[ui67/world-audit] family="+family+" view="+part.GetType().FullName+" state="+signature+
                " rect="+(part.transform is RectTransform rect?rect.rect.ToString():"n/a"));
        }

        // Bounded tracked-view audit. No scene scan and no native visibility is
        // forced: active hierarchy, inherited alpha and enabled native graphics
        // decide eligibility. Unit attack/bark blocks are distinguished from an
        // ordinary pooled unit overtip.
        static int NativeWorldUiEligibility66()
        {
            int mask=0;_nativeWorldEligibleInteractions66=_nativeWorldEligibleAttacks66=_nativeWorldEligibleBarks66=0;
            foreach(var view in _nativeWorldViews66)
            {
                if(view==null||!view.gameObject.activeInHierarchy)continue;
                int rootFamily=WorldHudPolicy.ProtectedFamily(view.GetType());
                if((rootFamily&WorldHudPolicy.InteractionFamily)!=0&&NativeWorldVisibleCandidate66(view))
                {mask|=WorldHudPolicy.InteractionFamily;++_nativeWorldEligibleInteractions66;AuditNativeWorldCandidate66(view,WorldHudPolicy.InteractionFamily);}
                if(_tacticalInspectionHeld71&&_worldInformationSources70.TryGetValue(view,out var source)&&
                    _tacticalInspectionSources71.Contains(source)&&source.TacticalGraphics72.Count>0)
                {mask|=WorldHudPolicy.AttackFamily;++_nativeWorldEligibleAttacks66;}
            }
            return mask;
        }
    }
}
