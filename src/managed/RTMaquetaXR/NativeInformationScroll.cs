using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    // ScrollRectExtended inherits UIBehaviour, not Unity's ScrollRect. Read and
    // drive the original native implementation; never replace or clone its UI.
    internal sealed class NativeInformationScroll
    {
        readonly Behaviour view;
        readonly ScrollRect standard;
        readonly PropertyInfo content, viewport, vertical, position;
        readonly MethodInfo stop;
        internal NativeInformationScroll(Behaviour component)
        {
            view=component; standard=component as ScrollRect;
            if(standard!=null)return;
            var type=component.GetType();
            content=type.GetProperty("content"); viewport=type.GetProperty("viewport");
            vertical=type.GetProperty("vertical"); position=type.GetProperty("verticalNormalizedPosition");
            stop=type.GetMethod("StopMovement",Type.EmptyTypes);
            if(content==null||content.PropertyType!=typeof(RectTransform)||viewport==null||
                viewport.PropertyType!=typeof(RectTransform)||vertical==null||vertical.PropertyType!=typeof(bool)||
                position==null||position.PropertyType!=typeof(float)||!position.CanWrite)
                throw new MissingMemberException(type.FullName,"native information scrolling");
        }
        internal static bool Supports(Component component)
        {
            if(component is ScrollRect)return true;
            if(!(component is Behaviour))return false;
            for(var type=component.GetType();type!=null;type=type.BaseType)
                if(type.FullName=="Kingmaker.UI.Common.ScrollRectExtended")return true;
            return false;
        }
        RectTransform Content => standard!=null?standard.content:content.GetValue(view,null) as RectTransform;
        RectTransform Viewport => standard!=null?standard.viewport:viewport.GetValue(view,null) as RectTransform;
        internal float Overflow
        {
            get
            {
                if(view==null||!view.isActiveAndEnabled||!(standard!=null?standard.vertical:(bool)vertical.GetValue(view,null)))return 0;
                var body=Content;var window=Viewport??view.transform as RectTransform;
                return body==null||window==null?0:Mathf.Max(0,body.rect.height-window.rect.height);
            }
        }
        internal bool Available => Overflow>1;
        internal bool Scroll(float axis,float seconds)
        {
            float overflow=Overflow;
            if(overflow<=1||float.IsNaN(axis)||float.IsInfinity(axis)||float.IsNaN(seconds)||float.IsInfinity(seconds))return false;
            float step=Mathf.Clamp(axis,-1,1)*Mathf.Clamp(seconds,0,.05f)*650/overflow;
            if(step==0)return false;
            float current=standard!=null?standard.verticalNormalizedPosition:(float)position.GetValue(view,null);
            float next=Mathf.Clamp01(current+step);
            if(standard!=null){standard.StopMovement();standard.verticalNormalizedPosition=next;}
            else{stop?.Invoke(view,null);position.SetValue(view,next,null);}
            return true;
        }
    }
}
