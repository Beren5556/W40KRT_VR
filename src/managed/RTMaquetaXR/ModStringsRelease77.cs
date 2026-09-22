using System.Collections.Generic;
namespace RTMaquetaXR
{
    internal static partial class ModLocalization
    {
        static partial void AddRelease77(Dictionary<string, string> s)
        {
            Add(s, "All diagnostics", "Todas las trazas");
            Add(s, "Experimental OFXR", "OFXR experimental");
            Add(s, "Optional frame generation for stereo VR, compatible with DLSS and DLAA. Off by default. Requires restarting the game. Uses real images during menus, transitions and failures. Engine cadence must be disabled before enabling OFXR. Status describes processing; visual quality and headset smoothness require a live comparison.", "Generación opcional de fotogramas para VR estéreo, compatible con DLSS y DLAA. Apagada por defecto. Requiere reiniciar el juego. Usa imágenes reales en menús, transiciones y fallos. Desactiva la cadencia del motor antes de activar OFXR. El estado describe el procesamiento; la calidad visual y la fluidez en visor requieren una comparación real.");
            Add(s, "Off at startup", "Apagado al arrancar");
            Add(s, "Waiting for stereo images", "Esperando imágenes estéreo");
            Add(s, "Preparing new image history", "Preparando nuevo historial de imágenes");
            Add(s, "Synthesis submitted", "Síntesis enviada");
            Add(s, "Showing real images", "Mostrando imágenes reales");
            Add(s, "Unavailable; showing real images", "No disponible; se muestran imágenes reales");
            Add(s, "Optional module missing", "Falta el módulo opcional");
            Add(s, "Another OFXR layer is active", "Hay otra capa OFXR activa");
            Add(s, "Conflict with engine cadence", "Conflicto con la cadencia del motor");
            Add(s, "Enable or disable all mod traces and detailed measurements together, including active OpenXR, NVIDIA and OFXR modules. Off by default; your choice is saved. Enabling traces adds measurement overhead. Runtime faults remain visible in the overlay.", "Activa o desactiva juntas todas las trazas y mediciones detalladas del mod, incluidos los módulos OpenXR, NVIDIA y OFXR activos. Apagadas por defecto; se guarda tu elección. Medir añade carga de trabajo. Los fallos de funcionamiento siguen visibles en el overlay.");
        }
    }
}
