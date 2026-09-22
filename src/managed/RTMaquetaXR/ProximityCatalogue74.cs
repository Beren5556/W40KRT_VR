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
        // Semantic catalogue and activation never depend on a rendered overtip.
        // A pooled image is only a presentation of the same eligible entry.
        sealed class ProximityEntry74
        {
            internal TouchProximityOption73 Option;
            internal float Radius;
            internal bool Near, Show;
            internal int Seen;
            internal Image Marker;
        }
        static readonly Dictionary<object,ProximityEntry74> _proximityEntries74=new Dictionary<object,ProximityEntry74>();
        static readonly Dictionary<object,object> _proximityModels74=new Dictionary<object,object>();
        static readonly HashSet<object> _proximityOwnedModels74=new HashSet<object>();
        static readonly HashSet<object> _proximityMetadataNeeded74=new HashSet<object>();
        static readonly HashSet<object> _proximityMarkerEntities74=new HashSet<object>();
        static readonly HashSet<object> _proximityBarks74=new HashSet<object>();
        static readonly List<object> _proximityDispose74=new List<object>();
        static readonly List<object> _proximityRetired74=new List<object>();
        static readonly List<ProximityEntry74> _proximityVisible74=new List<ProximityEntry74>();
        static readonly Dictionary<int,Sprite> _proximitySprites74=new Dictionary<int,Sprite>();
        static readonly Stack<Image> _proximityImagePool74=new Stack<Image>();
        static readonly List<Vector3> _proximityOrigins74=new List<Vector3>(8);
        static ConstructorInfo _proximityModelConstructor74;
        static MethodInfo _proximityName74;
        static Func<object,Vector3> _proximityPosition74,_proximityAnchor74;
        static Func<object,bool> _proximityInGame74;
        static bool _proximityHooks74,_proximityExecuting74;
        static object _proximityPool74;
        static float _proximityNext74,_proximitySpriteNext74;
        static int _proximityEpoch74;
        static long _proximityQueries74,_proximityRejected74,_proximityOrders74;

        static bool ProximityContext74 => _active&&_attached&&!_modeFlat&&TouchInputOwned&&!InNavigationMap&&!InSpaceCombat&&
            _modeName=="Default"&&!CinematicWanted&&!TouchRadialCombatNow(out _);

        static void InstallProximityCatalogue74()
        {
            var entity=AccessTools.TypeByName("Kingmaker.EntitySystem.Entities.MapObjectEntity");
            var baseEntity=AccessTools.TypeByName("Kingmaker.EntitySystem.Entities.Base.Entity");
            var vm=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.Overtips.MapObject.OvertipMapObjectVM");
            _proximityModelConstructor74=AccessTools.Constructor(vm,new[]{entity})??throw new MissingMethodException(vm.FullName,".ctor(MapObjectEntity)");
            _proximityName74=TouchSelectionCallFactory.ExactMethod(vm,"GetString",typeof(string),false,
                AccessTools.TypeByName("Kingmaker.Localization.SharedStringAsset"),typeof(string));
            _proximityPosition74=(Func<object,Vector3>)TouchSelectionCallFactory.Build(typeof(Func<object,Vector3>),TouchSelectionCallFactory.ExactMethod(baseEntity,"get_Position",typeof(Vector3),false));
            _proximityInGame74=(Func<object,bool>)TouchSelectionCallFactory.Build(typeof(Func<object,bool>),TouchSelectionCallFactory.ExactMethod(baseEntity,"get_IsInGame",typeof(bool),false));
            _proximityAnchor74=(Func<object,Vector3>)TouchSelectionCallFactory.Build(typeof(Func<object,Vector3>),TouchSelectionCallFactory.ExactMethod(vm,"GetEntityPosition",typeof(Vector3),false));
            if(_proximityHooks74)return;
            _harmony.Patch(_proximityModelConstructor74,postfix:new HarmonyMethod(typeof(Main),nameof(ProximityModelCreated74)));
            _harmony.Patch(AccessTools.DeclaredMethod(vm,"DisposeImplementation"),prefix:new HarmonyMethod(typeof(Main),nameof(ProximityModelDisposed74)));
            var view=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.View.Overtips.MapObject.OvertipMapObjectInteractionView");
            _harmony.Patch(AccessTools.DeclaredMethod(view,"SetupSprites"),postfix:new HarmonyMethod(typeof(Main),nameof(ProximitySpritesBound74)));
            _harmony.Patch(AccessTools.DeclaredMethod(view,"UpdateVisibility"),prefix:new HarmonyMethod(typeof(Main),nameof(ProximityNativeVisibility74)));
            _harmony.Patch(_touchProximityContracts73.TryInteract,prefix:new HarmonyMethod(typeof(Main),nameof(ProximityNativeAction74)));
            _proximityHooks74=true;
        }
        static void ProximityModelCreated74(object __instance,object __0)
        {if(__0!=null)_proximityModels74[__0]=__instance;}
        static void ProximityModelDisposed74(object __instance)
        {
            var entity=ReadField70(__instance,"MapObjectEntity");
            if(entity!=null&&_proximityModels74.TryGetValue(entity,out var current)&&ReferenceEquals(current,__instance))_proximityModels74.Remove(entity);
            _proximityOwnedModels74.Remove(__instance);
            object bark=ReadField70(__instance,"BarkBlockVM");if(bark!=null)_proximityBarks74.Remove(bark);
        }
        static void ProximitySpritesBound74(Component __instance)
        {
            if(__instance==null)return;
            string[] fields={"m_ActionSprites","m_MoveSprites","m_InfoSprites","m_CreditsSprites","m_PetsSprites"};
            for(int i=0;i<fields.Length;i++)
                if(ReadField70(ReadField70(__instance,fields[i]),"Main") is Sprite sprite&&sprite!=null)_proximitySprites74[i+1]=sprite;
        }
        static bool ProximityNativeVisibility74(Component __instance)
        {return !ProximityOwnsNativeMarker74(__instance);}
        static bool ProximityOwnsNativeMarker74(Component view)
        {
            if(!ProximityContext74||view==null||_touchProximityContracts73==null)return false;
            var vm=GetViewModel(view);var part=ReadMember73(vm,"FirstInteractionPart");
            return ProximityMigratedPart74(part);
        }
        static bool ProximityMigratedPart74(object part)
        {
            var c=_touchProximityContracts73;
            return c!=null&&part!=null&&!TouchProximityDirectPart73(part)&&ReadMember73(c.Settings(part),"ShowOvertip") is bool show&&show;
        }
        static bool ProximityNativeAction74(object __0,ref bool __result)
        {
            if(!ProximityContext74||_proximityExecuting74||!TouchProximityPart73(__0,false))return true;
            __result=false;return false;
        }
        static void ReadProximitySelection74()
        {
            _touchProximityUnits73.Clear();_proximityOrigins74.Clear();
            var c=_touchGroupContracts;if(c==null)return;object game=c.Game();
            var selection=game==null?null:c.Selection(game);
            var units=selection==null?null:c.SelectedUnits(selection) as IEnumerable;
            if(units==null)return;
            foreach(object unit in units)
                if(unit!=null&&_proximityInGame74(unit)){_touchProximityUnits73.Add(unit);_proximityOrigins74.Add(_proximityPosition74(unit));}
        }
        static float ProximityDistance74(object entity)
        {
            Vector3 position=_proximityPosition74(entity);float best=float.PositiveInfinity;
            foreach(var origin in _proximityOrigins74)best=Mathf.Min(best,(origin-position).sqrMagnitude);
            return Mathf.Sqrt(best);
        }
        static void UpdateProximityCatalogue74(bool force=false)
        {
            if(!ProximityContext74||_touchProximityContracts73==null||_touchLootContracts65==null)
            {
                HideProximityMarkers74();
                if(_proximityEntries74.Count>0&&(!_attached||InNavigationMap||InSpaceCombat||TouchRadialCombatNow(out _)))ClearProximityEntries74();
                return;
            }
            if(!force&&Time.unscaledTime<_proximityNext74)return;
            _proximityNext74=Time.unscaledTime+.15f;
            long started=DiagnosticTimestamp();
            try
            {
                object pool=_touchLootContracts65.Pool.Read(_touchLootContracts65.Game());
                if(!ReferenceEquals(pool,_proximityPool74)){ClearProximityEntries74();_proximityPool74=pool;}
                ReadProximitySelection74();++_proximityEpoch74;++_proximityQueries74;
                _touchProximityEntities73.Clear();_proximityVisible74.Clear();_proximityMetadataNeeded74.Clear();_proximityMarkerEntities74.Clear();
                var c=_touchProximityContracts73;bool reveal=TouchExplorationHighlightAllowed72;
                var entities=pool as IEnumerable;
                if(entities!=null)foreach(object entity in entities)
                {
                    if(!TouchProximityEntityVisible73(entity))continue;
                    float distance=ProximityDistance74(entity);
                    var parts=c.Interactions(entity) as IEnumerable;if(parts==null)continue;
                    foreach(object part in parts)
                    {
                        if(!TouchProximityPart73(part,false))continue;
                        float radius=ProximityRadius74(entity,part);
                        bool near=distance<=radius;
                        if(!_proximityEntries74.TryGetValue(part,out var entry))
                        {
                            entry=new ProximityEntry74{Option=new TouchProximityOption73{Entity=entity,Part=part,Key=System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(part)}};
                            _proximityEntries74.Add(part,entry);
                        }
                        entry.Seen=_proximityEpoch74;entry.Radius=radius;entry.Near=near;entry.Option.Distance=distance;
                        entry.Option.Available=near&&c.CanInteract(part);
                        entry.Show=(near||reveal)&&MapObjectInVrView72(entity);
                        if(near)_touchProximityEntities73.Add(entity);
                        if(near||entry.Show)
                        {
                            _proximityMetadataNeeded74.Add(entity);
                            object vm=ProximityModel74(entity);
                            entry.Option.Label=ProximityLabel74(vm,part);
                            entry.Option.Description75=ProximityDescription75(vm,part);
                            entry.Option.Icon=ProximitySprite74(part);
                        }
                        // One original marker per object; distinct native actions
                        // remain separate sectors when A opens the wheel.
                        if(entry.Show&&_proximityMarkerEntities74.Add(entity))_proximityVisible74.Add(entry);
                        else if(entry.Marker!=null)entry.Marker.enabled=false;
                    }
                }
                _proximityRetired74.Clear();
                foreach(var pair in _proximityEntries74)if(pair.Value.Seen!=_proximityEpoch74)_proximityRetired74.Add(pair.Key);
                foreach(var key in _proximityRetired74){RetireProximityMarker74(_proximityEntries74[key]);_proximityEntries74.Remove(key);}
                _proximityDispose74.Clear();
                foreach(var model in _proximityOwnedModels74)
                {
                    object entity=ReadField70(model,"MapObjectEntity");
                    if(entity==null||!_proximityMetadataNeeded74.Contains(entity)||
                        !_proximityModels74.TryGetValue(entity,out var current)||!ReferenceEquals(current,model))_proximityDispose74.Add(model);
                }
                foreach(var model in _proximityDispose74)(model as IDisposable)?.Dispose();
                _proximityDispose74.Clear();
            }
            catch(Exception error)
            {
                HideProximityMarkers74();
                string fault=error.GetBaseException().Message;
                if(_touchProximityFault73!=fault){_touchProximityFault73=fault;_log?.Error("[touch/proximity74] "+fault);}
            }
            finally{RecordModStage("ProximityCatalogue74",started);}
        }
        static object ProximityModel74(object entity)
        {
            if(_proximityModels74.TryGetValue(entity,out var vm))return vm;
            // Native metadata is available without instantiating a view or UI
            // raycaster. This fallback exists only for an eligible nearby icon.
            vm=_proximityModelConstructor74.Invoke(new[]{entity});
            _proximityOwnedModels74.Add(vm);
            object bark=ReadField70(vm,"BarkBlockVM");if(bark!=null)_proximityBarks74.Add(bark);
            return vm;
        }
        static float ProximityRadius74(object entity,object part)
        {
            // Native OvertipMapObjectVM uses max(ApproachRadius, 6.35), not
            // ApproachRadius alone. A invokes its approach-and-interact route.
            // Prefer the live native value; the fallback mirrors the verified
            // installed constructor when no VM/view has yet been created.
            float native=6.35f;
            if(_proximityModels74.TryGetValue(entity,out var vm)&&ReadField70(vm,"m_ProximityRadius") is float value&&value>0)native=value;
            return Mathf.Max(native,_touchProximityContracts73.Radius(part));
        }
        static string ProximityLabel74(object vm,object part)
        {
            object settings=_touchProximityContracts73.Settings(part);
            bool used=ReadMember73(part,"AlreadyUsed") is bool already&&already&&
                ReadMember73(settings,"OnlyCheckOnce") is bool once&&once;
            object asset=ReadMember73(settings,used?"DisplayNameAfterUse":"DisplayName");
            string label=asset==null?null:_proximityName74.Invoke(vm,new[]{asset,(object)string.Empty}) as string;
            if(string.IsNullOrWhiteSpace(label))label=ReactiveValue70(ReadField70(vm,"Name")) as string;
            return string.IsNullOrWhiteSpace(label)?ModLocalization.Text("Interaction"):label.Trim();
        }
        static Sprite ProximitySprite74(object part)
        {
            int kind=_touchProximityContracts73.UiType(part);
            if(_proximitySprites74.TryGetValue(kind,out var sprite)&&sprite!=null)return sprite;
            if(Time.unscaledTime>=_proximitySpriteNext74)
            {
                _proximitySpriteNext74=Time.unscaledTime+2f;
                var type=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.View.Overtips.MapObject.OvertipMapObjectInteractionView");
                foreach(var item in Resources.FindObjectsOfTypeAll(type))ProximitySpritesBound74(item as Component);
            }
            return _proximitySprites74.TryGetValue(kind,out sprite)?sprite:null;
        }
        static bool FillProximityOptions74(bool retain)
        {
            UpdateProximityCatalogue74(true);
            bool artChanged=false;
            if(retain)
            {
                // Keep sectors attached to identities until the wheel closes.
                // A removed/moved target turns grey; it cannot shift under a hold.
                foreach(var option in _touchProximityOptions73)
                {
                    bool exists=_proximityEntries74.TryGetValue(option.Part,out var entry);
                    option.Available=exists&&entry.Near&&entry.Option.Available;
                    if(exists)
                    {
                        artChanged|=option.Icon!=entry.Option.Icon;
                        option.Label=entry.Option.Label;option.Description75=entry.Option.Description75;option.Icon=entry.Option.Icon;
                    }
                }
            }
            else
            {
                _touchProximityOptions73.Clear();
                foreach(var entry in _proximityEntries74.Values)if(entry.Near)
                    _touchProximityOptions73.Add(new TouchProximityOption73{Entity=entry.Option.Entity,Part=entry.Option.Part,Key=entry.Option.Key,
                        Label=entry.Option.Label,Description75=entry.Option.Description75,Icon=entry.Option.Icon,Available=entry.Option.Available,Distance=entry.Option.Distance});
                _touchProximityOptions73.Sort((a,b)=>{int d=a.Distance.CompareTo(b.Distance);return d!=0?d:a.Key.CompareTo(b.Key);});
            }
            bool changed=_touchProximityAvailability73.Length!=_touchProximityOptions73.Count;
            if(changed)_touchProximityAvailability73=new bool[_touchProximityOptions73.Count];
            for(int i=0;i<_touchProximityOptions73.Count;i++)
            {changed|=_touchProximityAvailability73[i]!=_touchProximityOptions73[i].Available;_touchProximityAvailability73[i]=_touchProximityOptions73[i].Available;}
            if(_touchProximityOpen73&&(changed||artChanged))RebuildTouchProximityVisual73();
            return _touchProximityOptions73.Count>0;
        }
        static void UpdateProximityMarkers74(Vector3 head)
        {
            if(!ProximityContext74||_overtipsRoot==null){HideProximityMarkers74();return;}
            foreach(var entry in _proximityVisible74)
            {
                if(!entry.Show||entry.Option.Icon==null)continue;
                if(entry.Marker==null)
                {
                    while(_proximityImagePool74.Count>0&&entry.Marker==null)entry.Marker=_proximityImagePool74.Pop();
                    if(entry.Marker==null)
                    {
                        var obj=new GameObject("RTMaquetaXR native proximity icon",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
                        obj.transform.SetParent(_overtipsRoot,false);obj.layer=_overtipsRoot.gameObject.layer;
                        entry.Marker=obj.GetComponent<Image>();entry.Marker.raycastTarget=false;entry.Marker.preserveAspect=true;
                        entry.Marker.rectTransform.sizeDelta=new Vector2(64,64);
                    }
                }
                var marker=entry.Marker;marker.sprite=entry.Option.Icon;marker.enabled=true;
                marker.color=entry.Near&&!entry.Option.Available?new Color(.5f,.5f,.5f,.9f):Color.white;
                object vm=ProximityModel74(entry.Option.Entity);
                PlaceOvertip(marker.transform,_proximityAnchor74(vm)+Vector3.up*OvertipHeight,head);
            }
        }
        static void CollectProximityMarkers74(List<Graphic> destination)
        {foreach(var entry in _proximityVisible74)if(entry.Show&&entry.Marker!=null&&entry.Marker.enabled)destination.Add(entry.Marker);}
        static void HideProximityMarkers74()
        {foreach(var entry in _proximityEntries74.Values)if(entry.Marker!=null)entry.Marker.enabled=false;_proximityVisible74.Clear();_proximityNext74=0;}
        static void RetireProximityMarker74(ProximityEntry74 entry)
        {if(entry.Marker!=null){entry.Marker.enabled=false;_proximityImagePool74.Push(entry.Marker);entry.Marker=null;}}
        static void ClearProximityEntries74()
        {
            foreach(var entry in _proximityEntries74.Values)RetireProximityMarker74(entry);
            _proximityEntries74.Clear();_proximityVisible74.Clear();
            var owned=new List<object>(_proximityOwnedModels74);_proximityOwnedModels74.Clear();
            foreach(var vm in owned)(vm as IDisposable)?.Dispose();
        }
        static void ReleaseProximityCatalogue74()
        {
            ClearProximityEntries74();
            foreach(var image in _proximityImagePool74)if(image!=null)UnityEngine.Object.Destroy(image.gameObject);
            _proximityImagePool74.Clear();_proximitySprites74.Clear();_proximityPool74=null;_proximityNext74=0;
        }
    }
}
