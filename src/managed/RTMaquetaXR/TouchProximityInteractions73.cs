using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    internal sealed class TouchProximityWheelGraphic73 : MaskableGraphic
    {
        internal int Count,Selected;
        internal float Progress;
        internal bool[] Available;
        int availabilitySignature75;
        internal void State(int count,int selected,float progress,bool[] available)
        {
            progress=Mathf.Clamp01(progress);int signature=17;
            unchecked{if(available!=null)foreach(bool value in available)signature=signature*31+(value?1:0);}
            if(Count==count&&Selected==selected&&Progress==progress&&availabilitySignature75==signature)return;
            Count=count;Selected=selected;Progress=progress;Available=available;availabilitySignature75=signature;SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(Count<=0)return;
            Rect r=GetPixelAdjustedRect();Vector2 centre=r.center;
            float outer=TouchRadialLayout.OuterRadius,inner=TouchRadialLayout.InnerRadius;
            for(int i=0;i<Count;i++)
            {
                float from=(float)(Math.PI*2*i/Count),to=(float)(Math.PI*2*(i+1)/Count);
                bool enabled=Available==null||i>=Available.Length||Available[i];
                Color fill=!enabled?new Color(.20f,.20f,.20f,.90f):i==Selected?new Color(.18f,.74f,.48f,.94f):new Color(.045f,.065f,.06f,.94f);
                AddWedge(vh,centre,inner,outer,from,to,fill);
                float edge=Mathf.Lerp(from,to,i==Selected?Progress:0);
                if(enabled&&i==Selected&&Progress>0)AddWedge(vh,centre,outer-11,outer-3,from,edge,TouchRadialPalette.Gold);
            }
            AddDisc(vh,centre,inner-3,new Color(.025f,.035f,.03f,.98f));
        }
        static void AddWedge(VertexHelper vh,Vector2 c,float inner,float outer,float from,float to,Color color)
        {
            int steps=Math.Max(3,Mathf.CeilToInt((to-from)*12));
            for(int s=0;s<steps;s++)
            {
                float a=Mathf.Lerp(from,to,s/(float)steps),b=Mathf.Lerp(from,to,(s+1)/(float)steps);
                int start=vh.currentVertCount;UIVertex v=UIVertex.simpleVert;v.color=color;
                v.position=c+new Vector2(Mathf.Sin(a),Mathf.Cos(a))*inner;vh.AddVert(v);
                v.position=c+new Vector2(Mathf.Sin(a),Mathf.Cos(a))*outer;vh.AddVert(v);
                v.position=c+new Vector2(Mathf.Sin(b),Mathf.Cos(b))*outer;vh.AddVert(v);
                v.position=c+new Vector2(Mathf.Sin(b),Mathf.Cos(b))*inner;vh.AddVert(v);
                vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);
            }
        }
        static void AddDisc(VertexHelper vh,Vector2 c,float radius,Color color)
        {
            int centre=vh.currentVertCount;UIVertex v=UIVertex.simpleVert;v.color=color;v.position=c;vh.AddVert(v);
            const int steps=32;for(int i=0;i<=steps;i++)
            {float a=(float)(Math.PI*2*i/steps);v.position=c+new Vector2(Mathf.Sin(a),Mathf.Cos(a))*radius;vh.AddVert(v);if(i>0)vh.AddTriangle(centre,centre+i,centre+i+1);}
        }
    }

    internal sealed class TouchProximityInteractionContracts73
    {
        internal Func<object,object> Interactions,Settings,View;
        internal Func<object,int> UiType;
        internal Func<object,bool> CanInteract,Enabled;
        internal Func<object,int> Radius;
        internal MethodInfo TryInteract;
        internal Type UnitListType,DoorPart,LootPart;
        internal static TouchProximityInteractionContracts73 Create(Func<string,Type> find)
        {
            Type entity=find("Kingmaker.EntitySystem.Entities.MapObjectEntity")??throw new TypeLoadException("MapObjectEntity");
            Type part=find("Kingmaker.View.MapObjects.InteractionComponentBase+InteractionPart")??
                find("Kingmaker.View.MapObjects.InteractionComponentBase.InteractionPart")??throw new TypeLoadException("InteractionPart");
            Type handler=find("Kingmaker.Controllers.Clicks.Handlers.ClickMapObjectHandler")??throw new TypeLoadException("ClickMapObjectHandler");
            Type unit=find("Kingmaker.EntitySystem.Entities.BaseUnitEntity")??throw new TypeLoadException("BaseUnitEntity");
            Type door=find("Kingmaker.View.MapObjects.InteractionDoorPart")??throw new TypeLoadException("InteractionDoorPart");
            Type loot=find("Kingmaker.View.MapObjects.InteractionLootPart")??throw new TypeLoadException("InteractionLootPart");
            MethodInfo interactions=AccessTools.PropertyGetter(entity,"Interactions")??throw new MissingMethodException(entity.FullName,"Interactions");
            MethodInfo settings=AccessTools.PropertyGetter(part,"Settings")??throw new MissingMethodException(part.FullName,"Settings");
            Type mapView=find("Kingmaker.View.MapObjects.MapObjectView")??throw new TypeLoadException("MapObjectView");
            MethodInfo view=TouchSelectionCallFactory.ExactMethod(part,"get_View",mapView,false);
            MethodInfo ui=AccessTools.PropertyGetter(part,"UIInteractionType")??throw new MissingMethodException(part.FullName,"UIInteractionType");
            MethodInfo can=AccessTools.Method(part,"CanInteract",Type.EmptyTypes)??throw new MissingMethodException(part.FullName,"CanInteract");
            MethodInfo enabled=AccessTools.PropertyGetter(part,"Enabled")??throw new MissingMethodException(part.FullName,"Enabled");
            MethodInfo radius=AccessTools.PropertyGetter(part,"ApproachRadius")??throw new MissingMethodException(part.FullName,"ApproachRadius");
            Type actor=find("Kingmaker.Interaction.IInteractionVariantActor")??throw new TypeLoadException("IInteractionVariantActor");
            Type unitList=typeof(List<>).MakeGenericType(unit);
            MethodInfo invoke=TouchSelectionCallFactory.ExactMethod(handler,"TryInteract",typeof(bool),true,part,unitList,typeof(bool),actor);
            return new TouchProximityInteractionContracts73{
                Interactions=(Func<object,object>)TouchSelectionCallFactory.Build(typeof(Func<object,object>),interactions),
                Settings=(Func<object,object>)TouchSelectionCallFactory.Build(typeof(Func<object,object>),settings),
                View=(Func<object,object>)TouchSelectionCallFactory.Build(typeof(Func<object,object>),view),
                UiType=(Func<object,int>)TouchSelectionCallFactory.Build(typeof(Func<object,int>),ui),
                CanInteract=(Func<object,bool>)TouchSelectionCallFactory.Build(typeof(Func<object,bool>),can),
                Enabled=(Func<object,bool>)TouchSelectionCallFactory.Build(typeof(Func<object,bool>),enabled),
                Radius=(Func<object,int>)TouchSelectionCallFactory.Build(typeof(Func<object,int>),radius),
                TryInteract=invoke,UnitListType=unitList,DoorPart=door,LootPart=loot};
        }
    }

    internal sealed class TouchProximityOption73
    {
        internal object Entity,Part;
        internal string Label,Description75;
        internal Sprite Icon;
        internal float Distance;
        internal int Key;
        internal bool Available;
    }

    public static partial class Main
    {
        static TouchProximityInteractionContracts73 _touchProximityContracts73;
        static readonly List<TouchProximityOption73> _touchProximityOptions73=new List<TouchProximityOption73>(16);
        static readonly List<object> _touchProximityUnits73=new List<object>(8);
        static readonly HashSet<object> _touchProximityEntities73=new HashSet<object>();
        static GameObject _touchProximityRoot73;
        static Canvas _touchProximityCanvas73;
        static TouchProximityWheelGraphic73 _touchProximityGraphic73;
        static readonly List<Image> _touchProximityIcons73=new List<Image>(16);
        static bool[] _touchProximityAvailability73=new bool[0];
        static Text _touchProximityTitle73,_touchProximityLabel73,_touchProximityHint73,_touchProximityDescription75;
        static Material _touchProximityMaterial73,_touchProximityTextMaterial73;
        static bool _touchProximityOpen73,_touchProximityRelease73,_touchProximityPlaneReady73;
        static bool _touchProximityStickArmed73,_touchProximityLeftTriggerArmed73;
        static int _touchProximitySelected73=-1,_touchProximityHold73=-1;
        static float _touchProximityHoldAt73,_touchProximityRefresh73,_touchProximityProgress73;
        static Vector3 _touchProximityCentre73;
        static Quaternion _touchProximityRotation73;
        static Camera _touchProximityLeft73,_touchProximityRight73;
        static string _touchProximityFault73;
        static long _touchProximityOpens73,_touchProximityActions73;
        internal static bool TouchProximityCaptured73=>TouchInputOwned&&(_touchProximityOpen73||_touchProximityRelease73);
        internal static bool TouchProximityContractsReady73=>_touchProximityContracts73!=null;
        internal static bool IsTouchProximityCanvas73(Canvas canvas)=>canvas!=null&&canvas==_touchProximityCanvas73;

        static void InstallTouchProximityInteractions73()
        {
            try{_touchProximityContracts73=TouchProximityInteractionContracts73.Create(AccessTools.TypeByName);InstallProximityCatalogue74();_touchProximityFault73=null;}
            catch(Exception error){_touchProximityContracts73=null;_touchProximityFault73=error.Message;_log.Error("[touch/proximity73] "+error.Message);}
        }
        static void StopTouchProximityInteractions73()
        {
            ReleaseProximityCatalogue74();
            _touchProximityOpen73=_touchProximityRelease73=_touchProximityPlaneReady73=false;
            _touchProximityStickArmed73=_touchProximityLeftTriggerArmed73=false;
            _touchProximitySelected73=_touchProximityHold73=-1;_touchProximityOptions73.Clear();_touchProximityUnits73.Clear();_touchProximityEntities73.Clear();
            RenderPipelineManager.beginCameraRendering-=TouchProximityBeginCamera73;
            if(_touchProximityRoot73!=null)UnityEngine.Object.Destroy(_touchProximityRoot73);
            if(_touchProximityMaterial73!=null)UnityEngine.Object.Destroy(_touchProximityMaterial73);
            if(_touchProximityTextMaterial73!=null)UnityEngine.Object.Destroy(_touchProximityTextMaterial73);
            _touchProximityRoot73=null;_touchProximityCanvas73=null;_touchProximityGraphic73=null;_touchProximityTitle73=_touchProximityLabel73=_touchProximityHint73=null;
            _touchProximityIcons73.Clear();_touchProximityAvailability73=new bool[0];_touchProximityContracts73=null;
        }
        static void ProcessTouchProximityInteractions73(bool overlayWasOpen)
        {
            UpdateProximityCatalogue74();
            if(_touchProximityRelease73)
            {
                bool neutral=!_touchA.Held&&!_touchB.Held&&!_touchRawLeftTrigger&&Mathf.Abs(_touchSample.right.stickX)<.18f&&Mathf.Abs(_touchSample.right.stickY)<.18f;
                if(neutral)_touchProximityRelease73=false;
                return;
            }
            if(_touchProximityOpen73)
            {
                if(!TouchProximityBaseAllowed73(overlayWasOpen)||_touchB.Down){CloseTouchProximity73(true);return;}
                if(Time.unscaledTime>=_touchProximityRefresh73)
                {
                    _touchProximityRefresh73=Time.unscaledTime+.20f;
                    if(!RefreshTouchProximityCatalogue73(true)){CloseTouchProximity73(true);return;}
                }
                bool centred=Mathf.Abs(_touchSample.right.stickX)<TouchRadialPolicy.ReleaseDeadzone&&Mathf.Abs(_touchSample.right.stickY)<TouchRadialPolicy.ReleaseDeadzone;
                if(!_touchProximityStickArmed73&&centred)_touchProximityStickArmed73=true;
                if(!_touchRawLeftTrigger)_touchProximityLeftTriggerArmed73=true;
                int stick=_touchProximityStickArmed73?PickTouchProximity73(_touchSample.right.stickX,_touchSample.right.stickY,_touchProximityOptions73.Count):-1;
                int pointer=TouchProximityPointerHit73();
                int candidate=stick>=0?stick:pointer;
                bool available=candidate>=0&&candidate<_touchProximityOptions73.Count&&_touchProximityOptions73[candidate].Available;
                bool held=available&&(stick>=0||pointer>=0&&_touchProximityLeftTriggerArmed73&&_touchRawLeftTrigger);
                if(candidate<0||candidate>=_touchProximityOptions73.Count||!held)
                {_touchProximityHold73=-1;_touchProximityProgress73=0;}
                else if(_touchProximityHold73!=candidate)
                {_touchProximityHold73=candidate;_touchProximityHoldAt73=Time.unscaledTime;_touchProximityProgress73=0;}
                else
                {
                    _touchProximityProgress73=Mathf.Clamp01(Time.unscaledTime-_touchProximityHoldAt73);
                    if(_touchProximityProgress73>=1f){ExecuteTouchProximity73(candidate);CloseTouchProximity73(true);return;}
                }
                _touchProximitySelected73=candidate;
                ConsumeTouchProximityInput73();return;
            }
            if(!_touchA.Down||!TouchProximityBaseAllowed73(overlayWasOpen))return;
            bool found=RefreshTouchProximityCatalogue73(false);
            // A is dedicated to nearby interactions in exploration. Even when
            // no candidate exists it must not become a click/move behind this
            // query in the same frame.
            if(!found||_touchProximityOptions73.Count==0)
            {_touchProximityRelease73=true;ConsumeTouchProximityInput73();return;}
            CancelTouchPointerPress();StopTouchGroupMovement();_touchTabletop.Cancel(true);
            if(_touchProximityOptions73.Count==1&&_touchProximityOptions73[0].Available)
            {ExecuteTouchProximity73(0);_touchProximityRelease73=true;ConsumeTouchProximityInput73();return;}
            _touchProximityOpen73=true;_touchProximityPlaneReady73=false;_touchProximitySelected73=_touchProximityHold73=-1;
            _touchProximityStickArmed73=Mathf.Abs(_touchSample.right.stickX)<TouchRadialPolicy.ReleaseDeadzone&&Mathf.Abs(_touchSample.right.stickY)<TouchRadialPolicy.ReleaseDeadzone;
            _touchProximityLeftTriggerArmed73=!_touchRawLeftTrigger;
            _touchProximityProgress73=0;_touchProximityRefresh73=Time.unscaledTime+.20f;++_touchProximityOpens73;
            ConsumeTouchProximityInput73();
        }
        static bool TouchProximityBaseAllowed73(bool overlayWasOpen)
        {
            bool allowed=_touchSampleValid&&TouchInputOwned&&!overlayWasOpen&&!TouchOverlayOpen&&!TouchOverlayChordCaptured&&!TouchRadialCaptured&&
                !TouchManipulationRequested&&!_touchExplorationMapFrame&&!_touchMenuBackConsumed&&!InSpaceCombat&&!InNavigationMap&&!_modeFlat&&_attached&&
                !CinematicWanted&&!NativeTutorialInputBlocked&&!TouchMenuWindowVisible&&!TouchLocalMapCompactVisible&&
                _modeName=="Default"&&_touchGroupContracts!=null&&_touchGroupContracts.Allowed();
            if(!allowed)return false;
            try{object game=_touchGroupContracts.Game();return game!=null&&!_touchGroupContracts.UiBlocked(game);}
            catch{return false;}
        }
        static void ConsumeTouchProximityInput73()
        {
            _touchPrimary.Cancel();_touchSecondary.Cancel();_touchAnyButton.Cancel();_touchGameCancel.Cancel();_touchGamePause.Cancel();
            _touchGameAxesArmed=false;_touchClickFrozen=false;ResetTouchUiPress();
        }
        static bool RefreshTouchProximityCatalogue73(bool retain) => FillProximityOptions74(retain);
        static bool TouchProximityEntityVisible73(object entity)
        {
            var loot=_touchLootContracts65;if(entity==null||loot==null)return false;
            try
            {
                var view=loot.View(entity) as Component;
                return view!=null&&view.gameObject.activeInHierarchy&&_proximityInGame74(entity)&&loot.Revealed(entity)&&!loot.Fog(entity)&&loot.Perceived(entity);
            }catch{return false;}
        }
        static bool TouchProximityPart73(object part,bool requireAvailable)
        {
            var c=_touchProximityContracts73;if(part==null||c==null)return false;
            try
            {
                // Doors and loot already have reliable direct trigger routes.
                if(TouchProximityDirectPart73(part))return false;
                object settings=c.Settings(part);if(settings==null)return false;
                object overtip=ReadMember73(settings,"ShowOvertip");
                if(!(overtip is bool shown)||!shown||!c.Enabled(part))return false;
                return !requireAvailable||c.CanInteract(part);
            }catch{return false;}
        }
        static bool TouchProximityDirectPart73(object part)
        {
            var c=_touchProximityContracts73;
            return part!=null&&c!=null&&(c.DoorPart.IsInstanceOfType(part)||c.LootPart.IsInstanceOfType(part));
        }
        internal static bool TouchProximityEntityNeedsWheel73(object entity)
        {
            var c=_touchProximityContracts73;if(entity==null||c==null)return false;
            try{var parts=c.Interactions(entity) as IEnumerable;if(parts!=null)foreach(object part in parts)if(TouchProximityPart73(part,false))return true;}catch{}
            return false;
        }
        internal static bool TouchProximityEntityOnlyWheel73(object entity)
        {
            var c=_touchProximityContracts73;if(entity==null||c==null)return false;
            bool wheel=false;
            try
            {
                var parts=c.Interactions(entity) as IEnumerable;if(parts==null)return false;
                foreach(object part in parts)
                {
                    if(TouchProximityDirectPart73(part))return false;
                    wheel|=TouchProximityPart73(part,false);
                }
            }catch{return false;}
            return wheel;
        }
        internal static bool TouchProximityYReveal73(object entity)
        {return TouchExplorationHighlightAllowed72&&TouchProximityEntityVisible73(entity)&&TouchProximityEntityNeedsWheel73(entity);}
        static float TouchProximityDistance73(object entity,List<object> units)
        {
            object p=TryGetProp(entity,"Position");if(!(p is Vector3 target))return float.PositiveInfinity;
            float best=float.PositiveInfinity;
            foreach(object unit in units)if(TryGetProp(unit,"Position") is Vector3 origin)best=Mathf.Min(best,Vector3.Distance(origin,target));
            return best;
        }
        static object ReadMember73(object owner,string name)
        {
            if(owner==null)return null;Type type=owner.GetType();
            if(!_nativeMembers74.TryGetValue(type,out var members))_nativeMembers74.Add(type,members=new Dictionary<string,MemberInfo>());
            if(!members.TryGetValue(name,out var member))
            {
                const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
                for(Type current=type;current!=null&&member==null;current=current.BaseType)
                {
                    foreach(var property in current.GetProperties(flags))
                        if(property.Name==name&&property.GetIndexParameters().Length==0&&property.GetGetMethod(true)!=null){member=property;break;}
                    if(member==null)member=current.GetField(name,flags);
                }
                members[name]=member;
            }
            try{return member is PropertyInfo property?property.GetValue(owner,null):member is FieldInfo field?field.GetValue(owner):null;}catch{return null;}
        }
        static readonly Dictionary<Type,Dictionary<string,MemberInfo>> _nativeMembers74=new Dictionary<Type,Dictionary<string,MemberInfo>>();
        static string TouchNativeString73(object value)
        {
            value=ReactiveValue70(value);if(value is string text)return text;
            object localized=TryGetProp(value,"String")??TryGetProp(value,"Text")??TryGetProp(value,"Value");
            return localized as string;
        }
        static void ExecuteTouchProximity73(int index)
        {
            if(index<0||index>=_touchProximityOptions73.Count)return;var option=_touchProximityOptions73[index];var c=_touchProximityContracts73;
            try
            {
                ReadProximitySelection74();
                if(!TouchProximityEntityVisible73(option.Entity)||!TouchProximityPart73(option.Part,true)||
                    _touchProximityUnits73.Count==0||ProximityDistance74(option.Entity)>ProximityRadius74(option.Entity,option.Part))
                {++_proximityRejected74;return;}
                // Revalidate membership too: a consumed/replaced part must never execute.
                bool present=false;foreach(object part in (IEnumerable)c.Interactions(option.Entity))present|=ReferenceEquals(part,option.Part);
                if(!present){++_proximityRejected74;return;}
                var units=(IList)Activator.CreateInstance(c.UnitListType);foreach(object unit in _touchProximityUnits73)units.Add(unit);
                object result;
                _proximityExecuting74=true;
                try{result=c.TryInteract.Invoke(null,new object[]{option.Part,units,false,null});++_proximityOrders74;}
                finally{_proximityExecuting74=false;}
                if(result is bool accepted&&accepted){++_touchProximityActions73;ScheduleInteractionRefresh71();}
            }
            catch(Exception error){_touchProximityFault73=error.GetBaseException().Message;_log.Error("[touch/proximity73/action] "+_touchProximityFault73);}
        }
        static void CloseTouchProximity73(bool guard)
        {
            _touchProximityOpen73=false;_touchProximityRelease73=guard;_touchProximityPlaneReady73=false;
            _touchProximityStickArmed73=_touchProximityLeftTriggerArmed73=false;
            _touchProximitySelected73=_touchProximityHold73=-1;_touchProximityProgress73=0;
            if(_touchProximityCanvas73!=null)_touchProximityCanvas73.enabled=false;
        }
        static int PickTouchProximity73(float x,float y,int count)
        {
            float magnitude=Mathf.Sqrt(x*x+y*y);if(count<=0||magnitude<TouchRadialPolicy.ReleaseDeadzone)return -1;
            double angle=Math.Atan2(x,y);if(angle<0)angle+=Math.PI*2;
            return Math.Min(count-1,(int)Math.Floor(angle/(Math.PI*2)*count));
        }
        static int TouchProximityPointerHit73()
        {
            if(!_touchProximityPlaneReady73||!_touchSample.left.AimValid||_touchProximityRoot73==null)return -1;
            if(!TryTouchWorldRay(true,out Ray ray))return -1;
            var rect=(RectTransform)_touchProximityRoot73.transform;
            if(!new Plane(rect.forward,rect.position).Raycast(ray,out float distance)||distance<0)return -1;
            Vector3 local=rect.InverseTransformPoint(ray.GetPoint(distance));
            float x=local.x,y=local.y;
            float magnitude=Mathf.Sqrt(x*x+y*y);if(magnitude<TouchRadialLayout.InnerRadius||magnitude>TouchRadialLayout.OuterRadius)return -1;
            double angle=Math.Atan2(x,y);if(angle<0)angle+=Math.PI*2;
            return Math.Min(_touchProximityOptions73.Count-1,(int)Math.Floor(angle/(Math.PI*2)*_touchProximityOptions73.Count));
        }
        static void UpdateTouchProximityInteractionVisual73(Camera left,Camera right)
        {
            _touchProximityLeft73=left;_touchProximityRight73=right;
            if(!_touchProximityOpen73||left==null||right==null){if(_touchProximityCanvas73!=null)_touchProximityCanvas73.enabled=false;return;}
            if(_touchProximityRoot73==null)CreateTouchProximityVisual73();
            if(!_touchProximityPlaneReady73)
            {
                if(!TryTouchWorldRay(false,out Ray emitter))return;
                Vector3 head=(left.transform.position+right.transform.position)*.5f;
                _touchProximityRotation73=Quaternion.Slerp(left.transform.rotation,right.transform.rotation,.5f);
                Vector3 local=Quaternion.Inverse(_touchProximityRotation73)*(emitter.GetPoint(WorldScale*.12f)-head)/WorldScale;
                local.x=Mathf.Clamp(local.x+.10f,-.10f,.32f);local.y=Mathf.Clamp(local.y+.05f,-.16f,.22f);local.z=Mathf.Clamp(local.z,.38f,.58f);
                _touchProximityCentre73=head+_touchProximityRotation73*(local*WorldScale);_touchProximityPlaneReady73=true;
            }
            var rect=(RectTransform)_touchProximityRoot73.transform;rect.SetPositionAndRotation(_touchProximityCentre73,_touchProximityRotation73);
            rect.localScale=Vector3.one*(WorldScale*TouchRadialProjection.MetresPerPixel);
            if(_spatialFault!=null&&_touchProximityCanvas73.worldCamera!=left)_touchProximityCanvas73.worldCamera=left;
            if(_touchProximityGraphic73!=null)_touchProximityGraphic73.State(_touchProximityOptions73.Count,_touchProximitySelected73,_touchProximityProgress73,_touchProximityAvailability73);
            string label=_touchProximitySelected73>=0&&_touchProximitySelected73<_touchProximityOptions73.Count?_touchProximityOptions73[_touchProximitySelected73].Label:"";
            SetLiveText(_touchProximityTitle73,ModLocalization.Text("NEARBY INTERACTIONS"));SetLiveText(_touchProximityLabel73,label);
            SetLiveText(_touchProximityDescription75,_touchProximitySelected73>=0&&_touchProximitySelected73<_touchProximityOptions73.Count?
                _touchProximityOptions73[_touchProximitySelected73].Description75:"");
            SetLiveText(_touchProximityHint73,ModLocalization.Text("[R:XY] / [LT] · 1 s   [B] · CLOSE"));
            _touchProximityCanvas73.enabled=true;
        }
        static void CreateTouchProximityVisual73()
        {
            _touchProximityMaterial73=CreateLiveUiMaterial("RTMaquetaXR proximity wheel");_touchProximityTextMaterial73=CreateLiveUiMaterial("RTMaquetaXR proximity text");
            _touchProximityRoot73=new GameObject("RTMaquetaXR nearby interaction wheel",typeof(RectTransform),typeof(Canvas));_touchProximityRoot73.layer=5;UnityEngine.Object.DontDestroyOnLoad(_touchProximityRoot73);
            var root=(RectTransform)_touchProximityRoot73.transform;root.sizeDelta=new Vector2(1600,1400);
            _touchProximityCanvas73=_touchProximityRoot73.GetComponent<Canvas>();_touchProximityCanvas73.renderMode=RenderMode.WorldSpace;_touchProximityCanvas73.overrideSorting=true;_touchProximityCanvas73.sortingOrder=32764;
            var art=new GameObject("Interaction wheel",typeof(RectTransform),typeof(CanvasRenderer),typeof(TouchProximityWheelGraphic73));art.layer=5;art.transform.SetParent(root,false);
            _touchProximityGraphic73=art.GetComponent<TouchProximityWheelGraphic73>();_touchProximityGraphic73.rectTransform.sizeDelta=new Vector2(1000,1000);_touchProximityGraphic73.material=_touchProximityMaterial73;_touchProximityGraphic73.raycastTarget=false;
            _touchProximityTitle73=ProximityText73("Interaction heading",root,40,new Vector2(0,547),new Vector2(950,52),TouchRadialPalette.Gold);
            _touchProximityLabel73=ProximityText73("Interaction name",root,36,Vector2.zero,new Vector2(250,170),TouchRadialPalette.Ink);
            _touchProximityDescription75=ProximityText73("Interaction description",root,36,new Vector2(0,-528),new Vector2(1050,84),TouchRadialPalette.Ink);
            _touchProximityHint73=ProximityText73("Interaction controls",root,34,new Vector2(0,-621),new Vector2(1050,66),TouchRadialPalette.Ink,true);
            RenderPipelineManager.beginCameraRendering-=TouchProximityBeginCamera73;RenderPipelineManager.beginCameraRendering+=TouchProximityBeginCamera73;
            RebuildTouchProximityVisual73();
        }
        static Text ProximityText73(string name,RectTransform parent,int size,Vector2 position,Vector2 dimensions,Color color,bool icons=false)
        {
            var obj=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),icons?typeof(ControlIconText):typeof(Text));obj.layer=5;obj.transform.SetParent(parent,false);
            var text=obj.GetComponent<Text>();text.font=_liveFont??Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=size;text.alignment=TextAnchor.MiddleCenter;text.color=color;text.material=_touchProximityTextMaterial73;text.raycastTarget=false;text.resizeTextForBestFit=true;text.resizeTextMinSize=Mathf.Max(15,size-8);text.resizeTextMaxSize=size;text.rectTransform.anchoredPosition=position;text.rectTransform.sizeDelta=dimensions;
            text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;
            if(text is ControlIconText controls)controls.IconBodySize=40;return text;
        }
        static void RebuildTouchProximityVisual73()
        {
            if(_touchProximityRoot73==null)return;foreach(var icon in _touchProximityIcons73)if(icon!=null)UnityEngine.Object.Destroy(icon.gameObject);_touchProximityIcons73.Clear();
            int count=_touchProximityOptions73.Count;for(int i=0;i<count;i++)
            {
                float angle=(float)(Math.PI*2*(i+.5f)/count);var obj=new GameObject("Native interaction "+i,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));obj.layer=5;obj.transform.SetParent(_touchProximityRoot73.transform,false);
                var image=obj.GetComponent<Image>();image.sprite=_touchProximityOptions73[i].Icon;image.preserveAspect=true;image.raycastTarget=false;image.material=_touchProximityMaterial73;image.color=image.sprite==null?new Color(0,0,0,0):_touchProximityOptions73[i].Available?Color.white:new Color(.42f,.42f,.42f,.82f);
                float radius=(TouchRadialLayout.InnerRadius+TouchRadialLayout.OuterRadius)*.5f;
                float body=Mathf.Min(132,2*radius*Mathf.Sin(Mathf.PI/Mathf.Max(2,count))*.68f);
                image.rectTransform.sizeDelta=new Vector2(body,body);image.rectTransform.anchoredPosition=new Vector2(Mathf.Sin(angle),Mathf.Cos(angle))*radius;_touchProximityIcons73.Add(image);
            }
        }
        static void TouchProximityBeginCamera73(ScriptableRenderContext context,Camera camera)
        {
            if(_touchProximityCanvas73==null)return;bool eye=camera==_touchProximityLeft73||camera==_touchProximityRight73;bool capture=IsSpatialCaptureCamera(camera);
            _touchProximityCanvas73.enabled=_touchProximityOpen73&&(eye||capture);
        }
        internal static object TouchProximitySnapshot73()=>new{Ready=_touchProximityContracts73!=null,Open=_touchProximityOpen73,Options=_touchProximityOptions73.Count,Opens=_touchProximityOpens73,Actions=_touchProximityActions73,Orders=_proximityOrders74,Rejected=_proximityRejected74,Queries=_proximityQueries74,Catalogue=_proximityEntries74.Count,Markers=_proximityVisible74.Count,Fault=_touchProximityFault73,
            Route="A proximity / one native action directly / multiple native actions in right-hand wheel / right stick or left pointer+trigger 1 s / B cancel"};
    }
}
