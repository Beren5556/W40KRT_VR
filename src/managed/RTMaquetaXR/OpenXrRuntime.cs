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
        static string configuredManifest80;
        internal static void CaptureStartup(int requested,string manifest=null) { if(!selection.Captured)configuredManifest80=manifest;selection.Capture(requested); }
        internal static bool RestartPending(int requested) => selection.RestartPending(requested);
        internal static string WaitingText => Applied == 2 ? "Connect your Pimax headset and controllers through Pimax Play. The main menu unlocks automatically when VR is ready." : Applied == 1
            ? "Connect your headset through Meta Quest Link or Air Link. The main menu unlocks automatically when VR is ready."
            : "Connect your headset through Virtual Desktop. The main menu unlocks automatically when VR is ready.";

        internal static List<string> Candidates(int value) => RuntimeDiscovery80.Candidates(value,configuredManifest80);
        internal static string Resolve(int value) => RuntimeDiscovery80.Resolve(value,configuredManifest80);
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
