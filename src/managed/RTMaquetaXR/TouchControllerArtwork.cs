using UnityEngine;
using UnityEngine.UI;
namespace RTMaquetaXR
{
    // Only illustrations use these icons; the tracked 3D servo-skulls are unchanged.
    internal static class TouchControllerArtwork
    {
        static Texture2D texture;
        internal static Texture2D Texture => texture??(texture=ControlIconAtlas.Load("RTMaquetaXR.TouchControllers.png"));
        internal static Rect Uv(bool left) => new Rect(left?0:.5f,0,.5f,1);
        internal static RawImage Create(Transform parent,bool left,Material material)
        {
            var obj=new GameObject(left?"Approved left Touch illustration":"Approved right Touch illustration",typeof(RectTransform),typeof(CanvasRenderer),typeof(RawImage));
            obj.layer=5;obj.transform.SetParent(parent,false);var image=obj.GetComponent<RawImage>();
            image.raycastTarget=false;image.material=material;image.texture=Texture;image.uvRect=Uv(left);
            image.rectTransform.anchorMin=image.rectTransform.anchorMax=image.rectTransform.pivot=Vector2.one*.5f;return image;
        }
        internal static void Place(RawImage image,bool left,Vector2 position,float height,bool active)
        {
            if(image==null)return;image.rectTransform.anchoredPosition=position;
            image.rectTransform.sizeDelta=new Vector2(height*Texture.width*.5f/Texture.height,height);
            image.color=new Color(1,1,1,active?1:.72f);
        }
        internal static void Flat(Rect box,bool left)
        { var color=GUI.color;try{GUI.color=Color.white;GUI.DrawTextureWithTexCoords(box,Texture,Uv(left),true);}finally{GUI.color=color;} }
    }
    internal sealed class TouchGuideIconGraphic : MaskableGraphic
    {
        internal TouchGuideAnimation Animation;
        internal bool Keys;
        public override Texture mainTexture => ControlIconAtlas.Texture(Keys);
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();if(Animation==null)return;
            for(int i=0;i<Animation.IconCount;i++) {
                var icon=Animation.Icons[i];if(ControlIconAtlasLayout.Keyboard(icon.Symbol)!=Keys)continue;
                ControlIconDrawing.MeshBody(mesh,new Rect(icon.X-TouchGuideAnimation.Width*.5f,
                    TouchGuideAnimation.Height*.5f-icon.Y-icon.Size,icon.Size,icon.Size),icon.Symbol,Color.white);
            }
        }
    }
}
