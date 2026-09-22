using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.UI;
using Owlcat.Runtime.Visual.Waaagh;
using Owlcat.Runtime.Visual.Waaagh.Passes;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static readonly Dictionary<GameObject, int> _worldHudLayers = new Dictionary<GameObject, int>();
        static readonly List<GameObject> _worldHudRemoved = new List<GameObject>();
        static readonly WorldHudLatePass[] _worldHudPasses = new WorldHudLatePass[2];
        static WorldHudLatePass _worldHudListScope;
        static bool _worldHudHooks, _worldHudDirty = true, _worldHudFault, _worldHudNativeFallback, _worldHudRecoveryPending;
        static string _worldHudError;
        static int _worldHudLayer = -1, _worldHudFrame = -1;
        static long _worldHudFallbackRevision = long.MinValue;
        static int _worldHudEligibleFamilies, _worldHudReportFrame = -1, _worldHudEvaluatedFrame = -1, _worldHudReportedEyes, _worldHudEmptyEyes;
        static int _worldHudEmptyStreak, _worldHudPreviousEmptyFrame = -2;
        static long _worldHudLayerWrites, _worldHudListBuilds, _worldHudEmptyFrames, _worldHudPassRecords;
        static long _worldHudProcessingLists, _worldHudPopulatedLists, _worldHudEmptyLists, _worldHudInvalidLists;
        static long _worldHudPreparedEyes, _worldHudValidListEyes, _worldHudDrawSubmissions, _worldHudNativeRecoveries;
        static long _worldHudExplicitFrames,_worldHudExplicitDraws;
        static int WorldHudScreenParams, WorldHudScaledParams, WorldHudScreenSize, WorldHudMipBias,WorldHudMainTex,WorldHudSampleAdd;
        static readonly List<Graphic> _worldHudExplicitGraphics=new List<Graphic>(128);
        static readonly List<WorldHudUiDraw> _worldHudExplicitItems=new List<WorldHudUiDraw>(192);
        static MaterialPropertyBlock _worldHudProperties;
        delegate bool WorldHudResolveContract(ref CameraData data);
        static WorldHudResolveContract _worldHudResolveContract;

        internal static object WorldHudSnapshot() => new {
            Enabled = _cfg.uiFullResolution && _cfg.uiEnabled && EffectiveWorldOvertips,
            Active = _worldHudFrame == Time.frameCount, Layer = _worldHudLayer,
            Nodes = _worldHudLayers.Count, Fault = _worldHudFault, Error = _worldHudError,
            LayerWrites = _worldHudLayerWrites, ListBuilds = _worldHudListBuilds,
            EmptyEyeFrames = _worldHudEmptyFrames, RecordedEyePasses = _worldHudPassRecords,
            PreparedEyes = _worldHudPreparedEyes, ValidListEyes = _worldHudValidListEyes,
            DrawSubmissions = _worldHudDrawSubmissions, NativeRecoveries = _worldHudNativeRecoveries,
            ExplicitFrames = _worldHudExplicitFrames, ExplicitDraws = _worldHudExplicitDraws,
            CompatibilityFallback = _worldHudCompatibility68,
            CompatibilityChecks = _worldHudCompatibilityChecks69,
            NativeCompatibilityGroups = _worldHudNativeGroupCount69,
            NativeGroupTransitions = _worldHudNativeGroupTransitions69,
            GeometryRebuilds = _worldHudGeometryRebuilds69,
            NativeViewCacheBuilds = _nativeWorldCacheBuilds69,
            NativeViewHierarchyComponentsScanned = _nativeWorldHierarchyScans69,
            NativeOffsetWrites = _nativeWorldOffsetWrites69,
            NativeHealthEvaluations = _nativeWorldHealthEvaluations69,
            ProcessingListQueries = _worldHudProcessingLists, PopulatedListQueries = _worldHudPopulatedLists,
            EmptyListQueries = _worldHudEmptyLists, InvalidListQueries = _worldHudInvalidLists,
            EligibleFamilies = _worldHudEligibleFamilies, EligibleInteractions = _nativeWorldEligibleInteractions66,
            EligibleAttacks = _nativeWorldEligibleAttacks66, EligibleBarks = _nativeWorldEligibleBarks66,
            NativeFallback = _worldHudNativeFallback, EmptyStreak = _worldHudEmptyStreak,
            LeftFrame = _worldHudPasses[0]?.RenderedFrame ?? -1, RightFrame = _worldHudPasses[1]?.RenderedFrame ?? -1,
            Path = _worldHudNativeFallback ? "Selective original native route after anomalous empty replacement" :
                _worldHudExplicitItems.Count>0 ? "Native CanvasRenderer geometry after final resolve" :
                "Original world UI renderer lists after final resolve; no additional cameras or culling"
        };

        internal static void InvalidateWorldHudResolution() { _worldHudDirty = true; }

        internal static void PrepareWorldHudResolution(Camera left, Camera right)
        {
            if (_worldHudFallbackRevision != SpatialGameContextRevision)
            {
                _worldHudFallbackRevision = SpatialGameContextRevision; _worldHudNativeFallback = false;
                _worldHudRecoveryPending = false; _worldHudEmptyStreak = 0; _worldHudPreviousEmptyFrame = -2;
                _worldHudDirty = true;
            }
            if (_worldHudRecoveryPending)
            {
                _worldHudRecoveryPending = false; _worldHudNativeFallback = true; ++_worldHudNativeRecoveries;
                _log?.Error("[ui67/world-resolution] Eligible native UI produced empty replacement lists; restoring its original stereo route for this context.");
            }
            if (_worldHudNativeFallback)
            {
                _worldHudEligibleFamilies = NativeWorldUiEligibility66();
                RestoreWorldHudResolution(false); return;
            }
            if (!_cfg.uiFullResolution || !_cfg.uiEnabled || !EffectiveWorldOvertips || !_active || !_attached ||
                _modeFlat || !HudCaptureActive || left == null || right == null || _worldHudFault)
            {
                RestoreWorldHudResolution(!_active || !_attached || !_cfg.uiFullResolution || !_cfg.uiEnabled || !EffectiveWorldOvertips);
                return;
            }
            try
            {
                EnsureWorldHudHooks();
                if (_worldHudLayer < 0) ChooseWorldHudLayer();
                if (_worldHudDirty) RefreshWorldHudLayers();
                _worldHudEligibleFamilies = NativeWorldUiEligibility66();
                // Keep these objects in the existing eye cull. Only the two
                // original transparent renderer lists omit their private layer.
                left.cullingMask |= 1 << _worldHudLayer; right.cullingMask |= 1 << _worldHudLayer;
                if(_worldHudEligibleFamilies==0)
                {
                    // No visible world information: retain ownership/cache but
                    // do not enqueue two empty passes and four renderer lists.
                    _worldHudFrame=-1; _worldHudExplicitItems.Clear();
                    foreach(var pass in _worldHudPasses)pass?.Reset();
                    return;
                }
                _worldHudFrame = Time.frameCount;
                _worldHudPasses[0].Prepare(left, _worldHudFrame);
                _worldHudPasses[1].Prepare(right, _worldHudFrame);
                _worldHudPreparedEyes += 2;
            }
            catch (Exception error) { FailWorldHud(error); RestoreWorldHudResolution(); }
        }

        internal static void RestoreWorldHudResolution(bool releaseLayer = true)
        {
            foreach (var entry in _worldHudLayers)
                if (entry.Key != null && entry.Key.layer == _worldHudLayer) entry.Key.layer = entry.Value;
            _worldHudLayers.Clear(); _worldHudRemoved.Clear(); _worldHudDirty = true;
            _worldHudFrame = -1; _worldHudListScope = null;
            _worldHudExplicitItems.Clear();_worldHudExplicitGraphics.Clear();_worldHudNativeGroups69.Clear();
            _worldHudNativeGroupCount69=0;
            _worldHudReportFrame = _worldHudEvaluatedFrame = -1; _worldHudReportedEyes = _worldHudEmptyEyes = 0;
            foreach (var pass in _worldHudPasses) pass?.Reset();
            if (releaseLayer) { _worldHudLayer = -1; _worldHudNativeFallback = _worldHudRecoveryPending = false; ClearWorldHudGeometry68(); }
            // A render failure stays latched for this process. Toggling settings
            // must not repeatedly retry a broken callback once per eye.
        }

        static void ChooseWorldHudLayer()
        {
            if (UiLayerOwnership.World >= 0) { _worldHudLayer = UiLayerOwnership.World; return; }
            uint used = _hudLayer < 0 ? 0 : 1u << _hudLayer;
            foreach (var item in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (item != null) used |= 1u << item.gameObject.layer;
            uint named = 0;
            for (int candidate = 24; candidate < 32; ++candidate)
                if (!string.IsNullOrEmpty(LayerMask.LayerToName(candidate))) named |= 1u << candidate;
            int slot = UiLayerOwnership.Find(used, named, 24);
            if (slot >= 0) { _worldHudLayer = UiLayerOwnership.World = slot; return; }
            throw new InvalidOperationException("No free layer for world HUD isolation");
        }

        static void RefreshWorldHudLayers()
        {
            _worldHudRemoved.Clear();
            foreach (var entry in _worldHudLayers)
                if (entry.Key == null || !_hudWorldNodeSet.Contains(entry.Key.transform)) _worldHudRemoved.Add(entry.Key);
            foreach (var item in _worldHudRemoved)
            {
                if (item != null && item.layer == _worldHudLayer) item.layer = _worldHudLayers[item];
                _worldHudLayers.Remove(item);
            }
            foreach (var node in _hudWorldNodeSet)
            {
                if (node == null || node.GetComponent<Graphic>()==null) continue;
                GameObject item = node.gameObject;
                if (!_worldHudLayers.ContainsKey(item)) _worldHudLayers.Add(item, UiLayerOwnership.NativeLayer(item.layer, _worldHudLayer));
                if (item.layer != _worldHudLayer) { item.layer = _worldHudLayer; ++_worldHudLayerWrites; }
            }
            _worldHudDirty = false;
        }

        static void EnsureWorldHudHooks()
        {
            if (_worldHudHooks) return;
            var init = AccessTools.DeclaredMethod(typeof(WaaaghRendererLists), "Init", new[] { typeof(RenderingData).MakeByRefType() });
            var setup = AccessTools.DeclaredMethod(typeof(WaaaghRenderer), "Setup", new[] { typeof(ScriptableRenderContext), typeof(RenderingData).MakeByRefType() });
            if (init == null || setup == null) throw new MissingMethodException("World HUD pipeline contract changed");
            _worldHudResolveContract = BuildWorldHudResolveContract();
            // Lazy: loading the assembly or running contract tests never calls
            // Unity native functions from Main's static initializer.
            WorldHudScreenParams = Shader.PropertyToID("_ScreenParams");
            WorldHudScaledParams = Shader.PropertyToID("_ScaledScreenParams");
            WorldHudScreenSize = Shader.PropertyToID("_ScreenSize");
            WorldHudMipBias = Shader.PropertyToID("_GlobalMipBias");
            WorldHudMainTex = Shader.PropertyToID("_MainTex");
            WorldHudSampleAdd = Shader.PropertyToID("_TextureSampleAdd");
            _worldHudProperties=new MaterialPropertyBlock();
            _worldHudPasses[0] = new WorldHudLatePass { EyeBit = 1 }; _worldHudPasses[1] = new WorldHudLatePass { EyeBit = 2 };
            _harmony.Patch(init, prefix: new HarmonyMethod(typeof(Main), nameof(WorldHudListsPrefix)),
                finalizer: new HarmonyMethod(typeof(Main), nameof(WorldHudListsFinalizer)),
                transpiler: new HarmonyMethod(typeof(Main), nameof(WorldHudListTranspiler)));
            _harmony.Patch(setup, postfix: new HarmonyMethod(typeof(Main), nameof(WorldHudSetupPostfix)));
            _worldHudHooks = true;
        }

        static WorldHudResolveContract BuildWorldHudResolveContract()
        {
            var required = AccessTools.DeclaredField(typeof(CameraData), "CameraResolveRequired");
            var target = AccessTools.DeclaredField(typeof(CameraData), "CameraResolveTargetBufferType");
            if (required == null || required.FieldType != typeof(bool) || target == null || !target.FieldType.IsEnum)
                throw new MissingFieldException("World HUD camera resolve contract changed");
            var method = new DynamicMethod("RTMaquetaXR_WorldHudFinalTarget", typeof(bool),
                new[] { typeof(CameraData).MakeByRefType() }, typeof(Main), true);
            var il = method.GetILGenerator(); var no = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, required); il.Emit(OpCodes.Brfalse_S, no);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, target); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Ret);
            il.MarkLabel(no); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            return (WorldHudResolveContract)method.CreateDelegate(typeof(WorldHudResolveContract));
        }

        static WorldHudLatePass WorldHudPass(Camera camera)
        {
            if (_worldHudFault || _worldHudFrame != Time.frameCount || camera == null) return null;
            foreach (var pass in _worldHudPasses) if (pass != null && pass.Camera == camera && pass.Frame == _worldHudFrame) return pass;
            return null;
        }

        static void WorldHudSetupPostfix(WaaaghRenderer __instance, ref RenderingData __1)
        {
            var pass = WorldHudPass(__1.CameraData.Camera);
            if (pass == null) return;
            try { __instance.EnqueuePass(pass); pass.Enqueued = true; }
            catch (Exception error) { FailWorldHud(error); }
        }

        static void WorldHudListsPrefix(ref RenderingData __0, out WorldHudLatePass __state)
        {
            __state = _worldHudListScope;
            var pass = WorldHudPass(__0.CameraData.Camera);
            _worldHudListScope = null;
            if (pass == null || !pass.Enqueued) return;
            try
            {
                // ImportCameraData precedes WaaaghRendererLists.Init in the
                // installed renderer. Check the real destination BEFORE any
                // original renderer list loses these objects.
                pass.ValidateTarget(ref __0);
                if(_worldHudCollected68!=Time.frameCount)
                {
                    _worldHudCollected68=Time.frameCount;
                    // Collection happens after the normal Canvas pre-render
                    // rebuild, before either eye loses its original UI route.
                    // Both owned destinations must be valid before replacement.
                    if(!WorldHudBothTargets68()||!CollectWorldHudUi68())
                    {RestoreWorldHudResolution(false);return;}
                }
                _worldHudListScope = pass;
            }
            catch (Exception error) { FailWorldHud(error); }
        }

        static Exception WorldHudListsFinalizer(Exception __exception, WorldHudLatePass __state)
        {
            bool owned = _worldHudListScope != null;
            _worldHudListScope = __state;
            if (owned && __exception != null) FailWorldHud(__exception);
            return __exception; // Never swallow an exception from the game's renderer.
        }

        internal static IEnumerable<CodeInstruction> WorldHudListTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(); int count = 0;
            var create = AccessTools.DeclaredMethod(typeof(ScriptableRenderContext), "CreateRendererList", new[] { typeof(RendererListDesc) });
            var filter = AccessTools.DeclaredMethod(typeof(Main), nameof(WorldHudFilterDescriptor));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(create))
                {
                    var call = new CodeInstruction(OpCodes.Call, filter);
                    call.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    result.Add(call); ++count;
                }
                result.Add(instruction);
            }
            if (count != 6) throw new InvalidOperationException("Expected exactly six installed Waaagh renderer-list creations");
            return result;
        }

        static RendererListDesc WorldHudFilterDescriptor(RendererListDesc descriptor)
        {
            var pass = _worldHudListScope;
            if (pass == null || _worldHudFault || _worldHudNativeFallback) return descriptor;
            int slot = WorldHudPolicy.QueueSlot(descriptor.renderQueueRange.lowerBound, descriptor.renderQueueRange.upperBound);
            if (slot < 0) return descriptor;
            var high = descriptor; high.layerMask &= 1 << _worldHudLayer;
            pass.Descriptors[slot] = high; pass.Captured[slot] = true;
            descriptor.layerMask &= ~(1 << _worldHudLayer);
            return descriptor;
        }

        static void FailWorldHud(Exception error)
        {
            if (_worldHudFault) return;
            _worldHudFault = true; _worldHudError = error.Message;
            _log?.Error("[ui/world-resolution] Original stereo path restored on next update: " + error.Message);
        }

        static void ReportWorldHudLists(int eyeBit, int frame, bool empty)
        {
            if (_worldHudReportFrame != frame) { _worldHudReportFrame = frame; _worldHudReportedEyes = _worldHudEmptyEyes = 0; }
            _worldHudReportedEyes |= eyeBit; if (empty) _worldHudEmptyEyes |= eyeBit; ++_worldHudValidListEyes;
            if (_worldHudReportedEyes != 3 || _worldHudEvaluatedFrame == frame) return;
            _worldHudEvaluatedFrame = frame;
            bool bothEmpty = _worldHudEmptyEyes == 3;
            _worldHudEmptyStreak = WorldHudPolicy.NextEmptyRecoveryStreak(_worldHudEmptyStreak,
                _worldHudPreviousEmptyFrame, frame, _worldHudEligibleFamilies != 0, true, bothEmpty);
            _worldHudPreviousEmptyFrame = bothEmpty && _worldHudEligibleFamilies != 0 ? frame : -2;
            if (WorldHudPolicy.RestoreNativeRoute(_worldHudEmptyStreak)) _worldHudRecoveryPending = true;
        }

        struct WorldHudUiDraw
        {
            internal Graphic Graphic;
            internal Mesh Mesh;internal Matrix4x4 Matrix;internal Material Material;internal Texture Texture;
            internal Vector4 SampleAdd;internal int Submesh,Depth,Order;
        }

        static bool WorldHudBothTargets68()
        {
            foreach(var eye in _worldHudPasses)
            {
                var camera=eye?.Camera;var target=camera==null?null:camera.targetTexture;
                if(camera==null||target==null||!target.IsCreated())return false;
                var desc=target.descriptor;
                if(!WorldHudPolicy.ValidTarget(camera.pixelWidth,camera.pixelHeight,desc.width,desc.height,desc.msaaSamples,
                    camera.allowDynamicResolution||desc.useDynamicScale,camera.rect==new Rect(0,0,1,1)))return false;
            }
            return true;
        }

        static bool CollectWorldHudUi68()
        {
            EnforceWorldPresentation72();
            _worldHudCompatibility68=null;_worldHudNativeGroupCount69=0;
            _worldHudExplicitItems.Clear();_worldHudExplicitGraphics.Clear();
            if(_overtipsRoot==null)return true;
            CollectInteractionGraphics70(_worldHudExplicitGraphics);
            CollectProximityMarkers74(_worldHudExplicitGraphics);
            CollectTacticalGraphics72(_worldHudExplicitGraphics);
            int order=0;
            foreach(var graphic in _worldHudExplicitGraphics)
            {
                if(graphic==null||CoverageSuppressed77(graphic)||!graphic.isActiveAndEnabled||graphic.color.a<=.001f)continue;
                var renderer=graphic.canvasRenderer;
                if(renderer==null||renderer.cull||renderer.GetInheritedAlpha()<=.001f)continue;
                Mesh mesh=renderer.GetMesh();if(mesh==null||mesh.vertexCount==0)continue;
                int materialCount=renderer.materialCount;
                if(materialCount!=1||mesh.subMeshCount!=1||!mesh.isReadable)
                {RetainNativeWorldGraphic69(graphic,"Native geometry not safely reproducible");continue;}
                bool compatible=true;string reason=null;Material material=null;
                for(int slot=0;slot<materialCount;slot++)
                {
                    material=slot<renderer.materialCount?renderer.GetMaterial(slot):null;
                    if(material==null)material=graphic.materialForRendering;
                    if(!WorldHudMaterialSupported68(renderer,material,out reason)){compatible=false;break;}
                    if(graphic is Image image&&(image.overrideSprite??image.sprite)!=null&&(image.overrideSprite??image.sprite).associatedAlphaSplitTexture!=null)
                    {reason="Split-alpha sprite retains the original route";compatible=false;break;}
                }
                if(!compatible){RetainNativeWorldGraphic69(graphic,reason);continue;}
                IsolateCompatibleWorldGraphic69(graphic);
                if(IsTacticalGraphic72(graphic))++_worldTacticalDraws72;
                for(int slot=0;slot<materialCount;slot++)
                {
                    material=slot<renderer.materialCount?renderer.GetMaterial(slot):null;
                    if(material==null)material=graphic.materialForRendering;
                    Color color=renderer.GetColor();color.a=renderer.GetInheritedAlpha();
                    if(!_worldHudGeometry68.TryGetValue(graphic,out var geometry))
                    {_worldHudGeometry68.Add(graphic,geometry=new WorldHudGeometry68(graphic));}
                    Mesh copy=geometry.Prepare(mesh,color);
                    var texture=graphic.mainTexture;
                    _worldHudExplicitItems.Add(new WorldHudUiDraw{
                        Graphic=graphic,Mesh=copy,Matrix=graphic.rectTransform.localToWorldMatrix,Material=material,Texture=texture,
                        SampleAdd=texture is Texture2D tex&&tex.format==TextureFormat.Alpha8?new Vector4(1,1,1,0):Vector4.zero,
                        Submesh=slot,Depth=renderer.absoluteDepth,Order=order++});
                }
            }
            if(_worldHudNativeGroupCount69>0)
                _worldHudCompatibility68=(_worldHudCompatibility68??"Native compatibility route")+" · stable groups="+_worldHudNativeGroupCount69;
            _worldHudExplicitItems.Sort((a,b)=>{int depth=a.Depth.CompareTo(b.Depth);return depth!=0?depth:a.Order.CompareTo(b.Order);});
            if(_worldHudExplicitItems.Count>0)++_worldHudExplicitFrames;
            if(Time.frameCount%300==0)
            {
                _worldHudRetired68.Clear();foreach(var item in _worldHudGeometry68)
                    if(item.Key==null||item.Value.Seen<Time.frameCount-300)_worldHudRetired68.Add(item.Key);
                foreach(var graphic in _worldHudRetired68){_worldHudGeometry68[graphic].Dispose();_worldHudGeometry68.Remove(graphic);}
            }
            return true;
        }

        sealed class WorldHudLatePass : ScriptableRenderPass
        {
            internal Camera Camera;
            internal int Frame = -1, RenderedFrame = -1;
            internal int EyeBit;
            internal bool Enqueued;
            internal readonly RendererListDesc[] Descriptors = new RendererListDesc[2];
            internal readonly bool[] Captured = new bool[2];
            readonly RendererList[] lists = new RendererList[2];
            internal List<WorldHudUiDraw> Explicit;
            TextureHandle target;
            int width, height;
            bool ready;
            internal WorldHudLatePass() : base((RenderPassEvent)1001) { }
            public override string Name => "RTMaquetaXR world HUD at output resolution";
            internal void Prepare(Camera camera, int frame)
            { Camera = camera; Frame = frame; Explicit=_worldHudExplicitItems; Enqueued = ready = false; Captured[0] = Captured[1] = false; }
            internal void Reset() { Camera = null; Explicit=null; Frame = -1; Enqueued = ready = false; }

            internal void ValidateTarget(ref RenderingData data)
            {
                if (!_worldHudResolveContract(ref data.CameraData))
                    throw new InvalidOperationException("World HUD requires the last camera resolving to its final target");
                var resources = data.CameraData.Renderer.RenderGraphResources;
                target = resources.CameraResolveColorBuffer;
                if (!target.IsValid()) throw new InvalidOperationException("World HUD final resolve target unavailable");
                // Waaagh imports EVEN a camera RenderTexture through
                // ImportBackbuffer(RenderTargetIdentifier). That valid imported
                // handle deliberately has no queryable RenderGraph descriptor.
                // Verify the actual CameraData target that was imported, then
                // read its owned RenderTexture descriptor; never substitute an
                // intermediate graph texture or infer a backbuffer size.
                var output = data.CameraData.TargetTexture;
                if (output == null || output != Camera.targetTexture || !output.IsCreated())
                    throw new InvalidOperationException("World HUD final target is not the live owned eye texture");
                var descriptor = output.descriptor;
                width = Camera.pixelWidth; height = Camera.pixelHeight;
                if (!WorldHudPolicy.ValidTarget(width, height, descriptor.width, descriptor.height,
                    descriptor.msaaSamples, Camera.allowDynamicResolution || descriptor.useDynamicScale,
                    Camera.rect == new Rect(0, 0, 1, 1)))
                    throw new InvalidOperationException("World HUD requires the complete resolved eye target");
            }

            public override void ConfigureRendererLists(ref RenderingData data, RenderGraphResources resources)
            {
                if (_worldHudFault || Camera != data.CameraData.Camera || Frame != Time.frameCount) return;
                try
                {
                    if (!Captured[0] || !Captured[1])
                        throw new InvalidOperationException("World HUD requires both original transparent and overlay descriptors");
                    if(Explicit!=null&&Explicit.Count>0){ready=true;return;}
                    for (int slot = 0; slot < 2; ++slot)
                    { lists[slot] = data.Context.CreateRendererList(Descriptors[slot]); DependsOn(in lists[slot]); ++_worldHudListBuilds; }
                    ready = true;
                }
                catch (Exception error) { FailWorldHud(error); }
            }

            public override bool AreRendererListsEmpty(ScriptableRenderContext context)
            {
                if (!ready || _worldHudFault) return true;
                try
                {
                    if(Explicit!=null&&Explicit.Count>0)
                    {ReportWorldHudLists(EyeBit,Frame,false);return false;}
                    // Waaagh's base treats Processing like Empty. These two
                    // private lists have just entered PrepareRendererListsAsync;
                    // absence of a completed result must not erase the HUD.
                    // Query each list once and leave pending work to the normal
                    // DrawRendererList path; never wait or add a scene cull.
                    var transparent = context.QueryRendererListStatus(lists[0]);
                    var overlay = context.QueryRendererListStatus(lists[1]);
                    CountWorldHudListStatus(transparent); CountWorldHudListStatus(overlay);
                    if (!WorldHudPolicy.ValidListStatus((int)transparent) || !WorldHudPolicy.ValidListStatus((int)overlay))
                        throw new InvalidOperationException("World HUD renderer list became invalid");
                    bool empty = WorldHudPolicy.SkipEmptyLists((int)transparent, (int)overlay);
                    ReportWorldHudLists(EyeBit, Frame, empty);
                    if (empty) ++_worldHudEmptyFrames;
                    return empty;
                }
                catch (Exception error) { FailWorldHud(error); return true; }
            }

            protected override void RecordRenderGraph(ref RenderingData data)
            {
                if (!ready || _worldHudFault || Frame != Time.frameCount || Camera != data.CameraData.Camera) return;
                try
                {
                    var graph = data.RenderGraph;
                    var depthDesc = new TextureDesc(width, height) {
                        name = "RTMaquetaXR world HUD stencil", depthBufferBits = DepthBits.Depth24,
                        clearBuffer = true, clearColor = Color.clear, filterMode = FilterMode.Point
                    };
                    var depth = graph.CreateTexture(depthDesc);
                    using (var builder = graph.AddRenderPass<WorldHudPassData>(Name, out var pass))
                    {
                        // Alpha blending requires the resolved scene already in
                        // this color target, not a discardable write-only output.
                        builder.ReadTexture(target); builder.UseColorBuffer(target, 0);
                        builder.UseDepthBuffer(depth, DepthAccess.ReadWrite);
                        builder.AllowPassCulling(false);
                        pass.Owner = this; pass.Frame = Frame; pass.Width = width; pass.Height = height;
                        pass.Transparent = lists[0]; pass.Overlay = lists[1];
                        pass.Explicit=Explicit;
                        pass.View = Camera.worldToCameraMatrix;
                        // Installed CameraSetupPass supplies its logical
                        // ProjectionMatrix to this API. A separate GPU/Y flip
                        // here would transform the UI a second time.
                        pass.Projection = Camera.nonJitteredProjectionMatrix;
                        pass.RestoreProjection = Camera.projectionMatrix;
                        builder.SetRenderFunc<WorldHudPassData>(DrawWorldHud);
                        ++_worldHudPassRecords;
                    }
                }
                catch (Exception error) { FailWorldHud(error); }
            }
        }

        sealed class WorldHudPassData
        {
            internal WorldHudLatePass Owner;
            internal int Frame, Width, Height;
            internal RendererList Transparent, Overlay;
            internal List<WorldHudUiDraw> Explicit;
            internal Matrix4x4 View, Projection, RestoreProjection;
        }

        static void CountWorldHudListStatus(RendererListStatus status)
        {
            switch (status)
            {
                case RendererListStatus.kRendererListProcessing: ++_worldHudProcessingLists; break;
                case RendererListStatus.kRendererListEmpty: ++_worldHudEmptyLists; break;
                case RendererListStatus.kRendererListPopulated: ++_worldHudPopulatedLists; break;
                default: ++_worldHudInvalidLists; break;
            }
        }

        static void DrawWorldHud(WorldHudPassData pass, RenderGraphContext context)
        {
            var cmd = context.cmd;
            try
            {
                float width = pass.Width, height = pass.Height;
                cmd.SetViewport(new Rect(0, 0, width, height));
                cmd.SetViewProjectionMatrices(pass.View, pass.Projection);
                var parameters = new Vector4(width, height, 1 + 1 / width, 1 + 1 / height);
                cmd.SetGlobalVector(WorldHudScreenParams, parameters); cmd.SetGlobalVector(WorldHudScaledParams, parameters);
                cmd.SetGlobalVector(WorldHudScreenSize, new Vector4(width, height, 1 / width, 1 / height));
                cmd.SetGlobalVector(WorldHudMipBias, new Vector4(0, 1, 0, 0));
                // Main.EnsureOvertipOnTop already preserves ZTest Always on
                // these original materials. Fresh depth/stencil keeps UI masks
                // without attaching the low-resolution scene depth to this RT.
                if(pass.Explicit!=null&&pass.Explicit.Count>0)
                {
                    foreach(var item in pass.Explicit)
                    {
                        if(item.Graphic==null||CoverageSuppressed77(item.Graphic))continue;
                        if(item.Mesh==null||item.Material==null)throw new InvalidOperationException("Prepared native world UI resource expired before submission");
                        _worldHudProperties.Clear();
                        if(item.Texture!=null)_worldHudProperties.SetTexture(WorldHudMainTex,item.Texture);
                        _worldHudProperties.SetVector(WorldHudSampleAdd,item.SampleAdd);
                        cmd.DrawMesh(item.Mesh,item.Matrix,item.Material,item.Submesh,0,_worldHudProperties);
                        ++_worldHudExplicitDraws;
                    }
                }
                else {cmd.DrawRendererList(pass.Transparent); cmd.DrawRendererList(pass.Overlay);}
                pass.Owner.RenderedFrame = pass.Frame; ++_worldHudDrawSubmissions;
            }
            catch (Exception error) { FailWorldHud(error); }
            finally
            {
                cmd.SetViewProjectionMatrices(pass.View, pass.RestoreProjection);
                // Final resolve has completed: globals now describe its output.
                // The next camera owns SetCameraShaderVariablesPass. Do not use
                // Shader.GetGlobalVector here: it cannot read changes still
                // queued in this command buffer and would restore stale values.
            }
        }
    }
}
