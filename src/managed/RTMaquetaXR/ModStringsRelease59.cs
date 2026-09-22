using System.Collections.Generic;
namespace RTMaquetaXR
{
    internal static partial class ModLocalization
    {
        static partial void AddRelease59(Dictionary<string, string> s)
        {
            Add(s, "All mod diagnostics", "Todas las trazas del mod");
            Add(s, "Enable or stop all mod diagnostics, including timing collection, report files and OpenXR/NVIDIA bridge messages. Saved for the next launch. Game and other mod logs are unaffected. Runtime fault messages remain visible in the overlay.", "Activa o detiene todas las trazas del mod: mediciones, archivos de diagnóstico y mensajes de OpenXR/NVIDIA. Se guarda para el próximo arranque. No afecta a los registros del juego ni de otros mods. Los fallos de funcionamiento siguen indicándose en el overlay.");
            Add(s, "Gesture rotation sensitivity", "Sensibilidad del giro gestual");
            Add(s, "Adjust rotation with both Touch grips only. The initial response is 15% slower than before. Does not change stick turning, zoom, group movement or physical head tracking.", "Ajusta sólo el giro con ambos grips de los Touch. La respuesta inicial es un 15% más lenta que antes. No cambia el giro con stick, el zoom, el movimiento del grupo ni el seguimiento de la cabeza.");
        }
    }
}
