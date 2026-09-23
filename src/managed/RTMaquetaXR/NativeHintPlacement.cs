using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        sealed class NativeHintOffset { internal Vector2 Native, Applied; internal Vector3 NativeScale, AppliedScale; internal bool Placed; }
        static readonly Vector3[] _nativeHintCorners80 = new Vector3[4];
        static readonly Dictionary<RectTransform,NativeHintOffset> _nativeHintOffsets=new Dictionary<RectTransform,NativeHintOffset>();
        static Func<object,object> _nativeHintWindow;
        static bool _nativeHintInstalled;
        static void InstallNativeHintPlacement()
        {
            try
            {
                var type=AccessTools.TypeByName("Kingmaker.UI.MVVM.View.Tutorial.PC.TutorialHintWindowPCView");
                if(!_nativeHintInstalled)
                {
                    _nativeHintWindow=TouchSelectionCallFactory.FieldGetter(PcUiPath.Field(type,"m_WindowAnimator"));
                    _harmony.Patch(AccessTools.Method(type,"BindViewImplementation",Type.EmptyTypes),
                        postfix:new HarmonyMethod(typeof(Main),nameof(NativeHintBound)));
                    _nativeHintInstalled=true;
                }
                // Reacquire already-bound hints when VR resumes after restoring
                // their original positions. The hook itself is installed once.
                foreach(var view in UnityEngine.Object.FindObjectsByType(type,FindObjectsSortMode.None))NativeHintBound(view as Component);
            }
            catch(Exception error) { _log.Error("[ui/tutorial-position] "+error.Message); }
        }
        static void NativeHintBound(Component __instance)
        {
            if(__instance==null)return;
            var window=(_nativeHintWindow?.Invoke(__instance) as Component)?.transform as RectTransform;
            if(window!=null && !_nativeHintOffsets.ContainsKey(window))_nativeHintOffsets.Add(window,new NativeHintOffset());
        }
        internal static void UpdateNativeHintPlacement()
        {
            List<RectTransform> expired=null;
            foreach(var pair in _nativeHintOffsets)
            {
                var rect=pair.Key; var state=pair.Value;
                if(rect==null) { (expired??(expired=new List<RectTransform>())).Add(rect); continue; }
                bool show=_active && _attached && !_modeFlat && rect.gameObject.activeInHierarchy;
                if(!show)
                {
                    if(state.Placed && rect.anchoredPosition==state.Applied)rect.anchoredPosition=state.Native;
                    if(state.Placed && rect.localScale==state.AppliedScale)rect.localScale=state.NativeScale;
                    state.Placed=false; continue;
                }
                Vector2 current=rect.anchoredPosition;
                if(!state.Placed || current!=state.Applied)state.Native=current;
                if(!state.Placed || rect.localScale!=state.AppliedScale)state.NativeScale=rect.localScale;
                var parent=rect.parent as RectTransform;
                if(parent==null || parent.rect.width<=0 || parent.rect.height<=0)continue;
                // Fit the complete native hint, including long translated text;
                // drawing and picking retain the same live RectTransform.
                Rect area=parent.rect;
                area.xMin+=parent.rect.width*.18f;area.xMax-=parent.rect.width*.18f;
                area.yMin+=parent.rect.height*.03f;area.yMax-=parent.rect.height*.03f;
                rect.anchoredPosition=state.Native;rect.localScale=state.NativeScale*1.15f;
                Bounds bounds=NativeHintBounds80(rect,parent);
                if(bounds.size.x<=0 || bounds.size.y<=0){rect.localScale=state.NativeScale;state.Placed=false;continue;}
                float fit=Mathf.Min(1,Mathf.Min(area.width/bounds.size.x,area.height/bounds.size.y));
                rect.localScale*=fit;bounds=NativeHintBounds80(rect,parent);
                float dx=bounds.max.x>area.xMax?area.xMax-bounds.max.x:bounds.min.x<area.xMin?area.xMin-bounds.min.x:0;
                float dy=bounds.max.y>area.yMax?area.yMax-bounds.max.y:bounds.min.y<area.yMin?area.yMin-bounds.min.y:0;
                Vector2 target=state.Native+new Vector2(dx,dy);
                rect.anchoredPosition=target;
                state.Applied=target;state.AppliedScale=rect.localScale;state.Placed=true;
            }
            if(expired!=null)foreach(var rect in expired)_nativeHintOffsets.Remove(rect);
        }
        static void RestoreNativeHintPlacement()
        {
            foreach(var pair in _nativeHintOffsets)
                if(pair.Key!=null && pair.Value.Placed)
                {
                    if(pair.Key.anchoredPosition==pair.Value.Applied)pair.Key.anchoredPosition=pair.Value.Native;
                    if(pair.Key.localScale==pair.Value.AppliedScale)pair.Key.localScale=pair.Value.NativeScale;
                }
            _nativeHintOffsets.Clear();
        }
        static Bounds NativeHintBounds80(RectTransform rect,RectTransform parent)
        {
            rect.GetWorldCorners(_nativeHintCorners80);
            var bounds=new Bounds(parent.InverseTransformPoint(_nativeHintCorners80[0]),Vector3.zero);
            for(int i=1;i<4;i++)bounds.Encapsulate(parent.InverseTransformPoint(_nativeHintCorners80[i]));
            return bounds;
        }
    }
}
