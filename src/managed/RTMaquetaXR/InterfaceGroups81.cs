using System;
using System.Globalization;
using UnityEngine;
namespace RTMaquetaXR
{
    public sealed class InterfaceGroupSettings81
    {
        public float X, Y, Distance = ApprovedUserDefaults.Distance, Panel = ApprovedUserDefaults.Width, Content = 1, Aspect;
        internal static InterfaceGroupSettings81 LargeTutorialDefault() =>
            new InterfaceGroupSettings81 { X=0, Y=-.025f, Distance=.55f, Panel=.475f, Content=.85f, Aspect=0 };
        internal InterfaceGroupSettings81 Copy() => (InterfaceGroupSettings81)MemberwiseClone();
        internal void Normalize()
        {
            X = Bound(X, -.65f, .65f, 0); Y = Bound(Y, -.65f, .65f, 0);
            Distance = Bound(Distance, .5f, 3, ApprovedUserDefaults.Distance); Panel = Bound(Panel, .45f, 1, ApprovedUserDefaults.Width);
            Content = Bound(Content, .65f, 1.5f, 1); Aspect = Aspect == 0 ? 0 : Bound(Aspect, .8f, 2.4f, 0);
        }
        internal static float Bound(float v,float min,float max,float fallback) =>
            float.IsNaN(v)||float.IsInfinity(v) ? fallback : Math.Max(min,Math.Min(max,v));
        internal string Encode() => string.Join(",",new[]{X.ToString("R",CultureInfo.InvariantCulture),Y.ToString("R",CultureInfo.InvariantCulture),
            Distance.ToString("R",CultureInfo.InvariantCulture),Panel.ToString("R",CultureInfo.InvariantCulture),Content.ToString("R",CultureInfo.InvariantCulture),Aspect.ToString("R",CultureInfo.InvariantCulture)});
        internal static InterfaceGroupSettings81 Decode(string value,InterfaceGroupSettings81 previous)
        {
            var p=value.Split(',');if(p.Length!=6)return previous;
            var n=new float[6];for(int i=0;i<6;i++)if(!float.TryParse(p[i],NumberStyles.Float,CultureInfo.InvariantCulture,out n[i]))return previous;
            var result=new InterfaceGroupSettings81{X=n[0],Y=n[1],Distance=n[2],Panel=n[3],Content=n[4],Aspect=n[5]};result.Normalize();return result;
        }
    }
    public static partial class Main
    {
        static OverlayOption NativeGroupOptions81(string title, Func<InterfaceGroupSettings81> value) =>
            ImageGroup(title,"Adjust this group independently. Position and distance move the whole panel; content size and aspect change its internal layout.",
                ImageValue("Horizontal position","Move this group left or right.",()=>Percent81(value().X),d=>{value().X=Mathf.Clamp(value().X+d*.025f,-.65f,.65f);MarkSettingsDirty();}),
                ImageValue("Vertical position","Move this group up or down.",()=>Percent81(value().Y),d=>{value().Y=Mathf.Clamp(value().Y+d*.025f,-.65f,.65f);MarkSettingsDirty();}),
                ImageValue("Panel distance","Move the panel nearer or farther without changing its font size.",()=>value().Distance.ToString("0.00",ModLocalization.Culture)+" m",d=>{value().Distance=Mathf.Clamp(value().Distance+d*.05f,.5f,3);MarkSettingsDirty();}),
                ImageValue("Whole panel size","Scale this group's complete panel uniformly.",()=>Percent81(value().Panel),d=>{value().Panel=Mathf.Clamp(value().Panel+d*.025f,.45f,1);MarkSettingsDirty();}),
                ImageValue("Content size","Adjust native text size inside this group's panel.",()=>Percent81(value().Content),d=>{value().Content=Mathf.Clamp(value().Content+d*.05f,.65f,1.5f);MarkSettingsDirty();}),
                ImageValue("Panel aspect ratio","Change available layout proportions. Original retains the native proportions.",()=>value().Aspect==0?ModLocalization.Text("Original"):value().Aspect.ToString("0.0",ModLocalization.Culture),d=>{
                    float a=value().Aspect; a=a==0?(d<0?2.4f:.8f):Mathf.Round((a+d*.1f)*10)/10;value().Aspect=a<.79f||a>2.41f?0:a;MarkSettingsDirty();}));
        static string Percent81(float v)=>(v*100).ToString("0.#",ModLocalization.Culture)+"%";
        static void ResetInterfaceGroups81()
        {
            _cfg.dialogLayout81=new InterfaceGroupSettings81();_cfg.largeTutorialLayout81=InterfaceGroupSettings81.LargeTutorialDefault();
            _cfg.tipLayout81=new InterfaceGroupSettings81();_cfg.modMenuDistance81=.75f;
        }
    }
}
