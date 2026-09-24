using UnityEngine;
using UnityEngine.UI;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static void PlaceDialogueBackdrop81(SurfaceDialogState72 state,float width,float height)
        {
            if(state.Backdrop81==null)
            {
                var root=new GameObject("RTMaquetaXR Dialogue unified frame",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
                root.layer=state.Root.Rect.gameObject.layer;root.transform.SetParent(state.Root.Rect,false);
                state.Backdrop81=(RectTransform)root.transform;state.ArtMasks76.Add(root);
                var fill=root.GetComponent<Image>();fill.color=new Color(.035f,.045f,.043f,.98f);fill.raycastTarget=false;
                // Reuse the native decorative frame as sliced exterior art.
                Image native=null;
                foreach(var candidate in state.Speaker.Rect.GetComponentsInChildren<Image>(true))
                    if(candidate.sprite!=null&&candidate.sprite.border.sqrMagnitude>0&&candidate.gameObject.name=="DeviceFront") {native=candidate;break;}
                for(int i=0;i<4;++i)
                {
                    var edge=new GameObject("Native metal edge "+i,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
                    edge.layer=root.layer;edge.transform.SetParent(root.transform,false);
                    var image=edge.GetComponent<Image>();image.raycastTarget=false;
                    if(native!=null){image.sprite=native.sprite;image.type=Image.Type.Sliced;image.material=native.material;image.color=native.color;}
                    else image.color=new Color(.24f,.25f,.21f,1);
                    var rect=image.rectTransform;
                    rect.anchorMin=i<2?new Vector2(0,i):new Vector2(i-2,0);
                    rect.anchorMax=i<2?new Vector2(1,i):new Vector2(i-2,1);
                    rect.sizeDelta=i<2?new Vector2(8,8):new Vector2(8,8);rect.anchoredPosition=Vector2.zero;
                }
            }
            var panel=state.Backdrop81;panel.SetAsFirstSibling();panel.anchorMin=panel.anchorMax=new Vector2(.5f,.5f);
            panel.pivot=new Vector2(.5f,.5f);panel.anchoredPosition=Vector2.zero;panel.localScale=Vector3.one;
            panel.sizeDelta=new Vector2(width+20,height+20);
        }
    }
}
