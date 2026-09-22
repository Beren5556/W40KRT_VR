using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        struct HudTopologyNode
        {
            internal Transform Node, Parent;
            internal GameObject Item;
            internal int Children, Layer;
        }
        static readonly List<HudTopologyNode> _hudTopology = new List<HudTopologyNode>(1024);
        static readonly Dictionary<Transform, int> _hudTopologyIndex = new Dictionary<Transform, int>();
        static readonly List<Transform> _hudPendingBranches = new List<Transform>();
        static readonly List<Transform> _hudReconcileRoots = new List<Transform>();
        static readonly List<Transform> _hudOldBranchNodes = new List<Transform>();
        static readonly List<Transform> _hudAuditChildren = new List<Transform>();
        static readonly List<Transform> _hudEdgeAuditParents = new List<Transform>();
        static readonly HashSet<Transform> _hudEdgeAuditParentSet = new HashSet<Transform>();
        static readonly List<HudTopologyNode> _hudRetiredTopology = new List<HudTopologyNode>();
        static readonly HashSet<Transform> _hudRetiredNodeSet = new HashSet<Transform>();
        static readonly HashSet<Transform> _hudChangedParents = new HashSet<Transform>();
        static readonly HashSet<GameObject> _hudCurrentMembers = new HashSet<GameObject>();
        static readonly HudHierarchyPolicy<Transform> _hudHierarchyCache = new HudHierarchyPolicy<Transform>();
        static bool _hudHierarchyHooksReady, _hudRefreshingHierarchy;
        static Transform _hudKnownRoot, _hudKnownOvertips, _hudKnownGesture, _hudKnownOverlay, _hudKnownRadial, _hudKnownContextHints, _hudKnownWorldInformation;
        static bool _hudKnownWorldOvertips;
        static int _hudAuditCursor;
        static long _hudHierarchyAuditNodes;
        static long _hudChildEdgeAudits, _hudChildEdgesChecked;
        static long _hudReconciledBranches, _hudReconciledNodes, _hudRemovedNodes;

        static void EnsureHudHierarchyHooks()
        {
            if (_hudHierarchyHooksReady) return;
            Type[] args = { typeof(Canvas), typeof(Graphic) };
            var register = AccessTools.Method(typeof(GraphicRegistry), "RegisterGraphicForCanvas", args);
            var unregister = AccessTools.Method(typeof(GraphicRegistry), "UnregisterGraphicForCanvas", args);
            if (register == null || unregister == null)
                throw new MissingMethodException("HUD hierarchy notification contract unavailable");
            _harmony.Patch(register, postfix: new HarmonyMethod(typeof(Main), nameof(HudGraphicRegistered)));
            _harmony.Patch(unregister, postfix: new HarmonyMethod(typeof(Main), nameof(HudGraphicUnregistered)));
            _hudHierarchyHooksReady = true;
        }

        // OnCanvasHierarchyChanged also UNREGISTERS inactive Graphics when an
        // ancestor Canvas toggles. Known nodes use one expected-parent lookup;
        // visibility-only Register/Unregister never scans their ancestry/tree.
        static void HudGraphicRegistered(Canvas __0, Graphic __1)
        {
            if ((_touchRadialShown || _spatialNative != null) && __1 != null)
            {
                if (IsSpatialNode(__1.transform)) { _spatialScanFrame = 0; return; }
                if (__1.gameObject.layer == _spatialLayer) ReleaseSpatialDepartedNode(__1.transform);
            }
            if (_hudLayer < 0 || _hudRefreshingHierarchy || __1 == null) return;
            Transform node = __1.transform;
            bool newMember = !_hudHierarchyCache.Contains(node) && __0 != null && IsHudHierarchyMember(__0.transform);
            _hudHierarchyCache.Observe(node, node.parent, newMember);
            if (newMember) PcHudGraphicRegistered(__1);
        }

        static void HudGraphicUnregistered(Canvas __0, Graphic __1)
        {
            if (_hudLayer < 0 || _hudRefreshingHierarchy || __1 == null) return;
            Transform node = __1.transform;
            _hudHierarchyCache.Observe(node, node.parent, false);
        }

        static void InvalidateHudHierarchy()
        {
            _hudHierarchyCache.Invalidate();
        }

        static bool IsHudHierarchyMember(Transform node) => node != null && !IsSpatialNode(node) &&
            ((_worldInformationRoot70 != null && node.IsChildOf(_worldInformationRoot70.transform)) ||
             (_cfg.uiFullResolution && ((_uiRoot != null && node.IsChildOf(_uiRoot)) ||
             (_touchGestureRoot != null && node.IsChildOf(_touchGestureRoot.transform)) ||
             (_liveRoot != null && node.IsChildOf(_liveRoot.transform)) ||
             (_contextHintRoot != null && node.IsChildOf(_contextHintRoot.transform)) ||
             (_touchRadialRoot != null && node.IsChildOf(_touchRadialRoot.transform)) ||
             (_touchOverUi && !_touchWorldHit68 && _touchPointerRoot != null && node.IsChildOf(_touchPointerRoot.transform)))));

        static bool HudHierarchyNeedsRefresh()
        {
            var root = _cfg.uiFullResolution ? _uiRoot : null;
            var overtips = _cfg.uiFullResolution ? _overtipsRoot : null;
            var gesture = !_cfg.uiFullResolution || _touchGestureRoot == null ? null : _touchGestureRoot.transform;
            var overlay = !_cfg.uiFullResolution || _liveRoot == null ? null : _liveRoot.transform;
            var hints = !_cfg.uiFullResolution || _contextHintRoot == null ? null : _contextHintRoot.transform;
            var radial = !_cfg.uiFullResolution || _touchRadialRoot == null || _spatialFault == null ? null : _touchRadialRoot.transform;
            var information = _worldInformationRoot70 == null ? null : _worldInformationRoot70.transform;
            bool worldOvertips = _cfg.uiFullResolution && EffectiveWorldOvertips;
            if (_hudKnownRoot != root || _hudKnownOvertips != overtips ||
                _hudKnownWorldOvertips != worldOvertips || _hudKnownGesture != gesture || _hudKnownOverlay != overlay || _hudKnownRadial != radial || _hudKnownContextHints != hints)
                InvalidateHudHierarchy();
            if (_hudKnownWorldInformation != information) InvalidateHudHierarchy();
            // Bounded structural audit catches non-Graphic particles, pooled
            // scene nodes and external layer edits too. No added behaviours,
            // full-tree polling or texture reuse in unchanged frames.
            int count = Math.Min(64, _hudTopology.Count);
            for (int i = 0; i < count && !_hudHierarchyCache.Dirty; ++i)
            {
                if (_hudAuditCursor >= _hudTopology.Count) _hudAuditCursor = 0;
                var old = _hudTopology[_hudAuditCursor++]; ++_hudHierarchyAuditNodes;
                if (old.Node == null || !ReferenceEquals(old.Node.parent, old.Parent) ||
                    old.Node.gameObject.layer != WorldHudExpectedLayer(old.Node.gameObject, old.Layer))
                    _hudHierarchyCache.InvalidateBranch(old.Node);
                else if (old.Node.childCount != old.Children)
                    AuditHudChildChanges(old);
            }
            if (_hudHierarchyCache.FullRefreshRequired) return true;
            if (_hudHierarchyCache.Dirty) ReconcileHudHierarchyBranches();
            return false;
        }

        // Adding/removing a popup changes its container's child count, not the
        // role of every sibling below that container. Compare immediate edges
        // and let the existing reconciler handle only the branches that moved.
        // Keep old edges until reconciliation: moved/destroyed children still
        // need their original layer ledger restored in this same capture.
        static void AuditHudChildChanges(HudTopologyNode old)
        {
            ++_hudChildEdgeAudits;
            _hudHierarchyCache.CopyChildrenTo(old.Node, _hudAuditChildren);
            foreach (var child in _hudAuditChildren)
                if (child == null || child.parent != old.Node) _hudHierarchyCache.InvalidateBranch(child);
            int count = old.Node.childCount;
            _hudChildEdgesChecked += _hudAuditChildren.Count + count;
            for (int i = 0; i < count; ++i)
            {
                var child = old.Node.GetChild(i);
                if (!_hudHierarchyCache.HasParent(child, old.Node)) _hudHierarchyCache.InvalidateBranch(child);
            }
            old.Children = count;
            _hudTopology[_hudTopologyIndex[old.Node]] = old;
            _hudAuditChildren.Clear();
        }

        static void ReconcileHudHierarchyBranches()
        {
            ClearHudBranchWork();
            // A destroyed/moved child may have been replaced by an unregistered
            // non-Graphic while its parent's child count stayed identical.
            // Discover those edges BEFORE committing updated parent metadata.
            // Each parent and pending identity is inspected once. A growing
            // queue handles causal reparent chains without copying/revisiting
            // every prior pending branch on each discovery round.
            int auditedParents = 0, pendingCursor = 0;
            _hudHierarchyCache.AppendPendingTo(_hudPendingBranches, ref pendingCursor);
            for (int nextPending = 0; nextPending < _hudPendingBranches.Count; ++nextPending)
            {
                var pending = _hudPendingBranches[nextPending];
                if (_hudTopologyIndex.TryGetValue(pending, out int index))
                    QueueHudEdgeAuditParent(_hudTopology[index].Parent);
                if (pending != null) QueueHudEdgeAuditParent(pending.parent);
                int end = _hudEdgeAuditParents.Count;
                for (; auditedParents < end; ++auditedParents)
                {
                    var parent = _hudEdgeAuditParents[auditedParents];
                    AuditHudChildChanges(_hudTopology[_hudTopologyIndex[parent]]);
                }
                _hudHierarchyCache.AppendPendingTo(_hudPendingBranches, ref pendingCursor);
            }
            // New Graphics can arrive below new non-Graphic containers. Include
            // the highest new ancestor, but never expand a known branch to the
            // complete UI root just because one pooled child was added.
            foreach (var pending in _hudPendingBranches)
            {
                var root = pending;
                if (root != null && !_hudHierarchyCache.Contains(root))
                    while (root.parent != null && !_hudHierarchyCache.Contains(root.parent) && IsHudHierarchyMember(root.parent))
                        root = root.parent;
                bool covered = false;
                for (int i = _hudReconcileRoots.Count - 1; i >= 0; --i)
                {
                    var existing = _hudReconcileRoots[i];
                    if (ReferenceEquals(root, existing) || (root != null && existing != null && root.IsChildOf(existing)))
                    { covered = true; break; }
                    if (root != null && existing != null && existing.IsChildOf(root)) _hudReconcileRoots.RemoveAt(i);
                }
                if (!covered) _hudReconcileRoots.Add(root);
            }
            // Collect the OLD graph before removing anything. A node may have
            // left/died, or its old children may have moved elsewhere in the HUD.
            // Current-ancestry coalescing only limits native traversal. It must
            // not discard the old branch of a node moved INTO another dirty
            // branch: the two graphs can have different ancestors and roles.
            foreach (var pending in _hudPendingBranches) _hudHierarchyCache.CollectBranch(pending, _hudOldBranchNodes, _hudRetiredNodeSet);
            foreach (var root in _hudReconcileRoots) _hudHierarchyCache.CollectBranch(root, _hudOldBranchNodes, _hudRetiredNodeSet);
            foreach (var node in _hudOldBranchNodes)
                if (_hudTopologyIndex.TryGetValue(node, out int index))
                {
                    var old = _hudTopology[index]; _hudRetiredTopology.Add(old);
                    if (old.Parent != null) _hudChangedParents.Add(old.Parent);
                }
            _hudRefreshingHierarchy = true; _hudHierarchyCache.BeginPartial();
            bool worldChanged = false, completed = false;
            try
            {
                foreach (var old in _hudRetiredTopology)
                {
                    worldChanged |= _hudWorldNodeSet.Remove(old.Node);
                    _hudCurrentMembers.Remove(old.Item);
                    RemoveHudTopologyNode(old.Node);
                }
                foreach (var root in _hudReconcileRoots)
                    if (root != null && IsHudHierarchyMember(root))
                    { worldChanged |= IsolateHudBranch(root); ++_hudReconciledBranches; }
                foreach (var old in _hudRetiredTopology)
                {
                    // A detached old child can still belong to another HUD branch.
                    // Re-adopt it now; never wait for a later registration/frame.
                    if (old.Node != null && !_hudTopologyIndex.ContainsKey(old.Node) && IsHudHierarchyMember(old.Node))
                        worldChanged |= IsolateHudBranch(old.Node);
                    if (!_hudCurrentMembers.Contains(old.Item) && _hudLayers.TryGetValue(old.Item, out int layer))
                    {
                        if (old.Item != null) old.Item.layer = layer;
                        _hudLayers.Remove(old.Item);
                    }
                    if (!_hudTopologyIndex.ContainsKey(old.Node)) ++_hudRemovedNodes;
                }
                // Parent child counts changed, even when the parent itself did not
                // need re-isolation. Updating metadata avoids a redundant rescan on
                // the next bounded audit. All parents are captured before capture.
                foreach (var parent in _hudChangedParents)
                    if (parent != null && _hudTopologyIndex.ContainsKey(parent)) TrackHudHierarchyNode(parent);
                if (worldChanged) InvalidateWorldHudResolution();
                _hudHierarchyCache.EndPartial(); completed = true;
            }
            finally
            {
                _hudRefreshingHierarchy = false;
                // The caller restores/disables capture on a native failure.
                // Also make any later retry explicit and complete; never leave
                // registry notifications suppressed by an abandoned scope.
                if (!completed) _hudHierarchyCache.Abort();
                ClearHudBranchWork();
            }
        }

        static void ClearHudBranchWork()
        {
            _hudPendingBranches.Clear(); _hudReconcileRoots.Clear(); _hudOldBranchNodes.Clear();
            _hudRetiredTopology.Clear(); _hudRetiredNodeSet.Clear(); _hudChangedParents.Clear();
            _hudAuditChildren.Clear();
            _hudEdgeAuditParents.Clear(); _hudEdgeAuditParentSet.Clear();
        }

        static void QueueHudEdgeAuditParent(Transform parent)
        {
            if (parent != null && _hudTopologyIndex.ContainsKey(parent) && _hudEdgeAuditParentSet.Add(parent))
                _hudEdgeAuditParents.Add(parent);
        }

        static bool IsolateHudBranch(Transform root)
        {
            bool worldChanged = false;
            if (root.parent != null) _hudChangedParents.Add(root.parent);
            _hudNodes.Clear(); root.GetComponentsInChildren(true, _hudNodes);
            foreach (var node in _hudNodes)
            {
                if (node == null) continue;
                // A known non-Graphic child can arrive inside a new container
                // without a registry event of its own. Reconcile its actual
                // role in both directions before choosing isolation layers.
                bool world = EffectiveWorldOvertips && _overtipsRoot != null && (node == _overtipsRoot || node.IsChildOf(_overtipsRoot));
                if (world) worldChanged |= _hudWorldNodeSet.Add(node);
                else worldChanged |= _hudWorldNodeSet.Remove(node);
                if (_hudTopologyIndex.TryGetValue(node, out int index))
                {
                    var oldParent = _hudTopology[index].Parent;
                    if (oldParent != null && oldParent != node.parent) _hudChangedParents.Add(oldParent);
                }
            }
            _hudReconciledNodes += _hudNodes.Count;
            IsolateHudNodes();
            return worldChanged;
        }

        static void RemoveHudTopologyNode(Transform node)
        {
            if (!_hudTopologyIndex.TryGetValue(node, out int index)) return;
            int last = _hudTopology.Count - 1;
            if (index != last) { _hudTopology[index] = _hudTopology[last]; _hudTopologyIndex[_hudTopology[index].Node] = index; }
            _hudTopology.RemoveAt(last); _hudTopologyIndex.Remove(node); _hudHierarchyCache.Remove(node);
        }

        static void BeginHudHierarchyRefresh()
        {
            _hudRefreshingHierarchy = true;
            _hudHierarchyCache.Begin();
            _hudTopology.Clear(); _hudTopologyIndex.Clear(); _hudCurrentMembers.Clear(); _hudAuditCursor = 0;
            _hudWorldNodes.Clear(); _hudWorldNodeSet.Clear();
            if (EffectiveWorldOvertips && _overtipsRoot != null)
            {
                _overtipsRoot.GetComponentsInChildren(true, _hudWorldNodes);
                foreach (var node in _hudWorldNodes) _hudWorldNodeSet.Add(node);
            }
        }

        static void TrackHudHierarchyNode(Transform node)
        {
            if (!_hudRefreshingHierarchy || node == null) return;
            var record = new HudTopologyNode { Node = node, Parent = node.parent, Children = node.childCount,
                Item = node.gameObject, Layer = WorldHudTrackedLayer(node.gameObject, node.gameObject.layer) };
            if (_hudTopologyIndex.TryGetValue(node, out int index)) _hudTopology[index] = record;
            else { _hudTopologyIndex.Add(node, _hudTopology.Count); _hudTopology.Add(record); }
            _hudHierarchyCache.Record(node, node.parent);
        }

        static void EndHudHierarchyRefresh()
        {
            InvalidateWorldHudResolution();
            _hudDeadNodes.Clear();
            foreach (var pair in _hudLayers)
                if (pair.Key == null || !_hudCurrentMembers.Contains(pair.Key))
                {
                    if (pair.Key != null) pair.Key.layer = pair.Value;
                    _hudDeadNodes.Add(pair.Key);
                }
            foreach (var item in _hudDeadNodes) _hudLayers.Remove(item);
            _hudKnownRoot = _cfg.uiFullResolution ? _uiRoot : null;
            _hudKnownOvertips = _cfg.uiFullResolution ? _overtipsRoot : null;
            _hudKnownWorldOvertips = _cfg.uiFullResolution && EffectiveWorldOvertips;
            _hudKnownGesture = !_cfg.uiFullResolution || _touchGestureRoot == null ? null : _touchGestureRoot.transform;
            _hudKnownOverlay = !_cfg.uiFullResolution || _liveRoot == null ? null : _liveRoot.transform;
            _hudKnownContextHints = !_cfg.uiFullResolution || _contextHintRoot == null ? null : _contextHintRoot.transform;
            _hudKnownRadial = !_cfg.uiFullResolution || _touchRadialRoot == null || _spatialFault == null ? null : _touchRadialRoot.transform;
            _hudKnownWorldInformation = _worldInformationRoot70 == null ? null : _worldInformationRoot70.transform;
            _hudRefreshingHierarchy = false; _hudHierarchyCache.End();
        }

        static void ClearHudHierarchyCache()
        {
            _hudRefreshingHierarchy = false; _hudHierarchyCache.Clear();
            _hudTopology.Clear(); _hudTopologyIndex.Clear(); _hudCurrentMembers.Clear(); _hudAuditCursor = 0;
            ClearHudBranchWork();
            _hudKnownRoot = _hudKnownOvertips = _hudKnownGesture = _hudKnownOverlay = _hudKnownRadial = _hudKnownContextHints = _hudKnownWorldInformation = null;
        }
    }
}
