using UnityEngine;
using UnityEngine.UI;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static void OutlineRadialText(Text text)
        {
            // A narrow glyph outline retains contrast on bright scenery without
            // drawing the rectangular black strips removed from wheel help.
            var outline=text.gameObject.AddComponent<Outline>();
            outline.effectColor=new Color(0,0,0,.82f);
            outline.effectDistance=new Vector2(1.6f,-1.6f);
        }
        static void SharpenRadialText(Text text)
        {
            // Larger native-font glyphs are sampled into the same physical
            // footprint. Neither output resolution nor scene DLSS is changed.
            text.fontSize*=2;
            if(text is ControlIconText controls && controls.IconBodySize>0)controls.IconBodySize*=2;
            if(text is OverlayControlText72 overlay && overlay.IconBodySize>0)overlay.IconBodySize*=2;
            if(text.resizeTextForBestFit){text.resizeTextMinSize*=2;text.resizeTextMaxSize*=2;}
            text.rectTransform.sizeDelta*=2;
            text.rectTransform.localScale=Vector3.one*.5f;
        }
    }
}
