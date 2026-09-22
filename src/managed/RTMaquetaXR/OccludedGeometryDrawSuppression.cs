using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Owlcat.Runtime.Visual.Waaagh;
using UnityEngine;
using UnityEngine.Rendering;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        const string OccludedDrawLabel="Skip fully faded scenery (experimental)";
        const string OccludedDrawHelp="Avoid sending fully faded scenery to the VR eyes. Only applies when the game's own fade is active, with compatible opaque materials; preserves shadows. Experimental: residual fade edges may disappear. Disable here to compare the same view. Does not hide other floors or alter game rules.";
        static Harmony _occludedDrawHarmony;
        static bool _occludedDrawReady, _occludedDrawFailed;
        static string _occludedDrawError;
        static Func<object> _occludedDrawService;
        delegate object OccludedDrawRendererReader(ref CameraData data);
        static OccludedDrawRendererReader _occludedDrawRenderer;
        static Func<object,IList> _occludedDrawFeatures;
        static Func<object,bool> _occludedDrawFeatureActive;
        static Type _occludedDrawFeatureType;
        static readonly List<Material> _occludedDrawMaterials=new List<Material>();
        static readonly Dictionary<Shader,bool> _occludedDrawShaders=new Dictionary<Shader,bool>();
        static MaterialPropertyBlock _occludedDrawBlock;
        static int _occludedDrawOpacityId;
        static double _occludedDrawCpuSumMs,_occludedDrawCpuPeakMs;
        static double _occludedDrawTrackingCpuSumMs,_occludedDrawTrackingCpuPeakMs;
        static long _occludedDrawCpuSamples;
        static long _occludedDrawTrackingCpuSamples,_occludedDrawNoFeature;
        static long _occludedDrawUpdates, _occludedDrawEyeScopes, _occludedDrawEligible;
        static readonly long[] _occludedDrawRejects=new long[7];
        static readonly OccludedGeometryDrawState<Renderer> _occludedDrawState=new OccludedGeometryDrawState<Renderer>(
            r=>r!=null,OccludedDrawEligible,r=>r.forceRenderingOff,(r,v)=>r.forceRenderingOff=v,
            r=>(int)r.shadowCastingMode,(r,v)=>r.shadowCastingMode=(ShadowCastingMode)v);
        static string OccludedDrawStatus=>ModLocalization.Text(!_cfg.skipFullyFadedScenery?"Off":_occludedDrawFailed?"Disabled after error":_occludedDrawReady?"On":"Unavailable");

        static void SetOccludedGeometryDrawSuppression(bool enabled)
        {
            _cfg.skipFullyFadedScenery=enabled;
            if(!enabled) try { _occludedDrawState.InvalidateScopes(); } catch(Exception e) { FailOccludedDraw(e); }
            MarkSettingsDirty();
        }
        static bool OccludedDrawEligible(Renderer renderer)
        {
            if(!(renderer is MeshRenderer) || !renderer.enabled) { ++_occludedDrawRejects[0]; return false; }
            if(renderer.forceRenderingOff || renderer.shadowCastingMode==ShadowCastingMode.TwoSided ||
                renderer.shadowCastingMode==ShadowCastingMode.ShadowsOnly) { ++_occludedDrawRejects[1]; return false; }
            // Read the ACTUAL shared override now, not just the last fade event.
            // A later material effect or MPB clear must not leave a stale hide.
            renderer.GetPropertyBlock(_occludedDrawBlock);
            if(!_occludedDrawBlock.HasFloat(_occludedDrawOpacityId) || _occludedDrawBlock.GetFloat(_occludedDrawOpacityId)!=0)
            { ++_occludedDrawRejects[2]; return false; }
            renderer.GetSharedMaterials(_occludedDrawMaterials);
            if(_occludedDrawMaterials.Count==0) { ++_occludedDrawRejects[3]; return false; }
            for(int i=0;i<_occludedDrawMaterials.Count;++i)
            {
                var material=_occludedDrawMaterials[i];
                var shader=material==null?null:material.shader;
                // HasProperty alone is insufficient: non-clip variants (and
                // several transparent/ShaderGraph families) ignore opacity.
                if(material==null || shader==null || !OccludedDrawShaderCompatible(shader) ||
                    material.renderQueue>2500 || material.IsKeywordEnabled("_TRANSPARENT_ON") ||
                    !material.IsKeywordEnabled("OCCLUDED_OBJECT_CLIP") ||
                    !material.HasProperty(_occludedDrawOpacityId))
                { ++_occludedDrawRejects[4]; return false; }
                // Material-index overrides take precedence over the shared MPB.
                // Reject them entirely, rather than guessing effective values.
                renderer.GetPropertyBlock(_occludedDrawBlock,i);
                if(!_occludedDrawBlock.isEmpty) { ++_occludedDrawRejects[5]; return false; }
            }
            ++_occludedDrawEligible; return true;
        }
        static bool OccludedDrawShaderCompatible(Shader shader)
        {
            if(_occludedDrawShaders.TryGetValue(shader,out bool compatible)) return compatible;
            compatible=shader.name=="Owlcat/Lit";_occludedDrawShaders[shader]=compatible;return compatible;
        }
        static void OccludedDrawSetPropertyBlock(Renderer renderer,MaterialPropertyBlock block,object proxy,float opacity,object owner)
        {
            renderer.SetPropertyBlock(block); // Always perform the original write.
            if(!_occludedDrawReady || _occludedDrawFailed) return;
            long started=DiagnosticsRecording?System.Diagnostics.Stopwatch.GetTimestamp():0;
            try
            {
                ++_occludedDrawUpdates;
                // Proxies include VFX, GPU crowds and linked dissolve entities.
                // They have independent draw paths; do not pretend to cull them.
                bool zero=proxy==null && renderer is MeshRenderer && opacity==0 && ReferenceEquals(owner,_occludedDrawService());
                if(proxy!=null) ++_occludedDrawRejects[6];
                _occludedDrawState.Observe(renderer,owner,zero);
            }
            catch(Exception error) { FailOccludedDraw(error); }
            finally
            {
                if(started!=0)
                {
                    double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency;
                    _occludedDrawTrackingCpuSumMs+=ms;_occludedDrawTrackingCpuPeakMs=Math.Max(_occludedDrawTrackingCpuPeakMs,ms);++_occludedDrawTrackingCpuSamples;
                }
            }
        }
        static IEnumerable<CodeInstruction> OccludedDrawTransferTranspiler(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
        {
            var code=new List<CodeInstruction>(instructions);
            var set=AccessTools.Method(typeof(Renderer),nameof(Renderer.SetPropertyBlock),new[]{typeof(MaterialPropertyBlock)});
            var setFloat=AccessTools.Method(typeof(MaterialPropertyBlock),nameof(MaterialPropertyBlock.SetFloat),new[]{typeof(int),typeof(float)});
            var locals=__originalMethod.GetMethodBody().LocalVariables;
            int found=-1,opacityLocal=-1;
            for(int i=0;i<code.Count;++i)
            {
                if(Equals(code[i].operand,setFloat)) opacityLocal=OccludedDrawLocal(code[i-1]);
                if(!Equals(code[i].operand,set)) continue;
                if(found>=0 || i<4 || !(code[i-3].operand is FieldInfo rendererField) || rendererField.FieldType!=typeof(Renderer) ||
                    code[i-2].opcode!=OpCodes.Ldarg_0 || !(code[i-1].operand is FieldInfo blockField) || blockField.FieldType!=typeof(MaterialPropertyBlock))
                    throw new InvalidOperationException("Unexpected native fade renderer write");
                int recordLocal=OccludedDrawLocal(code[i-4]);
                if(recordLocal<0 || opacityLocal<0 || locals[opacityLocal].LocalType!=typeof(float) ||
                    locals[recordLocal].LocalType!=rendererField.DeclaringType)
                    throw new InvalidOperationException("Unexpected native fade locals");
                var proxy=AccessTools.Field(rendererField.DeclaringType,"proxy");
                if(proxy==null || proxy.FieldType.FullName!="Owlcat.Runtime.Visual.OcclusionGeometryClip.IRendererProxy")
                    throw new MissingFieldException("Native renderer proxy");
                var first=new CodeInstruction(OpCodes.Ldloc,recordLocal);
                first.labels.AddRange(code[i].labels); code[i].labels.Clear();
                first.blocks.AddRange(code[i].blocks); code[i].blocks.Clear();
                code[i].opcode=OpCodes.Call; code[i].operand=AccessTools.Method(typeof(Main),nameof(OccludedDrawSetPropertyBlock));
                code.InsertRange(i,new[]{first,new CodeInstruction(OpCodes.Ldfld,proxy),new CodeInstruction(OpCodes.Ldloc,opacityLocal),new CodeInstruction(OpCodes.Ldarg_0)});
                found=i; i+=4;
            }
            if(found<0) throw new MissingMethodException("Native fade property-block write");
            return code;
        }
        static int OccludedDrawLocal(CodeInstruction instruction)
        {
            if(instruction.opcode==OpCodes.Ldloc_0) return 0;
            if(instruction.opcode==OpCodes.Ldloc_1) return 1;
            if(instruction.opcode==OpCodes.Ldloc_2) return 2;
            if(instruction.opcode==OpCodes.Ldloc_3) return 3;
            if(instruction.opcode!=OpCodes.Ldloc && instruction.opcode!=OpCodes.Ldloc_S) return -1;
            if(instruction.operand is LocalBuilder b) return b.LocalIndex;
            if(instruction.operand is LocalVariableInfo v) return v.LocalIndex;
            return Convert.ToInt32(instruction.operand);
        }
        // Dedicated declaring type keeps Harmony __state separate from the
        // heartbeat, HUD and first-person render-scope owners on this worker.
        internal static class OccludedDrawRenderPatch
        {
            internal static void Prefix(ref CameraData __1,out OccludedDrawScope __state)
            {
                __state=default;
                if(!_occludedDrawReady || _occludedDrawFailed)
                { RetryOccludedDrawRestoration(); return; }
                if(_occludedDrawState.Count==0 && !_occludedDrawState.Hidden) return;
                long started=DiagnosticsRecording?System.Diagnostics.Stopwatch.GetTimestamp():0;
                try
                {
                    object service=_occludedDrawService();
                    bool hide=_cfg.skipFullyFadedScenery && _active && _attached && !_modeFlat &&
                        IsEye(__1.Camera) && __1.Camera.cameraType==CameraType.Game &&
                        service!=null && OccludedDrawCameraHasClip(ref __1);
                    __state=_occludedDrawState.Begin(hide,service);
                    if(hide && __state.Entered) ++_occludedDrawEyeScopes;
                }
                catch(Exception error) { FailOccludedDraw(error); }
                finally { RecordOccludedDrawCost(started); }
            }
            internal static Exception Finalizer(Exception __exception,OccludedDrawScope __state)
            {
                if(!__state.Entered) return __exception;
                long started=DiagnosticsRecording?System.Diagnostics.Stopwatch.GetTimestamp():0;
                try { _occludedDrawState.End(__state,_occludedDrawService?.Invoke()); }
                catch(Exception error) { FailOccludedDraw(error); }
                finally { RecordOccludedDrawCost(started); }
                if(__exception!=null) FailOccludedDraw(__exception);
                return __exception;
            }
        }
        static bool OccludedDrawCameraHasClip(ref CameraData data)
        {
            // The native setup pass writes its activation through a CommandBuffer.
            // Shader.GetGlobalFloat would only read a preceding camera's value.
            // Require the active feature belonging to THIS camera's renderer;
            // its Game-camera branch always queues the setup before geometry.
            object renderer=_occludedDrawRenderer(ref data);
            if(renderer==null) return false;
            var features=_occludedDrawFeatures(renderer);
            if(features==null) return false;
            for(int i=0;i<features.Count;++i)
            {
                object feature=features[i];
                if(feature is UnityEngine.Object unity && unity!=null && feature.GetType()==_occludedDrawFeatureType && _occludedDrawFeatureActive(feature))
                    return true;
            }
            ++_occludedDrawNoFeature;
            return false;
        }
        static void RecordOccludedDrawCost(long started)
        {
            if(started==0) return;
            double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency;
            _occludedDrawCpuSumMs+=ms;_occludedDrawCpuPeakMs=Math.Max(_occludedDrawCpuPeakMs,ms);++_occludedDrawCpuSamples;
        }
        static void RetryOccludedDrawRestoration()
        {
            if(_occludedDrawState.PendingRestores==0) return;
            try { _occludedDrawState.Clear(); }
            catch { /* Keep ownership for the next render callback. Initial failure is logged. */ }
        }
        static void OccludedDrawDisposePrefix(object __instance)
        { try { _occludedDrawState.RemoveOwner(__instance); } catch(Exception error) { FailOccludedDraw(error); } }
        static void InstallOccludedGeometryDrawSuppression()
        {
            if(_occludedDrawHarmony!=null) return;
            try
            {
                var service=AccessTools.TypeByName("Owlcat.Runtime.Visual.OcclusionGeometryClip.Service");
                var system=AccessTools.TypeByName("Owlcat.Runtime.Visual.OcclusionGeometryClip.System");
                var field=AccessTools.Field(system,"s_Service");
                var transfer=AccessTools.Method(service,"TransferOpacity");
                var dispose=AccessTools.Method(service,"Dispose");
                if(field==null || !field.IsStatic || field.FieldType!=service || transfer==null || transfer.IsStatic ||
                    transfer.ReturnType!=typeof(void) || transfer.GetParameters().Length!=0 || dispose==null)
                    throw new MissingMethodException("Native occluder-fade ownership");
                var getter=new DynamicMethod("RTVR_CurrentOccluderFadeService",typeof(object),Type.EmptyTypes,typeof(Main).Module,true);
                var il=getter.GetILGenerator();il.Emit(OpCodes.Ldsfld,field);il.Emit(OpCodes.Ret);
                _occludedDrawService=(Func<object>)getter.CreateDelegate(typeof(Func<object>));
                var rendererField=AccessTools.Field(typeof(CameraData),"Renderer");
                var featureField=AccessTools.Field(typeof(ScriptableRenderer),"m_RendererFeatures");
                _occludedDrawFeatureType=AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.RendererFeatures.CameraObjectClip.CameraObjectClipFeature");
                var active=AccessTools.PropertyGetter(_occludedDrawFeatureType,"isActive");
                if(rendererField==null || featureField==null || active==null) throw new MissingFieldException("Current camera native clipping feature");
                var reader=new DynamicMethod("RTVR_OccluderCameraRenderer",typeof(object),new[]{typeof(CameraData).MakeByRefType()},typeof(Main).Module,true);
                var ri=reader.GetILGenerator();ri.Emit(OpCodes.Ldarg_0);ri.Emit(OpCodes.Ldfld,rendererField);ri.Emit(OpCodes.Ret);
                _occludedDrawRenderer=(OccludedDrawRendererReader)reader.CreateDelegate(typeof(OccludedDrawRendererReader));
                _occludedDrawFeatures=ReferenceGetter<IList>(typeof(ScriptableRenderer),null,featureField);
                _occludedDrawFeatureActive=ReferenceGetter<bool>(_occludedDrawFeatureType,active,null);
                var render=typeof(WaaaghPipeline).GetMethod("RenderSingleCamera",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
                    new[]{typeof(ScriptableRenderContext),typeof(CameraData).MakeByRefType()},null);
                if(render==null) throw new MissingMethodException("Typed camera worker");
                _occludedDrawOpacityId=Shader.PropertyToID("_OccluderObjectOpacity");
                _occludedDrawBlock=new MaterialPropertyBlock();
                _occludedDrawHarmony=new Harmony("RTMaquetaXR.ExperimentalFadedScenery");
                _occludedDrawHarmony.Patch(transfer,transpiler:new HarmonyMethod(typeof(Main),nameof(OccludedDrawTransferTranspiler)));
                _occludedDrawHarmony.Patch(dispose,prefix:new HarmonyMethod(typeof(Main),nameof(OccludedDrawDisposePrefix)));
                _occludedDrawHarmony.Patch(render,
                    prefix:new HarmonyMethod(typeof(OccludedDrawRenderPatch),nameof(OccludedDrawRenderPatch.Prefix)),
                    finalizer:new HarmonyMethod(typeof(OccludedDrawRenderPatch),nameof(OccludedDrawRenderPatch.Finalizer)));
                _occludedDrawReady=true;
            }
            catch(Exception error)
            {
                FailOccludedDraw(error); _occludedDrawHarmony?.UnpatchAll(_occludedDrawHarmony.Id); _occludedDrawHarmony=null;
            }
        }
        static void FailOccludedDraw(Exception error)
        {
            bool report=!_occludedDrawFailed;
            _occludedDrawFailed=true;_occludedDrawReady=false;_occludedDrawError=error.Message;
            try { _occludedDrawState.Clear(); } catch { }
            if(report) _log?.Error("[performance/faded-scenery] Experimental draw suppression disabled; pending restores="+_occludedDrawState.PendingRestores+": "+error.Message);
        }
        static void StopOccludedGeometryDrawSuppression()
        {
            try { _occludedDrawState.Clear(); } catch(Exception error) { FailOccludedDraw(error); }
            _occludedDrawReady=false;
            // A failed native restore retains the minimal camera callback to retry.
            // Never discard a still-owned invisible renderer merely to unpatch.
            if(_occludedDrawState.PendingRestores==0)
            { _occludedDrawHarmony?.UnpatchAll(_occludedDrawHarmony.Id);_occludedDrawHarmony=null; }
            _occludedDrawMaterials.Clear();_occludedDrawShaders.Clear();_occludedDrawBlock=null;_occludedDrawService=null;
        }
        static object OccludedGeometryDrawSnapshot()=>new {
            Enabled=_cfg.skipFullyFadedScenery,Ready=_occludedDrawReady,Failed=_occludedDrawFailed,Error=_occludedDrawError,
            Candidates=_occludedDrawState.Count,NativeFadeWritesObserved=_occludedDrawUpdates,EyeScopes=_occludedDrawEyeScopes,
            PendingRestores=_occludedDrawState.PendingRestores,
            NoCompatibleCameraFeature=_occludedDrawNoFeature,
            OwnCpu=new { CameraCallbacks=_occludedDrawCpuSamples,MeanMsPerCameraCallback=_occludedDrawCpuSamples>0?_occludedDrawCpuSumMs/_occludedDrawCpuSamples:0,PeakMsPerCameraCallback=_occludedDrawCpuPeakMs,
                NativeFadeObservationCallbacks=_occludedDrawTrackingCpuSamples,MeanMsPerObservation=_occludedDrawTrackingCpuSamples>0?_occludedDrawTrackingCpuSumMs/_occludedDrawTrackingCpuSamples:0,PeakMsPerObservation=_occludedDrawTrackingCpuPeakMs,TimingsEnabled=DiagnosticsRecording },
            CompatibleRendererChecks=_occludedDrawEligible,RendererScopesSuppressed=_occludedDrawState.Writes,Restored=_occludedDrawState.Restores,
            Rejects=new { Renderer=_occludedDrawRejects[0],ShadowMode=_occludedDrawRejects[1],ChangedOpacity=_occludedDrawRejects[2],
                EmptyMaterials=_occludedDrawRejects[3],ShaderOrVariant=_occludedDrawRejects[4],PerMaterialOverrides=_occludedDrawRejects[5],Proxy=_occludedDrawRejects[6] },
            Scope="Experimental exact-zero native fade, opaque Owlcat/Lit clip variants, original shadows, VR eyes only; no automatic fade activation or other-floor removal."
        };
    }
}
