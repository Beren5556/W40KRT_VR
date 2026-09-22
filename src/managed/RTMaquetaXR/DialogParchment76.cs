using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static void SplitDialogParchment76(SurfaceDialogState72 state)
        {
            Image art=null;
            foreach(var image in state.Answers.Rect.GetComponentsInChildren<Image>(true))
                if(image.sprite!=null&&image.sprite.name=="Dialogue_BackRight"){art=image;break;}
            if(art==null||art.type!=Image.Type.Simple)return;
            // Native atlas: the first 48 of 1884 pixels finish the white left
            // panel. Move just that decoration, never text or answer hit areas.
            var rect=art.rectTransform;float width=rect.rect.width,height=rect.rect.height;
            if(width<=0||height<=0)return;
            float strip=width*(48f/1884f);
            state.AnswerArt76=new DialogRectState72(rect);
            Transform parent=rect.parent;int sibling=rect.GetSiblingIndex();
            var corners=new Vector3[4];rect.GetWorldCorners(corners);
            Vector3 lowerLeft=parent.InverseTransformPoint(corners[0]);
            var mask=DialogArtMask76("Answer frame",parent,new Vector2(lowerLeft.x+strip,lowerLeft.y),new Vector2(width-strip,height),state);
            mask.SetSiblingIndex(sibling);
            rect.SetParent(mask,false);rect.anchorMin=rect.anchorMax=Vector2.zero;rect.pivot=Vector2.zero;
            rect.anchoredPosition=new Vector2(-strip,0);rect.sizeDelta=new Vector2(width,height);rect.localScale=Vector3.one;
            var bounds=state.Speaker.Bounds;
            var upper=DialogArtMask76("Speaker parchment edge",state.Speaker.Rect,new Vector2(bounds.xMax,bounds.yMin),new Vector2(strip,height),state);
            var copy=new GameObject("Original parchment continuation",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            copy.layer=art.gameObject.layer;copy.transform.SetParent(upper,false);
            var imageCopy=copy.GetComponent<Image>();imageCopy.sprite=art.sprite;imageCopy.color=art.color;imageCopy.material=art.material;
            imageCopy.raycastTarget=false;
            var cr=imageCopy.rectTransform;cr.anchorMin=cr.anchorMax=Vector2.zero;cr.pivot=Vector2.zero;cr.anchoredPosition=Vector2.zero;cr.sizeDelta=new Vector2(width,height);
            state.Speaker.Bounds=Rect.MinMaxRect(bounds.xMin,bounds.yMin,bounds.xMax+strip,bounds.yMax);
            bounds=state.Answers.Bounds;
            state.Answers.Bounds=Rect.MinMaxRect(bounds.xMin+strip,bounds.yMin,bounds.xMax,bounds.yMax);
        }
        static RectTransform DialogArtMask76(string name,Transform parent,Vector2 localPosition,Vector2 size,SurfaceDialogState72 state)
        {
            var go=new GameObject("RTMaquetaXR "+name,typeof(RectTransform),typeof(RectMask2D));
            go.layer=parent.gameObject.layer;go.transform.SetParent(parent,false);
            var rect=(RectTransform)go.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=Vector2.zero;
            rect.localPosition=new Vector3(localPosition.x,localPosition.y,0);rect.sizeDelta=size;
            state.ArtMasks76.Add(go);return rect;
        }
    }
}
