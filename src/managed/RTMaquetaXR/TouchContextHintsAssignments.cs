using System;
using System.Text;

namespace RTMaquetaXR
{
    internal sealed class TouchContextBinding
    {
        internal readonly string Id, AssignmentId, Source, Glyph, CompactSource;
        internal TouchContextBinding(string id, string assignment, string source, string glyph, string compact) { Id = id; AssignmentId = assignment; Source = source; Glyph = glyph; CompactSource = compact; }
    }
    // Compact variants live beside the shared assignments, with an explicit
    // parent record. The tutorial, welcome, hints and validation use this source.
    internal static partial class TouchControlAssignments
    {
        internal static readonly string[] TutorialContexts = { "On-foot exploration", "Ground combat", "Dialogue and cutscenes", "Management screens", "Star map · warp routes", "Galactic map · system planets", "Space combat" };
        internal static readonly string[][] TutorialAssignments = {
            new[] { "select", "head-view", "combat-ground-head", "drag-select", "group-move", "table-move", "rotate-zoom", "tilt", "radial-left", "radial-weapons", "radial-left-modes", "radial-right", "wheel-cards", "exploration-shortcuts", "draw-distance", "pause", "secondary", "overlay", "overlay-navigation", "context-reminder", "hud-layout", "quality-settings", "optional-engine", "diagnostics-effects", "keyboard-settings" },
            new[] { "select", "head-view", "combat-ground-head", "drag-select", "table-move", "rotate-zoom", "tilt", "radial-left", "radial-weapons", "radial-left-modes", "combat-inspect", "combat-status", "tactical-info", "wheel-cards", "end-turn", "radial-right", "pause", "secondary", "overlay", "overlay-navigation", "context-reminder", "diagnostics-effects", "keyboard-settings" },
            new[] { "native-panels", "dialogue-camera", "table-move", "rotate-zoom", "tilt", "hud-layout", "secondary", "overlay", "overlay-navigation", "keyboard-settings" },
            new[] { "native-panels", "weapon-comparison", "wheel-cards", "radial-right", "hud-layout", "secondary", "overlay", "overlay-navigation", "quality-settings", "keyboard-settings" },
            new[] { "navigation-space", "map-layout", "native-panels", "radial-right", "secondary", "overlay", "overlay-navigation", "keyboard-settings" },
            new[] { "navigation-space", "map-layout", "native-panels", "radial-right", "secondary", "overlay", "overlay-navigation", "keyboard-settings" },
            new[] { "space-battle", "select", "head-view", "table-move", "rotate-zoom", "tilt", "radial-left", "radial-weapons", "radial-left-modes", "combat-inspect", "combat-status", "wheel-cards", "end-turn", "radial-right", "pause", "secondary", "overlay", "overlay-navigation", "context-reminder", "keyboard-settings" }
        };
        internal static readonly TouchContextBinding[] ContextBindings = {
            new TouchContextBinding("distance", "draw-distance", "Left thumbrest + right stick · draw distance", "REST+R:Y", "Draw distance"),
            new TouchContextBinding("context-reminder", "context-reminder", "Right stick click · show / hide controls", "R:PRESS", "Click · controls"),
            new TouchContextBinding("compare", "weapon-comparison", "Y · compare pointed weapon", "Y", "Compare weapon"),
            new TouchContextBinding("click", "select", "Right trigger · select / click", "A/RT", "Select"),
            new TouchContextBinding("ground-click", "select", "Right trigger · select / move", "RT", "Select / move"),
            new TouchContextBinding("order", "select", "Right trigger · native tactical order", "RT", "Order"),
            new TouchContextBinding("move", "group-move", "Right stick · move group", "R:XY", "Move · click 1 s: group"),
            new TouchContextBinding("pause", "pause", "X · pause / resume", "X", "Pause"),
            new TouchContextBinding("table", "table-move", "Grip + move hand · move table", "LG/RG", "Move table"),
            new TouchContextBinding("scale", "rotate-zoom", "Both grips · rotate / zoom", "LG+RG", "Rotate / zoom"),
            new TouchContextBinding("orbit", "tilt", "Left stick · rotate / tilt", "L:X", "Rotate / tilt"),
            new TouchContextBinding("wheels", "radial-left", "Grip + trigger · wheel (0.15 s)", "GR+T", "Wheels"),
            new TouchContextBinding("settings", "overlay", "F1 / four controls 1 s · settings", "LT+LG+RT+RG", "1 s · VR settings"),
            new TouchContextBinding("head-turn", "head-view", "LEFT stick · turn first-person view", "L", "Turn view"),
            new TouchContextBinding("head", "head-view", "B / bring gripped hands together · exit head view", "B", "Exit first person"),
            new TouchContextBinding("ground-head", "combat-ground-head", "Full right trigger 3 s on ground · move + head view", "RT 3s", "First-person move"),
            new TouchContextBinding("back", "secondary", "B · back / cancel", "B", "Cancel / back"),
            new TouchContextBinding("scroll", "native-panels", "Right stick · scroll native panel", "R:Y", "Scroll"),
            new TouchContextBinding("map", "navigation-space", "Right trigger · native selection / travel", "RT", "Select / travel"),
            new TouchContextBinding("map-zoom", "navigation-space", "Right stick · pan / left stick · zoom", "R/L", "Pan / zoom"),
            new TouchContextBinding("local", "exploration-shortcuts", "Right stick · pan map", "R", "Pan map"),
            new TouchContextBinding("local-zoom", "exploration-shortcuts", "Left stick · map zoom / rotation", "L", "Zoom / rotate map"),
            new TouchContextBinding("local-back", "exploration-shortcuts", "B / left stick click · close map", "B", "Close map"),
            new TouchContextBinding("inspect", "combat-inspect", "LEFT stick / RIGHT pointer + hold RIGHT grip · inspect actor", "RG:PRESS", "Hold right grip · card"),
            new TouchContextBinding("inspect-keep", "wheel-cards", "Keep RIGHT grip held · keep information", "RG:PRESS", "Hold · keep card"),
            new TouchContextBinding("mode", "radial-left-modes", "Left stick click · switch wheel mode", "L:PRESS", "Click · mode"),
            new TouchContextBinding("release", "radial-left", "Release grip / trigger · close wheel", "GR+T", "Release · close"),
            new TouchContextBinding("preview-left", "wheel-cards", "Point at ability + hold RIGHT grip · original information", "RG:PRESS", "Hold · ability information"),
            new TouchContextBinding("confirm-left", "radial-left", "A / right trigger · use ability", "A/RT", "Use ability"),
            new TouchContextBinding("preview-right", "radial-right", "Right stick / left pointer · menu preview", "R:XY", "Choose menu"),
            new TouchContextBinding("confirm-right", "radial-right", "LEFT trigger · open pointed menu", "LT:PRESS", "Open option"),
            new TouchContextBinding("party", "radial-left", "Hold left stick 1 s / right trigger · select", "L/RT", "1 s / click · select"),
            new TouchContextBinding("menus", "radial-right", "Hold right stick 1 s / left trigger · open", "R/LT", "1 s / click · open"),
            new TouchContextBinding("overlay-point", "overlay-navigation", "Point + A / right trigger · click controls", "A/RT", "Activate"),
            new TouchContextBinding("overlay-stick", "overlay-navigation", "Right stick · navigate / change", "R", "Navigate / adjust"),
            new TouchContextBinding("overlay-buttons", "overlay-navigation", "A · confirm    B · back", "A/B", "Confirm / back"),
            new TouchContextBinding("ships", "space-battle", "Left wheel · ship actions / ships", "L", "Ship actions / ships"),
            new TouchContextBinding("deep", "head-view", "Full right trigger 2 s · first person", "RT:PRESS 2s", "Full hold · first person"),
            new TouchContextBinding("frame", "select", "Double right trigger · frame unit", "RT:PRESS x2", "Frame character / ship"),
            new TouchContextBinding("box", "select", "Right trigger drag · group selection", "RT", "Drag · select group"),
            new TouchContextBinding("map-shortcut", "exploration-shortcuts", "Left stick click · map", "L:PRESS", "Click left · map"),
            new TouchContextBinding("group-frame", "exploration-shortcuts", "RIGHT stick click 1 s · frame group", "R:PRESS 1s", "Hold 1 s · frame group"),
            new TouchContextBinding("highlight", "exploration-shortcuts", "A · nearby interaction", "A", "Nearby interaction / wheel"),
            new TouchContextBinding("highlight-y", "exploration-shortcuts", "Hold button Y · objects and interactions", "Y:PRESS", "Hold · objects and interactions"),
            new TouchContextBinding("tactical-info", "tactical-info", "Hold Y · tactical information", "Y:PRESS", "Hold · tactical information"),
            new TouchContextBinding("map-pan", "navigation-space", "Right stick · pan", "R:XY", "Right stick · pan"),
            new TouchContextBinding("map-scale", "navigation-space", "Left stick · zoom", "L:Y", "Left stick · zoom")
        };
        internal static TouchControlAssignment Find(string id)
        {
            foreach (var item in All) if (item.Id == id) return item;
            throw new InvalidOperationException("Missing canonical Touch assignment: " + id);
        }
        internal static string ContextBinding(string id)
        {
            foreach (var item in ContextBindings)
                if (item.Id == id) { Find(item.AssignmentId); return item.Glyph + " · " + ModLocalization.Text(item.CompactSource); }
            throw new InvalidOperationException("Missing canonical context binding: " + id);
        }
        internal static string ContextTitle(TouchHintContext context)
        {
            switch (context)
            {
                case TouchHintContext.Exploration: case TouchHintContext.HeadView: return "On-foot exploration";
                case TouchHintContext.GroundCombat: case TouchHintContext.CombatHeadView: return "Ground combat";
                case TouchHintContext.Dialogue: case TouchHintContext.DialoguePanel: return "Dialogue and cutscenes";
                case TouchHintContext.Paused: return "Pause";
                case TouchHintContext.Management: return "Management screens";
                case TouchHintContext.GalacticMap: return "Star map · warp routes";
                case TouchHintContext.StarSystemMap: return "Galactic map · system planets";
                case TouchHintContext.SpaceCombat: return "Space combat";
                case TouchHintContext.LocalMap: return "Local map";
                case TouchHintContext.Settings: return "VR settings";
                case TouchHintContext.Welcome: return "Welcome aboard";
                case TouchHintContext.CombatActors: return "Combatants · information";
                case TouchHintContext.PartyWheel: return "Party wheel";
                case TouchHintContext.MenuWheel: return "Menu wheel";
                case TouchHintContext.LeftAbilities: case TouchHintContext.RightAbilities: return "Abilities · preview and confirm";
                case TouchHintContext.Tutorial: return "Tutorial";
                default: return "";
            }
        }
        internal static string ContextBody(TouchHintContext context, bool combatHead)
        {
            switch (context)
            {
                case TouchHintContext.Exploration: return Lines("ground-click", "move", "frame", "deep", "box", "table", "scale", "orbit", "wheels", "map-shortcut", "highlight", "highlight-y", "back", "group-frame", "pause", "settings");
                case TouchHintContext.GroundCombat: return combatHead ? Lines("order", "frame", "deep", "ground-head", "tactical-info", "table", "scale", "orbit", "wheels", "back", "pause", "settings") : Lines("order", "frame", "deep", "tactical-info", "table", "scale", "orbit", "wheels", "back", "pause", "settings");
                case TouchHintContext.HeadView: return Lines("ground-click", "move", "head-turn", "wheels", "head", "settings");
                case TouchHintContext.CombatHeadView: return Lines("order", "head-turn", "wheels", "settings", "head");
                case TouchHintContext.Dialogue: return Lines("click", "scroll", "back", "table", "orbit", "settings");
                case TouchHintContext.Management: return Lines("click", "scroll", "back", "compare", "settings");
                case TouchHintContext.Tutorial: case TouchHintContext.DialoguePanel: return Lines("click", "scroll", "back", "settings");
                case TouchHintContext.Paused: return Lines("click", "pause", "settings", "table", "orbit", "wheels");
                case TouchHintContext.GalacticMap: return Lines("map", "map-pan", "map-scale", "wheels", "back", "settings");
                case TouchHintContext.StarSystemMap: return Lines("map", "map-pan", "map-scale", "wheels", "back", "settings");
                case TouchHintContext.SpaceCombat: return Lines("order", "frame", "deep", "table", "scale", "ships", "orbit", "wheels", "back", "pause", "settings");
                case TouchHintContext.LocalMap: return Lines("local", "local-zoom", "local-back", "settings");
                case TouchHintContext.Settings: case TouchHintContext.Welcome: return Lines("overlay-point", "overlay-stick", "back");
                case TouchHintContext.CombatActors: return Lines("inspect", "mode", "scroll", "release");
                case TouchHintContext.PartyWheel: return Lines("party", "mode", "release");
                case TouchHintContext.MenuWheel: return Lines("menus", "release");
                case TouchHintContext.LeftAbilities: return Lines("preview-left", "confirm-left", "scroll", "release");
                case TouchHintContext.RightAbilities: return Lines("preview-right", "confirm-right", "release");
                default: return "";
            }
        }
        internal static string ContextGlyphLabel(string token)
        {
            if(token=="GR") return "[LG]/[RG]";
            if(token=="2GR") return "[LG]+[RG]";
            if(token=="GR+T") return "[LG]+[LT]";
            return System.Text.RegularExpressions.Regex.Replace(token, @"\b((?:RT|LT|RG|LG|L|R|A|B|X|Y|F1|REST|MENU)(?::(?:PRESS|XY|X|Y|CW|CCW|ROTATE))?)\b", "[$1]");
        }
        internal static string ContextCommon(TouchHintContext context) =>
            ContextGlyphLabel("R")+" · "+ModLocalization.Text("Click · controls")+"   [F1] · "+ModLocalization.Text("VR settings");
        static string Lines(params string[] ids)
        {
            var text = new StringBuilder(256);
            for (int i = 0; i < ids.Length; ++i)
            { if (i != 0) text.Append("\n"); text.Append(ContextBinding(ids[i])); }
            return text.ToString();
        }
    }
}
