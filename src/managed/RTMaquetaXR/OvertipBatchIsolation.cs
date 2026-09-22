using System;
using UnityEngine;
using UnityEngine.UI;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        sealed class OvertipBatchOwner
        {
            internal Transform Parent;
            internal Canvas Canvas, ParentCanvas;
            internal OvertipBatchRaycaster Raycaster;
            internal bool Added, AddedRaycaster;
        }
        static readonly OvertipBatchPolicy<Transform, OvertipBatchOwner> _overTipBatches =
            new OvertipBatchPolicy<Transform, OvertipBatchOwner>(CreateOvertipBatch, ValidOvertipBatch, ReleaseOvertipBatch);
        static Transform _overTipBatchRoot;
        static Canvas _overTipBatchRootCanvas;
        static bool _overTipBatchFault;
        static string _overTipBatchError;
        static int _overTipBatchCanvasCount, _overTipBatchRaycasterCount;
        static long _overTipBatchExisting, _overTipBatchCustomSkipped;

        // Root calls this before UpdateWorldOvertips' early guards; disabling
        // the option, entering flat mode or leaving VR restores owned additions.
        internal static void UpdateOvertipBatchIsolation()
        {
            try
            {
                if (_overTipBatchRoot != _overtipsRoot)
                { StopOvertipBatchIsolation(); _overTipBatchRoot = _overtipsRoot; }
                if (_cfg.isolateOvertipBatches && _overTipBatchRootCanvas == null && _overTipBatchRoot != null)
                    _overTipBatchRootCanvas = _overTipBatchRoot.GetComponent<Canvas>();
                bool enabled = _cfg.isolateOvertipBatches && !_overTipBatchFault && _active && Attached &&
                    EffectiveWorldOvertips && !_modeFlat && _overTipBatchRootCanvas != null;
                _overTipBatches.Begin(enabled, Time.frameCount);
                if (!_cfg.isolateOvertipBatches) { _overTipBatchFault = false; _overTipBatchError = null; }
            }
            catch (Exception error) { FailOvertipBatchIsolation(error); }
        }
        internal static void IsolateWorldOvertipBatches(Transform widget)
        {
            if (!_overTipBatches.Enabled || _overTipBatchFault) return;
            try { _overTipBatches.Observe(widget); }
            catch (Exception error) { FailOvertipBatchIsolation(error); }
        }
        internal static void StopOvertipBatchIsolation()
        {
            try { _overTipBatches.Clear(); _overTipBatchRoot = null; _overTipBatchRootCanvas = null; _overTipBatchFault = false; _overTipBatchError = null; }
            catch (Exception error) { FailOvertipBatchIsolation(error); }
        }
        static void FailOvertipBatchIsolation(Exception error)
        {
            _overTipBatchFault = true; _overTipBatchError = error.Message;
            try { _overTipBatches.Clear(); }
            catch (Exception cleanup) { _overTipBatchError += "; cleanup: " + cleanup.Message; }
            _log.Error("[ui/overtip-batch] Marker batching suspended: " + _overTipBatchError);
        }

        static OvertipBatchOwner CreateOvertipBatch(Transform widget)
        {
            if (widget == null || !(widget is RectTransform) || widget.parent == null || !widget.IsChildOf(_overTipBatchRoot)) return null;
            var owner = new OvertipBatchOwner { Parent = widget.parent, Canvas = widget.GetComponent<Canvas>() };
            if (owner.Canvas != null) { ++_overTipBatchExisting; return owner; }
            owner.ParentCanvas = widget.parent.GetComponentInParent<Canvas>();
            // HUD isolation creates the independent overtip root later in the
            // same LateUpdate. Wait for it; never borrow the panel's batch or
            // raycaster for a marker that is about to move into the world.
            if (owner.ParentCanvas == null || !owner.ParentCanvas.transform.IsChildOf(_overTipBatchRoot)) return null;
            var originalRaycaster = owner.ParentCanvas.GetComponent<GraphicRaycaster>();
            // A custom game's raycaster may add game-specific clipping rules.
            // Preserve it by leaving that marker's Canvas topology unchanged.
            if (originalRaycaster != null && originalRaycaster.GetType() != typeof(GraphicRaycaster))
            { ++_overTipBatchCustomSkipped; return owner; }
            try
            {
                owner.Canvas = widget.gameObject.AddComponent<Canvas>(); owner.Added = true; ++_overTipBatchCanvasCount;
                owner.Canvas.overrideSorting = false;
                owner.Canvas.worldCamera = owner.ParentCanvas.worldCamera;
                owner.Canvas.additionalShaderChannels = owner.ParentCanvas.additionalShaderChannels;
                if (originalRaycaster != null)
                {
                    owner.Raycaster = widget.gameObject.AddComponent<OvertipBatchRaycaster>();
                    owner.AddedRaycaster = true; ++_overTipBatchRaycasterCount;
                    owner.Raycaster.Source = originalRaycaster; owner.Raycaster.SourceCanvas = owner.ParentCanvas;
                }
                return owner;
            }
            catch { ReleaseOvertipBatch(owner); throw; }
        }
        static bool ValidOvertipBatch(Transform widget, OvertipBatchOwner owner)
        {
            if (widget == null || owner == null || widget.parent != owner.Parent || !widget.IsChildOf(_overTipBatchRoot)) return false;
            if (!owner.Added) return owner.Canvas != null || owner.ParentCanvas != null;
            if (owner.Canvas == null || owner.ParentCanvas == null ||
                widget.parent.GetComponentInParent<Canvas>() != owner.ParentCanvas) return false;
            if (owner.Canvas.worldCamera != owner.ParentCanvas.worldCamera) owner.Canvas.worldCamera = owner.ParentCanvas.worldCamera;
            if (owner.Canvas.additionalShaderChannels != owner.ParentCanvas.additionalShaderChannels)
                owner.Canvas.additionalShaderChannels = owner.ParentCanvas.additionalShaderChannels;
            return true;
        }
        static void ReleaseOvertipBatch(OvertipBatchOwner owner)
        {
            if (owner == null || !owner.Added) return;
            // Never destroy an original Canvas, GraphicRaycaster, widget or
            // material. No reparent, sorting override or RectTransform writes.
            if (owner.Raycaster != null)
            { owner.Raycaster.enabled = false; UnityEngine.Object.Destroy(owner.Raycaster); }
            if (owner.AddedRaycaster) { --_overTipBatchRaycasterCount; owner.AddedRaycaster = false; }
            if (owner.Canvas != null) { owner.Canvas.enabled = false; UnityEngine.Object.Destroy(owner.Canvas); }
            --_overTipBatchCanvasCount; owner.Added = false;
        }
        internal static object OvertipBatchSnapshot() => new {
            Requested = _cfg.isolateOvertipBatches, Enabled = _overTipBatches.Enabled,
            WorldRootCanvasReady = _overTipBatchRootCanvas != null,
            Canvases = _overTipBatchCanvasCount, Raycasters = _overTipBatchRaycasterCount,
            Tracked = _overTipBatches.Count, Capacity = OvertipBatchPolicy<Transform, OvertipBatchOwner>.Capacity,
            CapacityReached = _overTipBatches.CapacityReached, ExistingCanvasesPreserved = _overTipBatchExisting,
            CustomRaycastersSkipped = _overTipBatchCustomSkipped, Audits = _overTipBatches.Audited,
            Faulted = _overTipBatchFault, Error = _overTipBatchError,
            Tradeoff = "Optional per-marker Canvas batches; may trade CPU rebuild time for additional draw calls/raycasters"
        };
    }
}
