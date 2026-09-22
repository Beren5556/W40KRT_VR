using System.Collections.Generic;

namespace RTMaquetaXR
{
    internal static partial class ModLocalization
    {
        static partial void AddSpace(Dictionary<string,string> d)
        {
            Add(d,"Space","Espacio");
            Add(d,"Custom space background","Fondo espacial personalizado");
            Add(d,"Show the IC 2631 nebula below the Space combat board. Image: ESO, adapted under CC BY 4.0. Off restores the original game background. Ship lighting, board controls and grids remain unchanged.","Muestra la nebulosa IC 2631 bajo el tablero de Combate espacial. Imagen: ESO, adaptada bajo CC BY 4.0. Desactivado restaura el fondo original del juego. Conserva la iluminación de las naves, los controles y las cuadrículas.");
            Add(d,"Native space distance · terrain clipping disabled","Distancia espacial nativa · recorte terrestre desactivado");
            Add(d,"Galactic and star-system maps have independent panels. Space battles use the 3D tabletop and native tactical controls.","Mapa estelar y Mapa galáctico tienen paneles independientes. Combate espacial usa la maqueta 3D y los controles tácticos del juego.");
            Add(d,"Galactic map panel","Panel del mapa galáctico");
            Add(d,"Star-system map panel","Panel del Mapa galáctico");
            Add(d,"Adjust this map panel only. Native pan, zoom, routes and travel controls remain inside it. HUD presets also save both map layouts.","Ajusta sólo el panel de este mapa. Dentro se conservan los controles nativos de desplazamiento, zoom, rutas y viajes. Los presets de HUD también guardan ambos mapas.");
            Add(d,"Frame active ship","Encuadrar nave activa");
            Add(d,"Close the overlay and release the controls to frame the active ship. This does not select it or issue an order. Available during a space battle.","Cierra el overlay y suelta los controles para centrar la nave activa. No la selecciona ni emite órdenes. Disponible durante una batalla espacial.");
            Add(d,"Space battle","Combate espacial");
            Add(d,"Map panel size","Tamaño del panel del mapa");
            Add(d,"Change the visible size of this entire map panel. This does not zoom the map content or change dialogue and management windows.","Cambia el tamaño visible del panel completo de este mapa. No modifica el zoom de su contenido ni las ventanas de diálogo o gestión.");
            Add(d,"Map panel distance","Distancia del panel del mapa");
            Add(d,"Move this map panel nearer or farther. The pointer uses the displayed surface; the other map has its own distance.","Acerca o aleja el panel de este mapa. El puntero usa la superficie que ves; el otro mapa tiene su propia distancia.");
            Add(d,"Map panel proportions","Proporciones del panel del mapa");
            Add(d,"Original preserves the game's window ratio. Manual values change the panel width-to-height ratio. They do not alter the native map or its travel rules.","Original conserva la proporción de la ventana del juego. Los valores manuales cambian la relación entre anchura y altura del panel. No alteran el mapa ni las reglas de viaje.");
            Add(d,"Map panel horizontal position","Posición horizontal del mapa");
            Add(d,"Move this map panel left or right. Extreme offsets may put its edges outside the view. The pointer follows the new position.","Desplaza este panel a izquierda o derecha. Los valores extremos pueden dejar bordes fuera de la vista. El puntero sigue la nueva posición.");
            Add(d,"Map panel vertical position","Posición vertical del mapa");
            Add(d,"Move this map panel up or down. Negative lowers it; positive raises it. Management and dialogue placement are separate.","Desplaza este panel arriba o abajo. Los valores negativos lo bajan y los positivos lo suben. La posición de gestión y diálogos es independiente.");
            Add(d,"SPACE WEAPONS","ARMAS ESPACIALES");
            Add(d,"SHIP POSTS","PUESTOS DE LA NAVE");
            Add(d,"Native ship actions unavailable","Acciones nativas de la nave no disponibles");
            Add(d,"Select the unit whose turn it is","Selecciona la unidad que tiene el turno");
            Add(d,"No ship-post actions for this unit","Esta unidad no tiene acciones de puestos");
        }
    }
}
