using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Image[] _radialCrewPortraits=new Image[0];
        static Text[] _radialCooldownLabels=new Text[0];
        static void UpdateSpaceRadialStatus(int index, TouchRadialEntry entry)
        {
            var c=_touchSpaceRadialContracts;
            if(c==null || entry.Space==null || entry.Model==null)return;
            // Native VM properties already incorporate cooldown, ammunition
            // and slot variant art. Reading does not recreate any action VM.
            entry.Icon=c.SlotIcon(entry.Model) as Sprite;
            bool cooling=c.SlotCooling(entry.Model);
            string cooldown=cooling?c.SlotCooldown(entry.Model):"";
            object post=entry.Space.Post;
            if(post!=null && c.PostBlocked(post))cooldown=c.PostDuration(post);
            if(index<_radialCooldownLabels.Length && _radialCooldownLabels[index]!=null)
                SetLiveText(_radialCooldownLabels[index],cooldown);
            if(index<_radialCrewPortraits.Length && _radialCrewPortraits[index]!=null)
            {
                Sprite portrait=post==null?null:c.PostPortrait(post) as Sprite;
                var target=_radialCrewPortraits[index];
                if(target.sprite!=portrait)target.sprite=portrait;
                target.enabled=portrait!=null;
            }
        }
        static void CreateSpaceRadialStatus(int index, TouchRadialEntry entry, RectTransform icon, float size)
        {
            if(entry.Space==null)return;
            // Native crew portraits are a distinct, readable part of their
            // assigned post ability. They must not collapse into a tiny dot
            // beneath the icon or inherit an unrelated world render layer.
            float badge=Mathf.Clamp(size*.52f,28,56);
            var root=new GameObject("Native crew portrait",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            root.layer=icon.gameObject.layer;
            var rect=(RectTransform)root.transform;rect.SetParent(icon,false);rect.sizeDelta=new Vector2(badge,badge);
            rect.anchoredPosition=new Vector2(-size*.3f,-size*.42f);
            var portrait=root.GetComponent<Image>();portrait.raycastTarget=false;portrait.preserveAspect=true;portrait.material=_touchRadialMaterial;
            _radialCrewPortraits[index]=portrait;
            var label=RadialText("Native cooldown",icon,22,new Vector2(size*.26f,-size*.5f-badge*.2f),new Vector2(size*.72f,32));
            label.resizeTextForBestFit=true;label.resizeTextMinSize=15;label.resizeTextMaxSize=22;SharpenRadialText(label);
            _radialCooldownLabels[index]=label;UpdateSpaceRadialStatus(index,entry);
        }
    }
}
