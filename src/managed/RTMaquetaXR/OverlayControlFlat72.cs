using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace RTMaquetaXR
{
    // The flat/menu route uses the same explicit boxes as the stereo overlay:
    // never ask font layout to reserve a giant invisible glyph for an icon.
    internal static class OverlayControlFlat72
    {
        sealed class Piece
        {
            internal string Symbol, Word;
            internal bool Newline, Space;
            internal Rect Bounds;
            internal int Line;
            internal GUIContent Content;
        }
        sealed class Flow
        {
            internal readonly List<Piece> Pieces=new List<Piece>();
            internal readonly List<float> Heights=new List<float>(),Widths=new List<float>();
            internal float Height, Width;
            internal Font Font;
            internal FontStyle Style;
            internal int Size,Body;
            internal bool Icons;
            internal Flow(string source)
            {
                var runs=new List<ControlIconRun>();string text=ControlIconMarkup.Prepare(source,runs,0,false);int start=0;
                Icons=runs.Count>0;
                foreach(var run in runs){Words(text.Substring(start,run.Index-start));Pieces.Add(new Piece{Symbol=run.Symbol});start=run.Index+4;}
                Words(text.Substring(start));
            }
            void Words(string text)
            {
                foreach(Match m in words.Matches(text))Pieces.Add(new Piece{Word=m.Value,Content=new GUIContent(m.Value),
                    Newline=m.Value.Contains("\n"),Space=string.IsNullOrWhiteSpace(m.Value)&&!m.Value.Contains("\n")});
            }
            internal void Layout(GUIStyle style,float width,int body)
            {
                if(Font==style.font&&Size==style.fontSize&&Style==style.fontStyle&&Width==width&&Body==body&&Heights.Count>0)return;
                Font=style.font;Size=style.fontSize;Style=style.fontStyle;Width=width;Body=body;
                float textHeight=Mathf.Max(Size*1.2f,style.CalcSize(new GUIContent("Ag")).y),x=0;int line=0;
                Heights.Clear();Widths.Clear();Heights.Add(textHeight);Widths.Add(0);
                foreach(var p in Pieces)
                {
                    if(p.Newline){line++;x=0;Heights.Add(textHeight);Widths.Add(0);continue;}
                    Vector2 size;
                    if(p.Symbol!=null){Rect icon=ControlIconAtlas.BodyFit(new Rect(0,0,body,body),p.Symbol);size=new Vector2(icon.width+Size*.22f,icon.height+Size*.12f);}
                    else size=new Vector2(style.CalcSize(p.Content).x,textHeight);
                    if(x>0&&!p.Space&&x+size.x>width){line++;x=0;Heights.Add(textHeight);Widths.Add(0);}
                    if(p.Space&&x==0)size.x=0;
                    p.Line=line;p.Bounds=new Rect(x,0,size.x,size.y);x+=size.x;Widths[line]=x;Heights[line]=Mathf.Max(Heights[line],size.y);
                }
                Height=0;foreach(float h in Heights)Height+=h;
            }
        }
        static readonly Regex words=new Regex(@"\r?\n|[^\s]+|[^\S\r\n]+",RegexOptions.CultureInvariant);
        static readonly Dictionary<string,Flow> flows=new Dictionary<string,Flow>();
        static Flow Get(string source)
        {
            source=source??"";
            if(!flows.TryGetValue(source,out var flow))
            {if(flows.Count>=96)flows.Clear();flows.Add(source,flow=new Flow(source));}
            return flow;
        }
        internal static float Measure(string source,GUIStyle style,float width,int body)
        {var flow=Get(source);flow.Layout(style,width,body);return flow.Height;}
        internal static void Draw(Rect rect,string source,GUIStyle style,int body)
        {
            var flow=Get(source);
            if(!flow.Icons){GUI.Label(rect,source??"",style);return;}
            bool wrap=style.wordWrap,rich=style.richText;var alignment=style.alignment;
            try
            {
                style.wordWrap=false;style.richText=false;flow.Layout(style,rect.width,body);
                int horizontal=(int)alignment%3,vertical=(int)alignment/3;
                float top=vertical==1?Mathf.Max(0,(rect.height-flow.Height)*.5f):vertical==2?Mathf.Max(0,rect.height-flow.Height):0;
                style.alignment=TextAnchor.MiddleLeft;
                foreach(var p in flow.Pieces)
                {
                    if(p.Newline||p.Space)continue;float y=top;for(int i=0;i<p.Line;i++)y+=flow.Heights[i];
                    float left=horizontal==1?Mathf.Max(0,(rect.width-flow.Widths[p.Line])*.5f):horizontal==2?Mathf.Max(0,rect.width-flow.Widths[p.Line]):0;
                    Rect box=new Rect(rect.x+left+p.Bounds.x,rect.y+y+(flow.Heights[p.Line]-p.Bounds.height)*.5f,p.Bounds.width,p.Bounds.height);
                    if(p.Symbol==null)GUI.Label(box,p.Content,style);
                    else ControlIconDrawing.FlatBody(new Rect(box.center.x-body*.5f,box.center.y-body*.5f,body,body),p.Symbol,style.normal.textColor);
                }
            }
            finally{style.wordWrap=wrap;style.richText=rich;style.alignment=alignment;}
        }
    }
}
