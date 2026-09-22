using System.Collections.Generic;

namespace RTMaquetaXR
{
    internal static partial class ModLocalization
    {
        static partial void AddEngineCadence(Dictionary<string, string> strings)
        {
            Add(strings, "Engine cadence · experimental", "Cadencia del motor · experimental");
            Add(strings, "Off by default. Only selected compatible visual updates run at a lower rate. Head tracking, Touch input and VR rendering keep the runtime rate. Game simulation is unchanged; target frame rates are not guaranteed.", "Desactivada por defecto. Solo reduce la frecuencia de determinadas actualizaciones visuales compatibles. Cabeza, Touch y renderizado VR mantienen la frecuencia del runtime. La simulación del juego no cambia; no garantiza los FPS objetivo.");
            Add(strings, "Enable engine cadence", "Activar cadencia del motor");
            Add(strings, "Save whether to use reduced cadence on the next game launch. Enabling or disabling it requires restarting the game. Off uses the original update path.", "Guarda si se usará la cadencia reducida al volver a iniciar el juego. Activar o desactivar requiere reiniciar el juego. Desactivado usa la actualización original.");
            Add(strings, "VR / visual updates", "VR / actualizaciones visuales");
            Add(strings, "Save 72/36, 90/45 or 120/60 Hz for the next game launch. Changing the pair requires a restart. Match the VR rate in Virtual Desktop; this selector does not change headset refresh. Different runtime frame timing suspends the feature.", "Guarda 72/36, 90/45 o 120/60 Hz para el próximo inicio del juego. Cambiar el par requiere reiniciar. Haz coincidir la frecuencia VR en Virtual Desktop; este selector no cambia el refresco del visor. Si el ritmo del runtime difiere, la función se suspende.");
            Add(strings, "Cadence status", "Estado de cadencia");
            Add(strings, "Shows the cadence currently applied and any restart needed for saved changes. Only selected visual updates use the lower cadence. Requested rates do not guarantee measured FPS.", "Muestra la cadencia actual y si los cambios guardados requieren reiniciar. Solo determinadas actualizaciones visuales usan la cadencia reducida. Las frecuencias solicitadas no garantizan los FPS medidos.");
            Add(strings, "{0} Hz VR / {1} Hz visual updates", "{0} Hz VR / {1} Hz actualizaciones visuales");
            Add(strings, "Measured: {0} Hz VR / {1} Hz visual updates", "Medido: {0} Hz VR / {1} Hz actualizaciones visuales");
            Add(strings, "Waiting for VR", "Esperando VR");
            Add(strings, "Waiting for frame timing", "Esperando ritmo de fotogramas");
            Add(strings, "Frame timing mismatch", "Ritmo de fotogramas distinto");
            Add(strings, "Restart required", "Requiere reiniciar");
            Add(strings, "Restart required · current: {0}", "Requiere reiniciar · actual: {0}");
            Add(strings, "Active", "Activa");
            Add(strings, "Suspended", "Suspendida");
        }
    }
}
