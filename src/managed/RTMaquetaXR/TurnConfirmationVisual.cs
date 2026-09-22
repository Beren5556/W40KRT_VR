using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    internal static class TurnConfirmationLayout
    {
        internal const float Width=760,Height=340;
        internal static int Hit(float x,float y)
        {
            if(!TouchPointerMath.Finite(x)||!TouchPointerMath.Finite(y)||y<232||y>=292)return -1;
            return x>=64&&x<336?0:x>=424&&x<696?1:-1;
        }
    }
    public static partial class Main
    {
        static GameObject _turnCompactRoot;
        static Text _turnCompactTitle,_turnCompactCancel,_turnCompactConfirm;
        static Image _turnCompactNo,_turnCompactYes;
        static bool _turnCompactShown;
        static readonly List<GameObject> _turnCompactHidden=new List<GameObject>();
        static bool TurnConfirmationVisible => _endTurnConfirmation!=null && ReferenceEquals(_liveNavigation.Menu,_endTurnConfirmation);
        static void UpdateTurnConfirmationVisual()
        {
            if(_liveRoot==null)return;
            if(_turnCompactRoot==null||_turnCompactRoot.transform.parent!=_liveRoot.transform)
            {
                _turnCompactRoot=new GameObject("Turn confirmation",typeof(RectTransform));
                var r=(RectTransform)_turnCompactRoot.transform;r.SetParent(_liveRoot.transform,false);r.sizeDelta=new Vector2(760,340);
                LiveTopImage("Background",r,LiveBackground,0,0,760,340);
                LiveTopImage("Turn warning rule",r,new Color(.55f,.07f,.06f,1),26,24,708,3);
                _turnCompactTitle=LiveText("Turn title",r,36,FontStyle.Bold,LiveInk,35,-90,690,86);
                _turnCompactNo=LiveTopImage("Cancel turn button",r,LiveBackground,64,232,272,60);
                _turnCompactYes=LiveTopImage("Confirm turn button",r,new Color(.24f,.02f,.02f,1),424,232,272,60);
                _turnCompactCancel=LiveText("Cancel label",r,28,FontStyle.Normal,LiveInk,72,-244,256,38);
                _turnCompactConfirm=LiveText("Confirm label",r,28,FontStyle.Normal,LiveInk,432,-244,256,38);
                foreach(var t in new[]{_turnCompactTitle,_turnCompactCancel,_turnCompactConfirm})
                {t.alignment=TextAnchor.MiddleCenter;FitLiveText(t,23,t.fontSize);}
            }
            if(!_turnCompactShown)
            {
                _turnCompactShown=true;_turnCompactRoot.SetActive(true);_turnCompactHidden.Clear();
                for(int i=0;i<_liveRoot.transform.childCount;i++)
                {
                    var child=_liveRoot.transform.GetChild(i).gameObject;
                    if(child==_turnCompactRoot||(_overlayPointerDot!=null&&child==_overlayPointerDot.gameObject)||!child.activeSelf)continue;
                    _turnCompactHidden.Add(child);child.SetActive(false);
                }
            }
            SetLiveText(_turnCompactTitle,ModLocalization.Text("End the turn"));
            SetLiveText(_turnCompactCancel,ModLocalization.Text("Cancel"));SetLiveText(_turnCompactConfirm,ModLocalization.Text("Confirm"));
            _turnCompactNo.color=_livePage==0?LiveSelection:LiveBackground;
            _turnCompactYes.color=_livePage==1?new Color(.5f,.055f,.035f,1):new Color(.24f,.02f,.02f,1);
        }
        static void HideTurnConfirmationVisual()
        {
            if(!_turnCompactShown)return;_turnCompactShown=false;
            if(_turnCompactRoot!=null)_turnCompactRoot.SetActive(false);
            foreach(var child in _turnCompactHidden)if(child!=null)child.SetActive(true);
            _turnCompactHidden.Clear();
        }
        static bool PlaceFlatTurnConfirmation(float w,float h,out float x,out float y,out float scale)
        {
            scale=Mathf.Min(w*.52f/760,h*.48f/340);x=(w-760*scale)*.5f;y=(h-340*scale)*.5f;return scale>0;
        }
        static void DrawFlatTurnConfirmation()
        {
            DrawFlatLiveTheme("Background",0,0,760,340,LiveBackground);
            DrawFlatLiveText(ModLocalization.Text("End the turn"),35,90,690,86,36,true,LiveInk);
            DrawFlatLiveRect(64,232,272,60,_livePage==0?LiveSelection:LiveBackground);
            DrawFlatLiveRect(424,232,272,60,_livePage==1?new Color(.5f,.055f,.035f,1):new Color(.24f,.02f,.02f,1));
            DrawFlatLiveText(ModLocalization.Text("Cancel"),72,244,256,38,28,_livePage==0,LiveInk);
            DrawFlatLiveText(ModLocalization.Text("Confirm"),432,244,256,38,28,_livePage==1,LiveInk);
            DrawFlatOverlayTouchCursor();
        }
    }
}
