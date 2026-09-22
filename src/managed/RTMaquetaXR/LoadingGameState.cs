using System;
using HarmonyLib;
using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static Harmony _loadingStateHooks;
        static object _loadingStateOwner;
        static bool _loadingAwaitingInput;
        static Action<object> _loadingCloseWait;
        static Func<object, object> _loadingViewModel;
        static Func<object, bool> _loadingReadyModel;
        static string _loadingContinuationFailure;
        static bool _loadingAutoContinued;
        static float _loadingGameProgress = -1;
        // Observe the native UI. Do not infer readiness from 100% or release the
        // game's loading/network lock. Call its original CloseWait action only
        // after the native view explicitly reports readiness.
        internal static void InstallLoadingStateHooks()
        {
            if (_loadingStateHooks != null) return;
            try
            {
                var type = AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.View.LoadingScreen.LoadingScreenBaseView");
                _loadingCloseWait=(Action<object>)TouchSelectionCallFactory.Build(typeof(Action<object>),
                    TouchSelectionCallFactory.ExactMethod(type,"CloseWait",typeof(void),false));
                var model = AccessTools.TypeByName("Kingmaker.Code.UI.MVVM.VM.LoadingScreen.LoadingScreenVM");
                _loadingViewModel = (Func<object,object>)TouchSelectionCallFactory.Build(typeof(Func<object,object>),
                    TouchSelectionCallFactory.ExactMethod(type,"get_ViewModel",model,false));
                var readyField = AccessTools.Field(model,"NeedUserInput");
                if (readyField == null) throw new MissingFieldException("LoadingScreenVM.NeedUserInput");
                var readyProperty = TouchSelectionCallFactory.FieldGetter(readyField);
                var readyValue = (Func<object,bool>)TouchSelectionCallFactory.Build(typeof(Func<object,bool>),
                    TouchSelectionCallFactory.ExactMethod(readyField.FieldType,"get_Value",typeof(bool),false));
                _loadingReadyModel = instance => { var property = readyProperty(instance); return property != null && readyValue(property); };
                string[] names = { "Show", "Hide", "DestroyViewImplementation", "SetProgress", "ShowUserInputWaiting" };
                string[] callbacks = { nameof(LoadingStateShown), nameof(LoadingStateHidden), nameof(LoadingStateHidden), nameof(LoadingStateProgress), nameof(LoadingStateWaiting) };
                var methods = new System.Reflection.MethodInfo[names.Length];
                for (int i = 0; i < names.Length; ++i)
                {
                    methods[i] = AccessTools.DeclaredMethod(type, names[i], i == 3 ? new[] { typeof(float) } : i == 4 ? new[] { typeof(bool) } : Type.EmptyTypes);
                    if (methods[i] == null || methods[i].ReturnType != typeof(void)) throw new MissingMethodException(names[i]);
                }
                _loadingStateHooks = new Harmony("RTMaquetaXR.LoadingState");
                for (int i = 0; i < methods.Length; ++i)
                    _loadingStateHooks.Patch(methods[i], postfix: new HarmonyMethod(typeof(Main), callbacks[i]),
                        finalizer: i == 1 || i == 2 ? new HarmonyMethod(typeof(Main), nameof(LoadingStateExitFinalizer)) : null);
            }
            catch (Exception error)
            {
                _loadingStateHooks?.UnpatchAll(_loadingStateHooks.Id); _loadingStateHooks = null;
                _log.Error("[loading] State observer unavailable; native loading screen retained: " + error.Message);
            }
        }
        static void LoadingStateShown(object __instance)
        { _loadingStateOwner = __instance; _loadingGameProgress = -1; _loadingAwaitingInput = false; _loadingAutoContinued=false; _loadingContinuationFailure=null; }
        static void LoadingStateHidden(object __instance)
        {
            if (!ReferenceEquals(__instance, _loadingStateOwner)) return;
            _loadingStateOwner = null; _loadingGameProgress = -1; _loadingAwaitingInput = false;
        }
        static void LoadingStateProgress(object __instance, float __0)
        {
            if (!ReferenceEquals(__instance, _loadingStateOwner) || float.IsNaN(__0) || float.IsInfinity(__0)) return;
            _loadingGameProgress = Mathf.Max(_loadingGameProgress, Mathf.Clamp01(__0));
        }
        static void LoadingStateWaiting(object __instance, bool __0)
        {
            // Subscriptions can publish their initial state before Show(). Do
            // not let that stale false value identify a previous loading view.
            if (!ReferenceEquals(__instance, _loadingStateOwner)) return;
            _loadingAwaitingInput = __0;
        }
        static Exception LoadingStateExitFinalizer(object __instance, Exception __exception)
        {
            // Native teardown can throw before our postfix. Never retain a
            // disposed loading view as authority for the VR loading slate.
            // Preserve the original exception; do not release game/load locks.
            if (__exception != null) LoadingStateHidden(__instance);
            return __exception;
        }
        internal static bool ObservedNativeLoading => PresentationTransition && _loadingStateHooks != null && _loadingStateOwner != null;
        internal static void AdvanceReadyLoading()
        {
            if (!_active || !ObservedNativeLoading || _loadingAutoContinued || _loadingCloseWait==null || _loadingContinuationFailure!=null) return;
            try
            {
                if (_loadingStateOwner is UnityEngine.Object native && native == null)
                { LoadingStateHidden(_loadingStateOwner); return; }
                object model = _loadingViewModel == null ? null : _loadingViewModel(_loadingStateOwner);
                if (model == null) { LoadingStateHidden(_loadingStateOwner); return; }
                // Read the same native readiness property consumed by the view.
                // A missed UI callback (or a callback whose animation throws)
                // cannot strand an already-ready game behind our buttonless slate.
                _loadingAwaitingInput = _loadingReadyModel != null && _loadingReadyModel(model);
            }
            catch (Exception error)
            { _loadingContinuationFailure=error.Message; _log.Error("[loading] Native readiness unavailable; native screen retained: "+error.Message); return; }
            if (!_loadingAwaitingInput) return;
            // Native ShowUserInputWaiting is the ready signal, never progress=100%.
            // CloseWait is the original any-key action and retains native/network
            // completion rules; no direct lock release or fabricated load state.
            _loadingAutoContinued=true;
            try { _loadingCloseWait(_loadingStateOwner); }
            catch(Exception error)
            {
                // CloseWait may have partially notified native listeners. Do
                // not retry automatically and duplicate the notification; expose
                // the game's own loading screen instead of an inert custom slate.
                _loadingContinuationFailure=error.Message;
                _log.Error("[loading] Native continuation failed; native screen retained: "+error.Message);
            }
        }
        internal static bool NativeLoadingFallback => PresentationTransition &&
            (TouchOverlayOpen || _loadingStateHooks == null || _loadingFailed || _loadingContinuationFailure!=null || (_loadingStateOwner == null && LoadingScreenShowing()));
        internal static void StopLoadingStateHooks()
        {
            if (!_appQuitting) _loadingStateHooks?.UnpatchAll(_loadingStateHooks.Id);
            _loadingStateHooks = null; _loadingStateOwner = null; _loadingAwaitingInput = false; _loadingGameProgress = -1;
            _loadingViewModel=null; _loadingReadyModel=null; _loadingContinuationFailure=null;
        }
        internal static object LoadingStateSnapshot() => new {
            ObserverInstalled = _loadingStateHooks != null, NativeViewBound = _loadingStateOwner != null,
            AwaitingInput = _loadingAwaitingInput, Progress = _loadingGameProgress, ArtworkFound = _loadingLogo != null,
            ContinuationFailure = _loadingContinuationFailure,
            StereoFrames = _loadingStereoFrames, Failed = _loadingFailed, Color = LoadingColorSnapshot()
        };
    }
}
