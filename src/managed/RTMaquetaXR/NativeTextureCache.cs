using System;
using System.Collections.Generic;

namespace RTMaquetaXR
{
    // Ownership stays with Unity. Call Forget BEFORE releasing/recreating a
    // texture, even if the managed object is reused, and Clear on session exit.
    internal sealed class NativeTextureCache<T> where T : class
    {
        readonly Dictionary<T, IntPtr> pointers = new Dictionary<T, IntPtr>();
        public int Fetches { get; private set; }
        public IntPtr Get(T texture, Func<T, IntPtr> fetch)
        {
            if (ReferenceEquals(texture, null)) return IntPtr.Zero;
            if (pointers.TryGetValue(texture, out var pointer)) return pointer;
            pointer = fetch(texture); ++Fetches;
            if (pointer != IntPtr.Zero) pointers.Add(texture, pointer);
            return pointer;
        }
        public void Forget(T texture) { if (!ReferenceEquals(texture, null)) pointers.Remove(texture); }
        public void Clear() { pointers.Clear(); Fetches = 0; }
    }
}
