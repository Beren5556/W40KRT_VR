using System.Collections.Generic;
namespace RTMaquetaXR
{
    internal static partial class ModLocalization
    {
        static partial void AddRelease62(Dictionary<string, string> strings)
        {
            Add(strings, "OpenXR runtime", "Runtime OpenXR");
            Add(strings, "Choose Virtual Desktop (VDXR) or Meta Quest Link / Air Link. Requires restarting the game. Does not change the Windows default runtime, headset refresh or graphics settings. Connect through the selected PC application before launching.", "Elige Virtual Desktop (VDXR) o Meta Quest Link / Air Link. Requiere reiniciar el juego. No cambia el runtime predeterminado de Windows, el refresco del visor ni los ajustes gráficos. Conecta mediante la aplicación de PC elegida antes de arrancar.");
            Add(strings, "Waiting for the OpenXR headset", "Esperando al visor OpenXR");
            Add(strings, "Waiting for menu or gameplay to start OpenXR", "Esperando al menú o a la partida para iniciar OpenXR");
            Add(strings, "Connect your headset through Meta Quest Link or Air Link. The main menu unlocks automatically when VR is ready.", "Conecta el visor mediante Meta Quest Link o Air Link. El menú principal se desbloquea automáticamente cuando la VR está lista.");
            Add(strings, "OpenXR runtime not found: {0}. Install its PC application or choose the other runtime, then restart the game.", "No se encuentra el runtime OpenXR: {0}. Instala su aplicación de PC o elige el otro runtime y reinicia el juego.");
            Add(strings, "OpenXR / {0}: session created", "OpenXR / {0}: sesión creada");
            Add(strings, "OpenXR runtime change requires restarting the game", "Cambiar el runtime OpenXR requiere reiniciar el juego");
            Add(strings, "Tabletop VR · OpenXR · experimental ", "Maqueta VR · OpenXR · experimental ");
            Add(strings, "Current runtime: {0}. Changes require restarting the game.", "Runtime actual: {0}. Los cambios requieren reiniciar el juego.");
            Add(strings, "Resolution 1.00 = full OpenXR runtime recommendation. Live change; ", "Resolución 1,00 = recomendación completa del runtime OpenXR. Cambio en caliente; ");
            Add(strings, "; runtime recommends ", "; el runtime recomienda ");
            Add(strings, "Start automatically when the headset connects through the selected runtime", "Iniciar automáticamente al conectar el visor mediante el runtime elegido");
            Add(strings, "Final resolution sent to each eye. 100% uses the recommendation of the selected OpenXR runtime. Higher values can reveal more detail and cost more GPU time. Changing resolution may briefly pause the image.", "Resolución final enviada a cada ojo. El 100 % utiliza la recomendación del runtime OpenXR elegido. Valores mayores pueden mostrar más detalle y aumentar el coste de GPU. Cambiar la resolución puede pausar brevemente la imagen.");
            Add(strings, "Start the selected OpenXR runtime with the game when the headset is available. Saved for future launches. Changing it does not stop the current VR session.", "Inicia el runtime OpenXR elegido con el juego cuando el visor está disponible. Se guarda para los siguientes arranques. Cambiarlo no detiene la sesión VR actual.");
            Add(strings, "OpenXR runtime rejected this resolution", "El runtime OpenXR ha rechazado esta resolución");
            Add(strings, "The loaded OpenXR runtime does not match the selected runtime. Restart the game after changing it.", "El runtime OpenXR cargado no coincide con el elegido. Reinicia el juego después de cambiarlo.");
        }
    }
}
