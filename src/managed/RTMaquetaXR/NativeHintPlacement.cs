using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        sealed class NativeHintOffset { internal Vector2 Native, Applied; internal bool Placed; }
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
                    state.Placed=false; continue;
                }
                Vector2 current=rect.anchoredPosition;
                if(!state.Placed || current!=state.Applied)state.Native=current;
                var parent=rect.parent as RectTransform;
                float inset=Mathf.Clamp(parent==null?140:parent.rect.width*.10f,80,180);
                Vector2 target=state.Native+new Vector2(-inset,0);
                if(current!=target)rect.anchoredPosition=target;
                state.Applied=target; state.Placed=true;
            }
            if(expired!=null)foreach(var rect in expired)_nativeHintOffsets.Remove(rect);
        }
        static void RestoreNativeHintPlacement()
        {
            foreach(var pair in _nativeHintOffsets)
                if(pair.Key!=null && pair.Value.Placed && pair.Key.anchoredPosition==pair.Value.Applied)
                    pair.Key.anchoredPosition=pair.Value.Native;
            _nativeHintOffsets.Clear();
        }
    }
}
