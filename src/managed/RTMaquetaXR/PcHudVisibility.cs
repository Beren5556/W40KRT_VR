using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    // Unlike disabling a Selectable or its native CanvasGroup, this preserves
    // native Interactable and all bound models consumed by the wheels.
    public sealed class PcHudPointerFilter : MonoBehaviour, ICanvasRaycastFilter
    {
        internal bool Suppressed = true;
        public bool IsRaycastLocationValid(Vector2 point, Camera camera) => !Suppressed;
    }

    internal sealed class PcHudVisualLease
    {
        internal readonly Transform Root;
        internal Component PartyView;
        internal bool Seen;
        readonly Dictionary<Transform, PcHudAlphaLease> _groups = new Dictionary<Transform, PcHudAlphaLease>();
        readonly List<PcHudPointerFilter> _filters = new List<PcHudPointerFilter>();
        bool _suppressed = true;
        internal bool WorkSuppressed => _suppressed;
        internal PcHudVisualLease(Transform root)
        {
            Root = root;
            // One bounded branch snapshot on binding; native ignoreParentGroups
            // and nested sorting canvases cannot leak hidden chrome/raycasts.
            try
            {
                var groups = root.GetComponentsInChildren<CanvasGroup>(true);
                var canvases = root.GetComponentsInChildren<Canvas>(true);
                Guard(root);
                foreach (var group in groups) if (group != null) Guard(group.transform);
                foreach (var canvas in canvases) if (canvas != null) Guard(canvas.transform);
            }
            catch { Restore(); throw; }
        }
        internal void Guard(Transform node)
        {
            if (node == null || _groups.ContainsKey(node)) return;
            var group = new PcHudAlphaLease(node, _suppressed);
            _groups.Add(node, group);
            var filter = node.gameObject.AddComponent<PcHudPointerFilter>();
            if (filter == null) throw new InvalidOperationException("HUD raycast filter unavailable");
            filter.Suppressed = _suppressed;
            _filters.Add(filter);
        }
        internal void SetSuppressed(bool suppressed)
        {
            if (_suppressed == suppressed) return;
            _suppressed = suppressed;
            foreach (var filter in _filters) if (filter != null) filter.Suppressed = suppressed;
            foreach (var group in _groups.Values) group.SetSuppressed(suppressed);
            Main.PcHudWorkMembershipChanged();
            if (!suppressed) Main.PcHudWorkLeaseReleased(this);
        }
        internal void NewGraphic(Transform node)
        {
            while (node != null && node != Root)
            {
                if (node.GetComponent<CanvasGroup>() != null || node.GetComponent<Canvas>() != null) Guard(node);
                node = node.parent;
            }
        }
        internal void Restore()
        {
            // Destroy is deferred by Unity; remove our effect immediately.
            SetSuppressed(false);
            Main.ReleasePcHudPartyVisibility(this);
            Main.PcHudWorkLeaseReleased(this);
            foreach (var filter in _filters) if (filter != null) UnityEngine.Object.Destroy(filter);
            foreach (var group in _groups.Values) group.Restore();
            _groups.Clear(); _filters.Clear();
        }
        internal void ReleaseBranch(Transform branch)
        {
            var departed=new List<Transform>();
            foreach(var pair in _groups)
                if(pair.Key==branch || pair.Key.IsChildOf(branch)) { pair.Value.Restore(); departed.Add(pair.Key); }
            foreach(var node in departed)_groups.Remove(node);
            for(int i=_filters.Count-1;i>=0;i--)
            {
                var filter=_filters[i];
                if(filter==null || filter.transform==branch || filter.transform.IsChildOf(branch))
                {
                    if(filter!=null) { filter.Suppressed=false; UnityEngine.Object.Destroy(filter); }
                    _filters.RemoveAt(i);
                }
            }
        }
    }

    public static partial class Main
    {
        static PcHudVisibilityContracts _pcHud;
        static bool _pcHudAttempted, _pcHudFault, _pcHudAlphaHookReady, _pcHudAlphaHookAttempted;
        static float _pcHudNext;
        static object _pcHudView, _pcHudCommon;
        static readonly List<PcHudVisualLease> _pcHudLeases = new List<PcHudVisualLease>(12);
        static readonly Dictionary<Transform, PcHudVisualLease> _pcHudRoots = new Dictionary<Transform, PcHudVisualLease>();
        internal static bool PcHudMinimal => _cfg.pcHudMinimal;
        internal static void SetPcHudMinimal(bool value) { _cfg.pcHudMinimal = value; _pcHudNext = 0; MarkSettingsDirty(); }

        static void InstallPcHudAlphaHook()
        {
            if (_pcHudAlphaHookReady) return;
            var setter = AccessTools.PropertySetter(typeof(CanvasGroup), "alpha");
            if (setter == null || setter.GetMethodBody() == null)
                throw new MissingMethodException("CanvasGroup.set_alpha managed wrapper");
            _harmony.Patch(setter, prefix: new HarmonyMethod(typeof(Main), nameof(PcHudAlphaBeforeWrite)));
            _pcHudAlphaHookReady = true;
        }
        internal static void InstallPcHudAlphaSuppression()
        {
            if (_pcHudAlphaHookAttempted) return;
            _pcHudAlphaHookAttempted = true;
            try { InstallPcHudAlphaHook(); }
            catch (Exception error) { _log.Error("[ui/clean-hud] Alpha suppression unavailable; native windows remain operational: " + error.Message); }
        }
        static void PcHudAlphaBeforeWrite(CanvasGroup __instance, ref float __0) => PcHudAlphaLease.BeforeNativeWrite(__instance, ref __0);

        static bool CanReplacePcHudPanel(PcUiPath path)
        {
            switch (path.Replacement)
            {
                case PcHudReplacement.Party: return TouchRadialPartyAvailableForHud;
                // The installed EndTurn button is inside ActionBarView. A
                // missing end-turn route must preserve its enclosing bar too.
                case PcHudReplacement.Actions: return TouchRadialActionsAvailableForHud && TouchRadialEndTurnAvailable;
                case PcHudReplacement.Combatants: return TouchRadialCombatantsAvailableForHud;
                case PcHudReplacement.EndTurn: return TouchRadialEndTurnAvailable;
                default: return TouchRadialMenusAvailable;
            }
        }

        static void RefreshPcUiRoots()
        {
            var game = _pcHud.Game(); var root = game == null ? null : _pcHud.Root(game);
            _pcHudView = root == null ? null : _pcHud.View(root);
            _pcHudCommon = root == null ? null : _pcHud.Common(root);
        }

        internal static void UpdatePcHudPresentation()
        {
            PcHudWorkHeartbeat();
            bool navigationPanel = _active && _modeFlat && InNavigationMap;
            if ((!_attached || _uiRoot == null) && !navigationPanel) { StopPcHudPresentation(); return; }
            if (Time.unscaledTime < _pcHudNext) return;
            _pcHudNext = Time.unscaledTime + .2f;
            try
            {
                if (!_pcHudAttempted)
                {
                    _pcHudAttempted = true;
                    _pcHud = PcHudVisibilityContracts.Create(AccessTools.TypeByName);
                    InstallPcHudPartyVisibility();
                    InstallTouchMenuWindows();
                    _log.Log("[ui/clean-hud] Bound native PC chrome; models stay active, window/dialog/tutorial roots stay visible.");
                }
                if (_pcHud == null) return;
                InstallPcHudAlphaSuppression();
                RefreshPcUiRoots();
                foreach (var lease in _pcHudLeases) lease.Seen = false;
                var paths = InNavigationMap ? _pcHud.NavigationMenus : _pcHud.SurfaceType.IsInstanceOfType(_pcHudView) ? _pcHud.SurfacePanels :
                    _pcHud.SpaceType.IsInstanceOfType(_pcHudView) ? _pcHud.SpacePanels : null;
                // A tutorial/dialog or an explicitly opened service window must
                // not reveal/re-hide every persistent bar. Only chrome paths
                // are leased; the actual tutorial, dialog and window stay live.
                // Navigation/space HUD includes routes, shields, auxiliaries,
                // objectives and native end-turn with no equivalent in our wheels.
                // Only replace the native right-menu chrome in navigation.
                // Map routes, labels, objectives and travel controls stay live.
                bool wheelsAvailable = (InNavigationMap ? TouchRadialMenusAvailable : InSpaceCombat ? !_cfg.spaceHudVisible : PcHudMinimal) && _presentation != null && _pcHudAlphaHookReady;
                if (paths != null) foreach (var path in paths)
                {
                    bool hide = wheelsAvailable && (InSpaceCombat || CanReplacePcHudPanel(path));
                    if(path.RequiresTouch)hide&=TouchInputOwned;
                    var view = path.Read(_pcHudView) as Component;
                    // Flat navigation panels deliberately have no attached
                    // world HUD. These exact native menu paths are sufficient
                    // ownership; do not discard them because _uiRoot is null.
                    // Space has independent static and dynamic canvases. The
                    // exact bound native field proves ownership for both.
                    if (view == null || (!navigationPanel && !InSpaceCombat && !view.transform.IsChildOf(_uiRoot))) continue;
                    PcHudVisualLease lease;
                    if (!_pcHudRoots.TryGetValue(view.transform, out lease))
                    {
                        if (!hide) continue;
                        lease = new PcHudVisualLease(view.transform);
                        _pcHudRoots.Add(view.transform, lease); _pcHudLeases.Add(lease);
                        PcHudWorkMembershipChanged();
                    }
                    lease.Seen = true; lease.SetSuppressed(hide);
                    if (path.Replacement == PcHudReplacement.Party) UpdatePcHudPartyVisibility(lease, view, hide);
                }
                for (int i = _pcHudLeases.Count - 1; i >= 0; --i)
                    if (!_pcHudLeases[i].Seen)
                    {
                        var lease = _pcHudLeases[i]; _pcHudRoots.Remove(lease.Root); lease.Restore(); _pcHudLeases.RemoveAt(i);
                    }
                _pcHudFault = false;
            }
            catch (Exception error)
            {
                RestorePcHudVisualLeases();
                if (!_pcHudFault) _log.Error("[ui/clean-hud] Native chrome unavailable; preserving visible UI and retrying: " + error);
                _pcHudFault = true; _pcHudNext = Time.unscaledTime + 1f;
                if (_pcHud == null) _pcHudAttempted = false;
            }
            // An optional chrome-visibility failure must never disable native
            // window sizing, input and the reachable CLOSE footer again.
            if (_pcHud != null)
                try { UpdateTouchMenuWindow(); }
                catch (Exception error) { StopTouchMenuWindow(); if (!_pcHudFault) _log.Error("[ui/window] " + error); _pcHudFault = true; }
        }

        // Called only for a genuinely new hierarchy member by the existing
        // registry notification. No per-frame walk, no scene/object discovery.
        internal static void PcHudGraphicRegistered(Graphic graphic)
        {
            PcHudWorkMembershipChanged();
            if (graphic!=null && IsRadialStatusNode(graphic.transform)) return;
            if (_pcHudRoots.Count == 0 || graphic == null) return;
            var node = graphic.transform;
            for (var parent = node; parent != null; parent = parent.parent)
            {
                PcHudVisualLease lease;
                if (_pcHudRoots.TryGetValue(parent, out lease)) { lease.NewGraphic(node); return; }
            }
        }

        internal static void StopPcHudPresentation()
        {
            StopTouchMenuWindow();
            RestorePcHudVisualLeases();
            _pcHudView = _pcHudCommon = null; _pcHudNext = 0;
        }
        static void ReleasePcHudStatusBranch(Transform branch)
        {
            foreach(var lease in _pcHudLeases)lease.ReleaseBranch(branch);
            PcHudWorkMembershipChanged();
        }
        static void RestorePcHudVisualLeases()
        {
            foreach (var lease in _pcHudLeases) lease.Restore();
            _pcHudLeases.Clear(); _pcHudRoots.Clear();
        }
    }
}
