using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace RTMaquetaXR
{
    // Only owned unlit, non-postprocessed capture cameras use these stacks.
    // A retained default stack prevents four irrelevant world-volume blends
    // per frame. An explicit lease restores the caller's selected stack and
    // delays destruction while a nested render still references it.
    internal sealed class HudCaptureVolumeState
    {
        internal readonly VolumeManager Manager;
        internal readonly VolumeStack Stack;
        internal int Users;
        internal bool Retired, Destroyed;
        internal HudCaptureVolumeState(Camera camera)
        {
            Manager = VolumeManager.instance;
            Stack = Manager.CreateStack();
            try { Manager.Update(Stack, camera.transform, (LayerMask)0); }
            catch { Manager.DestroyStack(Stack); Destroyed = true; throw; }
        }
        internal void Retire() { Retired = true; TryDestroy(); }
        internal void TryDestroy()
        {
            if (!Retired || Destroyed || Users != 0) return;
            if (ReferenceEquals(Manager.stack, Stack)) Manager.ResetMainStack();
            Manager.DestroyStack(Stack); Destroyed = true;
        }
    }
    public static partial class Main
    {
        struct HudVolumeScope
        {
            internal HudCaptureVolumeState State;
            internal VolumeStack Previous;
        }
        struct HudVolumeWrapperState
        {
            internal int Depth;
            internal bool Entered;
            internal HudCaptureVolumeState EnclosingCapture;
        }
        static readonly Dictionary<Camera, HudCaptureVolumeState> _hudVolumeStates = new Dictionary<Camera, HudCaptureVolumeState>();
        static readonly List<HudVolumeScope> _hudVolumeScopes = new List<HudVolumeScope>(8);
        static FieldInfo _hudVolumeModeField, _hudVolumeStackField;
        static bool _hudVolumeIsolationInstalled;
        static int _hudVolumeWrapperDepth;
        static long _hudVolumeStacksCreated, _hudVolumeUpdatesAvoided, _hudVolumeStacksRetired;

        static void InstallHudVolumeIsolation()
        {
            if (_hudVolumeIsolationInstalled) return;
            Type pipeline = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.WaaaghPipeline");
            Type data = AccessTools.TypeByName("Owlcat.Runtime.Visual.Waaagh.WaaaghAdditionalCameraData");
            if (pipeline == null || data == null) throw new MissingMemberException("UI volume pipeline unavailable");
            _hudVolumeModeField = AccessTools.Field(data, "m_VolumeFrameworkUpdateModeOption");
            _hudVolumeStackField = AccessTools.Field(data, "m_VolumeStack");
            if (_hudVolumeModeField == null || !_hudVolumeModeField.FieldType.IsEnum ||
                Convert.ToInt32(Enum.Parse(_hudVolumeModeField.FieldType, "ViaScripting")) != 1 ||
                _hudVolumeStackField == null || _hudVolumeStackField.FieldType != typeof(VolumeStack))
                throw new MissingMemberException("UI volume ownership contract changed");
            var volume = AccessTools.Method(pipeline, "UpdateVolumeFramework", new[] { typeof(Camera), data });
            var standalone = AccessTools.Method(pipeline, "RenderSingleCamera", new[] { typeof(ScriptableRenderContext), typeof(Camera) });
            var stack = AccessTools.Method(pipeline, "RenderCameraStack", new[] { typeof(ScriptableRenderContext), typeof(List<>).MakeGenericType(data) });
            if (volume == null || !volume.IsStatic || volume.ReturnType != typeof(void) ||
                standalone == null || standalone.IsStatic || standalone.ReturnType != typeof(void) ||
                stack == null || stack.IsStatic || stack.ReturnType != typeof(void))
                throw new MissingMemberException("UI volume render scopes changed");
            var begin = new HarmonyMethod(typeof(Main), nameof(BeginHudVolumeWrapper));
            var end = new HarmonyMethod(typeof(Main), nameof(EndHudVolumeWrapper));
            var harmony = new Harmony("RTMaquetaXR.HudVolumeIsolation");
            try
            {
                harmony.Patch(standalone, prefix: begin, finalizer: end);
                harmony.Patch(stack, prefix: begin, finalizer: end);
                harmony.Patch(volume, prefix: new HarmonyMethod(typeof(Main), nameof(HudVolumeUpdatePrefix)));
            }
            catch { harmony.UnpatchAll("RTMaquetaXR.HudVolumeIsolation"); throw; }
            _hudVolumeIsolationInstalled = true;
        }
        static void CreateHudVolumeState(Camera camera, Component data)
        {
            InstallHudVolumeIsolation();
            var state = new HudCaptureVolumeState(camera);
            try
            {
                _hudVolumeModeField.SetValue(data, Enum.ToObject(_hudVolumeModeField.FieldType, 1));
                _hudVolumeStackField.SetValue(data, state.Stack);
                _hudVolumeStates.Add(camera, state); ++_hudVolumeStacksCreated;
            }
            catch { state.Retire(); throw; }
        }
        // Capturing the depth at both native wrappers also covers failures in
        // camera-data initialization, before the ordinary render-worker scope.
        static void BeginHudVolumeWrapper(out HudVolumeWrapperState __state)
        {
            __state = new HudVolumeWrapperState { Depth = _hudVolumeScopes.Count, Entered = true };
            // A nested original camera executes its native volume update and
            // does not add a private UI scope. Remember an actually selected
            // enclosing capture so that original camera cannot leave world
            // volumes selected when the suspended UI render resumes. The
            // enclosing scope keeps this stack alive even if retired inside.
            for (int i = _hudVolumeScopes.Count - 1; i >= 0; --i)
            {
                HudCaptureVolumeState enclosing = _hudVolumeScopes[i].State;
                if (!enclosing.Destroyed && ReferenceEquals(enclosing.Manager.stack, enclosing.Stack))
                { __state.EnclosingCapture = enclosing; break; }
            }
            ++_hudVolumeWrapperDepth;
        }
        static bool HudVolumeUpdatePrefix(Camera __0)
        {
            if (_hudVolumeWrapperDepth <= 0 || __0 == null ||
                !_hudVolumeStates.TryGetValue(__0, out HudCaptureVolumeState state) || state.Retired) return true;
            _hudVolumeScopes.Add(new HudVolumeScope { State = state, Previous = state.Manager.stack });
            ++state.Users; state.Manager.stack = state.Stack; ++_hudVolumeUpdatesAvoided;
            // The standalone wrapper passes null AdditionalCameraData to this
            // method. Selecting our already-initialized stack directly handles
            // that route too, without affecting any original game camera.
            return false;
        }
        static Exception EndHudVolumeWrapper(Exception __exception, HudVolumeWrapperState __state)
        {
            if (!__state.Entered) return __exception;
            try
            {
                for (int i = _hudVolumeScopes.Count - 1; i >= __state.Depth; --i)
                {
                    HudVolumeScope scope = _hudVolumeScopes[i];
                    _hudVolumeScopes.RemoveAt(i);
                    if (ReferenceEquals(scope.State.Manager.stack, scope.State.Stack))
                        scope.State.Manager.stack = scope.Previous;
                    --scope.State.Users; scope.State.TryDestroy();
                }
            }
            finally
            {
                try
                {
                    HudCaptureVolumeState enclosing = __state.EnclosingCapture;
                    if (enclosing != null && !enclosing.Destroyed && enclosing.Users > 0 &&
                        !ReferenceEquals(enclosing.Manager.stack, enclosing.Stack))
                        enclosing.Manager.stack = enclosing.Stack;
                }
                finally { _hudVolumeWrapperDepth = Math.Max(0, _hudVolumeWrapperDepth - 1); }
            }
            return __exception;
        }
        static void ReleaseHudVolumeState(Camera camera)
        {
            if (camera == null || !_hudVolumeStates.TryGetValue(camera, out HudCaptureVolumeState state)) return;
            _hudVolumeStates.Remove(camera);
            try
            {
                var data = camera.GetComponent(_camDataType);
                if (data != null && ReferenceEquals(_hudVolumeStackField.GetValue(data), state.Stack))
                    _hudVolumeStackField.SetValue(data, null);
            }
            finally { state.Retire(); ++_hudVolumeStacksRetired; }
        }
        internal static object HudVolumeSnapshot() => new {
            ActiveStacks = _hudVolumeStates.Count, OpenScopes = _hudVolumeScopes.Count,
            StacksCreated = _hudVolumeStacksCreated, UpdatesAvoided = _hudVolumeUpdatesAvoided,
            StacksRetired = _hudVolumeStacksRetired,
            Scope = "Owned unlit HUD/spatial cameras only; retained default volumes; previous stack restored per native camera wrapper"
        };
    }
}
