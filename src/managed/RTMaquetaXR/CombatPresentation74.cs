using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        sealed class PresentationOwner74
        {
            internal object Root,Unit;
            internal bool Radial;
            internal bool Disposed,Faulted75;
            internal readonly Dictionary<object,Dictionary<MethodBase,object[]>> Pending=new Dictionary<object,Dictionary<MethodBase,object[]>>();
            internal readonly Dictionary<object,Dictionary<MethodBase,object[]>> CallBuffers=new Dictionary<object,Dictionary<MethodBase,object[]>>();
            internal readonly List<object> Models=new List<object>();
            internal readonly List<PresentationCall75> Work75=new List<PresentationCall75>();
            internal int Revision=-1;
        }
        struct PresentationCall75
        {internal object Target;internal MethodInfo Method;internal object[] Arguments;}
        static readonly Dictionary<object,PresentationOwner74> _presentationRoots74=new Dictionary<object,PresentationOwner74>();
        static readonly Dictionary<object,PresentationOwner74> _presentationModels74=new Dictionary<object,PresentationOwner74>();
        static readonly HashSet<PresentationOwner74> _presentationDirty74=new HashSet<PresentationOwner74>();
        static readonly List<PresentationOwner74> _presentationWork74=new List<PresentationOwner74>();
        static readonly Dictionary<object,object> _attackTargets74=new Dictionary<object,object>();
        static readonly Dictionary<object,int> _attackSignatures74=new Dictionary<object,int>();
        static readonly HashSet<object> _attackSeen74=new HashSet<object>();
        static readonly HashSet<object> _activeBarks74=new HashSet<object>();
        static readonly Dictionary<object,WorldInformationSource70> _barkSources74=new Dictionary<object,WorldInformationSource70>();
        static readonly HashSet<WorldInformationSource70> _interactionSources74=new HashSet<WorldInformationSource70>();
        static readonly List<WorldInformationSource70> _inspectionWork74=new List<WorldInformationSource70>();
        static FieldInfo[] _attackFields74;
        static FieldInfo _attackBurst74;
        static Type _hitModelType74;
        static MethodInfo _hitUpdate74,_healthUpdate74,_unitUpdate74;
        static int _presentationMask74,_presentationReady74,_attackRevision74,_attackDispatch74,_attackCaptured74=-1;
        static int _inspectionRevision74=-1;
        static bool _presentationInstalled74,_presentationFlushing74,_coverageCombat74;
        static object _attackList74;
        static object _assigningHit74;
        static string _presentationError74;
        static long _presentationSkips74,_presentationFlushes74,_positionSkips74,_attackCopies74,_coverageSkips74,_inspectionReuse74;
        static bool Presentation74(int bit)=>_active&&_attached&&!_modeFlat&&(_presentationMask74&_presentationReady74&bit)!=0;
        static bool CombatPresentationContext74=>_active&&_attached&&!_modeFlat&&!InNavigationMap&&!InSpaceCombat&&TouchRadialCombatNow(out _);

        static void InstallCombatPresentation74()
        {
            if(_presentationInstalled74)return;
            _presentationInstalled74=true;_presentationMask74=_cfg.combatPresentationMask74&15;
            try
            {
                var root=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.Overtips.Unit.OvertipEntityUnitVM");
                var parts="Kingmaker.Code.UI.MVVM.VM.Overtips.Unit.UnitOvertipParts.";
                _hitModelType74=AccessTools.TypeByName(parts+"OvertipHitChanceBlockVM");
                _hitUpdate74=TouchSelectionCallFactory.ExactMethod(_hitModelType74,"UpdateProperties",typeof(void),false);
                var health=AccessTools.TypeByName(parts+"OvertipHealthBlockVM");
                _healthUpdate74=TouchSelectionCallFactory.ExactMethod(health,"UpdateProperties",typeof(void),false,typeof(bool));
                _unitUpdate74=TouchSelectionCallFactory.ExactMethod(root,"OnUpdateHandler",typeof(void),false);
                var constructor=AccessTools.Constructor(root,new[]{AccessTools.TypeByName("Kingmaker.Mechanics.Entities.AbstractUnitEntity")})??throw new MissingMethodException(root.FullName,".ctor(AbstractUnitEntity)");
                _harmony.Patch(constructor,postfix:new HarmonyMethod(typeof(Main),nameof(PresentationRootCreated74)));
                _harmony.Patch(AccessTools.DeclaredMethod(root,"DisposeImplementation"),prefix:new HarmonyMethod(typeof(Main),nameof(PresentationRootDisposed74)));
                var baseRoot=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.Overtips.OvertipEntityVM");
                _harmony.Patch(AccessTools.DeclaredMethod(baseRoot,"DisposeImplementation"),prefix:new HarmonyMethod(typeof(Main),nameof(PresentationRootDisposed74)));
                _harmony.Patch(_unitUpdate74,prefix:new HarmonyMethod(typeof(Main),nameof(PresentationPosition74)));
                PatchPresentationNoArgs74(_hitUpdate74);
                PatchPresentationNoArgs74(AccessTools.DeclaredMethod(_hitModelType74,"UpdateSingleSelectedAbilityProperty"));
                _harmony.Patch(_healthUpdate74,prefix:new HarmonyMethod(typeof(Main),nameof(PresentationHealth74)));
                PatchPresentationNoArgs74(AccessTools.DeclaredMethod(health,"UpdateEnemyShields"));
                var name=AccessTools.TypeByName(parts+"OvertipNameBlockVM");
                foreach(var method in AccessTools.GetDeclaredMethods(name))
                    if(method.Name=="UpdateProperties"&&method.ReturnType==typeof(void)&&method.GetParameters().Length==0)PatchPresentationNoArgs74(method);
                var cover=AccessTools.TypeByName(parts+"OvertipCoverBlockVM");
                _harmony.Patch(AccessTools.DeclaredMethod(cover,"UpdateCover"),prefix:new HarmonyMethod(typeof(Main),nameof(PresentationCover74)));
                var hitHandler=AccessTools.DeclaredMethod(_hitModelType74,"HandleCellAbility");
                _harmony.Patch(hitHandler,prefix:new HarmonyMethod(typeof(Main),nameof(PresentationAttack74)));
                _harmony.Patch(AccessTools.DeclaredMethod(_hitModelType74,"OnVisibilityChanged"),prefix:new HarmonyMethod(typeof(Main),nameof(PresentationHitVisibility74)));
                var state=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.Common.UnitState.UnitState");
                // Observe only. Shared UnitState is NEVER suspended or rewritten.
                _harmony.Patch(AccessTools.DeclaredMethod(state,"HandleCellAbility"),prefix:new HarmonyMethod(typeof(Main),nameof(ObserveAttackTargets74)));
                _harmony.Patch(AccessTools.DeclaredMethod(state,"HandleAbilityTargetSelectionEnd"),postfix:new HarmonyMethod(typeof(Main),nameof(ClearAttackTargets74)));
                var data=AccessTools.TypeByName("Kingmaker.UnitLogic.Abilities.AbilityTargetUIData");
                _attackFields74=data.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
                _attackBurst74=AccessTools.Field(data,"<BurstHitChances>k__BackingField");
                foreach(string range in new[]{"AbilityPatternRange","AbilitySingleTargetRange"})
                {
                    var type=AccessTools.TypeByName("Kingmaker.UI.Pointer.AbilityTarget."+range);
                    var update=TouchSelectionCallFactory.ExactMethod(type,"SetRangeToWorldPosition",typeof(void),false,typeof(Vector3),typeof(bool));
                    _harmony.Patch(update,prefix:new HarmonyMethod(typeof(Main),nameof(AttackDispatch74)));
                    var end=AccessTools.DeclaredMethod(type,"HandleAbilityTargetSelectionEnd");
                    if(end!=null)_harmony.Patch(end,postfix:new HarmonyMethod(typeof(Main),nameof(ClearAttackTargets74)));
                }
                // Presentation-only visibility: affected friends are not rejected
                // by the desktop camera or the weapon's direct-target UI test.
                var canHit=AccessTools.DeclaredMethod(_hitModelType74,"CalculateCanHit");
                if(canHit!=null)_harmony.Patch(canHit,postfix:new HarmonyMethod(typeof(Main),nameof(PresentationAffectedHit74)));
                InstallCombatViews74(root.Assembly);
                _presentationReady74|=3;
            }
            catch(Exception error){_presentationError74=error.ToString();_log?.Error("[ui74/presentation] "+error);}
            try
            {
                var bark=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.Bark.BaseBarkVM");
                _harmony.Patch(AccessTools.DeclaredMethod(bark,"ShowBark"),postfix:new HarmonyMethod(typeof(Main),nameof(BarkShown74)));
                _harmony.Patch(AccessTools.DeclaredMethod(bark,"HideBark"),postfix:new HarmonyMethod(typeof(Main),nameof(BarkHidden74)));
                _harmony.Patch(AccessTools.DeclaredMethod(bark,"DisposeImplementation"),prefix:new HarmonyMethod(typeof(Main),nameof(BarkHidden74)));
                _presentationReady74|=4;
            }
            catch(Exception error){_presentationError74=error.ToString();_log?.Error("[ui74/registry] "+error);}
            try{InstallRadialPresentation74();_presentationReady74|=8;}
            catch(Exception error){_presentationError74=error.ToString();_log?.Error("[ui74/radial] "+error);}
            InstallPresentationMetrics74();
            InstallCombatAudioAudit75();
        }
        static void PatchPresentationNoArgs74(MethodInfo method)
        {if(method!=null)_harmony.Patch(method,prefix:new HarmonyMethod(typeof(Main),nameof(PresentationNoArgs74)));}
        static void PresentationRootCreated74(object __instance)
        {RegisterPresentationOwner74(__instance,false);}
        static PresentationOwner74 RegisterPresentationOwner74(object root,bool radial)
        {
            if(root==null)return null;
            if(_presentationRoots74.TryGetValue(root,out var owner))return owner;
            owner=new PresentationOwner74{Root=root,Unit=ReadMember73(root,"Unit")??ReadMember73(root,"DestructibleEntity"),Radial=radial};_presentationRoots74.Add(root,owner);
            string[] fields=radial?new[]{"OvertipHitChanceBlockVM","UnitBuffs"}:new[]{"HitChanceBlockVM","HealthBlockVM","NameBlockVM","CoverBlockVM","BuffPartVM"};
            foreach(string field in fields)
            {object model=ReadField70(root,field);if(model==null)continue;owner.Models.Add(model);_presentationModels74[model]=owner;}
            return owner;
        }
        static void PresentationRootDisposed74(object __instance)
        {
            RetirePresentationModel75(__instance);
            if(!_presentationRoots74.TryGetValue(__instance,out var owner))return;
            owner.Disposed=true;owner.Pending.Clear();owner.CallBuffers.Clear();
            foreach(var model in owner.Models)_presentationModels74.Remove(model);
            _presentationDirty74.Remove(owner);_presentationRoots74.Remove(__instance);++_attackRevision74;
        }
        static bool PresentationVisible74(PresentationOwner74 owner)
        {
            if(owner.Radial)return _radialStatusRoot!=null&&_radialStatusRoot.activeInHierarchy;
            return TouchWorldInspectionRequested70&&owner.Unit!=null&&(_attackTargets74.ContainsKey(owner.Unit)||PointedUnit74(owner.Unit));
        }
        static bool PointedUnit74(object unit)
        {
            if(NativeTacticalPreviewActive70||_attackTargets74.Count>0)return false;
            var view=ReadMember73(unit,"View") as Component;
            var pointer=PointerObject70("PointerOn")??PointerObject70("OvertipObject");
            return view!=null&&pointer!=null&&(pointer.transform==view.transform||pointer.transform.IsChildOf(view.transform));
        }
        static bool QueuePresentation74(object instance,MethodBase method,object[] args)
        {
            if(_presentationFlushing74||!CombatPresentationContext74||
                !_presentationModels74.TryGetValue(instance,out var owner)||owner.Disposed||owner.Faulted75||!Presentation74(owner.Radial?8:1))return true;
            return EnqueuePresentation74(owner,instance,method,args);
        }
        static bool EnqueuePresentation74(PresentationOwner74 owner,object instance,MethodBase method,object[] args)
        {
            // Coalesce notifications from a single native event before drawing.
            if(!owner.Pending.TryGetValue(instance,out var calls))
            {
                if(!owner.CallBuffers.TryGetValue(instance,out calls))owner.CallBuffers.Add(instance,calls=new Dictionary<MethodBase,object[]>());
                calls.Clear();owner.Pending.Add(instance,calls);
            }
            calls[method]=args;_presentationDirty74.Add(owner);++_presentationSkips74;return false;
        }
        static bool PresentationViewVisibility74(Component __instance,MethodBase __originalMethod)
        {
            // Native visibility also dispatches CombatText reactive commands.
            // Keep those events synchronous, even while our graphics are hidden.
            // Only the explicitly audited data/layout work is coalesced.
            if(__instance==null||GetViewModel(__instance)==null)return false;
            if(_presentationFlushing74||!CombatPresentationContext74||
                !_worldInformationSources70.TryGetValue(__instance,out var source))return true;
            // A native visibility event can change eligibility without changing
            // the attack result (fog, hidden condition). Invalidate only when
            // membership differs, so a stable query still reuses its geometry.
            bool selected=_tacticalInspectionSources71.Contains(source);
            if((_presentationReady74&2)!=0&&TouchWorldInspectionRequested70&&_attackTargets74.Count>0&&
                selected!=TargetIncluded74(source,null))++_attackRevision74;
            return true;
        }
        static bool PresentationNoArgs74(object __instance,MethodBase __originalMethod)=>
            !ReferenceEquals(__instance,_assigningHit74)&&QueuePresentation74(__instance,__originalMethod,null);
        static bool PresentationHealth74(object __instance,MethodBase __originalMethod,bool __0)
        {return QueuePresentation74(__instance,__originalMethod,__0?PresentationTrue74:PresentationFalse74);}
        static readonly object[] PresentationTrue74={true},PresentationFalse74={false};
        static bool PresentationPosition74(object __instance)
        {
            if(_presentationFlushing74||!Presentation74(1)||!CombatPresentationContext74)return true;
            if(!_presentationRoots74.TryGetValue(__instance,out var owner))owner=RegisterPresentationOwner74(__instance,false);
            if(PresentationVisible74(owner))return true;
            ++_positionSkips74;return false;
        }
        static bool PresentationCover74()
        {if(!Presentation74(1)||!CombatPresentationContext74)return true;++_coverageSkips74;return false;}
        static void AttackDispatch74(){unchecked{++_attackDispatch74;}}
        static void ObserveAttackTargets74(object __0){CaptureAttackTargets74(__0 as IList);}
        static void CaptureAttackTargets74(IList list)
        {
            if(!CombatPresentationContext74||list==null||_attackFields74==null)return;
            if(_attackCaptured74==_attackDispatch74&&ReferenceEquals(_attackList74,list))return;
            _attackCaptured74=_attackDispatch74;_attackList74=list;_attackSeen74.Clear();bool changed=false;
            foreach(object item in list)
            {
                object target=ReadMember73(item,"Target");if(target==null||!_attackSeen74.Add(target))continue;
                int signature=17;
                unchecked{foreach(var field in _attackFields74){object value=field.GetValue(item);
                    if(value is IList sequence){foreach(var number in sequence)signature=signature*31+(number?.GetHashCode()??0);}
                    else signature=signature*31+(value?.GetHashCode()??0);}}
                if(_attackSignatures74.TryGetValue(target,out int previous)&&previous==signature&&
                    _attackTargets74.TryGetValue(target,out var retained)&&AttackDataEqual74(retained,item))continue;
                // Enumeration boxes the struct; clone its mutable burst list too.
                if(_attackBurst74?.GetValue(item) is List<float> burst)_attackBurst74.SetValue(item,new List<float>(burst));
                _attackTargets74[target]=item;_attackSignatures74[target]=signature;changed=true;++_attackCopies74;
            }
            _proximityRetired74.Clear();foreach(var target in _attackTargets74.Keys)if(!_attackSeen74.Contains(target))_proximityRetired74.Add(target);
            foreach(var target in _proximityRetired74){_attackTargets74.Remove(target);_attackSignatures74.Remove(target);changed=true;}
            if(changed)++_attackRevision74;
        }
        static bool AttackDataEqual74(object a,object b)
        {
            foreach(var field in _attackFields74)
            {
                object left=field.GetValue(a),right=field.GetValue(b);
                if(left is IList list&&right is IList other)
                {if(list.Count!=other.Count)return false;for(int i=0;i<list.Count;i++)if(!Equals(list[i],other[i]))return false;}
                else if(!Equals(left,right))return false;
            }
            return true;
        }
        static void ClearAttackTargets74()
        {
            if(_attackTargets74.Count>0)++_attackRevision74;
            _attackTargets74.Clear();_attackSignatures74.Clear();_attackList74=null;_attackCaptured74=-1;
        }
        static bool PresentationAttack74(object __instance,object __0)
        {
            CaptureAttackTargets74(__0 as IList);
            if(_presentationFlushing74||!CombatPresentationContext74||!_presentationModels74.TryGetValue(__instance,out var owner)||owner.Disposed||owner.Faulted75||!Presentation74(owner.Radial?8:2))return true;
            _presentationDirty74.Add(owner);++_presentationSkips74;return false;
        }
        static void PresentationAffectedHit74(object __instance,ref bool __result)
        {
            if((_presentationReady74&2)!=0&&CombatPresentationContext74&&TouchWorldInspectionRequested70&&_presentationModels74.TryGetValue(__instance,out var owner)&&owner.Unit!=null&&_attackTargets74.ContainsKey(owner.Unit))__result=true;
        }
        static bool PresentationHitVisibility74(object __instance,ref bool __0)
        {
            if(ReferenceEquals(__instance,_assigningHit74))return false;
            if(!CombatPresentationContext74||!_presentationModels74.TryGetValue(__instance,out var owner))return true;
            if(PresentationVisible74(owner))
            {if(!owner.Radial&&owner.Unit!=null&&_attackTargets74.ContainsKey(owner.Unit))__0=true;return true;}
            if(!Presentation74(owner.Radial?8:1))return true;
            _presentationDirty74.Add(owner);++_presentationSkips74;return false;
        }
        static void FlushPresentationOwner74(PresentationOwner74 owner,bool force=false)
        {
            if(owner==null||owner.Disposed||owner.Faulted75||_presentationFlushing74)return;
            bool canHydrate=(_presentationReady74&2)!=0;
            if(!force&&owner.Pending.Count==0&&(!canHydrate||owner.Revision==_attackRevision74))
            {_presentationDirty74.Remove(owner);return;}
            long started=DiagnosticTimestamp();_presentationFlushing74=true;
            try
            {
                bool hydrated=canHydrate&&(owner.Revision!=_attackRevision74||force);
                if(hydrated)
                {
                    foreach(var model in owner.Models)if(_hitModelType74.IsInstanceOfType(model))
                    {
                        var reactive=ReadField70(model,"m_AbilityTargetUIData");
                        var property=PcUiPath.Property(reactive.GetType(),"Value");
                        object data=owner.Unit!=null&&_attackTargets74.TryGetValue(owner.Unit,out var hit)?hit:Activator.CreateInstance(property.PropertyType);
                        _assigningHit74=model;
                        try{property.SetValue(reactive,data,null);}
                        finally{_assigningHit74=null;}
                        _hitUpdate74.Invoke(model,null);
                    }
                    if(!owner.Radial&&(force||owner.Revision<0))
                    {
                        if(_unitUpdate74.DeclaringType.IsInstanceOfType(owner.Root))_unitUpdate74.Invoke(owner.Root,null);
                        var health=ReadField70(owner.Root,"HealthBlockVM");if(health!=null&&_healthUpdate74.DeclaringType.IsInstanceOfType(health))_healthUpdate74.Invoke(health,PresentationTrue74);
                    }
                    owner.Revision=_attackRevision74;
                }
                owner.Work75.Clear();
                foreach(var pair in owner.Pending)foreach(var call in pair.Value)
                    owner.Work75.Add(new PresentationCall75{Target=pair.Key,Method=(MethodInfo)call.Key,Arguments=call.Value});
                owner.Pending.Clear();
                foreach(var call in owner.Work75)
                    if(!(call.Method==_hitUpdate74&&hydrated)&&!(call.Target is UnityEngine.Object obj&&obj==null))
                    {
                        if(owner.Disposed)break;
                        if(call.Target is Component view&&_worldInformationSources70.TryGetValue(view,out var source)&&!WorldSourceCurrent75(source))continue;
                        call.Method.Invoke(call.Target,call.Arguments);
                    }
                owner.Work75.Clear();_presentationDirty74.Remove(owner);++_presentationFlushes74;
            }
            catch(Exception error)
            {
                // A failed native binding must not keep intercepting updates.
                _presentationError74=error.GetBaseException().Message;owner.Faulted75=true;
                owner.Pending.Clear();owner.Work75.Clear();_presentationDirty74.Remove(owner);
                if(_presentationViews75.TryGetValue(owner.Root,out var views))
                    foreach(var source in views)FaultWorldSource75(source,error);
                _log?.Error("[ui74/hydration] "+error);
            }
            finally{_assigningHit74=null;_presentationFlushing74=false;RecordModStage("CombatPresentationHydrate74",started);}
        }
        static void FlushVisiblePresentation74()
        {
            if(_presentationDirty74.Count==0)return;
            _presentationWork74.Clear();
            if(CombatPresentationContext74)
            {
                // No walk of all sleeping models on every hidden frame.
                if(TouchWorldInspectionRequested70)
                    foreach(var source in _tacticalInspectionSources71)
                        if(source.ViewModel!=null&&_presentationRoots74.TryGetValue(source.ViewModel,out var owner)&&
                            _presentationDirty74.Contains(owner)&&!_presentationWork74.Contains(owner))_presentationWork74.Add(owner);
                if(_radialStatusModel!=null&&_radialStatusRoot!=null&&_radialStatusRoot.activeInHierarchy&&
                    _presentationRoots74.TryGetValue(_radialStatusModel,out var radial)&&_presentationDirty74.Contains(radial))_presentationWork74.Add(radial);
            }
            else _presentationWork74.AddRange(_presentationDirty74);
            foreach(var owner in _presentationWork74)FlushPresentationOwner74(owner);
            _presentationWork74.Clear();
        }
        static void InstallCombatViews74(Assembly assembly)
        {
            foreach(var type in assembly.GetTypes())
            {
                if(type.ContainsGenericParameters||!typeof(Component).IsAssignableFrom(type))continue;
                string name=type.FullName;
                if(WorldHudPolicy.ProtectedFamily(type)==WorldHudPolicy.AttackFamily)
                {
                    var bind=AccessTools.DeclaredMethod(type,"BindViewImplementation");
                    if(bind!=null)_harmony.Patch(bind,postfix:new HarmonyMethod(typeof(Main),nameof(CombatViewBound74)));
                    var destroy=AccessTools.DeclaredMethod(type,"DestroyViewImplementation");
                    if(destroy!=null)_harmony.Patch(destroy,prefix:new HarmonyMethod(typeof(Main),nameof(WorldViewReleased75)));
                    var update=AccessTools.DeclaredMethod(type,"UpdateVisibility");
                    if(update!=null&&update.ReturnType==typeof(void)&&update.GetParameters().Length==0)
                        _harmony.Patch(update,prefix:new HarmonyMethod(typeof(Main),nameof(PresentationViewVisibility74)));
                    foreach(string property in new[]{"CheckVisibility","CheckVisibleTrigger"})
                    {
                        var getter=AccessTools.DeclaredMethod(type,"get_"+property);
                        if(getter!=null)_harmony.Patch(getter,postfix:new HarmonyMethod(typeof(Main),nameof(CombatViewVisible74)));
                    }
                }
                if(name.Contains("Overtip")&&name.Contains("Cover"))
                {
                    var visible=AccessTools.DeclaredMethod(type,"get_IsVisible");
                    if(visible!=null&&visible.ReturnType==typeof(bool))_harmony.Patch(visible,postfix:new HarmonyMethod(typeof(Main),nameof(CoverVisible74)));
                    var bind=AccessTools.DeclaredMethod(type,"BindViewImplementation");
                    if(bind!=null)_harmony.Patch(bind,postfix:new HarmonyMethod(typeof(Main),nameof(CoverViewBound74)));
                    var release=AccessTools.DeclaredMethod(type,"DestroyViewImplementation");
                    if(release!=null)_harmony.Patch(release,prefix:new HarmonyMethod(typeof(Main),nameof(CoverViewReleased77)));
                }
            }
        }
        static void CombatViewBound74(Component __instance)
        {
            if(!_active||!_attached||_modeFlat)return;
            RegisterWorldInformationSource70(__instance,GetViewModel(__instance),true);
            _nativeWorldViews66.Add(__instance);_nativeWorldTransforms66.Add(__instance.transform);++_attackRevision74;
        }
        static bool TargetIncluded74(WorldInformationSource70 source,WorldInformationSource70 pointed)
        {
            if(source?.Unit==null)return false;
            object state=ReadField70(source.ViewModel,"UnitState");
            if(NativeStateBool72(state,"HasHiddenCondition")||ReadMember73(source.Unit,"IsInFogOfWar") is bool fog&&fog)return false;
            if(_attackTargets74.Count>0)return _attackTargets74.ContainsKey(source.Unit);
            return !NativeTacticalPreviewActive70&&ReferenceEquals(source,pointed);
        }
        static void CombatViewVisible74(Component __instance,ref bool __result)
        {
            if(!CombatPresentationContext74||!TouchWorldInspectionRequested70)return;
            if(_worldInformationSources70.TryGetValue(__instance,out var source)&&WorldSourceCurrent75(source)&&TargetIncluded74(source,null))__result=true;
        }
        static void CoverVisible74(ref bool __result){if(CombatPresentationContext74)__result=false;}
        static void CoverViewBound74(Component __instance)
        {
            ReleaseCoverageOwner77(__instance);
            foreach(var graphic in __instance.GetComponentsInChildren<Graphic>(true))
                RegisterCoverageGraphic74(graphic,__instance);
        }
        static readonly Dictionary<Graphic,bool> _coverageGraphics74=new Dictionary<Graphic,bool>();
        static void RegisterCoverageGraphic74(Graphic graphic,object owner=null)
        {
            if(graphic==null)return;
            RetainCoverage77(owner,graphic);
            if(!_coverageGraphics74.ContainsKey(graphic))_coverageGraphics74.Add(graphic,graphic.canvasRenderer.cull);
            if(CombatPresentationContext74)graphic.canvasRenderer.cull=true;
        }
        static void UpdateCoverageState74()
        {
            bool combat=CombatPresentationContext74;if(combat==_coverageCombat74)return;_coverageCombat74=combat;
            foreach(var pair in _coverageGraphics74)if(pair.Key!=null)
            {pair.Key.canvasRenderer.cull=combat||pair.Value;if(!combat)pair.Key.SetAllDirty();}
        }
        static void BarkShown74(object __instance){_activeBarks74.Add(__instance);_worldDialogueNext72=0;}
        static void BarkHidden74(object __instance){_activeBarks74.Remove(__instance);_worldDialogueNext72=0;}
        static void RegisterPresentationSource74(WorldInformationSource70 source)
        {
            RegisterPresentationBinding75(source);
            if(source.ViewModel==null)return;
            object bark=ReadField70(source.ViewModel,"BarkBlockVM")??source.ViewModel;
            _barkSources74[bark]=source;
            if(NativeStateBool72(bark,"IsBarkActive"))_activeBarks74.Add(bark);
            if((source.Family&WorldHudPolicy.InteractionFamily)!=0)_interactionSources74.Add(source);
            if((source.Family&WorldHudPolicy.AttackFamily)!=0)RegisterPresentationOwner74(source.ViewModel,false);
        }
        static void RemovePresentationSource74(WorldInformationSource70 source)
        {
            RemovePresentationBinding75(source);
            _interactionSources74.Remove(source);
            object bark=ReadField70(source.ViewModel,"BarkBlockVM")??source.ViewModel;
            if(bark!=null&&_barkSources74.TryGetValue(bark,out var current)&&ReferenceEquals(current,source))_barkSources74.Remove(bark);
        }
        static string CollectDialogue74()
        {
            _worldLines70.Clear();_worldLineSet70.Clear();
            foreach(var bark in _activeBarks74)
            {
                if(!NativeStateBool72(bark,"IsBarkActive"))continue;
                bool sourceValid=_barkSources74.TryGetValue(bark,out var source)&&source.View!=null;
                if(sourceValid||_proximityBarks74.Contains(bark))
                {
                    string text=ReactiveValue70(ReadField70(bark,"Text")) as string;
                    if(!string.IsNullOrWhiteSpace(text)&&_worldLineSet70.Add(text)){_worldLines70.Add(text);if(sourceValid)CacheWorldDialogueTheme71(source);}
                }
            }
            foreach(var source in _interactionSources74)
                if(source.View!=null&&source.View.gameObject.activeInHierarchy)
                    foreach(var graphic in source.Texts)
                        if(graphic!=null&&source.InteractionTextRenderers.Contains(graphic.canvasRenderer)&&AddWorldGraphicLine71(graphic))CacheWorldDialogueTheme71(source,graphic);
            return _worldLines70.Count==0?null:string.Join("\n",_worldLines70);
        }
        static void ReleasePresentation74()
        {
            _presentationWork74.Clear();_presentationWork74.AddRange(_presentationDirty74);
            foreach(var owner in _presentationWork74)FlushPresentationOwner74(owner);
            _presentationWork74.Clear();_presentationDirty74.Clear();
            foreach(var pair in _coverageGraphics74)if(pair.Key!=null){pair.Key.canvasRenderer.cull=pair.Value;pair.Key.SetAllDirty();}
            _coverageGraphics74.Clear();_barkSources74.Clear();_interactionSources74.Clear();ClearAttackTargets74();
            ResetCoverageOwners77();
        }
        static object CombatPresentationSnapshot74()=>new{RequestedMask=_cfg.combatPresentationMask74,AppliedMask=_presentationMask74,ReadyMask=_presentationReady74,
            Owners=_presentationRoots74.Count,PendingOwners=_presentationDirty74.Count,HiddenOrCoalescedCalls=_presentationSkips74,Hydrations=_presentationFlushes74,
            HiddenPositions=_positionSkips74,CoverCalculationsSkipped=_coverageSkips74,Targets=_attackTargets74.Count,Revision=_attackRevision74,
            TargetCopies=_attackCopies74,InspectionReuses=_inspectionReuse74,RetainedRadialLeases=_radialRetainedLeases74,
            FamiliarVisuals76=FamiliarVisualSnapshot76(),HiddenRadialTicks=_radialHiddenTicks74,NativeCosts=PresentationCostsSnapshot74(),Error=_presentationError74,
            RetiredBindings75=_retiredBindings75,LocalFaults75=_localPresentationFaults75,PreparationFaults75=_worldPreparationFaults75,Audio75=CombatAudioSnapshot75()};
    }
}
