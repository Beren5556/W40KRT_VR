namespace RTMaquetaXR
{
    // Private unnamed slots belong to the capture lifetime, not to a particular
    // inventory/world attachment. Pooled UI clones can inherit such a slot.
    internal static class UiLayerOwnership
    {
        internal static int Hud = -1, World = -1, Spatial = -1;
        internal static uint Reserved => Bit(Hud) | Bit(World) | Bit(Spatial);
        internal static uint Bit(int layer) => layer < 0 ? 0u : 1u << layer;
        internal static int NativeLayer(int observed, int isolated, int inherited = 5) =>
            observed == isolated ? (inherited == isolated || inherited < 0 ? 5 : inherited) : observed;
        internal static int Find(uint used, uint named, int minimum)
        {
            uint unavailable = used | named | Reserved;
            for (int n = 31; n >= minimum; --n) if ((unavailable & (1u << n)) == 0) return n;
            return -1;
        }
    }
}
