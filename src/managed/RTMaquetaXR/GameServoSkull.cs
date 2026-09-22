using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace RTMaquetaXR
{
    internal sealed class GameServoSkullPart
    {
        internal Mesh Mesh; // Borrowed immutable game mesh. Never destroy or read its CPU vertices.
        internal Material Material; // Owned by the loader; shared by both hands.
        internal Matrix4x4 LocalToHand;
    }

    public static partial class Main
    {
        // Verified in the user's installed game: ServoSkull_02, its cable and
        // their original 512px albedo all reside in this unit bundle.
        const string GameServoSkullAsset = "824da097d3ea9454f929c07c1007bb6a";
        sealed class GameServoLoad
        {
            internal Task<GameObject> Pending;
            internal MethodInfo Free;
            internal bool Held, Cancelled, Failed;
        }
        static GameServoLoad _gameServoLoad;
        static GameServoSkullPart[] _gameServoParts;
        static Material _gameServoMaterial;

        internal static bool TryCreateGameServoSkull(out GameServoSkullPart[] parts)
            => TryPrepareGameServoSkull(false, out parts);
        internal static bool TryGetServoSkullForPortraits(out GameServoSkullPart[] parts)
            => TryPrepareGameServoSkull(true, out parts);
        static bool TryPrepareGameServoSkull(bool helpVisible, out GameServoSkullPart[] parts)
        {
            parts = _gameServoParts;
            if (parts != null) return true;
            if ((!TouchHandsVisible && !helpVisible) || !TouchInputOwned) return false;
            if (_gameServoLoad == null)
            {
                var state = new GameServoLoad(); _gameServoLoad = state;
                state.Pending = LoadGameServoSkullAsync(state);
            }
            var load = _gameServoLoad;
            if (load.Cancelled || load.Failed || !load.Pending.IsCompleted) return false;
            // No wait on the game thread: only inspect a completed async load.
            var prefab = load.Pending.GetAwaiter().GetResult();
            if (prefab == null) { load.Failed = true; return false; }
            try
            {
                _gameServoParts = BuildGameServoSkullParts(prefab);
                parts = _gameServoParts;
                _log.Log("[touch/hands] Original game ServoSkull_02 ready; parts=" + parts.Length +
                    "; vertices=" + (parts[0].Mesh.vertexCount + parts[1].Mesh.vertexCount) +
                    "; texture=" + _gameServoMaterial.GetTexture("_BaseMap").name +
                    "; rigid meshes, no instantiated unit, animation, physics, lights or shadows.");
                return true;
            }
            catch (Exception error)
            {
                load.Failed = true;
                if (_gameServoMaterial != null) UnityEngine.Object.Destroy(_gameServoMaterial);
                _gameServoMaterial = null; _gameServoParts = null; parts = null;
                ReleaseGameServoResource(load);
                _log.Error("[touch/hands] Could not prepare the original game model; input remains available: " + error.Message);
                return false;
            }
        }

        static async Task<GameObject> LoadGameServoSkullAsync(GameServoLoad state)
        {
            try
            {
                var library = AccessTools.TypeByName("Kingmaker.Blueprints.ResourcesLibrary");
                if (library == null) throw new InvalidOperationException("Game resource library not found");
                MethodInfo request = null;
                foreach (var method in library.GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    var p = method.GetParameters();
                    if (method.Name == "TryGetResourceAsync" && method.IsGenericMethodDefinition &&
                        method.GetGenericArguments().Length == 1 && p.Length == 3 &&
                        p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(bool) && p[2].ParameterType == typeof(bool))
                        request = method.MakeGenericMethod(typeof(GameObject));
                }
                state.Free = library.GetMethod("FreeResourceRequest", BindingFlags.Static | BindingFlags.Public,
                    null, new[] { typeof(string), typeof(bool) }, null);
                if (request == null || state.Free == null) throw new MissingMethodException("Game resource request/release contract changed");
                var pending = request.Invoke(null, new object[] { GameServoSkullAsset, true, true }) as Task<GameObject>;
                if (pending == null) throw new InvalidOperationException("Game resource request did not return a task");
                // Unity's synchronization context keeps completion/release on the
                // main thread. Never schedule Unity work on a pool worker.
                var prefab = await pending;
                // The game's completed request increments both counters even
                // when its Resource is null; every successful await needs release.
                state.Held = true;
                if (state.Cancelled) { ReleaseGameServoResource(state); return null; }
                if (prefab == null) throw new InvalidOperationException("Original servo-skull prefab unavailable");
                return prefab;
            }
            catch (Exception error)
            {
                state.Failed = true;
                ReleaseGameServoResource(state);
                if (!state.Cancelled) _log.Error("[touch/hands] Original model load failed; controls remain available: " + error.GetBaseException().Message);
                return null;
            }
        }

        static GameServoSkullPart[] BuildGameServoSkullParts(GameObject prefab)
        {
            var meshes = new Mesh[2]; var transforms = new Matrix4x4[2];
            Material original = null;
            Matrix4x4 rootInverse = prefab.transform.worldToLocalMatrix;
            // Read components from the prefab asset; never instantiate its unit
            // scripts or activate its Animator. Meshes stay in their rest pose.
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer skin) mesh = skin.sharedMesh;
                else if (renderer is MeshRenderer)
                { var filter = renderer.GetComponent<MeshFilter>(); if (filter != null) mesh = filter.sharedMesh; }
                if (mesh == null) continue;
                int index = mesh.name == "ServoSkull_02" ? 0 : mesh.name == "ServoSkull_02_cable" ? 1 : -1;
                if (index < 0 || meshes[index] != null) continue;
                if (mesh.subMeshCount != 1 || mesh.vertexCount <= 0 || mesh.vertexCount > 20000)
                    throw new InvalidOperationException("Unexpected original servo-skull geometry");
                var material = renderer.sharedMaterial;
                if (material == null || (original != null && material != original))
                    throw new InvalidOperationException("Unexpected original servo-skull material layout");
                original = material; meshes[index] = mesh;
                transforms[index] = rootInverse * renderer.transform.localToWorldMatrix;
            }
            if (meshes[0] == null || meshes[1] == null || original == null)
                throw new InvalidOperationException("Original skull and cable renderer pair not found");
            Texture albedo = original.HasProperty("_BaseMap") ? original.GetTexture("_BaseMap") : original.mainTexture;
            Shader shader = Shader.Find("Owlcat/Unlit");
            if (albedo == null || shader == null || !shader.isSupported)
                throw new InvalidOperationException("Original albedo or compatible visual shader unavailable");
            // Preserve the actual game's UVs and detailed painted texture, with a
            // stable material that needs no additional scene lighting or effects.
            var materialCopy = new Material(shader) { name = "RTMaquetaXR original ServoSkull_02", renderQueue = 2000 };
            _gameServoMaterial = materialCopy;
            materialCopy.SetTexture("_BaseMap", albedo);
            if (materialCopy.HasProperty("_MainTex")) materialCopy.SetTexture("_MainTex", albedo);
            materialCopy.SetTextureScale("_BaseMap", original.GetTextureScale("_BaseMap"));
            materialCopy.SetTextureOffset("_BaseMap", original.GetTextureOffset("_BaseMap"));
            if (materialCopy.HasProperty("_BaseColor")) materialCopy.SetColor("_BaseColor", Color.white);
            SetLiveMaterialInt(materialCopy, "_Surface", 0);
            SetLiveMaterialInt(materialCopy, "_SrcBlend", (int)BlendMode.One);
            SetLiveMaterialInt(materialCopy, "_DstBlend", (int)BlendMode.Zero);
            SetLiveMaterialInt(materialCopy, "_ZWrite", 1);
            SetLiveMaterialInt(materialCopy, "_ZTest", (int)CompareFunction.LessEqual);
            SetLiveMaterialInt(materialCopy, "_CullMode", (int)CullMode.Back);
            SetLiveMaterialInt(materialCopy, "_ReceiveShadows", 0);
            // sharedMesh.bounds is in the rest mesh coordinates. Skinned localBounds
            // is in a rotated bone space and is intentionally not used here.
            Bounds bounds = meshes[0].bounds;
            Vector3 extent = bounds.size;
            float span = Mathf.Max(transforms[0].MultiplyVector(Vector3.right * extent.x).magnitude,
                Mathf.Max(transforms[0].MultiplyVector(Vector3.up * extent.y).magnitude,
                    transforms[0].MultiplyVector(Vector3.forward * extent.z).magnitude));
            if (float.IsNaN(span) || float.IsInfinity(span) || span < .001f)
                throw new InvalidOperationException("Original mesh bounds are invalid");
            // Register the original green instrument screen with the aim-space
            // origin. Centering the overall bounds instead put the beam beside
            // the screen; the previous 180-degree yaw made it point backwards.
            Vector3 screen = transforms[0].MultiplyPoint3x4(new Vector3(
                TouchServoAlignment.ScreenX, TouchServoAlignment.ScreenY, TouchServoAlignment.ScreenZ));
            Quaternion screenAxes = Quaternion.LookRotation(transforms[0].MultiplyVector(Vector3.forward),
                transforms[0].MultiplyVector(Vector3.up));
            Matrix4x4 normalize = Matrix4x4.Scale(Vector3.one * (.16f / span)) *
                Matrix4x4.Rotate(Quaternion.Inverse(screenAxes)) * Matrix4x4.Translate(-screen);
            var parts = new GameServoSkullPart[2];
            for (int i = 0; i < 2; ++i)
                parts[i] = new GameServoSkullPart { Mesh = meshes[i], Material = materialCopy, LocalToHand = normalize * transforms[i] };
            return parts;
        }

        static void ReleaseGameServoResource(GameServoLoad state)
        {
            if (!state.Held) return;
            state.Held = false;
            try { state.Free.Invoke(null, new object[] { GameServoSkullAsset, true }); }
            catch (Exception error) { _log.Error("[touch/hands] Releasing original model handle: " + error.GetBaseException().Message); }
        }
        internal static void ResetGameServoSkullLoader()
        {
            if (_gameServoMaterial != null) UnityEngine.Object.Destroy(_gameServoMaterial);
            _gameServoMaterial = null; _gameServoParts = null;
            var state = _gameServoLoad; _gameServoLoad = null;
            if (state == null) return;
            state.Cancelled = true;
            ReleaseGameServoResource(state);
            // A pending request observes Cancelled after its await and releases
            // its own handle, even if the VR session has already ended.
        }
    }
}
