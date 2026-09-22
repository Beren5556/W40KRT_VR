using System.Collections.Generic;

namespace RTMaquetaXR
{
    internal static partial class ModLocalization
    {
        static partial void AddRelease50Neural(Dictionary<string, string> strings)
        {
            Add(strings, "TAA smooths edges using previous frames; SMAA is non-temporal. DLAA works at full resolution; DLSS reconstructs from fewer pixels. Diagnostic tests NVIDIA while displaying the current image without AA. NVIDIA never computes TAA as a fallback.", "TAA suaviza bordes con fotogramas anteriores; SMAA no es temporal. DLAA trabaja a resolución completa; DLSS reconstruye desde menos píxeles. Diagnóstico evalúa NVIDIA mostrando la imagen actual sin AA. NVIDIA nunca calcula TAA de respaldo.");
            Add(strings, "FSR is suspended in VR while NVIDIA is selected. If NVIDIA fails, the current image is shown without antialiasing.", "FSR queda suspendido en VR al seleccionar NVIDIA. Si NVIDIA falla, se muestra la imagen actual sin antialiasing.");
            Add(strings, "No AA fallback: {0}", "Respaldo sin AA: {0}");
            Add(strings, "No AA (waiting to switch to {0})", "Sin AA (esperando cambiar a {0})");
            Add(strings, "{0}: warmup {1}/{2} (no AA visible)", "{0}: preparación {1}/{2} (imagen sin AA)");
            Add(strings, "DLAA diagnostic (no AA displayed)", "Diagnóstico DLAA (se muestra sin AA)");
            Add(strings, "NVIDIA diagnostic (no AA image)", "Diagnóstico NVIDIA (imagen sin AA)");
            Add(strings, "Recommended DLSS: 310.9.1.0. Other versions and NVIDIA overrides are allowed. Version and source describe the runtime detected by NGX; unknown identity does not block it. A real backend failure displays the current image without antialiasing. Restart the game after replacing a DLL.", "DLSS recomendado: 310.9.1.0. Se admiten otras versiones y ajustes de NVIDIA. La versión y el origen son los detectados por NGX; una identidad desconocida no lo bloquea. Un fallo real muestra la imagen actual sin antialiasing. Reinicia el juego si sustituyes una DLL.");
        }
    }
}
