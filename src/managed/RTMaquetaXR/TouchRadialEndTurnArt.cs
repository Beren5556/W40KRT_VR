using UnityEngine;
using UnityEngine.UI;
namespace RTMaquetaXR
{
    public static partial class Main
    {
        static void ReadNativeEndTurnArt(Component button,out Sprite skull,out Sprite greenEye)
        {
            skull=greenEye=null;
            // Both SurfacePCView and SpacePCView use these original two layers.
            // The parent Image is merely EndTurn_Background, not the skull.
            // Include inactive state images without enabling native controls.
            foreach(var image in button.GetComponentsInChildren<Image>(true))
            {
                if(image.sprite==null)continue;
                if(image.sprite.name=="EndTurn_Normal")skull=image.sprite;
                else if(image.sprite.name=="EndTurn_GreenColor")greenEye=image.sprite;
            }
            if(skull==null||greenEye!=null&&greenEye.texture!=skull.texture)greenEye=null;
        }
    }
}
