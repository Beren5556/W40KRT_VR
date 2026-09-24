using System;
using System.Collections.Generic;

namespace RTMaquetaXR
{
    internal sealed class OverlayOption
    {
        string label, description, actionLabel;
        // Option identity and callbacks never change with language. Only our
        // presentation keys are resolved; native game text has no path here.
        internal string LabelKey => label;
        internal string Label { get => LabelProvider != null ? LabelProvider() : ModLocalization.Text(label); set => label = value; }
        internal string Description { get => DescriptionProvider != null ? DescriptionProvider() : ModLocalization.Text(description); set => description = value; }
        internal Func<string> LabelProvider;
        internal Func<string> DescriptionProvider;
        internal Func<string> Value;
        internal Action<int> Change;
        internal Action Action;
        internal bool ActionButton;
        internal string ActionLabel { get => ModLocalization.Text(actionLabel); set => actionLabel = value; }
        internal Func<float> Slider01;
        internal Func<bool> Enabled;
        internal OverlayMenu Menu;
        internal OverlayCommand Command;
    }

    internal enum OverlayCommand { None, Back, Close }

    internal sealed class OverlayMenu
    {
        readonly string title;
        internal string Title => TitleProvider != null ? TitleProvider() : ModLocalization.Text(title);
        internal Func<string> TitleProvider;
        internal readonly List<OverlayOption> Options;
        internal readonly bool Modal;

        internal OverlayMenu(string title, IEnumerable<OverlayOption> options, bool root = false, bool modal = false)
        {
            this.title = title;
            Options = new List<OverlayOption>(options);
            Modal = modal;
            if (modal) return; // A compact modal exposes exactly its visible buttons.
            if (!root) Options.Add(new OverlayOption { Label = "Back to previous menu", Command = OverlayCommand.Back,
                Description = "Return to the previous menu. Each setting description explains when saved changes apply." });
            Options.Add(new OverlayOption { Label = "Close panel", Command = OverlayCommand.Close,
                Description = "Close this panel and return to the game. Hold both triggers and both grips for 2 seconds to open or close it. Release all four before repeating." });
        }
    }

    // Navigation is independent of Unity and never edits a setting while entering,
    // leaving or closing a menu. Keep the parent's selection when returning.
    internal sealed class LiveOverlayNavigation
    {
        struct Parent { internal OverlayMenu Menu; internal int Index; }
        readonly Stack<Parent> parents = new Stack<Parent>();
        OverlayMenu root;
        internal OverlayMenu Menu { get; private set; }
        internal int Index { get; private set; } = -1;
        internal int Revision { get; private set; }
        internal int Depth => parents.Count;
        internal bool Visible => Current != null;
        internal OverlayOption Current => Menu != null && Index >= 0 && Index < Menu.Options.Count ? Menu.Options[Index] : null;

        internal void Reset(OverlayMenu menu) { root = menu; Close(); }
        internal bool OpenMenu(OverlayMenu menu)
        {
            if (root == null || menu == null || menu.Options.Count == 0) return false;
            parents.Clear();
            if (!menu.Modal && !ReferenceEquals(root, menu))
            {
                var route = new List<Parent>();
                if (FindRoute(root, menu, route, new HashSet<OverlayMenu>()))
                    foreach (var parent in route) parents.Push(parent);
                else // The automatic one-page guide is intentionally outside the settings tree.
                    parents.Push(new Parent { Menu = root, Index = 0 });
            }
            Menu = menu; Index = 0; ++Revision; return true;
        }
        static bool FindRoute(OverlayMenu source, OverlayMenu target, List<Parent> route, HashSet<OverlayMenu> visited)
        {
            if (source == null || !visited.Add(source)) return false;
            for (int i = 0; i < source.Options.Count; ++i)
            {
                var child = source.Options[i].Menu;
                if (child == null) continue;
                route.Add(new Parent { Menu = source, Index = i });
                if (ReferenceEquals(child, target) || FindRoute(child, target, route, visited)) return true;
                route.RemoveAt(route.Count - 1);
            }
            return false;
        }
        internal void Close() { parents.Clear(); Menu = null; Index = -1; ++Revision; }
        internal void Cycle()
        {
            if (!Visible)
            {
                parents.Clear(); Menu = root;
                Index = Menu != null && Menu.Options.Count > 0 ? 0 : -1;
                ++Revision; return;
            }
            if (Index == Menu.Options.Count - 1) { Back(); return; }
            ++Index; ++Revision;
        }
        internal void Back()
        {
            if (Menu?.Modal == true || parents.Count == 0) { Close(); return; }
            var parent = parents.Pop(); Menu = parent.Menu; Index = parent.Index; ++Revision;
        }
        internal bool Select(int index)
        {
            if (!Visible || index < 0 || index >= Menu.Options.Count) return false;
            if (Index != index) { Index = index; ++Revision; }
            return true;
        }
        internal bool Change(int direction)
        {
            var option = Current;
            if (direction == 0 || option?.Change == null || (option.Enabled != null && !option.Enabled())) return false;
            option.Change(direction); return true;
        }
        internal bool Execute()
        {
            var option = Current; if (option == null) return false;
            if (option.Command == OverlayCommand.Close) Close();
            else if (option.Command == OverlayCommand.Back) Back();
            else if (option.Menu != null)
            {
                if (option.Menu.Options.Count == 0 || (option.Enabled != null && !option.Enabled())) return false;
                parents.Push(new Parent { Menu = Menu, Index = Index });
                Menu = option.Menu; Index = 0; ++Revision;
            }
            else if (option.Action != null)
            {
                if (option.Enabled != null && !option.Enabled()) return false;
                option.Action();
            }
            else return false; // A/trigger enters or acts; B is the dedicated return control.
            return true;
        }
        internal string ExecuteLabel => ModLocalization.Text(Current?.Menu != null ? "open" :
            Current?.Command == OverlayCommand.Close ? "close" :
            Current?.Command == OverlayCommand.Back ? "back" :
            Current?.Action != null ? (Current.ActionLabel ?? "apply") : "no action");
        internal string CycleLabel => ModLocalization.Text(Visible && Index == Menu.Options.Count - 1 ? (Depth == 0 ? "close" : "back") : "next");
    }

