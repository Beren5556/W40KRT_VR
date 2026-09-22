using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Sprites;
namespace RTMaquetaXR
{
    // Original art remains original. Build only a desaturated presentation
    // variant, once per sprite, asynchronously: no ReadPixels/GPU wait or
    // per-frame material/texture allocation when availability changes.
    internal sealed class TouchRadialDisabledArt : IDisposable
    {
        readonly Dictionary<Sprite, Sprite> cache = new Dictionary<Sprite, Sprite>();
        readonly HashSet<Sprite> requested = new HashSet<Sprite>();
        readonly Queue<Sprite> queue = new Queue<Sprite>();
        readonly Queue<Sprite> retirement = new Queue<Sprite>();
        readonly HashSet<Sprite> visible = new HashSet<Sprite>();
        bool pending, disposed;
        internal string Fault;
        internal void SetCatalogue(System.Collections.Generic.IList<TouchRadialEntry> entries)
        { visible.Clear(); foreach(var entry in entries) if(entry.Icon!=null)visible.Add(entry.Icon); }
        internal Sprite Get(Sprite source)
        {
            if (source == null || disposed) return null;
            if (cache.TryGetValue(source, out var result)) return result;
            if (requested.Add(source)) queue.Enqueue(source);
            return null;
        }
        internal void Pump()
        {
            if (disposed || pending || queue.Count == 0 || !SystemInfo.supportsAsyncGPUReadback) return;
            var source = queue.Dequeue(); if (source == null || source.texture == null) return;
            if(!visible.Contains(source)){requested.Remove(source);return;}
            float aspect = source.rect.width / Mathf.Max(1, source.rect.height);
            int w = Mathf.Max(1, Mathf.RoundToInt(256 * Mathf.Min(1, aspect)));
            int h = Mathf.Max(1, Mathf.RoundToInt(256 * Mathf.Min(1, 1/aspect)));
            var target = RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            target.filterMode = FilterMode.Bilinear;
            var uv = DataUtility.GetOuterUV(source); bool prior = GL.sRGBWrite; var active = RenderTexture.active;
            try
            {
                GL.sRGBWrite = true;
                Graphics.Blit(source.texture,target,new Vector2(uv.z-uv.x,uv.w-uv.y),new Vector2(uv.x,uv.y));
                pending = true;
                AsyncGPUReadback.Request(target,0,TextureFormat.RGBA32, request =>
                {
                    try
                    {
                        if (disposed || source == null) return;
                        if (request.hasError) { Fault="Asynchronous icon readback failed"; return; }
                        var data = request.GetData<Color32>(); var pixels = new Color32[data.Length];
                        for (int i=0;i<pixels.Length;++i)
                        { var p=data[i]; byte gray=TouchRadialDisabledColor.Gray(p.r,p.g,p.b); pixels[i]=new Color32(gray,gray,gray,p.a); }
                        var texture = new Texture2D(w,h,TextureFormat.RGBA32,false,false) { name="RTMaquetaXR unavailable native icon",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp };
                        texture.SetPixels32(pixels); texture.Apply(false,true);
                        cache[source]=Sprite.Create(texture,new Rect(0,0,w,h),new Vector2(.5f,.5f),100);
                        retirement.Enqueue(source);
                        // Never evict visible art: doing so with a >128-slot
                        // catalogue would continuously requeue the same icons.
                        int candidates=retirement.Count;
                        while(cache.Count>Math.Max(128,visible.Count)&&retirement.Count>0&&candidates-->0)
                        {
                            var key=retirement.Dequeue(); if(!cache.TryGetValue(key,out var old))continue;
                            if(visible.Contains(key)){retirement.Enqueue(key);continue;}
                            cache.Remove(key);requested.Remove(key);
                            if(old!=null){var oldTexture=old.texture;UnityEngine.Object.Destroy(old);if(oldTexture!=null)UnityEngine.Object.Destroy(oldTexture);}
                        }
                    }
                    catch(Exception error){Fault=error.Message;}
                    finally { RenderTexture.ReleaseTemporary(target); pending=false; }
                });
            }
            catch(Exception error) { pending=false; RenderTexture.ReleaseTemporary(target); Fault=error.Message; }
            finally { GL.sRGBWrite=prior; RenderTexture.active=active; }
        }
        public void Dispose()
        {
            disposed=true; queue.Clear(); requested.Clear();retirement.Clear();visible.Clear();
            foreach(var sprite in cache.Values) if(sprite!=null) { var texture=sprite.texture; UnityEngine.Object.Destroy(sprite); if(texture!=null)UnityEngine.Object.Destroy(texture); }
            cache.Clear(); // In-flight callback retires its own target on completion.
        }
    }
}
