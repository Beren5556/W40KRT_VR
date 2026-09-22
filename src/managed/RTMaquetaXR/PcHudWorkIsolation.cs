using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        sealed class PcHudDeferredWork
        {
            internal readonly HashSet<Graphic> Graphics = new HashSet<Graphic>();
            internal readonly HashSet<RectTransform> Layouts = new HashSet<RectTransform>();
        }
        static Harmony _pcHudWorkHarmony;
        static bool _pcHudWorkReady, _pcHudWorkFlushPending;
        static string _pcHudWorkFailure;
        static long _pcHudGraphicsDeferred, _pcHudLayoutsDeferred, _pcHudWorkRestored;
        static long _pcHudRegistrationsDeferred;
        static long _pcHudVisibleMembershipHits;
        static int _pcHudVisibleMembershipFrame = -1;
        static readonly HashSet<Transform> _pcHudVisibleMembership = new HashSet<Transform>();
        static readonly Dictionary<Transform, PcHudVisualLease> _pcHudWorkMembership = new Dictionary<Transform, PcHudVisualLease>();
        static readonly Dictionary<PcHudVisualLease, PcHudDeferredWork> _pcHudDeferred = new Dictionary<PcHudVisualLease, PcHudDeferredWork>();
        static void InstallPcHudWorkIsolation()
        {
            if (_pcHudWorkHarmony != null) return;
            try
            {
                _pcHudWorkHarmony = new Harmony("RTMaquetaXR.HiddenHudWork");
                var mark = typeof(LayoutRebuilder).GetMethod("MarkLayoutForRebuild", BindingFlags.Static | BindingFlags.Public,
                    null, new[] { typeof(RectTransform) }, null);
                var register = typeof(CanvasUpdateRegistry).GetMethod("InternalRegisterCanvasElementForGraphicRebuild", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(ICanvasElement) }, null);
                if (mark == null || mark.ReturnType != typeof(void) || mark.GetMethodBody() == null ||
                    register == null || register.ReturnType != typeof(bool) || register.GetMethodBody() == null)
                    throw new MissingMethodException("uGUI dirty registration boundaries");
                // Defer at registration, before ancestor layout searches, queue
                // sorting and native Graphic rebuild dispatch. Keep the later
                // guards for entries queued before a bar becomes hidden.
                _pcHudWorkHarmony.Patch(mark, prefix: new HarmonyMethod(typeof(Main), nameof(PcHudLayoutWorkPrefix)));
                _pcHudWorkHarmony.Patch(register, prefix: new HarmonyMethod(typeof(Main), nameof(PcHudGraphicRegistrationPrefix)));
                foreach (string name in new[] { "PerformLayoutCalculation", "PerformLayoutControl" })
                {
                    var method = typeof(LayoutRebuilder).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new[] { typeof(RectTransform), typeof(UnityAction<Component>) }, null);
                    if (method == null || method.ReturnType != typeof(void) || method.GetMethodBody() == null)
                        throw new MissingMethodException("uGUI layout traversal " + name);
                    _pcHudWorkHarmony.Patch(method, prefix: new HarmonyMethod(typeof(Main), nameof(PcHudLayoutWorkPrefix)));
                }
                foreach (var type in new[] { typeof(Graphic), AccessTools.TypeByName("TMPro.TextMeshProUGUI") })
                {
                    var method = type?.GetMethod("Rebuild", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                        null, new[] { typeof(CanvasUpdate) }, null);
                    if (method == null || method.ReturnType != typeof(void) || method.GetMethodBody() == null)
                        throw new MissingMethodException("uGUI graphic rebuild " + type?.FullName);
                    _pcHudWorkHarmony.Patch(method, prefix: new HarmonyMethod(typeof(Main), nameof(PcHudGraphicWorkPrefix)));
                }
                _pcHudWorkReady = true;
            }
            catch (Exception error)
            {
                _pcHudWorkHarmony?.UnpatchAll(_pcHudWorkHarmony.Id); _pcHudWorkHarmony = null;
                PcHudWorkFailure(error);
            }
        }
        static PcHudVisualLease PcHudSuppressedOwner(Transform node)
        {
            if (!_pcHudWorkReady || !_active || !_attached || !_cfg.pcHudMinimal || _pcHudRoots.Count == 0 || node == null) return null;
            int frame = Time.frameCount;
            if (_pcHudVisibleMembershipFrame != frame)
            { _pcHudVisibleMembership.Clear(); _pcHudVisibleMembershipFrame = frame; }
            if (_pcHudVisibleMembership.Contains(node))
            { ++_pcHudVisibleMembershipHits; return null; }
            if (_pcHudWorkMembership.TryGetValue(node, out var cached))
            {
                // Native pooling may reparent an existing Graphic without a
                // registration callback. Never suppress it outside its lease.
                if (cached != null && cached.WorkSuppressed && cached.Root != null &&
                    (node == cached.Root || node.IsChildOf(cached.Root))) return cached;
                _pcHudWorkMembership.Remove(node);
            }
            PcHudVisualLease owner = null;
            // Registrations also arrive from visible popups and other UI. Bound
            // discovery by our handful of hidden bars, not by every ancestor in
            // their potentially deep native hierarchy. IsChildOf checks current
            // native ancestry, including ancestor-only pooling without callbacks.
            // A visible inner lease does not cancel its hidden outer lease.
            foreach (var candidate in _pcHudRoots.Values)
                if (candidate.WorkSuppressed && candidate.Root != null &&
                    (node == candidate.Root || node.IsChildOf(candidate.Root)))
                { owner = candidate; break; }
            // Bound memory also when temporary tooltip widgets are destroyed.
            if (_pcHudWorkMembership.Count >= 8192) _pcHudWorkMembership.Clear();
            // A non-member hit only retains native work: if it is pooled into
            // a hidden bar in this same frame, the extra native rebuild is safe
            // (the alpha lease still hides it). Recheck next frame. Positive
            // entries must still validate current ancestry on EVERY use, since
            // suppressing a newly visible pooled item would lose its update.
            if (owner != null) _pcHudWorkMembership[node] = owner;
            else
            {
                if (_pcHudVisibleMembership.Count >= 8192) _pcHudVisibleMembership.Clear();
                _pcHudVisibleMembership.Add(node);
            }
            return owner != null && owner.WorkSuppressed ? owner : null;
        }
        static PcHudDeferredWork PcHudWorkFor(PcHudVisualLease owner)
        {
            if (!_pcHudDeferred.TryGetValue(owner, out var work))
            { work = new PcHudDeferredWork(); _pcHudDeferred.Add(owner, work); }
            return work;
        }
        static bool PcHudLayoutWorkPrefix(RectTransform __0)
        {
            try
            {
                var owner = PcHudSuppressedOwner(__0); if (owner == null) return true;
                PcHudWorkFor(owner).Layouts.Add(__0); ++_pcHudLayoutsDeferred; return false;
            }
            catch (Exception error) { PcHudWorkFailure(error); return true; }
        }
        static bool PcHudGraphicWorkPrefix(Graphic __instance, CanvasUpdate __0)
        {
            // Keep non-render stages untouched. Native model/subscription work
            // and property setters continue; only invisible geometry is delayed.
            if (__0 != CanvasUpdate.PreRender && __0 != CanvasUpdate.LatePreRender) return true;
            try
            {
                var owner = PcHudSuppressedOwner(__instance == null ? null : __instance.transform);
                if (owner == null) return true;
                PcHudWorkFor(owner).Graphics.Add(__instance); ++_pcHudGraphicsDeferred; return false;
            }
            catch (Exception error) { PcHudWorkFailure(error); return true; }
        }
        static bool PcHudGraphicRegistrationPrefix(ICanvasElement __0, ref bool __result)
        {
            var graphic = __0 as Graphic;
            if (graphic == null || PcHudGraphicWorkPrefix(graphic, CanvasUpdate.PreRender)) return true;
            ++_pcHudRegistrationsDeferred;
            __result = false;
            return false;
        }
        internal static void PcHudWorkMembershipChanged()
        { _pcHudWorkMembership.Clear(); _pcHudVisibleMembership.Clear(); }
        internal static void PcHudWorkLeaseReleased(PcHudVisualLease owner)
        {
            PcHudWorkMembershipChanged();
            if (!_pcHudDeferred.TryGetValue(owner, out var work)) return;
            _pcHudDeferred.Remove(owner);
            // Rebuild the latest native values once when revealing/restoring.
            // Destroyed pooled graphics are ignored; no synchronous ForceUpdate.
            foreach (var rect in work.Layouts) if (rect != null) LayoutRebuilder.MarkLayoutForRebuild(rect);
            foreach (var graphic in work.Graphics) if (graphic != null) graphic.SetAllDirty();
            ++_pcHudWorkRestored;
        }
        static void PcHudWorkFailure(Exception error)
        {
            _pcHudWorkReady = false; _pcHudWorkFlushPending = true;
            if (_pcHudWorkFailure == null) _log?.Error("[performance/hidden-hud] Native rebuilding retained: " + error.Message);
            _pcHudWorkFailure = error.Message;
        }
        internal static void PcHudWorkHeartbeat()
        {
            if (!_pcHudWorkFlushPending) return;
            _pcHudWorkFlushPending = false;
            // This is outside CanvasUpdateRegistry traversal. Re-registering
            // graphics from inside that traversal would be rejected by uGUI.
            foreach (var owner in new List<PcHudVisualLease>(_pcHudDeferred.Keys)) PcHudWorkLeaseReleased(owner);
        }
        static void StopPcHudWorkIsolation()
        {
            _pcHudWorkReady = false; _pcHudWorkFlushPending = true; PcHudWorkHeartbeat();
            PcHudWorkMembershipChanged(); _pcHudVisibleMembershipFrame = -1;
            _pcHudWorkHarmony?.UnpatchAll(_pcHudWorkHarmony.Id); _pcHudWorkHarmony = null;
        }
        static object PcHudWorkSnapshot() => new {
            Ready = _pcHudWorkReady, Failure = _pcHudWorkFailure, DeferredLayoutTraversals = _pcHudLayoutsDeferred,
            DeferredGraphicStages = _pcHudGraphicsDeferred, RestoredBranches = _pcHudWorkRestored,
            GraphicRegistrationsAvoided = _pcHudRegistrationsDeferred,
            PendingBranches = _pcHudDeferred.Count, CachedMemberships = _pcHudWorkMembership.Count,
            VisibleMembershipCacheHits = _pcHudVisibleMembershipHits, VisibleMembershipFrame = _pcHudVisibleMembershipFrame,
            Scope = "Exact invisible chrome leases: defer hidden rebuilds, validate positive owners every time. Visible misses reuse within one frame only and always retain native work. Models, availability, scripts, controls and windows stay live."
        };
    }
}
