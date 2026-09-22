using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    // Mod-owned text only. Native names, tooltips and dialogue never pass through
    // this component. Layout uses non-breaking spaces at the actual font size;
    // companion image canvases render the approved artwork at the actual UI size.
    internal sealed class ControlIconText : Text
    {
        string source = "";
        internal int IconBodySize;
        int preparedBodySize;
        int CurrentBodySize => ControlIconSizing65.Body(fontSize, IconBodySize);
        readonly List<ControlIconRun> runs = new List<ControlIconRun>();
        ControlIconLayer icons, keys;
        public override string text
        {
            get => base.text;
            set
            {
                value = value ?? ""; if (source == value) return;
                source = value; PrepareArtwork();
                if (runs.Count == 0) { if(icons!=null)icons.Clear(); if(keys!=null)keys.Clear(); }
            }
        }
        void PrepareArtwork()
        {
            preparedBodySize=CurrentBodySize;
            string prepared=ControlIconMarkup.Prepare(source,runs,preparedBodySize,true);
            if(runs.Count>0){supportRichText=true;resizeTextForBestFit=false;}
            base.text=prepared;
            // Different controls can produce identical placeholder text (A/B,
            // LT/RT...). Text's setter otherwise skips rebuilding that change.
            SetVerticesDirty();
        }
        void LateUpdate()
        {
            // Fixed-size sprite slots are independent of Text's best-fit pass.
            // Reformat only on an actual size change, never per eye or frame.
            if(preparedBodySize!=CurrentBodySize || (runs.Count>0&&(!supportRichText||resizeTextForBestFit)))PrepareArtwork();
        }
        protected override void Awake()
        {
            base.Awake();
            icons=CreateLayer(false);keys=CreateLayer(true);
        }
        ControlIconLayer CreateLayer(bool keyboard)
        {
            var child=new GameObject(keyboard?"Keyboard control artwork":"Approved Touch control artwork",typeof(RectTransform),typeof(CanvasRenderer),typeof(ControlIconLayer));
            child.layer=gameObject.layer;var rect=(RectTransform)child.transform;rect.SetParent(transform,false);
            rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;rect.pivot=rectTransform.pivot;
            var layer=child.GetComponent<ControlIconLayer>();layer.raycastTarget=false;layer.Owner=this;layer.Keys=keyboard;layer.enabled=false;return layer;
        }
        protected override void OnEnable() { base.OnEnable(); if(icons!=null && icons.HasLayout)icons.QueueRefresh();if(keys!=null && keys.HasLayout)keys.QueueRefresh(); }
        protected override void OnDisable() { if(icons!=null)icons.enabled=false;if(keys!=null)keys.enabled=false;base.OnDisable(); }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            base.OnPopulateMesh(mesh); if (icons == null) return;
            icons.Placements.Clear(); keys.Placements.Clear();
            // Read the ACTUAL generated text quads after Unity's alignment,
            // wrapping, rich-text parsing and pixel adjustment. Character cursor
            // arrays do not share rich-tag indices on the shipped Unity runtime.
            var vertex = new UIVertex();
            for(int i=0;i+3<mesh.currentVertCount;i+=4)
            {
                mesh.PopulateUIVertex(ref vertex,i);
                var marker=vertex.color;
                if(marker.r!=1 || marker.b!=253 || marker.g>=runs.Count)continue;
                var run=runs[marker.g];
                float minX=float.MaxValue,minY=float.MaxValue,maxX=float.MinValue,maxY=float.MinValue;
                for(int j=0;j<4;j++)
                {
                    mesh.PopulateUIVertex(ref vertex,i+j);
                    minX=Mathf.Min(minX,vertex.position.x);maxX=Mathf.Max(maxX,vertex.position.x);
                    minY=Mathf.Min(minY,vertex.position.y);maxY=Mathf.Max(maxY,vertex.position.y);
                    vertex.color=new Color32(0,0,0,0);mesh.SetUIVertex(vertex,i+j);
                }
                float body=run.BodySize;
                var layer=ControlIconAtlasLayout.Keyboard(run.Symbol)?keys:icons;
                layer.Placements.Add(new ControlIconPlacement{Symbol=run.Symbol,
                    Bounds=new Rect((minX+maxX-body)*.5f,(minY+maxY-body)*.5f,body,body)});
            }
            icons.Ink = keys.Ink = color;
            // Factories set these AFTER Awake: the companion must inherit the
            // final canvas layer, text pivot and the game's supported UI shader.
            // Apply outside the current graphic rebuild, just like geometry.
            QueueLayout(icons);QueueLayout(keys);
        }
        static Rect PlaceArtwork(Vector2 line,float advance,float body)
        { return new Rect(line.x+(advance-body)*.5f,line.y-(ControlIconSizing65.Slot((int)body)+body)*.5f,body,body); }
        void QueueLayout(ControlIconLayer layer)
        {
            layer.SourceLayer=gameObject.layer;layer.SourcePivot=rectTransform.pivot;
            layer.SourceMaterial=material;layer.HasLayout=true;layer.QueueRefresh();
        }
    }
    internal struct ControlIconPlacement { internal string Symbol; internal Rect Bounds; }
    internal sealed class ControlIconLayer : MaskableGraphic
    {
        static readonly List<ControlIconLayer> pending = new List<ControlIconLayer>();
        bool queued;
        internal ControlIconText Owner;
        internal bool Keys;
        public override Texture mainTexture => ControlIconAtlas.Texture(Keys);
        internal bool HasLayout;
        internal int SourceLayer;
        internal Vector2 SourcePivot;
        internal Material SourceMaterial;
        internal Color Ink = Color.white;
        internal readonly List<ControlIconPlacement> Placements = new List<ControlIconPlacement>();
        static ControlIconLayer() { Canvas.preWillRenderCanvases += Flush; }
        internal void QueueRefresh() { if(!queued){queued=true;pending.Add(this);} }
        // Text's mesh callback runs inside Unity's graphic rebuild. Registering
        // another Graphic there is rejected, and rebuilding synchronously would
        // overwrite Unity's shared VertexHelper. Queue before the next rebuild.
        static void Flush()
        {
            if(pending.Count==0)return;
            foreach(var layer in pending)if(layer!=null){layer.queued=false;if(layer.HasLayout && layer.Owner!=null && layer.Owner.isActiveAndEnabled){layer.ApplySourceLayout();layer.enabled=layer.Placements.Count>0;layer.SetVerticesDirty();}}
            pending.Clear();
        }
        void ApplySourceLayout()
        {
            if(gameObject.layer!=SourceLayer)gameObject.layer=SourceLayer;
            if(rectTransform.pivot!=SourcePivot)rectTransform.pivot=SourcePivot;
            if(material!=SourceMaterial)material=SourceMaterial;
        }
        internal void Clear() { Placements.Clear(); QueueRefresh(); }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); foreach(var item in Placements) ControlIconDrawing.MeshBody(mesh,item.Bounds,item.Symbol,Ink);
        }
    }
    internal static class ControlIconDrawing
    {
        sealed class FlatLabel
        {
            internal readonly List<ControlIconRun> Runs = new List<ControlIconRun>();
            internal readonly GUIContent Content;
            internal FlatLabel(string source,int bodySize) { Content=new GUIContent(ControlIconMarkup.Prepare(source,Runs,bodySize)); }
        }
        static readonly Dictionary<string,FlatLabel> flatLabels=new Dictionary<string,FlatLabel>();
        // Full-colour approved artwork. The text tint controls opacity only:
        // cyan/gold label colours must not recolour ivory or green glyph details.
        internal static void Mesh(VertexHelper mesh, Rect bounds, string symbol, Color ink)
        {
            Emit(mesh,ControlIconAtlas.Fit(bounds,symbol),symbol,ink);
        }
        internal static void MeshBody(VertexHelper mesh,Rect bounds,string symbol,Color ink) => Emit(mesh,ControlIconAtlas.BodyFit(bounds,symbol),symbol,ink);
        static void Emit(VertexHelper mesh,Rect rect,string symbol,Color ink)
        {
            Rect uv=ControlIconAtlas.Uv(symbol);
            var color=new Color(1,1,1,ink.a);int first=mesh.currentVertCount;
            mesh.AddVert(new Vector2(rect.xMin,rect.yMin),color,new Vector2(uv.xMin,uv.yMin));
            mesh.AddVert(new Vector2(rect.xMin,rect.yMax),color,new Vector2(uv.xMin,uv.yMax));
            mesh.AddVert(new Vector2(rect.xMax,rect.yMax),color,new Vector2(uv.xMax,uv.yMax));
            mesh.AddVert(new Vector2(rect.xMax,rect.yMin),color,new Vector2(uv.xMax,uv.yMin));
            mesh.AddTriangle(first,first+1,first+2);mesh.AddTriangle(first,first+2,first+3);
        }
        internal static void Flat(Rect rect,string symbol,Color ink)
        {
            var color=GUI.color;
            try { GUI.color=new Color(1,1,1,ink.a);GUI.DrawTextureWithTexCoords(ControlIconAtlas.Fit(rect,symbol),
                ControlIconAtlas.Texture(ControlIconAtlasLayout.Keyboard(symbol)),ControlIconAtlas.Uv(symbol),true); }
            finally {GUI.color=color;}
        }
        internal static void FlatBody(Rect rect,string symbol,Color ink)
        {
            var color=GUI.color;
            try {GUI.color=new Color(1,1,1,ink.a);GUI.DrawTextureWithTexCoords(ControlIconAtlas.BodyFit(rect,symbol),ControlIconAtlas.Texture(ControlIconAtlasLayout.Keyboard(symbol)),ControlIconAtlas.Uv(symbol),true);}
            finally {GUI.color=color;}
        }
        static FlatLabel PreparedFlatLabel(string source,int bodySize)
        {
            source=source??"";string cacheKey=bodySize+"|"+source;
            if(!flatLabels.TryGetValue(cacheKey,out var label))
            {
                if(flatLabels.Count>=256)flatLabels.Clear();
                flatLabels.Add(cacheKey,label=new FlatLabel(source,bodySize));
            }
            return label;
        }
        // Same prepared content is measured and drawn in every flat path.
        internal static void Label(Rect rect,string source,GUIStyle style)
        {
            var label=PreparedFlatLabel(source,ControlIconSizing65.Body(style.fontSize,0));
            bool rich=style.richText;
            try
            {
                style.richText=true;GUI.Label(rect,label.Content,style);
                foreach(var run in label.Runs)
                {
                    Vector2 p=style.GetCursorPixelPosition(rect,label.Content,run.Index);
                    Vector2 q=style.GetCursorPixelPosition(rect,label.Content,run.Index+4);
                    float size=run.BodySize;
                    FlatBody(new Rect(p.x+(q.x-p.x-size)*.5f,p.y+(ControlIconSizing65.Slot((int)size)-size)*.5f,size,size),run.Symbol,style.normal.textColor);
                }
            }
            finally {style.richText=rich;}
        }
        internal static void LayoutLabel(string source)
        {
            var style=GUI.skin.label;
            var label=PreparedFlatLabel(source,ControlIconSizing65.Body(style.fontSize,0));
            bool rich=style.richText;
            try
            {
                style.richText=true;
                var rect=GUILayoutUtility.GetRect(label.Content,style);
                Label(rect,source,style);
            }
            finally {style.richText=rich;}
        }
    }
}
