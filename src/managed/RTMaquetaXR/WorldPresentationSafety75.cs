using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly Dictionary<object,HashSet<WorldInformationSource70>> _presentationViews75 =
            new Dictionary<object,HashSet<WorldInformationSource70>>();
        static readonly Dictionary<Type,MethodInfo> _visibilityMethods75 = new Dictionary<Type,MethodInfo>();
        static long _retiredBindings75,_localPresentationFaults75,_worldPreparationFaults75;
        static string _worldPreparationError75;

        static void RegisterPresentationBinding75(WorldInformationSource70 source)
        {
            source.Binding75.Bind(source.ViewModel);
            if(source.ViewModel==null)return;
            if(!_presentationViews75.TryGetValue(source.ViewModel,out var views))
                _presentationViews75.Add(source.ViewModel,views=new HashSet<WorldInformationSource70>());
            views.Add(source);++_attackRevision74;
        }
        static void RemovePresentationBinding75(WorldInformationSource70 source)
        {
            object model=source.Binding75.Model;
            if(model!=null&&_presentationViews75.TryGetValue(model,out var views))
            {views.Remove(source);if(views.Count==0)_presentationViews75.Remove(model);}
            source.Binding75.Retire();_tacticalInspectionSources71.Remove(source);++_attackRevision74;
        }
        static void RetirePresentationModel75(object model)
        {
            if(model==null||!_presentationViews75.TryGetValue(model,out var views))return;
            _presentationViews75.Remove(model);
            foreach(var source in views)
            {
                source.Binding75.Retire();_tacticalInspectionSources71.Remove(source);
                source.PresentationState74=-1;ApplyWorldSourcePresentation72(source);++_retiredBindings75;
            }
            ++_attackRevision74;
        }
        static void WorldViewReleased75(Component __instance)
        {
            if(__instance==null||!_worldInformationSources70.TryGetValue(__instance,out var source))return;
            RemovePresentationBinding75(source);
            // Keep the graphics hidden through pooling; the next native Bind
            // re-registers this component and refreshes its actual model.
            source.PresentationState74=-1;ApplyWorldSourcePresentation72(source);
            if(source.ViewModel!=null&&_presentationRoots74.TryGetValue(source.ViewModel,out var owner))
                owner.Pending.Remove(__instance);
            ++_retiredBindings75;
        }
        static bool WorldSourceCurrent75(WorldInformationSource70 source)
        {
            return source!=null&&source.View!=null&&source.Binding75.IsCurrent(GetViewModel(source.View));
        }
        static void FaultWorldSource75(WorldInformationSource70 source,Exception error)
        {
            if(source==null)return;
            bool first=!source.Binding75.Faulted;source.Binding75.Fault();
            _tacticalInspectionSources71.Remove(source);source.PresentationState74=-1;
            ApplyWorldSourcePresentation72(source);
            if(first)
            {
                ++_localPresentationFaults75;
                _log?.Error("[ui75/local] Hidden one invalid information binding; stereo retained: "+error.GetBaseException());
            }
        }
        static bool RefreshInspectionSource75(WorldInformationSource70 source)
        {
            if(!WorldSourceCurrent75(source))return false;
            long revision=source.Binding75.Revision;
            try
            {
                var owner=RegisterPresentationOwner74(source.ViewModel,false);
                bool pending=owner!=null&&owner.Pending.ContainsKey(source.View);
                FlushPresentationOwner74(owner,true);
                // Reactive native updates may unbind/reuse a view synchronously.
                if(revision!=source.Binding75.Revision||!WorldSourceCurrent75(source))return false;
                if(!pending)
                {
                    var type=source.View.GetType();
                    if(!_visibilityMethods75.TryGetValue(type,out var update))
                        _visibilityMethods75.Add(type,update=AccessTools.Method(type,"UpdateVisibility",Type.EmptyTypes));
                    update?.Invoke(source.View,null);
                }
                return revision==source.Binding75.Revision&&WorldSourceCurrent75(source);
            }
            catch(Exception error){FaultWorldSource75(source,error);return false;}
        }
        internal static void PrepareWorldPresentation75(Camera left,Camera right,Vector3 centre)
        {
            try
            {
                PrepareWorldInformation71();UpdateWorldOvertips(centre);UpdateWorldInformation70(left,right);
                _worldPreparationError75=null;
            }
            catch(Exception error)
            {
                // Information is optional. Camera setup already succeeded:
                // never route a widget exception through scene detachment.
                ++_worldPreparationFaults75;
                string message=error.GetBaseException().ToString();
                if(_worldPreparationError75!=message)_log?.Error("[ui75/preparation] Stereo retained: "+message);
                _worldPreparationError75=message;
            }
        }
    }
}
