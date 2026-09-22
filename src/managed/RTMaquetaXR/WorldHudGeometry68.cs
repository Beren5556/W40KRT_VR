using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        // Copy only changed native geometry. CanvasRenderer's mesh is borrowed:
        // it must never be recolored or destroyed by the mod. Baking the native
        // renderer tint into vertex colors also works for TMP/custom shaders
        // which do not consume a _RendererColor uniform.
        sealed class WorldHudGeometry68 : IDisposable
        {
            internal readonly Graphic Graphic;
            internal Mesh Source, Copy;
            internal bool Dirty=true;
            internal int Seen;
            Color tint;
            readonly List<Color32> sourceColors=new List<Color32>();
            readonly List<Color32> colors=new List<Color32>();
            internal WorldHudGeometry68(Graphic graphic)
            {Graphic=graphic;graphic.RegisterDirtyVerticesCallback(MarkDirty);}
            void MarkDirty(){Dirty=true;}
            internal Mesh Prepare(Mesh source,Color rendererTint)
            {
                if(Dirty||Source!=source||Copy==null||Copy.vertexCount!=source.vertexCount)
                {
                    if(Copy!=null)UnityEngine.Object.Destroy(Copy);
                    Copy=UnityEngine.Object.Instantiate(source);Copy.name="RTMaquetaXR native world UI geometry";
                    Source=source;sourceColors.Clear();source.GetColors(sourceColors);
                    if(sourceColors.Count==0)for(int i=0;i<source.vertexCount;i++)sourceColors.Add(new Color32(255,255,255,255));
                    Dirty=false;tint=new Color(-1,-1,-1,-1);++_worldHudGeometryRebuilds69;
                }
                if(tint!=rendererTint)
                {
                    colors.Clear();foreach(var color in sourceColors)colors.Add((Color)color*rendererTint);
                    Copy.SetColors(colors);tint=rendererTint;
                }
                Seen=Time.frameCount;return Copy;
            }
            public void Dispose()
            {
                if(Graphic!=null)Graphic.UnregisterDirtyVerticesCallback(MarkDirty);
                if(Copy!=null)UnityEngine.Object.Destroy(Copy);Copy=null;Source=null;
            }
        }
        static readonly Dictionary<Graphic,WorldHudGeometry68> _worldHudGeometry68=new Dictionary<Graphic,WorldHudGeometry68>();
        static readonly List<Graphic> _worldHudRetired68=new List<Graphic>();
        static readonly HashSet<GameObject> _worldHudNativeGroups69=new HashSet<GameObject>();
        static string _worldHudCompatibility68;
        static int _worldHudNativeGroupCount69;
        static long _worldHudCompatibilityChecks69,_worldHudNativeGroupTransitions69,_worldHudGeometryRebuilds69;
        static int _worldHudCollected68=-1;

        static void ClearWorldHudGeometry68()
        {
            foreach(var item in _worldHudGeometry68.Values)item.Dispose();
            _worldHudGeometry68.Clear();_worldHudRetired68.Clear();_worldHudNativeGroups69.Clear();
            _worldHudNativeGroupCount69=0;_worldHudCollected68=-1;
        }

        static bool WorldHudMaterialSupported68(CanvasRenderer renderer,Material material,out string reason)
        {
            reason=null;++_worldHudCompatibilityChecks69;
            // Do not silently drop CanvasRenderer-only clipping or stencil pop
            // instructions. Such a subtree retains its complete original route
            // for BOTH eyes. It is reported as degraded, never as sharp UI.
            if(renderer.hasRectClipping||renderer.hasPopInstruction||renderer.popMaterialCount!=0)
            {reason="Native mask/clip requires the original CanvasRenderer route";return false;}
            if(material==null||material.shader==null||material.passCount!=1||!material.shader.isSupported)
            {reason="Missing, unsupported or multi-pass native UI material";return false;}
            if(material.HasProperty("_StencilComp")&&material.GetInt("_StencilComp")!=(int)UnityEngine.Rendering.CompareFunction.Always&&
                material.GetInt("_StencilComp")!=(int)UnityEngine.Rendering.CompareFunction.Disabled)
            {reason="Native stencil-dependent material retains the original route";return false;}
            return true;
        }

        static void RetainNativeWorldGraphic69(Graphic graphic,string reason)
        {
            if(graphic==null)return;
            if(IsTacticalGraphic72(graphic))++_worldTacticalFallback72;
            var item=graphic.gameObject;
            if(_worldHudNativeGroups69.Add(item))++_worldHudNativeGroupTransitions69;
            if(_worldHudLayers.TryGetValue(item,out int original)&&item.layer==_worldHudLayer)item.layer=original;
            if(_worldHudGeometry68.TryGetValue(graphic,out var geometry))
            {geometry.Dispose();_worldHudGeometry68.Remove(graphic);}
            ++_worldHudNativeGroupCount69;
            if(_worldHudCompatibility68==null)_worldHudCompatibility68=reason;
        }

        static void IsolateCompatibleWorldGraphic69(Graphic graphic)
        {
            if(graphic==null)return;
            var item=graphic.gameObject;
            if(_worldHudNativeGroups69.Remove(item))++_worldHudNativeGroupTransitions69;
            if(_worldHudLayers.TryGetValue(item,out _)&&item.layer!=_worldHudLayer)
            {item.layer=_worldHudLayer;++_worldHudLayerWrites;}
        }
    }
}
