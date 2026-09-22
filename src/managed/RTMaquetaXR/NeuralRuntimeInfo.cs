using System;

namespace RTMaquetaXR
{
    // Informational only: no version/source result can enable or disable NGX.
    // A missing additive export from an older bridge is not a renderer failure.
    internal sealed class NeuralRuntimeInfo
    {
        internal const string RecommendedVersion = "310.9.1.0";
        internal delegate int ReadStatus(ref NeuralNative.RuntimeStatus status);
        readonly ReadStatus read;
        readonly long interval;
        long next;
        bool keyKnown;
        ulong previousGeneration;
        uint previousState, previousReason;
        internal NeuralNative.RuntimeStatus Current;
        internal bool HasStatus { get; private set; }
        internal bool MissingExport { get; private set; }
        internal string ReadError { get; private set; }
        internal long Reads { get; private set; }
        internal NeuralRuntimeInfo(ReadStatus read, long interval)
        { this.read = read ?? throw new ArgumentNullException(nameof(read)); this.interval = Math.Max(1, interval); }

        internal void Refresh(NeuralNative.BackendStatus backend, long now, bool force = false)
        {
            if (MissingExport) return;
            bool changed = !keyKnown || previousGeneration != backend.generation || previousState != backend.state || previousReason != backend.reason;
            if (!force && !changed && now < next) return;
            keyKnown = true; previousGeneration = backend.generation; previousState = backend.state; previousReason = backend.reason;
            next = now > long.MaxValue - interval ? long.MaxValue : now + interval;
            try
            {
                ++Reads;
                var value = new NeuralNative.RuntimeStatus { size = 2608, abi = NeuralNative.Abi };
                if (read(ref value) != 1 || value.size != 2608 || value.abi != NeuralNative.Abi)
                { HasStatus = false; ReadError = "Runtime identity unavailable"; return; }
                Current = value; HasStatus = true; ReadError = null;
            }
            catch (EntryPointNotFoundException) { MissingExport = true; HasStatus = false; ReadError = "Older bridge: runtime source unavailable"; }
            catch (Exception error) { HasStatus = false; ReadError = "Runtime identity unavailable: " + error.Message; }
        }

        internal string Version(NeuralNative.BackendStatus backend)
        {
            if (HasStatus) return Current.generation == backend.generation && (Current.identity == 1 || Current.identity == 2) && (Current.flags & 1) != 0 ?
                VersionString(Current.versionMajor, Current.versionMinor, Current.versionPatch, Current.versionBuild) : null;
            return (backend.runtimeMajor | backend.runtimeMinor | backend.runtimePatch | backend.runtimeBuild) != 0 ?
                VersionString(backend.runtimeMajor, backend.runtimeMinor, backend.runtimePatch, backend.runtimeBuild) : null;
        }
        static string VersionString(uint major, uint minor, uint patch, uint build) => major + "." + minor + "." + patch + "." + build;
        internal bool HasIdentity => HasStatus && Current.generation == previousGeneration && (Current.identity == 1 || Current.identity == 2);
        internal string Source => !HasStatus ? "Source unavailable" : !HasIdentity ? "Source unidentified" : Current.source == 1 ? "Mod NVIDIA folder" :
            Current.source == 2 ? "External runtime" : Current.source == 3 ? "NVIDIA override" : "Source unidentified";
        internal string Identity => !HasStatus ? "Unavailable" : !HasIdentity ? "Unidentified" : Current.identity == 1 ? "Runtime log" : "Loaded module";
        internal bool IsRecommended(NeuralNative.BackendStatus backend) => Version(backend) == RecommendedVersion;
        internal string PanelText(NeuralNative.BackendStatus backend)
        {
            string version = Version(backend);
            return (version ?? ModLocalization.Text("Version unknown")) + " · " + ModLocalization.Text(Source) + "\n" +
                ModLocalization.Text(version == RecommendedVersion ? "Recommended version" : version == null ? "NOTICE: unverified · permitted" : "NOTICE: other version · permitted");
        }
        internal string Notice(NeuralNative.BackendStatus backend)
        {
            string version = Version(backend);
            if (version == RecommendedVersion) return "";
            return ModLocalization.Text(version == null ? "Runtime unidentified · permitted" : "Other NVIDIA runtime · permitted");
        }
        internal static string ImageStatus(string mode, string output, string measurement, string notice) =>
            mode + "\n" + output + (string.IsNullOrEmpty(notice) ? "" : " · " + notice) + "\n" + measurement;
        internal string Failure(string fallback, NeuralNative.BackendStatus backend, ulong generation)
        {
            // Never attach a fault from a previous configuration to a new job.
            if (HasStatus && Current.generation == generation && Current.lastErrorReason != 0 && !string.IsNullOrWhiteSpace(Current.message))
                return Current.message.Trim() + " (native reason " + Current.lastErrorReason + ")";
            return backend.generation == generation && backend.reason != 0 ? fallback + ": " + Reason(backend.reason) + " (native reason " + backend.reason + ")" : fallback;
        }
        internal static string Reason(uint reason)
        {
            switch (reason)
            {
                case 1:return "bridge ABI mismatch";case 2:return "invalid configuration";case 3:return "duplicate eye/frame";
                case 4:return "native job queue full";case 5:return "incompatible graphics resource";case 6:return "unsupported GPU/runtime capability";
                case 7:return "NVIDIA runtime loading or compatibility failure";case 8:return "NVIDIA initialization failed";
                case 9:return "NVIDIA feature creation failed";case 10:return "NVIDIA evaluation failed";case 11:return "graphics device removed";
                case 12:return "job cancelled";case 13:return "stale frame or configuration";default:return "native reason " + reason;
            }
        }
    }
}
