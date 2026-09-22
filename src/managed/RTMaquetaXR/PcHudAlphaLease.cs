using System;
using System.Collections.Generic;
using UnityEngine;

namespace RTMaquetaXR
{
    // CanvasGroup is unique per GameObject. Preserve the game's latest desired
    // alpha, including zero written by a native fade, while suppressing drawing.
    // The managed set_alpha wrapper is intercepted; its injected engine call is
    // untouched. No hierarchy wrappers or every-frame alpha toggles are needed.
    internal sealed class PcHudAlphaLease
    {
        sealed class SharedState
        {
            internal CanvasGroup Group;
            internal bool Owned;
            internal float NativeAlpha;
            internal int Owners, HiddenOwners;
        }
        static readonly Dictionary<CanvasGroup, SharedState> Active = new Dictionary<CanvasGroup, SharedState>();
        static int _ownWrite;
        internal CanvasGroup Group => _state.Group;
        readonly SharedState _state;
        bool _suppressed, _restored;

        internal PcHudAlphaLease(Transform node, bool suppressed)
        {
            var group = node.GetComponent<CanvasGroup>();
            bool owned = group == null;
            if (owned) group = node.gameObject.AddComponent<CanvasGroup>();
            if (group == null) throw new InvalidOperationException("HUD CanvasGroup unavailable");
            if (!Active.TryGetValue(group, out _state))
            {
                _state = new SharedState { Group = group, Owned = owned, NativeAlpha = group.alpha };
                Active.Add(group, _state);
            }
            // Some prefabs put one replaceable bar inside another. Share the
            // native state; disposing either lease must not reveal the other.
            ++_state.Owners;
            SetSuppressed(suppressed);
        }

        // This must run for *native* writes even when the effective alpha is
        // already zero. Otherwise a fade ending at zero would restore stale art.
        internal static void BeforeNativeWrite(CanvasGroup group, ref float value)
        {
            if (_ownWrite != 0 || ReferenceEquals(group, null)) return;
            SharedState state;
            if (!Active.TryGetValue(group, out state)) return;
            state.NativeAlpha = value;
            if (state.HiddenOwners != 0) value = 0;
        }

        internal void SetSuppressed(bool suppressed)
        {
            if (_restored || _suppressed == suppressed) return;
            _suppressed = suppressed;
            _state.HiddenOwners += suppressed ? 1 : -1;
            Write(_state.HiddenOwners != 0 ? 0 : _state.NativeAlpha);
        }

        void Write(float value)
        {
            if (Group == null || Group.alpha == value) return;
            ++_ownWrite;
            try { Group.alpha = value; }
            finally { --_ownWrite; }
        }

        internal void Restore()
        {
            if (_restored) return;
            SetSuppressed(false); _restored = true;
            if (--_state.Owners != 0) return;
            Active.Remove(Group);
            Write(_state.NativeAlpha);
            if (_state.Owned && Group != null) UnityEngine.Object.Destroy(Group);
        }
        internal static void CopyNativeAlphas(GameObject source,GameObject destination)
        {
            var originals=source.GetComponentsInChildren<CanvasGroup>(true);
            var copies=destination.GetComponentsInChildren<CanvasGroup>(true);
            if(originals.Length!=copies.Length)throw new InvalidOperationException("Native widget group hierarchy changed during clone");
            for(int i=0;i<copies.Length;i++)
                copies[i].alpha=Active.TryGetValue(originals[i],out var state)?state.NativeAlpha:originals[i].alpha;
        }
    }
}
