using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        struct PointerCameraStamp
        {
            public Camera Camera;
            public Matrix4x4 View, Projection;
            public Rect PixelRect;
            public float Depth, Near, Far;
            public int Display, Mask;
            public RenderTexture Target;
        }
        struct PointerCanvasStamp
        {
            public SavedCanvas Entry;
            public Canvas Canvas;
            public Camera Camera;
            public Matrix4x4 Matrix;
            public Rect Rect;
            public int SortingLayer, SortingOrder;
            public RenderMode Mode;
            public bool Active, Enabled;
        }
        static readonly List<PointerCanvasStamp> _pointerCanvases = new List<PointerCanvasStamp>();
        static readonly Dictionary<string, int> _pointerFallbackReasons = new Dictionary<string, int>();
        static bool _pointerCacheHook, _pointerCacheDisabled;
        static int _pointerFrame = -1, _pointerGeneration, _pointerCapturedGeneration;
        static int _pointerWidth, _pointerHeight, _pointerCandidateUses;
        static EventSystem _pointerEventSystem, _pointerFallbackEventSystem;
        static PointerInputModule _pointerModule;
        static Type _pointerModuleType;
        static bool _pointerModuleCompatible;
        static Vector2 _pointerPosition;
        static RaycastResult _pointerResult;
        static PointerCameraStamp _pointerGameCamera, _pointerPickCamera, _pointerHitCamera;
        static Transform _pointerHitTransform;
        static Matrix4x4 _pointerHitMatrix;
        static Rect _pointerHitRect;
        static bool _pointerHadHit;
        static string _pointerLastStatus = "not-installed";
        static string _pointerDisabledReason;
        static int _pointerCaptureCalls, _pointerCaptures, _pointerQueries, _pointerCacheHits, _pointerCacheMisses;
        static int _pointerFallbacks, _pointerFreshQueries, _pointerVerifications, _pointerMismatches;
        static long _pointerCaptureTicks, _pointerQueryTicks, _pointerFreshTicks;

        internal static void InstallPointerRaycastCache()
        {
            try
            {
                var target = AccessTools.Method(typeof(PointerInputModule), "GetMousePointerEventData", new[] { typeof(int) });
                if (target == null || AccessTools.Field(typeof(PointerInputModule), "m_PointerData") == null || _harmony == null)
                { _pointerCacheHook = false; _pointerLastStatus = "hook-unavailable"; return; }
                // Installation remains safe after UnpatchAll/reload, without adding duplicate postfixes.
                var postfix = AccessTools.Method(typeof(Main), nameof(CaptureGamePointerRaycast));
                var patches = Harmony.GetPatchInfo(target);
                bool installed = false;
                if (patches != null)
                    foreach (var patch in patches.Postfixes)
                        if (patch.owner == _harmony.Id && patch.PatchMethod == postfix) { installed = true; break; }
                if (!installed) _harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                _pointerCacheHook = true; _pointerLastStatus = "waiting-for-mouse-input";
                _log.Log("[performance] Game pointer raycast reuse installed; camera/UI guards and periodic verification enabled.");
            }
            catch (Exception e)
            {
                _pointerCacheHook = false; _pointerLastStatus = "hook-error";
                _log.Error("[performance] Pointer raycast cache unavailable; original picking retained: " + e.Message);
            }
        }

        internal static void InvalidatePointerRaycastCache()
        {
            _pointerFrame = -1; ++_pointerGeneration;
            _pointerCacheDisabled = false; _pointerCandidateUses = 0;
            _pointerDisabledReason = null;
            _pointerEventSystem = null; _pointerModule = null;
            _pointerResult = default(RaycastResult); _pointerHitTransform = null;
            _pointerGameCamera = _pointerPickCamera = _pointerHitCamera = default(PointerCameraStamp);
            _pointerCanvases.Clear(); // Keep capacity for the next attachment.
            _pointerLastStatus = "attachment-invalidated";
        }

        static bool PointerCacheActive() => _cfg.useGamePointerCache && _pointerCacheHook &&
            !_pointerCacheDisabled && _active && _attached && !_modeFlat && !FlatWanted;

        // This exact overload has just raycasted. Process() itself may return without processing
        // mouse input; stamping its postfix would incorrectly make an old result look current.
        static void CaptureGamePointerRaycast(PointerInputModule __instance, int __0, bool __runOriginal,
            Dictionary<int, PointerEventData> ___m_PointerData)
        {
            if (!PointerCacheActive() || __0 != 0 || !__runOriginal) return;
            long started = DiagnosticTimestamp(); ++_pointerCaptureCalls;
            try
            {
                var es = EventSystem.current;
                if (es == null || es.currentInputModule != __instance || !__instance.IsActive()) return;
                var type = __instance.GetType();
                if (_pointerModuleType != type)
                {
                    _pointerModuleType = type;
                    _pointerModuleCompatible = type == typeof(StandaloneInputModule) ||
                        type.FullName == "Kingmaker.UI.Selection.KingmakerInputModule";
                }
                if (!_pointerModuleCompatible || ___m_PointerData == null ||
                    !___m_PointerData.TryGetValue(-1, out var data) || data == null || data.pointerId != -1) return;
                if (_attachedCam == null || _pickCam == null || !PointerPositionEqual(data.position, Input.mousePosition)) return;

                _pointerFrame = -1; // Publish only after the complete snapshot is captured.
                _pointerEventSystem = es; _pointerModule = __instance; _pointerPosition = data.position;
                _pointerResult = data.pointerCurrentRaycast; _pointerHadHit = _pointerResult.gameObject != null;
                _pointerGameCamera = ReadPointerCamera(_attachedCam); _pointerPickCamera = ReadPointerCamera(_pickCam);
                _pointerHitCamera = ReadPointerCamera(_pointerResult.module != null ? _pointerResult.module.eventCamera : null);
                _pointerWidth = Screen.width; _pointerHeight = Screen.height;
                CapturePointerCanvases();
                _pointerHitTransform = _pointerHadHit ? _pointerResult.gameObject.transform : null;
                if (_pointerHitTransform != null)
                {
                    _pointerHitMatrix = _pointerHitTransform.localToWorldMatrix;
                    var rect = _pointerHitTransform as RectTransform;
                    _pointerHitRect = rect != null ? rect.rect : default(Rect);
                }
                _pointerCapturedGeneration = _pointerGeneration;
                _pointerFrame = Time.frameCount; ++_pointerCaptures;
            }
            catch (Exception e) { DisablePointerCache("capture-error", e.Message); }
            finally { if (started != 0 && DiagnosticsRecording) _pointerCaptureTicks += Stopwatch.GetTimestamp() - started; }
        }

        internal static bool TopPointerHitOptimized(ref RaycastResult top)
        {
            long started = DiagnosticTimestamp(); ++_pointerQueries;
            try
            {
                string reason;
                bool reusable;
                try { reusable = CanReusePointerRaycast(out reason); }
                catch (Exception e)
                {
                    DisablePointerCache("lookup-error", e.Message);
                    reusable = false; reason = "lookup-error";
                }
                if (!reusable) return FreshPointerFallback(ref top, reason);
                // A captured miss is usable too. It must not trigger a second complete UI query.
                // Root/target guards cannot prove that every descendant Graphic or CanvasGroup
                // stayed unchanged. These sampled comparisons detect additional divergences;
                // they are not an exhaustive guarantee about all intermediate UI mutations.
                ++_pointerCandidateUses;
                if (_pointerCandidateUses == 1 || _pointerCandidateUses % 120 == 0)
                {
                    ++_pointerVerifications;
                    RaycastResult fresh = default(RaycastResult);
                    bool haveFresh = FreshPointerRaycast(ref fresh);
                    if (haveFresh != _pointerHadHit || fresh.gameObject != _pointerResult.gameObject || fresh.module != _pointerResult.module)
                    {
                        ++_pointerMismatches; CountPointerFallback("verification-mismatch");
                        DisablePointerCache("verification-mismatch", "winner changed between input processing and reticle placement");
                        top = fresh; return haveFresh;
                    }
                }
                top = _pointerResult;
                if (_pointerHadHit) { ++_pointerCacheHits; _pointerLastStatus = "cached-hit"; }
                else { ++_pointerCacheMisses; _pointerLastStatus = "cached-miss"; }
                return _pointerHadHit;
            }
            finally { if (started != 0 && DiagnosticsRecording) _pointerQueryTicks += Stopwatch.GetTimestamp() - started; }
        }

        static bool CanReusePointerRaycast(out string reason)
        {
            reason = "setting-disabled";
            if (!_cfg.useGamePointerCache) return false;
            reason = "hook-unavailable";
            if (!_pointerCacheHook) return false;
            reason = "disabled-until-attach";
            if (_pointerCacheDisabled) return false;
            reason = "mode-inactive";
            if (!_active || !_attached || _modeFlat || FlatWanted) return false;
            reason = "input-not-current";
            if (_pointerFrame != Time.frameCount || _pointerCapturedGeneration != _pointerGeneration) return false;
            var es = EventSystem.current;
            reason = "input-module-changed";
            if (es == null || es != _pointerEventSystem || _pointerModule == null ||
                es.currentInputModule != _pointerModule || !_pointerModule.IsActive()) return false;
            reason = "pointer-moved";
            if (!PointerPositionEqual(_pointerPosition, Input.mousePosition)) return false;
            reason = "camera-changed";
            if (Screen.width != _pointerWidth || Screen.height != _pointerHeight ||
                !PointerCameraEqual(_pointerGameCamera, ReadPointerCamera(_attachedCam)) ||
                !PointerCameraEqual(_pointerPickCamera, ReadPointerCamera(_pickCam))) return false;
            reason = "ui-placement-changed";
            if (!PointerCanvasesCurrent()) { ++_pointerGeneration; _pointerFrame = -1; return false; }
            if (_pointerHadHit)
            {
                reason = "hit-changed";
                if (_pointerResult.gameObject == null || !_pointerResult.gameObject.activeInHierarchy ||
                    _pointerResult.module == null || !_pointerResult.module.IsActive() || _pointerHitTransform == null ||
                    !PointerMatrixEqual(_pointerHitMatrix, _pointerHitTransform.localToWorldMatrix) ||
                    !PointerCameraEqual(_pointerHitCamera, ReadPointerCamera(_pointerResult.module.eventCamera))) return false;
                var rect = _pointerHitTransform as RectTransform;
                if (rect != null && !PointerRectEqual(_pointerHitRect, rect.rect)) return false;
            }
            reason = null; return true;
        }

        static bool FreshPointerFallback(ref RaycastResult top, string reason)
        { CountPointerFallback(reason); return FreshPointerRaycast(ref top); }
        static void CountPointerFallback(string reason)
        {
            ++_pointerFallbacks; _pointerLastStatus = reason;
            _pointerFallbackReasons.TryGetValue(reason, out int count); _pointerFallbackReasons[reason] = count + 1;
        }
        static bool FreshPointerRaycast(ref RaycastResult top)
        {
            long started = DiagnosticTimestamp(); ++_pointerFreshQueries;
            try
            {
                top = default(RaycastResult);
                var es = EventSystem.current;
                if (es == null) return false;
                if (_pickPed == null || _pointerFallbackEventSystem != es)
                { _pickPed = new PointerEventData(es); _pointerFallbackEventSystem = es; }
                _pickPed.position = Input.mousePosition;
                es.RaycastAll(_pickPed, _pickHits);
                if (_pickHits.Count == 0) return false;
                top = _pickHits[0]; return true;
            }
            finally { if (started != 0 && DiagnosticsRecording) _pointerFreshTicks += Stopwatch.GetTimestamp() - started; }
        }
        static void DisablePointerCache(string reason, string detail)
        {
            if (!_pointerCacheDisabled)
                _log.Log("[performance] Pointer raycast reuse disabled until next attachment (" + reason + "): " + detail);
            _pointerCacheDisabled = true; _pointerFrame = -1; _pointerLastStatus = reason; _pointerDisabledReason = reason;
        }

        static PointerCameraStamp ReadPointerCamera(Camera camera)
        {
            if (camera == null) return default(PointerCameraStamp);
            return new PointerCameraStamp { Camera = camera, View = camera.worldToCameraMatrix,
                Projection = camera.projectionMatrix, PixelRect = camera.pixelRect, Depth = camera.depth,
                Near = camera.nearClipPlane, Far = camera.farClipPlane, Display = camera.targetDisplay,
                Mask = camera.cullingMask, Target = camera.targetTexture };
        }
        static bool PointerCameraEqual(PointerCameraStamp a, PointerCameraStamp b) => a.Camera == b.Camera &&
            PointerMatrixEqual(a.View, b.View) && PointerMatrixEqual(a.Projection, b.Projection) &&
            PointerRectEqual(a.PixelRect, b.PixelRect) && a.Depth == b.Depth && a.Near == b.Near && a.Far == b.Far &&
            a.Display == b.Display && a.Mask == b.Mask && a.Target == b.Target;
        static PointerCanvasStamp ReadPointerCanvas(SavedCanvas entry)
        {
            var canvas = entry.canvas;
            if (canvas == null) return new PointerCanvasStamp { Entry = entry };
            var rect = canvas.transform as RectTransform;
            return new PointerCanvasStamp { Entry = entry, Canvas = canvas, Camera = canvas.worldCamera,
                Matrix = canvas.transform.localToWorldMatrix, Rect = rect != null ? rect.rect : default(Rect),
                Active = canvas.gameObject.activeInHierarchy, Enabled = canvas.enabled, Mode = canvas.renderMode,
                SortingLayer = canvas.sortingLayerID, SortingOrder = canvas.sortingOrder };
        }
        static bool PointerCanvasEqual(PointerCanvasStamp a, PointerCanvasStamp b) => ReferenceEquals(a.Entry, b.Entry) &&
            a.Canvas == b.Canvas && a.Camera == b.Camera && PointerMatrixEqual(a.Matrix, b.Matrix) &&
            PointerRectEqual(a.Rect, b.Rect) && a.Active == b.Active && a.Enabled == b.Enabled &&
            a.Mode == b.Mode && a.SortingLayer == b.SortingLayer && a.SortingOrder == b.SortingOrder;
        static void CapturePointerCanvases()
        {
            bool changed = _pointerCanvases.Count != _savedCanvases.Count;
            for (int i = 0; i < _savedCanvases.Count; ++i)
            {
                var stamp = ReadPointerCanvas(_savedCanvases[i]);
                if (i < _pointerCanvases.Count)
                { if (!PointerCanvasEqual(_pointerCanvases[i], stamp)) changed = true; _pointerCanvases[i] = stamp; }
                else _pointerCanvases.Add(stamp);
            }
            if (_pointerCanvases.Count > _savedCanvases.Count)
                _pointerCanvases.RemoveRange(_savedCanvases.Count, _pointerCanvases.Count - _savedCanvases.Count);
            if (changed) ++_pointerGeneration;
        }
        static bool PointerCanvasesCurrent()
        {
            if (_pointerCanvases.Count != _savedCanvases.Count) return false;
            for (int i = 0; i < _savedCanvases.Count; ++i)
                if (!PointerCanvasEqual(_pointerCanvases[i], ReadPointerCanvas(_savedCanvases[i]))) return false;
            return true;
        }
        static bool PointerPositionEqual(Vector2 a, Vector3 b) => a.x == b.x && a.y == b.y;
        static bool PointerRectEqual(Rect a, Rect b) => a.x == b.x && a.y == b.y && a.width == b.width && a.height == b.height;
        static bool PointerMatrixEqual(Matrix4x4 a, Matrix4x4 b)
        { for (int i = 0; i < 16; ++i) if (a[i] != b[i]) return false; return true; }

        static object PointerRaycastSnapshot() => new {
            Enabled = _cfg.useGamePointerCache, HookInstalled = _pointerCacheHook, DisabledUntilAttach = _pointerCacheDisabled,
            DisabledReason = _pointerDisabledReason,
            LastStatus = _pointerLastStatus, Module = _pointerModuleType != null ? _pointerModuleType.FullName : null,
            UiPlacementGeneration = _pointerGeneration, CaptureCalls = _pointerCaptureCalls, Captures = _pointerCaptures, Queries = _pointerQueries,
            CachedHits = _pointerCacheHits, CachedMisses = _pointerCacheMisses, Fallbacks = _pointerFallbacks,
            FreshQueries = _pointerFreshQueries, Verifications = _pointerVerifications, Mismatches = _pointerMismatches,
            VerifyFirstUse = true, VerificationEveryUses = 120, FallbackReasons = new Dictionary<string, int>(_pointerFallbackReasons),
            CaptureCpuMs = PointerMeanMilliseconds(_pointerCaptureTicks, _pointerCaptureCalls),
            QueryCpuMs = PointerMeanMilliseconds(_pointerQueryTicks, _pointerQueries),
            FreshQueryCpuMs = PointerMeanMilliseconds(_pointerFreshTicks, _pointerFreshQueries)
        };
        static double PointerMeanMilliseconds(long ticks, int count) => count == 0 ? 0 :
            Math.Round(ticks * 1000.0 / Stopwatch.Frequency / count, 4);
        static void ResetPointerRaycastWindow()
        {
            _pointerCaptureCalls = _pointerCaptures = _pointerQueries = _pointerCacheHits = _pointerCacheMisses = 0;
            _pointerFallbacks = _pointerFreshQueries = _pointerVerifications = _pointerMismatches = 0;
            _pointerCaptureTicks = _pointerQueryTicks = _pointerFreshTicks = 0;
            _pointerFallbackReasons.Clear();
        }
    }
}
