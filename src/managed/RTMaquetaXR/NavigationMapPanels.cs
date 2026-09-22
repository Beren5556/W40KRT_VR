namespace RTMaquetaXR
{
    public static partial class Main
    {
        internal static NavigationMapPanel GalacticMapPanel => new NavigationMapPanel(_cfg.mapGlobalWidth,
            _cfg.mapGlobalDistance, _cfg.mapGlobalAspect, _cfg.mapGlobalOffsetX, _cfg.mapGlobalOffsetY);
        internal static NavigationMapPanel StarSystemMapPanel => new NavigationMapPanel(_cfg.mapSystemWidth,
            _cfg.mapSystemDistance, _cfg.mapSystemAspect, _cfg.mapSystemOffsetX, _cfg.mapSystemOffsetY);
        internal static NavigationMapPanel NavigationPanel => InGalacticMap ? GalacticMapPanel : StarSystemMapPanel;

        internal static void SetGalacticMapWidth(float value) { _cfg.mapGlobalWidth = NavigationMapPolicy.Width(value); MarkSettingsDirty(); }
        internal static void SetGalacticMapDistance(float value) { _cfg.mapGlobalDistance = NavigationMapPolicy.Distance(value); MarkSettingsDirty(); }
        internal static void SetGalacticMapOffsetX(float value) { _cfg.mapGlobalOffsetX = NavigationMapPolicy.Offset(value); MarkSettingsDirty(); }
        internal static void SetGalacticMapOffsetY(float value) { _cfg.mapGlobalOffsetY = NavigationMapPolicy.Offset(value); MarkSettingsDirty(); }
        internal static void CycleGalacticMapAspect(int direction) { _cfg.mapGlobalAspect = NavigationMapPolicy.NextAspect(_cfg.mapGlobalAspect, direction); MarkSettingsDirty(); }
        internal static void SetStarSystemMapWidth(float value) { _cfg.mapSystemWidth = NavigationMapPolicy.Width(value); MarkSettingsDirty(); }
        internal static void SetStarSystemMapDistance(float value) { _cfg.mapSystemDistance = NavigationMapPolicy.Distance(value); MarkSettingsDirty(); }
        internal static void SetStarSystemMapOffsetX(float value) { _cfg.mapSystemOffsetX = NavigationMapPolicy.Offset(value); MarkSettingsDirty(); }
        internal static void SetStarSystemMapOffsetY(float value) { _cfg.mapSystemOffsetY = NavigationMapPolicy.Offset(value); MarkSettingsDirty(); }
        internal static void CycleStarSystemMapAspect(int direction) { _cfg.mapSystemAspect = NavigationMapPolicy.NextAspect(_cfg.mapSystemAspect, direction); MarkSettingsDirty(); }
    }
}
