using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    internal sealed class TouchGuideGraphic : Graphic
    {
        internal TouchGuideAnimation Animation;
        internal Color Frame, Ink, Accent;
        RawImage leftPortrait, rightPortrait;
        TouchGuideGraphic annotations;
        TouchGuideIconGraphic icons,keys;
        bool annotationLayer;
        internal void RefreshPortraits()
        {
            if (Animation == null || annotationLayer) return;

            if (leftPortrait == null) leftPortrait = TouchControllerArtwork.Create(transform,true,material);
            if (rightPortrait == null) rightPortrait = TouchControllerArtwork.Create(transform,false,material);
            float portraitY=TouchGuideAnimation.Height*.5f-TouchGuideAnimation.PortraitTop-TouchGuideAnimation.PortraitSize*.5f;
            TouchControllerArtwork.Place(leftPortrait,true,new Vector2(Animation.LeftPortraitX-WidthHalf,portraitY),TouchGuideAnimation.PortraitSize,Animation.LeftPortraitActive);
            TouchControllerArtwork.Place(rightPortrait,false,new Vector2(Animation.RightPortraitX-WidthHalf,portraitY),TouchGuideAnimation.PortraitSize,Animation.RightPortraitActive);
            // Opaque portraits prevent a second alpha multiplication at their
            // edges. Draw gesture annotations after them, in their own child.
            // Keeping every layer under this root also preserves hide/destroy.
            if (annotations == null)
            {
                var obj = new GameObject("Gesture annotations",typeof(RectTransform),typeof(CanvasRenderer),typeof(TouchGuideGraphic));
                obj.layer = 5;
                var rect = (RectTransform)obj.transform; rect.SetParent(transform,false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f);
                rect.sizeDelta = new Vector2(TouchGuideAnimation.Width,TouchGuideAnimation.Height);
                annotations = obj.GetComponent<TouchGuideGraphic>(); annotations.annotationLayer = true;
                annotations.raycastTarget = false; annotations.material = material;
            }
            annotations.Animation = Animation; annotations.Frame = Frame; annotations.Ink = Ink; annotations.Accent = Accent;
            annotations.SetVerticesDirty();
            if(icons==null)icons=CreateIcons(false);if(keys==null)keys=CreateIcons(true);
            icons.Animation=keys.Animation=Animation;icons.SetVerticesDirty();keys.SetVerticesDirty();
        }
        TouchGuideIconGraphic CreateIcons(bool keyboard)
        {
            var obj=new GameObject(keyboard?"Approved keyboard icons":"Approved Touch icons",typeof(RectTransform),typeof(CanvasRenderer),typeof(TouchGuideIconGraphic));
            obj.layer=5;var rect=(RectTransform)obj.transform;rect.SetParent(transform,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one*.5f;rect.sizeDelta=new Vector2(TouchGuideAnimation.Width,TouchGuideAnimation.Height);
            var graphic=obj.GetComponent<TouchGuideIconGraphic>();graphic.Keys=keyboard;graphic.material=material;graphic.raycastTarget=false;return graphic;
        }
        const float WidthHalf = TouchGuideAnimation.Width/2;
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear(); if (Animation == null || !annotationLayer) return;
            for (int i = 0; i < Animation.Count; ++i)
            {
                var stroke = Animation.Strokes[i];
                var start = new Vector2(stroke.X0 - TouchGuideAnimation.Width / 2, TouchGuideAnimation.Height / 2 - stroke.Y0);
                var end = new Vector2(stroke.X1 - TouchGuideAnimation.Width / 2, TouchGuideAnimation.Height / 2 - stroke.Y1);
                var delta = end - start; if (delta.sqrMagnitude < .00001f) continue;
                var side = new Vector2(-delta.y, delta.x).normalized * (stroke.Width * .5f);
                Color32 color = stroke.Tone == 0 ? Frame : stroke.Tone == 1 ? Ink : Accent;
                int vertex = helper.currentVertCount;
                helper.AddVert(start - side, color, Vector2.zero); helper.AddVert(start + side, color, Vector2.zero);
                helper.AddVert(end + side, color, Vector2.zero); helper.AddVert(end - side, color, Vector2.zero);
                helper.AddTriangle(vertex, vertex + 1, vertex + 2); helper.AddTriangle(vertex, vertex + 2, vertex + 3);
            }
        }
    }

    public static partial class Main
    {
        static readonly TouchGuideAnimation _touchGuideAnimation = new TouchGuideAnimation();
        static TouchGuideGraphic _touchGuideGraphic;
        static bool _touchGuideVisualShown;
        static float _touchGuideNextAnimation;
        static TouchControlAssignment _touchGuideAnimatedTopic;
        const float TouchGuideX = 565, TouchGuideY = 160;

        static void UpdateTouchGuideVisual()
        {
            var topic = TouchGuideVisible ? TouchGuideTopic : null;
            if (topic == null) { HideTouchGuideVisual(); return; }
            if (_liveRoot != null && (_touchGuideGraphic == null || _touchGuideGraphic.transform.parent != _liveRoot.transform))
            {
                var obj = new GameObject("TouchGuideAnimation", typeof(RectTransform), typeof(CanvasRenderer), typeof(TouchGuideGraphic));
                // Capture isolation records the original layer when this late
                // child appears. Do not inherit the parent's temporary layer.
                obj.layer = 5;
                var rect = (RectTransform)obj.transform; rect.SetParent(_liveRoot.transform, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = new Vector2(TouchGuideX + TouchGuideAnimation.Width / 2 - LivePanelWidth / 2,
                    LivePanelHeight / 2 - TouchGuideY - TouchGuideAnimation.Height / 2);
                rect.sizeDelta = new Vector2(TouchGuideAnimation.Width, TouchGuideAnimation.Height);
                _touchGuideGraphic = obj.GetComponent<TouchGuideGraphic>();
                _touchGuideGraphic.raycastTarget = false; _touchGuideGraphic.material = _liveImageMaterial;
                _touchGuideGraphic.Animation = _touchGuideAnimation;
                _touchGuideGraphic.Frame = TouchGuidePalette.Grid; _touchGuideGraphic.Ink = TouchGuidePalette.Ink; _touchGuideGraphic.Accent = TouchGuidePalette.Action;
                _touchGuideNextAnimation = 0;
            }
            if (!_touchGuideVisualShown)
            {
                _touchGuideVisualShown = true;
                if (_touchGuideGraphic != null) _touchGuideGraphic.gameObject.SetActive(true);
                _touchGuideNextAnimation = 0;
            }
            float now = Time.unscaledTime;
            if (ReferenceEquals(topic, _touchGuideAnimatedTopic) && now < _touchGuideNextAnimation) return;
            _touchGuideAnimatedTopic = topic; _touchGuideNextAnimation = now + .05f;
            _touchGuideAnimation.Build(topic.Gesture, now);
            if (_touchGuideGraphic != null) { _touchGuideGraphic.RefreshPortraits(); _touchGuideGraphic.SetVerticesDirty(); }
        }
        static void HideTouchGuideVisual()
        {
            if (!_touchGuideVisualShown) return;
            _touchGuideVisualShown = false; _touchGuideAnimatedTopic = null;
            if (_touchGuideGraphic != null) _touchGuideGraphic.gameObject.SetActive(false);
        }
        static void DrawFlatTouchGuide()
        {
            DrawFlatTouchStrokes(_touchGuideAnimation, TouchGuideX, TouchGuideY, 1);
        }
        static void DrawFlatTouchStrokes(TouchGuideAnimation animation, float x, float y, float scale)
        {
            if (TouchControllerArtwork.Texture != null)
            {
                Color saved = GUI.color;
                try
                {
                    GUI.color = Color.white;
                    float size=TouchGuideAnimation.PortraitSize;
                    TouchControllerArtwork.Flat(new Rect(x+(animation.LeftPortraitX-size*.5f)*scale,y+TouchGuideAnimation.PortraitTop*scale,size*scale,size*scale),true);
                    TouchControllerArtwork.Flat(new Rect(x+(animation.RightPortraitX-size*.5f)*scale,y+TouchGuideAnimation.PortraitTop*scale,size*scale,size*scale),false);
                }
                finally { GUI.color = saved; }
            }
            for(int i=0;i<animation.IconCount;i++) {
                var icon=animation.Icons[i];ControlIconDrawing.FlatBody(new Rect(x+icon.X*scale,y+icon.Y*scale,icon.Size*scale,icon.Size*scale),icon.Symbol,Color.white);
            }
            for (int i = 0; i < animation.Count; ++i)
            {
                var stroke = animation.Strokes[i];
                float dx = stroke.X1 - stroke.X0, dy = stroke.Y1 - stroke.Y0;
                float length = Mathf.Sqrt(dx * dx + dy * dy); if (length < .001f) continue;
                var matrix = GUI.matrix;
                try
                {
                    GUIUtility.RotateAroundPivot(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, new Vector2(x + stroke.X0 * scale, y + stroke.Y0 * scale));
                    DrawFlatLiveRect(x + stroke.X0 * scale, y + (stroke.Y0 - stroke.Width * .5f) * scale, length * scale, stroke.Width * scale,
                        stroke.Tone == 0 ? TouchGuidePalette.Grid : stroke.Tone == 1 ? TouchGuidePalette.Ink : TouchGuidePalette.Action);
                }
                finally { GUI.matrix = matrix; }
            }
        }
    }
}
