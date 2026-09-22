using System.Collections.Generic;

namespace RTMaquetaXR
{
    internal static partial class ModLocalization
    {
        static partial void AddOverlay(Dictionary<string, string> strings)
        {
            Add(strings, "Hold · information / FX", "Mantén · información / FX");
            Add(strings, "Familiar effects only with Y", "Efectos de familiares sólo con Y");
            Add(strings, "In ground combat, hide identified persistent familiar lights and visual effects until you hold Y. Keeps familiar models, rules, attacks and audio. Outside combat their normal appearance returns.", "En Combates terrestres, oculta luces y efectos persistentes identificados de los familiares hasta mantener Y. Conserva sus modelos, reglas, ataques y sonido. Fuera de combate recuperan su apariencia normal.");
            Add(strings, "Management window size", "Tamaño de ventanas de gestión");
            Add(strings, "Adjust text and buttons in tabletop management windows. Native flat screens use Management window size and proportions instead. Dialogue HUD settings stay unchanged.", "Ajusta textos y botones en las ventanas de gestión de la maqueta. En pantallas planas usa el tamaño y la proporción de gestión. Conserva los ajustes del HUD de diálogos.");
            Add(strings, "Enlarge the regular game HUD, including its outer edges. 100% uses the binocular area; maximum 180%. Management windows have their own size setting. Text and button size changes content inside these edges.", "Amplía el HUD habitual, incluidos sus bordes. El 100% usa el área binocular; máximo 180%. Las ventanas de gestión tienen su propio tamaño. El tamaño de textos y botones modifica el contenido interior.");
            Add(strings, "Move the regular HUD left or right by up to 65% of its width. Management windows use their own horizontal position. Extreme values may place buttons outside your view. Aiming follows the displayed panel.", "Mueve el HUD habitual a izquierda o derecha hasta un 65% de su ancho. Gestión tiene posición horizontal propia. Los valores extremos pueden dejar botones fuera de la vista. El apuntado sigue el panel mostrado.");
            Add(strings, "Move the regular HUD up or down by up to 65% of its height. Negative moves down; positive moves up. Management windows use their own vertical position.", "Sube o baja el HUD habitual hasta un 65% de su altura. Negativo baja; positivo sube. Las ventanas de gestión tienen posición vertical propia.");
            Add(strings, "Contextual control hints", "Ayuda contextual de controles");
            Add(strings, "Show a discreet reminder of the controls available in the current context. Original game information remains visible.", "Muestra un recordatorio discreto de los controles disponibles en cada contexto. Conserva visible la información original del juego.");
            Add(strings, "Combat double-click head view", "Doble clic de combate: primera persona");
            Add(strings, "In ground combat, double-click a movement destination with the right trigger to move and stay in first person. Native movement rules still apply. A single click keeps normal movement.", "En Combates terrestres, pulsa dos veces el gatillo derecho sobre un destino para moverte y permanecer en primera persona. Respeta las reglas del juego. Un clic mantiene el movimiento normal.");
            Add(strings, "Restore the default interface or save three layouts for HUD, management and both space maps. Eye resolution, DLSS, camera and controls stay unchanged.", "Restaura la interfaz predeterminada o guarda tres diseños de HUD, gestión y ambos mapas espaciales. Conserva resolución del visor, DLSS, cámara y controles.");
            Add(strings, "Load saved HUD, management and space-map layouts. Other mod settings are preserved. Older presets keep any map or management settings they did not store.", "Carga los diseños guardados de HUD, gestión y mapas espaciales. Conserva los demás ajustes. Los presets antiguos respetan los valores de mapas o gestión que no incluían.");
            Add(strings, "Management window distance", "Distancia de ventanas de gestión");
            Add(strings, "Move management windows nearer or farther without changing dialogue HUD placement or wheels.", "Acerca o aleja las ventanas de gestión sin cambiar la posición del HUD de diálogos ni las ruedas.");
            Add(strings, "Management window proportions", "Proporción de ventanas de gestión");
            Add(strings, "Original keeps the game aspect ratio. Manual proportions reflow tabletop management windows; native flat screens change their displayed proportions. Aiming follows the displayed window.", "Original conserva la proporción del juego. El ajuste manual redistribuye las ventanas en la maqueta; en pantallas planas cambia su proporción visual. El apuntado sigue la ventana mostrada.");
            Add(strings, "Management horizontal position", "Posición horizontal de gestión");
            Add(strings, "Move management windows left or right independently of dialogue HUD placement and wheels.", "Mueve las ventanas de gestión a izquierda o derecha sin alterar el HUD de diálogos ni las ruedas.");
            Add(strings, "Management vertical position", "Posición vertical de gestión");
            Add(strings, "Move management windows up or down independently of dialogue HUD placement and wheels.", "Sube o baja las ventanas de gestión sin alterar el HUD de diálogos ni las ruedas.");
            Add(strings, "Fit inventory, equipment and management windows inside your view. This size applies to complete windows, including native flat menus, independently of dialogue HUD settings and wheels.", "Encaja inventario, equipo y gestión dentro de la vista. Ajusta ventanas completas, incluidos los menús planos del juego, sin cambiar el HUD de diálogos ni las ruedas.");
            Add(strings, "Engine effects", "Efectos del motor");
            Add(strings, "Skip fully faded scenery (experimental)", "Omitir escenario desvanecido · prueba");
            Add(strings, "Disabled after error", "Desactivado tras error");
            Add(strings, "Avoid sending fully faded scenery to the VR eyes. Only applies when the game's own fade is active, with compatible opaque materials; preserves shadows. Experimental: residual fade edges may disappear. Disable here to compare the same view. Does not hide other floors or alter game rules.", "Evita enviar escenario completamente desvanecido a los ojos VR. Sólo actúa con el desvanecimiento del juego y materiales opacos compatibles; conserva las sombras. Experimental: pueden desaparecer bordes residuales del desvanecimiento. Desactívalo para comparar la misma vista. No oculta otros pisos ni cambia las reglas del juego.");
            Add(strings, "Original", "Original");
            Add(strings, "Reduced", "Reducido");
            Add(strings, "Minimal", "Mínimo");
            Add(strings, "Unavailable", "No disponible");
            Add(strings, "Original keeps the game effects. Reduced removes volumetric lighting/fog and screen-space reflections in VR. Minimal also removes ambient occlusion, bloom and camera blur. Keeps particles, tactical targeting and interface. Compare in the same scene; CPU stalls may remain.", "Original conserva los efectos. Reducido elimina luz/niebla volumétrica y reflejos de pantalla en VR. Mínimo elimina además oclusión ambiental, resplandor y desenfoque. Conserva partículas, apuntado e interfaz. Compara en la misma escena; pueden persistir parones de CPU.");
            Add(strings, "Main menu", "Menú principal");
            Add(strings, "Mod language", "Idioma del mod");
            Add(strings, "Initially follows the game: Spanish if the game uses Spanish, otherwise English. Your choice applies immediately to all mod menus, help and messages, is saved and takes priority on later launches. Original game text keeps its own language.", "De inicio usa castellano si el juego está en castellano; en otro caso, inglés. Tu elección se aplica al instante a los menús, ayudas y mensajes del mod, se guarda y tiene prioridad en futuros arranques. Los textos originales del juego conservan su idioma.");
            Add(strings, "Advanced settings", "Configuración avanzada");
            Add(strings, "Open all mod settings, grouped as before. The image and language shortcuts share the same values as their advanced entries.", "Abre todos los ajustes del mod con su organización habitual. Los accesos de imagen e idioma comparten los mismos valores que sus opciones avanzadas.");
            Add(strings, "Image", "Imagen");
            Add(strings, "Adjust antialiasing, DLSS/DLAA, resolution and sharpness while playing. The status below reports the mode actually in use.", "Ajusta el suavizado de bordes, DLSS/DLAA, la resolución y la nitidez mientras juegas. El estado inferior indica el modo que se está usando realmente.");
            Add(strings, "Render mode", "Tipo de renderizado");
            Add(strings, "TAA smooths edges using previous frames; SMAA is non-temporal. DLAA works at full resolution; DLSS reconstructs from fewer pixels. Diagnostic tests NVIDIA while displaying TAA.", "TAA suaviza bordes con fotogramas previos; SMAA no es temporal. DLAA usa la resolución completa; DLSS reconstruye desde menos píxeles. Diagnóstico prueba NVIDIA mientras muestra TAA.");
            Add(strings, "Output resolution", "Resolución de salida");
            Add(strings, "Final resolution sent to each eye. 100% uses the current VDXR recommendation. Higher values can reveal more detail and cost more GPU time. Changing resolution may briefly pause the image.", "Resolución final enviada a cada ojo. El 100% usa la recomendación actual de VDXR. Subirla puede mostrar más detalle y exige más GPU. El cambio puede pausar brevemente la imagen.");
            Add(strings, "DLSS scale", "Escala DLSS");
            Add(strings, "DLSS only. Lower values reduce GPU work and fine detail. Range: 50–100%, in 5% steps. DLAA always uses the full output resolution.", "Sólo con DLSS. Reducirla aligera el trabajo de la GPU y reduce el detalle fino. Rango: 50–100%, en pasos del 5%. DLAA siempre usa la resolución de salida completa.");
            Add(strings, "Sharpness · TAA / NVIDIA", "Nitidez · TAA / NVIDIA");
            Add(strings, "Sharpen edges without increasing resolution. High values may create halos or grain. TAA and NVIDIA have separate values. TAA sharpening does not affect SMAA or No antialiasing.", "Refuerza los bordes sin subir la resolución. Los valores altos pueden crear halos o grano. TAA y NVIDIA tienen valores independientes. La nitidez de TAA no afecta a SMAA ni al modo sin suavizado.");
            Add(strings, "NVIDIA model preset", "Modelo de NVIDIA");
            Add(strings, "Auto lets NVIDIA choose its model. K requests that preset for DLSS/DLAA. This does not change resolution. The status distinguishes the request from the model that can be identified.", "Auto deja que NVIDIA elija el modelo. K solicita ese modelo para DLSS/DLAA sin cambiar la resolución. El estado distingue lo solicitado del modelo que se ha podido identificar.");
            Add(strings, "NVIDIA runtime", "Runtime de NVIDIA");
            Add(strings, "Recommended DLSS: 310.9.1.0. Other versions and NVIDIA overrides are allowed. Version and source describe the runtime detected by NGX; unknown identity does not block it. TAA is retained on a real backend failure. Restart the game after replacing a DLL.", "DLSS recomendado: 310.9.1.0. Se admiten otras versiones y ajustes de NVIDIA. La versión y el origen son los detectados por NGX; una identidad desconocida no lo bloquea. Sólo un fallo real activa TAA. Reinicia el juego si sustituyes una DLL.");
            Add(strings, "Draw distance", "Distancia de dibujado");
            Add(strings, "Limit how far the scene is drawn in VR. This can reduce slowdowns when looking across large areas. A short distance may hide the far background.", "Limita hasta dónde se dibuja el escenario en VR. Puede reducir las caídas al mirar zonas extensas. Una distancia corta puede ocultar el fondo lejano.");
            Add(strings, "Distance profile", "Perfil de distancia");
            Add(strings, "Choose the game default, 100, 60 or Custom. A shorter distance may improve frame rate and remove distant scenery. Locked during an automatic comparison.", "Elige la distancia original, 100, 60 o Personalizada. Una distancia menor puede mejorar los FPS y ocultar escenario lejano. Se bloquea durante una comparación automática.");
            Add(strings, "Custom distance", "Distancia personalizada");
            Add(strings, "Select Custom and set the range from 20 to 400 scene units, in steps of 2. Reducing it may improve frame rate; check that scenery you need remains visible.", "Activa Personalizada y ajusta entre 20 y 400 unidades de escenario, en pasos de 2. Reducirla puede mejorar los FPS; comprueba que el escenario necesario siga visible.");
            Add(strings, "Adaptive distance · experimental", "Distancia adaptativa · prueba");
            Add(strings, "Adjust range using height, viewing direction and both eyes. Shortens it only when a reliable ground plane is found. Your manual ceiling returns at the horizon. Multi-level scenes may lose background; use Manual if this occurs.", "Ajusta el alcance según la altura, la mirada y ambos ojos. Sólo lo acorta si detecta un suelo fiable. Recupera tu límite manual al mirar al horizonte. Puede ocultar fondos en escenarios con varios niveles; en ese caso usa el ajuste manual.");
            Add(strings, "Applied distance", "Distancia aplicada");
            Add(strings, "Current range and manual ceiling in scene units. Adaptive range shortens gradually and expands immediately. Suspended during cinematics and automatic comparisons.", "Alcance actual y límite manual en unidades de escenario. El ajuste adaptativo acorta gradualmente y amplía de inmediato. Se suspende en cinemáticas y comparaciones automáticas.");
            Add(strings, "Tabletop", "Maqueta");
            Add(strings, "Adjust movement, rotation and zoom response. The tabletop keeps its comfort limits. Recenter uses your current seated pose as the reference.", "Ajusta la respuesta del movimiento, giro y zoom. La maqueta mantiene sus límites de confort. Recentrar usa tu postura sentada actual como referencia.");
            Add(strings, "Follow moving characters", "Seguir a los personajes");
            Add(strings, "In third person, smoothly follow characters moved with the right stick, with quicker alignment toward their travel direction. Off keeps the table still. Head view is unchanged. Movement keeps the heading at the start of each stick deflection; centre the stick to use the new heading.", "En tercera persona, sigue suavemente al grupo movido con el stick derecho y orienta la vista hacia su marcha. Desactivado mantiene la maqueta quieta. No afecta a primera persona. La dirección se fija al mover el stick; céntralo para usar la nueva orientación.");
            Add(strings, "Draw distance shortcut", "Atajo de distancia");
            Add(strings, "Off by default. Enable left thumbrest touch + right stick up/down to change draw distance. When off, resting your thumb cannot change rendering range; the Draw distance menu remains available.", "Desactivado por defecto. Permite cambiar la distancia tocando el apoyo del pulgar izquierdo y moviendo el stick derecho arriba/abajo. Desactivado, apoyar el pulgar no cambia el alcance. El menú de distancia sigue disponible.");
            Add(strings, "Movement speed · Touch", "Velocidad al mover · Touch");
            Add(strings, "Adjust how far the tabletop moves when you move one or both hands while holding the grips. Physical hand and head tracking remain unchanged. Tracking jumps still have a speed limit.", "Ajusta cuánto se desplaza la maqueta al mover una o ambas manos con los grips pulsados. No cambia el seguimiento físico de manos y cabeza. Los saltos de seguimiento conservan su límite de velocidad.");
            Add(strings, "Rotation speed · Touch", "Velocidad de giro · Touch");
            Add(strings, "Adjust tabletop rotation and tilt speed. 100% is the initial response; lower values move the view more slowly. Physical head tracking remains unchanged.", "Ajusta la velocidad de giro e inclinación de la maqueta. El 100% es la respuesta inicial; valores menores mueven la vista más despacio. No cambia el seguimiento físico de la cabeza.");
            Add(strings, "Zoom speed · Touch", "Velocidad de zoom · Touch");
            Add(strings, "Adjust how much the tabletop scale changes as you move both hands together or apart while holding the grips. 100% is the initial response. Scale limits remain active.", "Ajusta cuánto cambia la escala al acercar o separar ambas manos con los grips pulsados. El 100% es la respuesta inicial. Los límites de escala siguen activos.");
            Add(strings, "Recenter view", "Recentrar vista");
            Add(strings, "Use your current headset position and orientation as the reference. Sit in your playing position, then press A or the right trigger.", "Usa la posición y orientación actuales del visor como referencia. Siéntate en tu posición de juego y pulsa A o el gatillo derecho.");
            Add(strings, "Interface", "Interfaz");
            Add(strings, "Configure the game panel, indicators and flat menu screen. These settings affect the game interface. This configuration panel is positioned separately.", "Configura el panel del juego, los indicadores y los menús planos. Estos ajustes afectan a la interfaz del juego. Este panel de configuración tiene una posición independiente.");
            Add(strings, "Hands and indicators", "Manos e indicadores");
            Add(strings, "Control the visibility of hands, gesture help and scene indicators.", "Controla la visibilidad de las manos, la ayuda de gestos y los indicadores del escenario.");
            Add(strings, "Gesture help", "Ayuda de gestos");
            Add(strings, "Show servo-skull gesture hints near the upper-right corner: movement, rotation, tilt, zoom and selection. Hiding the hints keeps the pointer and all controls.", "Muestra pistas de gestos con servocráneos cerca de la esquina superior derecha: mover, girar, inclinar, zoom y seleccionar. Ocultar estas pistas mantiene el puntero y todos los controles.");
            Add(strings, "3D servo-skulls", "Servocráneos 3D");
            Add(strings, "Show a fully 3D servo-skull following each hand. Rotate the Touch controllers to inspect it. Hiding the models keeps aiming available and lets you compare their rendering cost.", "Muestra un servocráneo 3D siguiendo cada mano. Gira los Touch para verlo desde otros ángulos. Ocultar los modelos mantiene el apuntado y permite comparar su coste de renderizado.");
            Add(strings, "Unit indicators", "Indicadores de unidades");
            Add(strings, "Place unit indicators above their characters in the 3D scene. Off returns these indicators to the flat game panel.", "Coloca los indicadores de unidades sobre sus personajes en la escena 3D. Desactivado los devuelve al panel plano del juego.");
            Add(strings, "Marker size", "Tamaño de marcadores");
            Add(strings, "Limit the extra marker enlargement used to retain legibility as tabletop scale changes. 1.0 removes this extra enlargement; higher values allow larger markers.", "Limita la ampliación adicional de los marcadores al cambiar la escala de la maqueta. 1,0 elimina esta ampliación; los valores mayores permiten marcadores más grandes.");
            Add(strings, "Game panel", "Panel del juego");
            Add(strings, "The keyboard-and-mouse interface adapts its windows to the headset aspect ratio. Adjust size, aspect and position without restarting.", "La interfaz de teclado y ratón adapta sus ventanas a las proporciones del visor. Ajusta tamaño, proporción y posición sin reiniciar.");
            Add(strings, "Hide persistent HUD panels", "Ocultar paneles fijos del HUD");
            Add(strings, "Use the wheels in exploration and combat. Hides the usual party, action and menu bars while retaining dialogues, tutorials and opened windows. Turn off to restore the original panels or compare their cost.", "Usa las ruedas en exploración y combate. Oculta las barras habituales de grupo, acciones y menús, conservando diálogos, tutoriales y ventanas abiertas. Desactívalo para recuperar los paneles originales o comparar su coste.");
            Add(strings, "Service window text size", "Texto en ventanas de gestión");
            Add(strings, "Set text and button size in the enlarged inventory, map and other service windows. These windows stay centred and provide CLOSE below. Regular HUD placement is preserved when they close.", "Ajusta el tamaño de textos y botones en el inventario, mapa y demás ventanas ampliadas. Permanecen centradas e incluyen CERRAR al pie. Al cerrarlas se recupera la posición habitual del HUD.");
            Add(strings, "Text and button size", "Tamaño de textos y botones");
            Add(strings, "Enlarge the HUD content from 100% to 180% inside the same panel area. Reflows the keyboard-and-mouse layout instead of pushing its edges farther apart. Applies immediately; very large values can crowd fixed-size game windows. Does not lower interface resolution or move world/map markers.", "Amplía el contenido del HUD del 100% al 180% dentro del mismo panel, reajustando la distribución de teclado y ratón. Se aplica al instante; valores altos pueden amontonar ventanas de tamaño fijo. No reduce la resolución ni mueve marcadores del mundo o del mapa.");
            Add(strings, "Show game panel", "Mostrar panel del juego");
            Add(strings, "Show or hide the game interface inside the tabletop. This configuration panel remains available so you can turn it back on.", "Muestra u oculta la interfaz del juego dentro de la maqueta. Este panel de configuración sigue disponible para que puedas volver a activarla.");
            Add(strings, "Panel distance", "Distancia del panel");
            Add(strings, "Move the game panel nearer to or farther from your head, in virtual metres. Does not reposition this configuration panel.", "Acerca o aleja el panel del juego respecto a tu cabeza, en metros virtuales. No desplaza este panel de configuración.");
            Add(strings, "Whole panel size", "Tamaño del panel completo");
            Add(strings, "Enlarge the whole interface, including its outer edges. 100% uses the centred binocular area; maximum 180%. To make text and buttons larger while keeping these edges in place, use Text and button size instead. Also affects flat menus.", "Amplía toda la interfaz, incluidos sus bordes. El 100% usa el área binocular centrada; máximo 180%. Para ampliar textos y botones manteniendo los bordes, usa Tamaño de textos y botones. También afecta a los menús planos.");
            Add(strings, "HUD aspect ratio", "Proporción del HUD");
            Add(strings, "Headset adapts the interface to the binocular field of view. Manual values change width-to-height ratio without stretching text. Affects the tabletop HUD; flat menus keep the original window ratio.", "Visor adapta la interfaz al campo de visión binocular. Los valores manuales cambian la proporción sin estirar los textos. Afecta al HUD de la maqueta; los menús planos conservan la proporción de la ventana original.");
            Add(strings, "HUD horizontal position", "Posición horizontal del HUD");
            Add(strings, "Move the interface and flat menus by up to 65% of their width. Size is preserved. Extreme values may place buttons outside your view. Aiming follows the actual panel position.", "Desplaza la interfaz y los menús planos hasta un 65% de su anchura, sin cambiar el tamaño. Los extremos pueden dejar botones fuera de la vista. El apuntado sigue la posición real del panel.");
            Add(strings, "HUD vertical position", "Posición vertical del HUD");
            Add(strings, "Move the interface and flat menus by up to 65% of their height without shrinking them. Negative moves down; positive moves up. Extreme values may leave elements outside your view.", "Desplaza la interfaz y los menús planos hasta un 65% de su altura sin reducirlos. Negativo baja; positivo sube. Los extremos pueden dejar elementos fuera de la vista.");
            Add(strings, "Interface resolution", "Resolución de la interfaz");
            Add(strings, "Actual pixels for flat menus and the independent HUD. Does not change text size or eye resolution. Higher values may improve clarity and cost GPU time. Changing the window may briefly pause the image. Actual shows the applied size.", "Píxeles reales de los menús planos y el HUD independiente. No cambia el tamaño del texto ni la resolución de los ojos. Subirla puede mejorar la claridad y exige GPU. Cambiar la ventana puede pausar la imagen. Actual indica el tamaño aplicado.");
            Add(strings, "Full-resolution HUD", "HUD a resolución completa");
            Add(strings, "Render the PC panel and world unit indicators at a resolution independent of DLSS. Preserves masks, drawing order and aiming. Turn off to compare with the earlier path, whose clarity depends on each eye internal resolution.", "Dibuja el panel y los indicadores de unidades a una resolución independiente de DLSS. Conserva máscaras, orden de dibujo y apuntado. Desactívalo para comparar con el método anterior, cuya claridad depende de la resolución interna de cada ojo.");
            Add(strings, "Flat screen in menus", "Pantalla plana en menús");
            Add(strings, "Show the game on a flat screen when a full-screen menu suspends the tabletop. Touch pointing remains available in those menus.", "Muestra el juego en una pantalla plana cuando un menú a pantalla completa suspende la maqueta. El apuntado Touch sigue disponible en esos menús.");
            Add(strings, "Performance", "Rendimiento");
            Add(strings, "Mod options that reduce rendering work and record timings. Their effect depends on the scene and active effects. Compare while holding the same view.", "Opciones del mod para reducir el trabajo de renderizado y registrar tiempos. Su efecto depende de la escena y los efectos activos. Compara manteniendo la misma vista.");
            Add(strings, "Coalesce popup layout", "Agrupar ajustes de ventanas");
            Add(strings, "Avoid repeatedly updating every panel when a popup tries several consecutive positions. Keeps an update per popup. Turn off to compare with the previous path.", "Evita actualizar todos los paneles repetidamente cuando una ventana emergente prueba varias posiciones seguidas. Mantiene una actualización por ventana. Desactívalo para comparar con el método anterior.");
            Add(strings, "Stable HUD geometry", "Geometría estable del HUD");
            Add(strings, "Keep panel geometry fixed while moving your head or zooming the table. Reduces repeated Canvas work without lowering HUD resolution. Turn off for a live comparison; maps keep their registered projection.", "Mantiene fija la geometría del panel al mover la cabeza o hacer zoom. Reduce trabajo repetido de Canvas sin bajar la resolución del HUD. Desactívalo para comparar en caliente; los mapas conservan su proyección registrada.");
            Add(strings, "Separate unit indicators", "Separar indicadores");
            Add(strings, "Update each unit indicator separately. May reduce CPU work but increase draw calls and pointing cost. Off by default. Compare in the same combat view.", "Actualiza cada indicador de unidad por separado. Puede reducir el trabajo de CPU, pero aumentar las llamadas de dibujo y el coste del apuntado. Desactivado por defecto. Compara desde la misma vista de combate.");
            Add(strings, "Reduce monitor rendering", "Reducir dibujo del monitor");
            Add(strings, "Reuse one eye image for the monitor instead of drawing the world again. Can reduce work in VR. Both eyes continue to render separately.", "Reutiliza la imagen de un ojo para el monitor, evitando dibujar de nuevo el mundo. Puede reducir trabajo en VR. Ambos ojos siguen renderizándose por separado.");
            Add(strings, "Allow game FSR", "Permitir FSR del juego");
            Add(strings, "Allow the game FSR setting to affect both eyes. May reduce detail to save GPU time. Suspended while DLSS, DLAA or NVIDIA Diagnostic is active.", "Permite que el ajuste FSR del juego afecte a ambos ojos. Puede reducir detalle para ahorrar tiempo de GPU. Se suspende con DLSS, DLAA o Diagnóstico de NVIDIA activos.");
            Add(strings, "Cull objects outside view", "Recortar objetos no visibles");
            Add(strings, "Reduce drawing outside the visible headset area. A margin is preserved. Culling may be suspended when effects such as reflections need those regions.", "Reduce el dibujo fuera del área visible del visor, conservando un margen. El recorte puede suspenderse si efectos como los reflejos necesitan esas zonas.");
            Add(strings, "Cull instances outside view", "Recortar instancias no visibles");
            Add(strings, "Cull repeated objects drawn by the GPU outside the view. May reduce rendering work. Suspended when effects or viewing direction require the full image.", "Descarta objetos repetidos dibujados por la GPU fuera de la vista. Puede reducir el trabajo de renderizado. Se suspende si los efectos o la dirección de la mirada requieren la imagen completa.");
            Add(strings, "Synchronize visible effects", "Sincronizar efectos visibles");
            Add(strings, "Update which effects the VR view needs when monitor rendering is reduced. Considers both eyes rather than relying on a camera that is no longer drawn.", "Actualiza qué efectos necesita la vista VR cuando se reduce el dibujo del monitor. Tiene en cuenta ambos ojos en lugar de depender de una cámara que ya no se dibuja.");
            Add(strings, "Record diagnostics", "Registrar diagnósticos");
            Add(strings, "Start or pause timing collection and diagnostic file writing. Turn off to compare its overhead. Error messages remain available and existing files are preserved.", "Inicia o pausa la recogida de tiempos y la escritura de diagnósticos. Desactívalo para comparar su coste. Los errores siguen registrándose y se conservan los archivos existentes.");
            Add(strings, "Detailed profiling", "Medición detallada");
            Add(strings, "Add rendering, logic, camera and interface timings to identify spikes. Active only while diagnostics are recording. Adds measurement overhead.", "Añade tiempos de renderizado, lógica, cámara e interfaz para localizar picos. Sólo actúa mientras se registran diagnósticos. La medición añade algo de trabajo.");
            Add(strings, "New measurement", "Nueva medición");
            Add(strings, "Clear recent frame-rate and timing samples to begin a fresh comparison. Quality settings and previously saved diagnostic files are preserved.", "Borra las muestras recientes de FPS y tiempos para iniciar una nueva comparación. Conserva los ajustes de calidad y los archivos de diagnóstico ya guardados.");
            Add(strings, "Visual effects", "Efectos visuales");
            Add(strings, "Adjust grids, unit markers and character highlights throughout the VR scene, in exploration and combat. Preserves the game's information and rules.", "Ajusta cuadrículas, marcadores y resaltado de personajes en toda la escena VR, tanto en exploración como en combate. Conserva la información y las reglas del juego.");
            Add(strings, "Deployment aura", "Aura de despliegue");
            Add(strings, "Show the enveloping preparation glow while placing units before combat. Off removes this visual effect while preserving preparation rules, placement markers and other buffs. Can be changed during deployment.", "Muestra el brillo envolvente al colocar unidades antes del combate. Desactivado elimina ese efecto, conservando las reglas de preparación, los marcadores y otras mejoras. Se puede cambiar durante el despliegue.");
            Add(strings, "Grids and areas", "Cuadrículas y áreas");
            Add(strings, "Dim scene grids wherever the game displays them, preserving their boundaries, range and colours. Changes visual intensity without altering gameplay calculations.", "Atenúa las cuadrículas donde aparezcan, conservando sus límites, alcance y colores. Cambia la intensidad visual sin alterar los cálculos del juego.");
            Add(strings, "Markers and reticles", "Marcadores y retículas");
            Add(strings, "Dim native unit markers in exploration and combat. Keeps shapes and colours to distinguish targets, selected units and speakers. Does not remove attack effects.", "Atenúa los marcadores originales en exploración y combate. Mantiene formas y colores para distinguir objetivos, unidades seleccionadas e interlocutores. No elimina efectos de ataques.");
            Add(strings, "Character highlight", "Resaltado de personajes");
            Add(strings, "Original keeps the outline. Dimmed reduces its intensity. Base circle replaces it; No outline hides it. The last two modes help compare outline cost. Other tactical markers remain.", "Original conserva el contorno. Atenuado reduce su intensidad. Círculo en la base lo sustituye; Sin contorno lo oculta. Estos dos últimos permiten comparar su coste. Los demás marcadores tácticos permanecen.");
            Add(strings, "Outline intensity", "Intensidad del contorno");
            Add(strings, "Adjust outline intensity in Dimmed mode. Does not change the base circle or other highlight modes.", "Ajusta la intensidad del contorno en el modo Atenuado. No cambia el círculo en la base ni los demás modos de resaltado.");
            Add(strings, "Advanced", "Arranque y compatibilidad");
            Add(strings, "Startup and compatibility settings. Vertical image flips correct an upside-down image and last for this session only.", "Ajustes de arranque y compatibilidad. Las inversiones verticales corrigen una imagen boca abajo y sólo duran durante esta sesión.");
            Add(strings, "Start VR automatically", "Iniciar VR automáticamente");
            Add(strings, "Start OpenXR/VDXR with the game when the headset is available. Saved for future launches. Changing it does not stop the current VR session.", "Inicia OpenXR/VDXR con el juego cuando el visor esté disponible. Se guarda para futuros arranques. Cambiarlo no detiene la sesión VR actual.");
            Add(strings, "Flip eye images · this session", "Invertir ojos · esta sesión");
            Add(strings, "Flip both eye images vertically if they appear upside down. Does not swap left and right. Resets on the next launch.", "Invierte verticalmente las imágenes de ambos ojos si aparecen boca abajo. No intercambia izquierda y derecha. Se restablece en el próximo arranque.");
            Add(strings, "Flip flat screen · this session", "Invertir plano · esta sesión");
            Add(strings, "Flip the headset flat screen vertically if it appears upside down. Does not change the stereo tabletop. Resets on the next launch.", "Invierte verticalmente la pantalla plana del visor si aparece boca abajo. No cambia la maqueta estereoscópica. Se restablece en el próximo arranque.");

            Add(strings, "No antialiasing", "Sin suavizado de bordes");
            Add(strings, "NVIDIA diagnostic (TAA image)", "Diagnóstico NVIDIA (imagen TAA)");
            Add(strings, "On", "Activado");
            Add(strings, "Off", "Desactivado");
            Add(strings, "(change pending)", "(cambio pendiente)");
            Add(strings, "select DLSS to apply", "selecciona DLSS para aplicar");
            Add(strings, "Auto (NVIDIA selection)", "Auto (elección de NVIDIA)");
            Add(strings, "{0} units", "{0} unidades");
            Add(strings, "(adjusting selects Custom)", "(ajustar activa Personalizada)");
            Add(strings, "Use current head position", "Usar posición actual de la cabeza");
            Add(strings, "Allowed; suspended with NVIDIA", "Permitido; suspendido con NVIDIA");
            Add(strings, "Allowed", "Permitido");
            Add(strings, "Recording · this session", "Registrando · esta sesión");
            Add(strings, "Paused · this session", "En pausa · esta sesión");
            Add(strings, "Reset current samples", "Reiniciar muestras actuales");
            Add(strings, "Original", "Original");
            Add(strings, "Dimmed", "Atenuado");
            Add(strings, "Base circle", "Círculo en la base");
            Add(strings, "No outline", "Sin contorno");
            Add(strings, "Active image: {0}", "Imagen activa: {0}");
            Add(strings, "preparing", "preparando");
            Add(strings, "Output: {0} × {1} per eye", "Salida: {0} × {1} por ojo");
            Add(strings, "recenter", "recentrar");
            Add(strings, "reset samples", "reiniciar muestras");
            Add(strings, "Back to previous menu", "Volver al menú anterior");
            Add(strings, "Return to the previous menu. Changed settings are already applied; no confirmation is needed.", "Vuelve al menú anterior. Los cambios ya están aplicados; no hace falta confirmarlos.");
            Add(strings, "Return to the previous menu. Each setting description explains when saved changes apply.", "Vuelve al menú anterior. La descripción de cada ajuste explica cuándo se aplican los cambios guardados.");
            Add(strings, "Close panel", "Cerrar panel");
            Add(strings, "Close this panel and return to the game. Hold both triggers and both grips for 2 seconds to open or close it. Release all four before repeating.", "Cierra este panel y vuelve al juego. Mantén ambos gatillos y ambos grips durante 2 segundos para abrirlo o cerrarlo. Suelta los cuatro antes de repetir.");
            Add(strings, "open", "abrir");
            Add(strings, "close", "cerrar");
            Add(strings, "back", "volver");
            Add(strings, "apply", "aplicar");
            Add(strings, "no action", "sin acción");
            Add(strings, "next", "siguiente");
            Add(strings, "{0} settings", "{0} opciones");
            Add(strings, "Return to game", "Volver al juego");
            Add(strings, "Return to previous menu", "Volver al menú anterior");
            Add(strings, "Point at - / +, or use the right stick", "Apunta a - / +, o usa el stick derecho");
            Add(strings, "A / right trigger  ·  {0}", "A / gatillo derecho  ·  {0}");
            Add(strings, "Setting unavailable  ·  B: back", "Opción no disponible  ·  B: volver");
            Add(strings, "CLOSE  ·  point and release the right trigger, or press A", "CERRAR  ·  apunta y suelta el gatillo derecho, o pulsa A");
            Add(strings, "F1 / grips + triggers 2 s: close  |  Point/trigger: select  |  Stick: navigate  |  A: enter  |  B: back", "F1 / grips + gatillos 2 s: cerrar  |  Puntero/gatillo: elegir  |  Stick: navegar  |  A: entrar  |  B: volver");
            Add(strings, "< PREVIOUS", "< ANTERIOR");
            Add(strings, "NEXT >", "SIGUIENTE >");
            Add(strings, "F1 / 4 controls · 2 s", "F1 / 4 botones · 2 s");
            Add(strings, "DESCRIPTION", "DESCRIPCIÓN");
            Add(strings, "Status unavailable", "Estado no disponible");
            Add(strings, "Could not apply setting", "No se pudo aplicar el ajuste");
        }
    }
}
