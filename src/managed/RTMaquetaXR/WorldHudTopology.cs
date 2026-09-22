using UnityEngine;

namespace RTMaquetaXR
{
    public static partial class Main
    {
        static void RemoveWorldHudLayer(GameObject item)
        {
            if (!_worldHudLayers.TryGetValue(item, out int original)) return;
            if (item != null && item.layer == _worldHudLayer) item.layer = original;
            _worldHudLayers.Remove(item);
        }
        static int WorldHudTrackedLayer(GameObject item, int actual)
        {
            bool owned = _worldHudLayers.TryGetValue(item, out int original);
            return WorldHudPolicy.TrackedLayer(actual, original, _worldHudLayer, owned);
        }

        static int WorldHudExpectedLayer(GameObject item, int tracked)
        {
            return WorldHudPolicy.ExpectedLayer(tracked, _worldHudLayer, _worldHudLayers.ContainsKey(item));
        }
    }
}
