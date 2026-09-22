using System.Collections.Generic;
namespace RTMaquetaXR
{
    internal static partial class ModLocalization
    {
        static partial void AddRelease71(Dictionary<string,string> d)
        {
            Add(d,"Right stick · view-relative","Stick derecho · relativo a la vista");
            Add(d,"Your view defines all four movement directions.","La vista define las cuatro direcciones de movimiento.");
            Add(d,"In On-foot exploration, the horizontal direction of your view when the stick leaves centre defines forward, backward and sideways movement. That direction stays fixed during the deflection; centre the stick before adopting a new heading. Camera follow and wrist movement do not redirect it. Passing the ray over HUD does not stop movement; clicking or opening a menu does. Combat uses native pointer orders.","En Exploración a pie, la dirección horizontal de la vista cuando el stick sale del centro define avance, retroceso y desplazamiento lateral. Esa dirección permanece fija mientras mantienes el stick; céntralo antes de adoptar un rumbo nuevo. Ni el seguimiento de cámara ni la muñeca lo desvían. Pasar el rayo sobre el HUD no detiene el movimiento; hacer clic o abrir un menú sí. El combate usa órdenes nativas con el puntero.");
            Add(d,"Tactical information","Información táctica");
            Add(d,"Hold Y during your turn","Mantén Y durante tu turno");
            Add(d,"Show original combat information only while requested.","Muestra la información original de combate sólo mientras la solicitas.");
            Add(d,"During your Ground combat turn, hold Y to reveal the original floating hit chances, health and attack information on the terrain. The game continues running. Releasing Y hides those elements and returns to the low-cost combat presentation. It is inactive during enemy turns.","Durante tu turno en Combates terrestres, mantén Y para mostrar sobre el terreno los porcentajes de impacto, vida e información de ataque originales. El juego continúa en marcha. Al soltar Y se ocultan y vuelve la presentación de combate de bajo coste. No actúa durante los turnos enemigos.");
            Add(d,"Hold · tactical information","Mantén · información táctica");
            Add(d,"Hold Y · tactical information","Mantén Y · información táctica");
        }
    }
}
