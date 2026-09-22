using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    // Contextual help keeps controls in an explicit group of atlas graphics.
    // Placement therefore never depends on a hidden marker surviving Text's
    // wrapping/truncation pass. Literal separators and durations remain Text.
    internal sealed class ContextHintControlGroup : MonoBehaviour
    {
        static readonly Regex RichTags = new Regex(@"<[^>]+>", RegexOptions.CultureInvariant);
        const string Slot = "\u00a0\u00a0\u00a0\u00a0";
        readonly List<Text> labels = new List<Text>();
        readonly List<ContextHintControlIcon> icons = new List<ContextHintControlIcon>();
        readonly List<ControlIconRun> runs = new List<ControlIconRun>();
        Font font;
        Material fontMaterial, iconMaterial;
        Color ink = Color.white;
        int fontSize = 28, bodySize = 40;
        string value = "";
        internal float PreferredWidth { get; private set; }
        internal float PreferredHeight { get; private set; }
        internal RectTransform Rect => (RectTransform)transform;

        internal void Configure(Font currentFont, Material currentFontMaterial, Material currentIconMaterial,
            int currentFontSize, int currentBodySize, Color currentInk)
        {
            font = currentFont; fontMaterial = currentFontMaterial; iconMaterial = currentIconMaterial;
            fontSize = currentFontSize; bodySize = currentBodySize; ink = currentInk;
            Rebuild();
        }

        internal void SetFont(Font currentFont)
        {
            if (font == currentFont) return;
            font = currentFont; Rebuild();
        }

        internal void SetValue(string current)
        {
            current = current ?? "";
            if (value == current) return;
            value = current; Rebuild();
        }

        Text Label(int index)
        {
            while (labels.Count <= index)
            {
                var item = new GameObject("Control literal", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                item.layer = gameObject.layer; item.transform.SetParent(transform, false);
                var text = item.GetComponent<Text>(); text.raycastTarget = false;
                text.supportRichText = false; text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow; text.alignment = TextAnchor.MiddleLeft;
                labels.Add(text);
            }
            return labels[index];
        }

        ContextHintControlIcon Icon(int index)
        {
            while (icons.Count <= index)
            {
                var item = new GameObject("Approved control icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(ContextHintControlIcon));
                item.layer = gameObject.layer; item.transform.SetParent(transform, false);
                var icon = item.GetComponent<ContextHintControlIcon>(); icon.raycastTarget = false;
                icons.Add(icon);
            }
            return icons[index];
        }

        void Rebuild()
        {
            for (int i = 0; i < labels.Count; ++i) labels[i].gameObject.SetActive(false);
            for (int i = 0; i < icons.Count; ++i) icons[i].gameObject.SetActive(false);
            PreferredWidth = 0; PreferredHeight = 0;
            if (font == null || string.IsNullOrEmpty(value)) return;

            string prepared = ControlIconMarkup.Prepare(value, runs, bodySize);
            string plain = RichTags.Replace(prepared, "");
            int cursor = 0, labelIndex = 0, iconIndex = 0;
            for (int runIndex = 0; runIndex <= runs.Count; ++runIndex)
            {
                int marker = runIndex < runs.Count ? plain.IndexOf(Slot, cursor, StringComparison.Ordinal) : plain.Length;
                if (marker < cursor) marker = cursor;
                string literal = plain.Substring(cursor, marker - cursor);
                if (literal.Length > 0)
                {
                    var text = Label(labelIndex++); text.gameObject.SetActive(true); text.text = literal;
                    text.font = font; text.fontSize = fontSize * 2; text.fontStyle = FontStyle.Bold;
                    text.color = ink; text.material = fontMaterial; text.rectTransform.localScale = Vector3.one * .5f;
                    float labelWidth = Mathf.Ceil(text.preferredWidth * .5f) + 2;
                    PreferredHeight = Mathf.Max(PreferredHeight, Mathf.Ceil(text.preferredHeight * .5f));
                    text.rectTransform.anchorMin = text.rectTransform.anchorMax = text.rectTransform.pivot = new Vector2(0, .5f);
                    text.rectTransform.anchoredPosition = new Vector2(PreferredWidth, 0);
                    text.rectTransform.sizeDelta = new Vector2(labelWidth * 2, Rect.rect.height * 2);
                    PreferredWidth += labelWidth;
                }
                if (runIndex == runs.Count) break;
                var icon = Icon(iconIndex++); icon.gameObject.SetActive(true); icon.material = iconMaterial;
                icon.color = ink; icon.SetSymbol(runs[runIndex].Symbol, bodySize);
                float artWidth = ControlIconAtlas.BodyFit(new Rect(0, 0, bodySize, bodySize), runs[runIndex].Symbol).width;
                float artHeight = ControlIconAtlas.BodyFit(new Rect(0, 0, bodySize, bodySize), runs[runIndex].Symbol).height;
                float iconWidth = Mathf.Ceil(Mathf.Max(ControlIconSizing65.Slot(bodySize), artWidth + 4));
                icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = icon.rectTransform.pivot = new Vector2(0, .5f);
                icon.rectTransform.anchoredPosition = new Vector2(PreferredWidth, 0);
                icon.rectTransform.sizeDelta = new Vector2(iconWidth, Rect.rect.height);
                PreferredWidth += iconWidth; PreferredHeight = Mathf.Max(PreferredHeight, Mathf.Ceil(artHeight)); cursor = marker + Slot.Length;
            }
        }
    }

    internal sealed class ContextHintControlIcon : MaskableGraphic
    {
        string symbol = ""; int body;
        internal void SetSymbol(string current, int currentBody)
        {
            if (symbol == current && body == currentBody) return;
            symbol = current ?? ""; body = currentBody; SetVerticesDirty();
        }
        public override Texture mainTexture => ControlIconAtlas.Texture(ControlIconAtlasLayout.Keyboard(symbol));
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); if (string.IsNullOrEmpty(symbol) || body <= 0) return;
            Rect r = rectTransform.rect;
            var bounds = new Rect(r.center.x - body * .5f, r.center.y - body * .5f, body, body);
            ControlIconDrawing.MeshBody(mesh, bounds, symbol, color);
        }
    }
}
