using System;
using System.Collections.Generic;
namespace RTMaquetaXR
{
    internal static class TouchQuickGuideLayout
    {
        // Optional compact invitation. Full controls remain in seven tutorial sections.
        internal const float Width = 920, Height = 510, WidthAtDistance = .66f;
        internal const float YesX = 170, NoX = 500, ButtonY = 366, ButtonWidth = 250, ButtonHeight = 62;
        internal static TouchControlAssignment[] Assignments()
        {
            var result = new List<TouchControlAssignment>();
            foreach (var item in TouchControlAssignments.All) if (item.Quick) result.Add(item);
            return result.ToArray();
        }
        internal static TouchControlAssignment Keyboard() => TouchControlAssignments.Find("keyboard-settings");
        internal static bool TryFlatPlacement(float w, float h, out float x, out float y, out float scale)
        {
            x = y = scale = 0;
            if (float.IsNaN(w) || float.IsInfinity(w) || float.IsNaN(h) || float.IsInfinity(h) || w <= 0 || h <= 0) return false;
            scale = Math.Min(w * .66f / Width, h * .62f / Height);
            x = (w - Width * scale) * .5f; y = (h - Height * scale) * .5f; return true;
        }
        internal static int PointerHit(float x, float y)
        {
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y) || y < ButtonY || y >= ButtonY + ButtonHeight) return -1;
            if (x >= YesX && x < YesX + ButtonWidth) return 0;
            return x >= NoX && x < NoX + ButtonWidth ? 1 : -1;
        }
    }
}
