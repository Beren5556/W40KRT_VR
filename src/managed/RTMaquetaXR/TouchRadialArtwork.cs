using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Sprites;

namespace RTMaquetaXR
{
    internal static class TouchRadialFooterLayout
    {
        // Unboxed text stays inside the wheel half of the spatial atlas (±600),
        // with control hints ABOVE the wheel, clear of the native status meters.
        internal static readonly Vector2 ActionPosition=new Vector2(635,-50),ActionTextSize=new Vector2(280,250);
        internal static readonly Vector2 HintPosition=new Vector2(-10,543),HintTextSize=new Vector2(660,108);
    }
    internal static class TouchRadialPalette
    {
        internal static readonly Color Surface = new Color(.008f,.01f,.012f,1);
        internal static readonly Color Panel = new Color(.018f,.023f,.025f,1);
        internal static readonly Color Brass = new Color(.37f,.25f,.11f,1);
        internal static readonly Color Edge = new Color(.62f,.46f,.23f,1);
        internal static readonly Color Rule = new Color(.12f,.13f,.12f,1);
        internal static readonly Color Gold = new Color(1,.72f,.25f,1);
        internal static readonly Color Cyan = new Color(.21f,.86f,1,1);
        internal static readonly Color Ink = new Color(.96f,.94f,.83f,1);
        internal static readonly Color Muted = new Color(.87f,.87f,.87f,1);
    }
    internal static class TouchRadialArtwork
    {
        internal static readonly Vector2 TitlePosition=new Vector2(0,96),TitleBox=new Vector2(180,30);
        internal static readonly Vector2 LabelPosition=new Vector2(0,9),LabelBox=new Vector2(224,126);
        internal static readonly Vector2 CancelPosition=new Vector2(0,-99),CancelBox=new Vector2(180,24);
        internal static readonly Vector2 SwitchPosition=new Vector2(0,-73),SwitchBox=new Vector2(202,20);
        internal static float FrameScale(int count,int inner=-1) => Mathf.Min(1,315f/Mathf.Max(1,TouchRadialPolicy.Rings(count,inner))/50);
        internal static float IconSize(int index,int count,bool portrait,int inner=-1)
        {
            int rings=TouchRadialPolicy.Rings(count,inner),slots=TouchRadialPolicy.RingCount(count,TouchRadialPolicy.RingFor(index,count,inner),inner);
            float band=(TouchRadialLayout.OuterRadius-TouchRadialLayout.InnerRadius)/Mathf.Max(1,rings);
            float chord=2*TouchRadialLayout.Radius(index,count,inner)*Mathf.Sin(Mathf.Min(Mathf.PI*.5f,(float)TouchRadialPolicy.HalfAngle(index,count,inner)));
            return Mathf.Max(.5f,Mathf.Min(portrait?154:132,Mathf.Min(Mathf.Min(band*.68f,band-28*FrameScale(count,inner)),chord*.72f)));
        }
        internal static Vector2 ActionExtent(float radius,float aspect)
        {
            // The full source rectangle fits inside the round plate, including
            // all four corners. Long symbols remain complete, never cropped.
            aspect=Mathf.Max(.01f,aspect);float y=radius/Mathf.Sqrt(aspect*aspect+1);
            return new Vector2(y*aspect,y);
        }
        internal static Vector2 IconUv(Vector2 unit,Vector4 bounds,float aspect,bool portrait)
        {
            // Portrait medallions favour the upper face. Action quads use their
            // entire source rectangle. UVs never leave
            // the native sprite's atlas rectangle, including non-square art.
            aspect=Mathf.Max(.01f,aspect);
            if(!portrait)return new Vector2(Mathf.Lerp(bounds.x,bounds.z,(unit.x+1)*.5f),Mathf.Lerp(bounds.y,bounds.w,(unit.y+1)*.5f));
            float u=.5f+unit.x*.5f*Mathf.Min(1,1/aspect);
            float span=Mathf.Min(1,aspect),center=portrait ? .5f+(1-span)*.20f : .5f;
            float v=center+unit.y*.5f*span;
            return new Vector2(Mathf.Lerp(bounds.x,bounds.z,u),Mathf.Lerp(bounds.y,bounds.w,v));
        }
        internal static void Disc(VertexHelper vh,Vector2 center,float radius,Color color,int pieces=48)
        {
            int first=vh.currentVertCount;vh.AddVert(center,color,Vector2.zero);
            for(int n=0;n<=pieces;++n)
            {
                float angle=n*Mathf.PI*2/pieces;
                vh.AddVert(center+new Vector2(Mathf.Sin(angle),Mathf.Cos(angle))*radius,color,Vector2.zero);
                if(n>0)vh.AddTriangle(first,first+n,first+n+1);
            }
            FeatherArc(vh,center,0,Mathf.PI*2,radius,radius+2.4f,color,Clear(color),pieces);
        }
        internal static void Arc(VertexHelper vh,Vector2 center,float start,float end,float inner,float outer,Color color,int pieces)
        {
            FeatherArc(vh,center,start,end,inner,outer,color,color,pieces);
            FeatherArc(vh,center,start,end,Mathf.Max(0,inner-2.4f),inner,Clear(color),color,pieces);
            FeatherArc(vh,center,start,end,outer,outer+2.4f,color,Clear(color),pieces);
        }
        static Color Clear(Color value) { value.a=0; return value; }
        // One coverage fringe in the independent HUD capture. This removes
        // hard one-pixel steps without temporal history, extra cameras, full
        // screen supersampling or applying DLSS to the wheel.
        static void FeatherArc(VertexHelper vh,Vector2 center,float start,float end,float inner,float outer,Color inside,Color outside,int pieces)
        {
            int first=vh.currentVertCount;
            for(int n=0;n<=pieces;++n)
            {
                float a=Mathf.Lerp(start,end,n/(float)pieces);var x=new Vector2(Mathf.Sin(a),Mathf.Cos(a));
                vh.AddVert(center+x*inner,inside,Vector2.zero);vh.AddVert(center+x*outer,outside,Vector2.zero);
                if(n==0)continue;int at=first+(n-1)*2;
                vh.AddTriangle(at,at+1,at+3);vh.AddTriangle(at,at+3,at+2);
            }
        }
        internal static void Line(VertexHelper vh,Vector2 from,Vector2 to,float width,Color color)
        {
            TouchPointerGraphic.Line(vh,from,to,width,color);
            var delta=to-from;float length=delta.magnitude;if(length<.001f)return;
            var normal=new Vector2(-delta.y,delta.x)/length;
            for(int sign=-1;sign<=1;sign+=2)
            {
                var a=normal*(width*.5f*sign);var b=normal*((width*.5f+2.4f)*sign);int first=vh.currentVertCount;
                vh.AddVert(from+a,color,Vector2.zero);vh.AddVert(to+a,color,Vector2.zero);
                vh.AddVert(to+b,Clear(color),Vector2.zero);vh.AddVert(from+b,Clear(color),Vector2.zero);
                vh.AddTriangle(first,first+1,first+2);vh.AddTriangle(first,first+2,first+3);
            }
        }
        static Vector2 PlatePoint(int n,Vector2 extent,float bevel)
        {
            switch(n%8)
            {
                case 0:return new Vector2(-extent.x+bevel,-extent.y);
                case 1:return new Vector2(-extent.x,-extent.y+bevel);
                case 2:return new Vector2(-extent.x,extent.y-bevel);
                case 3:return new Vector2(-extent.x+bevel,extent.y);
                case 4:return new Vector2(extent.x-bevel,extent.y);
                case 5:return new Vector2(extent.x,extent.y-bevel);
                case 6:return new Vector2(extent.x,-extent.y+bevel);
                default:return new Vector2(extent.x-bevel,-extent.y);
            }
        }
        internal static void Plaque(VertexHelper vh,Vector2 center,Vector2 extent,float trim,Color focus)
        {
            int first=vh.currentVertCount;vh.AddVert(center,TouchRadialPalette.Surface,Vector2.zero);
            for(int n=0;n<=8;++n)
            {
                vh.AddVert(center+PlatePoint(n,extent+Vector2.one*8*trim,8*trim),TouchRadialPalette.Surface,Vector2.zero);
                if(n>0)vh.AddTriangle(first,first+n,first+n+1);
            }
            PlaqueRim(vh,center,extent,3*trim,6*trim,focus);
            PlaqueRim(vh,center,extent,7*trim,8*trim,TouchRadialPalette.Edge);
        }
        static void PlaqueRim(VertexHelper vh,Vector2 center,Vector2 extent,float inside,float outside,Color color)
        {
            for(int n=0;n<8;++n)
            {
                int first=vh.currentVertCount;
                vh.AddVert(center+PlatePoint(n,extent+Vector2.one*inside,inside),color,Vector2.zero);
                vh.AddVert(center+PlatePoint(n,extent+Vector2.one*outside,outside),color,Vector2.zero);
                vh.AddVert(center+PlatePoint(n+1,extent+Vector2.one*outside,outside),color,Vector2.zero);
                vh.AddVert(center+PlatePoint(n+1,extent+Vector2.one*inside,inside),color,Vector2.zero);
                vh.AddTriangle(first,first+1,first+2);vh.AddTriangle(first,first+2,first+3);
            }
        }
    }
    // Borrow the same Sprite as the game's Image. A circular mesh clips its
    // corners without extra cameras, textures, stencil masks or materials.
    internal sealed class TouchRadialIcon : Image
    {
        internal bool Portrait;
        Sprite accent;
        internal Sprite Accent { get=>accent; set {if(accent==value)return;accent=value;SetVerticesDirty();} }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var art=overrideSprite!=null?overrideSprite:sprite;if(art==null)return;
            Rect rect=GetPixelAdjustedRect();Vector4 uv=DataUtility.GetOuterUV(art);
            float aspect=art.rect.width/Mathf.Max(1,art.rect.height),radius=Mathf.Min(rect.width,rect.height)*.5f;
            if(!Portrait)
            {
                Vector2 extent=TouchRadialArtwork.ActionExtent(radius,aspect);Color32 tint=color;
                vh.AddVert(rect.center+new Vector2(-extent.x,-extent.y),tint,new Vector2(uv.x,uv.y));
                vh.AddVert(rect.center+new Vector2(-extent.x,extent.y),tint,new Vector2(uv.x,uv.w));
                vh.AddVert(rect.center+new Vector2(extent.x,extent.y),tint,new Vector2(uv.z,uv.w));
                vh.AddVert(rect.center+new Vector2(extent.x,-extent.y),tint,new Vector2(uv.z,uv.y));
                vh.AddTriangle(0,1,2);vh.AddTriangle(0,2,3);
                if(accent!=null&&accent.texture==art.texture)
                {
                    Vector4 glow=DataUtility.GetOuterUV(accent);int start=vh.currentVertCount;
                    vh.AddVert(rect.center+new Vector2(-extent.x,-extent.y),tint,new Vector2(glow.x,glow.y));
                    vh.AddVert(rect.center+new Vector2(-extent.x,extent.y),tint,new Vector2(glow.x,glow.w));
                    vh.AddVert(rect.center+new Vector2(extent.x,extent.y),tint,new Vector2(glow.z,glow.w));
                    vh.AddVert(rect.center+new Vector2(extent.x,-extent.y),tint,new Vector2(glow.z,glow.y));
                    vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);
                }
                return;
            }
            const int segments=48;int center=vh.currentVertCount;Color32 nativeColor=color;
            vh.AddVert(rect.center,nativeColor,TouchRadialArtwork.IconUv(Vector2.zero,uv,aspect,Portrait));
            for(int n=0;n<=segments;++n)
            {
                float angle=n*Mathf.PI*2/segments;var unit=new Vector2(Mathf.Sin(angle),Mathf.Cos(angle));
                vh.AddVert(rect.center+unit*radius,nativeColor,TouchRadialArtwork.IconUv(unit,uv,aspect,Portrait));
                if(n>0)vh.AddTriangle(center,center+n,center+n+1);
            }
            // Preserve native atlas UVs at the silhouette while coverage fades
            // outside it. No neighbouring sprite pixels enter the round edge.
            int fringe=vh.currentVertCount;var transparent=nativeColor;transparent.a=0;
            for(int n=0;n<=segments;++n)
            {
                float angle=n*Mathf.PI*2/segments;var unit=new Vector2(Mathf.Sin(angle),Mathf.Cos(angle));
                var sample=TouchRadialArtwork.IconUv(unit,uv,aspect,Portrait);
                vh.AddVert(rect.center+unit*radius,nativeColor,sample);
                vh.AddVert(rect.center+unit*(radius+1.6f),transparent,sample);
                if(n==0)continue;int at=fringe+(n-1)*2;vh.AddTriangle(at,at+1,at+3);vh.AddTriangle(at,at+3,at+2);
            }
        }
    }
    internal sealed class TouchRadialGraphic : MaskableGraphic
    {
        int count,selected=-1,hovered=-1,ringSplit=-1;bool[] enabledSlots,characters,nativeSelected,abilities;float[] aspects;
        int dwellStep;
        internal bool[] DangerSlots;
        internal void Dwell(float progress)
        {
            // Quantized at most 40 updates per confirmation; idle never dirties
            // the canvas and the progress geometry belongs to the existing mesh.
            int step=Mathf.RoundToInt(Mathf.Clamp01(progress)*40);
            if(step==dwellStep)return;dwellStep=step;SetVerticesDirty();
        }
        internal void State(int value,int selection,int pointer,bool[] enabled,bool[] portraits,bool[] marked,bool[] plates,float[] ratios,int inner=-1)
        {
            if(ringSplit==inner&&count==value&&selected==selection&&hovered==pointer&&ReferenceEquals(enabledSlots,enabled)&&ReferenceEquals(characters,portraits)&&ReferenceEquals(nativeSelected,marked)&&ReferenceEquals(abilities,plates)&&ReferenceEquals(aspects,ratios))return;
            ringSplit=inner;count=value;selected=selection;hovered=pointer;enabledSlots=enabled;characters=portraits;nativeSelected=marked;abilities=plates;aspects=ratios;SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var origin=Vector2.zero;int rings=TouchRadialPolicy.Rings(count,ringSplit);
            // One continuous chassis connects every sector and the cog teeth.
            TouchRadialArtwork.Disc(vh,origin,480,TouchRadialPalette.Surface);
            TouchRadialArtwork.Arc(vh,origin,0,Mathf.PI*2,470,477,TouchRadialPalette.Brass,128);
            TouchRadialArtwork.Arc(vh,origin,0,Mathf.PI*2,478,480,TouchRadialPalette.Edge,128);
            for(int n=0;n<48;++n)
            {
                float a=n*Mathf.PI*2/48;
                TouchRadialArtwork.Arc(vh,origin,a-.018f,a+.018f,480,486,TouchRadialPalette.Brass,1);
            }
            for(int i=0;i<count;++i)
            {
                int ring=TouchRadialPolicy.RingFor(i,count,ringSplit),slots=TouchRadialPolicy.RingCount(count,ring,ringSplit);
                float band=(TouchRadialLayout.OuterRadius-TouchRadialLayout.InnerRadius)/Mathf.Max(1,rings);
                float inner=TouchRadialLayout.InnerRadius+ring*band,outer=inner+band;
                float angle=(float)TouchRadialPolicy.Angle(i,count,ringSplit),half=(float)TouchRadialPolicy.HalfAngle(i,count,ringSplit);
                bool active=enabledSlots==null||enabledSlots[i];bool portrait=characters!=null&&characters[i];
                bool native=nativeSelected!=null&&nativeSelected[i];
                Color focus=!active?TouchRadialPalette.Muted:i==hovered?TouchRadialPalette.Cyan:i==selected?TouchRadialPalette.Gold:native?TouchRadialPalette.Ink:ringSplit>0&&ring==1?TouchRadialPalette.Cyan:TouchRadialPalette.Brass;
                Color fill=!active?new Color(.055f,.055f,.055f,1):i==hovered?new Color(.012f,.08f,.10f,1):i==selected?new Color(.085f,.045f,.008f,1):TouchRadialPalette.Panel;
                if(DangerSlots!=null&&i<DangerSlots.Length&&DangerSlots[i])
                {fill=active?new Color(.24f,.012f,.015f,1):new Color(.07f,.035f,.035f,1);focus=active?new Color(1,.24f,.20f,1):TouchRadialPalette.Muted;}
                TouchRadialArtwork.Arc(vh,origin,angle-half+.002f,angle+half-.002f,inner+2,outer-2,fill,Mathf.Max(4,64/slots));
                var separator=new Vector2(Mathf.Sin(angle-half),Mathf.Cos(angle-half));
                TouchRadialArtwork.Line(vh,separator*(inner+8),separator*(outer-8),1.2f,TouchRadialPalette.Rule);
                if(i==selected||i==hovered)TouchRadialArtwork.Arc(vh,origin,angle-half+.025f,angle+half-.025f,outer-6,outer-3,focus,Mathf.Max(4,64/slots));
                float radius=TouchRadialLayout.Radius(i,count,ringSplit),size=TouchRadialArtwork.IconSize(i,count,portrait,ringSplit),medal=size*.5f;
                float trim=TouchRadialArtwork.FrameScale(count,ringSplit);int detail=Mathf.Clamp(Mathf.CeilToInt(size*.35f),8,48);
                var center=new Vector2(Mathf.Sin(angle),Mathf.Cos(angle))*radius;
                bool plaque=abilities!=null&&abilities[i];
                Vector2 extent=plaque?TouchRadialArtwork.ActionExtent(medal,aspects==null?1:aspects[i]):new Vector2(medal,medal);
                if(plaque)TouchRadialArtwork.Plaque(vh,center,extent,trim,focus);
                else
                {
                    TouchRadialArtwork.Disc(vh,center,medal+8*trim,TouchRadialPalette.Surface,detail);
                    TouchRadialArtwork.Arc(vh,center,0,Mathf.PI*2,medal+3*trim,medal+6*trim,focus,detail);
                    TouchRadialArtwork.Arc(vh,center,0,Mathf.PI*2,medal+7*trim,medal+8*trim,TouchRadialPalette.Edge,detail);
                }
                // A native selected character keeps its mark even when neither
                // cursor is over it. Disabled art uses a cached light-grey copy.
                if(native)TouchRadialArtwork.Arc(vh,center,Mathf.PI*.82f,Mathf.PI*1.18f,medal+10*trim,medal+13*trim,TouchRadialPalette.Ink,Mathf.Max(2,detail/4));
                if(!active)
                {
                    // A persistent barred badge remains unambiguous even
                    // before the asynchronous greyscale icon becomes ready.
                    var badge=center+new Vector2(extent.x*.70f,-extent.y*.70f);
                    TouchRadialArtwork.Disc(vh,badge,13*trim,TouchRadialPalette.Surface,16);
                    TouchRadialArtwork.Arc(vh,badge,0,Mathf.PI*2,10*trim,12*trim,TouchRadialPalette.Muted,16);
                    TouchRadialArtwork.Line(vh,badge+new Vector2(-8,-8)*trim,badge+new Vector2(8,8)*trim,2*trim,TouchRadialPalette.Muted);
                }
            }
            for(int ring=0;ring<=rings;++ring)
            {
                float r=TouchRadialLayout.InnerRadius+(TouchRadialLayout.OuterRadius-TouchRadialLayout.InnerRadius)*ring/Mathf.Max(1,rings);
                TouchRadialArtwork.Arc(vh,origin,0,Mathf.PI*2,r-1,r+1,TouchRadialPalette.Brass,96);
            }
            TouchRadialArtwork.Disc(vh,origin,151,TouchRadialPalette.Surface);
            TouchRadialArtwork.Arc(vh,origin,0,Mathf.PI*2,145,148,TouchRadialPalette.Edge,96);
            if(dwellStep>0)TouchRadialArtwork.Arc(vh,origin,0,Mathf.PI*2*dwellStep/40f,137,143,TouchRadialPalette.Cyan,Mathf.Max(2,dwellStep*2));
            // Small mechanical registration marks, outside the text field.
            for(int side=-1;side<=1;side+=2)
            {
                TouchRadialArtwork.Line(vh,new Vector2(side*135,-23),new Vector2(side*135,23),3,TouchRadialPalette.Brass);
                TouchRadialArtwork.Line(vh,new Vector2(-43,side*136),new Vector2(43,side*136),2,TouchRadialPalette.Brass);
            }
        }
    }
}
