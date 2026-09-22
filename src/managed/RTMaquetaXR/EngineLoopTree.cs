using System;
using System.Collections.Generic;
using UnityEngine.LowLevel;

namespace RTMaquetaXR
{
    internal struct EngineLoopBeginMarker { }
    internal struct EngineLoopEndMarker { }

    // Pure tree surgery over Unity's actual struct. Native pointers, conditions,
    // delegates and subsystem ordering are copied; never substitute defaults.
    internal static class EngineLoopTree
    {
        internal static readonly string[] PhaseNames = {
            "UnityEngine.PlayerLoop.EarlyUpdate+ExecuteMainThreadJobs",
            "UnityEngine.PlayerLoop.EarlyUpdate+UpdatePreloading",
            "UnityEngine.PlayerLoop.FixedUpdate+ScriptRunBehaviourFixedUpdate",
            "UnityEngine.PlayerLoop.FixedUpdate+PhysicsFixedUpdate",
            "UnityEngine.PlayerLoop.PreUpdate+PhysicsUpdate",
            "UnityEngine.PlayerLoop.Update+ScriptRunBehaviourUpdate",
            "UnityEngine.PlayerLoop.Update+DirectorUpdate",
            "UnityEngine.PlayerLoop.Update+ScriptRunDelayedTasks",
            "UnityEngine.PlayerLoop.PreLateUpdate+DirectorUpdateAnimationBegin",
            "UnityEngine.PlayerLoop.PreLateUpdate+DirectorUpdateAnimationEnd",
            "UnityEngine.PlayerLoop.PreLateUpdate+LegacyAnimationUpdate",
            "UnityEngine.PlayerLoop.PreLateUpdate+ParticleSystemBeginUpdateAll",
            "UnityEngine.PlayerLoop.PreLateUpdate+ScriptRunBehaviourLateUpdate",
            "UnityEngine.PlayerLoop.PreLateUpdate+EndGraphicsJobsAfterScriptUpdate",
            "UnityEngine.PlayerLoop.PostLateUpdate+PlayerUpdateCanvases",
            "UnityEngine.PlayerLoop.PostLateUpdate+ParticleSystemEndUpdateAll",
            "UnityEngine.PlayerLoop.PostLateUpdate+EndGraphicsJobsAfterScriptLateUpdate",
            "UnityEngine.PlayerLoop.PostLateUpdate+VFXUpdate",
            "UnityEngine.PlayerLoop.PostLateUpdate+UpdateAllRenderers",
            "UnityEngine.PlayerLoop.PostLateUpdate+UpdateAllSkinnedMeshes",
            "UnityEngine.PlayerLoop.PostLateUpdate+PlayerEmitCanvasGeometry",
            "UnityEngine.PlayerLoop.PostLateUpdate+FinishFrameRendering",
            "UnityEngine.PlayerLoop.PostLateUpdate+PresentAfterDraw"
        };
        internal static bool Owned(PlayerLoopSystem node)
        {
            bool kind = node.type == typeof(EngineLoopBeginMarker) || node.type == typeof(EngineLoopEndMarker);
            return kind && node.updateFunction == IntPtr.Zero && node.loopConditionFunction == IntPtr.Zero &&
                node.updateDelegate != null && node.updateDelegate.GetInvocationList().Length == 1 && node.updateDelegate.Target is EngineLoopProbe;
        }
        internal static PlayerLoopSystem Remove(PlayerLoopSystem root, out int removed)
        { removed = 0; return Strip(root, ref removed, 0); }
        static PlayerLoopSystem Strip(PlayerLoopSystem node, ref int removed, int depth)
        {
            if (depth > 64) throw new InvalidOperationException("Unexpected PlayerLoop depth");
            if (node.subSystemList == null) return node;
            var result = new List<PlayerLoopSystem>(node.subSystemList.Length); bool changed = false;
            foreach (var child in node.subSystemList)
            {
                var clean = Strip(child, ref removed, depth + 1);
                if (Owned(clean))
                {
                    ++removed; changed = true;
                    // Preserve systems another mod added below our boundary.
                    if (clean.subSystemList != null) result.AddRange(clean.subSystemList);
                }
                else
                {
                    result.Add(clean);
                    changed |= !ReferenceEquals(clean.subSystemList, child.subSystemList);
                }
            }
            if (changed) node.subSystemList = result.ToArray();
            return node;
        }
        internal static int[] Count(PlayerLoopSystem root)
        {
            var counts = new int[PhaseNames.Length]; int nodes = 0;
            CountNode(root, counts, ref nodes, 0); return counts;
        }
        static void CountNode(PlayerLoopSystem node, int[] counts, ref int nodes, int depth)
        {
            if (depth > 64 || ++nodes > 8192) throw new InvalidOperationException("Unexpected PlayerLoop structure");
            int index = Array.IndexOf(PhaseNames, node.type?.FullName);
            if (index >= 0) ++counts[index];
            if (node.subSystemList != null) foreach (var child in node.subSystemList) CountNode(child, counts, ref nodes, depth + 1);
        }
        internal static PlayerLoopSystem Insert(PlayerLoopSystem root, EngineLoopProbe[] probes, out int inserted)
        {
            var clean = Remove(root, out int ignored); var counts = Count(clean);
            if (probes == null || probes.Length != PhaseNames.Length) throw new ArgumentException("Probe catalog mismatch");
            inserted = 0; var result = Add(clean, probes, counts, ref inserted, 0);
            var restored = Remove(result, out int boundaries);
            if (boundaries != inserted * 2 || !Same(clean, restored)) throw new InvalidOperationException("Original PlayerLoop preservation failed");
            return result;
        }
        static PlayerLoopSystem Add(PlayerLoopSystem node, EngineLoopProbe[] probes, int[] counts, ref int inserted, int depth)
        {
            if (depth > 64) throw new InvalidOperationException("Unexpected PlayerLoop depth");
            if (node.subSystemList == null) return node;
            var result = new List<PlayerLoopSystem>(node.subSystemList.Length + 8); bool changed = false;
            foreach (var child in node.subSystemList)
            {
                var original = Add(child, probes, counts, ref inserted, depth + 1);
                int index = Array.IndexOf(PhaseNames, original.type?.FullName);
                var probe = index >= 0 && counts[index] == 1 ? probes[index] : null;
                if (probe != null)
                {
                    result.Add(new PlayerLoopSystem { type = typeof(EngineLoopBeginMarker), updateDelegate = probe.Begin });
                    result.Add(original);
                    result.Add(new PlayerLoopSystem { type = typeof(EngineLoopEndMarker), updateDelegate = probe.End });
                    ++inserted; changed = true;
                }
                else { result.Add(original); changed |= !ReferenceEquals(original.subSystemList, child.subSystemList); }
            }
            if (changed) node.subSystemList = result.ToArray();
            return node;
        }
        internal static bool Same(PlayerLoopSystem left, PlayerLoopSystem right)
        {
            if (left.type != right.type || left.updateFunction != right.updateFunction || left.loopConditionFunction != right.loopConditionFunction ||
                left.updateDelegate != right.updateDelegate) return false;
            if (left.subSystemList == null || right.subSystemList == null) return left.subSystemList == right.subSystemList;
            if (left.subSystemList.Length != right.subSystemList.Length) return false;
            for (int i = 0; i < left.subSystemList.Length; ++i)
                if (!Same(left.subSystemList[i], right.subSystemList[i])) return false;
            return true;
        }
    }
}
