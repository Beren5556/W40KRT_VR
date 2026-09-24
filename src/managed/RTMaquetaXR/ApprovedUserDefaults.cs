namespace RTMaquetaXR
{
    // Approved public defaults for a clean 0.9.81 installation.
    // Existing player settings take priority unless an older-version upgrade
    // explicitly applies the revised defaults after backup and confirmation.
    internal static class ApprovedUserDefaults
    {
        internal const float WorldScale=9.3835f;
        internal const float MapZoom=0f;
        internal const float Distance=.5f, Width=.85f, Aspect=1.7f, OffsetX=0f, OffsetY=0f;
        internal const float MenuDistance=.650000036f, MenuWidth=.670000136f, MenuAspect=1.5f, MenuX=0f, MenuY=0f, MenuText=1.05f;
        internal const int Raster=1, DrawMode=3, EngineEffects=1, OutlineMode=2;
        internal const float DrawDistance=45f, OutlineIntensity=1f;
        internal const bool AdaptiveDistance=true, PreparationAura=false;
    }
}
