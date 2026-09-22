using System;
using System.Collections.Generic;

namespace RTMaquetaXR
{
    internal static class CombatVisualPolicy
    {
        internal static float Intensity(float value, float floor)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 1f;
            return Math.Max(floor, Math.Min(1f, value));
        }
        internal static bool EyeScope(bool installed, bool world, bool knownEye) => installed && world && knownEye;
        internal static bool HasOverrides(float surface, float marks, int outlineMode, float outlineIntensity) =>
            Intensity(surface, .25f) < 1f || Intensity(marks, .25f) < 1f || outlineMode == 2 || outlineMode == 3 ||
            (outlineMode == 1 && Intensity(outlineIntensity, .1f) < 1f);
        internal static bool SuppressOutline(bool eyeScope, bool ownedUnit, int mode) => eyeScope && ownedUnit && (mode == 2 || mode == 3);
        internal static bool DimOutline(bool eyeScope, bool ownedUnit, int mode) => eyeScope && ownedUnit && mode == 1;
        internal static bool CircleVisible(bool active, int mode, bool unitActive, float transition, float alpha) =>
            active && mode == 2 && unitActive && transition > 0 && alpha > .001f;
        internal static bool PersistentCircleVisible(bool active, int mode, bool unitActive, bool selected, float transition, float alpha) =>
            active && mode == 2 && unitActive && (selected || (transition > 0 && alpha > .001f));
        internal static float Radius(float colliderRadius) => float.IsNaN(colliderRadius) || float.IsInfinity(colliderRadius) || colliderRadius <= 0 ? .45f : Math.Max(.2f, Math.Min(4f, colliderRadius * 1.08f));
        // Exactly one final multiplier; never attenuate both a tint and alpha.
        internal static int OpacityProperty(bool alphaMulty, bool finalAlpha, bool alphaScale, bool baseColor, bool color, bool tint)
        {
            if (alphaMulty) return 0;
            if (finalAlpha) return 1;
            if (alphaScale) return 2;
            if (baseColor) return 3;
            if (color) return 4;
            return tint ? 5 : -1;
        }
    }

    internal sealed class CombatVisualRestoreQueue<T>
    {
        readonly List<T> entries = new List<T>();
        internal int Count => entries.Count;
        internal void Add(T entry) => entries.Add(entry);
        internal void RestoreAll(Action<T> restore)
        {
            Exception first = null;
            try
            {
                for (int i = entries.Count - 1; i >= 0; --i)
                    try { restore(entries[i]); } catch (Exception error) { if (first == null) first = error; }
            }
            finally { entries.Clear(); }
            if (first != null) throw first;
        }
    }
}
