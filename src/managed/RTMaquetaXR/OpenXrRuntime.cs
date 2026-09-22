using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace RTMaquetaXR
{
    internal static class OpenXrRuntime
    {
        static readonly OpenXrRuntimeSelection selection = new OpenXrRuntimeSelection();
        internal static int Applied => selection.Applied;
        internal static string Name => OpenXrRuntimePolicy.Name(Applied);
        internal static string Manifest { get; private set; }
        internal static void CaptureStartup(int requested) => selection.Capture(requested);
        internal static bool RestartPending(int requested) => selection.RestartPending(requested);
        internal static string WaitingText => Applied == 1
            ? "Connect your headset through Meta Quest Link or Air Link. The main menu unlocks automatically when VR is ready."
            : "Connect your headset through Virtual Desktop. The main menu unlocks automatically when VR is ready.";

        static string ReadRegistry(RegistryView view, string subkey, string name)
        {
            try
            {
                using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (var key = machine.OpenSubKey(subkey, false)) return key?.GetValue(name) as string;
            }
            catch (Exception e) when (e is System.Security.SecurityException || e is UnauthorizedAccessException || e is IOException) { return null; }
        }
        internal static List<string> Candidates(int value)
        {
            var paths = new List<string>();
            // Prefer a matching registered runtime; never use SteamVR or the other
            // vendor merely because it is the system default. Registry is read-only.
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                paths.Add(ReadRegistry(view, @"SOFTWARE\Khronos\OpenXR\1", "ActiveRuntime"));
                try
                {
                    using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var key = machine.OpenSubKey(@"SOFTWARE\Khronos\OpenXR\1\AvailableRuntimes", false))
                        if (key != null) paths.AddRange(key.GetValueNames());
                }
                catch (Exception e) when (e is System.Security.SecurityException || e is UnauthorizedAccessException || e is IOException) { }
                string oculusBase = ReadRegistry(view, @"SOFTWARE\Oculus VR, LLC\Oculus", "Base");
                if (!string.IsNullOrWhiteSpace(oculusBase))
                    paths.Add(Path.Combine(oculusBase, @"Support\oculus-runtime\oculus_openxr_64.json"));
            }
            foreach (string programFiles in new[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
            {
                if (string.IsNullOrEmpty(programFiles)) continue;
                paths.Add(Path.Combine(programFiles, @"Virtual Desktop Streamer\OpenXR\virtualdesktop-openxr.json"));
                paths.Add(Path.Combine(programFiles, @"Oculus\Support\oculus-runtime\oculus_openxr_64.json"));
            }
            // A custom installation may only have registered the loader override.
            // It is accepted only when its manifest belongs to the chosen vendor.
            paths.Add(Environment.GetEnvironmentVariable("XR_RUNTIME_JSON"));
            return paths;
        }
        internal static string Resolve(int value) => OpenXrRuntimePolicy.FindManifest(value, Candidates(value), File.Exists);
        internal static void PrepareProcess()
        {
            Manifest = Resolve(Applied);
            if (Manifest == null)
                throw new FileNotFoundException(ModLocalization.Format("OpenXR runtime not found: {0}. Install its PC application or choose the other runtime, then restart the game.", Name));
            // No registry, service, driver or global/user environment modification.
            Environment.SetEnvironmentVariable("XR_RUNTIME_JSON", Manifest, EnvironmentVariableTarget.Process);
        }
    }
}
