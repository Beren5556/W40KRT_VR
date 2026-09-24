using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        sealed class NativeGroup81 : IDisposable
        {
            internal Component Owner;
            internal RectTransform Root, View;
            internal DialogRectState72 Original;
            internal Vector2 NativeSize;
            internal Vector3 OriginalPosition3D;
            internal InterfaceGroupSettings81 Last;
            internal readonly Dictionary<Component, float> Fonts = new Dictionary<Component,float>();
            internal readonly Dictionary<Component,Vector2> FontLimits = new Dictionary<Component,Vector2>();
            internal readonly List<Component> Nodes = new List<Component>();
            internal float NextAudit;
            internal bool HadParent, Management;
            internal float ReferenceDistance;
            internal NativeGroup81(Component owner,RectTransform view,bool dialogue)
            {
                Owner=owner;View=view;Original=new DialogRectState72(view);NativeSize=view.rect.size;OriginalPosition3D=view.anchoredPosition3D;
                // Native dialogue starts as a short bar. Its two-row layout
                // needs the complete reference viewport, including its mask.
                if(dialogue)NativeSize=_hudNativeReferenceSize;
                HadParent=Original.Parent!=null;Management=owner==_touchMenuWindowView;
                ReferenceDistance=Management?ApprovedUserDefaults.MenuDistance:ApprovedUserDefaults.Distance;
                // Keep the game's parent, animator, CanvasGroup and masks. Only
                // the view geometry is adjusted, before the normal canvas update.
                Root=view;
                Audit();
            }
            internal void Audit()
            {
                if(View==null)return;
                Nodes.Clear();View.GetComponentsInChildren(true,Nodes);
                foreach(var c in Nodes)
                {

                    var property=FontProperty81(c);if(property==null||Fonts.ContainsKey(c))continue;
                    Fonts.Add(c,Convert.ToSingle(property.GetValue(c,null)));Last=null;
                    var min=c.GetType().GetProperty("fontSizeMin");var max=c.GetType().GetProperty("fontSizeMax");
                    if(min!=null&&max!=null)FontLimits[c]=new Vector2(Convert.ToSingle(min.GetValue(c,null)),Convert.ToSingle(max.GetValue(c,null)));
                }
                NextAudit=Time.unscaledTime+.5f;
            }
            internal void Layout(InterfaceGroupSettings81 settings,Vector3 head,Quaternion rotation)
            {
                if(Time.unscaledTime>=NextAudit)Audit();
                Vector2 size=NativeSize;
                if(settings.Aspect>0)size.x=size.y*settings.Aspect;
                bool changed=Last==null||Last.Aspect!=settings.Aspect||Last.Content!=settings.Content;
                bool resized=(Root.rect.size-size).sqrMagnitude>.25f;
                if(changed||resized)
                {
                    Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,size.x);
                    Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,size.y);
                    foreach(var font in Fonts)if(font.Key!=null)SetFont81(font.Key,font.Value*settings.Content);
                    foreach(var font in FontLimits)if(font.Key!=null)SetFontLimits81(font.Key,font.Value*settings.Content);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(View);Last=settings.Copy();
                }
                float scale=HudLayoutWorldScale;
                float near=_pickCam==null?.01f:_pickCam.nearClipPlane;
                float far=_pickCam==null?50*scale:_pickCam.farClipPlane;
                if(!LiveOverlayPolicy.TryDistance81(near,far,scale,settings.Distance,out float depth))return;
                var cover=HudPanelLayout.CoverViews(depth/scale,scale,far,NativeSize.x,_hudViewPlanes);
                var baseline=HudPanelLayout.Calculate(ReferenceDistance,scale,settings.Panel,Root.rect.width,Root.rect.height,far,0,_hudViewPlanes);
                float width=baseline.Width,height=baseline.Height;
                float x=cover.Width*settings.X,y=cover.Height*settings.Y;
                // _hudViewPlanes are measured in metres, unlike Unity transforms.
                if(PanelFit80.Fit(depth/scale,width/scale,height/scale,x/scale,y/scale,_hudViewPlanes,out var fit))
                {width=fit.Width*scale;x=fit.X*scale;y=fit.Y*scale;}
                Vector3 p=new Vector3(x,y,depth);
                Vector3 origin=_hudStableSpace?Vector3.zero:head;Quaternion facing=_hudStableSpace?Quaternion.identity:rotation;
                float desired=width/Root.rect.width;
                Vector3 inherited=Root.parent==null?Vector3.one:Root.parent.lossyScale;
                Vector3 localScale=new Vector3(desired/Mathf.Max(.000001f,Mathf.Abs(inherited.x)),
                    desired/Mathf.Max(.000001f,Mathf.Abs(inherited.y)),desired/Mathf.Max(.000001f,Mathf.Abs(inherited.z)));
                Vector3 pivot=facing*(desired*(Vector3)Root.rect.center);
                PlaceHudTransform(Root,origin+facing*p-pivot,facing,localScale);
            }
            public void Dispose()
            {
                foreach(var p in Fonts)if(p.Key!=null)SetFont81(p.Key,p.Value);
                foreach(var p in FontLimits)if(p.Key!=null)SetFontLimits81(p.Key,p.Value);
                // Never reclaim a view which the game has moved to another parent.
                if(View!=null&&Original.Parent==View.parent){Original.Restore();View.anchoredPosition3D=OriginalPosition3D;}
            }
        }
        static readonly Dictionary<Type,PropertyInfo> _fontProperties81=new Dictionary<Type,PropertyInfo>();
        static PropertyInfo FontProperty81(Component component)
        {
            if(component==null)return null;var type=component.GetType();
            if(!_fontProperties81.TryGetValue(type,out var p))
            {
                p=typeof(Text).IsAssignableFrom(type)||type.FullName.StartsWith("TMPro.",StringComparison.Ordinal)?type.GetProperty("fontSize"):null;
                if(p!=null&&(!p.CanRead||!p.CanWrite))p=null;_fontProperties81[type]=p;
            }
            return p;
        }
        static void SetFont81(Component component,float size)
        {
            var p=FontProperty81(component);if(p==null)return;
            p.SetValue(component,p.PropertyType==typeof(int)?(object)Mathf.RoundToInt(size):size,null);
        }
        static void SetFontLimits81(Component c,Vector2 limits)
        {c.GetType().GetProperty("fontSizeMin").SetValue(c,limits.x,null);c.GetType().GetProperty("fontSizeMax").SetValue(c,limits.y,null);}
        static readonly NativeGroup81[] _nativeGroups81=new NativeGroup81[4]; // management, dialogue, modal tutorial, tip
        static readonly List<Component> _tutorialViews81=new List<Component>();
        static bool IndependentGroupOwns81(Transform node)
        {
            foreach(var g in _nativeGroups81)if(g!=null&&node!=null&&(node==g.View||node.IsChildOf(g.View)))return true;
            return false;
        }
        static void UpdateNativeGroup81(int index,Component owner,RectTransform view,InterfaceGroupSettings81 settings,Vector3 head,Quaternion rotation)
        {
            var group=_nativeGroups81[index];
            if(group!=null&&(group.Owner!=owner||group.View!=view||view==null||!view.gameObject.activeInHierarchy||
                (group.HadParent&&(group.Original.Parent==null||!group.Original.Parent.gameObject.activeInHierarchy))))
            {var old=group.View;group.Dispose();_nativeGroups81[index]=group=null;if(old!=null)MaskUtilities.Notify2DMaskStateChanged(old);}
            if(owner==null||view==null||!view.gameObject.activeInHierarchy||view.rect.width<1||view.rect.height<1)return;
            if(group==null) { _nativeGroups81[index]=group=new NativeGroup81(owner,view,index==1); MaskUtilities.Notify2DMaskStateChanged(view); }
            group.Layout(settings,head,rotation);
        }
        internal static void PrepareNativeGroups81(Vector3 head,Quaternion rotation)
        {
            if(!_active||!_attached||_modeFlat){RestoreNativeGroups81();return;}
            // Management already has the accepted 0.9.80 viewport, font/content,
            // distance and offset controls. Do not apply them a second time.
            Component dialog=null;
            foreach(var entry in _surfaceDialogs72)if(entry.Key!=null&&entry.Key.isActiveAndEnabled&&entry.Value.Dialog!=null&&GetViewModel(entry.Value.Dialog)!=null)
            {dialog=entry.Value.Dialog;break;}
            UpdateNativeGroup81(1,dialog,dialog?.transform as RectTransform,_cfg.dialogLayout81,head,rotation);
            Component large=null,small=null;
            for(int i=_tutorialViews81.Count-1;i>=0;--i)
            {
                var view=_tutorialViews81[i];if(view==null){_tutorialViews81.RemoveAt(i);continue;}
                if(!view.gameObject.activeInHierarchy||GetViewModel(view)==null)continue;
                if(view.GetType().Name=="TutorialModalWindowPCView")large=view;else small=view;
            }
            UpdateNativeGroup81(2,large,TransformFrom72(large==null?null:ReadField70(large,"m_WindowAnimator")) as RectTransform,_cfg.largeTutorialLayout81,head,rotation);
            UpdateNativeGroup81(3,small,TransformFrom72(small==null?null:ReadField70(small,"m_WindowAnimator")) as RectTransform,_cfg.tipLayout81,head,rotation);
        }
        static void RestoreNativeGroups81()
        {
            for(int i=0;i<_nativeGroups81.Length;++i){var view=_nativeGroups81[i]?.View;_nativeGroups81[i]?.Dispose();_nativeGroups81[i]=null;if(view!=null)MaskUtilities.Notify2DMaskStateChanged(view);}
        }
        static void IncludeNativeGroupDepth81(ref float depth,Vector3 head,Vector3 forward)
        {
            foreach(var group in _nativeGroups81)
                if(group?.Root!=null&&group.Root.gameObject.activeInHierarchy)
                    depth=HudCapturePolicy.IncludeHelper(depth,ToPoint(group.Root.position),ToPoint(head),ToPoint(forward),true);
        }
        static Vector2 IndependentDialogueSize81(Vector2 fallback)
        {
            var group=_nativeGroups81[1];
            return group?.Root==null?fallback:group.Root.rect.size;
        }
        static bool IndependentPointer81(Ray ray,out Vector3 point)
        {
            point=default;float best=float.MaxValue;bool found=false;
            for(int i=_nativeGroups81.Length-1;i>=0;--i)
            {
                var g=_nativeGroups81[i];if(g==null||g.Root==null||!g.Root.gameObject.activeInHierarchy)continue;
                if(!new Plane(g.Root.forward,g.Root.position).Raycast(ray,out float d)||d>=best)continue;
                var hit=ray.GetPoint(d);if(!g.Root.rect.Contains(g.Root.InverseTransformPoint(hit)))continue;
                point=hit;best=d;found=true;
                // Modal/tutorial roots take priority over game panels behind.
                if(i>=2)return true;
            }
            return found;
        }
    }
}
