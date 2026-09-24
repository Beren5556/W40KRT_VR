using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        sealed class DialogRectState72
        {
            internal RectTransform Rect;
            internal Transform Parent;
            internal int Sibling;
            internal Vector2 Min,Max,Pivot,Position,Size,Measured;
            internal Vector3 Scale;
            internal Quaternion Rotation;
            internal Rect Bounds;
            internal DialogRectState72(RectTransform rect)
            {
                Rect=rect;Parent=rect.parent;Sibling=rect.GetSiblingIndex();Min=rect.anchorMin;Max=rect.anchorMax;
                Pivot=rect.pivot;Position=rect.anchoredPosition;Size=rect.sizeDelta;Measured=rect.rect.size;
                Scale=rect.localScale;Rotation=rect.localRotation;Bounds=rect.rect;
            }
            internal void Restore()
            {
                if(Rect==null)return;
                Rect.SetParent(Parent,false);Rect.SetSiblingIndex(Sibling);Rect.anchorMin=Min;Rect.anchorMax=Max;
                Rect.pivot=Pivot;Rect.anchoredPosition=Position;Rect.sizeDelta=Size;Rect.localScale=Scale;Rect.localRotation=Rotation;
            }
        }
        sealed class SurfaceDialogState72
        {
            internal Component Dialog;
            internal DialogRectState72 Root,Speaker,Answers,ScrollButton75;
            internal readonly Dictionary<Graphic,bool> PassiveRaycasts75=new Dictionary<Graphic,bool>();
            internal readonly Dictionary<GameObject,bool> Decorations=new Dictionary<GameObject,bool>();
            internal DialogRectState72 AnswerArt76;
            internal readonly List<GameObject> ArtMasks76=new List<GameObject>();
            internal Vector2 Viewport;
            internal Vector2 SpeakerPosition75,AnswersPosition75;
            internal Vector3 SpeakerScale75,AnswersScale75;
            internal int Language=-1,ContentSignature;
            internal bool Applied;
            internal RectTransform Backdrop81;
        }
        static readonly Dictionary<Canvas,SurfaceDialogState72> _surfaceDialogs72=new Dictionary<Canvas,SurfaceDialogState72>();
        static readonly HashSet<Canvas> _dialogPending75=new HashSet<Canvas>();
        static readonly List<Canvas> _dialogWork75=new List<Canvas>();
        static bool _dialogHooks75;
        static void InstallDialogLayout75()
        {
            if(_dialogHooks75)return;_dialogHooks75=true;
            try
            {
                var type=AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.View.Dialog.SurfaceDialog.SurfaceDialogPCView");
                foreach(string method in new[]{"BindViewImplementation","OnPartsUpdating"})
                    _harmony.Patch(AccessTools.DeclaredMethod(type,method),postfix:new HarmonyMethod(typeof(Main),nameof(DialogContentChanged75)));
                Canvas.preWillRenderCanvases+=FlushDialogLayout75;
            }
            catch(Exception e){_log?.Error("[dialog75/contracts] "+e.Message);}
        }
        static void DialogContentChanged75(Component __instance)
        {
            if(!_active||!_attached||__instance==null)return;
            var canvas=__instance.GetComponent<Canvas>();if(canvas==null)return;
            _dialogPending75.Add(canvas);
            // Native Bind has finished; geometry must precede the next pointer
            // sample and first capture, not wait for the 0.5 s discovery scan.
            try{ApplySurfaceDialogLayout72(canvas);}catch(Exception e){_log?.Error("[dialog75/bind] "+e.Message);}
        }
        static void FlushDialogLayout75()
        {
            if(!_active||!_attached||_dialogPending75.Count==0)return;
            _dialogWork75.Clear();_dialogWork75.AddRange(_dialogPending75);_dialogPending75.Clear();
            foreach(var canvas in _dialogWork75)
                try{ApplySurfaceDialogLayout72(canvas);}catch(Exception e){_log?.Error("[dialog75/layout] "+e.Message);}
            _dialogWork75.Clear();
        }

        static void ApplySurfaceDialogLayout72(Canvas canvas)
        {
            if(canvas==null||!canvas.name.StartsWith("SurfaceDialogPCView",StringComparison.Ordinal)||!canvas.gameObject.activeInHierarchy)return;
            var viewport=canvas.transform as RectTransform;if(viewport==null||viewport.rect.width<100||viewport.rect.height<100)return;
            if(!_surfaceDialogs72.TryGetValue(canvas,out var state))
            {
                Component dialog=null;
                foreach(var part in canvas.GetComponents<Component>())
                    if(part!=null&&part.GetType().Name=="SurfaceDialogPCView"){dialog=part;break;}
                if(dialog==null||GetViewModel(dialog)==null)return;
                Transform speakerScroll=TransformFrom72(ReadField70(dialog,"m_SpeakerScrollRect"));
                Transform answerScroll=TransformFrom72(ReadField70(dialog,"m_AnswerScrollRect"));
                RectTransform speaker=DialogPanelRoot72(viewport,speakerScroll,TransformFrom72(ReadField70(dialog,"m_SpeakerPortrait")),answerScroll);
                RectTransform answers=DialogPanelRoot72(viewport,answerScroll,TransformFrom72(ReadField70(dialog,"m_AnswererPortrait")),speakerScroll);
                if(speaker==null||answers==null||speaker==answers||speaker.IsChildOf(answers)||answers.IsChildOf(speaker))return;
                // Measure complete panels after binding, not a transient answer
                // viewport that excludes its frame and the player's portrait.
                LayoutRebuilder.ForceRebuildLayoutImmediate(speaker);LayoutRebuilder.ForceRebuildLayoutImmediate(answers);
                if(speaker.rect.width<100||answers.rect.width<100||speaker.rect.height<80||answers.rect.height<80)return;
                state=new SurfaceDialogState72{Dialog=dialog,Root=new DialogRectState72(viewport),Speaker=new DialogRectState72(speaker),Answers=new DialogRectState72(answers)};
                _surfaceDialogs72.Add(canvas,state);HideDialogDecorations72(canvas,state);
                state.Speaker.Bounds=DialogFrameBounds75(speaker);state.Answers.Bounds=DialogFrameBounds75(answers);
                SplitDialogParchment76(state);
                MoveDialogScrollButton75(state);
                RemovePassiveDialogRaycasts75(state);
            }
            if(state.Dialog==null||state.Speaker.Rect==null||state.Answers.Rect==null)return;
            Transform history=TransformFrom72(ReadField70(state.Dialog,"m_HistoryContainer"));
            Transform answerPanel=TransformFrom72(ReadField70(state.Dialog,"m_AnswerPanel"));
            int signature=DialogContentSignature73(history,answerPanel);
            // The native dialog canvas is a 451-unit HIGH horizontal bar, not
            // the headset viewport. Fitting two rows into that bar halves all
            // text and portrait sizes. Use the complete logical HUD surface.
            Vector2 size=IndependentDialogueSize81(_hudReferenceSize);
            if(_cfg.dialogLayout81.Aspect>0)size.x=size.y*_cfg.dialogLayout81.Aspect;
            if(state.ScrollButton75?.Rect!=null)state.ScrollButton75.Rect.localScale=Vector3.zero;
            if(state.Applied&&state.Viewport==size&&viewport.rect.size==size&&state.Language==ModLocalization.Revision&&state.ContentSignature==signature&&
                state.Speaker.Rect.parent==viewport&&state.Answers.Rect.parent==viewport&&
                state.Speaker.Rect.anchoredPosition==state.SpeakerPosition75&&state.Answers.Rect.anchoredPosition==state.AnswersPosition75&&
                state.Speaker.Rect.localScale==state.SpeakerScale75&&state.Answers.Rect.localScale==state.AnswersScale75)return;
            state.Viewport=size;state.Language=ModLocalization.Revision;state.ContentSignature=signature;
            var a=state.Speaker;var b=state.Answers;
            // The native surface is a short horizontal strip. Expanding only
            // its children leaves the lower answer panel outside the actual
            // canvas/raycast bounds. Give the real dialog root the same logical
            // HUD extent used for layout; its original geometry is retained in
            // Root and restored when VR stops.
            // sizeDelta is an offset for stretched native anchors, not the
            // actual viewport size. Preserve those anchors without doubling
            // the panel extent or undoing the independent group's geometry.
            viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,size.x);
            viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,size.y);
            float width=Mathf.Max(a.Bounds.width,b.Bounds.width);
            float speakerFactor=1,answerFactor=1;
            float speakerHeight=a.Bounds.height*speakerFactor,answerHeight=b.Bounds.height*answerFactor,gap=-18;
            // One transform scales frame, portrait, font and native hit areas.
            // Preserve the viewport instead of clipping the lower block.
            float scale=Mathf.Min(1.20f,Mathf.Min(size.x*.94f/(width+144),size.y*.86f/(speakerHeight+answerHeight+gap)));
            scale=Mathf.Max(.1f,scale);
            StackDialogRect72(a,viewport,new Vector2(0,(answerHeight+gap)*scale*.5f),scale*speakerFactor);
            StackDialogRect72(b,viewport,new Vector2(0,-(speakerHeight+gap)*scale*.5f),scale*answerFactor);
            // Only frame edges overlap. Speaker draws over the joined edge,
            // while portraits/text/answer hit areas retain their native layout.
            if(a.Rect.GetSiblingIndex()<b.Rect.GetSiblingIndex())a.Rect.SetSiblingIndex(b.Rect.GetSiblingIndex());
            LayoutRebuilder.ForceRebuildLayoutImmediate(a.Rect);LayoutRebuilder.ForceRebuildLayoutImmediate(b.Rect);
            state.SpeakerPosition75=a.Rect.anchoredPosition;state.AnswersPosition75=b.Rect.anchoredPosition;
            state.SpeakerScale75=a.Rect.localScale;state.AnswersScale75=b.Rect.localScale;
            PlaceDialogueBackdrop81(state,width*scale,(speakerHeight+answerHeight+gap)*scale);
            state.Applied=true;
            if(DiagnosticsRecording)_log.Log("[dialog72] Native stacked panels: speaker="+a.Rect.name+" answers="+b.Rect.name+
                " width="+width+" heights="+speakerHeight+"/"+answerHeight+" scale="+scale+" viewport="+size);
        }
        static int DialogContentSignature73(Transform history,Transform answers)
        {
            unchecked
            {
                int hash=17;
                foreach(var root in new[]{history,answers})
                {
                    if(root==null){hash=hash*31;continue;}
                    hash=hash*31+root.childCount;
                    foreach(var rect in root.GetComponentsInChildren<RectTransform>(true))
                    {
                        if(rect==null||!rect.gameObject.activeSelf)continue;
                        hash=hash*31+Mathf.RoundToInt(rect.rect.width);
                        hash=hash*31+Mathf.RoundToInt(rect.rect.height);
                        foreach(var component in rect.GetComponents<Component>())
                        {
                            if(component==null)continue;
                            string text=component is Text legacy?legacy.text:TryGetProp(component,"text") as string;
                            if(!string.IsNullOrEmpty(text))hash=hash*31+text.GetHashCode();
                        }
                    }
                }
                return hash;
            }
        }
        static RectTransform DialogPanelRoot72(Transform canvas,Transform content,Transform portrait,Transform other)
        {
            if(content==null||portrait==null||!content.IsChildOf(canvas)||!portrait.IsChildOf(canvas))return null;
            for(Transform node=content;node!=null&&node!=canvas;node=node.parent)
                if((portrait==node||portrait.IsChildOf(node))&&!(other!=null&&(other==node||other.IsChildOf(node))))return node as RectTransform;
            return null;
        }
        static Transform TransformFrom72(object value)
        {
            if(value is Component component)return component.transform;
            if(value is GameObject gameObject)return gameObject.transform;
            return value as Transform;
        }
        static Rect DialogPanelBounds72(RectTransform panel)
        {
            // Direct children contain the native frame/portrait/scroll holders;
            // their unclipped scrolling descendants must not enlarge the panel.
            Rect bounds=panel.rect;var corners=new Vector3[4];
            foreach(Transform child in panel)
            {
                var rect=child as RectTransform;if(rect==null||!rect.gameObject.activeSelf)continue;
                rect.GetWorldCorners(corners);
                foreach(var corner in corners)
                {
                    var p=panel.InverseTransformPoint(corner);
                    bounds.xMin=Mathf.Min(bounds.xMin,p.x);bounds.xMax=Mathf.Max(bounds.xMax,p.x);
                    bounds.yMin=Mathf.Min(bounds.yMin,p.y);bounds.yMax=Mathf.Max(bounds.yMax,p.y);
                }
            }
            return bounds;
        }
        static Rect DialogFrameBounds75(RectTransform panel)
        {
            // Verified native prefab: DeviceFront is the complete 416-high
            // frame. Parent bounds also include empty space/hover decorations.
            var frame=panel.Find("DeviceFront") as RectTransform;
            if(frame==null)return DialogPanelBounds72(panel);
            var corners=new Vector3[4];frame.GetWorldCorners(corners);
            var min=panel.InverseTransformPoint(corners[0]);var max=min;
            foreach(var c in corners){var p=panel.InverseTransformPoint(c);min=Vector3.Min(min,p);max=Vector3.Max(max,p);}
            return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
        }
        static void MoveDialogScrollButton75(SurfaceDialogState72 state)
        {
            var rect=TransformFrom72(ReadField70(state.Dialog,"m_ScrollToBottomButton")) as RectTransform;
            if(rect==null)return;
            for(var parent=rect.parent;parent!=null&&parent!=state.Speaker.Rect;parent=parent.parent)
                if(parent.name=="ButtonHolder"){rect=parent as RectTransform;break;}
            state.ScrollButton75=new DialogRectState72(rect);
            // Keep the parent: native visibility subscriptions reference it.
            // Native animation may re-enable the button: zero scale keeps all
            // of its art and hit area absent without disabling the scroll model.
            rect.localScale=Vector3.zero;
        }
        static void RemovePassiveDialogRaycasts75(SurfaceDialogState72 state)
        {
            // Frame/background Images should not intercept answer clicks.
            // Keep portrait, button, scroll and text hit areas unchanged.
            foreach(var root in new[]{state.Root.Rect,state.Speaker.Rect,state.Answers.Rect})
            {
                foreach(var graphic in root.GetComponents<Graphic>())
                {state.PassiveRaycasts75[graphic]=graphic.raycastTarget;graphic.raycastTarget=false;}
                foreach(string name in new[]{"DeviceFront","BackgroundImage","MonitorImage"})
                {
                    var child=root.Find(name);if(child==null)continue;
                    foreach(var graphic in child.GetComponents<Graphic>())
                    {state.PassiveRaycasts75[graphic]=graphic.raycastTarget;graphic.raycastTarget=false;}
                }
            }
        }
        static void StackDialogRect72(DialogRectState72 state,RectTransform parent,Vector2 position,float scale)
        {
            var rect=state.Rect;
            if(rect.parent!=parent)rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=state.Pivot;
            rect.anchoredPosition=position-state.Bounds.center*scale;
            rect.sizeDelta=state.Measured;rect.localScale=Vector3.one*scale;rect.localRotation=Quaternion.identity;
        }
        static void HideDialogDecorations72(Canvas canvas,SurfaceDialogState72 state)
        {
            foreach(var node in canvas.GetComponentsInChildren<Transform>(true))
            {
                if(node==null||node==canvas.transform)continue;
                string name=node.name.ToLowerInvariant();var image=node.GetComponent<Image>();
                string sprite=image==null||image.sprite==null?"":image.sprite.name.ToLowerInvariant();
                bool decorative=name.Contains("candle")||sprite.Contains("candle")||name.Contains("wax")||name.Contains("flame")||name.Contains("decoration")||name.Contains("ornament");
                if(!decorative)continue;
                bool functional=false;
                foreach(var part in node.GetComponentsInChildren<Component>(true))
                {
                    if(part==null)continue;string type=part.GetType().Name;
                    if(part is ScrollRect||part is Selectable||type.Contains("Button")||type.Contains("Text")||type.Contains("Portrait")){functional=true;break;}
                }
                if(functional)continue;
                state.Decorations[node.gameObject]=node.gameObject.activeSelf;node.gameObject.SetActive(false);
            }
        }
        static void RestoreSurfaceDialogLayout72()
        {
            foreach(var pair in _surfaceDialogs72)
            {
                var s=pair.Value;if(s==null)continue;s.ScrollButton75?.Restore();s.Speaker?.Restore();s.Answers?.Restore();s.Root?.Restore();
                foreach(var item in s.PassiveRaycasts75)if(item.Key!=null)item.Key.raycastTarget=item.Value;
                foreach(var item in s.Decorations)if(item.Key!=null)item.Key.SetActive(item.Value);
                s.AnswerArt76?.Restore();
                foreach(var item in s.ArtMasks76)if(item!=null)UnityEngine.Object.Destroy(item);
            }
            _surfaceDialogs72.Clear();_dialogPending75.Clear();_dialogWork75.Clear();
        }
    }
}
