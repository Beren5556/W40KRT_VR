using UnityEngine;
using UnityEngine.UI;
namespace RTMaquetaXR
{
    internal static class TouchRadialBadgeLayout
    {
        internal static float Size(int index,int count,float iconSize,int inner=-1)
        {
            float band=(TouchRadialLayout.OuterRadius-TouchRadialLayout.InnerRadius)/TouchRadialPolicy.Rings(count,inner);
            // Keep the plate inside its native ring/sector even when a crowded
            // wheel gives the portrait almost the whole radial band. The
            // numeral itself is enlarged separately; forcing a large plate
            // here would spill into the adjacent action's hit region.
            float gap=Mathf.Min(6,band*.035f);
            float available=band*.5f-iconSize*.5f-gap*2;
            return Mathf.Max(.5f,Mathf.Min(44,Mathf.Min(iconSize*.31f,available)));
        }
        internal static Vector2 Offset(int index,int count,float iconSize,float size,int inner=-1)
        {
            float band=(TouchRadialLayout.OuterRadius-TouchRadialLayout.InnerRadius)/TouchRadialPolicy.Rings(count,inner);
            float angle=(float)TouchRadialPolicy.Angle(index,count,inner);
            return new Vector2(Mathf.Sin(angle),Mathf.Cos(angle))*(iconSize*.5f+size*.5f+Mathf.Min(6,band*.035f));
        }
    }
    // Retained badge geometry: contrast plate and an optional fallback arrow.
    // Original native level-up sprite remains the foreground when available.
    internal sealed class TouchRadialPortraitBadge : Graphic
    {
        bool arrow;
        internal void Arrow(bool value) { if(arrow==value)return;arrow=value;SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            Build(vh,Mathf.Min(rectTransform.rect.width,rectTransform.rect.height),arrow);
        }
        internal static void Build(VertexHelper vh,float size,bool arrow)
        {
            vh.Clear();float detailScale=Mathf.Min(1,size/22f),radius=Mathf.Max(0,size*.5f-2.4f*detailScale);
            TouchRadialArtwork.Disc(vh,Vector2.zero,radius,TouchRadialPalette.Gold,32);
            TouchRadialArtwork.Disc(vh,Vector2.zero,Mathf.Max(0,radius-2.2f*detailScale),TouchRadialPalette.Surface,32);
            if(!arrow)return;
            float a=radius*.55f;
            TouchRadialArtwork.Line(vh,new Vector2(0,-a),new Vector2(0,a),3.2f*detailScale,TouchRadialPalette.Gold);
            TouchRadialArtwork.Line(vh,new Vector2(-a*.7f,a*.15f),new Vector2(0,a),3.2f*detailScale,TouchRadialPalette.Gold);
            TouchRadialArtwork.Line(vh,new Vector2(a*.7f,a*.15f),new Vector2(0,a),3.2f*detailScale,TouchRadialPalette.Gold);
        }
    }
    public static partial class Main
    {
        static Image[] _touchRadialLevelBadges=new Image[0];
        static TouchRadialPortraitBadge[] _touchRadialLevelPlates=new TouchRadialPortraitBadge[0];
        static TouchRadialPortraitBadge CreateTouchRadialPortraitBadge(RectTransform parent,string name,Vector2 anchor,float size)
        {
            var root=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(TouchRadialPortraitBadge));
            root.layer=parent.gameObject.layer;root.transform.SetParent(parent,false);
            var badge=root.GetComponent<TouchRadialPortraitBadge>();badge.raycastTarget=false;badge.material=_touchRadialMaterial;
            badge.rectTransform.anchorMin=badge.rectTransform.anchorMax=anchor;badge.rectTransform.sizeDelta=new Vector2(size,size);
            return badge;
        }
        static void CreateTouchRadialLevelBadges()
        {
            _touchRadialLevelBadges=new Image[_touchRadialEntries.Count];
            _touchRadialLevelPlates=new TouchRadialPortraitBadge[_touchRadialEntries.Count];
            for(int i=0;i<_touchRadialEntries.Count;++i)
            {
                var entry=_touchRadialEntries[i];if(!entry.Party)continue;
                var icon=_touchRadialIcons[i];
                float iconSize=icon.rectTransform.sizeDelta.x;
                float size=TouchRadialBadgeLayout.Size(i,_touchRadialEntries.Count,iconSize,_touchRadialInnerCount);
                var plate=CreateTouchRadialPortraitBadge(icon.rectTransform,"Native party level-up indicator",new Vector2(.5f,.5f),size);
                plate.rectTransform.anchoredPosition=TouchRadialBadgeLayout.Offset(i,_touchRadialEntries.Count,iconSize,size,_touchRadialInnerCount);
                _touchRadialLevelPlates[i]=plate;
                var root=new GameObject("Native party level-up badge",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
                root.layer=icon.gameObject.layer;root.transform.SetParent(plate.transform,false);
                var badge=root.GetComponent<Image>();badge.raycastTarget=false;badge.preserveAspect=true;badge.material=_touchRadialMaterial;
                var rect=badge.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.sizeDelta=new Vector2(size*.8f,size*.8f);
                _touchRadialLevelBadges[i]=badge;UpdateTouchRadialLevelBadge(i,entry);
            }
        }
        static void UpdateTouchRadialLevelBadge(int index,TouchRadialEntry entry)
        {
            if(index<0||index>=_touchRadialLevelBadges.Length)return;
            var badge=_touchRadialLevelBadges[index];if(badge==null)return;
            bool visible=entry.Party&&!_touchRadialCombat&&entry.LevelUp;
            var plate=_touchRadialLevelPlates[index];
            if(plate!=null){if(plate.enabled!=visible)plate.enabled=visible;plate.Arrow(visible&&entry.LevelUpIcon==null);}
            bool native=visible&&entry.LevelUpIcon!=null;
            if(badge.enabled!=native)badge.enabled=native;
            if(badge.sprite!=entry.LevelUpIcon)badge.sprite=entry.LevelUpIcon;
            if(badge.color!=entry.LevelUpColor)badge.color=entry.LevelUpColor;
        }
        static void ClearTouchRadialLevelBadges()
        {
            foreach(var badge in _touchRadialLevelPlates)if(badge!=null)UnityEngine.Object.Destroy(badge.gameObject);
            _touchRadialLevelBadges=new Image[0];
            _touchRadialLevelPlates=new TouchRadialPortraitBadge[0];
        }
    }
}
