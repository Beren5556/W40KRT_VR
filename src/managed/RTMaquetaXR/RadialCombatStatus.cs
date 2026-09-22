using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace RTMaquetaXR
{
    internal sealed class RadialCombatStatusContracts
    {
        internal PcHudVisibilityContracts Roots;
        internal PcUiPath Momentum, Veil, Unit, Hull;
        internal ConstructorInfo UnitModel;
        internal Type UnitViewType;
        internal Action<object> Initialize;
        internal Action<object, object> Bind;
        internal Func<object,object>[] Animators;
        internal Func<object,bool>[] NativeVisibility;
        internal MethodInfo FadeMethod;
        internal Action<object,bool,UnityAction> PlayFade;
        internal static RadialCombatStatusContracts Create(Func<string,Type> resolve)
        {
            var c = new RadialCombatStatusContracts { Roots = PcHudVisibilityContracts.Create(resolve) };
            var surface = c.Roots.SurfaceType;
            c.Momentum = PcUiPath.Create(surface,"m_StaticPartPCView","SurfaceHUDView","m_ActionBarView","m_SurfaceMomentumPCView");
            c.Veil = PcUiPath.Create(surface,"m_StaticPartPCView","SurfaceHUDView","m_ActionBarView","m_VeilThicknessView");
            c.Unit = PcUiPath.Create(surface,"m_StaticPartPCView","SurfaceHUDView","m_SurfaceCombatCurrentUnitView");
            c.Hull = PcUiPath.Create(c.Roots.SpaceType,"m_StaticPartPCView","m_SpaceCombatPCView","m_SpaceCombatServicePanelPCView","m_HPSlider");
            c.Animators=new Func<object,object>[2];c.NativeVisibility=new Func<object,bool>[2];
            var bars=new[]{c.Veil.ValueType,c.Momentum.ValueType};
            for(int i=0;i<2;i++)
            {
                var field=PcUiPath.Field(bars[i],"m_Animator");
                c.Animators[i]=TouchSelectionCallFactory.FieldGetter(field);
                var modelProperty=PcUiPath.Property(bars[i],"ViewModel"); var modelRead=PcUiPath.Getter(modelProperty);
                var battle=TouchRadialPartyContracts.Reactive<bool>(modelProperty.PropertyType,"IsTurnBasedActive");
                var player=TouchRadialPartyContracts.Reactive<bool>(modelProperty.PropertyType,"IsPlayerTurn");
                var mode=TouchRadialPartyContracts.Reactive<bool>(modelProperty.PropertyType,"IsAppropriateGameMode");
                c.NativeVisibility[i]=view=>{var model=modelRead(view);return model!=null&&battle(model)&&player(model)&&mode(model);};
                if(c.FadeMethod==null)c.FadeMethod=TouchSelectionCallFactory.ExactMethod(field.FieldType,"PlayAnimation",typeof(void),false,typeof(bool),typeof(UnityAction));
            }
            c.PlayFade=(Action<object,bool,UnityAction>)TouchSelectionCallFactory.Build(typeof(Action<object,bool,UnityAction>),c.FadeMethod);
            c.UnitViewType = c.Unit.ValueType;
            var model = resolve("Kingmaker.Code.UI.MVVM.VM.SurfaceCombat.SurfaceCombatUnitVM");
            var entity = resolve("Kingmaker.EntitySystem.Entities.MechanicEntity");
            c.UnitModel = model?.GetConstructor(new[] { entity, typeof(bool) }) ?? throw new MissingMethodException("Native selected-unit status model");
            c.Initialize = (Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>),
                AccessTools.Method(c.UnitViewType,"Initialize",Type.EmptyTypes) ?? throw new MissingMethodException("Native status Initialize"));
            c.Bind = (Action<object,object>)TouchSelectionCallFactory.Build(typeof(Action<object,object>),
                AccessTools.Method(c.UnitViewType,"Bind",new[] { model }) ?? throw new MissingMethodException("Native status Bind"));
            return c;
        }
        internal object View()
        {
            var game=Roots.Game(); var root=game==null?null:Roots.Root(game);
            return root==null?null:Roots.View(root);
        }
    }

    public static partial class Main
    {
        static RadialCombatStatusContracts _radialStatusContracts;
        static readonly SpatialNativeLease[] _radialStatusLeases = new SpatialNativeLease[4];
        static readonly Component[] _radialStatusSources = new Component[4];
        static readonly Component[] _radialStatusAnimators = new Component[2];
        static readonly RectTransform[] _radialStatusReferenceArt = new RectTransform[2];
        static float _radialStatusReferenceWidth;
        static GameObject _radialStatusRoot, _radialStatusPortrait;
        static object _radialStatusUnit, _radialStatusModel;
        static Component _radialStatusPortraitTemplate;
        static long _radialStatusContextRevision = -1, _radialStatusPortraitCreates, _radialStatusPortraitReuses;
        static bool _radialStatusSpace, _radialStatusAttempted;
        static float _radialStatusNext;
        static long _radialStatusBindings;
        static string _radialStatusError;
        static readonly Vector2[] RadialStatusCenters = { new Vector2(-430,-536), new Vector2(430,-536),
            new Vector2(-625,380), new Vector2(0,-543) };
        static readonly Vector2[] RadialStatusBounds = { new Vector2(570,124), new Vector2(570,124),
            new Vector2(310,300), new Vector2(650,110) };
        internal static bool IsRadialStatusNode(Transform node) => node!=null && _radialStatusRoot!=null &&
            (node==_radialStatusRoot.transform || node.IsChildOf(_radialStatusRoot.transform));

        static void InstallRadialCombatStatus()
        {
            if (_radialStatusAttempted) return;
            _radialStatusAttempted=true;
            try
            {
                EnsureRadialBuffContracts76();
                var contracts=RadialCombatStatusContracts.Create(AccessTools.TypeByName);
                _harmony.Patch(contracts.FadeMethod,prefix:new HarmonyMethod(typeof(Main),nameof(RadialStatusFadePrefix)));
                _radialStatusContracts=contracts;
            }
            catch (Exception error)
            {
                _radialStatusContracts=null; ReportRadialStatusCleanup(error);
            }
        }
        static void UpdateRadialCombatStatus()
        {
            bool wanted=_touchRadialShown && _touchRadial.Side==0 &&
                (InSpaceCombat || TouchRadialCombatNow(out var ignored));
            if (!wanted || _touchRadialRoot==null) { ReleaseRadialCombatStatus(); return; }
            if (_radialStatusRoot != null && _radialStatusContextRevision != SpatialGameContextRevision)
                DestroyRetainedRadialCombatStatus();
            if(_radialStatusRoot==null && Time.unscaledTime<_radialStatusNext)return;
            try
            {
                InstallRadialCombatStatus();
                var c=_radialStatusContracts; if(c==null)return;
                if (_radialStatusRoot==null || _radialStatusSpace!=InSpaceCombat)
                {
                    DestroyRetainedRadialCombatStatus(); _radialStatusSpace=InSpaceCombat;
                    _radialStatusContextRevision=SpatialGameContextRevision;
                    EnsureSpatialRoot();
                    _radialStatusRoot=new GameObject("Native combat instruments",typeof(RectTransform),typeof(CanvasGroup));
                    _radialStatusRoot.layer=_spatialLayer;
                    var rect=(RectTransform)_radialStatusRoot.transform; rect.SetParent(_touchRadialRoot.transform,false);
                    rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f); rect.sizeDelta=new Vector2(1600,1200);
                    _radialStatusRoot.GetComponent<CanvasGroup>().blocksRaycasts=false;
                    _radialStatusNext=0; _spatialScanFrame=0;
                }
                if (!_radialStatusRoot.activeSelf)
                {
                    _radialStatusRoot.SetActive(true);
                    _radialStatusNext=0;
                }
                if (Time.unscaledTime>=_radialStatusNext)
                {
                    _radialStatusNext=Time.unscaledTime+.2f;
                    object view=c.View();
                    if (_radialStatusSpace)
                    {
                        // The shipped HP slider contains its original gold frame
                        // and HpNumber/Health child, bound to the player's ship.
                        LeaseRadialStatus(3,c.Hull.Read(view) as Component);
                    }
                    else
                    {
                        LeaseRadialStatus(0,c.Veil.Read(view) as Component);
                        LeaseRadialStatus(1,c.Momentum.Read(view) as Component);
                        UpdateRadialStatusPortrait(c,c.Unit.Read(view) as Component);
                        RefreshRadialStatusIdentity58();
                    }
                }
                HydrateRadialPortrait74();
                long layoutStarted74=DiagnosticTimestamp();
                Camera camera=_radialFlatDrawing?_radialFlatLeft:(_spatialBlackCamera!=null?_spatialBlackCamera:_touchRadialEyeLeft);
                bool alignedBars = !_radialStatusSpace && _radialStatusReferenceWidth > 0 &&
                    _radialStatusReferenceArt[0] != null && _radialStatusReferenceArt[1] != null;
                for(int i=0;i<_radialStatusLeases.Length;i++)
                {
                    // Once matched, generic bounds must not overwrite the
                    // matched scale and then force AlignReference to undo it.
                    if(i<2 && alignedBars) _radialStatusLeases[i]?.SetCamera(camera);
                    else _radialStatusLeases[i]?.Place(RadialStatusCenters[i],RadialStatusBounds[i],camera, true);
                }
                // Compare the actual shared brass background sprite, not the
                // union of momentum's heroic-action popovers and empty hit areas.
                // Freeze the visible reference width for this wheel lifetime.
                if (!_radialStatusSpace && _radialStatusReferenceArt[0] != null && _radialStatusReferenceArt[1] != null)
                {
                    if (_radialStatusReferenceWidth <= 0)
                        _radialStatusReferenceWidth = 500f; // Fixed logical width, independent of native combat layout.
                    for (int i=0;i<2;i++) _radialStatusLeases[i]?.AlignReference(_radialStatusReferenceArt[i],
                        _radialStatusReferenceWidth, RadialStatusCenters[i]);
                }
                RecordModStage("RadialStatusLayout74",layoutStarted74);
            }
            catch(Exception error)
            {
                DestroyRetainedRadialCombatStatus(); _radialStatusNext=Time.unscaledTime+2;
                if(_radialStatusError!=error.Message) _log.Error("[radial/native-status] "+error.Message);
                _radialStatusError=error.Message;
            }
        }
        static void LeaseRadialStatus(int index,Component source)
        {
            if(source==_radialStatusSources[index])return;
            long leaseStarted=DiagnosticTimestamp();
            RestoreRadialStatusFade(index);
            _radialStatusLeases[index]?.Dispose(); _radialStatusLeases[index]=null; _radialStatusSources[index]=null;
            if(source==null)return;
            ReleasePcHudStatusBranch(source.transform);
            RestoreHudRoot(source.transform);
            _radialStatusLeases[index]=new SpatialNativeLease(source,(RectTransform)_radialStatusRoot.transform,FindUICamera());
            _radialStatusSources[index]=source;
            if(index<2)
            {
                _radialStatusReferenceWidth=0;
                _radialStatusReferenceArt[index]=null;
                // Verified in surfacepcview.res: both originals use this same
                // 266x34 sprite; nested prediction/heroic containers are excluded.
                foreach (var picture in source.GetComponentsInChildren<Image>(true))
                    if (picture.name == "BackgroundImage" && picture.sprite != null)
                    { _radialStatusReferenceArt[index]=picture.rectTransform; break; }
                _radialStatusAnimators[index]=_radialStatusContracts.Animators[index](source) as Component;
                if(_radialStatusAnimators[index]!=null)_radialStatusContracts.PlayFade(_radialStatusAnimators[index],true,null);
            }
            foreach(var node in source.GetComponentsInChildren<Transform>(true))node.gameObject.layer=_spatialLayer;
            foreach(var graphic in source.GetComponentsInChildren<Graphic>(true))graphic.SetAllDirty();
            _spatialScanFrame=0; _radialFlatLayout=-1; ++_radialStatusBindings;
            PcHudWorkMembershipChanged();
            RecordModStage("RadialStatusLease74",leaseStarted);
        }
        static void UpdateRadialStatusPortrait(RadialCombatStatusContracts c,Component template)
        {
            object selected=null;
            var selection=TouchCameraSelectedUnits();
            if(selection!=null && _touchGroupContracts!=null)
                foreach(object unit in selection) if(unit!=null && _touchGroupContracts.Controllable(unit)) { selected=unit; break; }
            if(selected==null || template==null) { DestroyRadialStatusPortrait(); return; }
            if (_radialStatusPortrait != null && _radialStatusPortraitTemplate != template)
                DestroyRadialStatusPortrait();
            if(_radialStatusPortrait!=null && ReferenceEquals(selected,_radialStatusUnit))
            {
                if (!_radialStatusPortrait.activeSelf)
                {
                    _radialStatusPortrait.SetActive(true); ++_radialStatusPortraitReuses;
                }
                LeaseRadialStatus(2, _radialStatusPortrait.transform as RectTransform);
                return;
            }
            if(_radialStatusPortrait==null)
            {
                long cloneStarted=DiagnosticTimestamp();
                // Clone only the original portrait widget, under an inactive
                // host. Its own native VM is selected-friendly, independent of
                // the initiative widget (which switches to enemies on their turn).
                bool active=_radialStatusRoot.activeSelf; _radialStatusRoot.SetActive(false);
                try
                {
                    long partStarted75=DiagnosticTimestamp();
                    _radialStatusPortrait=UnityEngine.Object.Instantiate(template.gameObject,_radialStatusRoot.transform,false);
                    RecordModStage("RadialPortraitInstantiate75",partStarted75);partStarted75=DiagnosticTimestamp();
                    _radialStatusPortrait.name="Native selected ally status";
                    _radialStatusPortraitTemplate=template; ++_radialStatusPortraitCreates;
                    PcHudAlphaLease.CopyNativeAlphas(template.gameObject,_radialStatusPortrait);
                    PrepareRadialStatusPortrait58(_radialStatusPortrait);
                    foreach(var graphic in _radialStatusPortrait.GetComponentsInChildren<Graphic>(true))graphic.raycastTarget=false;
                    RecordModStage("RadialPortraitPrepare75",partStarted75);partStarted75=DiagnosticTimestamp();
                    c.Initialize(_radialStatusPortrait.GetComponent(c.UnitViewType));
                    RecordModStage("RadialPortraitInitialize75",partStarted75);
                }
                finally { _radialStatusRoot.SetActive(active);RecordModStage("RadialStatusClone74",cloneStarted); }
            }
            long bindStarted=DiagnosticTimestamp();
            object next=c.UnitModel.Invoke(new object[]{selected,true});
            try { c.Bind(_radialStatusPortrait.GetComponent(c.UnitViewType),next); }
            catch { (next as IDisposable)?.Dispose(); throw; }
            if(_radialStatusModel!=null)PresentationRootDisposed74(_radialStatusModel);
            (_radialStatusModel as IDisposable)?.Dispose(); _radialStatusModel=next; _radialStatusUnit=selected;
            RegisterPresentationOwner74(next,true);_radialBuffFingerprint74=0;
            _radialStatusPortrait.SetActive(true);
            bool existingLease = _radialStatusLeases[2] != null;
            LeaseRadialStatus(2,_radialStatusPortrait.transform as RectTransform);
            if (existingLease) _radialStatusLeases[2]?.CaptureNewNodes();
            RecordModStage("RadialStatusBind74",bindStarted);
            _spatialScanFrame=0;
        }
        static void DestroyRadialStatusPortrait()
        {
            _radialDuplicateBuffs58.Clear();_radialBuffIdentities58.Clear();
            var lease=_radialStatusLeases[2];var portrait=_radialStatusPortrait;var model=_radialStatusModel;
            _radialStatusLeases[2]=null;_radialStatusSources[2]=null;
            _radialStatusPortrait=null;_radialStatusModel=null;_radialStatusUnit=null;_radialStatusPortraitTemplate=null;
            try { lease?.Dispose(); }
            finally
            {
                if(portrait!=null) { portrait.SetActive(false); UnityEngine.Object.Destroy(portrait); }
                try { if(model!=null)PresentationRootDisposed74(model); (model as IDisposable)?.Dispose(); }
                catch(Exception error) { ReportRadialStatusCleanup(error); }
            }
        }
        static void ReleaseRadialCombatStatus()
        {
            if (_radialStatusRoot==null) return;
            if (_radialStatusContextRevision!=SpatialGameContextRevision || !_active ||
                (!_radialStatusSpace && !TouchRadialCombatNow(out var inactiveCombat)))
            { DestroyRetainedRadialCombatStatus(); return; }
            if (!_radialStatusRoot.activeSelf)
            {if(!RetainRadialInstruments74)RestoreRadialStatusLeases();return;}
            if(!RetainRadialInstruments74)RestoreRadialStatusLeases();
            else ++_radialRetainedLeases74;
            // Keep exactly one native portrait/VM while its scene remains.
            // Inactive Unity views do not render or run their Update methods.
            // Changes to the owned model coalesce while hidden. Reopening
            // hydrates its original life/AP/effects before the next draw.
            if (_radialStatusPortrait!=null) _radialStatusPortrait.SetActive(false);
            _radialStatusRoot.SetActive(false);
            _pcHudNext=0; _spatialScanFrame=0; _radialFlatLayout=-1; PcHudWorkMembershipChanged();
        }
        static void RestoreRadialStatusLeases()
        {
            _radialStatusReferenceWidth=0;
            _radialStatusReferenceArt[0]=_radialStatusReferenceArt[1]=null;
            for(int i=0;i<_radialStatusLeases.Length;i++)
            {
                RestoreRadialStatusFade(i);
                var lease=_radialStatusLeases[i];_radialStatusLeases[i]=null;_radialStatusSources[i]=null;
                try { lease?.Dispose(); }
                catch(Exception error) { ReportRadialStatusCleanup(error); }
            }
        }
        static void DestroyRetainedRadialCombatStatus()
        {
            RestoreRadialStatusLeases();
            try { DestroyRadialStatusPortrait(); }
            catch(Exception error) { ReportRadialStatusCleanup(error); }
            if (_radialStatusRoot!=null)
            { _radialStatusRoot.SetActive(false); UnityEngine.Object.Destroy(_radialStatusRoot); }
            _radialStatusRoot=null; _radialStatusContextRevision=-1;
            _pcHudNext=0; _spatialScanFrame=0; _radialFlatLayout=-1; PcHudWorkMembershipChanged();
        }
        static void ReportRadialStatusCleanup(Exception error)
        {
            if(_radialStatusError!=error.Message)_log.Error("[radial/native-status/restore] "+error.Message);
            _radialStatusError=error.Message;
        }
        static object RadialCombatStatusSnapshot() => new { Ready=_radialStatusContracts!=null, Active=_radialStatusRoot!=null && _radialStatusRoot.activeSelf,
            PortraitInstancesCreated=_radialStatusPortraitCreates, PortraitReopensReused=_radialStatusPortraitReuses,
            NativeBindings=_radialStatusBindings, Error=_radialStatusError, OriginalVeilAndMomentum=true,
            OriginalPlayerShipHull=true, SelectedFriendlyNativePortrait=true,
            RemovedClonedBuffInstances=_radialOrphanBuffs58, DuplicateIdentityHides=_radialDuplicateCount58,
            MatchedVisibleBarWidth=_radialStatusReferenceWidth,
            DistinctEffectsAndNativeOverflowPreserved=true };
        static void RadialStatusFadePrefix(Component __instance,ref bool __0,UnityAction __1)
        {
            // Only the two borrowed indicators remain visible on enemy turns.
            // No other game fades, modal callbacks or UI visibility are changed.
            if(__instance!=null && __1==null && (__instance==_radialStatusAnimators[0] || __instance==_radialStatusAnimators[1]))__0=true;
        }
        static void RestoreRadialStatusFade(int index)
        {
            if(index>=2)return;
            var animator=_radialStatusAnimators[index]; _radialStatusAnimators[index]=null;
            var source=_radialStatusSources[index];
            if(animator!=null && source!=null)
            {
                try { _radialStatusContracts.PlayFade(animator,_radialStatusContracts.NativeVisibility[index](source),null); }
                catch(Exception error)
                {
                    // A native VM may be disposed during a scene transition.
                    // Never let that prevent restoration of its transform/layers.
                    if(_radialStatusError!=error.Message)_log.Error("[radial/native-status/restore] "+error.Message);
                    _radialStatusError=error.Message;
                }
            }
        }
    }
}
