using System;
using System.Collections.Generic;

namespace RTMaquetaXR
{
    // Graphic registry changes are not necessarily hierarchy changes: Unity
    // unregisters inactive Graphics when an ancestor Canvas is toggled too.
    // Compare only captured transform identity/parent, never visibility state.
    internal sealed class HudHierarchyPolicy<T> where T : class
    {
        readonly Dictionary<T, T> parents = new Dictionary<T, T>();
        readonly Dictionary<T, HashSet<T>> children = new Dictionary<T, HashSet<T>>();
        readonly HashSet<T> pending = new HashSet<T>();
        readonly List<T> pendingOrder = new List<T>();
        internal bool FullRefreshRequired { get; private set; } = true;
        internal bool Dirty => FullRefreshRequired || pending.Count != 0;
        internal bool Refreshing { get; private set; }
        internal long Scans { get; private set; }
        internal long Invalidations { get; private set; }
        internal long PartialRefreshes { get; private set; }
        internal int Count => parents.Count;
        internal bool Contains(T node) => node != null && parents.ContainsKey(node);
        internal bool HasParent(T node, T parent) => node != null && parents.TryGetValue(node, out T expected) && ReferenceEquals(expected, parent);
        internal void CopyChildrenTo(T parent, List<T> result)
        {
            result.Clear();
            if (parent != null && children.TryGetValue(parent, out HashSet<T> branch)) result.AddRange(branch);
        }
        internal void Begin() { Refreshing = true; parents.Clear(); children.Clear(); }
        internal void BeginPartial() { Refreshing = true; }
        internal void Record(T node, T parent)
        {
            if (node == null) return;
            if (parents.TryGetValue(node, out T old) && old != null && !ReferenceEquals(old, parent) &&
                children.TryGetValue(old, out HashSet<T> siblings)) siblings.Remove(node);
            parents[node] = parent;
            if (parent != null)
            {
                if (!children.TryGetValue(parent, out HashSet<T> branch)) children.Add(parent, branch = new HashSet<T>());
                branch.Add(node);
            }
        }
        internal void Remove(T node)
        {
            if (node == null) return;
            if (parents.TryGetValue(node, out T parent) && parent != null && children.TryGetValue(parent, out HashSet<T> branch))
            { branch.Remove(node); if (branch.Count == 0) children.Remove(parent); }
            parents.Remove(node); children.Remove(node);
        }
        internal void CollectBranch(T root, List<T> result, HashSet<T> visited)
        {
            if (root == null || !visited.Add(root)) return;
            int start = result.Count; result.Add(root);
            for (int i = start; i < result.Count; ++i)
                if (children.TryGetValue(result[i], out HashSet<T> branch))
                    foreach (T child in branch) if (visited.Add(child)) result.Add(child);
        }
        internal void CopyPendingTo(List<T> result) { result.Clear(); result.AddRange(pendingOrder); }
        // Edge discovery may add another moved branch. Append each pending
        // identity only once instead of copying the growing set every round.
        internal void AppendPendingTo(List<T> result, ref int cursor)
        {
            if (cursor < 0 || cursor > pendingOrder.Count) throw new ArgumentOutOfRangeException(nameof(cursor));
            while (cursor < pendingOrder.Count) result.Add(pendingOrder[cursor++]);
        }
        internal void End() { Refreshing = FullRefreshRequired = false; pending.Clear(); pendingOrder.Clear(); ++Scans; }
        internal void EndPartial() { Refreshing = false; pending.Clear(); pendingOrder.Clear(); ++PartialRefreshes; }
        internal void Abort() { Refreshing = false; Invalidate(); }
        internal void Invalidate() { if (!Dirty) ++Invalidations; FullRefreshRequired = true; }
        internal void InvalidateBranch(T node)
        {
            if (FullRefreshRequired || node == null) return;
            bool wasDirty = Dirty;
            if (pending.Add(node))
            {
                pendingOrder.Add(node);
                if (!wasDirty) ++Invalidations;
            }
        }
        internal static bool Matches(T parent, T expectedParent, int children, int expectedChildren, int layer, int expectedLayer) =>
            ReferenceEquals(parent, expectedParent) && children == expectedChildren && layer == expectedLayer;
        internal void Observe(T node, T parent, bool newMember)
        {
            if (Refreshing || node == null) return;
            if (parents.TryGetValue(node, out T expected))
            {
                if (!ReferenceEquals(parent, expected)) InvalidateBranch(node);
            }
            else if (newMember) InvalidateBranch(node);
        }
        internal void Clear() { parents.Clear(); children.Clear(); pending.Clear(); pendingOrder.Clear(); Refreshing = false; FullRefreshRequired = true; }
    }
}
