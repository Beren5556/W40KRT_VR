using System;

namespace RTMaquetaXR
{
    // The PC interface remains one proportional screen. This geometry is also
    // used by the pick camera; moving a layer forward must not move its buttons
    // away from the screen coordinates used by Owlcat's input module.
    internal struct HudPanelGeometry
    {
        public float Distance, Width, Height, UnitsScale, Aspect, VerticalFov, AngularWidth, CentreX, CentreY;
    }

    // Inward frustum plane in head-centred metres: X*x + Y*y + Z*z + Offset >= 0.
    internal struct HudFrustumPlane
    {
        public float X, Y, Z, Offset;
    }

    internal static class HudPanelLayout
    {
        internal const float ReferenceWidth = 1920f, ReferenceHeight = 1080f;
        internal static bool IsNestedPcChrome(string name) =>
            name == "PartyPCView" || name == "SurfaceActionBarPCView";

        internal static bool ValidReference(float width, float height) =>
            Finite(width) && Finite(height) && width >= 320 && height >= 240 &&
            width <= 16384 && height <= 16384 && width / height >= .5f && width / height <= 4f;

        internal static HudPanelGeometry Calculate(float distanceMetres, float worldScale, float widthFraction,
            float referenceWidth, float referenceHeight, float farClip, float layerInsetMetres = 0,
            HudFrustumPlane[] viewPlanes = null, float offsetX = 0, float offsetY = 0)
        {
            if (!Finite(distanceMetres) || distanceMetres <= 0 || !Finite(worldScale) || worldScale <= 0)
                throw new ArgumentOutOfRangeException("HUD distance and scale must be finite and positive");
            if (!ValidReference(referenceWidth, referenceHeight))
            { referenceWidth = ReferenceWidth; referenceHeight = ReferenceHeight; }
            widthFraction = Finite(widthFraction) ? Math.Max(.45f, Math.Min(1.8f, widthFraction)) : .9f;
            offsetX = ClampOffset(offsetX); offsetY = ClampOffset(offsetY);
            float nominalDistance = distanceMetres * worldScale;
            // A tight world draw distance must not clip the game interface. Keep
            // its angular size while bringing the panel inside the eye frustum.
            if (Finite(farClip) && farClip > .1f) nominalDistance = Math.Min(nominalDistance, farClip * .8f);
            float inset = Finite(layerInsetMetres) ? Math.Max(0, layerInsetMetres) * worldScale : 0;
            float distance = nominalDistance - Math.Min(inset, nominalDistance * .05f);
            // The centered binocular fit is the 100% reference, NOT an automatic
            // constraint after manual placement. Offset and enlargement must not
            // shrink text again to counteract the user's explicit adjustments.
            // At >100% or large offsets some panel edges can leave the view.
            float halfTangent = FitHalfTangent(nominalDistance * .95f / worldScale,
                referenceWidth / referenceHeight, viewPlanes) * widthFraction;
            halfTangent = Math.Min(4, halfTangent);
            float width = 2 * distance * halfTangent, height = width * referenceHeight / referenceWidth;
            return new HudPanelGeometry { Distance = distance, Width = width, Height = height,
                UnitsScale = width / referenceWidth, Aspect = referenceWidth / referenceHeight,
                CentreX = width * offsetX, CentreY = height * offsetY,
                VerticalFov = (float)(2 * Math.Atan(height * .5f / distance) * 180 / Math.PI),
                AngularWidth = (float)(2 * Math.Atan(halfTangent) * 180 / Math.PI) };
        }

        internal static float FitHalfTangent(float distance, float aspect, HudFrustumPlane[] planes,
            float offsetX = 0, float offsetY = 0)
        {
            const float startupHalfTangent = .839099631f; // 80 degrees, only before valid OpenXR views.
            if (planes == null || planes.Length != 8 || !Finite(distance) || distance <= 0 ||
                !Finite(aspect) || aspect <= 0) return startupHalfTangent;
            float halfHeight = float.MaxValue;
            foreach (var plane in planes)
            {
                float centre = plane.Z * distance + plane.Offset;
                float edge = Math.Abs(plane.X) * aspect + Math.Abs(plane.Y) -
                    2 * (plane.X * aspect * ClampOffset(offsetX) + plane.Y * ClampOffset(offsetY));
                if (!Finite(centre) || !Finite(edge) || centre <= 0 || edge <= 0) return startupHalfTangent;
                halfHeight = Math.Min(halfHeight, centre / edge);
            }
            // Match the native quad's maximum width/distance of eight. This
            // cap only shrinks an unusually wide runtime frustum; never stretch
            // a portrait or narrow fit to an arbitrary minimum panel size.
            return Math.Min(4, halfHeight * aspect / distance);
        }