    // Shared dimensions for the stereo canvas and flat-menu presentation.
    internal static class LiveOverlayLayout
    {
        internal const float Width = 1320, Height = 1020, SliderX = 582, SliderWidth = 606;
        internal const int VisibleRows = 10;
        internal static int FirstRow(int selected) => Math.Max(0, selected - VisibleRows + 1);

        internal static bool TryFlatPlacement(float width, float height, out float x, out float y, out float scale)
        {
            x = y = scale = 0;
            if (width <= 0 || height <= 0 || float.IsNaN(width) || float.IsInfinity(width) ||
                float.IsNaN(height) || float.IsInfinity(height)) return false;
            scale = Math.Min(width * .92f / Width, height * .82f / Height);
            x = (width - Width * scale) * .5f;
            y = (height - Height * scale) * .5f;
            return scale > 0;
        }
        internal static int PointerHit(float x, float y, int selected, int count, bool change, bool execute, bool enabled, bool actionButton = false)
        {
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y) || x < 0 || x > Width || y < 0 || y > Height) return -1;
            if (actionButton && enabled && execute && x >= 630 && x < 1130 && y >= 172 && y < 268) return -4;
            int first = FirstRow(selected);
            if (x >= 34 && x < 438 && y >= 91 && y < 451)
            {
                int index = first + (int)((y - 91) / 36);
                return index < count ? index : -1;
            }
            if (enabled && change && y >= 284 && y < 334)
            {
                if (x >= 500 && x < 556) return -2;
                if (x >= 1214 && x < 1270) return -3;
            }
            if (enabled && execute && x >= 500 && x < 1270 && y >= 344 && y < 384) return -4;
            if (y >= 468 && y < 502)
            {
                if (x >= 34 && x < 226 && first > 0) return -5;
                if (x >= 246 && x < 438 && first + VisibleRows < count) return -6;
            }
            return -1;
        }
    }

    internal static class LiveOverlayPolicy
    {
        internal static float Slider(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0;
            return Math.Max(0, Math.Min(1, value));
        }
        internal static bool TryDistance(float near, float far, float scale, out float distance)
            => TryDistance81(near, far, scale, .75f, out distance);
        internal static bool TryDistance81(float near, float far, float scale, float wantedMetres, out float distance)
        {
            distance = 0;
            if (float.IsNaN(near) || float.IsInfinity(near) || float.IsNaN(far) || float.IsInfinity(far) || near < 0 || far <= near)
                return false;
            double gap = (double)far - near;
            double minimum = Math.Min(Math.Max(near * 1.5, 0.001), near + gap * 0.2);
            double maximum = near + gap * 0.7;
            double wanted = float.IsNaN(scale) || float.IsInfinity(scale) ? 1 : Math.Max(0.3, scale * wantedMetres);
            distance = (float)Math.Max(minimum, Math.Min(maximum, wanted));
            return distance > near && distance < far;
        }
    }
}
