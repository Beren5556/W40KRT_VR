using System;

namespace RTMaquetaXR
{
    // All mod-owned UMM messages pass this gate, including callbacks handed to
    // integration hooks. The game's logger and other mods are never patched.
    internal sealed class ModDiagnosticLog
    {
        readonly Action<string> output, error;
        internal static volatile bool Enabled = false;
        internal ModDiagnosticLog(Action<string> output, Action<string> error)
        { this.output = output; this.error = error; }
        internal void Log(string message) { if (Enabled) output(message); }
        internal void Error(string message) { if (Enabled) error(message); }
    }
}