        // The desktop is a 16:9 texture, not the headset's logical UI viewport.
        // Derive a useful layout aspect from the independently measured horizontal
        // and vertical binocular limits. Calculate still fits all four corners,
        // including canted eyes, so the axis estimate cannot overrun either eye.
        internal static float ViewAspect(float distance, HudFrustumPlane[] planes)
        {
            if (planes == null || planes.Length != 8 || !Finite(distance) || distance <= 0) return 1f;
            float horizontal = float.MaxValue, vertical = float.MaxValue;
            foreach (var plane in planes)
            {
                float centre = plane.Z * distance + plane.Offset;
                if (!Finite(centre) || centre <= 0) return 1f;
                if (Math.Abs(plane.X) > .00001f) horizontal = Math.Min(horizontal, centre / Math.Abs(plane.X));
                if (Math.Abs(plane.Y) > .00001f) vertical = Math.Min(vertical, centre / Math.Abs(plane.Y));
            }
            if (horizontal == float.MaxValue || vertical == float.MaxValue || vertical <= 0) return 1f;
            return ClampAspect(horizontal / vertical);
        }

        internal static float ClampAspect(float value) => Finite(value) ? Math.Max(.75f, Math.Min(2.4f, value)) : 1f;
        internal static float ClampElementScale(float value) => Finite(value) ? Math.Max(1f, Math.Min(1.8f, value)) : 1f;
        internal static float LogicalWidth(float nativeWidth, float elementScale) =>
            (Finite(nativeWidth) && nativeWidth >= 320 && nativeWidth <= 16384 ? nativeWidth : ReferenceWidth) /
            ClampElementScale(elementScale);
        internal static float ClampOffset(float value) => Finite(value) ? Math.Max(-.65f, Math.Min(.65f, value)) : 0;

        // A game fade must cover the UNION of both eye views, unlike readable
        // HUD chrome which fits their intersection. Solve each eye's four corner
        // rays against a head-facing plane, including cant, offsets and IPD.
        internal static HudPanelGeometry CoverViews(float distanceMetres, float worldScale, float farClip,
            float referenceWidth, HudFrustumPlane[] planes, float logicalAspect = 0)
        {
            var depth = Calculate(distanceMetres, worldScale, 1, referenceWidth, ReferenceHeight,
                farClip, .025f, planes);
            float distance = depth.Distance / worldScale;
            float minX = -distance * 1.5f, maxX = -minX, minY = minX, maxY = maxX;
            bool measured = planes != null && planes.Length == 8;
            if (measured)
            {
                minX = minY = float.MaxValue; maxX = maxY = float.MinValue;
                for (int eye = 0; eye < 2 && measured; ++eye)
                    for (int x = 0; x < 2 && measured; ++x)
                        for (int y = 0; y < 2; ++y)
                        {
                            var a = planes[eye * 4 + x]; var b = planes[eye * 4 + 2 + y];
                            float determinant = a.X * b.Y - a.Y * b.X;
                            if (!Finite(determinant) || Math.Abs(determinant) < .000001f) { measured = false; break; }
                            float ac = -(a.Z * distance + a.Offset), bc = -(b.Z * distance + b.Offset);
                            float px = (ac * b.Y - a.Y * bc) / determinant;
                            float py = (a.X * bc - ac * b.X) / determinant;
                            if (!Finite(px) || !Finite(py)) { measured = false; break; }
                            minX = Math.Min(minX, px); maxX = Math.Max(maxX, px);
                            minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
                        }
            }
            if (!measured || minX >= maxX || minY >= maxY)
            { minX = minY = -distance * 1.5f; maxX = maxY = distance * 1.5f; }
            float width = (maxX - minX) * 1.02f * worldScale;
            float height = (maxY - minY) * 1.02f * worldScale;
            // A stable logical aspect lets the native black bars retain their
            // anchors without rebuilding layout for tiny tracking fluctuations.
            float aspect = Finite(logicalAspect) && logicalAspect > .1f ? logicalAspect : width / height;
            width = Math.Max(width, height * aspect); height = width / aspect;
            return new HudPanelGeometry { Distance = depth.Distance, Width = width, Height = height,
                CentreX = (minX + maxX) * .5f * worldScale, CentreY = (minY + maxY) * .5f * worldScale,
                Aspect = aspect, UnitsScale = width / referenceWidth,
                VerticalFov = (float)(2 * Math.Atan(height * .5f / depth.Distance) * 180 / Math.PI),
                AngularWidth = (float)(2 * Math.Atan(width * .5f / depth.Distance) * 180 / Math.PI) };
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
