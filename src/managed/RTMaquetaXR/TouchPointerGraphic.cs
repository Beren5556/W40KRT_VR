using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    internal enum TouchGlyphMode { Aim, Pan, RotateScale, Tilt, Release, Selection, Turn }

    // Reticle and gesture arrows. The help bubble draws background, cached
    // model portraits, then annotations; opaque portraits cannot hide LIMIT.
    // The small retained vector mesh
    // changes only when the gesture changes, not for every tracking sample.
    public sealed class TouchPointerGraphic : Graphic
    {
        TouchGlyphMode mode;
        bool leftGrip, rightGrip, limited, hit;
        bool backgroundOnly, annotationsOnly;
        TouchPointerGraphic annotations;
        internal void PlaceAnnotationsOverPortraits()
        {
            if (annotations != null) return;
            var obj = new GameObject("Gesture arrows above portraits",typeof(RectTransform),typeof(CanvasRenderer),typeof(TouchPointerGraphic));
            obj.layer = 5; var rect = (RectTransform)obj.transform;
            rect.SetParent(transform,false); rect.sizeDelta = rectTransform.sizeDelta;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f);
            annotations = obj.GetComponent<TouchPointerGraphic>(); annotations.annotationsOnly = true;
            annotations.raycastTarget = false; annotations.material = material; backgroundOnly = true;
            annotations.SetState(mode,leftGrip,rightGrip,limited,hit); SetVerticesDirty();
        }
        internal void SetState(TouchGlyphMode value, bool left, bool right, bool atLimit, bool hasHit)
        {
            if (mode == value && leftGrip == left && rightGrip == right && limited == atLimit && hit == hasHit) return;
            mode = value; leftGrip = left; rightGrip = right; limited = atLimit; hit = hasHit; SetVerticesDirty();
            if (annotations != null) annotations.SetState(value,left,right,atLimit,hasHit);
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Color accent = limited ? new Color(1f, .66f, .16f) : new Color(.24f, .91f, 1f);
            if (mode == TouchGlyphMode.Aim)
            {
                if (annotationsOnly) return;
                Color aim = hit ? new Color(.3f, 1f, .65f) : Color.white;
                Ring(mesh, Vector2.zero, 8, 3.8f, new Color(0, 0, 0, .9f));
                Ring(mesh, Vector2.zero, 8, 1.5f, aim);
                Disk(mesh, Vector2.zero, 1.6f, aim);
                return;
            }
            if (!annotationsOnly) Disk(mesh, Vector2.zero, 61, new Color(.015f, .025f, .04f, .87f));
            if (backgroundOnly) return;


            if (mode == TouchGlyphMode.RotateScale)
            {
                ArcArrow(mesh, new Vector2(0, 8), 48, 15, 158, accent);
                Arrow(mesh, new Vector2(-7, -33), new Vector2(-39, -33), accent);
                Arrow(mesh, new Vector2(7, -33), new Vector2(39, -33), accent);
            }
            else if (mode == TouchGlyphMode.Pan)
            {
                Arrow(mesh, new Vector2(-9, 34), new Vector2(-41, 34), accent);
                Arrow(mesh, new Vector2(9, 34), new Vector2(41, 34), accent);
                Arrow(mesh, new Vector2(0, 25), new Vector2(0, 46), accent);
            }
            else if (mode == TouchGlyphMode.Tilt)
            {
                Line(mesh, new Vector2(-21, 33), new Vector2(21, 45), 3, accent);
                ArcArrow(mesh, new Vector2(0, 45), 17, 205, 327, accent);
            }
            else if (mode == TouchGlyphMode.Turn)
            {
                ArcArrow(mesh, new Vector2(0, 8), 48, 15, 158, accent);
                Arrow(mesh, new Vector2(-5, -33), new Vector2(-34, -33), accent);
                Arrow(mesh, new Vector2(5, -33), new Vector2(34, -33), accent);
            }
            else if (mode == TouchGlyphMode.Selection)
            {
                Line(mesh, new Vector2(-19, 31), new Vector2(16, 31), 2, accent);
                Line(mesh, new Vector2(-19, 31), new Vector2(-19, 47), 2, accent);
                Line(mesh, new Vector2(-19, 47), new Vector2(16, 47), 2, accent);
                Line(mesh, new Vector2(16, 47), new Vector2(16, 31), 2, accent);
                Arrow(mesh, new Vector2(6, 40), new Vector2(29, 29), accent);
            }
            else Ring(mesh, new Vector2(0, 39), 11, 2, accent);
            if (limited)
            {
                Line(mesh, new Vector2(-47, -48), new Vector2(47, -48), 3, accent);
                Line(mesh, new Vector2(0, 4), new Vector2(0, 19), 4, accent);
                Disk(mesh, new Vector2(0, -3), 2.5f, accent);
            }
        }
        internal static void Line(VertexHelper mesh, Vector2 from, Vector2 to, float width, Color color)
        {
            Vector2 n = new Vector2(-(to - from).y, (to - from).x).normalized * (width * .5f);
            Quad(mesh, from - n, from + n, to + n, to - n, color);
        }
        static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(a, color, Vector2.zero); mesh.AddVert(b, color, Vector2.zero);
            mesh.AddVert(c, color, Vector2.zero); mesh.AddVert(d, color, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2); mesh.AddTriangle(first, first + 2, first + 3);
        }
        static void Disk(VertexHelper mesh, Vector2 center, float radius, Color color) => Ellipse(mesh, center, radius, radius, color);
        static void Ellipse(VertexHelper mesh, Vector2 center, float x, float y, Color color)
        {
            int first = mesh.currentVertCount; mesh.AddVert(center, color, Vector2.zero);
            const int steps = 24;
            for (int i = 0; i <= steps; ++i)
            {
                float a = i * Mathf.PI * 2 / steps;
                mesh.AddVert(center + new Vector2(Mathf.Cos(a) * x, Mathf.Sin(a) * y), color, Vector2.zero);
                if (i > 0) mesh.AddTriangle(first, first + i, first + i + 1);
            }
        }
        static void Ring(VertexHelper mesh, Vector2 center, float radius, float width, Color color)
        {
            Vector2 last = center + new Vector2(radius, 0);
            for (int i = 1; i <= 32; ++i)
            {
                float a = i * Mathf.PI * 2 / 32;
                Vector2 next = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                Line(mesh, last, next, width, color); last = next;
            }
        }
        static void Arrow(VertexHelper mesh, Vector2 from, Vector2 to, Color color)
        {
            Line(mesh, from, to, 3, color); Vector2 direction = (to - from).normalized;
            Vector2 n = new Vector2(-direction.y, direction.x);
            Line(mesh, to - direction * 8 + n * 5, to, 3, color);
            Line(mesh, to - direction * 8 - n * 5, to, 3, color);
        }
        static void ArcArrow(VertexHelper mesh, Vector2 center, float radius, float start, float end, Color color)
        {
            Vector2 last = center + new Vector2(Mathf.Cos(start * Mathf.Deg2Rad), Mathf.Sin(start * Mathf.Deg2Rad)) * radius;
            for (int i = 1; i <= 18; ++i)
            {
                float a = Mathf.Lerp(start, end, i / 18f) * Mathf.Deg2Rad;
                Vector2 next = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                if (i == 18) Arrow(mesh, last, next, color); else Line(mesh, last, next, 3, color);
                last = next;
            }
        }
    }
}
