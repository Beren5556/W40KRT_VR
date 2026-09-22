using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Type _spaceBackdropType;
        static FieldInfo[] _spaceBackdropDecorations;
        static Type _spaceBoardGraphType;
        static FieldInfo _spaceBoardActive, _spaceBoardCenter, _spaceBoardRotation;
        static PropertyInfo _spaceBoardGraphs, _spaceBoardSize;
        static long _spaceBackdropRevision=-1;
        static float _spaceBackdropNext;
        static int _spaceBackdropProbes;
        static bool? _spaceBackdropChoice;
        static readonly Dictionary<Renderer,bool> _spaceBackdropRenderers=new Dictionary<Renderer,bool>();
        static string _spaceBackdropFault;
        static GameObject _spaceBackgroundPlane;
        static Mesh _spaceBackgroundMesh;
        static Material _spaceBackgroundMaterial;
        static Texture2D _spaceBackgroundTexture;
        static int _spaceNativeBackgroundMask;
        internal static bool CustomSpaceBackgroundActive => _active && InSpaceCombat && _cfg.customSpaceBackground &&
            _spaceBackgroundPlane != null && _spaceBackgroundPlane.activeInHierarchy;
        internal static void UpdateSpaceBackdrop()
        {
            if(!_active||!InSpaceCombat){RestoreSpaceBackdrop();return;}
            if(_spaceBackdropRevision!=SpatialGameContextRevision)
            {RestoreSpaceBackdrop();_spaceBackdropRevision=SpatialGameContextRevision;_spaceBackdropProbes=0;_spaceBackdropNext=0;}
            if(_spaceBackdropChoice!=_cfg.customSpaceBackground)
            {
                RestoreCustomSpaceBackdrop();_spaceBackdropChoice=_cfg.customSpaceBackground;
                _spaceBackdropProbes=0;_spaceBackdropNext=0;
            }
            UpdateSpaceGridPresentation();
            if(!_cfg.customSpaceBackground)RestoreCustomSpaceBackdrop();
            if((_cfg.customSpaceBackground?_spaceBackdropRenderers.Count>0:_spaceNativeBackgroundMask!=0)||
                _spaceBackdropProbes>=8||Time.unscaledTime<_spaceBackdropNext||ObservedNativeLoading||!_attached||_modeFlat)return;
            _spaceBackdropNext=Time.unscaledTime+1;
            try
            {
                // A failed optional asset must retain the native background.
                // Construct and validate before suppressing the native dome.
                if(_cfg.customSpaceBackground&&!PrepareCustomSpaceBackground())return;
                ++_spaceBackdropProbes;
                if(_spaceBackdropType==null)
                {
                    var composer=AccessTools.TypeByName("Kingmaker.SpaceCombat.SpaceCombatBackgroundComposer.SpaceCombatBackgroundComposer");
                    if(composer==null)throw new TypeLoadException("Native space background composer");
                    var names=new[]{"m_SkyDome","m_Star","m_Planets","m_SpaceObjects","m_Fx"};
                    var decorations=new FieldInfo[names.Length];
                    for(int i=0;i<names.Length;i++)
                    {
                        decorations[i]=AccessTools.Field(composer,names[i]);
                        if(decorations[i]==null||decorations[i].FieldType!=typeof(GameObject))
                            throw new MissingFieldException("Native space backdrop: "+names[i]);
                    }
                    _spaceBackdropDecorations=decorations;_spaceBackdropType=composer;
                }
                // Suppress only the composer's decorative backdrop branches.
                // Otherwise distant decorative nebulae still paint the black
                // exterior beyond our finite image. Gameplay ships, asteroids,
                // grid, abilities and the separate m_UnitsLight stay untouched.
                foreach(var component in UnityEngine.Object.FindObjectsByType(_spaceBackdropType,FindObjectsSortMode.None))
                {
                    foreach(var field in _spaceBackdropDecorations)
                    {
                        var decoration=field.GetValue(component) as GameObject;if(decoration==null)continue;
                        foreach(var renderer in decoration.GetComponentsInChildren<Renderer>(true))
                        {
                            _spaceNativeBackgroundMask|=1<<renderer.gameObject.layer;
                            if(!_cfg.customSpaceBackground||_spaceBackdropRenderers.ContainsKey(renderer))continue;
                            _spaceBackdropRenderers.Add(renderer,renderer.forceRenderingOff);renderer.forceRenderingOff=true;
                        }
                    }
                }
            }
            catch(Exception error)
            {
                RestoreCustomSpaceBackdrop();++_spaceBackdropProbes;
                if(_spaceBackdropFault!=error.Message){_spaceBackdropFault=error.Message;_log.Log("[space/backdrop] "+error.Message);}
            }
        }
        internal static void RestoreSpaceBackdrop()
        {
            RestoreSpaceGridPresentation();
            RestoreCustomSpaceBackdrop();
            _spaceBackdropRevision=-1;_spaceNativeBackgroundMask=0;_spaceBackdropChoice=null;
        }
        internal static void ApplyNativeSpaceBackgroundLayer(Camera eye)
        {
            // Native background geometry is normally rendered by a separate
            // background camera. Mono camera stacks are not copied to VR eyes.
            // Include its exact dome layer in each eye when Original is chosen.
            if(InSpaceCombat&&!CustomSpaceBackgroundActive)eye.cullingMask|=_spaceNativeBackgroundMask;
        }
        static void RestoreCustomSpaceBackdrop()
        {
            foreach(var pair in _spaceBackdropRenderers)if(pair.Key!=null&&pair.Key.forceRenderingOff)pair.Key.forceRenderingOff=pair.Value;
            _spaceBackdropRenderers.Clear();
            if(_spaceBackgroundPlane!=null){_spaceBackgroundPlane.SetActive(false);UnityEngine.Object.Destroy(_spaceBackgroundPlane);}
            if(_spaceBackgroundMesh!=null)UnityEngine.Object.Destroy(_spaceBackgroundMesh);
            if(_spaceBackgroundMaterial!=null)UnityEngine.Object.Destroy(_spaceBackgroundMaterial);
            _spaceBackgroundPlane=null;_spaceBackgroundMesh=null;_spaceBackgroundMaterial=null;
            // Retain the immutable texture across toggles and area transitions.
        }
        internal static void ReleaseSpaceBackgroundTexture()
        {
            if(_spaceBackgroundTexture!=null)UnityEngine.Object.Destroy(_spaceBackgroundTexture);
            _spaceBackgroundTexture=null;
        }
        static bool PrepareCustomSpaceBackground()
        {
            if(_spaceBackgroundPlane!=null)return true;
            if(ObservedNativeLoading||!_attached||_modeFlat||!TrySpaceBoardBackdrop(out var board,out var rotation,out float span,out float depth))return false;
            if(_spaceBackgroundTexture==null)
            {
                using(var stream=typeof(Main).Assembly.GetManifestResourceStream("RTMaquetaXR.SpaceBackground.png"))
                {
                    if(stream==null)throw new FileNotFoundException("Embedded space background");
                    byte[] bytes=new byte[checked((int)stream.Length)];int offset=0;
                    while(offset<bytes.Length){int count=stream.Read(bytes,offset,bytes.Length-offset);if(count==0)throw new EndOfStreamException();offset+=count;}
                    var texture=new Texture2D(2,2,TextureFormat.RGBA32,true,false){name="RTMaquetaXR dark blue space",filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp,anisoLevel=8};
                    if(!ImageConversion.LoadImage(texture,bytes,true)){UnityEngine.Object.Destroy(texture);throw new InvalidDataException("Space background PNG");}
                    _spaceBackgroundTexture=texture;
                }
            }
            var shader=Shader.Find("Owlcat/Unlit");
            if(shader==null||!shader.isSupported)throw new InvalidOperationException("Native unlit background shader unavailable");
            try
            {
                _spaceBackgroundMaterial=new Material(shader){name="RTMaquetaXR space background",renderQueue=1000};
                _spaceBackgroundMaterial.SetTexture("_BaseMap",_spaceBackgroundTexture);
                if(_spaceBackgroundMaterial.HasProperty("_MainTex"))_spaceBackgroundMaterial.SetTexture("_MainTex",_spaceBackgroundTexture);
                if(_spaceBackgroundMaterial.HasProperty("_BaseColor"))_spaceBackgroundMaterial.SetColor("_BaseColor",new Color(.65f,.65f,.65f,1));
                SetLiveMaterialInt(_spaceBackgroundMaterial,"_Surface",0);
                SetLiveMaterialInt(_spaceBackgroundMaterial,"_SrcBlend",(int)BlendMode.One);
                SetLiveMaterialInt(_spaceBackgroundMaterial,"_DstBlend",(int)BlendMode.Zero);
                SetLiveMaterialInt(_spaceBackgroundMaterial,"_ZWrite",1);
                SetLiveMaterialInt(_spaceBackgroundMaterial,"_ZTest",(int)CompareFunction.LessEqual);
                SetLiveMaterialInt(_spaceBackgroundMaterial,"_CullMode",(int)CullMode.Off);
                SetLiveMaterialInt(_spaceBackgroundMaterial,"_ReceiveShadows",0);
                _spaceBackgroundMesh=new Mesh{name="RTMaquetaXR background quad"};
                _spaceBackgroundMesh.vertices=new[]{new Vector3(-.5f,0,-.5f),new Vector3(-.5f,0,.5f),new Vector3(.5f,0,.5f),new Vector3(.5f,0,-.5f)};
                _spaceBackgroundMesh.uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right};
                _spaceBackgroundMesh.colors=new[]{Color.white,Color.white,Color.white,Color.white};
                _spaceBackgroundMesh.triangles=new[]{0,1,2,0,2,3};
                _spaceBackgroundMesh.RecalculateNormals();_spaceBackgroundMesh.RecalculateBounds();
                _spaceBackgroundPlane=new GameObject("RTMaquetaXR space background",typeof(MeshFilter),typeof(MeshRenderer));
                _spaceBackgroundPlane.GetComponent<MeshFilter>().sharedMesh=_spaceBackgroundMesh;
                var renderer=_spaceBackgroundPlane.GetComponent<MeshRenderer>();renderer.sharedMaterial=_spaceBackgroundMaterial;
                renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
                // Static world geometry still needs camera motion for DLSS/TAA.
                renderer.motionVectorGenerationMode=MotionVectorGenerationMode.Camera;
                // A single un-repeated square image, sized by the native board
                // plus 15% on each side. Fixed at graph origin for this area;
                // never chase the ship, head, camera, zoom or far clip plane.
                _spaceBackgroundMaterial.SetTextureScale("_BaseMap",Vector2.one);
                if(_spaceBackgroundMaterial.HasProperty("_MainTex"))_spaceBackgroundMaterial.SetTextureScale("_MainTex",Vector2.one);
                _spaceBackgroundPlane.transform.position=board+rotation*(Vector3.down*depth);
                _spaceBackgroundPlane.transform.rotation=rotation;
                _spaceBackgroundPlane.transform.localScale=new Vector3(span,1,span);
                // No collider, light, camera or global render-state changes.
                // Exactly one cached two-triangle unlit surface for both eyes.
                return true;
            }
            catch{RestoreCustomSpaceBackdrop();throw;}
        }
        static bool TrySpaceBoardBackdrop(out Vector3 center,out Quaternion rotation,out float span,out float depth)
        {
            center=Vector3.zero;rotation=Quaternion.identity;span=depth=0;
            if(_spaceBoardGraphType==null)
            {
                var astar=AccessTools.TypeByName("AstarPath")??throw new TypeLoadException("AstarPath");
                var grid=AccessTools.TypeByName("Kingmaker.Pathfinding.CustomGridGraph")??throw new TypeLoadException("CustomGridGraph");
                var active=AccessTools.Field(astar,"active");var graphs=AccessTools.Property(astar,"graphs");
                var size=AccessTools.Property(grid,"size");
                var origin=AccessTools.Field(grid,"center");var angles=AccessTools.Field(grid,"rotation");
                if(active==null||!active.IsStatic||active.FieldType!=astar||graphs?.GetGetMethod(true)==null||
                    !typeof(IEnumerable).IsAssignableFrom(graphs.PropertyType)||size?.PropertyType!=typeof(Vector2)||
                    origin?.FieldType!=typeof(Vector3)||angles?.FieldType!=typeof(Vector3))
                    throw new MissingMemberException("Native space board graph contract");
                _spaceBoardActive=active;_spaceBoardGraphs=graphs;_spaceBoardSize=size;
                _spaceBoardCenter=origin;_spaceBoardRotation=angles;_spaceBoardGraphType=grid;
            }
            var path=_spaceBoardActive.GetValue(null);if(path==null)return false;
            var graphsNow=_spaceBoardGraphs.GetValue(path,null) as IEnumerable;if(graphsNow==null)return false;
            // Identical graph selection to native GridVisualizer.Rebuild.
            foreach(var graph in graphsNow)
            {
                if(graph==null||!_spaceBoardGraphType.IsInstanceOfType(graph))continue;
                center=(Vector3)_spaceBoardCenter.GetValue(graph);
                var angles=(Vector3)_spaceBoardRotation.GetValue(graph);
                var size=(Vector2)_spaceBoardSize.GetValue(graph,null);
                if(!TouchTabletopState.Finite(TablePoint(center))||!TouchTabletopState.Finite(TablePoint(angles))||
                    !SpaceBackdropPolicy65.TryDimensions(size.x,size.y,out span,out depth))return false;
                rotation=Quaternion.Euler(angles);return true;
            }
            return false;
        }
    }
}
