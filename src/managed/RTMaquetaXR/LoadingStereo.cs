using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RTMaquetaXR
{
    // Tracking-space screen; artwork/fonts are borrowed from the running game.
    public static partial class Main
    {
        static LoadingTargetPair<RenderTexture> _loadingTargets;
        static RenderTexture _loadingAntialiasTarget, _loadingFontAtlas;
        static bool _loadingFontAtlasIsolated;
        static int _loadingGeometrySamples = 1;
        static int _loadingTargetWidth, _loadingTargetHeight, _loadingRequestedWidth, _loadingRequestedHeight;
        static int _loadingDeviceMaximum, _loadingTextRasterScale = 2;
        static long _loadingTargetRebuilds;
        static Material _loadingMaterial, _loadingLogoMaterial, _loadingFontMaterial;
        static MaterialPropertyBlock _loadingImageProperties, _loadingFontProperties;
        static bool _loadingSrgbWrite, _loadingColorReady, _loadingFontAlphaOnly;
        static int _loadingSampleAddId;
        static string _loadingFrameShader, _loadingLogoShader, _loadingTextShader, _loadingAtlasFormat;
        static Mesh _loadingTitle, _loadingDedication, _loadingSignature, _loadingStatus, _loadingBar, _loadingLogoMesh;
        const string LoadingCreditTitle = "MOD VR · By Beren5556";
        const string LoadingDedication = "Modded with love for the Warhammer 40K universe,\nand with gratitude to Owlcat Games for bringing its wonders to life.\nI hope you enjoy this new perspective on the game.";
        const string LoadingSignature = "Beren5556";
        static Sprite _loadingLogo;
        static Font _loadingFont;
        static TextGenerator _loadingTextGenerator;
        static CommandBuffer _loadingCommands;
        static bool _loadingPoseSet, _loadingFailed, _loadingFontDirty;
        static Vector3 _loadingPosition;
        static Quaternion _loadingRotation;
        static float _missingSceneSince = -1, _loadingNextAssets, _loadingNextText;
        static int _loadingAssetAttempts, _loadingFontRevision;
        static int _loadingLanguageRevision = -1;
        static string _loadingStatusText;
        static long _loadingStereoFrames;
        internal static void RecenterLoadingStereo() { _loadingPoseSet = false; }

        internal static bool LoadingStereoWanted(bool sceneRendered, bool panel, bool nativeLoading, bool valid, bool render, float now)
        {
            // A missed camera render is not a loading event. This flag comes
            // from the native view's Show/Hide lifecycle AND transition mode.
            // Never put an un-dismissible loading screen over a failed scene.
            if (sceneRendered || panel || !nativeLoading || !valid || !render)
            {
                _missingSceneSince = -1;
                // A native loading panel or a skipped XR frame can temporarily
                // replace this slate. Keep its anchor through the same load.
                if (!nativeLoading) _loadingPoseSet = false;
                return false;
            }
            if (_missingSceneSince < 0) _missingSceneSince = now;
            return true;
        }
        internal static bool RenderLoadingStereo(XrFrame frame, out RenderTexture left, out RenderTexture right)
        {
            left = right = null;
            if (_loadingFailed || frame.valid == 0 || frame.shouldRender == 0 || frame.serial == 0) return false;
            try
            {
                if (_loadingCommands == null)
                {
                    _loadingCommands = new CommandBuffer { name = "RTMaquetaXR loading stereo" };
                    _loadingDeviceMaximum = SystemInfo.maxTextureSize;
                    _loadingTargets = new LoadingTargetPair<RenderTexture>(CreateLoadingLeft, CreateLoadingRight, ReleaseLoadingTarget);
                    _loadingMaterial = CreateLiveUiMaterial("RTMaquetaXR loading frame");
                    _loadingMaterial.mainTexture = Texture2D.whiteTexture;
                    _loadingFontMaterial = CreateLiveUiMaterial("RTMaquetaXR loading typography");
                    _loadingLogoMaterial = CreateLiveUiMaterial("RTMaquetaXR game logo");
                    ConfigureLoadingColor();
                    _loadingFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    _loadingTextGenerator = new TextGenerator();
                    _loadingBar = LoadingQuad(.72f, .007f, new Color(.58f, .67f, .39f, 1));
                    Font.textureRebuilt += LoadingFontRebuilt;
                    _loadingFontDirty = true;
                }
                EnsureLoadingTargets(frame.width, frame.height);
                RefreshLoadingArtwork();
                RefreshLoadingText();
                if (!_loadingPoseSet)
                {
                    var forward = frame.head.Rotation * Vector3.forward; forward.y = 0;
                    if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
                    _loadingRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
                    _loadingPosition = frame.head.Position + forward.normalized * 1.5f;
                    _loadingPoseSet = true;
                }
                DrawLoadingEye(_loadingTargets.Left, frame.left);
                DrawLoadingEye(_loadingTargets.Right, frame.right);
                left = _loadingTargets.Left; right = _loadingTargets.Right; ++_loadingStereoFrames;
                return true;
            }
            catch (Exception error)
            {
                _loadingFailed = true;
                _log.Error("[loading] Custom display unavailable; native loading screen retained: " + error.Message);
                return false;
            }
        }
        static void RefreshLoadingArtwork()
        {
            if (_loadingLogo != null || _loadingAssetAttempts >= 4 || Time.unscaledTime < _loadingNextAssets) return;
            ++_loadingAssetAttempts; _loadingNextAssets = Time.unscaledTime + 2;
            // Exact sprite verified in installed Bundles/ui. Bounded attempts
            // only while loading renders; never scan during normal gameplay.
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                if (sprite != null && sprite.name == "logo-start_TEMP_UI")
                { _loadingLogo = sprite; _loadingLogoMesh = BuildLoadingLogo(sprite); _loadingLogoMaterial.mainTexture = sprite.texture; break; }
            foreach (var font in Resources.FindObjectsOfTypeAll<Font>())
                if (font != null && font.name == "TTSupermolotNeue-Medium")
                { _loadingFont = font; _loadingFontDirty = true; break; }
        }
        static void LoadingFontRebuilt(Font font)
        {
            if (font != _loadingFont) return;
            ++_loadingFontRevision;
            // Retained UVs belong to our private atlas snapshot. The game's
            // next font rebuild cannot invalidate them or animate static text.
            if (_loadingFontAtlas == null) _loadingFontDirty = true;
        }
        static void RefreshLoadingText()
        {
            if (_loadingLanguageRevision != ModLocalization.Revision) _loadingFontDirty = true;
            if (!_loadingFontDirty && Time.unscaledTime < _loadingNextText) return;
            _loadingNextText = Time.unscaledTime + .1f;
            string status = _loadingAwaitingInput ? ModLocalization.Text("READY  /  ENTERING THE GAME") :
                _loadingGameProgress >= 0 ? ModLocalization.Format("LOADING  {0}%", Mathf.FloorToInt(_loadingGameProgress * 100)) : ModLocalization.Text("LOADING YOUR GAME");
            if (!_loadingFontDirty && status == _loadingStatusText) return;
            if (_loadingFont == null) throw new InvalidOperationException("Loading font unavailable");
            // Static bitmap fonts cannot create denser glyphs; retain their
            // original physical sizing rather than shrinking an unchanged atlas.
            _loadingTextRasterScale = _loadingFont.dynamic ? LoadingQualityPolicy.TextRasterScale(_loadingTargetHeight) : 1;
            string title = ModLocalization.Text(LoadingCreditTitle), dedication = ModLocalization.Text(LoadingDedication);
            _loadingFont.RequestCharactersInTexture(title, 36 * _loadingTextRasterScale, FontStyle.Normal);
            _loadingFont.RequestCharactersInTexture(dedication, 21 * _loadingTextRasterScale, FontStyle.Italic);
            _loadingFont.RequestCharactersInTexture(LoadingSignature, 29 * _loadingTextRasterScale, FontStyle.Italic);
            _loadingFont.RequestCharactersInTexture(status, 25 * _loadingTextRasterScale, FontStyle.Normal);
            int revision;
            int attempts = 0;
            do {
                revision = _loadingFontRevision;
                ReplaceLoadingText(ref _loadingTitle, title, 36);
                ReplaceLoadingText(ref _loadingDedication, dedication, 21, FontStyle.Italic);
                ReplaceLoadingText(ref _loadingSignature, LoadingSignature, 29, FontStyle.Italic);
                ReplaceLoadingText(ref _loadingStatus, status, 25);
                ++attempts;
            } while (revision != _loadingFontRevision && attempts < 2);
            if (revision != _loadingFontRevision) throw new InvalidOperationException("Unstable loading font atlas");
            var atlas = _loadingFont.material.mainTexture as Texture2D;
            _loadingFontAlphaOnly = atlas != null && atlas.format == TextureFormat.Alpha8;
            _loadingAtlasFormat = atlas != null ? atlas.format.ToString() : "non-Texture2D";
            SnapshotLoadingFontAtlas(_loadingFont.material.mainTexture);
            // CanvasRenderer normally supplies this compiled uniform. These
            // direct mesh draws must not inherit a prior font/image's value.
            _loadingFontProperties.SetVector(_loadingSampleAddId, _loadingFontAlphaOnly ? new Vector4(1,1,1,0) : Vector4.zero);
            _loadingStatusText = status; _loadingFontDirty = false;
            _loadingLanguageRevision = ModLocalization.Revision;
        }
        static void SnapshotLoadingFontAtlas(Texture source)
        {
            if (source == null) throw new InvalidOperationException("Loading font has no atlas");
            if (_loadingFontAtlas == null || _loadingFontAtlas.width != source.width || _loadingFontAtlas.height != source.height)
            {
                ReleaseLoadingWork(ref _loadingFontAtlas);
                // Preserve sampled RGBA, including the alpha-only convention.
                // Mipmaps filter glyph coverage when large raster glyphs shrink
                // onto the loading plane. No scene TAA/DLSS or GPU readback.
                _loadingFontAtlas = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                { name = "RTMaquetaXR immutable loading font atlas", useMipMap = true, autoGenerateMips = false,
                    filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp, antiAliasing = 1 };
                if (!_loadingFontAtlas.Create()) throw new InvalidOperationException("Loading font snapshot allocation");
            }
            bool savedSrgb = GL.sRGBWrite; var savedTarget = RenderTexture.active;
            try { GL.sRGBWrite = false; Graphics.Blit(source, _loadingFontAtlas); _loadingFontAtlas.GenerateMips(); }
            finally { GL.sRGBWrite = savedSrgb; RenderTexture.active = savedTarget; }
            _loadingFontMaterial.mainTexture = _loadingFontAtlas;
            _loadingFontAtlasIsolated = true;
        }
        static void ReplaceLoadingText(ref Mesh target, string content, int size, FontStyle style = FontStyle.Normal)
        {
            if (target == null) target = new Mesh { name = "RTMaquetaXR loading text" };
            var settings = new TextGenerationSettings { font = _loadingFont, fontSize = size * _loadingTextRasterScale, fontStyle = style,
                color = new Color(.85f,.83f,.70f,1), scaleFactor = 1, lineSpacing = 1,
                textAnchor = TextAnchor.MiddleCenter, pivot = new Vector2(.5f,.5f),
                generationExtents = new Vector2(1200,100) * _loadingTextRasterScale, horizontalOverflow = HorizontalWrapMode.Overflow,
                verticalOverflow = VerticalWrapMode.Overflow, richText = false, updateBounds = true };
            _loadingTextGenerator.Invalidate();
            if (!_loadingTextGenerator.Populate(content, settings)) throw new InvalidOperationException("Loading text generation failed");
            var source = _loadingTextGenerator.verts;
            // This game's UnityEngine.UI.Text consumes every quad; it does
            // not append the old trailing dummy quad used by older Unity.
            int count = source.Count / 4 * 4;
            var vertices = new Vector3[count]; var uv = new Vector2[count]; var colors = new Color32[count];
            var indices = new int[count / 4 * 6];
            float unitsPerRasterPixel = .001f / _loadingTextRasterScale;
            for (int i = 0; i < count; ++i) { vertices[i] = source[i].position * unitsPerRasterPixel; uv[i] = source[i].uv0; colors[i] = source[i].color; }
            for (int q = 0; q < count / 4; ++q) { int a=q*4,b=q*6; indices[b]=a;indices[b+1]=a+1;indices[b+2]=a+2;indices[b+3]=a+2;indices[b+4]=a+3;indices[b+5]=a; }
            target.Clear(); target.vertices=vertices;target.uv=uv;target.colors32=colors;target.triangles=indices;target.RecalculateBounds();
        }
        static RenderTexture NewLoadingTarget(string name, int width, int height)
        {
            var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1, bindTextureMS = false, useMipMap = false, autoGenerateMips = false };
            if (!target.Create()) { UnityEngine.Object.Destroy(target); throw new InvalidOperationException("Loading target allocation"); }
            return target;
        }
        static RenderTexture CreateLoadingLeft(int width, int height) => NewLoadingTarget("RTMaquetaXR loading left", width, height);
        static RenderTexture CreateLoadingRight(int width, int height) => NewLoadingTarget("RTMaquetaXR loading right", width, height);
        static void EnsureLoadingTargets(int headsetWidth, int headsetHeight)
        {
            if (!LoadingQualityPolicy.TrySize(headsetWidth, headsetHeight, _loadingDeviceMaximum, out int width, out int height))
                throw new InvalidOperationException("Loading target dimensions unavailable");
            _loadingRequestedWidth = headsetWidth; _loadingRequestedHeight = headsetHeight;
            if (!_loadingTargets.Ensure(width, height)) return;
            ReleaseLoadingWork(ref _loadingAntialiasTarget);
            _loadingGeometrySamples = 1;
            var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0) { msaaSamples = 2, sRGB = true };
            if (SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor) >= 2)
            {
                // One shared scratch surface is resolved before the other eye
                // uses it. Native source textures remain single-sample.
                _loadingAntialiasTarget = new RenderTexture(descriptor)
                { name = "RTMaquetaXR loading geometry AA", bindTextureMS = true, filterMode = FilterMode.Bilinear };
                if (!_loadingAntialiasTarget.Create()) ReleaseLoadingWork(ref _loadingAntialiasTarget);
                else _loadingGeometrySamples = _loadingAntialiasTarget.antiAliasing;
            }
            _loadingTargetWidth = width; _loadingTargetHeight = height; ++_loadingTargetRebuilds;
            _loadingTextRasterScale = LoadingQualityPolicy.TextRasterScale(height);
            _loadingFontDirty = true;
            _log.Log("[loading/image] " + width + "x" + height + " per eye; requested=" + headsetWidth + "x" + headsetHeight +
                "; text raster=" + _loadingTextRasterScale + "x; independent of scene DLSS");
        }
        static void ReleaseLoadingTarget(RenderTexture target)
        {
            if (target == null) return;
            try { OpenXR.ForgetTexture(target); target.Release(); }
            finally { UnityEngine.Object.Destroy(target); }
        }
        static void DrawLoadingEye(RenderTexture target, XrView eye)
        {
            const float near = .02f, far = 20;
            var projection = Matrix4x4.Frustum(Mathf.Tan(eye.fov.left) * near, Mathf.Tan(eye.fov.right) * near,
                Mathf.Tan(eye.fov.down) * near, Mathf.Tan(eye.fov.up) * near, near, far);
            var view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(eye.pose.Position, eye.pose.Rotation, Vector3.one).inverse;
            var command = _loadingCommands; command.Clear();
            var work = _loadingAntialiasTarget != null ? _loadingAntialiasTarget : target;
            command.SetRenderTarget(work); command.SetViewport(new Rect(0, 0, target.width, target.height));
            command.ClearRenderTarget(false, true, Color.black);
            command.SetViewProjectionMatrices(view, projection);
            var matrix = Matrix4x4.TRS(_loadingPosition, _loadingRotation, Vector3.one);
            // No surrounding frame: only the original logo, dedication and
            // progress appear over the untouched, opaque black clear.
            if (_loadingLogoMesh != null) command.DrawMesh(_loadingLogoMesh, matrix * Matrix4x4.Translate(new Vector3(0,.24f,0)), _loadingLogoMaterial, 0, 0, _loadingImageProperties);
            command.DrawMesh(_loadingTitle, matrix * Matrix4x4.Translate(new Vector3(0,-.095f,0)), _loadingFontMaterial, 0, 0, _loadingFontProperties);
            command.DrawMesh(_loadingDedication, matrix * Matrix4x4.Translate(new Vector3(0,-.225f,0)), _loadingFontMaterial, 0, 0, _loadingFontProperties);
            command.DrawMesh(_loadingSignature, matrix * Matrix4x4.Translate(new Vector3(0,-.355f,0)), _loadingFontMaterial, 0, 0, _loadingFontProperties);
            command.DrawMesh(_loadingStatus, matrix * Matrix4x4.Translate(new Vector3(0,-.49f,0)), _loadingFontMaterial, 0, 0, _loadingFontProperties);
            float fill = _loadingAwaitingInput ? 1 : Mathf.Clamp01(_loadingGameProgress);
            if (_loadingGameProgress < 0 && !_loadingAwaitingInput) fill = .12f;
            float barX = _loadingGameProgress < 0 && !_loadingAwaitingInput ? Mathf.Sin(Time.unscaledTime * 1.2f) * .28f : -.36f * (1-fill);
            command.DrawMesh(_loadingBar, matrix * Matrix4x4.TRS(new Vector3(barX,-.425f,0),Quaternion.identity,new Vector3(fill,1,1)), _loadingMaterial, 0, 0, _loadingImageProperties);
            if (work != target) command.ResolveAntiAliasedSurface(work, target);
            ExecuteLoadingCommands();
        }
        static void ConfigureLoadingColor()
        {
            _loadingFrameShader = _loadingMaterial.shader.name;
            _loadingLogoShader = _loadingLogoMaterial.shader.name;
            _loadingTextShader = _loadingFontMaterial.shader.name;
            bool linearProject = QualitySettings.activeColorSpace == ColorSpace.Linear;
            if (!LoadingColorPolicy.TrySrgbWrite(_loadingFrameShader, linearProject, out _loadingSrgbWrite) ||
                !LoadingColorPolicy.TrySrgbWrite(_loadingLogoShader, linearProject, out bool logoWrite) ||
                !LoadingColorPolicy.TrySrgbWrite(_loadingTextShader, linearProject, out bool textWrite) ||
                logoWrite != _loadingSrgbWrite || textWrite != _loadingSrgbWrite)
                throw new InvalidOperationException("Loading shaders have an unsupported color-transfer contract");
            _loadingSampleAddId = Shader.PropertyToID("_TextureSampleAdd");
            _loadingImageProperties = new MaterialPropertyBlock();
            _loadingFontProperties = new MaterialPropertyBlock();
            // Not a ShaderLab property in these shaders, but a verified DXBC
            // uniform; HasProperty would incorrectly skip this required value.
            _loadingImageProperties.SetVector(_loadingSampleAddId, Vector4.zero);
            _loadingFontProperties.SetVector(_loadingSampleAddId, Vector4.zero);
            _loadingColorReady = true;
            _log.Log("[loading/color] frame=" + _loadingFrameShader + "; logo=" + _loadingLogoShader +
                "; text=" + _loadingTextShader + "; srgbWrite=" + _loadingSrgbWrite + "; explicit texture sample addition");
        }
        static void ExecuteLoadingCommands()
        {
            // Owlcat/UI/Default already encodes linear RGB in its shipped PS.
            // Keep sRGB-tagged targets for the native source/compositor contract,
            // suppress only the redundant write conversion, and restore Unity.
            bool previous = GL.sRGBWrite;
            try { GL.sRGBWrite = _loadingSrgbWrite; Graphics.ExecuteCommandBuffer(_loadingCommands); }
            finally { GL.sRGBWrite = previous; }
        }
        internal static object LoadingColorSnapshot() => new {
            Ready = _loadingColorReady, FrameShader = _loadingFrameShader, LogoShader = _loadingLogoShader,
            TextShader = _loadingTextShader, SrgbWrite = _loadingSrgbWrite,
            AtlasFormat = _loadingAtlasFormat, AlphaOnlyAtlas = _loadingFontAlphaOnly,
            ExplicitTextureSampleAdd = _loadingColorReady, BlackBackground = true,
            OutputWidth = _loadingTargetWidth, OutputHeight = _loadingTargetHeight,
            RequestedWidth = _loadingRequestedWidth, RequestedHeight = _loadingRequestedHeight,
            TextRasterScale = _loadingTextRasterScale, TargetRebuilds = _loadingTargetRebuilds,
            FontAtlasIsolated = _loadingFontAtlasIsolated, FontMipmaps = _loadingFontAtlasIsolated,
            GeometrySamples = _loadingGeometrySamples,
            SceneUpscalerUsed = false
        };
        static Mesh BuildLoadingLogo(Sprite sprite)
        {
            var points=sprite.vertices;var vertices=new Vector3[points.Length];var colors=new Color32[points.Length];
            float scale=.98f/Mathf.Max(.001f,sprite.bounds.size.x);
            for(int i=0;i<points.Length;++i){vertices[i]=((Vector3)points[i]-sprite.bounds.center)*scale;colors[i]=Color.white;}
            var source=sprite.triangles;var triangles=new int[source.Length];for(int i=0;i<source.Length;++i)triangles[i]=source[i];
            var mesh=new Mesh{name="RTMaquetaXR borrowed game logo"};mesh.vertices=vertices;mesh.uv=sprite.uv;mesh.colors32=colors;mesh.triangles=triangles;mesh.RecalculateBounds();return mesh;
        }
        static void LoadingStroke(List<Vector3> vertices, List<int> triangles, Vector2 a, Vector2 b, float thickness)
        {
            var delta = (b-a).normalized; var side = new Vector2(-delta.y,delta.x)*(thickness*.5f);int start=vertices.Count;
            vertices.Add(a+side);vertices.Add(b+side);vertices.Add(b-side);vertices.Add(a-side);
            triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);triangles.Add(start);triangles.Add(start+2);triangles.Add(start+3);
        }
        static Mesh LoadingMesh(List<Vector3> vertices,List<int> triangles,string name,Color color)
        {
            var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);
            var colors=new Color32[vertices.Count];for(int i=0;i<colors.Length;++i)colors[i]=color;
            mesh.colors32=colors;mesh.uv=new Vector2[vertices.Count];mesh.RecalculateBounds();return mesh;
        }
        static Mesh LoadingQuad(float width,float height,Color color)
        {
            var v=new List<Vector3>();var t=new List<int>();LoadingStroke(v,t,new Vector2(-width/2,0),new Vector2(width/2,0),height);
            return LoadingMesh(v,t,"RTMaquetaXR loading progress",color);
        }
        internal static void ReleaseLoadingStereo()
        {
            _loadingTargets?.Clear(); _loadingTargets = null;
            ReleaseLoadingWork(ref _loadingAntialiasTarget); ReleaseLoadingWork(ref _loadingFontAtlas);
            _loadingFontAtlasIsolated = false; _loadingGeometrySamples = 1;
            _loadingTargetWidth = _loadingTargetHeight = _loadingRequestedWidth = _loadingRequestedHeight = 0;
            _loadingDeviceMaximum = 0; _loadingTextRasterScale = 2;
            Font.textureRebuilt -= LoadingFontRebuilt;
            if (_loadingCommands != null) _loadingCommands.Release(); _loadingCommands = null;
            foreach(var item in new UnityEngine.Object[]{_loadingMaterial,_loadingFontMaterial,_loadingLogoMaterial,_loadingTitle,_loadingDedication,_loadingSignature,_loadingStatus,_loadingBar,_loadingLogoMesh})
                if(item!=null)UnityEngine.Object.Destroy(item);
            _loadingMaterial=_loadingFontMaterial=_loadingLogoMaterial=null;
            _loadingImageProperties=_loadingFontProperties=null;
            _loadingColorReady=_loadingSrgbWrite=_loadingFontAlphaOnly=false;
            _loadingFrameShader=_loadingLogoShader=_loadingTextShader=_loadingAtlasFormat=null;
            _loadingTitle=_loadingDedication=_loadingSignature=_loadingStatus=_loadingBar=_loadingLogoMesh=null;
            if (_loadingTextGenerator != null) ((IDisposable)_loadingTextGenerator).Dispose();
            _loadingTextGenerator=null;_loadingFont=null;_loadingLogo=null;
            _loadingPoseSet=_loadingFailed=false;_missingSceneSince=-1;_loadingNextAssets=_loadingNextText=0;_loadingAssetAttempts=0;_loadingStatusText=null;
        }
        static void ReleaseLoadingWork(ref RenderTexture target)
        { if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); } target = null; }
    }
}
