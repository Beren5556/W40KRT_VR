namespace RTMaquetaXR
{
    internal static class TouchContextHintLayout
    {
        internal const int Width = 1120, HeaderHeight = 108, CellWidth = 1072,
            CaptionMinimum = 420, CaptionMaximum = 600, CellHeight = 52, Padding = 24, Gap = 16;
        // One left-aligned column inside a screen-centred block. Height follows
        // the real binding count; no fixed capacity silently drops controls.
        internal static int CellX(int index) => Padding;
        internal static int CellY(int index) => HeaderHeight + index * CellHeight;
        internal static int HeightForRows(int rows) => HeaderHeight + System.Math.Max(0, rows) * CellHeight + Padding;
    }
    internal enum TouchHintContext
    {
        Hidden, Exploration, GroundCombat, HeadView, CombatHeadView, Dialogue, DialoguePanel, Paused, Management,
        GalacticMap, StarSystemMap, SpaceCombat, LocalMap, Settings, Welcome,
        CombatActors, PartyWheel, MenuWheel, LeftAbilities, RightAbilities, Tutorial
    }
    internal struct TouchHintState
    {
        internal bool Enabled, Ready, Loading, Welcome, Settings, Radial, RightWheel,
            Actors, Party, SpaceWheel, LocalMap, Tutorial, Management, Galactic, System,
            Space, Dialogue, Combat, FirstPerson, Flat, Paused;
    }
    internal static class TouchContextHintsPolicy
    {
        internal static float VerticalShift(float leftBottom,float leftTop,float rightBottom,float rightTop,float fraction)
        {
            float bottom=System.Math.Max(leftBottom,rightBottom),top=System.Math.Min(leftTop,rightTop);
            return top>bottom ? -(top-bottom)*System.Math.Max(0,System.Math.Min(.5f,fraction)) : 0;
        }
        // Input owners take precedence over the area underneath them. This is a
        // read-only description; it never takes input or changes a game mode.
        internal static TouchHintContext Resolve(TouchHintState s)
        {
            if (!s.Enabled || !s.Ready || s.Loading) return TouchHintContext.Hidden;
            if (s.Welcome || s.Settings || s.Tutorial || s.LocalMap || s.Management || s.Dialogue || s.Galactic || s.System || s.Flat) return TouchHintContext.Hidden;
            if (s.Radial)
            {
                if (s.RightWheel) return TouchHintContext.MenuWheel;
                if (s.Actors) return TouchHintContext.CombatActors;
                if (s.Party) return TouchHintContext.PartyWheel;
                return TouchHintContext.LeftAbilities;
            }
            // Native pause in a playable scene retains that scene's controls.
            if (s.Space) return TouchHintContext.SpaceCombat;
            if (s.FirstPerson) return s.Combat ? TouchHintContext.CombatHeadView : TouchHintContext.HeadView;
            return s.Combat ? TouchHintContext.GroundCombat : TouchHintContext.Exploration;
        }
    }
    internal sealed class TouchContextHintCache
    {
        internal TouchHintContext Context = TouchHintContext.Hidden;
        internal int Language = -1;
        internal bool CombatHead;
        internal long Changes;
        internal bool Changed(TouchHintContext context, int language, bool combatHead)
        {
            if (Context == context && Language == language && CombatHead == combatHead) return false;
            Context = context; Language = language; CombatHead = combatHead; ++Changes; return true;
        }
    }
}
