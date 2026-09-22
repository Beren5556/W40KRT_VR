using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        // Borrow only ordinary RGBA sprites verified in settingspcview.res.
        // Other Owlcat decorations encode colour channels for custom materials;
        // using those with UI/Default produces orange/green artefacts.
        static Sprite _liveNativeMonitor, _liveNativeButton;
        static int _liveThemeAttempts;
        static float _liveThemeNextAttempt;
        static readonly List<Image> _liveThemedImages = new List<Image>();

        static void TryLiveGameTheme()
        {
            if ((_liveNativeMonitor != null && _liveNativeButton != null) || Time.unscaledTime < _liveThemeNextAttempt) return;
            ++_liveThemeAttempts;
            _liveThemeNextAttempt = Time.unscaledTime + 10f;
            // Creation/open only; at most one attempt per ten seconds. A missing
            // settings asset may arrive much later, so opening again can retry.
            // Never load bundles, clone native views or scan during gameplay.
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite == null) continue;
                if (sprite.name == "Monitor_M" && sprite.rect.width == 2048 && sprite.rect.height == 1024)
                    _liveNativeMonitor = sprite;
                else if (sprite.name == "Dialogue_Button_Normal" && sprite.rect.width == 298 && sprite.rect.height == 76)
                    _liveNativeButton = sprite;
            }
            foreach (var image in _liveThemedImages) if (image != null) ApplyLiveGameTheme(image);
        }

        static int LiveThemeRole(string name)
        {
            switch (name)
            {
                case "Background": case "QuickBackground": return 1;
                case "ActionFrame": case "DecreaseFrame": case "IncreaseFrame":
                case "PreviousFrame": case "NextFrame": case "QuickCloseFrame": return 2;
                default: return 0;
            }
        }

        static void RegisterLiveGameTheme(Image image)
        {
            if (LiveThemeRole(image.name) == 0) return;
            _liveThemedImages.Add(image); ApplyLiveGameTheme(image);
        }

        static void ApplyLiveGameTheme(Image image)
        {
            int role = LiveThemeRole(image.name);
            Sprite sprite = role == 1 ? _liveNativeMonitor : _liveNativeButton;
            if (sprite == null || sprite.texture == null) return;
            image.sprite = sprite; image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = role == 1 ? 3f : 1.5f;
            image.color = Color.white;
        }

        static float LiveThemeEdge(int index, float a, float b, float c, float d) =>
            index == 0 ? a : index == 1 ? b : index == 2 ? c : d;

        static void DrawFlatLiveTheme(string name, float x, float y, float width, float height, Color fallback)
        {
            int role = LiveThemeRole(name);
            Sprite sprite = role == 1 ? _liveNativeMonitor : _liveNativeButton;
            if (role == 0 || sprite == null || sprite.texture == null ||
                (sprite.packed && sprite.packingRotation != SpritePackingRotation.None))
            { DrawFlatLiveRect(x, y, width, height, fallback); return; }
            // Match Image.Type.Sliced without copying or unpacking the atlas.
            var outer = DataUtility.GetOuterUV(sprite); var inner = DataUtility.GetInnerUV(sprite);
            var border = sprite.border * (100f / Mathf.Max(1f, sprite.pixelsPerUnit) / (role == 1 ? 3f : 1.5f));
            float horizontal = Mathf.Min(1f, width / Mathf.Max(1f, border.x + border.z));
            float vertical = Mathf.Min(1f, height / Mathf.Max(1f, border.y + border.w));
            GUI.color = Color.white;
            for (int row = 0; row < 3; ++row)
            for (int col = 0; col < 3; ++col)
            {
                float x0 = LiveThemeEdge(col, x, x + border.x * horizontal, x + width - border.z * horizontal, x + width);
                float x1 = LiveThemeEdge(col + 1, x, x + border.x * horizontal, x + width - border.z * horizontal, x + width);
                float y0 = LiveThemeEdge(row, y, y + border.w * vertical, y + height - border.y * vertical, y + height);
                float y1 = LiveThemeEdge(row + 1, y, y + border.w * vertical, y + height - border.y * vertical, y + height);
                float u0 = LiveThemeEdge(col, outer.x, inner.x, inner.z, outer.z);
                float u1 = LiveThemeEdge(col + 1, outer.x, inner.x, inner.z, outer.z);
                float v0 = LiveThemeEdge(row, outer.w, inner.w, inner.y, outer.y);
                float v1 = LiveThemeEdge(row + 1, outer.w, inner.w, inner.y, outer.y);
                if (x1 > x0 && y1 > y0)
                    GUI.DrawTextureWithTexCoords(new Rect(x0, y0, x1 - x0, y1 - y0), sprite.texture, new Rect(u0, v1, u1 - u0, v0 - v1));
            }
        }

        static void ReleaseLiveGameTheme()
        {
            // Ownership remains with the game. Never Destroy sprites/textures.
            _liveThemedImages.Clear(); _liveNativeMonitor = _liveNativeButton = null; _liveThemeAttempts = 0; _liveThemeNextAttempt = 0;
        }
    }
}
