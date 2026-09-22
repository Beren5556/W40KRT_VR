using System;
using System.IO;
using System.Reflection;
using UnityEngine;
namespace RTMaquetaXR
{
    internal static class ControlIconAtlas
    {
        static Texture2D touch,keyboard;
        internal static Texture2D Texture(bool keys)
        {
            if(keys)return keyboard??(keyboard=Load("RTMaquetaXR.ControlKeys.png"));
            return touch??(touch=Load("RTMaquetaXR.ControlIcons.png"));
        }
        internal static Texture2D Load(string resource)
        {
            using(var stream=typeof(ControlIconAtlas).Assembly.GetManifestResourceStream(resource))
            {
                if(stream==null)throw new InvalidOperationException("Missing embedded control artwork: "+resource);
                using(var memory=new MemoryStream())
                {
                    stream.CopyTo(memory);
                    var texture=new Texture2D(2,2,TextureFormat.RGBA32,true,false);
                    if(!ImageConversion.LoadImage(texture,memory.ToArray(),false))throw new InvalidOperationException("Invalid control artwork: "+resource);
                    texture.ignoreMipmapLimit=true; // UI art must not inherit the scene texture-quality reduction.
                    texture.name=resource;texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Trilinear;
                    texture.anisoLevel=2;texture.Apply(true,true);UnityEngine.Object.DontDestroyOnLoad(texture);return texture;
                }
            }
        }
        internal static Rect Uv(string symbol)
        {
            ControlIconAtlasLayout.Bounds(symbol,out var x,out var y,out var w,out var h);return new Rect(x,y,w,h);
        }
        internal static Rect BodyFit(Rect box,string symbol)
        {
            Rect uv=Uv(symbol);bool keys=ControlIconAtlasLayout.Keyboard(symbol);
            float referenceBody=keys?216f:96f;
            float width=uv.width*(keys?1254f:1675f)/referenceBody*box.width;
            float height=uv.height*(keys?1254f:940f)/referenceBody*box.height;
            return new Rect(box.center.x-width*.5f,box.center.y-height*.5f,width,height);
        }
        internal static Rect Fit(Rect box,string symbol)
        {
            Rect uv=Uv(symbol);
            float aspect=ControlIconAtlasLayout.Keyboard(symbol)?uv.width/uv.height:uv.width*1675f/(uv.height*940f);
            float width=Mathf.Min(box.width,box.height*aspect),height=width/aspect;
            return new Rect(box.center.x-width*.5f,box.center.y-height*.5f,width,height);
        }
    }
}
