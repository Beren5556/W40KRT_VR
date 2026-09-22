using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static GameObject _touchBoxVisualRoot;
        static Material _touchBoxMaterial;
        static LineRenderer _touchBoxBorder;
        static readonly List<LineRenderer> _touchBoxFootprints = new List<LineRenderer>(16);
        static Camera _touchBoxEyeLeft, _touchBoxEyeRight;
        static bool _touchBoxVisualActive;

        internal static void UpdateTouchSelectionVisuals(Camera left, Camera right)
        {
            try { UpdateTouchSelectionVisualsCore(left, right); }
            catch (System.Exception error)
            {
                // Never continue an invisible drag or fail the stereo renderer
                // because the optional selection material could not be built.
                CancelTouchSelection(); DestroyTouchSelectionVisuals();
                _log.Error("[touch/selection-visual] " + error.Message);
            }
        }
        static void UpdateTouchSelectionVisualsCore(Camera left, Camera right)
        {
            _touchBoxEyeLeft = left; _touchBoxEyeRight = right;
            _touchBoxVisualActive = TouchInputOwned && _touchSampleValid && _touchBox.Active && !TouchOverlayOpen &&
                !_modeFlat && left != null && right != null;
            if (!_touchBoxVisualActive) { HideTouchSelectionVisuals(); return; }
            if (_touchBoxVisualRoot == null)
            {
                _touchBoxMaterial = CreateLiveUiMaterial("RTMaquetaXR scene selection");
                _touchBoxVisualRoot = new GameObject("RTMaquetaXR Touch scene selection");
                _touchBoxVisualRoot.layer = 5; UnityEngine.Object.DontDestroyOnLoad(_touchBoxVisualRoot);
                _touchBoxBorder = CreateTouchSelectionLine("Selection border", new Color(.25f, .95f, 1f, .95f));
                RenderPipelineManager.beginCameraRendering += TouchSelectionBeginCamera;
            }
            Vector3 lift = TableVector(_touchBoxPlane.Normal) * (WorldScale * .0015f);
            _touchBoxBorder.startWidth = _touchBoxBorder.endWidth = WorldScale * .0014f;
            for (int corner = 0; corner < 4; ++corner)
                _touchBoxBorder.SetPosition(corner, TableVector(_touchBoxPlane.World(_touchBoxRectangle.Corner(corner))) + lift);
            int count = 0;
            for (int i = 0; i < _touchBoxUnits.Count; ++i)
            {
                var candidate = _touchBoxUnits[i]; if (!candidate.Selected || candidate.View == null) continue;
                if (count == _touchBoxFootprints.Count) _touchBoxFootprints.Add(CreateTouchSelectionLine("Included unit footprint", new Color(.38f, 1f, .32f, .95f)));
                var line = _touchBoxFootprints[count++];
                line.startWidth = line.endWidth = WorldScale * .0018f;
                Vector3 center = candidate.Footprint.center, extent = candidate.Footprint.extents;
                for (int corner = 0; corner < 4; ++corner)
                {
                    Vector3 p = center + new Vector3(corner == 0 || corner == 3 ? -extent.x : extent.x, 0, corner < 2 ? -extent.z : extent.z);
                    line.SetPosition(corner, TableVector(_touchBoxPlane.World(_touchBoxPlane.Project(ToPoint(p)))) + lift * 1.5f);
                }
            }
            for (int i = 0; i < _touchBoxFootprints.Count; ++i) _touchBoxFootprints[i].enabled = i < count;
            _touchBoxBorder.enabled = true;
        }

        static LineRenderer CreateTouchSelectionLine(string name, Color color)
        {
            var go = new GameObject(name, typeof(LineRenderer)); go.layer = 5;
            go.transform.SetParent(_touchBoxVisualRoot.transform, false);
            var line = go.GetComponent<LineRenderer>(); line.sharedMaterial = _touchBoxMaterial;
            line.useWorldSpace = true; line.loop = true; line.positionCount = 4;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off; line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.startColor = line.endColor = color; line.numCornerVertices = 2;
            return line;
        }

        static void TouchSelectionBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            bool visible = _touchBoxVisualActive && (camera == _touchBoxEyeLeft || camera == _touchBoxEyeRight);
            if (_touchBoxBorder != null) _touchBoxBorder.enabled = visible;
            for (int i = 0; i < _touchBoxFootprints.Count; ++i) _touchBoxFootprints[i].enabled = visible && i < _touchBoxCandidateCount;
        }
        static void HideTouchSelectionVisuals()
        {
            _touchBoxVisualActive = false;
            if (_touchBoxBorder != null) _touchBoxBorder.enabled = false;
            for (int i = 0; i < _touchBoxFootprints.Count; ++i) _touchBoxFootprints[i].enabled = false;
        }
        static void DestroyTouchSelectionVisuals()
        {
            RenderPipelineManager.beginCameraRendering -= TouchSelectionBeginCamera;
            if (_touchBoxVisualRoot != null) UnityEngine.Object.Destroy(_touchBoxVisualRoot);
            if (_touchBoxMaterial != null) UnityEngine.Object.Destroy(_touchBoxMaterial);
            _touchBoxVisualRoot = null; _touchBoxBorder = null; _touchBoxMaterial = null;
            _touchBoxEyeLeft = _touchBoxEyeRight = null; _touchBoxFootprints.Clear(); _touchBoxVisualActive = false;
        }
    }
}
