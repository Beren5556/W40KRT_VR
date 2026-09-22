using System.Collections.Generic;
namespace RTMaquetaXR
{
    internal static partial class ModLocalization
    {
        static partial void AddRelease73(Dictionary<string,string> d)
        {
            Add(d,"NEARBY INTERACTIONS","INTERACCIONES CERCANAS");
            Add(d,"Interaction","Interacción");
            Add(d,"Travel to this destination.","Viajar a este destino.");
            Add(d,"Examine this point of interest.","Examinar este punto de interés.");
            Add(d,"Use this nearby interaction.","Usar esta interacción cercana.");
            Add(d,"[R:XY] / [LT] · 1 s   [B] · CLOSE","[R:XY] / [LT] · 1 s   [B] · CERRAR");
            Add(d,"Right stick · selected leader","Stick derecho · líder seleccionado");
            Add(d,"Movement keeps the selected character's facing.","El movimiento conserva la orientación del personaje seleccionado.");
            Add(d,"In On-foot exploration, forward, backward and sideways movement use the selected native leader's facing when the RIGHT stick leaves centre. That heading remains fixed during the deflection; centre the stick before capturing the character's current facing again. Camera follow and Touch pointing never redirect movement. Combat keeps its native pointer orders.",
                "En Exploración a pie, el avance, retroceso y desplazamiento lateral usan la orientación del líder nativo seleccionado cuando el stick DERECHO sale del centro. Ese rumbo permanece fijo durante la inclinación; centra el stick para volver a capturar la orientación actual del personaje. Ni el seguimiento de cámara ni el apuntado Touch redirigen el movimiento. El combate conserva sus órdenes nativas con el puntero.");
            Add(d,"A · nearby interaction","A · interacción cercana");
            Add(d,"Nearby interaction / wheel","Interacción cercana / rueda");
            Add(d,"LEFT stick: map · RIGHT stick 1 s: group · Y: highlight · A: interact","Stick IZQUIERDO: mapa · Stick DERECHO 1 s: grupo · Y: resaltar · A: interactuar");
            Add(d,"One available action executes directly; several open a wheel.","Una acción disponible se ejecuta directamente; varias abren una rueda.");
            Add(d,"In On-foot exploration, A activates a discovered interaction near the selected group. If several interactions are nearby, choose an available one with the RIGHT stick held for 1 s, or point with the LEFT Touch and hold its trigger for 1 s. Unavailable choices remain grey. B closes the wheel. Hold Y to highlight loot and discovered interaction icons. Doors, chests and other direct world actions keep their RIGHT-trigger interaction.",
                "En Exploración a pie, A activa una interacción descubierta cercana al grupo seleccionado. Si hay varias interacciones cerca, elige una disponible manteniendo el stick DERECHO 1 s, o señala con el Touch IZQUIERDO y mantén su gatillo 1 s. Las opciones no disponibles permanecen en gris. B cierra la rueda. Mantén Y para resaltar botín e iconos de interacción descubiertos. Puertas, cofres y otras acciones directas del mundo conservan su interacción con el gatillo DERECHO.");
        }
    }
}
