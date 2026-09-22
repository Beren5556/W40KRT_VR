using System;
using System.Text.RegularExpressions;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static string ProximityDescription75(object vm,object part)
        {
            object settings=_touchProximityContracts73.Settings(part);
            foreach(string member in new[]{"Description","DescriptionText","TooltipDescription"})
            {
                object value=ReadMember73(settings,member)??ReadMember73(part,member);
                string text=TouchNativeString73(value);
                if(string.IsNullOrWhiteSpace(text)&&value!=null&&
                    _proximityName74.GetParameters()[0].ParameterType.IsInstanceOfType(value))
                    text=_proximityName74.Invoke(vm,new[]{value,(object)string.Empty}) as string;
                if(!string.IsNullOrWhiteSpace(text))return BriefProximityText75(text);
            }
            switch(_touchProximityContracts73.UiType(part))
            {
                case 2:return ModLocalization.Text("Travel to this destination.");
                case 3:return ModLocalization.Text("Examine this point of interest.");
                default:return ModLocalization.Text("Use this nearby interaction.");
            }
        }
        static string BriefProximityText75(string value)
        {
            string text=Regex.Replace(Regex.Replace(value??"","<[^>]*>",""),@"\s+"," ").Trim();
            if(text.Length<=130)return text;
            int end=127;while(end>0&&char.IsHighSurrogate(text[end-1]))--end;
            int space=text.LastIndexOf(' ',end-1,end);if(space>=95)end=space;
            return text.Substring(0,end).TrimEnd()+"…";
        }
    }
}
