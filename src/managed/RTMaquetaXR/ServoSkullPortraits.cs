using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    // Images are made from the same borrowed meshes/albedo as the held skulls.
    // Only the first visible help view bakes them; animation moves cached quads.
    public static partial class Main
    {
        internal const int ServoPortraitResolution = 512;
        internal const float ServoPortraitHalfSpan = .064f;
        internal const float ServoPortraitYaw = 18f;
        static RenderTexture _servoPortraitLeft, _servoPortraitRight;
        static float _servoPortraitRetry;
        static int _servoPortraitFailures;
        static long _servoPortraitBakes;
        static bool _servoPortraitReady;

        internal static bool EnsureServoSkullPortraits()
        {
            if (_servoPortraitLeft != null && _servoPortraitRight != null) return true;
            if (_servoPortraitFailures >= 3 || Time.unscaledTime < _servoPortraitRetry) return false;
            if (!TryGetServoSkullForPortraits(out GameServoSkullPart[] parts)) return false;
            RenderTexture left = null, right = null;
            try
            {
                left = BakeServoSkullPortrait(parts, -ServoPortraitYaw, "left");
                right = BakeServoSkullPortrait(parts, ServoPortraitYaw, "right");
                _servoPortraitLeft = left; _servoPortraitRight = right; ++_servoPortraitBakes;
                _servoPortraitReady = true;
                _log.Log("[presentation/skulls] Two cached 512px portraits of original ServoSkull_02; no recurring model render.");
                return true;
            }
            catch (Exception error)
            {
                ReleaseServoPortrait(left); ReleaseServoPortrait(right);
                ++_servoPortraitFailures; _servoPortraitRetry = Time.unscaledTime + 3;
                _log.Error("[presentation/skulls] Portrait unavailable; controls remain usable: " + error.Message);
                return false;
            }
        }
        static RenderTexture BakeServoSkullPortrait(GameServoSkullPart[] parts, float yaw, string side)
        {
            Material material = null; RenderTexture work = null, result = null; CommandBuffer command = null;
            try
            {
                material = new Material(parts[0].Material) { name = "RTMaquetaXR portrait material" };
                int depthPass = material.FindPass("GBUFFER"), colorPass = material.FindPass("Unlit");
                if (depthPass < 0 || colorPass < 0) throw new InvalidOperationException("Original model's unlit pass unavailable");
                var descriptor = new RenderTextureDescriptor(ServoPortraitResolution, ServoPortraitResolution, RenderTextureFormat.ARGB32, 24)
                { msaaSamples = 4, sRGB = true };
                int samples = Math.Max(1, SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor));
                work = NewFlatHandTarget("RTMaquetaXR portrait AA", ServoPortraitResolution, ServoPortraitResolution, samples, 24);
                result = new RenderTexture(ServoPortraitResolution, ServoPortraitResolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                { name = "RTMaquetaXR original skull portrait " + side, antiAliasing = 1, useMipMap = true,
                    autoGenerateMips = false, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
                if (!result.Create()) throw new InvalidOperationException("Portrait allocation");
                Matrix4x4 turn = Matrix4x4.Rotate(Quaternion.Euler(0, yaw, 0));
                // Crop the real head, green screen and upper conduits. The long
                // hanging cable would make the face illegible in a small icon.
                Vector3 center = turn.MultiplyPoint3x4(new Vector3(-.0125f, -.003f, .02f));
                Vector3 camera = center + Vector3.forward * .5f;
                Quaternion facing = Quaternion.LookRotation(-Vector3.forward, Vector3.up);
                var view = Matrix4x4.Scale(new Vector3(1,1,-1)) * Matrix4x4.TRS(camera, facing, Vector3.one).inverse;
                var projection = Matrix4x4.Ortho(-ServoPortraitHalfSpan, ServoPortraitHalfSpan, -ServoPortraitHalfSpan, ServoPortraitHalfSpan, .01f, 2);
                float fow = Shader.GetGlobalFloat(TouchHandFowId), volume = Shader.GetGlobalFloat(TouchHandVolumeId), mip = Shader.GetGlobalFloat(TouchHandMipBiasId);
                Vector4 fog = Shader.GetGlobalVector(TouchHandFogParamsId), fogColor = Shader.GetGlobalVector(TouchHandFogColorId), oldCamera = Shader.GetGlobalVector(TouchHandCameraId);
                command = new CommandBuffer { name = "RTMaquetaXR bake original servo-skull portrait once" };
                command.SetRenderTarget(work); command.SetViewport(new Rect(0,0,ServoPortraitResolution,ServoPortraitResolution));
                // The UI consumes straight-alpha images, whereas resolving an
                // opaque mesh against transparent black produces premultiplied
                // edge RGB. An opaque black photographic backing keeps alpha
                // one through MSAA and mipmaps, avoiding a second alpha multiply
                // in either native UI shader. Original albedo remains untinted.
                command.ClearRenderTarget(true, true, TouchGuidePalette.Surface); command.SetViewProjectionMatrices(view, projection);
                command.SetGlobalFloat(TouchHandFowId,0); command.SetGlobalFloat(TouchHandVolumeId,0); command.SetGlobalFloat(TouchHandMipBiasId,0);
                command.SetGlobalVector(TouchHandFogParamsId,new Vector4(0,0,0,1)); command.SetGlobalVector(TouchHandFogColorId,Vector4.zero); command.SetGlobalVector(TouchHandCameraId,camera);
                foreach (var part in parts) command.DrawMesh(part.Mesh,turn * part.LocalToHand,material,0,depthPass);
                command.ClearRenderTarget(false,true,TouchGuidePalette.Surface);
                foreach (var part in parts) command.DrawMesh(part.Mesh,turn * part.LocalToHand,material,0,colorPass);
                command.SetGlobalFloat(TouchHandFowId,fow); command.SetGlobalFloat(TouchHandVolumeId,volume); command.SetGlobalFloat(TouchHandMipBiasId,mip);
                command.SetGlobalVector(TouchHandFogParamsId,fog); command.SetGlobalVector(TouchHandFogColorId,fogColor); command.SetGlobalVector(TouchHandCameraId,oldCamera);
                if (samples > 1) command.ResolveAntiAliasedSurface(work,result); else command.CopyTexture(work,0,0,result,0,0);
                bool savedSrgb = GL.sRGBWrite; var savedTarget = RenderTexture.active;
                try { GL.sRGBWrite = false; Graphics.ExecuteCommandBuffer(command); result.GenerateMips(); }
                finally { GL.sRGBWrite = savedSrgb; RenderTexture.active = savedTarget; }
                var complete = result; result = null; return complete;
            }
            finally
            {
                if (command != null) command.Release();
                if (material != null) UnityEngine.Object.Destroy(material);
                ReleaseServoPortrait(work); ReleaseServoPortrait(result);
            }
        }
        static void ReleaseServoPortrait(RenderTexture target)
        { if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); } }
        internal static void ReleaseServoSkullPortraits()
        {
            ReleaseServoPortrait(_servoPortraitLeft); ReleaseServoPortrait(_servoPortraitRight);
            _servoPortraitLeft = _servoPortraitRight = null; _servoPortraitFailures = 0; _servoPortraitRetry = 0;
            _servoPortraitReady = false;
        }
        internal static RawImage CreateServoPortrait(Transform parent, bool left, Material material)
        {
            var obj = new GameObject(left ? "Original ServoSkull_02 left portrait" : "Original ServoSkull_02 right portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            obj.layer = 5; obj.transform.SetParent(parent,false);
            var image = obj.GetComponent<RawImage>(); image.raycastTarget = false; image.material = material;
            image.enabled = false; return image;
        }
        internal static void PlaceServoPortrait(RawImage image, bool left, Vector2 position, float size, bool active)
        {
            if (image == null) return;
            var texture = left ? _servoPortraitLeft : _servoPortraitRight;
            bool ready = texture != null;
            if (image.enabled != ready) image.enabled = ready;
            if (image.texture != texture) image.texture = texture;
            // Active-hand feedback belongs to the surrounding cyan marker.
            // Multiplying this small image by grey destroyed readable detail.
            var color = Color.white;
            if (image.color != color) image.color = color;
            if (image.rectTransform.anchoredPosition != position) image.rectTransform.anchoredPosition = position;
            var dimensions = new Vector2(size,size);
            if (image.rectTransform.sizeDelta != dimensions) image.rectTransform.sizeDelta = dimensions;
        }
        internal static object ServoSkullPortraitSnapshot() => new {
            Ready = _servoPortraitReady, Bakes = _servoPortraitBakes,
            Resolution = ServoPortraitResolution, OriginalAsset = GameServoSkullAsset, Failures = _servoPortraitFailures,
            PerFrameModelRender = false
        };
    }
}
