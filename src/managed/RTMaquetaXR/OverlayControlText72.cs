using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    internal sealed class OverlayControlSymbol72 : MaskableGraphic
    {
        internal string Symbol;
        internal float Body;
        public override Texture mainTexture => ControlIconAtlas.Texture(ControlIconAtlasLayout.Keyboard(Symbol));
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); if (string.IsNullOrEmpty(Symbol)) return;
            var rect = rectTransform.rect;
            ControlIconDrawing.MeshBody(mesh, new Rect(rect.center.x-Body*.5f,rect.center.y-Body*.5f,Body,Body), Symbol, color);
        }
    }

    // Explicit text/icon layout for the options overlay. No oversized invisible
    // font glyphs: font metrics cannot swallow an entire row containing buttons.
    internal sealed class OverlayControlText72 : Text
    {
        sealed class Piece
        {
            internal string Value, Symbol;
            internal bool Newline, Space;
            internal RectTransform Rect;
            internal Text Label;
            internal OverlayControlSymbol72 Icon;
            internal float Width, Height, X;
            internal int Line;
        }
        internal int IconBodySize = 40;
        internal float ContentHeight;
        string source = "", prepared = "";
        readonly List<ControlIconRun> runs = new List<ControlIconRun>();
        readonly List<Piece> pieces = new List<Piece>();
        readonly List<float> lineHeights = new List<float>(), lineWidths = new List<float>();
        static readonly Regex words = new Regex(@"\r?\n|[^\s]+|[^\S\r\n]+", RegexOptions.CultureInvariant);
        bool dirty = true, composed;
        Font oldFont;
        Color oldColor;
        FontStyle oldStyle;
        TextAnchor oldAlignment;
        int oldSize, oldBody;
        float oldWidth, oldHeight;
        Material oldMaterial;
        public override string text
        {
            get => source;
            set
            {
                value = value ?? ""; if (source == value) return;
                source = value; prepared = ControlIconMarkup.Prepare(value, runs, 0, false);
                composed = runs.Count > 0; base.text = composed ? "" : value;
                RebuildPieces(); dirty = true; SetVerticesDirty(); SetLayoutDirty();
            }
        }
        // UnityEngine.UI.Text.OnPopulateMesh reads the virtual `text` property.
        // This component deliberately exposes the authored source there, while
        // its approved control glyphs and labels are rendered by child graphics.
        // Calling the base mesh builder in composed mode would therefore draw
        // the complete source a second time underneath those children. It was
        // visible only in the in-game uGUI surface; the flat overlay renderer
        // has its own layout path and never created this duplicate.
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            if (composed) { mesh.Clear(); return; }
            base.OnPopulateMesh(mesh);
        }
        protected override void OnRectTransformDimensionsChange() { base.OnRectTransformDimensionsChange(); dirty = true; }
        void LateUpdate() { RefreshLayout(); }
        internal void RefreshLayout()
        {
            if (!composed || font == null || !isActiveAndEnabled) return;
            float width = rectTransform.rect.width, height = rectTransform.rect.height;
            if (!dirty && oldFont == font && oldColor == color && oldStyle == fontStyle && oldSize == fontSize &&
                oldBody == IconBodySize && oldWidth == width && oldHeight == height && oldMaterial == material && oldAlignment == alignment) return;
            dirty = false; oldFont = font; oldColor = color; oldStyle = fontStyle; oldSize = fontSize;
            oldWidth = width; oldHeight = height; oldBody = IconBodySize; oldMaterial = material; oldAlignment = alignment;
            var settings = GetGenerationSettings(new Vector2(100000, 100000));
            settings.richText = false; settings.resizeTextForBestFit = false;
            settings.horizontalOverflow = HorizontalWrapMode.Overflow; settings.verticalOverflow = VerticalWrapMode.Overflow;
            float textHeight = Mathf.Max(fontSize * 1.2f, cachedTextGeneratorForLayout.GetPreferredHeight("Ag", settings) / pixelsPerUnit);
            int line = 0; float x = 0; lineHeights.Clear(); lineWidths.Clear(); lineHeights.Add(textHeight); lineWidths.Add(0);
            foreach (var p in pieces)
            {
                if (p.Newline) { line++; x=0; lineHeights.Add(textHeight);lineWidths.Add(0);p.Line=line;continue; }
                if (p.Icon != null)
                {
                    Rect bounds=ControlIconAtlas.BodyFit(new Rect(0,0,IconBodySize,IconBodySize),p.Symbol);
                    p.Width=bounds.width+fontSize*.22f;p.Height=bounds.height+fontSize*.12f;
                }
                else
                { p.Width=cachedTextGeneratorForLayout.GetPreferredWidth(p.Value,settings)/pixelsPerUnit;p.Height=textHeight; }
                if (x>0 && !p.Space && x+p.Width>width)
                { line++;x=0;lineHeights.Add(textHeight);lineWidths.Add(0); }
                if(p.Space && x==0)p.Width=0;
                p.X=x;p.Line=line;x+=p.Width;lineWidths[line]=x;lineHeights[line]=Mathf.Max(lineHeights[line],p.Height);
            }
            ContentHeight=0;foreach(float h in lineHeights)ContentHeight+=h;
            float vertical=alignment==TextAnchor.MiddleLeft||alignment==TextAnchor.MiddleCenter||alignment==TextAnchor.MiddleRight ? Mathf.Max(0,(height-ContentHeight)*.5f) :
                alignment==TextAnchor.LowerLeft||alignment==TextAnchor.LowerCenter||alignment==TextAnchor.LowerRight ? Mathf.Max(0,height-ContentHeight):0;
            foreach (var p in pieces)
            {
                if(p.Rect==null)continue;
                float y=vertical;for(int i=0;i<p.Line;i++)y+=lineHeights[i];
                float left=alignment==TextAnchor.UpperCenter||alignment==TextAnchor.MiddleCenter||alignment==TextAnchor.LowerCenter ? Mathf.Max(0,(width-lineWidths[p.Line])*.5f):
                    alignment==TextAnchor.UpperRight||alignment==TextAnchor.MiddleRight||alignment==TextAnchor.LowerRight ? Mathf.Max(0,width-lineWidths[p.Line]):0;
                p.Rect.anchorMin=p.Rect.anchorMax=p.Rect.pivot=new Vector2(0,1);
                p.Rect.anchoredPosition=new Vector2(left+p.X,-y-(lineHeights[p.Line]-p.Height)*.5f);
                p.Rect.sizeDelta=new Vector2(Mathf.Max(1,p.Width),p.Height);p.Rect.gameObject.layer=gameObject.layer;
                if(p.Label!=null)
                {
                    p.Label.font=font;p.Label.fontSize=fontSize;p.Label.fontStyle=fontStyle;p.Label.color=color;p.Label.material=material;
                    p.Label.text=p.Value;p.Label.verticalOverflow=VerticalWrapMode.Overflow;p.Label.horizontalOverflow=HorizontalWrapMode.Overflow;
                }
                else if(p.Icon!=null)
                {p.Icon.Symbol=p.Symbol;p.Icon.Body=IconBodySize;p.Icon.color=color;p.Icon.material=material;p.Icon.SetVerticesDirty();}
            }
        }
        void RebuildPieces()
        {
            foreach(var p in pieces)if(p.Rect!=null){p.Rect.gameObject.SetActive(false);Destroy(p.Rect.gameObject);}
            pieces.Clear();if(!composed)return;
            int start=0;
            foreach(var run in runs)
            {
                AddWords(prepared.Substring(start,run.Index-start));
                AddPiece(new Piece{Symbol=run.Symbol});start=run.Index+4;
            }
            AddWords(prepared.Substring(start));
        }
        void AddWords(string text)
        {
            foreach(Match match in words.Matches(text))
                AddPiece(new Piece{Value=match.Value,Newline=match.Value.Contains("\n"),Space=string.IsNullOrWhiteSpace(match.Value)&&!match.Value.Contains("\n")});
        }
        void AddPiece(Piece p)
        {
            pieces.Add(p);if(p.Newline||p.Space)return;
            bool icon=p.Symbol!=null;
            var obj=new GameObject(icon?"Approved control":"Control text",typeof(RectTransform),typeof(CanvasRenderer),icon?typeof(OverlayControlSymbol72):typeof(Text));
            obj.layer=gameObject.layer;p.Rect=(RectTransform)obj.transform;p.Rect.SetParent(transform,false);
            if(icon){p.Icon=obj.GetComponent<OverlayControlSymbol72>();p.Icon.Symbol=p.Symbol;p.Icon.raycastTarget=false;}
            else {p.Label=obj.GetComponent<Text>();p.Label.raycastTarget=false;p.Label.supportRichText=false;}
        }
    }
}
