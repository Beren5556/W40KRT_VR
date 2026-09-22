using System;
using System.Collections.Generic;
using System.IO;

namespace RTMaquetaXR
{
    // A process chooses its loader once. Menu edits only affect the next launch.
    internal sealed class OpenXrRuntimeSelection
    {
        internal int Applied { get; private set; }
        internal bool Captured { get; private set; }
        internal static int Normalize(int value) => value == 1 ? 1 : 0;
        internal void Capture(int value)
        {
            if (Captured) return;
            Applied = Normalize(value); Captured = true;
        }
        internal bool RestartPending(int requested) => Captured && Normalize(requested) != Applied;
    }

    internal static class OpenXrRuntimePolicy
    {
        internal static string Name(int value) => OpenXrRuntimeSelection.Normalize(value) == 1 ? "Meta Quest Link" : "VDXR";
        internal static string ManifestName(int value) => OpenXrRuntimeSelection.Normalize(value) == 1 ? "oculus_openxr_64.json" : "virtualdesktop-openxr.json";
        internal static bool MatchesManifest(int value, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try { return string.Equals(Path.GetFileName(path.Trim().Trim('"')), ManifestName(value), StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
        }
        internal static string FindManifest(int value, IEnumerable<string> candidates, Func<string, bool> exists)
        {
            foreach (string candidate in candidates)
            {
                if (!MatchesManifest(value, candidate)) continue;
                string path;
                try { path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"'))); }
                catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) { continue; }
                if (exists(path)) return path;
            }
            return null;
        }
    }
}
