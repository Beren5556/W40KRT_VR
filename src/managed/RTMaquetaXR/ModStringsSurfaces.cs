using System.Collections.Generic;

namespace RTMaquetaXR
{
    internal static partial class ModLocalization
    {
        // Only text owned by the mod. Native character, ability and effect
        // names are never looked up here, even if they match an English key.
        static partial void AddSurfaces(Dictionary<string, string> d)
        {
            Add(d, "Click: {0}\nHold: {1}", "Clic: {0}\nMantén: {1}");
            Add(d, "HOLD 1s · CONFIRM", "MANTÉN 1s · ACEPTAR");
            Add(d, "POINT · INSPECT", "SEÑALA · CONSULTAR");
            Add(d, "Press A or Trigger", "Pulsa A o Gatillo");
            Add(d, "A / TRIGGER · USE", "A / GATILLO · USAR");
            Add(d, "STICK / CLICK · INSPECT", "STICK / CLIC · CONSULTAR");
            Add(d, "ACTIONS", "ACCIONES");
            Add(d, "COMBATANTS", "COMBATIENTES");
            Add(d, "PARTY", "GRUPO");
            Add(d, "BATTLE PORTRAITS", "COMBATIENTES");
            Add(d, "CHARACTER ACTIONS", "ACCIONES DEL PERSONAJE");
            Add(d, "COMMAND DECK", "PUESTO DE MANDO");
            Add(d, "Native action bar unavailable", "Barra de acciones no disponible");
            Add(d, "Select one character · click left stick for portraits", "Selecciona un personaje · clic en stick izq.: grupo");
            Add(d, "Select one character", "Selecciona un personaje");
            Add(d, "Native battle action context unavailable", "Acciones de combate no disponibles");
            Add(d, "No actions available here", "No hay acciones disponibles aquí");
            Add(d, "Party panel unavailable", "Panel del grupo no disponible");
            Add(d, "Party panel not visible", "Panel del grupo no visible");
            Add(d, "Party portraits are updating", "Actualizando retratos del grupo");
            Add(d, "No party portraits available", "No hay retratos del grupo disponibles");
            Add(d, "Battle portrait panel unavailable", "Panel de combatientes no disponible");
            Add(d, "Battle portrait panel not visible", "Panel de combatientes no visible");
            Add(d, "Battle portraits are updating", "Actualizando retratos de combate");
            Add(d, "No battle portraits available", "No hay retratos de combate disponibles");
            Add(d, "Cancel", "Cancelar");
            Add(d, "Information only", "Sólo información");
            Add(d, "Click: {0}\nRelease: {1}", "Clic: {0}\nSoltar: {1}");
            Add(d, "RELEASE · CLOSE", "SOLTAR · CERRAR");
            Add(d, "CENTRE · CANCEL", "CENTRO · CANCELAR");
            Add(d, "L-STICK CLICK · SWITCH", "CLIC STICK IZQ. · CAMBIAR");
            Add(d, " (unavailable)", " (no disponible)");
            Add(d, "Ability", "Habilidad");
            Add(d, "Character", "Personaje");
            Add(d, "Inventory", "Inventario");
            Add(d, "Augmentations", "Mejoras");
            Add(d, "Journal", "Diario");
            Add(d, "Map", "Mapa");
            Add(d, "Encyclopedia", "Enciclopedia");
            Add(d, "Voidship", "Nave del vacío");
            Add(d, "Colonies", "Colonias");
            Add(d, "Cargo", "Carga");
            Add(d, "Formation", "Formación");
            Add(d, "Game menu", "Menú del juego");
            Add(d, "Pause", "Pausa");
            Add(d, "Co-op roles", "Roles cooperativos");
            Add(d, "End turn", "Terminar turno");
            Add(d, "Start battle", "Comenzar batalla");
            Add(d, "Hold right grip and trigger for 0.15 s. Use the right stick for 1 s or point with the LEFT Touch and click its trigger. This is the native right options panel, including Space combat and navigation maps. During ground deployment it also offers Start battle when the game permits it. Unavailable options are grey and cannot execute. B or releasing the opening controls closes it. No pages: all current options are on the wheel.", "Mantén grip y gatillo derechos 0,15 s. Mantén el stick derecho en una opción 1 s o señala con el Touch IZQUIERDO y pulsa su gatillo. Es el panel derecho original, también en Combate espacial y mapas. Durante el despliegue terrestre también ofrece Comenzar batalla cuando el juego lo permite. Las opciones no disponibles están grises y no se ejecutan. B o soltar la combinación lo cierra. Sin páginas: la rueda incluye todas las opciones actuales.");
            Add(d, "DEAD", "MUERTO");
            Add(d, "CRIPPLED", "INCAPACITADO");
            Add(d, "SELECTED", "SELECCIONADO");
            Add(d, "CURRENT TURN", "TURNO ACTUAL");
            Add(d, "ENEMY", "ENEMIGO");
            Add(d, "NEUTRAL", "NEUTRAL");
            Add(d, "ALLY", "ALIADO");
            Add(d, "SKIPS TURN", "PIERDE EL TURNO");
            Add(d, "CONTROL LOST", "SIN CONTROL");
            Add(d, "CANNOT ACT", "NO PUEDE ACTUAR");
            Add(d, "HP", "PV");
            Add(d, "Initiative", "Iniciativa");
            Add(d, "Squad", "Escuadra");
            Add(d, "Hit", "Impacto");
            Add(d, "Damage", "Daño");
            Add(d, "Dodge", "Esquiva");
            Add(d, "Parry", "Parada");
            Add(d, "Cover", "Cobertura");
            Add(d, "Effects:", "Efectos:");
            Add(d, "None", "Ninguno");
            Add(d, "CLOSE", "CERRAR");
            Add(d, "MOD VR By Beren5556", "MOD VR por Beren5556");
            Add(d, "MOD VR · By Beren5556", "MOD VR · por Beren5556");
            Add(d, "READY  /  PRESS A OR RIGHT TRIGGER TO CONTINUE", "LISTO  /  PULSA A O EL GATILLO DERECHO PARA CONTINUAR");
            Add(d, "LOADING  {0}%", "CARGANDO  {0}%");
            Add(d, "LOADING YOUR GAME", "CARGANDO LA PARTIDA");
            Add(d, "Modded with love for the Warhammer 40K universe,\nand with gratitude to Owlcat Games for bringing its wonders to life.\nI hope you enjoy this new perspective on the game.",
                "Creado con amor por el universo de Warhammer 40K,\ny con gratitud a Owlcat Games por dar vida a sus maravillas.\nEspero que disfrutéis de esta nueva perspectiva del juego.");
            Add(d, "Diagnostics paused · read FPS in Virtual Desktop", "Diagnóstico en pausa · consulta los FPS en Virtual Desktop");
            Add(d, "Waiting for a stable stereo sample", "Esperando una muestra estéreo estable");
            Add(d, "n/a", "n/d");
            Add(d, " ms (includes waits)", " ms (incluye esperas)");
            Add(d, "Headset · ", "Visor · ");
            Add(d, "Manual · ", "Manual · ");
            Add(d, " · pending", " · pendiente");
            Add(d, " · actual ", " · real ");
            Add(d, "Earlier path · rendered inside each eye", "Método anterior · dibujado dentro de cada ojo");
            Add(d, "Capture suspended: {0}", "Captura suspendida: {0}");
            Add(d, "Preparing independent HUD", "Preparando HUD independiente");
            Add(d, "HUD {0} × {1} · independent of DLSS", "HUD {0} × {1} · independiente de DLSS");
            Add(d, "Draw distance: {0} units", "Distancia de dibujado: {0} unidades");
        }
    }
}
