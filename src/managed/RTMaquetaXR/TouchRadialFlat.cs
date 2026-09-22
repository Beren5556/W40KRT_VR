using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Camera _radialFlatLeft, _radialFlatRight;
        static bool _radialFlatDrawing;
        static readonly MaterialPropertyBlock _radialFlatProperties = new MaterialPropertyBlock();
        static Graphic[] _radialFlatGraphics;
        static Transform _radialFlatGraphicsRoot;
        static long _radialFlatLayout = -1;
        static int _radialFlatPreparedFrame = -1;
        static readonly int RadialMainTexture = Shader.PropertyToID("_MainTex");
        internal static bool FlatRadialOwnsCanvas79 => _modeFlat && _touchRadialShown &&
            _radialFlatPreparedFrame == Time.frameCount;

        // Navigation/management remain native flat panels, while the wheel is
        // drawn into the independent, antialiased OpenXR controller layers.
        // No game camera rendering, HUD scaling or neural reconstruction here.
        internal static void PrepareFlatRadial(XrFrame frame, bool afterCanvas = false)
        {
            if (!_touchRadial.Visible || _radialFlatPreparedFrame == Time.frameCount) return;
            if (_radialFlatLeft == null)
            {
                _radialFlatLeft = FlatRadialCamera("RTVR left wheel reference");
                _radialFlatRight = FlatRadialCamera("RTVR right wheel reference");
            }
            ConfigureFlatRadialCamera79(_radialFlatLeft, frame.left);
            ConfigureFlatRadialCamera79(_radialFlatRight, frame.right);
            SetTouchSpatialFrame(frame, frame.head.Position * WorldScale, frame.head.Rotation);
            _radialFlatDrawing = true;
            try { UpdateTouchRadialVisuals(_radialFlatLeft, _radialFlatRight); }
            finally { _radialFlatDrawing = false; }
            if (_touchRadialRoot == null || !_touchRadialShown) return;
            if (_radialFlatGraphicsRoot != _touchRadialRoot.transform || _radialFlatLayout != _touchRadialLayoutBuilds)
            {
                _radialFlatGraphicsRoot = _touchRadialRoot.transform;
                _radialFlatLayout = _touchRadialLayoutBuilds;
                _radialFlatGraphics = _touchRadialRoot.GetComponentsInChildren<Graphic>(true);
                // Direct mesh draws ignore layers; native map cameras must not
                // also flatten this physical wheel into their captured panel.
                foreach (var node in _touchRadialRoot.GetComponentsInChildren<Transform>(true))
                    node.gameObject.layer = _spatialLayer;
            }
            _radialFlatPreparedFrame = Time.frameCount;
            // Normally prepared in LateUpdate, before Unity builds canvases.
            // Only an exceptional late preparation needs this local fallback.
            if (afterCanvas) foreach (var graphic in _radialFlatGraphics)
                if (graphic != null && graphic.isActiveAndEnabled) graphic.Rebuild(CanvasUpdate.PreRender);
        }
        static Camera FlatRadialCamera(string name)
        {
            var root = new GameObject(name, typeof(Camera));
            Object.DontDestroyOnLoad(root);
            var camera = root.GetComponent<Camera>(); camera.enabled = false;
            camera.nearClipPlane = .01f * WorldScale; camera.farClipPlane = 5f * WorldScale;
            return camera;
        }
        static void ConfigureFlatRadialCamera79(Camera camera, XrView eye)
        {
            float near = .02f * WorldScale, far = 20f * WorldScale;
            camera.transform.SetPositionAndRotation(eye.pose.Position * WorldScale, eye.pose.Rotation);
            camera.orthographic = false;
            camera.nearClipPlane = near; camera.farClipPlane = far;
            camera.projectionMatrix = Matrix4x4.Frustum(Mathf.Tan(eye.fov.left) * near, Mathf.Tan(eye.fov.right) * near,
                Mathf.Tan(eye.fov.down) * near, Mathf.Tan(eye.fov.up) * near, near, far);
        }
        static void DrawFlatRadial(CommandBuffer command)
        {
            if (!_touchRadial.Visible || !_touchRadialShown || _radialFlatGraphics == null) return;
            var tracking = Matrix4x4.Scale(Vector3.one / WorldScale);
            foreach (var graphic in _radialFlatGraphics)
            {
                if (graphic == null || !graphic.isActiveAndEnabled || graphic.canvasRenderer.cull) continue;
                var mesh = graphic.canvasRenderer.GetMesh(); // Native borrowed mesh; no new Mesh allocation.
                if (mesh == null || mesh.vertexCount == 0) continue;
                _radialFlatProperties.Clear(); _radialFlatProperties.SetTexture(RadialMainTexture, graphic.mainTexture);
                command.DrawMesh(mesh, tracking * graphic.transform.localToWorldMatrix,
                    graphic.materialForRendering, 0, 0, _radialFlatProperties);
            }
        }
        static void DestroyFlatRadial()
        {
            if (_radialFlatLeft != null) Object.Destroy(_radialFlatLeft.gameObject);
            if (_radialFlatRight != null) Object.Destroy(_radialFlatRight.gameObject);
            _radialFlatLeft = _radialFlatRight = null; _radialFlatGraphics = null; _radialFlatGraphicsRoot = null;
            _radialFlatPreparedFrame = -1;
        }
    }
}
